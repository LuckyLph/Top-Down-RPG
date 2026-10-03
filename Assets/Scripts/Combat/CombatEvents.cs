using System;

public sealed class CombatEvents
{
    public event Action<DamageReport> DamageApplied;
    public event Action<HealReport> HealApplied;

    public void Publish(DamageReport report)
    {
        DamageApplied?.Invoke(report);
    }

    public void Publish(HealReport report)
    {
        HealApplied?.Invoke(report);
    }
}
