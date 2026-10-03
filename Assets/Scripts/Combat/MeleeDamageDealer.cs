using System.Collections.Generic;
using UnityEngine;
using VContainer;

[DisallowMultipleComponent]
public class MeleeDamageDealer : MonoBehaviour
{
    [SerializeField, Min(0)] private int damageAmount = 1;
    [SerializeField, Min(0f)] private float attackInterval = 0.75f;

    private IClock clock = UnityClock.Shared;
    private HitService hitService;
    private DamageType damageType = DamageType.Physical;
    private StatusEffectDefinition[] statuses;
    private float nextAttackTime;

    public int DamageAmount => damageAmount;
    public DamageType DamageType => damageType;
    public float AttackInterval => attackInterval;
    public float NextAttackTime => nextAttackTime;

    [Inject]
    public void Construct(IClock gameClock, HitService hits)
    {
        clock = gameClock ?? UnityClock.Shared;
        hitService = hits;
    }

    public void Initialize(MobConfig config)
    {
        if (config == null)
        {
            return;
        }

        damageAmount = Mathf.Max(0, config.AttackDamage);
        attackInterval = Mathf.Max(0f, config.AttackInterval);
        damageType = config.AttackDamageType;
        IReadOnlyList<StatusEffectDefinition> attackStatuses = config.AttackStatuses;
        statuses = new StatusEffectDefinition[attackStatuses.Count];
        for (int i = 0; i < statuses.Length; i++)
        {
            statuses[i] = attackStatuses[i];
        }
    }

    public void ResetCooldown(bool readyImmediately = true)
    {
        nextAttackTime = readyImmediately ? 0f : clock.Time + attackInterval;
    }

    public bool TryDealDamage(Transform target)
    {
        if (target == null || damageAmount <= 0 || clock.Time < nextAttackTime)
        {
            return false;
        }

        if (hitService == null)
        {
            Debug.LogError($"{nameof(MeleeDamageDealer)} was not injected with a {nameof(HitService)}.", this);
            return false;
        }

        DamageReceiver receiver = DamageReceiver.FindFor(target);
        if (receiver == null)
        {
            return false;
        }

        if (!hitService.ApplyHit(receiver, new Hit(damageAmount, damageType, statuses), gameObject))
        {
            return false;
        }

        nextAttackTime = clock.Time + attackInterval;
        return true;
    }
}
