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
    /// Raised on the authoritative machine when a status was blocked by an immunity.
    /// </summary>
    public event Action<StatusEffectDefinition> Blocked;

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

    public StatusSnapshot GetSnapshot(int index)
    {
        return set.GetSnapshot(index);
    }

    public int GetStacks(StatusEffectDefinition definition)
    {
        return set.GetStacks(definition);
    }

    internal void SetHold(IStatusHold statusHold)
    {
        hold = statusHold;
    }

    internal void NotifyBlocked(StatusEffectDefinition definition)
    {
        Blocked?.Invoke(definition);
    }
}
