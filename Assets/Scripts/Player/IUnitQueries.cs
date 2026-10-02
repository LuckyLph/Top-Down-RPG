using UnityEngine;

/// <summary>
/// What <see cref="PlayerOrders"/> needs to know about a target, so orders stay free of engine lookups.
/// </summary>
public interface IUnitQueries
{
    bool IsAlive(UnitTarget target);
    Vector2 PositionOf(UnitTarget target);

    /// <summary>
    /// Collider-to-collider distance from the player to <paramref name="target"/>, zero when they overlap.
    /// </summary>
    float DistanceTo(UnitTarget target);
}
