using System;
using System.Collections.Generic;

namespace ActionMeThis.Triggers;

/// <summary>
/// A piece of local player state a rule can react to.
///
/// Values are serialised by name into the config, so existing members must not be
/// renamed or removed once shipped. Appending is fine.
/// </summary>
public enum PlayerTrigger
{
    WeaponDrawn,
    OffhandDrawn,
    Sitting,
    SittingOnGround,
    SittingInChair,
    Mounted,
    RidingPillion,
    Flying,
    Swimming,
    Diving,
    InCombat,
    Casting,
    Crafting,
    Gathering,
    Fishing,
    Performing,
    Carrying,
    Emoting,
    Jumping,
    Stealthed,
    Dead,
    InDuty,
    InCutscene,
    InGpose,
    InPvP,
    Transformed,
    UsingFashionAccessory,
    RolePlaying,

    // Proximity states. These read the radius, count and name list off the condition
    // rather than being a plain on/off flag - see RuleCondition.
    NearPlayer,
    NearFriend,
    NearPartyMember,
    NearNamedPlayer,
}

public static class PlayerTriggers
{
    public static readonly IReadOnlyList<PlayerTrigger> All =
        (PlayerTrigger[])Enum.GetValues(typeof(PlayerTrigger));

    public static string Label(this PlayerTrigger trigger) => trigger switch
    {
        PlayerTrigger.WeaponDrawn           => "Weapon drawn",
        PlayerTrigger.OffhandDrawn          => "Offhand drawn",
        PlayerTrigger.Sitting               => "Sitting (any)",
        PlayerTrigger.SittingOnGround       => "Sitting on the ground",
        PlayerTrigger.SittingInChair        => "Sitting on furniture",
        PlayerTrigger.Mounted               => "Mounted",
        PlayerTrigger.RidingPillion         => "Riding pillion",
        PlayerTrigger.Flying                => "Flying",
        PlayerTrigger.Swimming              => "Swimming",
        PlayerTrigger.Diving                => "Diving",
        PlayerTrigger.InCombat              => "In combat",
        PlayerTrigger.Casting               => "Casting",
        PlayerTrigger.Crafting              => "Crafting",
        PlayerTrigger.Gathering             => "Gathering",
        PlayerTrigger.Fishing               => "Fishing",
        PlayerTrigger.Performing            => "Performing",
        PlayerTrigger.Carrying              => "Carrying an object",
        PlayerTrigger.Emoting               => "Emoting",
        PlayerTrigger.Jumping               => "Jumping",
        PlayerTrigger.Stealthed             => "Stealthed",
        PlayerTrigger.Dead                  => "Dead",
        PlayerTrigger.InDuty                => "In a duty",
        PlayerTrigger.InCutscene            => "In a cutscene",
        PlayerTrigger.InGpose               => "In group pose",
        PlayerTrigger.InPvP                 => "In PvP",
        PlayerTrigger.Transformed           => "Transformed",
        PlayerTrigger.UsingFashionAccessory => "Using a fashion accessory",
        PlayerTrigger.RolePlaying           => "Role playing",
        PlayerTrigger.NearPlayer            => "Near another player",
        PlayerTrigger.NearFriend            => "Near a friend",
        PlayerTrigger.NearPartyMember       => "Near a party member",
        PlayerTrigger.NearNamedPlayer       => "Near a specific player",
        _                                   => trigger.ToString(),
    };

    /// <summary>
    /// Whether the trigger is a proximity check, which needs a radius and a count from
    /// the condition rather than being a simple on/off state.
    /// </summary>
    public static bool IsProximity(this PlayerTrigger trigger) => trigger
        is PlayerTrigger.NearPlayer
        or PlayerTrigger.NearFriend
        or PlayerTrigger.NearPartyMember
        or PlayerTrigger.NearNamedPlayer;

    public static string Description(this PlayerTrigger trigger) => trigger switch
    {
        PlayerTrigger.WeaponDrawn     => "Active while your weapon is unsheathed. Sheathing ends it.",
        PlayerTrigger.OffhandDrawn    => "Active while your offhand is drawn.",
        PlayerTrigger.Sitting         => "Either kind of sitting: /sit on the ground or on furniture.",
        PlayerTrigger.SittingOnGround => "/sit with no chair in reach, and ground-sit emote loops.",
        PlayerTrigger.SittingInChair  => "Sitting on a chair, bed, or other furniture.",
        PlayerTrigger.Flying          => "Airborne on a flying mount.",
        PlayerTrigger.Diving          => "Underwater. Swimming stays active while diving.",
        PlayerTrigger.Casting         => "Active for the duration of a cast bar.",
        PlayerTrigger.Emoting         => "Playing a non-looping emote.",
        PlayerTrigger.Transformed     => "Changed shape by a fantasia, a duty effect, or an item.",
        PlayerTrigger.NearPlayer      => "Any other player character within the radius. Yourself does not count.",
        PlayerTrigger.NearFriend      => "A player on your friend list within the radius.",
        PlayerTrigger.NearPartyMember => "A player in your party within the radius.",
        PlayerTrigger.NearNamedPlayer => "One of the players you list below, within the radius. "
                                       + "Write Name@World to pin an entry to one world.",
        _                             => string.Empty,
    };
}
