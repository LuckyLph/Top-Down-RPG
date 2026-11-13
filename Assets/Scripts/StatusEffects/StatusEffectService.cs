using System.Collections.Generic;
using UnityEngine;
using VContainer.Unity;

/// <summary>
/// The only path that changes statuses, acting only where <see cref="IGameAuthority.IsAuthoritative"/>. Tracks
/// every unit a status landed on, ticks them each frame on the game clock (turning periodic ticks into damage and
/// heals through <see cref="DamageService"/>) and clears a unit's statuses when it dies. Units whose statuses are
/// held (a client's player while that client loads, <see cref="StatusEffects.IsHeld"/>) or that are out of play (a
/// travelling player, <see cref="IUnitProtection"/>) do not tick.
/// </summary>
public sealed class StatusEffectService : ITickable
{
    private readonly DamageService damageService;
    private readonly CombatEvents combatEvents;
    private readonly IGameAuthority authority;
    private readonly IClock clock;
    private readonly IUnitProtection protection;
    private readonly List<TrackedUnit> units = new();
    private readonly List<StatusTick> dueTicks = new();
    private readonly List<PendingTick> pendingTicks = new();
    private float lastTickTime = float.NaN;

    /// <summary>
    /// Captures the damage service periodic ticks go through, the combat events blocks are published on, the
    /// authority, the game clock that times ticks and which units are out of play.
    /// </summary>
    public StatusEffectService(DamageService damageService, CombatEvents combatEvents, IGameAuthority authority, IClock clock, IUnitProtection protection)
    {
        this.damageService = damageService;
        this.combatEvents = combatEvents;
        this.authority = authority;
        this.clock = clock;
        this.protection = protection;
    }

    internal int TrackedUnitCount => units.Count;

    /// <summary>
    /// Applies <paramref name="definition"/> to <paramref name="target"/> from <paramref name="source"/>. Debuffs
    /// only land across factions and buffs only within one; immunities block it (reported for an Immune popup).
    /// </summary>
    public StatusApplyOutcome Apply(DamageReceiver target, StatusEffectDefinition definition, GameObject source = null)
    {
        if (!authority.IsAuthoritative)
        {
            return StatusApplyOutcome.NoAuthority;
        }

        StatusEffects statuses = target != null ? target.Statuses : null;
        if (statuses == null || definition == null)
        {
            return StatusApplyOutcome.Invalid;
        }

        if (target.Health == null || target.Health.IsDead)
        {
            return StatusApplyOutcome.TargetDead;
        }

        DamageReceiver sourceUnit = CombatFactions.UnitOf(source);
        bool wrongFaction = definition.Kind == StatusKind.Debuff
            ? CombatFactions.SameFaction(sourceUnit, target)
            : CombatFactions.OtherFaction(sourceUnit, target);
        if (wrongFaction)
        {
            return StatusApplyOutcome.WrongFaction;
        }

        float dealt = sourceUnit != null ? sourceUnit.DamageDealtMultiplier : 1f;
        StatusApplyOutcome outcome = statuses.Set.Apply(definition, source, dealt, target.StatusImmunities);
        if (outcome == StatusApplyOutcome.Immune)
        {
            statuses.NotifyBlocked(definition, source);
            PublishBlocked(target, definition, source);
            return outcome;
        }

        Track(target);
        return outcome;
    }

    /// <summary>
    /// Whether <paramref name="unit"/>'s statuses stand still: held by its <see cref="StatusEffects"/> or out of play.
    /// False for a unit without statuses.
    /// </summary>
    public bool IsHeld(DamageReceiver unit)
    {
        StatusEffects statuses = unit != null ? unit.Statuses : null;
        return statuses != null && (statuses.IsHeld || protection.IsProtected(unit));
    }

    /// <summary>
    /// Removes every instance of <paramref name="definition"/> from <paramref name="target"/>.
    /// </summary>
    public bool Remove(DamageReceiver target, StatusEffectDefinition definition)
    {
        StatusEffects statuses = target != null ? target.Statuses : null;
        return authority.IsAuthoritative && statuses != null && statuses.Set.Remove(definition);
    }

    /// <summary>
    /// Removes every status from <paramref name="target"/> on the authoritative machine; does nothing elsewhere.
    /// </summary>
    public void ClearAll(DamageReceiver target)
    {
        StatusEffects statuses = target != null ? target.Statuses : null;
        if (authority.IsAuthoritative && statuses != null)
        {
            statuses.Set.Clear();
        }
    }

    /// <summary>
    /// Publishes a block the host reported, regardless of authority, from this machine's copy of its
    /// <paramref name="source"/> when it knows one. Used by clients for the Immune popup.
    /// </summary>
    public void PublishReplicatedBlock(DamageReceiver target, StatusEffectDefinition definition, GameObject source = null)
    {
        if (target != null && definition != null)
        {
            PublishBlocked(target, definition, source);
        }
    }

