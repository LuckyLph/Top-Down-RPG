using System;
using UnityEngine;

/// <summary>
/// The status effects on a player or mob. Owns the unit's <see cref="StatusEffectSet"/>, which
/// <see cref="StatusEffectService"/> changes and ticks on the authoritative machine and
/// <see cref="NetworkStatusEffects"/> mirrors on clients.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(DamageReceiver))]
public class StatusEffects : MonoBehaviour
{
    private readonly StatusEffectSet set = new();

    private IStatusHold hold;

    /// <summary>
    /// Raised whenever the active statuses change: landed, stacked, refreshed, expired, removed or synced.
    /// </summary>
    public event Action Changed
    {
        add => set.Changed += value;
        remove => set.Changed -= value;
    }

    /// <summary>
    /// Raised on the authoritative machine when a status was blocked by an immunity, with the status's source.
    /// </summary>
    public event Action<StatusEffectDefinition, GameObject> Blocked;

    public StatusEffectSet Set => set;
    public int Count => set.Count;
    public float DamageDealtMultiplier => set.DamageDealtMultiplier;
    public float DamageTakenMultiplier => set.DamageTakenMultiplier;
    public StatusControls Controls => set.Controls;
    public float MoveSpeedMultiplier => set.MoveSpeedMultiplier;
    public bool IsHeld => hold != null && hold.IsHeld;

    /// <summary>
    /// The factor to apply to the unit's move speed: 0 while stunned or rooted, otherwise the combined slow.
    /// </summary>
    public float MovementScale => (set.Controls & (StatusControls.Stun | StatusControls.Root)) != 0 ? 0f : set.MoveSpeedMultiplier;

    /// <summary>
    /// A read-only copy of the active instance at <paramref name="index"/> (0 to <see cref="Count"/> - 1).
    /// </summary>
    public StatusSnapshot GetSnapshot(int index)
    {
        return set.GetSnapshot(index);
    }

    /// <summary>
    /// Stacks of <paramref name="definition"/> on this unit, 0 when it is not active.
    /// </summary>
    public int GetStacks(StatusEffectDefinition definition)
    {
        return set.GetStacks(definition);
    }

    /// <summary>
    /// The longest remaining time of <paramref name="definition"/> on this unit, 0 when it is not active.
    /// </summary>
    public float GetRemaining(StatusEffectDefinition definition)
    {
        return set.GetRemaining(definition);
    }

    /// <summary>
    /// Sets what decides whether these statuses are held still (null for never); the host's
    /// <see cref="NetworkStatusEffects"/> sets itself while spawned.
    /// </summary>
    internal void SetHold(IStatusHold statusHold)
    {
        hold = statusHold;
    }

    /// <summary>
    /// Raises <see cref="Blocked"/>; called by <see cref="StatusEffectService"/> when an immunity blocks a status.
    /// </summary>
    internal void NotifyBlocked(StatusEffectDefinition definition, GameObject source)
    {
        Blocked?.Invoke(definition, source);
    }
}
