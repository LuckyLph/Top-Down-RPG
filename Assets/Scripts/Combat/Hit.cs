using System;
using UnityEngine;

/// <summary>
/// What a swing, attack or ability deals: an amount of one damage type and the statuses it applies to a target
/// that survives it.
/// </summary>
[Serializable]
public struct Hit
{
    [SerializeField, Min(0)] private int amount;
    [SerializeField] private DamageType type;
    [SerializeField] private StatusEffectDefinition[] statuses;

    /// <summary>
    /// Creates a hit dealing <paramref name="amount"/> <paramref name="type"/> damage and applying
    /// <paramref name="statuses"/> (none when null).
    /// </summary>
    public Hit(int amount, DamageType type, StatusEffectDefinition[] statuses = null)
    {
        this.amount = amount;
        this.type = type;
        this.statuses = statuses;
    }

    public int Amount => amount;
    public DamageType Type => type;
    public int StatusCount => statuses != null ? statuses.Length : 0;

    /// <summary>
    /// The status at <paramref name="index"/>, from 0 to <see cref="StatusCount"/> - 1; may be null for an empty
    /// inspector entry.
    /// </summary>
    public StatusEffectDefinition GetStatus(int index)
    {
        return statuses[index];
    }
}
