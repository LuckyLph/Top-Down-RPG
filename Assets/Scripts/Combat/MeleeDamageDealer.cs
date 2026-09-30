using UnityEngine;
using VContainer;

[DisallowMultipleComponent]
public class MeleeDamageDealer : MonoBehaviour
{
    [SerializeField, Min(0)] private int damageAmount = 1;
    [SerializeField, Min(0f)] private float attackInterval = 0.75f;

    private IClock clock = UnityClock.Shared;
    private float nextAttackTime;

    public int DamageAmount => damageAmount;
    public float AttackInterval => attackInterval;
    public float NextAttackTime => nextAttackTime;

    [Inject]
    public void Construct(IClock gameClock)
    {
        clock = gameClock ?? UnityClock.Shared;
    }

    public void Initialize(MobConfig config)
    {
        if (config == null)
        {
            return;
        }

        damageAmount = Mathf.Max(0, config.attackDamage);
        attackInterval = Mathf.Max(0f, config.attackInterval);
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

        DamageReceiver receiver = DamageReceiver.FindFor(target);
        if (receiver == null)
        {
            return false;
        }

        int appliedDamage = receiver.ReceiveDamage(damageAmount, gameObject);
        if (appliedDamage <= 0)
        {
            return false;
        }

        nextAttackTime = clock.Time + attackInterval;
        return true;
    }
}
