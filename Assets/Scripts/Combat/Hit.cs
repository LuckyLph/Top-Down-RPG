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

    public Hit(int amount, DamageType type, StatusEffectDefinition[] statuses = null)
    {
        this.amount = amount;
        this.type = type;
        this.statuses = statuses;
    }

    public int Amount => amount;
    public DamageType Type => type;
    public int StatusCount => statuses != null ? statuses.Length : 0;

    public StatusEffectDefinition GetStatus(int index)
    {
        return statuses[index];
    }
}
