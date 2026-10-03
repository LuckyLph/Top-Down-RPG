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
    private StatusEffects statuses;
    private bool statusesResolved;

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
    public StatusTags StatusImmunities => profile != null ? profile.StatusImmunities : StatusTags.None;

    /// <summary>
    /// This unit's status effects, or null when it cannot carry any.
    /// </summary>
    public StatusEffects Statuses
    {
        get
        {
            if (!statusesResolved)
            {
                statuses = GetComponent<StatusEffects>();
                statusesResolved = true;
            }

            return statuses;
        }
    }

    private void Awake()
    {
        ResolveHealth();
    }

    public static DamageReceiver FindFor(Transform hitTransform)
    {
        return hitTransform != null ? hitTransform.GetComponentInParent<DamageReceiver>() : null;
    }

    /// <summary>
    /// This unit's defence against <paramref name="type"/> right now: its profile combined with its statuses.
    /// </summary>
    public DefenseSnapshot GetDefense(DamageType type)
    {
        int resistance = profile != null ? profile.GetResistance(type) : 0;
        DamageTypeMask immunities = profile != null ? profile.DamageImmunities : DamageTypeMask.None;
        float taken = 1f;

        StatusEffects unitStatuses = Statuses;
        if (unitStatuses != null)
        {
            StatusEffectSet set = unitStatuses.Set;
            resistance += set.GetResistanceDelta(type);
            immunities |= set.GrantedDamageImmunity;
            taken = set.DamageTakenMultiplier;
        }

        return new DefenseSnapshot(resistance, immunities.Includes(type), taken);
    }

    /// <summary>
    /// The multiplier this unit's statuses apply to the damage it deals.
    /// </summary>
    public float DamageDealtMultiplier
    {
        get
        {
            StatusEffects unitStatuses = Statuses;
            return unitStatuses != null ? unitStatuses.DamageDealtMultiplier : 1f;
        }
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
