using System;
using System.Collections.Generic;
using UnityEngine;
using VContainer.Unity;

/// <summary>
/// Flies the projectiles weapons launch at contact (Docs/Animation.md, Phase 4), on every machine, each frame on the
/// game clock. Every machine flies the arrows it learns of, the owner's at its contact and the others' from the owner's
/// contact message, and shows each through a pooled <see cref="ProjectileVisual"/>; only where
/// <see cref="IGameAuthority.IsAuthoritative"/> does a projectile that stops at a unit hit it, through
/// <see cref="HitService"/> with its shooter as the source, so each arrow lands at most once, on the host. Projectiles
/// are not networked objects. Flights are pooled, so launching and flying allocates nothing once warm.
/// </summary>
public sealed class ProjectileService : ITickable, IDisposable
{
    private readonly IGameAuthority authority;
    private readonly HitService hitService;
    private readonly IProjectileQueries queries;
    private readonly IClock clock;
    private readonly ProjectileVisualPool visuals;
    private readonly List<ProjectileFlight> flying = new();
    private readonly Stack<ProjectileFlight> idle = new();
    private readonly List<ProjectileContact> contacts = new();
    private float lastTickTime = float.NaN;

    /// <summary>
    /// Casts against the unit and projectile wall layers of <paramref name="settings"/> and shows projectiles from
    /// <paramref name="visualPool"/>.
    /// </summary>
    public ProjectileService(IGameAuthority authority, HitService hitService, CombatSettings settings, IClock clock, ProjectileVisualPool visualPool)
        : this(authority, hitService, new PhysicsProjectileQueries(settings.UnitLayers, settings.ProjectileWallLayers), clock, visualPool)
    {
    }

    /// <summary>
    /// Flies projectiles through <paramref name="projectileQueries"/>, for tests that stand fakes in for physics;
    /// <paramref name="visualPool"/> may be null to show nothing.
    /// </summary>
    internal ProjectileService(IGameAuthority authority, HitService hitService, IProjectileQueries projectileQueries, IClock clock, ProjectileVisualPool visualPool)
    {
        this.authority = authority;
        this.hitService = hitService;
        queries = projectileQueries;
        this.clock = clock;
        visuals = visualPool;
    }

    /// <summary>
    /// Raised when a projectile stops at a unit, with the unit, whether this machine resolved the hit, and the shooter.
    /// For tests.
    /// </summary>
    internal event Action<DamageReceiver, bool, GameObject> UnitStruck;

    public int FlyingCount => flying.Count;

    /// <summary>
    /// The projectile at <paramref name="index"/> of those flying, for tests.
    /// </summary>
    internal ProjectileFlight Flying(int index)
    {
        return flying[index];
    }

    /// <summary>
    /// Launches <paramref name="spec"/> for <paramref name="shooter"/> from its feet at <paramref name="origin"/> along
    /// <paramref name="direction"/>, carrying <paramref name="hit"/>, homing on <paramref name="target"/> when the spec
    /// homes. It covers its launch distance at once (so a wall or unit within it stops it now) and is shown there.
    /// </summary>
    public void Launch(GameObject shooter, in ProjectileSpec spec, in Hit hit, Vector2 origin, Vector2 direction, in UnitTarget target)
    {
        ProjectileFlight flight = idle.Count > 0 ? idle.Pop() : new ProjectileFlight();
        flight.Launch(shooter, CombatFactions.UnitOf(shooter), spec, hit, origin, direction, target);
        if (Land(flight, flight.Advance(spec.LaunchDistance, queries, contacts)))
        {
            return;
        }

        flight.Visual = visuals != null ? visuals.Get(spec.Visual) : null;
        if (flight.Visual != null)
        {
            flight.Visual.Show(flight.Position, flight.Direction);
        }

        flying.Add(flight);
    }

    /// <summary>
    /// Each frame: flies every projectile by its speed over the game-clock time since the last frame.
    /// </summary>
    public void Tick()
    {
        float now = clock.Time;
        float deltaTime = float.IsNaN(lastTickTime) ? 0f : now - lastTickTime;
        lastTickTime = now;
        Advance(deltaTime);
    }

    /// <summary>
    /// Flies every projectile <paramref name="deltaTime"/> seconds, landing the ones that stop and moving the others'
    /// visuals.
    /// </summary>
    internal void Advance(float deltaTime)
    {
        if (deltaTime <= 0f)
        {
            return;
        }

        for (int i = flying.Count - 1; i >= 0; i--)
        {
            ProjectileFlight flight = flying[i];
            if (Land(flight, flight.Advance(flight.Spec.Speed * deltaTime, queries, contacts)))
            {
                flying.RemoveAt(i);
                continue;
            }

            if (flight.Visual != null)
            {
                flight.Visual.Move(flight.Position, flight.Direction);
            }
        }
    }

    /// <summary>
    /// Releases every visual and forgets every projectile.
    /// </summary>
    public void Dispose()
    {
        for (int i = 0; i < flying.Count; i++)
        {
            Recycle(flying[i]);
        }

        flying.Clear();
    }

    /// <summary>
    /// Ends <paramref name="flight"/> when <paramref name="outcome"/> stopped it, hitting the unit it stopped at where
    /// this machine is the authority; returns whether it ended.
    /// </summary>
    private bool Land(ProjectileFlight flight, ProjectileOutcome outcome)
    {
        if (outcome == ProjectileOutcome.Flying)
        {
            return false;
        }

        if (outcome == ProjectileOutcome.Unit)
        {
            bool resolved = authority.IsAuthoritative && hitService != null;
            if (resolved)
            {
                hitService.ApplyHit(flight.Struck, flight.Hit, flight.Source);
            }

            UnitStruck?.Invoke(flight.Struck, resolved, flight.Source);
        }

        Recycle(flight);
        return true;
    }

    /// <summary>
    /// Returns the flight's visual to the pool and the flight to the idle stack.
    /// </summary>
    private void Recycle(ProjectileFlight flight)
    {
        if (visuals != null && flight.Visual != null)
        {
            visuals.Release(flight.Visual);
        }

        flight.Clear();
        idle.Push(flight);
    }
}
