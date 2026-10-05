using UnityEngine;

/// <summary>
/// Applies a <see cref="Hit"/>: its damage through <see cref="DamageService"/>, then, if the target survived, its
/// statuses through <see cref="StatusEffectService"/>, each under its own faction rule (so a hit's debuffs land
/// with its damage on enemies and its buffs on allies, whose damage is refused). What swings, mob attacks and
/// abilities call. Keeps the two services independent of each other.
/// </summary>
public sealed class HitService
{
    private readonly DamageService damageService;
    private readonly StatusEffectService statusService;

    /// <summary>
    /// Captures the damage and status services a hit is split between.
    /// </summary>
    public HitService(DamageService damageService, StatusEffectService statusService)
    {
        this.damageService = damageService;
        this.statusService = statusService;
    }

    /// <summary>
    /// Applies <paramref name="hit"/> to <paramref name="target"/>. Returns whether it reached the target: its damage
    /// resolved (an immune hit included) or one of its statuses landed, stacked, refreshed or was blocked by an
    /// immunity. A refused hit (no authority, the wrong faction, a dead target) returns false.
    /// </summary>
    public bool ApplyHit(DamageReceiver target, in Hit hit, GameObject source = null)
    {
        if (target == null)
        {
            return false;
        }

        bool reached = hit.Amount > 0 && damageService.ApplyDamage(target, hit.Amount, hit.Type, source).Resolved;
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
