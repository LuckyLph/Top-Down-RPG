using UnityEngine;

/// <summary>
/// What <see cref="PlayerOrders"/> needs to know about a target, so orders stay free of engine lookups.
/// </summary>
public interface IUnitQueries
{
    /// <summary>
    /// Whether <paramref name="target"/> still exists and is not dead; orders on a dead target end.
    /// </summary>
    bool IsAlive(UnitTarget target);
    /// <summary>
    /// World position of <paramref name="target"/>, used as the chase goal and to aim at it.
    /// </summary>
    Vector2 PositionOf(UnitTarget target);

    /// <summary>
    /// Collider-to-collider distance from the player to <paramref name="target"/>, zero when they overlap.
    /// </summary>
    float DistanceTo(UnitTarget target);

    /// <summary>
    /// The living enemy closest to <paramref name="point"/> among those within <paramref name="range"/> of the player
    /// (collider to collider), or an empty target when there is none; an attack move engages it.
    /// </summary>
    UnitTarget NearestEnemyInRange(Vector2 point, float range);
}
