using System;

// Session-wide combat event hub, registered in the Gameplay scope.
public sealed class CombatEvents
{
    public event Action<DamageReport> DamageApplied;

    public void Publish(DamageReport report)
    {
        DamageApplied?.Invoke(report);
    }
}
