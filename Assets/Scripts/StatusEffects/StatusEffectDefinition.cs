using UnityEngine;

/// <summary>
/// A timed buff or debuff: how it stacks, what it does each tick and how it changes the unit's combat values.
/// See Docs/DamageAndStatusEffects.md.
/// </summary>
[CreateAssetMenu(fileName = "Status", menuName = "TopDownRPG/Combat/Status Effect")]
public sealed class StatusEffectDefinition : ScriptableObject
{
    [Header("Display")]
    [SerializeField] private string displayName = "Status";
    [SerializeField] private Sprite icon;
    [SerializeField, TextArea] private string description;

    [Header("Rules")]
    [SerializeField] private StatusKind kind = StatusKind.Debuff;
    [SerializeField, Tooltip("Matched against status immunities.")] private StatusTags tags;
    [SerializeField, Min(0.05f)] private float duration = 5f;
    [SerializeField] private StatusStacking stacking = StatusStacking.Refresh;
    [SerializeField, Min(1), Tooltip("Stack count for AddStack, instance count for Independent.")] private int maxStacks = 1;

    [Header("Periodic")]
    [SerializeField] private StatusPeriodic periodic;
    [SerializeField, Min(0), Tooltip("Per tick and per stack.")] private int periodicAmount;
    [SerializeField] private DamageType periodicDamageType = DamageType.Physical;
    [SerializeField, Min(0.05f)] private float tickInterval = 1f;

    [Header("Modifiers (per stack)")]
    [SerializeField, Min(0f)] private float damageDealtMultiplier = 1f;
    [SerializeField, Min(0f)] private float damageTakenMultiplier = 1f;
    [SerializeField, Tooltip("Percentage points added to the unit's resistances.")]
    private DamageResistance[] resistanceDeltas = new DamageResistance[0];

    [Header("Immunities")]
    [SerializeField] private DamageTypeMask grantsDamageImmunity;
    [SerializeField, Tooltip("Landing this status removes active statuses with these tags.")]
    private StatusTags grantsStatusImmunity;

    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
    public Sprite Icon => icon;
    public string Description => description;
    public StatusKind Kind => kind;
    public StatusTags Tags => tags;
    public float Duration => Mathf.Max(0.05f, duration);
    public StatusStacking Stacking => stacking;
    public int MaxStacks => Mathf.Max(1, maxStacks);
    public StatusPeriodic Periodic => periodic;
    public int PeriodicAmount => Mathf.Max(0, periodicAmount);
    public DamageType PeriodicDamageType => periodicDamageType;
    public float TickInterval => Mathf.Max(0.05f, tickInterval);
    public float DamageDealtMultiplier => Mathf.Max(0f, damageDealtMultiplier);
    public float DamageTakenMultiplier => Mathf.Max(0f, damageTakenMultiplier);
    public DamageTypeMask GrantsDamageImmunity => grantsDamageImmunity;
    public StatusTags GrantsStatusImmunity => grantsStatusImmunity;
    public bool HasPeriodicEffect => periodic != StatusPeriodic.None && PeriodicAmount > 0;

    /// <summary>
    /// The resistance percentage points one stack adds against <paramref name="type"/>.
    /// </summary>
    public int GetResistanceDelta(DamageType type)
    {
        if (resistanceDeltas == null)
        {
            return 0;
        }

        int total = 0;
        for (int i = 0; i < resistanceDeltas.Length; i++)
        {
            if (resistanceDeltas[i].Type == type)
            {
                total += resistanceDeltas[i].Percent;
            }
        }

        return total;
    }

    public static StatusEffectDefinition Create(
        string name,
        StatusKind kind,
        float duration,
        StatusStacking stacking = StatusStacking.Refresh,
        int maxStacks = 1,
        StatusTags tags = StatusTags.None,
        StatusPeriodic periodic = StatusPeriodic.None,
        int periodicAmount = 0,
        DamageType periodicDamageType = DamageType.Physical,
        float tickInterval = 1f,
        float damageDealtMultiplier = 1f,
        float damageTakenMultiplier = 1f,
        DamageResistance[] resistanceDeltas = null,
        DamageTypeMask grantsDamageImmunity = DamageTypeMask.None,
        StatusTags grantsStatusImmunity = StatusTags.None)
    {
        StatusEffectDefinition definition = CreateInstance<StatusEffectDefinition>();
        definition.name = name;
        definition.displayName = name;
        definition.kind = kind;
        definition.duration = duration;
        definition.stacking = stacking;
        definition.maxStacks = maxStacks;
        definition.tags = tags;
        definition.periodic = periodic;
        definition.periodicAmount = periodicAmount;
        definition.periodicDamageType = periodicDamageType;
        definition.tickInterval = tickInterval;
        definition.damageDealtMultiplier = damageDealtMultiplier;
        definition.damageTakenMultiplier = damageTakenMultiplier;
        definition.resistanceDeltas = resistanceDeltas ?? new DamageResistance[0];
        definition.grantsDamageImmunity = grantsDamageImmunity;
        definition.grantsStatusImmunity = grantsStatusImmunity;
        definition.hideFlags = HideFlags.HideAndDontSave;
        return definition;
    }
}
