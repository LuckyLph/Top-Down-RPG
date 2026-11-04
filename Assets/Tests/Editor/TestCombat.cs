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
    /// on a fresh <see cref="ManualClock"/> that stands still, so time only passes when a test advances it directly,
    /// holding the units <paramref name="protection"/> keeps out of play (none by default).
    /// </summary>
    public static StatusEffectService CreateStatusService(DamageService damageService, IGameAuthority authority, CombatEvents combatEvents, IClock clock = null, IUnitProtection protection = null)
    {
        return new StatusEffectService(damageService, combatEvents, authority, clock ?? new ManualClock(), protection ?? new FakeUnitProtection());
    }

    /// <summary>
    /// Creates an ability service with its own damage, status and hit services, all sharing
    /// <paramref name="combatEvents"/> or fresh events.
    /// </summary>
    public static AbilityService CreateAbilityService(IGameAuthority authority, CombatEvents combatEvents = null)
    {
        combatEvents ??= new CombatEvents();
        DamageService damageService = CreateDamageService(authority, combatEvents);
        HitService hitService = new(damageService, CreateStatusService(damageService, authority, combatEvents), new FakeUnitProtection());
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
        return new HitService(damageService, CreateStatusService(damageService, authority, combatEvents), new FakeUnitProtection());
    }

    /// <summary>
    /// Creates an attack service over its own hit service under <paramref name="authority"/>, sharing
    /// <paramref name="combatEvents"/> or fresh events.
    /// </summary>
    public static AttackService CreateAttackService(IGameAuthority authority, CombatEvents combatEvents = null)
    {
        return new AttackService(authority, CreateHitService(authority, combatEvents), Settings);
    }

    /// <summary>
    /// Creates an attack service over <paramref name="hits"/> under <paramref name="authority"/>.
    /// </summary>
    public static AttackService CreateAttackService(IGameAuthority authority, HitService hits)
    {
        return new AttackService(authority, hits, Settings);
    }

    /// <summary>
    /// Gives <paramref name="unit"/> a kinematic body and a unit-sized collider and stands it
    /// <paramref name="distance"/> to the right of the origin, inside the attack area of a mob's melee dealer standing
    /// there without a collider of its own.
    /// </summary>
    public static void StandInReach(Component unit, float distance = 0.6f)
    {
        unit.transform.position = new Vector3(distance, 0f, 0f);
        if (!unit.TryGetComponent(out BoxCollider2D _))
        {
            unit.gameObject.AddComponent<BoxCollider2D>();
        }

        if (!unit.TryGetComponent(out Rigidbody2D body))
        {
            body = unit.gameObject.AddComponent<Rigidbody2D>();
        }

        body.bodyType = RigidbodyType2D.Kinematic;
        Physics2D.SyncTransforms();
    }

    /// <summary>
    /// Starts <paramref name="dealer"/>'s attack on <paramref name="target"/>, then plays its windup on
    /// <paramref name="clock"/> and ticks it, so its contact lands. Returns whether the attack started.
    /// </summary>
    public static bool Strike(MeleeDamageDealer dealer, ManualClock clock, Transform target)
    {
        if (!dealer.TryStartAttack(target))
        {
            return false;
        }

        clock.Advance(dealer.Timeline.Windup);
        dealer.Tick();
        return true;
    }
}