    /// <summary>
    /// Each frame: measures the game-clock time since the last frame and, on the authoritative machine, advances
    /// every tracked unit by it. The first frame only records the time.
    /// </summary>
    public void Tick()
    {
        float now = clock.Time;
        float deltaTime = float.IsNaN(lastTickTime) ? 0f : now - lastTickTime;
        lastTickTime = now;
        if (!authority.IsAuthoritative || deltaTime <= 0f)
        {
            return;
        }

        Advance(deltaTime);
    }

    /// <summary>
    /// Advances the statuses of every tracked unit neither held nor out of play by <paramref name="deltaTime"/>,
    /// untracking units with none left, then applies the periodic ticks that came due as heals or damage once every set
    /// has been advanced.
    /// </summary>
    internal void Advance(float deltaTime)
    {
        pendingTicks.Clear();
        for (int i = units.Count - 1; i >= 0; i--)
        {
            DamageReceiver unit = units[i].Receiver;
            StatusEffects statuses = unit != null ? unit.Statuses : null;
            if (statuses == null || statuses.Count == 0)
            {
                Untrack(i);
                continue;
            }

            if (statuses.IsHeld || protection.IsProtected(unit))
            {
                continue;
            }

            dueTicks.Clear();
            statuses.Set.Tick(deltaTime, dueTicks);
            for (int t = 0; t < dueTicks.Count; t++)
            {
                pendingTicks.Add(new PendingTick(unit, dueTicks[t]));
            }
        }

        for (int i = 0; i < pendingTicks.Count; i++)
        {
            PendingTick pending = pendingTicks[i];
            if (pending.Target == null)
            {
                continue;
            }

            StatusTick tick = pending.Tick;
            if (tick.Definition.Periodic == StatusPeriodic.Heal)
            {
                damageService.ApplyHeal(pending.Target, tick.Amount, tick.Source);
            }
            else
            {
                damageService.ApplyPeriodicDamage(pending.Target, tick.Amount, tick.Definition.PeriodicDamageType, tick.Source, tick.DealtMultiplier);
            }
        }

        pendingTicks.Clear();
    }

    /// <summary>
    /// Starts ticking <paramref name="target"/> and listening for its death, once per unit.
    /// </summary>
    private void Track(DamageReceiver target)
    {
        for (int i = 0; i < units.Count; i++)
        {
            if (units[i].Receiver == target)
            {
                return;
            }
        }

        Health health = target.Health;
        health.Died += HandleUnitDied;
        units.Add(new TrackedUnit(target, health));
    }

    /// <summary>
    /// Stops ticking the unit at <paramref name="index"/> and listening for its death.
    /// </summary>
    private void Untrack(int index)
    {
        units[index].Health.Died -= HandleUnitDied;
        units.RemoveAt(index);
    }

    /// <summary>
    /// Clears every status on a tracked unit that died.
    /// </summary>
    private void HandleUnitDied(Health health)
    {
        for (int i = 0; i < units.Count; i++)
        {
            if (ReferenceEquals(units[i].Health, health) && units[i].Receiver != null && units[i].Receiver.Statuses != null)
            {
                units[i].Receiver.Statuses.Set.Clear();
            }
        }
    }

    /// <summary>
    /// Publishes a <see cref="StatusReport"/> of the block at the target's popup position.
    /// </summary>
    private void PublishBlocked(DamageReceiver target, StatusEffectDefinition definition, GameObject source)
    {
        combatEvents.PublishBlocked(new StatusReport(target.Health, definition, source, target.PopupWorldPosition));
    }

    /// <summary>
    /// A unit being ticked, with the health captured when tracking started, which the death handler is attached to.
    /// </summary>
    private readonly struct TrackedUnit
    {
        /// <summary>
        /// Pairs <paramref name="receiver"/> with its <paramref name="health"/>.
        /// </summary>
        public TrackedUnit(DamageReceiver receiver, Health health)
        {
            Receiver = receiver;
            Health = health;
        }

        public DamageReceiver Receiver { get; }
        public Health Health { get; }
    }

    /// <summary>
    /// A periodic tick that came due on <see cref="Target"/>, held until every unit has advanced and then applied.
    /// </summary>
    private readonly struct PendingTick
    {
        /// <summary>
        /// Pairs <paramref name="tick"/> with the unit it applies to.
        /// </summary>
        public PendingTick(DamageReceiver target, StatusTick tick)
        {
            Target = target;
            Tick = tick;
        }

        public DamageReceiver Target { get; }
        public StatusTick Tick { get; }
    }
}
