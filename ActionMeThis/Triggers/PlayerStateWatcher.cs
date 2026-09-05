using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;

using CsCharacter = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;

namespace ActionMeThis.Triggers;

/// <summary>
/// Samples the local player every frame and publishes a <see cref="PlayerSnapshot"/>.
///
/// Simple states are cheap and read every frame. Scanning the object table for nearby
/// players is not, so that runs on its own interval and the previous result is carried
/// forward in between.
///
/// Flapping is not smoothed here - a rule combines several conditions, so it is the rule
/// as a whole that gets debounced, in RuleEngine.
/// </summary>
public sealed class PlayerStateWatcher : IDisposable
{
    /// <summary>How often to rescan the object table for nearby players.</summary>
    private const double ProximityIntervalMilliseconds = 200;

    /// <summary>Players further away than this are not tracked at all.</summary>
    private const float MaxTrackedDistance = 100f;

    private readonly IFramework framework;
    private readonly IObjectTable objects;
    private readonly ICondition condition;
    private readonly IClientState clientState;

    // Framework thread only.
    private readonly HashSet<PlayerTrigger> flags = [];
    private readonly List<NearbyPlayer> nearby = [];

    /// <summary>
    /// World row id -> name. Resolving a world means an Excel lookup, and a crowded zone
    /// would repeat it for every player on every scan, so hold onto the answers.
    /// </summary>
    private readonly Dictionary<uint, string> worldNames = [];

    private double proximityDue;
    private bool nearbyDirty = true;

    public PlayerStateWatcher(
        IFramework framework, IObjectTable objects, ICondition condition, IClientState clientState)
    {
        this.framework = framework;
        this.objects = objects;
        this.condition = condition;
        this.clientState = clientState;

        this.framework.Update += OnUpdate;
    }

    /// <summary>The most recent reading. Replaced wholesale, never edited in place.</summary>
    public PlayerSnapshot Current { get; private set; } = PlayerSnapshot.Empty;

    /// <summary>Raised every frame with the current snapshot, after sampling.</summary>
    public event Action<PlayerSnapshot>? Sampled;

    public void Dispose() => framework.Update -= OnUpdate;

    private void OnUpdate(IFramework fw)
    {
        var player = objects.LocalPlayer;

        // No local player means a loading screen, a zone change, or the title screen.
        // Hold the last known state instead of reporting everything as inactive: clearing
        // it would revert every applied rule on each loading screen and re-apply it on
        // arrival, costing a pair of redraws every time you move between zones.
        if (player == null)
        {
            Sampled?.Invoke(Current);
            return;
        }

        var flagsChanged = SampleFlags(player);

        proximityDue -= fw.UpdateDelta.TotalMilliseconds;
        if (proximityDue <= 0)
        {
            proximityDue = ProximityIntervalMilliseconds;
            SampleNearby(player);
        }

        if (flagsChanged || nearbyDirty || !Current.HasPlayer)
        {
            nearbyDirty = false;
            Current = new PlayerSnapshot(new HashSet<PlayerTrigger>(flags), [.. nearby], true);
        }

        Sampled?.Invoke(Current);
    }

