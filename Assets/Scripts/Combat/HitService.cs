using UnityEngine;

/// <summary>
/// Applies a <see cref="Hit"/>: its damage through <see cref="DamageService"/>, then, if the target survived, its
/// statuses through <see cref="StatusEffectService"/>, each under its own faction rule (so a hit's debuffs land
/// with its damage on enemies and its buffs on allies, whose damage is refused). What swings, mob attacks, projectiles
/// and abilities call. A hit on a unit out of play (<see cref="IUnitProtection"/>: a travelling player) is ignored.
/// Keeps the two services independent of each other.
/// </summary>
public sealed class HitService
{
    private readonly DamageService damageService;
    private readonly StatusEffectService statusService;
    private readonly IUnitProtection protection;

    /// <summary>
    /// Captures the damage and status services a hit is split between, and which units are out of play.
    /// </summary>
    public HitService(DamageService damageService, StatusEffectService statusService, IUnitProtection protection)
    {
        this.damageService = damageService;
        this.statusService = statusService;
        this.protection = protection;
    }

    /// <summary>
    /// Applies <paramref name="hit"/> to <paramref name="target"/>, its damage coming from <paramref name="origin"/>
    /// when it came from a point (where an arrow was shot from, where an attacker or caster stood). Returns whether it
    /// reached the target: its damage resolved (an immune hit included) or one of its statuses landed, stacked,
    /// refreshed or was blocked by an immunity. A refused hit (no authority, the wrong faction, a dead target) and an
    /// ignored one (a target out of play) return false.
    /// </summary>
    public bool ApplyHit(DamageReceiver target, in Hit hit, GameObject source = null, Vector2? origin = null)
    {
        if (target == null || protection.IsProtected(target))
        {
            return false;
        }

        bool reached = hit.Amount > 0 && damageService.ApplyDamage(target, hit.Amount, hit.Type, source, DamageFlags.None, origin).Resolved;
        if (target.Health == null || target.Health.IsDead)
        {
            return reached;
        }

        for (int i = 0; i < hit.StatusCount; i++)
        {
            StatusApplyOutcome outcome = statusService.Apply(target, hit.GetStatus(i), source);
            reached |= outcome is StatusApplyOutcome.Landed or StatusApplyOutcome.Stacked or StatusApplyOutcome.Refreshed or StatusApplyOutcome.Immune;
        }

        return reached;
    }
}
