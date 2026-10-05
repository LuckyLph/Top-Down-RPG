using System;
using UnityEngine;

/// <summary>
/// Marks a unit that combat can affect and is the target type of <see cref="DamageService"/>, <see cref="HitService"/>
/// and statuses: exposes its <see cref="Health"/>, faction and immunities from its <see cref="CombatProfile"/>, its
/// current defence and damage-dealt multiplier (profile plus statuses), and where popups appear over it.
/// </summary>
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

    /// <summary>
    /// Caches the sibling <see cref="Health"/>.
    /// </summary>
    private void Awake()
    {
        ResolveHealth();
    }

    /// <summary>
    /// The unit a collider's transform belongs to: the receiver on <paramref name="hitTransform"/> or a parent, or
    /// null when there is none.
    /// </summary>
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

    /// <summary>
    /// Test setup: replaces the combat profile without the inspector.
    /// </summary>
    internal void SetProfile(CombatProfile combatProfile)
    {
        profile = combatProfile;
    }

    /// <summary>
    /// Raises <see cref="ImmuneHit"/>; called by <see cref="DamageService"/> on the authority when a hit resolves as
    /// immune.
    /// </summary>
    internal void NotifyImmuneHit(DamageType type, DamageFlags flags)
    {
        ImmuneHit?.Invoke(type, flags);
    }

    /// <summary>
    /// Looks up the sibling <see cref="Health"/> when not cached yet, so it works before <c>Awake</c> too.
    /// </summary>
    private void ResolveHealth()
    {
        if (health == null)
        {
            health = GetComponent<Health>();
        }
    }
}
