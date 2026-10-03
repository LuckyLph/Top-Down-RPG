using UnityEngine;

/// <summary>
/// A unit's faction, base resistances, damage immunities and status immunities, referenced by its <see cref="DamageReceiver"/>.
/// </summary>
[CreateAssetMenu(fileName = "Combat Profile", menuName = "TopDownRPG/Combat/Combat Profile")]
public sealed class CombatProfile : ScriptableObject
{
    [SerializeField] private Faction faction;
    [SerializeField, Tooltip("Percent per damage type; entries for the same type add up. Missing types resist nothing.")]
    private DamageResistance[] resistances = new DamageResistance[0];
    [SerializeField, Tooltip("Damage types that deal nothing to this unit.")]
    private DamageTypeMask damageImmunities;
    [SerializeField, Tooltip("Statuses with any of these tags never land on this unit.")]
    private StatusTags statusImmunities;

    public Faction Faction => faction;
    public DamageTypeMask DamageImmunities => damageImmunities;
    public StatusTags StatusImmunities => statusImmunities;

    /// <summary>
    /// The base resistance percentage against <paramref name="type"/>, before any clamping.
    /// </summary>
    public int GetResistance(DamageType type)
    {
        if (resistances == null)
        {
            return 0;
        }

        int total = 0;
        for (int i = 0; i < resistances.Length; i++)
        {
            if (resistances[i].Type == type)
            {
                total += resistances[i].Percent;
            }
        }

        return total;
    }

    public static CombatProfile Create(
        Faction faction,
        DamageTypeMask immunities = DamageTypeMask.None,
        StatusTags statusImmunities = StatusTags.None,
        params DamageResistance[] resistances)
    {
        CombatProfile profile = CreateInstance<CombatProfile>();
        profile.faction = faction;
        profile.damageImmunities = immunities;
        profile.statusImmunities = statusImmunities;
        profile.resistances = resistances ?? new DamageResistance[0];
        profile.hideFlags = HideFlags.HideAndDontSave;
        return profile;
    }
}
