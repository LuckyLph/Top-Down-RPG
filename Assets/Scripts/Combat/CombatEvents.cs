using System;

public sealed class CombatEvents
{
    public event Action<DamageReport> DamageApplied;

    public void Publish(DamageReport report)
    {
        DamageApplied?.Invoke(report);
    }
}