    /// <summary>Returns whether the flag set differs from the published one.</summary>
    private bool SampleFlags(IPlayerCharacter player)
    {
        flags.Clear();

        var status = player.StatusFlags;
        Set(PlayerTrigger.WeaponDrawn, status.HasFlag(StatusFlags.WeaponOut));
        Set(PlayerTrigger.OffhandDrawn, status.HasFlag(StatusFlags.OffhandOut));
        Set(PlayerTrigger.InCombat, status.HasFlag(StatusFlags.InCombat));
        Set(PlayerTrigger.Casting, status.HasFlag(StatusFlags.IsCasting));

        // Sitting, mounts and the other posture states are not surfaced by Dalamud's
        // object wrapper, so read the character mode straight off the game struct.
        var mode = ReadMode(player.Address);
        var onGround = mode == CharacterModes.EmoteLoop;
        var onFurniture = mode == CharacterModes.InPositionLoop;

        Set(PlayerTrigger.SittingOnGround, onGround);
        Set(PlayerTrigger.SittingInChair, onFurniture);
        Set(PlayerTrigger.Sitting, onGround || onFurniture);
        Set(PlayerTrigger.Mounted, mode == CharacterModes.Mounted);
        Set(PlayerTrigger.RidingPillion, mode == CharacterModes.RidingPillion);
        Set(PlayerTrigger.Crafting, mode == CharacterModes.Crafting);
        Set(PlayerTrigger.Gathering, mode == CharacterModes.Gathering);
        Set(PlayerTrigger.Performing, mode == CharacterModes.Performance);
        Set(PlayerTrigger.Carrying, mode == CharacterModes.Carrying);
        Set(PlayerTrigger.Dead, mode == CharacterModes.Dead);

        Set(PlayerTrigger.Flying, condition[ConditionFlag.InFlight]);
        Set(PlayerTrigger.Swimming, condition[ConditionFlag.Swimming]);
        Set(PlayerTrigger.Diving, condition[ConditionFlag.Diving]);
        Set(PlayerTrigger.Fishing, condition[ConditionFlag.Fishing]);
        Set(PlayerTrigger.Emoting, condition[ConditionFlag.Emoting]);
        Set(PlayerTrigger.Jumping, condition[ConditionFlag.Jumping] || condition[ConditionFlag.Jumping61]);
        Set(PlayerTrigger.Stealthed, condition[ConditionFlag.Stealthed]);
        Set(PlayerTrigger.InDuty, condition[ConditionFlag.BoundByDuty]);
        Set(PlayerTrigger.Transformed, condition[ConditionFlag.Transformed]);
        Set(PlayerTrigger.UsingFashionAccessory, condition[ConditionFlag.UsingFashionAccessory]);
        Set(PlayerTrigger.RolePlaying, condition[ConditionFlag.RolePlaying]);
        Set(PlayerTrigger.InCutscene,
            condition[ConditionFlag.OccupiedInCutSceneEvent] || condition[ConditionFlag.WatchingCutscene]);

        Set(PlayerTrigger.InGpose, clientState.IsGPosing);
        Set(PlayerTrigger.InPvP, clientState.IsPvP);

        return !flags.SetEquals(Current.Flags);
    }

    private void SampleNearby(IPlayerCharacter player)
    {
        nearby.Clear();

        var origin = player.Position;
        var self = player.GameObjectId;

        // PlayerObjects walks slots 0-199 and includes you, so skip yourself by id.
        foreach (var other in objects.PlayerObjects)
        {
            if (other.GameObjectId == self)
                continue;

            var distance = Vector3.Distance(origin, other.Position);
            if (distance > MaxTrackedDistance)
                continue;

            var world = other is IPlayerCharacter pc ? WorldName(pc) : string.Empty;

            nearby.Add(new NearbyPlayer(other.Name.TextValue, world, distance, other.StatusFlags));
        }

        nearby.Sort(static (a, b) => a.Distance.CompareTo(b.Distance));

        // Distances move constantly, so a rescan always counts as a change. Conditions
        // compare against a radius, which keeps that from mattering downstream.
        nearbyDirty = true;
    }

    private string WorldName(IPlayerCharacter player)
    {
        if (!player.HomeWorld.IsValid)
            return string.Empty;

        var rowId = player.HomeWorld.RowId;
        if (worldNames.TryGetValue(rowId, out var cached))
            return cached;

        var name = player.HomeWorld.Value.Name.ToString();
        worldNames[rowId] = name;
        return name;
    }

    private static unsafe CharacterModes ReadMode(nint address)
        => address == nint.Zero ? CharacterModes.None : ((CsCharacter*)address)->Mode;

    private void Set(PlayerTrigger trigger, bool value)
    {
        if (value)
            flags.Add(trigger);
    }
}
