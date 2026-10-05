using System;

/// <summary>
/// The Gameplay-scope hub for combat outcomes: damage and heals that landed and statuses an immunity blocked,
/// published by <see cref="DamageService"/> and <see cref="StatusEffectService"/> on the authoritative machine and on
/// clients replaying the host's results, and consumed by presentation such as <see cref="DamagePopupPresenter"/>.
/// </summary>
public sealed class CombatEvents
{
    public event Action<DamageReport> DamageApplied;
    public event Action<HealReport> HealApplied;
    public event Action<StatusReport> StatusBlocked;

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
}
