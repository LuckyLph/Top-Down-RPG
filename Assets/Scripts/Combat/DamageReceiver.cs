using UnityEngine;
using VContainer;

[DisallowMultipleComponent]
[RequireComponent(typeof(Health))]
public class DamageReceiver : MonoBehaviour
{
    [SerializeField] private Vector3 floatingTextOffset = new(0f, 1.4f, 0f);

    private Health health;
    private CombatEvents combatEvents;

    public Health Health
    {
        get
        {
            ResolveHealth();
            return health;
        }
    }

    private void Awake()
    {
        ResolveHealth();
    }

    [Inject]
    public void Construct(CombatEvents events)
    {
        combatEvents = events;
    }

    public static DamageReceiver FindFor(Transform hitTransform)
    {
        return hitTransform != null ? hitTransform.GetComponentInParent<DamageReceiver>() : null;
    }

    public int ReceiveDamage(int amount, GameObject source = null)
    {
        ResolveHealth();

        int appliedDamage = health.ApplyDamage(amount, source);
        if (appliedDamage > 0)
        {
            combatEvents?.Publish(new DamageReport(health, appliedDamage, source, transform.position + floatingTextOffset));
        }

        return appliedDamage;
    }

    private void ResolveHealth()
    {
        if (health == null)
        {
            health = GetComponent<Health>();
        }
    }
}
