using UnityEngine;

/// <summary>
/// Builds combat services for tests with the default <see cref="CombatSettings"/>.
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
}
