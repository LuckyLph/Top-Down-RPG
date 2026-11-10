using System;
using UnityEngine;

/// <summary>
/// The derived columns of the design tables (Docs/DesignSystem.md, Generated tables): attack periods, hits and time to
/// kill, periodic totals, hits resolved against a profile, and the contrast ratio of two colours.
/// </summary>
public static class DesignMath
{
    private const float TickEpsilon = 0.0001f;

    /// <summary>
    /// Seconds from one attack's start to the next one's at attack speed 1: the cooldown, or the time until the attack
    /// may be cut short when that is longer, the end of its contact when orders may end its recovery and the end of its
    /// timeline otherwise.
    /// </summary>
    public static float AttackPeriod(float cooldown, in AttackTimeline timeline, bool recoveryCancellable)
    {
        float busy = recoveryCancellable ? timeline.Windup + timeline.Contact : timeline.Duration;
        return Mathf.Max(cooldown, busy);
    }

    /// <summary>
    /// How many hits of <paramref name="damagePerHit"/> take <paramref name="health"/> to 0; 0 when a hit deals
    /// nothing.
    /// </summary>
    public static int HitsToKill(int health, int damagePerHit)
    {
        return damagePerHit > 0 ? (health + damagePerHit - 1) / damagePerHit : 0;
    }

    /// <summary>
    /// Seconds from the first attack's start to the killing hit: every period but the last, then the windup to its
    /// contact.
    /// </summary>
    public static float TimeToKill(int hits, float period, float windup)
    {
        return hits > 0 ? (hits - 1) * period + windup : 0f;
    }

    /// <summary>
    /// How many times a status of <paramref name="duration"/> ticks every <paramref name="interval"/> seconds: one
    /// interval after it lands, then every interval, the instant it expires included, as <see cref="StatusEffectSet"/>
    /// ticks it.
    /// </summary>
    public static int PeriodicTicks(float duration, float interval)
    {
        return interval > 0f ? Mathf.FloorToInt((duration + TickEpsilon) / interval) : 0;
    }

    /// <summary>
    /// <paramref name="amount"/> of <paramref name="type"/> damage dealt at a multiplier of 1 against
    /// <paramref name="profile"/>'s base defence (no statuses), through <see cref="DamageMath.Resolve"/>.
    /// </summary>
    public static DamageResult Resolve(int amount, DamageType type, CombatProfile profile, CombatSettings settings)
    {
        DefenseSnapshot defense = profile != null
            ? new DefenseSnapshot(profile.GetResistance(type), profile.DamageImmunities.Includes(type), 1f)
            : DefenseSnapshot.None;
        return DamageMath.Resolve(amount, type, 1f, defense, settings);
    }

    /// <summary>
    /// The contrast ratio of two opaque sRGB colours, from 1 to 21 (WCAG 2's relative luminance).
    /// </summary>
    public static double ContrastRatio(Color a, Color b)
    {
        double lighter = Math.Max(RelativeLuminance(a), RelativeLuminance(b));
        double darker = Math.Min(RelativeLuminance(a), RelativeLuminance(b));
        return (lighter + 0.05) / (darker + 0.05);
    }

    /// <summary>
    /// The relative luminance of an sRGB colour, from 0 for black to 1 for white.
    /// </summary>
    public static double RelativeLuminance(Color colour)
    {
        return 0.2126 * Linear(colour.r) + 0.7152 * Linear(colour.g) + 0.0722 * Linear(colour.b);
    }

    /// <summary>
    /// One sRGB channel in linear light.
    /// </summary>
    private static double Linear(float channel)
    {
        double c = Mathf.Clamp01(channel);
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }
}
