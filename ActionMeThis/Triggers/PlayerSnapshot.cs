using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.Enums;

namespace ActionMeThis.Triggers;

/// <summary>
/// One reading of the local player's state.
///
/// Handed to the UI, which draws on the render thread, so it is immutable once built -
/// the watcher swaps a whole new instance in rather than editing this one.
/// </summary>
public sealed class PlayerSnapshot(
    IReadOnlySet<PlayerTrigger> flags,
    IReadOnlyList<NearbyPlayer> nearby,
    bool hasPlayer)
{
    public static readonly PlayerSnapshot Empty = new(new HashSet<PlayerTrigger>(), [], false);

    /// <summary>The simple on/off states that held at sample time.</summary>
    public IReadOnlySet<PlayerTrigger> Flags { get; } = flags;

    /// <summary>Other player characters in range, nearest first. Never includes you.</summary>
    public IReadOnlyList<NearbyPlayer> Nearby { get; } = nearby;

    /// <summary>False on the title screen and during zone changes.</summary>
    public bool HasPlayer { get; } = hasPlayer;
}

/// <summary>Another player character and how far away they were.</summary>
public readonly record struct NearbyPlayer(string Name, string World, float Distance, StatusFlags Status)
{
    public bool IsFriend => Status.HasFlag(StatusFlags.Friend);

    public bool IsPartyMember => Status.HasFlag(StatusFlags.PartyMember);

    public bool IsAllianceMember => Status.HasFlag(StatusFlags.AllianceMember);

    /// <summary>
    /// Whether this player answers to a configured entry, which is either a bare name or
    /// "Name@World". Names are not unique across worlds, so the qualified form exists for
    /// when that matters.
    /// </summary>
    public bool Matches(string entry)
    {
        var at = entry.IndexOf('@');
        if (at < 0)
            return string.Equals(Name, entry.Trim(), System.StringComparison.OrdinalIgnoreCase);

        var name = entry[..at].Trim();
        var world = entry[(at + 1)..].Trim();

        return string.Equals(Name, name, System.StringComparison.OrdinalIgnoreCase)
            && string.Equals(World, world, System.StringComparison.OrdinalIgnoreCase);
    }

    public string Describe()
    {
        var tags = new List<string>();
        if (IsFriend)
            tags.Add("friend");
        if (IsPartyMember)
            tags.Add("party");
        else if (IsAllianceMember)
            tags.Add("alliance");

        var suffix = tags.Count == 0 ? string.Empty : $" ({string.Join(", ", tags)})";
        return $"{Name}{(World.Length > 0 ? "@" + World : string.Empty)} - {Distance:F1}y{suffix}";
    }
}
