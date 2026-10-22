using UnityEngine;

/// <summary>
/// How a weapon's projectile flies (Docs/Animation.md, Phase 4): its speed, how far it flies at most, the radius it
/// sweeps, how far ahead of the shooter's feet it leaves, whether it homes on the attack's target, and the visual each
/// machine shows for it.
/// </summary>
public readonly struct ProjectileSpec
{
    /// <summary>
    /// Creates a projectile flying at <paramref name="speed"/> units per second for at most <paramref name="range"/>
    /// units from the shooter's feet, sweeping <paramref name="radius"/>, leaving <paramref name="launchDistance"/>
    /// ahead of the feet, homing or not, drawn with <paramref name="visual"/> (none in tests).
    /// </summary>
    public ProjectileSpec(float speed, float range, float radius, float launchDistance, bool homing, ProjectileVisual visual)
    {
        Speed = Mathf.Max(0.01f, speed);
        Range = Mathf.Max(0f, range);
        Radius = Mathf.Max(0.001f, radius);
        LaunchDistance = Mathf.Clamp(launchDistance, 0f, Range);
        Homing = homing;
        Visual = visual;
    }

    public float Speed { get; }
    public float Range { get; }
    public float Radius { get; }
    public float LaunchDistance { get; }
    public bool Homing { get; }
    public ProjectileVisual Visual { get; }
}
