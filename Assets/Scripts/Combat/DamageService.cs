using UnityEngine;

/// <summary>
/// The only gameplay path that changes HP. Damage and heals are decided only where
/// <see cref="IGameAuthority.IsAuthoritative"/>; clients replay the host's results.
/// </summary>
public sealed class DamageService
{
    private readonly CombatEvents combatEvents;
    private readonly IGameAuthority authority;
    private readonly CombatSettings settings;

    public DamageService(CombatEvents combatEvents, IGameAuthority authority, CombatSettings settings)
    {
        this.combatEvents = combatEvents;
        this.authority = authority;
        this.settings = settings;
    }

    /// <summary>
    /// Resolves and applies a hostile hit. Rejected (returning <c>default</c>) without authority, for a missing or
    /// dead target, a non-positive amount, or a source of the target's own faction. An immune target takes nothing
    /// but the hit still resolves and is reported.
    /// </summary>
    public DamageResult ApplyDamage(
        DamageReceiver target,
        int amount,
        DamageType type = DamageType.Physical,
        GameObject source = null,
        DamageFlags flags = DamageFlags.None)
    {
        if (target == null || amount <= 0 || !authority.IsAuthoritative)
        {
            return default;
        }

        Health health = target.Health;
        if (health == null || health.IsDead || SameFaction(source, target))
        {
            return default;
        }

        DamageResult result = DamageMath.Resolve(amount, type, 1f, target.GetDefense(type), settings, flags);
        if (result.IsImmune)
        {
            target.NotifyImmuneHit(result.Type, result.Flags);
        }

        return ApplyAndPublish(target, health, result, source);
    }

    /// <summary>
    /// Applies and publishes a hit the host already resolved, regardless of authority. Used by clients.
    /// </summary>
    public DamageResult ApplyReplicatedHit(DamageReceiver target, int amount, DamageType type, DamageFlags flags)
    {
        if (target == null || target.Health == null)
        {
            return default;
        }

        return ApplyAndPublish(target, target.Health, new DamageResult(type, amount, Mathf.Max(0, amount), flags), null);
    }

    /// <summary>
    /// Heals a living target by up to <paramref name="amount"/>. Rejected without authority, for a non-positive
    /// amount, or a source of another faction. Returns the HP added.
    /// </summary>
    public int ApplyHeal(DamageReceiver target, int amount, GameObject source = null)
    {
        if (target == null || amount <= 0 || !authority.IsAuthoritative || target.Health == null)
        {
            return 0;
        }

        if (OtherFaction(source, target))
        {
            return 0;
        }

        return HealAndPublish(target, amount, source);
    }

    /// <summary>
    /// Applies and publishes a heal the host already decided, regardless of authority. Used by clients.
    /// </summary>
    public int ApplyReplicatedHeal(DamageReceiver target, int amount)
    {
        if (target == null || amount <= 0 || target.Health == null)
        {
            return 0;
        }

        return HealAndPublish(target, amount, null);
    }

    private DamageResult ApplyAndPublish(DamageReceiver target, Health health, DamageResult result, GameObject source)
    {
        int applied = result.Amount > 0 ? health.ApplyDamage(result.Amount, source, result.Type, result.Flags) : 0;
        DamageResult landed = result.WithAmount(applied);
        if (!landed.Resolved)
        {
            return default;
        }

        combatEvents.Publish(new DamageReport(health, applied, landed.Type, landed.Flags, source, target.PopupWorldPosition));
        return landed;
    }

    private int HealAndPublish(DamageReceiver target, int amount, GameObject source)
    {
        Health health = target.Health;
        int healed = health.Heal(amount);
        if (healed > 0)
        {
            combatEvents.Publish(new HealReport(health, healed, source, target.PopupWorldPosition));
        }

        return healed;
    }

    private static bool SameFaction(GameObject source, DamageReceiver target)
    {
        Faction sourceFaction = FactionOf(source);
        return sourceFaction != Faction.None && sourceFaction == target.Faction;
    }

    private static bool OtherFaction(GameObject source, DamageReceiver target)
    {
        Faction sourceFaction = FactionOf(source);
        return sourceFaction != Faction.None && target.Faction != Faction.None && sourceFaction != target.Faction;
    }

    private static Faction FactionOf(GameObject source)
    {
        if (source == null)
        {
            return Faction.None;
        }

        DamageReceiver receiver = source.GetComponentInParent<DamageReceiver>();
        return receiver != null ? receiver.Faction : Faction.None;
    }
}
