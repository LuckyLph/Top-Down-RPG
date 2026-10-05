using System.Collections.Generic;
using UnityEngine;
using VContainer;

/// <summary>
/// A mob's melee attack: a <see cref="Hit"/> built from its <see cref="MobConfig"/>, dealt through
/// <see cref="HitService"/> no more often than the attack interval on the game clock.
/// </summary>
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

    /// <summary>
    /// Injects the clock the attack interval runs on (falling back to <see cref="UnityClock.Shared"/> when null)
    /// and the service hits go through.
    /// </summary>
    [Inject]
    public void Construct(IClock gameClock, HitService hits)
    {
        clock = gameClock ?? UnityClock.Shared;
        hitService = hits;
    }

    /// <summary>
    /// Copies the attack damage, interval, damage type and statuses from <paramref name="config"/>; keeps the
    /// current values when it is null.
    /// </summary>
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

    /// <summary>
    /// Makes the next attack available now, or one full interval from now when <paramref name="readyImmediately"/>
    /// is false.
    /// </summary>
    public void ResetCooldown(bool readyImmediately = true)
    {
        nextAttackTime = readyImmediately ? 0f : clock.Time + attackInterval;
    }

    /// <summary>
    /// Hits the unit <paramref name="target"/> belongs to when the attack is ready, with this mob as the source,
    /// and starts the interval only if the hit reached it. False when there is no target or damage, the attack is
    /// cooling down, the target is not a unit, or the hit was refused; logs an error without a
    /// <see cref="HitService"/>.
    /// </summary>
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
