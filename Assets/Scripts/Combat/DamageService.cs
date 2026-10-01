using UnityEngine;

public sealed class DamageService
{
    private readonly CombatEvents combatEvents;

    public DamageService(CombatEvents combatEvents)
    {
        this.combatEvents = combatEvents;
    }

    public int ApplyDamage(DamageReceiver target, int amount, GameObject source = null)
    {
        if (target == null || amount <= 0)
        {
            return 0;
        }

        Health health = target.Health;
        if (health == null)
        {
            return 0;
        }

        int appliedDamage = health.ApplyDamage(amount, source);
        if (appliedDamage > 0)
        {
            combatEvents.Publish(new DamageReport(health, appliedDamage, source, target.PopupWorldPosition));
        }

        return appliedDamage;
    }
}
