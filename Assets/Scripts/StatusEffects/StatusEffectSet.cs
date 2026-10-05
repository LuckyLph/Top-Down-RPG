using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One unit's active status instances and their combined effect. Timers count down by the delta passed to
/// <see cref="Tick"/>; combined values are recomputed only when the set changes. On clients the set is a mirror
/// rebuilt with <see cref="SyncFrom"/> and never ticked.
/// </summary>
public sealed class StatusEffectSet
{
    private const float Epsilon = 0.0001f;
    private const int DamageTypeCount = (int)DamageType.True + 1;

    private readonly List<Instance> instances = new();
    private readonly int[] resistanceDeltas = new int[DamageTypeCount];
    private int nextInstanceId = 1;

    public event Action Changed;

    public int Count => instances.Count;
    public float MoveSpeedMultiplier { get; private set; } = 1f;
    public float DamageDealtMultiplier { get; private set; } = 1f;
    public float DamageTakenMultiplier { get; private set; } = 1f;
    public DamageTypeMask GrantedDamageImmunity { get; private set; }
    public StatusTags GrantedStatusImmunity { get; private set; }
    public StatusControls Controls { get; private set; }

    /// <summary>
    /// The resistance percentage points the active statuses add against <paramref name="type"/>, stacks included;
    /// 0 for an unknown type.
    /// </summary>
    public int GetResistanceDelta(DamageType type)
    {
        int index = (int)type;
        return index >= 0 && index < DamageTypeCount ? resistanceDeltas[index] : 0;
    }

    /// <summary>
    /// A read-only copy of the instance at <paramref name="index"/> (0 to <see cref="Count"/> - 1).
    /// </summary>
    public StatusSnapshot GetSnapshot(int index)
    {
        Instance instance = instances[index];
        return new StatusSnapshot(instance.Definition, instance.Id, instance.Stacks, instance.Remaining);
    }

    /// <summary>
    /// Stacks of <paramref name="definition"/>: the stack count of its instance, or the number of instances for
    /// <see cref="StatusStacking.Independent"/>. 0 when it is not active.
    /// </summary>
    public int GetStacks(StatusEffectDefinition definition)
    {
        int stacks = 0;
        for (int i = 0; i < instances.Count; i++)
        {
            if (instances[i].Definition == definition)
            {
                stacks += instances[i].Stacks;
            }
        }

        return stacks;
    }

    /// <summary>
    /// Whether a status with <paramref name="tags"/> would be blocked by <paramref name="baseImmunity"/> or by an
    /// immunity an active status grants.
    /// </summary>
    public bool IsImmune(StatusTags tags, StatusTags baseImmunity)
    {
        return (tags & (baseImmunity | GrantedStatusImmunity)) != 0;
    }

    /// <summary>
    /// Applies <paramref name="definition"/> following its stacking rule, capturing <paramref name="source"/> and
    /// its <paramref name="dealtMultiplier"/> for periodic ticks.
    /// </summary>
    public StatusApplyOutcome Apply(StatusEffectDefinition definition, GameObject source, float dealtMultiplier, StatusTags baseImmunity = StatusTags.None)
    {
        if (definition == null)
        {
            return StatusApplyOutcome.Invalid;
        }

        if (IsImmune(definition.Tags, baseImmunity))
        {
            return StatusApplyOutcome.Immune;
        }

        StatusApplyOutcome outcome = definition.Stacking switch
        {
            StatusStacking.AddStack => ApplyStack(definition, source, dealtMultiplier),
            StatusStacking.Independent => ApplyIndependent(definition, source, dealtMultiplier),
            _ => ApplyRefresh(definition, source, dealtMultiplier),
        };

        if (definition.GrantsStatusImmunity != StatusTags.None)
        {
            RemoveTagged(definition.GrantsStatusImmunity, definition);
        }

        Recompute();
        Changed?.Invoke();
        return outcome;
    }

    /// <summary>
    /// Removes every instance of <paramref name="definition"/>. Returns whether any was active.
    /// </summary>
    public bool Remove(StatusEffectDefinition definition)
    {
        bool removed = false;
        for (int i = instances.Count - 1; i >= 0; i--)
        {
            if (instances[i].Definition == definition)
            {
                instances.RemoveAt(i);
                removed = true;
            }
        }

        if (removed)
        {
            Recompute();
            Changed?.Invoke();
        }

        return removed;
    }

    /// <summary>
    /// Removes every instance, raising <see cref="Changed"/> only when there was any.
    /// </summary>
    public void Clear()
    {
        if (instances.Count == 0)
        {
            return;
        }

        instances.Clear();
        Recompute();
        Changed?.Invoke();
    }

