using UnityEngine;

/// <summary>
/// Source of randomness for gameplay (mob timings, patrol points), so tests can substitute a seeded or scripted one.
/// </summary>
public interface IRandom
{
    /// <summary>
    /// A uniformly distributed value between <paramref name="minInclusive"/> and <paramref name="maxInclusive"/>.
    /// </summary>
    float Range(float minInclusive, float maxInclusive);
    /// <summary>
    /// A uniformly distributed point inside the unit circle.
    /// </summary>
    Vector2 InsideUnitCircle();
}
