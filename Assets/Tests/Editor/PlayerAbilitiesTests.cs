using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VContainer;

/// <summary>
/// Tests for <see cref="PlayerAbilities"/>: slots from the class and weapon, casting and its failures, completion
/// and cancel, remote casts under authority, recent cast history, cooldowns, unit filters and the shipped blanks.
/// </summary>
public class PlayerAbilitiesTests
{
    private readonly List<Object> createdObjects = new();
    private ManualClock clock;
    private AbilityService service;
    private int applied;
    private int resolved;
    private PlayerAbilities abilities;
    private PlayerWeaponController weapon;
    private Health health;
    private AbilityDefinition[] classAbilities;
    private AbilityDefinition[] weaponAbilities;

    /// <summary>
    /// Creates a manual clock, three class abilities (the first with a 2-second cooldown) and an empty fourth
    /// slot, two weapon abilities, and an authoritative player using them.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        clock = new ManualClock();
        applied = 0;
        resolved = 0;
        classAbilities = new[] { Ability("Q", cooldown: 2f), Ability("W"), Ability("E"), null };
        weaponAbilities = new[] { Ability("A"), Ability("S") };
        abilities = CreatePlayer(FixedGameAuthority.Authoritative);
    }

    /// <summary>
    /// Destroys every object and asset the test tracked.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        foreach (Object created in createdObjects)
        {
            if (created != null)
            {
                Object.DestroyImmediate(created);
            }
        }

        createdObjects.Clear();
    }

    /// <summary>
    /// Slots 0 to 3 hold the class abilities and slots 4 and 5 the weapon's; slots outside the bar are empty.
    /// </summary>
    [Test]
    public void Slots_ComeFromTheClassThenTheWeapon()
    {
        for (int i = 0; i < PlayerClass.AbilityCount; i++)
        {
            Assert.That(abilities.GetAbility(i), Is.SameAs(classAbilities[i]));
        }

        Assert.That(abilities.GetAbility(4), Is.SameAs(weaponAbilities[0]));
        Assert.That(abilities.GetAbility(5), Is.SameAs(weaponAbilities[1]));
        Assert.That(abilities.GetAbility(6), Is.Null);
        Assert.That(abilities.GetAbility(-1), Is.Null);
    }

    /// <summary>
    /// Equipping another weapon raises SlotsChanged once and replaces only the weapon slots.
    /// </summary>
    [Test]
    public void EquippingAnotherWeapon_RebuildsItsSlots()
    {
        AbilityDefinition other = Ability("Other");
        int changes = 0;
        abilities.SlotsChanged += () => changes++;

        weapon.Equip(Track(PlayerWeapon.Create("Club", 1, 0.5f, 0.5f, new Vector2[4], weaponAbilities: new[] { other, null })));

        Assert.That(changes, Is.EqualTo(1));
        Assert.That(abilities.GetAbility(4), Is.SameAs(other));
        Assert.That(abilities.GetAbility(5), Is.Null);
        Assert.That(abilities.GetAbility(0), Is.SameAs(classAbilities[0]));
    }

    /// <summary>
    /// A successful cast raises CastStarted with its slot, ability and aim, starts the cooldown, puts the player
    /// in the casting state and is applied once.
    /// </summary>
    [Test]
    public void TryCast_StartsTheCooldown_RaisesCastStarted_AndAppliesOnce()
    {
        AbilityCast? started = null;
        abilities.CastStarted += cast => started = cast;
        CastAim aim = new(new Vector2(1f, 2f), Vector2.right, default);

        Assert.That(abilities.TryCast(0, aim), Is.EqualTo(CastOutcome.Started));

        Assert.That(started.HasValue, Is.True);
        Assert.That(started.Value.Slot, Is.EqualTo(0));
        Assert.That(started.Value.Ability, Is.SameAs(classAbilities[0]));
        Assert.That(started.Value.Aim.Point, Is.EqualTo(new Vector2(1f, 2f)));
        Assert.That(abilities.RemainingCooldown(0), Is.EqualTo(2f));
        Assert.That(abilities.IsCasting, Is.True);
        Assert.That(applied, Is.EqualTo(1));
    }

    /// <summary>
    /// Casting an empty slot, an ability on cooldown, during another cast or while dead raises CastFailed with the
    /// matching outcome, without starting, applying or putting anything on cooldown.
    /// </summary>
    [Test]
    public void FailedCasts_ReportWhy_WithoutSideEffects()
    {
        List<(int, CastOutcome)> failures = new();
        int started = 0;
        abilities.CastFailed += (slot, reason) => failures.Add((slot, reason));
        abilities.CastStarted += _ => started++;

        Assert.That(abilities.TryCast(3, default), Is.EqualTo(CastOutcome.EmptySlot));

        abilities.TryCast(0, default);
        abilities.CompleteCast(0);
        clock.Advance(1f);
        Assert.That(abilities.TryCast(0, default), Is.EqualTo(CastOutcome.OnCooldown));

        abilities.TryCast(1, default);
        Assert.That(abilities.TryCast(2, default), Is.EqualTo(CastOutcome.CastInProgress));
        abilities.CompleteCast(1);

        health.ApplyDamage(health.MaxHealth);
        Assert.That(abilities.TryCast(4, default), Is.EqualTo(CastOutcome.Dead));

        Assert.That(failures, Is.EqualTo(new[] { (3, CastOutcome.EmptySlot), (0, CastOutcome.OnCooldown), (2, CastOutcome.CastInProgress), (4, CastOutcome.Dead) }));
        Assert.That(started, Is.EqualTo(2));
        Assert.That(applied, Is.EqualTo(2));
        Assert.That(abilities.RemainingCooldown(2), Is.Zero, "A failed cast must not start a cooldown.");
        Assert.That(abilities.RemainingCooldown(4), Is.Zero);
    }

    /// <summary>
    /// Completing or cancelling only acts on the slot that is casting, once; a completed cast keeps its aim and
    /// resolves its effect, while a cancelled one ends without completing or resolving.
    /// </summary>
    [Test]
    public void CompleteAndCancel_EndOnlyTheRunningSlot_AndOnlyACompletedCastResolves()
    {
        List<int> ended = new();
        List<AbilityCast> completed = new();
        abilities.CastEnded += ended.Add;
        abilities.CastCompleted += completed.Add;
        abilities.TryCast(1, new CastAim(Vector2.one, Vector2.up, default));

        abilities.CompleteCast(0);
        abilities.CompleteCast(1);
        abilities.CompleteCast(1);

        Assert.That(ended, Is.EqualTo(new[] { 1 }));
        Assert.That(completed.Count, Is.EqualTo(1));
        Assert.That(completed[0].Aim.Point, Is.EqualTo(Vector2.one), "The completed cast keeps the aim it started with.");
        Assert.That(resolved, Is.EqualTo(1));
        Assert.That(abilities.IsCasting, Is.False);

        clock.Advance(10f);
        abilities.TryCast(1, default);
        abilities.CancelCast(1);
        abilities.CancelCast(1);
        Assert.That(ended, Is.EqualTo(new[] { 1, 1 }));
        Assert.That(completed.Count, Is.EqualTo(1), "A cancelled cast does not complete.");
        Assert.That(resolved, Is.EqualTo(1), "Nor resolve its effect.");
        Assert.That(abilities.IsCasting, Is.False);
    }

    /// <summary>
    /// On the authority, a remote completion resolves a unit cast whose target resolved here, but not one without
    /// a target, an empty slot or a caster seen as dead; resolving never counts as applying a cast start.
    /// </summary>
    [Test]
    public void ResolveRemoteCast_ResolvesOnTheAuthority_UnderThePlayRemoteCastRules()
    {
        abilities.Configure(Track(PlayerClass.Create("Unit", Track(AbilityDefinition.Create("Mend", targeting: AbilityTargeting.Unit, unitFilter: AbilityUnitFilter.Ally)))));
        Health target = Track(new GameObject("Target")).AddComponent<Health>();
        CastAim aimed = new(Vector2.zero, Vector2.up, new UnitTarget(target, null, UnitTeam.Ally));

        abilities.ResolveRemoteCast(0, default, Vector2.zero);
        Assert.That(resolved, Is.Zero, "The target did not resolve on this machine.");
        abilities.ResolveRemoteCast(3, aimed, Vector2.zero);
        Assert.That(resolved, Is.Zero, "An empty slot.");

        abilities.ResolveRemoteCast(0, aimed, Vector2.zero);
        Assert.That(resolved, Is.EqualTo(1));
        Assert.That(applied, Is.Zero, "Resolving is not starting.");

        health.ApplyDamage(health.MaxHealth);
        abilities.ResolveRemoteCast(0, aimed, Vector2.zero);
        Assert.That(resolved, Is.EqualTo(1), "This machine sees the caster as dead.");
    }

    /// <summary>
    /// On a machine without authority a cast still starts locally, but the ability service does not apply it.
    /// </summary>
    [Test]
    public void WithoutAuthority_CastsRunLocally_ButAreNotApplied()
    {
        PlayerAbilities client = CreatePlayer(new FixedGameAuthority(false));

        Assert.That(client.TryCast(0, default), Is.EqualTo(CastOutcome.Started));
        Assert.That(applied, Is.Zero);
    }

    /// <summary>
    /// On the authority, a replayed remote cast is applied without raising events, starting a cooldown or entering
    /// the casting state, and an empty slot is not applied.
    /// </summary>
    [Test]
    public void PlayRemoteCast_IsAppliedByTheAuthority_WithoutCooldownOrEvents()
    {
        int started = 0;
        abilities.CastStarted += _ => started++;

        abilities.PlayRemoteCast(0, default);
        abilities.PlayRemoteCast(3, default);

        Assert.That(applied, Is.EqualTo(1), "An empty slot is not applied.");
        Assert.That(started, Is.Zero);
        Assert.That(abilities.RemainingCooldown(0), Is.Zero);
        Assert.That(abilities.IsCasting, Is.False);
    }

    /// <summary>
    /// Through <see cref="PlayerOrders"/>, clearing the orders on death ends a running cast, and the next press is
    /// blocked only by the ability's cooldown, never by a leaked cast.
    /// </summary>
    [Test]
    public void ThroughTheOrders_DeathMidCastEndsTheCast_AndTheNextCastWaitsOnlyForItsCooldown()
    {
        abilities.Configure(Track(PlayerClass.Create("Casting", Track(AbilityDefinition.Create("Slow", cooldown: 2f, castTime: 0.5f)))));
        PlayerControlSettings settings = Track(TestPlayerControlSettings.Create());
        PlayerOrders orders = new(settings, new PlayerUnitQueries(null), abilities);
        List<CastOutcome> failures = new();
        int ended = 0;
        abilities.CastFailed += (_, reason) => failures.Add(reason);
        abilities.CastEnded += _ => ended++;
        PlayerCommand press = new(Vector2.right, default, false, false, false, abilitySlot: 0);

        orders.Tick(press, new PlayerOrderContext(Vector2.zero, clock.Time, true, 0f, 0.3f));
        Assert.That(orders.Current, Is.EqualTo(PlayerOrderKind.Casting));
        Assert.That(abilities.IsCasting, Is.True);

        orders.Clear();
        Assert.That(abilities.IsCasting, Is.False, "Death ends the running cast.");
        Assert.That(ended, Is.EqualTo(1));

        clock.Advance(1f);
        orders.Tick(press, new PlayerOrderContext(Vector2.zero, clock.Time, true, 0f, 0.3f));
        Assert.That(failures, Is.EqualTo(new[] { CastOutcome.OnCooldown }), "Only the cooldown blocks the next cast, never a leaked cast.");

        clock.Advance(1f);
        orders.Tick(press, new PlayerOrderContext(Vector2.zero, clock.Time, true, 0f, 0.3f));
        Assert.That(orders.Current, Is.EqualTo(PlayerOrderKind.Casting));
        Assert.That(applied, Is.EqualTo(2));
    }

    /// <summary>
    /// A replayed unit cast is ignored when its target did not resolve here or has died, or when the caster is
    /// seen as dead.
    /// </summary>
    [Test]
    public void PlayRemoteCast_IgnoresADeadCaster_AndUnitCastsWithoutALivingTarget()
    {
        abilities.Configure(Track(PlayerClass.Create("Unit", Track(AbilityDefinition.Create("Mend", targeting: AbilityTargeting.Unit, unitFilter: AbilityUnitFilter.Ally)))));
        Health target = Track(new GameObject("Target")).AddComponent<Health>();
        CastAim aimed = new(Vector2.zero, Vector2.up, new UnitTarget(target, null, UnitTeam.Ally));

        abilities.PlayRemoteCast(0, default);
        Assert.That(applied, Is.Zero, "The target did not resolve on this machine.");

        abilities.PlayRemoteCast(0, aimed);
        Assert.That(applied, Is.EqualTo(1));

        target.ApplyDamage(target.MaxHealth);
        abilities.PlayRemoteCast(0, aimed);
        Assert.That(applied, Is.EqualTo(1), "The target died before the cast arrived.");

        target.Restore();
        health.ApplyDamage(health.MaxHealth);
        abilities.PlayRemoteCast(0, aimed);
        Assert.That(applied, Is.EqualTo(1), "This machine sees the caster as dead.");
    }

    /// <summary>
    /// Local and remote casts are recorded newest first for the gizmos, remote casts of empty slots are skipped,
    /// and only the latest eight are kept.
    /// </summary>
    [Test]
    public void RecentCasts_RecordLocalAndRemoteCastsNewestFirst_ForTheGizmos()
    {
        Assert.That(abilities.RecentCastCount, Is.Zero);

        abilities.TryCast(0, new CastAim(Vector2.one, Vector2.up, default));
        abilities.CompleteCast(0);
        abilities.PlayRemoteCast(1, default);
        abilities.PlayRemoteCast(3, default);

        Assert.That(abilities.TryGetRecentCast(0, out AbilityCast newest, out bool newestRemote), Is.True);
        Assert.That(newest.Slot, Is.EqualTo(1));
        Assert.That(newestRemote, Is.True);
        Assert.That(abilities.TryGetRecentCast(1, out AbilityCast older, out bool olderRemote), Is.True);
        Assert.That(older.Slot, Is.EqualTo(0));
        Assert.That(older.Aim.Point, Is.EqualTo(Vector2.one));
        Assert.That(olderRemote, Is.False);
        Assert.That(abilities.TryGetRecentCast(2, out _, out _), Is.False, "An empty slot's remote cast is not recorded.");

        for (int i = 0; i < 20; i++)
        {
            abilities.PlayRemoteCast(2, default);
        }

        Assert.That(abilities.RecentCastCount, Is.EqualTo(8), "Only the latest casts are kept.");
        Assert.That(abilities.TryGetRecentCast(-1, out _, out _), Is.False);
        Assert.That(abilities.TryGetRecentCast(8, out _, out _), Is.False);
    }

    /// <summary>
    /// <see cref="AbilityCooldowns"/> keeps a slot unready, with the time left, until its cooldown ends, without
    /// affecting other slots.
    /// </summary>
    [Test]
    public void Cooldowns_TrackReadinessAndRemainingTime()
    {
        AbilityCooldowns cooldowns = new(2);
        Assert.That(cooldowns.IsReady(0, 0f), Is.True);

        cooldowns.Start(0, 1f, 3f);

        Assert.That(cooldowns.IsReady(0, 3.9f), Is.False);
        Assert.That(cooldowns.Remaining(0, 3f), Is.EqualTo(1f));
        Assert.That(cooldowns.IsReady(0, 4f), Is.True);
        Assert.That(cooldowns.Remaining(0, 5f), Is.Zero);
        Assert.That(cooldowns.IsReady(1, 0f), Is.True);
    }

    /// <summary>
    /// Enemy, ally and any unit filters accept exactly their teams, and none accepts a unit with no team.
    /// </summary>
    [Test]
    public void UnitFilters_AcceptTheRightTeams()
    {
        AbilityDefinition enemy = Ability("Enemy", unitFilter: AbilityUnitFilter.Enemy);
        AbilityDefinition ally = Ability("Ally", unitFilter: AbilityUnitFilter.Ally);
        AbilityDefinition any = Ability("Any", unitFilter: AbilityUnitFilter.Any);

        Assert.That(enemy.Accepts(UnitTeam.Enemy) && !enemy.Accepts(UnitTeam.Ally), Is.True);
        Assert.That(ally.Accepts(UnitTeam.Ally) && !ally.Accepts(UnitTeam.Enemy), Is.True);
        Assert.That(any.Accepts(UnitTeam.Ally) && any.Accepts(UnitTeam.Enemy), Is.True);
        Assert.That(enemy.Accepts(UnitTeam.None) || ally.Accepts(UnitTeam.None) || any.Accepts(UnitTeam.None), Is.False);
    }

    /// <summary>
    /// The placeholder class and sword blank abilities are all set and distinct, and between them cover every
    /// targeting mode and cast movement, varied cooldowns and cast times, an instant and a bufferable cast, and
    /// ally and enemy unit targeting.
    /// </summary>
    [Test]
    public void Blanks_CoverEveryBranchOfThePipeline()
    {
        PlayerClass placeholder = AssetDatabase.LoadAssetAtPath<PlayerClass>("Assets/Data/Classes/Class_Placeholder.asset");
        PlayerWeapon sword = AssetDatabase.LoadAssetAtPath<PlayerWeapon>("Assets/Data/Weapons/Sword.asset");
        Assert.That(placeholder, Is.Not.Null);
        Assert.That(sword, Is.Not.Null);

        AbilityDefinition[] blanks = Enumerable.Range(0, PlayerClass.AbilityCount).Select(placeholder.GetAbility)
            .Concat(Enumerable.Range(0, PlayerWeapon.AbilityCount).Select(sword.GetAbility))
            .ToArray();

        Assert.That(blanks, Has.None.Null);
        Assert.That(blanks, Is.Unique);
        Assert.That(blanks.Select(blank => blank.Cooldown), Is.Unique);
        Assert.That(blanks.Select(blank => blank.CastTime).Distinct().Count(), Is.GreaterThanOrEqualTo(4));
        Assert.That(blanks.Any(blank => blank.CastTime == 0f), Is.True, "One blank should be instant.");
        Assert.That(blanks.Select(blank => blank.Targeting).Distinct().Count(), Is.EqualTo(4));
        Assert.That(blanks.Select(blank => blank.CastMovement).Distinct().Count(), Is.EqualTo(3));
        Assert.That(blanks.Any(blank => blank.Bufferable), Is.True);
        Assert.That(blanks.Any(blank => blank.Targeting == AbilityTargeting.Unit && blank.UnitFilter == AbilityUnitFilter.Ally), Is.True);
        Assert.That(blanks.Any(blank => blank.Targeting == AbilityTargeting.Unit && blank.UnitFilter == AbilityUnitFilter.Enemy), Is.True);
    }

    /// <summary>
    /// The player prefab has a <see cref="PlayerAbilities"/> on the placeholder class, referenced by its
    /// <see cref="PlayerController"/>.
    /// </summary>
    [Test]
    public void PlayerPrefab_HasAbilitiesWithThePlaceholderClass()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player/Player.prefab");
        PlayerAbilities prefabAbilities = prefab.GetComponent<PlayerAbilities>();

        Assert.That(prefabAbilities, Is.Not.Null);
        Assert.That(prefabAbilities.Class, Is.SameAs(AssetDatabase.LoadAssetAtPath<PlayerClass>("Assets/Data/Classes/Class_Placeholder.asset")));
        Assert.That(new SerializedObject(prefab.GetComponent<PlayerController>()).FindProperty("abilities").objectReferenceValue, Is.SameAs(prefabAbilities));
    }

    /// <summary>
    /// Creates a player with health, a weapon holding the weapon abilities and <see cref="PlayerAbilities"/> on
    /// the class abilities, backed by an ability service under <paramref name="authority"/> whose applied and
    /// resolved events bump the test counters.
    /// </summary>
    private PlayerAbilities CreatePlayer(IGameAuthority authority)
    {
        GameObject player = Track(new GameObject("Player"));
        player.AddComponent<Rigidbody2D>();
        health = player.AddComponent<Health>();
        weapon = player.AddComponent<PlayerWeaponController>();
        weapon.Construct(new SlashSpawner(new ContainerBuilder().Build(), TestCombat.CreateHitService(authority)), clock);
        weapon.Equip(Track(PlayerWeapon.Create("Sword", 1, 0.35f, 0.55f, new Vector2[4], weaponAbilities: weaponAbilities)));
        service = TestCombat.CreateAbilityService(authority);
        service.CastApplied += (_, _) => applied++;
        service.CastResolved += (_, _) => resolved++;
        PlayerAbilities playerAbilities = player.AddComponent<PlayerAbilities>();
        playerAbilities.Construct(clock, service);
        playerAbilities.Configure(Track(PlayerClass.Create("Test", classAbilities)));
        return playerAbilities;
    }

    /// <summary>
    /// Creates a tracked ability with the given cooldown and unit filter.
    /// </summary>
    private AbilityDefinition Ability(string name, float cooldown = 1f, AbilityUnitFilter unitFilter = AbilityUnitFilter.Enemy)
    {
        return Track(AbilityDefinition.Create(name, cooldown, unitFilter: unitFilter));
    }

    /// <summary>
    /// Records <paramref name="created"/> for destruction on teardown and returns it.
    /// </summary>
    private T Track<T>(T created) where T : Object
    {
        createdObjects.Add(created);
        return created;
    }
}
