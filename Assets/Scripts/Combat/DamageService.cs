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

    /// <summary>
    /// Captures the event hub results are published on, the authority check and the resolution rules.
    /// </summary>
    public DamageService(CombatEvents combatEvents, IGameAuthority authority, CombatSettings settings)
    {
        this.combatEvents = combatEvents;
        this.authority = authority;
        this.settings = settings;
    }

    /// <summary>
    /// Resolves and applies a hostile hit, coming from <paramref name="origin"/> when it came from a point (where an
    /// arrow was shot from, where an attacker or caster stood). Rejected (returning <c>default</c>) without authority,
    /// for a missing or dead target, a non-positive amount, or a source of the target's own faction. An immune target
    /// takes nothing but the hit still resolves and is reported.
    /// </summary>
    public DamageResult ApplyDamage(
        DamageReceiver target,
        int amount,
        DamageType type = DamageType.Physical,
        GameObject source = null,
        DamageFlags flags = DamageFlags.None,
        Vector2? origin = null)
    {
        DamageReceiver sourceUnit = CombatFactions.UnitOf(source);
        float dealt = sourceUnit != null ? sourceUnit.DamageDealtMultiplier : 1f;
        return Resolve(target, amount, type, source, sourceUnit, dealt, flags, origin);
    }

    /// <summary>
    /// Applies a damage-over-time tick with the source's damage-dealt multiplier captured when its status landed,
    /// so ticks keep working after the source dies. Flagged <see cref="DamageFlags.Periodic"/>.
    /// </summary>
    public DamageResult ApplyPeriodicDamage(DamageReceiver target, int amount, DamageType type, GameObject source, float dealtMultiplier)
    {
        return Resolve(target, amount, type, source, CombatFactions.UnitOf(source), dealtMultiplier, DamageFlags.Periodic, null);
    }

    /// <summary>
    /// Applies and publishes a hit the host already resolved, regardless of authority, with this machine's copy of its
    /// <paramref name="source"/> (null when the host named none or this machine does not know it) and the point it came
    /// from as the host resolved it. Used by clients.
    /// </summary>
    public DamageResult ApplyReplicatedHit(DamageReceiver target, int amount, DamageType type, DamageFlags flags, GameObject source = null, Vector2? origin = null)
    {
        if (target == null || target.Health == null)
        {
            return default;
        }

        return ApplyAndPublish(target, target.Health, new DamageResult(type, amount, Mathf.Max(0, amount), flags), source, origin);
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

        if (CombatFactions.OtherFaction(CombatFactions.UnitOf(source), target))
        {
            return 0;
        }

        return HealAndPublish(target, amount, source);
    }

    /// <summary>
    /// Applies and publishes a heal the host already decided, regardless of authority, from this machine's copy of its
    /// <paramref name="source"/> when it knows one. Used by clients.
    /// </summary>
    public int ApplyReplicatedHeal(DamageReceiver target, int amount, GameObject source = null)
    {
        if (target == null || amount <= 0 || target.Health == null)
        {
            return 0;
        }

        return HealAndPublish(target, amount, source);
    }

    /// <summary>
    /// The shared authoritative path of direct and periodic damage: rejects as <see cref="ApplyDamage"/> describes,
    /// resolves the amount against the target's current defence with <see cref="DamageMath"/>, notifies the target
    /// of an immune hit, then applies and publishes the result.
    /// </summary>
    private DamageResult Resolve(
        DamageReceiver target,
        int amount,
        DamageType type,
        GameObject source,
        DamageReceiver sourceUnit,
        float dealtMultiplier,
        DamageFlags flags,
        Vector2? origin)
    {
        if (target == null || amount <= 0 || !authority.IsAuthoritative)
        {
            return default;
        }

        Health health = target.Health;
        if (health == null || health.IsDead || CombatFactions.SameFaction(sourceUnit, target))
        {
            return default;
        }

        DamageResult result = DamageMath.Resolve(amount, type, dealtMultiplier, target.GetDefense(type), settings, flags);
        if (result.IsImmune)
        {
            target.NotifyImmuneHit(result.Type, result.Flags, source);
        }

        return ApplyAndPublish(target, health, result, source, origin);
    }

    /// <summary>
    /// Removes the resolved amount from <paramref name="health"/> and publishes a <see cref="DamageReport"/> with the
    /// HP actually removed and the hit's <paramref name="origin"/>. Returns the result with that amount, or
    /// <c>default</c> (nothing published) when nothing landed and the hit was not immune.
    /// </summary>
    private DamageResult ApplyAndPublish(DamageReceiver target, Health health, DamageResult result, GameObject source, Vector2? origin)
    {
        int applied = result.Amount > 0 ? health.ApplyDamage(result.Amount, source, result.Type, result.Flags, origin) : 0;
        DamageResult landed = result.WithAmount(applied);
        if (!landed.Resolved)
        {
            return default;
        }

        combatEvents.Publish(new DamageReport(health, applied, landed.Type, landed.Flags, source, target.PopupWorldPosition, origin));
        return landed;
    }

    /// <summary>
    /// Heals the target's health and publishes a <see cref="HealReport"/> when HP was added. Returns the HP added.
    /// </summary>
    private int HealAndPublish(DamageReceiver target, int amount, GameObject source)
    {
        Health health = target.Health;
        int healed = health.Heal(amount, source);
        if (healed > 0)
        {
            combatEvents.Publish(new HealReport(health, healed, source, target.PopupWorldPosition));
        }

        return healed;
    }
}
