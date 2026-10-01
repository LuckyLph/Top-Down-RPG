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

    public Vector3 PopupWorldPosition => transform.position + floatingTextOffset;

    private void Awake()
    {
        ResolveHealth();
    }

    public static DamageReceiver FindFor(Transform hitTransform)
    {
        return hitTransform != null ? hitTransform.GetComponentInParent<DamageReceiver>() : null;
    }

    private void ResolveHealth()
    {
        if (health == null)
        {
            health = GetComponent<Health>();
        }
    }
}
