using UnityEngine;

public sealed class DamageService
{
    private readonly CombatEvents combatEvents;
    private readonly IGameAuthority authority;

    public DamageService(CombatEvents combatEvents, IGameAuthority authority)
    {
        this.combatEvents = combatEvents;
        this.authority = authority;
    }

    public int ApplyDamage(DamageReceiver target, int amount, GameObject source = null)
    {
        if (target == null || amount <= 0 || !authority.IsAuthoritative)
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
