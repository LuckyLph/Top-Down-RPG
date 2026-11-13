using System.Globalization;

/// <summary>
/// The numbers a tooltip shows under its title (Docs/UiRevamp.md, Behaviour: The command bar), read from the
/// definitions: an ability's cooldown and cast time, then its range on a second line when it is aimed at a point, a
/// unit or along a line of some length; a weapon's damage and type, and its range, or melee. Built when a slot shows
/// another ability or weapon, never per frame.
/// </summary>
public static class TooltipDetails
{
    private const string Separator = " · ";

    /// <summary>
    /// The numbers for <paramref name="ability"/>, such as "Cooldown 2 s · Cast 0.25 s" and "Range 6" under it; null
    /// for none.
    /// </summary>
    public static string For(AbilityDefinition ability)
    {
        if (ability == null)
        {
            return null;
        }

        string cast = ability.CastTime > 0f ? $"Cast {Number(ability.CastTime)} s" : "Instant";
        string timing = $"Cooldown {Number(ability.Cooldown)} s{Separator}{cast}";
        return HasRange(ability) ? $"{timing}\nRange {Number(ability.Range)}" : timing;
    }

    /// <summary>
    /// The numbers for <paramref name="weapon"/>, such as "24 Physical · Range 5", or "20 Physical · Melee" for a weapon
    /// that strikes; null for none.
    /// </summary>
    public static string For(PlayerWeapon weapon)
    {
        if (weapon == null)
        {
            return null;
        }

        string range = weapon.FiresProjectile ? $"Range {Number(weapon.AttackRange)}" : "Melee";
        return $"{weapon.Damage} {weapon.DamageType}{Separator}{range}";
    }

    /// <summary>
    /// Whether <paramref name="ability"/>'s range means something to the player: a point or a unit it is aimed at, or a
    /// line of some length.
    /// </summary>
    private static bool HasRange(AbilityDefinition ability)
    {
        return ability.Targeting switch
        {
            AbilityTargeting.Point => true,
            AbilityTargeting.Unit => true,
            AbilityTargeting.Direction => ability.Range > 0f,
            _ => false
        };
    }

    /// <summary>
    /// <paramref name="value"/> with up to two decimals and no trailing zeros, in the invariant culture.
    /// </summary>
    private static string Number(float value)
    {
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