    /// <summary>
    /// Advances every timer by <paramref name="deltaTime"/>, adding each periodic tick that came due to
    /// <paramref name="dueTicks"/> (a tick on the expiry instant included) and removing expired instances.
    /// </summary>
    public void Tick(float deltaTime, List<StatusTick> dueTicks)
    {
        if (deltaTime <= 0f || instances.Count == 0)
        {
            return;
        }

        bool expired = false;
        for (int i = instances.Count - 1; i >= 0; i--)
        {
            Instance instance = instances[i];
            instance.Remaining -= deltaTime;

            if (instance.Definition.HasPeriodicEffect)
            {
                instance.TickTimer -= deltaTime;
                float interval = instance.Definition.TickInterval;
                while (instance.TickTimer <= Epsilon && instance.Remaining - instance.TickTimer >= -Epsilon)
                {
                    dueTicks?.Add(new StatusTick(
                        instance.Definition,
                        instance.Definition.PeriodicAmount * instance.Stacks,
                        instance.Source,
                        instance.DealtMultiplier));
                    instance.TickTimer += interval;
                }
            }

            if (instance.Remaining <= Epsilon)
            {
                instances.RemoveAt(i);
                expired = true;
            }
            else
            {
                instances[i] = instance;
            }
        }

        if (expired)
        {
            Recompute();
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Runs every remaining time down by <paramref name="deltaTime"/> (never below 0) without ticking or expiring
    /// anything, so a client's mirror shows countdowns between the host's updates.
    /// </summary>
    public void CountDown(float deltaTime)
    {
        if (deltaTime <= 0f)
        {
            return;
        }

        for (int i = 0; i < instances.Count; i++)
        {
            Instance instance = instances[i];
            instance.Remaining = Mathf.Max(0f, instance.Remaining - deltaTime);
            instances[i] = instance;
        }
    }

    /// <summary>
    /// The longest remaining time among the instances of <paramref name="definition"/>, or 0 when it is not active.
    /// </summary>
    public float GetRemaining(StatusEffectDefinition definition)
    {
        float remaining = 0f;
        for (int i = 0; i < instances.Count; i++)
        {
            if (instances[i].Definition == definition && instances[i].Remaining > remaining)
            {
                remaining = instances[i].Remaining;
            }
        }

        return remaining;
    }

    /// <summary>
    /// Replaces the instances with <paramref name="snapshots"/>, as a client mirroring the host does.
    /// </summary>
    public void SyncFrom(IReadOnlyList<StatusSnapshot> snapshots)
    {
        instances.Clear();
        for (int i = 0; i < snapshots.Count; i++)
        {
            StatusSnapshot snapshot = snapshots[i];
            if (snapshot.Definition == null)
            {
                continue;
            }

            instances.Add(new Instance
            {
                Definition = snapshot.Definition,
                Id = snapshot.InstanceId,
                Stacks = Mathf.Max(1, snapshot.Stacks),
                Remaining = snapshot.Remaining,
                TickTimer = snapshot.Definition.TickInterval,
                DealtMultiplier = 1f,
            });
        }

        Recompute();
        Changed?.Invoke();
    }

    /// <summary>
    /// <see cref="StatusStacking.Refresh"/>: lands a new instance, or resets the existing one's duration and takes
    /// over its source and multiplier.
    /// </summary>
    private StatusApplyOutcome ApplyRefresh(StatusEffectDefinition definition, GameObject source, float dealtMultiplier)
    {
        int index = IndexOf(definition);
        if (index < 0)
        {
            instances.Add(NewInstance(definition, source, dealtMultiplier));
            return StatusApplyOutcome.Landed;
        }

        Instance instance = instances[index];
        instance.Remaining = definition.Duration;
        instance.Source = source;
        instance.DealtMultiplier = dealtMultiplier;
        instances[index] = instance;
        return StatusApplyOutcome.Refreshed;
    }

    /// <summary>
    /// <see cref="StatusStacking.AddStack"/>: lands a new instance, or adds a stack up to the maximum (a refresh once
    /// at the cap) and resets the duration, source and multiplier.
    /// </summary>
    private StatusApplyOutcome ApplyStack(StatusEffectDefinition definition, GameObject source, float dealtMultiplier)
    {
        int index = IndexOf(definition);
        if (index < 0)
        {
            instances.Add(NewInstance(definition, source, dealtMultiplier));
            return StatusApplyOutcome.Landed;
        }

        Instance instance = instances[index];
        bool added = instance.Stacks < definition.MaxStacks;
        instance.Stacks = Mathf.Min(definition.MaxStacks, instance.Stacks + 1);
        instance.Remaining = definition.Duration;
        instance.Source = source;
        instance.DealtMultiplier = dealtMultiplier;
        instances[index] = instance;
        return added ? StatusApplyOutcome.Stacked : StatusApplyOutcome.Refreshed;
    }

    /// <summary>
    /// <see cref="StatusStacking.Independent"/>: adds a separate instance, or at the instance cap restarts the one
    /// closest to expiring with the new source and multiplier. Always reports a landing.
    /// </summary>
    private StatusApplyOutcome ApplyIndependent(StatusEffectDefinition definition, GameObject source, float dealtMultiplier)
    {
        int count = 0;
        int closestToExpiring = -1;
        for (int i = 0; i < instances.Count; i++)
        {
            if (instances[i].Definition != definition)
            {
                continue;
            }

            count++;
            if (closestToExpiring < 0 || instances[i].Remaining < instances[closestToExpiring].Remaining)
            {
                closestToExpiring = i;
            }
        }

        if (count < definition.MaxStacks)
        {
            instances.Add(NewInstance(definition, source, dealtMultiplier));
        }
        else
        {
            Instance replaced = instances[closestToExpiring];
            replaced.Remaining = definition.Duration;
            replaced.Source = source;
            replaced.DealtMultiplier = dealtMultiplier;
            instances[closestToExpiring] = replaced;
        }

        return StatusApplyOutcome.Landed;
    }

    /// <summary>
    /// A fresh one-stack instance with the next id, the full duration and its first tick one interval away.
    /// </summary>
    private Instance NewInstance(StatusEffectDefinition definition, GameObject source, float dealtMultiplier)
    {
        return new Instance
        {
            Definition = definition,
            Id = nextInstanceId++,
            Stacks = 1,
            Remaining = definition.Duration,
            TickTimer = definition.TickInterval,
            Source = source,
            DealtMultiplier = dealtMultiplier,
        };
    }

    /// <summary>
    /// Removes instances carrying any of <paramref name="tags"/>, except those of <paramref name="keep"/> (the status
    /// granting the immunity). Leaves recomputing and raising <see cref="Changed"/> to the caller.
    /// </summary>
    private void RemoveTagged(StatusTags tags, StatusEffectDefinition keep)
    {
        for (int i = instances.Count - 1; i >= 0; i--)
        {
            StatusEffectDefinition definition = instances[i].Definition;
            if (definition != keep && (definition.Tags & tags) != 0)
            {
                instances.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// The index of the first instance of <paramref name="definition"/>, or -1.
    /// </summary>
    private int IndexOf(StatusEffectDefinition definition)
    {
        for (int i = 0; i < instances.Count; i++)
        {
            if (instances[i].Definition == definition)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Recombines every instance: multipliers multiply per stack, resistance deltas add per stack, and controls and
    /// granted immunities are unioned.
    /// </summary>
    private void Recompute()
    {
        float moveSpeed = 1f;
        float dealt = 1f;
        float taken = 1f;
        StatusControls controls = StatusControls.None;
        DamageTypeMask damageImmunity = DamageTypeMask.None;
        StatusTags statusImmunity = StatusTags.None;
        Array.Clear(resistanceDeltas, 0, resistanceDeltas.Length);

        for (int i = 0; i < instances.Count; i++)
        {
            StatusEffectDefinition definition = instances[i].Definition;
            int stacks = instances[i].Stacks;
            moveSpeed *= Mathf.Pow(definition.MoveSpeedMultiplier, stacks);
            dealt *= Mathf.Pow(definition.DamageDealtMultiplier, stacks);
            controls |= definition.Controls;
            taken *= Mathf.Pow(definition.DamageTakenMultiplier, stacks);
            damageImmunity |= definition.GrantsDamageImmunity;
            statusImmunity |= definition.GrantsStatusImmunity;
            for (int type = 0; type < DamageTypeCount; type++)
            {
                resistanceDeltas[type] += definition.GetResistanceDelta((DamageType)type) * stacks;
            }
        }

        MoveSpeedMultiplier = moveSpeed;
        DamageDealtMultiplier = dealt;
        Controls = controls;
        DamageTakenMultiplier = taken;
        GrantedDamageImmunity = damageImmunity;
        GrantedStatusImmunity = statusImmunity;
    }

    /// <summary>
    /// One active status: its definition, id, stacks, remaining time, time to its next periodic tick, and the source
    /// and dealt multiplier its ticks use.
    /// </summary>
    private struct Instance
    {
        public StatusEffectDefinition Definition;
        public int Id;
        public int Stacks;
        public float Remaining;
        public float TickTimer;
        public GameObject Source;
        public float DealtMultiplier;
    }
}
