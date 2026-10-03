using System;

/// <summary>
/// Tags a status carries, matched against status immunities. Assets serialize the value, so new tags are only
/// ever appended.
/// </summary>
[Flags]
public enum StatusTags
{
    None = 0,
    Stun = 1 << 0,
    Root = 1 << 1,
    Silence = 1 << 2,
    Slow = 1 << 3,
    Burn = 1 << 4,
    Chill = 1 << 5,
    Poison = 1 << 6,
    Bleed = 1 << 7,
}

/// <summary>
/// What a status stops its unit from doing. Stun: no movement, auto attacks or casts. Root: no movement. Silence:
/// no casts.
/// </summary>
[Flags]
public enum StatusControls
{
    None = 0,
    Stun = 1 << 0,
    Root = 1 << 1,
    Silence = 1 << 2,
}

/// <summary>
/// Whether a status helps (lands on the source's own faction) or harms (lands on the other faction).
/// </summary>
public enum StatusKind : byte
{
    Buff = 0,
    Debuff = 1,
}

/// <summary>
/// What applying a status that is already active does.
/// </summary>
public enum StatusStacking : byte
{
    /// <summary>One instance; reapplying resets its duration.</summary>
    Refresh = 0,

    /// <summary>One instance with a stack count up to the maximum; each application adds a stack and resets the duration.</summary>
    AddStack = 1,

    /// <summary>Each application is its own instance with its own timer, up to the maximum.</summary>
    Independent = 2,
}

/// <summary>
/// What a status does every tick interval.
/// </summary>
public enum StatusPeriodic : byte
{
    None = 0,
    Damage = 1,
    Heal = 2,
}

/// <summary>
/// The result of trying to apply a status.
/// </summary>
public enum StatusApplyOutcome : byte
{
    Landed,
    Refreshed,
    Stacked,
    Immune,
    WrongFaction,
    TargetDead,
    NoAuthority,
    Invalid,
}
