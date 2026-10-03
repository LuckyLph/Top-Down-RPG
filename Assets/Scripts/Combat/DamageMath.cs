using System;
using UnityEngine;

/// <summary>
/// The arithmetic of resolving a hit: immunity, clamped resistance, multipliers, rounding and the minimum.
/// </summary>
public static class DamageMath
{
    /// <summary>
    /// Resolves <paramref name="rawAmount"/> of <paramref name="type"/> damage dealt with
    /// <paramref name="dealtMultiplier"/> against <paramref name="defense"/>. Returns <c>default</c> for a
    /// non-positive amount.
    /// </summary>
    public static DamageResult Resolve(
        int rawAmount,
        DamageType type,
        float dealtMultiplier,
        in DefenseSnapshot defense,
        CombatSettings settings,
        DamageFlags flags = DamageFlags.None)
    {
        if (rawAmount <= 0)
        {
            return default;
        }

        if (defense.Immune)
        {
            return new DamageResult(type, rawAmount, 0, flags | DamageFlags.Immune);
        }

        int resistance = type == DamageType.True
            ? 0
            : Mathf.Clamp(defense.ResistancePercent, settings.ResistanceFloor, settings.ResistanceCap);
        if (resistance > 0)
        {
            flags |= DamageFlags.Resisted;
        }
        else if (resistance < 0)
        {
            flags |= DamageFlags.Weakness;
        }

        double scaled = rawAmount
            * Math.Max(0.0, dealtMultiplier)
            * (1.0 - resistance / 100.0)
            * Math.Max(0.0, defense.DamageTakenMultiplier);
        int amount = Math.Max(settings.MinimumDamage, (int)Math.Round(scaled, MidpointRounding.AwayFromZero));
        return new DamageResult(type, rawAmount, amount, flags);
    }
}
