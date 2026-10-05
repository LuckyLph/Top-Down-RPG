using UnityEngine;

/// <summary>
/// Which side a targeted unit is on, seen from the local player.
/// </summary>
public enum UnitTeam
{
    None,
    Enemy,
    Ally
}

/// <summary>
/// A unit picked under the pointer or held by an order: its <see cref="Health"/> (the identity), the collider used
/// for range checks, and its team.
/// </summary>
public readonly struct UnitTarget
{
    /// <summary>
    /// Creates a target for the unit owning <paramref name="health"/>; a null health makes an empty target.
    /// </summary>
    public UnitTarget(Health health, Collider2D collider, UnitTeam team)
    {
        Health = health;
        Collider = collider;
        Team = team;
    }

    public Health Health { get; }
    public Collider2D Collider { get; }
    public UnitTeam Team { get; }
    public bool Exists => Health != null;

    /// <summary>
    /// Whether <paramref name="other"/> is the same unit, compared by health reference; never true for an empty
    /// target.
    /// </summary>
    public bool IsSameUnit(UnitTarget other)
    {
        return Exists && ReferenceEquals(Health, other.Health);
    }
}
