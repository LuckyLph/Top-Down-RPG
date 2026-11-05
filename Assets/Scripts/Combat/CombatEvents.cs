using System;
using UnityEngine;

/// <summary>
/// The Gameplay-scope hub for combat outcomes: damage and heals that landed and statuses an immunity blocked,
/// published by <see cref="DamageService"/> and <see cref="StatusEffectService"/> on the authoritative machine and on
/// clients replaying the host's results, and consumed by presentation such as <see cref="DamagePopupPresenter"/>; and
/// a client's own melee hits it predicts at their contact (<see cref="AttackService.Predict"/>), for its hit feedback.
/// </summary>
public sealed class CombatEvents
{
    public event Action<DamageReport> DamageApplied;
    public event Action<HealReport> HealApplied;
    public event Action<StatusReport> StatusBlocked;

    /// <summary>
    /// Raised on a client with the attacker whose own melee hit it predicts at its contact; nothing is applied.
    /// </summary>
    public event Action<GameObject> HitPredicted;

    /// <summary>
    /// Raises <see cref="DamageApplied"/> with a hit that landed (including an immune one).
    /// </summary>
    public void Publish(DamageReport report)
    {
        DamageApplied?.Invoke(report);
    }

    /// <summary>
    /// Raises <see cref="HealApplied"/> with a heal that restored HP.
    /// </summary>
    public void Publish(HealReport report)
    {
        HealApplied?.Invoke(report);
    }

    /// <summary>
    /// Raises <see cref="StatusBlocked"/> with a status that a status immunity kept off its target.
    /// </summary>
    public void PublishBlocked(StatusReport report)
    {
        StatusBlocked?.Invoke(report);
    }

    /// <summary>
    /// Raises <see cref="HitPredicted"/> with <paramref name="attacker"/>, whose own melee hit a client predicts.
    /// </summary>
    public void PublishPredicted(GameObject attacker)
    {
        HitPredicted?.Invoke(attacker);
    }
}
