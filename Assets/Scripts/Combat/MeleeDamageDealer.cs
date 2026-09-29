using UnityEngine;

[DisallowMultipleComponent]
public class MeleeDamageDealer : MonoBehaviour
{
    [SerializeField, Min(0)] private int damageAmount = 1;
    [SerializeField, Min(0f)] private float attackInterval = 0.75f;

    private float nextAttackTime;

    public int DamageAmount => damageAmount;
    public float AttackInterval => attackInterval;
    public float NextAttackTime => nextAttackTime;

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
        nextAttackTime = readyImmediately ? 0f : Time.time + attackInterval;
    }

    public bool TryDealDamage(Transform target)
    {
        if (target == null || damageAmount <= 0 || Time.time < nextAttackTime)
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

        nextAttackTime = Time.time + attackInterval;
        return true;
    }
}
