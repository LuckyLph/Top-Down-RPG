using UnityEngine;

/// <summary>
/// The faction rules shared by damage, heals and statuses. A unit or source without a faction is exempt.
/// </summary>
public static class CombatFactions
{
    /// <summary>
    /// The unit <paramref name="source"/> belongs to: the <see cref="DamageReceiver"/> on it or a parent.
    /// </summary>
    public static DamageReceiver UnitOf(GameObject source)
    {
        return source != null ? source.GetComponentInParent<DamageReceiver>() : null;
    }

    /// <summary>
    /// Whether <paramref name="source"/> and <paramref name="target"/> are on the same faction, which blocks
    /// hostile effects.
    /// </summary>
    public static bool SameFaction(DamageReceiver source, DamageReceiver target)
    {
        Faction sourceFaction = source != null ? source.Faction : Faction.None;
        return sourceFaction != Faction.None && sourceFaction == target.Faction;
    }

    /// <summary>
    /// Whether <paramref name="source"/> and <paramref name="target"/> are on different factions, which blocks
    /// helpful effects.
    /// </summary>
    public static bool OtherFaction(DamageReceiver source, DamageReceiver target)
    {
        Faction sourceFaction = source != null ? source.Faction : Faction.None;
        return sourceFaction != Faction.None && target.Faction != Faction.None && sourceFaction != target.Faction;
    }
}
