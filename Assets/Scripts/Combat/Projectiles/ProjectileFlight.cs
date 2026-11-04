using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// How a step of a projectile's flight ended: still flying, stopped by a wall or a unit, or out of range.
/// </summary>
public enum ProjectileOutcome
{
    Flying,
    Wall,
    Unit,
    Expired
}

/// <summary>
/// One projectile in flight (Docs/Animation.md, Phase 4), free of engine lookups so EditMode tests drive it with fake
/// queries. It flies from where it was launched at its speed, and each step stops it at the nearest wall or unit it
/// can hit, or once it has flown its range. It never hits its shooter, a dead unit or a unit of the faction its shooter
/// had at launch (it flies through teammates, even once its shooter is gone); a homing projectile with a living target
/// turns toward the target before each step and hits only it, and one without a target, or whose target died or is
/// gone, flies straight and hits the first unit it can. Reused by <see cref="ProjectileService"/>'s pool:
/// <see cref="Launch"/> resets it.
/// </summary>
public sealed class ProjectileFlight
{
    private const float MinStep = 1e-5f;

    private DamageReceiver shooterUnit;
    private Faction shooterFaction;
    private Transform shooterRoot;
    private Health targetHealth;
    private Collider2D targetCollider;

    public GameObject Source { get; private set; }
    public Hit Hit { get; private set; }
    public ProjectileSpec Spec { get; private set; }
    public Vector2 Position { get; private set; }

    /// <summary>
    /// The point it was shot from, its shooter's feet at the shot, which its hit comes from on every machine.
    /// </summary>
    public Vector2 Origin { get; private set; }

    public Vector2 Direction { get; private set; }
    public float Flown { get; private set; }
    public DamageReceiver Struck { get; private set; }
    public ProjectileVisual Visual { get; set; }

    /// <summary>
    /// Whether the projectile still turns toward a living target.
    /// </summary>
    public bool IsHoming => Spec.Homing && HasLivingTarget;

    private bool HasLivingTarget => targetHealth != null && targetCollider != null && targetCollider.enabled && !targetHealth.IsDead;

    /// <summary>
    /// Starts a flight of <paramref name="spec"/> from <paramref name="origin"/> along <paramref name="direction"/>
    /// (down when zero) for <paramref name="source"/>, whose unit (<paramref name="sourceUnit"/>, may be null) it never
    /// hits, carrying <paramref name="hit"/>, toward <paramref name="target"/> when it homes.
    /// </summary>
    public void Launch(GameObject source, DamageReceiver sourceUnit, in ProjectileSpec spec, in Hit hit, Vector2 origin, Vector2 direction, in UnitTarget target)
    {
        Source = source;
        shooterUnit = sourceUnit;
        shooterFaction = sourceUnit != null ? sourceUnit.Faction : Faction.None;
        shooterRoot = source != null ? source.transform : null;
        Spec = spec;
        Hit = hit;
        Position = origin;
        Origin = origin;
        Direction = direction.sqrMagnitude > 1e-8f ? direction.normalized : Vector2.down;
        Flown = 0f;
        Struck = null;
        targetHealth = target.Health;
        targetCollider = target.Collider;
        if (IsHoming)
        {
            Aim();
        }
    }

    /// <summary>
    /// Flies <paramref name="distance"/> units, or less up to the range, turning toward a homing target first, and
    /// stops at the nearest wall or unit it can hit along the way (<see cref="Struck"/> for a unit).
    /// </summary>
    public ProjectileOutcome Advance(float distance, IProjectileQueries queries, List<ProjectileContact> contacts)
    {
        bool homing = IsHoming;
        if (homing)
        {
            Aim();
        }

        float step = Mathf.Min(Mathf.Max(0f, distance), Spec.Range - Flown);
        if (step <= MinStep)
        {
            return Flown >= Spec.Range - MinStep ? ProjectileOutcome.Expired : ProjectileOutcome.Flying;
        }

        queries.Cast(Position, Direction, step, Spec.Radius, contacts);
        float nearest = float.PositiveInfinity;
        DamageReceiver nearestUnit = null;
        bool wall = false;
        for (int i = 0; i < contacts.Count; i++)
        {
            ProjectileContact contact = contacts[i];
            if (contact.Distance >= nearest || (!contact.IsWall && !CanHit(contact.Unit, homing)))
            {
                continue;
            }

            nearest = contact.Distance;
            nearestUnit = contact.Unit;
            wall = contact.IsWall;
        }

        contacts.Clear();
        if (!float.IsPositiveInfinity(nearest))
        {
            float travelled = Mathf.Clamp(nearest, 0f, step);
            Position += Direction * travelled;
            Flown += travelled;
            Struck = wall ? null : nearestUnit;
            return wall ? ProjectileOutcome.Wall : ProjectileOutcome.Unit;
        }

        Position += Direction * step;
        Flown += step;
        return Flown >= Spec.Range - MinStep ? ProjectileOutcome.Expired : ProjectileOutcome.Flying;
    }

    /// <summary>
    /// Forgets the shooter, target, hit and visual, so a pooled flight holds no references.
    /// </summary>
    public void Clear()
    {
        Source = null;
        shooterUnit = null;
        shooterFaction = Faction.None;
        shooterRoot = null;
        targetHealth = null;
        targetCollider = null;
        Struck = null;
        Hit = default;
        Visual = null;
    }

    /// <summary>
    /// Whether the projectile may stop at <paramref name="unit"/>: not its shooter or a part of it, alive, not of the
    /// faction its shooter had at launch, and, while it homes, its target.
    /// </summary>
    private bool CanHit(DamageReceiver unit, bool homing)
    {
        if (unit == null || unit == shooterUnit || (shooterRoot != null && unit.transform.IsChildOf(shooterRoot)))
        {
            return false;
        }

        Health health = unit.Health;
        if (health == null || health.IsDead || CombatFactions.SameFaction(shooterFaction, unit))
        {
            return false;
        }

        return !homing || health == targetHealth;
    }

    /// <summary>
    /// Turns toward the middle of the target's collider, keeping the direction when it is already there.
    /// </summary>
    private void Aim()
    {
        Vector2 toTarget = (Vector2)targetCollider.bounds.center - Position;
        if (toTarget.sqrMagnitude > 1e-8f)
        {
            Direction = toTarget.normalized;
        }
    }
}
