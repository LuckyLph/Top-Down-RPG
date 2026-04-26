using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Health))]
public class DamageReceiver : MonoBehaviour
{
    [SerializeField] private Vector3 floatingTextOffset = new(0f, 1.4f, 0f);

    private Health health;

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

    public int ReceiveDamage(int amount, GameObject source = null)
    {
        ResolveHealth();

        int appliedDamage = health.ApplyDamage(amount, source);
        if (appliedDamage > 0)
        {
            FloatingDamageText.Spawn(appliedDamage, transform.position + floatingTextOffset);
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
