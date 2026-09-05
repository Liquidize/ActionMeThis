using System;
using System.Collections.Generic;
using System.Linq;
using ActionMeThis.Triggers;

namespace ActionMeThis.Rules;

/// <summary>
/// One thing that has to be true for a rule to fire. A rule holds several of these and
/// requires all of them, so "weapon drawn AND near a friend" is two conditions.
/// </summary>
[Serializable]
public class RuleCondition
{
    public PlayerTrigger Trigger { get; set; } = PlayerTrigger.WeaponDrawn;

    /// <summary>Require the trigger to be absent instead of present.</summary>
    public bool Invert { get; set; }

    /// <summary>Proximity only: how far away, in yalms, still counts as near.</summary>
    public float Radius { get; set; } = 10f;

    /// <summary>Proximity only: how many matching players are needed.</summary>
    public int MinimumCount { get; set; } = 1;

    /// <summary>
    /// <see cref="PlayerTrigger.NearNamedPlayer"/> only: who to look for. Entries are a
    /// bare character name, or "Name@World" to pin one to a world.
    /// </summary>
    public List<string> Names { get; set; } = [];

    public bool Matches(PlayerSnapshot snapshot)
    {
        var value = Trigger.IsProximity()
            ? CountNearby(snapshot) >= Math.Max(1, MinimumCount)
            : snapshot.Flags.Contains(Trigger);

        return value != Invert;
    }

    /// <summary>
    /// How many nearby players satisfy this condition right now. Written as plain loops
    /// rather than LINQ because rules are evaluated on every frame.
    /// </summary>
    public int CountNearby(PlayerSnapshot snapshot)
    {
        if (!Trigger.IsProximity())
            return 0;

        var count = 0;
        foreach (var player in snapshot.Nearby)
        {
            if (player.Distance <= Radius && MatchesKind(player))
                count++;
        }

        return count;
    }

    public string Describe()
    {
        var subject = Trigger.Label().ToLowerInvariant();

        if (!Trigger.IsProximity())
            return Invert ? $"not {subject}" : subject;

        var count = MinimumCount > 1 ? $"{MinimumCount}+ " : string.Empty;
        var who = Trigger switch
        {
            PlayerTrigger.NearFriend      => "friends",
            PlayerTrigger.NearPartyMember => "party members",
            PlayerTrigger.NearNamedPlayer => Names.Count == 0 ? "(nobody listed)" : string.Join("/", Names),
            _                             => "players",
        };

        var text = $"{count}{who} within {Radius:0.#}y";
        return Invert ? $"no {text}" : $"near {text}";
    }

    private bool MatchesKind(NearbyPlayer player) => Trigger switch
    {
        PlayerTrigger.NearPlayer      => true,
        PlayerTrigger.NearFriend      => player.IsFriend,
        PlayerTrigger.NearPartyMember => player.IsPartyMember,
        PlayerTrigger.NearNamedPlayer => MatchesAnyName(player),
        _                             => false,
    };

    private bool MatchesAnyName(NearbyPlayer player)
    {
        foreach (var name in Names)
        {
            if (player.Matches(name))
                return true;
        }

        return false;
    }

    public RuleCondition Clone() => new()
    {
        Trigger = Trigger,
        Invert = Invert,
        Radius = Radius,
        MinimumCount = MinimumCount,
        Names = [.. Names],
    };
}
