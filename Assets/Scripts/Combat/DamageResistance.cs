using System;
using UnityEngine;

/// <summary>
/// A resistance percentage against one damage type. Negative values are weaknesses.
/// </summary>
[Serializable]
public struct DamageResistance
{
    [SerializeField] private DamageType type;
    [SerializeField, Range(-100, 100)] private int percent;

    public DamageResistance(DamageType type, int percent)
    {
        this.type = type;
        this.percent = percent;
    }

    public DamageType Type => type;
    public int Percent => percent;
}
