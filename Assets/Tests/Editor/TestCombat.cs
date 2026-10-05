using UnityEngine;

/// <summary>
/// Builds combat and status services for tests with the default <see cref="CombatSettings"/>.
/// </summary>
internal static class TestCombat
{
    private static CombatSettings settings;

    public static CombatSettings Settings
    {
        get
        {
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<CombatSettings>();
                settings.hideFlags = HideFlags.HideAndDontSave;
            }

            return settings;
        }
    }

    /// <summary>
    /// Creates a damage service with the given authority and the shared default settings, publishing on
    /// <paramref name="combatEvents"/> or on fresh events when none are given.
    /// </summary>
    public static DamageService CreateDamageService(IGameAuthority authority, CombatEvents combatEvents = null)
    {
        return new DamageService(combatEvents ?? new CombatEvents(), authority, Settings);
    }

    /// <summary>
    /// Creates a status service over <paramref name="damageService"/> that ticks on <paramref name="clock"/>, or
    /// on a fresh <see cref="ManualClock"/> that stands still, so time only passes when a test advances it directly.
    /// </summary>
    public static StatusEffectService CreateStatusService(DamageService damageService, IGameAuthority authority, CombatEvents combatEvents, IClock clock = null)
    {
        return new StatusEffectService(damageService, combatEvents, authority, clock ?? new ManualClock());
    }

    /// <summary>
    /// Creates an ability service with its own damage, status and hit services, all sharing
    /// <paramref name="combatEvents"/> or fresh events.
    /// </summary>
    public static AbilityService CreateAbilityService(IGameAuthority authority, CombatEvents combatEvents = null)
    {
        combatEvents ??= new CombatEvents();
        DamageService damageService = CreateDamageService(authority, combatEvents);
        HitService hitService = new(damageService, CreateStatusService(damageService, authority, combatEvents));
        return new AbilityService(authority, damageService, hitService, Settings);
    }

    /// <summary>
    /// Creates a hit service with its own damage and status services, both sharing <paramref name="combatEvents"/>
    /// or fresh events.
    /// </summary>
    public static HitService CreateHitService(IGameAuthority authority, CombatEvents combatEvents = null)
    {
        combatEvents ??= new CombatEvents();
        DamageService damageService = CreateDamageService(authority, combatEvents);
        return new HitService(damageService, CreateStatusService(damageService, authority, combatEvents));
    }
}
