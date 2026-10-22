using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Something a projectile met along a step of its flight: how far along the step, and the unit it is, or none for a
/// wall.
/// </summary>
public readonly struct ProjectileContact
{
    /// <summary>
    /// Creates a contact <paramref name="distance"/> units along the step with <paramref name="unit"/>, or with a wall
    /// when it is null.
    /// </summary>
    public ProjectileContact(float distance, DamageReceiver unit)
    {
        Distance = distance;
        Unit = unit;
    }

    public float Distance { get; }
    public DamageReceiver Unit { get; }
    public bool IsWall => ReferenceEquals(Unit, null);
}

/// <summary>
/// What a projectile's flight asks of the world, so EditMode tests can stand fakes in for physics.
/// </summary>
public interface IProjectileQueries
{
    /// <summary>
    /// Fills <paramref name="results"/> (cleared first, in no particular order) with every wall and unit a circle of
    /// <paramref name="radius"/> meets moving <paramref name="distance"/> units from <paramref name="origin"/> along the
    /// unit <paramref name="direction"/>.
    /// </summary>
    void Cast(Vector2 origin, Vector2 direction, float distance, float radius, List<ProjectileContact> results);
}
