using System;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Health))]
public class DamageReceiver : MonoBehaviour
{
    [SerializeField] private Vector3 floatingTextOffset = new(0f, 1.4f, 0f);
    [SerializeField, Tooltip("Faction, resistances and immunities. Without one the unit has no faction and resists nothing.")]
    private CombatProfile profile;

    private Health health;

    /// <summary>
    /// Raised on the authoritative machine for a hit this unit was immune to, which changes no HP.
    /// </summary>
    public event Action<DamageType, DamageFlags> ImmuneHit;

    public Health Health
    {
        get
        {
            ResolveHealth();
            return health;
        }
    }

    public Vector3 PopupWorldPosition => transform.position + floatingTextOffset;
    public CombatProfile Profile => profile;
    public Faction Faction => profile != null ? profile.Faction : Faction.None;

    private void Awake()
    {
        ResolveHealth();
    }

    public static DamageReceiver FindFor(Transform hitTransform)
    {
        return hitTransform != null ? hitTransform.GetComponentInParent<DamageReceiver>() : null;
    }

    /// <summary>
    /// This unit's defence against <paramref name="type"/> right now.
    /// </summary>
    public DefenseSnapshot GetDefense(DamageType type)
    {
        if (profile == null)
        {
            return DefenseSnapshot.None;
        }

        return new DefenseSnapshot(profile.GetResistance(type), profile.DamageImmunities.Includes(type), 1f);
    }

    internal void SetProfile(CombatProfile combatProfile)
    {
        profile = combatProfile;
    }

    internal void NotifyImmuneHit(DamageType type, DamageFlags flags)
    {
        ImmuneHit?.Invoke(type, flags);
    }

    private void ResolveHealth()
    {
        if (health == null)
        {
            health = GetComponent<Health>();
        }
    }
}
