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

    public static DamageService CreateDamageService(IGameAuthority authority, CombatEvents combatEvents = null)
    {
        return new DamageService(combatEvents ?? new CombatEvents(), authority, Settings);
    }

    public static StatusEffectService CreateStatusService(DamageService damageService, IGameAuthority authority, CombatEvents combatEvents, IClock clock = null)
    {
        return new StatusEffectService(damageService, combatEvents, authority, clock ?? new ManualClock());
    }

    public static AbilityService CreateAbilityService(IGameAuthority authority, CombatEvents combatEvents = null)
    {
        combatEvents ??= new CombatEvents();
        DamageService damageService = CreateDamageService(authority, combatEvents);
        HitService hitService = new(damageService, CreateStatusService(damageService, authority, combatEvents));
        return new AbilityService(authority, damageService, hitService);
    }

    public static HitService CreateHitService(IGameAuthority authority, CombatEvents combatEvents = null)
    {
        combatEvents ??= new CombatEvents();
        DamageService damageService = CreateDamageService(authority, combatEvents);
        return new HitService(damageService, CreateStatusService(damageService, authority, combatEvents));
    }
}
