using System;

/// <summary>
/// The kind of a hit. Assets serialize the value, so new types are only ever appended.
/// </summary>
public enum DamageType : byte
{
    Physical = 0,
    Fire = 1,
    Frost = 2,
    Lightning = 3,
    Poison = 4,
    True = 5,
}

/// <summary>
/// A set of <see cref="DamageType"/>s, one bit per type.
/// </summary>
[Flags]
public enum DamageTypeMask
{
    None = 0,
    Physical = 1 << (int)DamageType.Physical,
    Fire = 1 << (int)DamageType.Fire,
    Frost = 1 << (int)DamageType.Frost,
    Lightning = 1 << (int)DamageType.Lightning,
    Poison = 1 << (int)DamageType.Poison,
    True = 1 << (int)DamageType.True,
    All = Physical | Fire | Frost | Lightning | Poison | True,
}

/// <summary>
/// What happened to a resolved hit, for popups and replication.
/// </summary>
[Flags]
public enum DamageFlags : byte
{
    None = 0,
    Immune = 1 << 0,
    Resisted = 1 << 1,
    Weakness = 1 << 2,
    Periodic = 1 << 3,
}

/// <summary>
/// Which side a unit fights on. Hostile effects only land across factions, helpful ones only within one.
/// <see cref="None"/> takes part in neither rule.
/// </summary>
public enum Faction : byte
{
    None = 0,
    Players = 1,
    Mobs = 2,
}

/// <summary>
/// Helpers for <see cref="DamageTypeMask"/>.
/// </summary>
public static class DamageTypeMaskExtensions
{
    /// <summary>
    /// Whether <paramref name="mask"/> contains <paramref name="type"/>.
    /// </summary>
    public static bool Includes(this DamageTypeMask mask, DamageType type)
    {
        return ((int)mask & (1 << (int)type)) != 0;
    }
}
