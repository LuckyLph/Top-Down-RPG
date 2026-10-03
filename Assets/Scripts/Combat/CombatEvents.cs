using System;

public sealed class CombatEvents
{
    public event Action<DamageReport> DamageApplied;
    public event Action<HealReport> HealApplied;
    public event Action<StatusReport> StatusBlocked;

    public void Publish(DamageReport report)
    {
        DamageApplied?.Invoke(report);
    }

    public void Publish(HealReport report)
    {
        HealApplied?.Invoke(report);
    }

    public void PublishBlocked(StatusReport report)
    {
        StatusBlocked?.Invoke(report);
    }
}
