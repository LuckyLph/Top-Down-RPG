using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VContainer;

public class PlayerAbilitiesTests
{
    private readonly List<Object> createdObjects = new();
    private ManualClock clock;
    private AbilityService service;
    private int applied;
    private PlayerAbilities abilities;
    private PlayerWeaponController weapon;
    private Health health;
    private AbilityDefinition[] classAbilities;
    private AbilityDefinition[] weaponAbilities;

    [SetUp]
    public void SetUp()
    {
        clock = new ManualClock();
        applied = 0;
        classAbilities = new[] { Ability("Q", cooldown: 2f), Ability("W"), Ability("E"), null };
        weaponAbilities = new[] { Ability("A"), Ability("S") };
        abilities = CreatePlayer(FixedGameAuthority.Authoritative);
    }

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

    [Test]
    public void FailedCasts_ReportWhy_WithoutSideEffects()
    {
        List<(int, CastOutcome)> failures = new();
        int started = 0;
        abilities.CastFailed += (slot, reason) => failures.Add((slot, reason));
        abilities.CastStarted += _ => started++;

        Assert.That(abilities.TryCast(3, default), Is.EqualTo(CastOutcome.EmptySlot));

        abilities.TryCast(0, default);
        abilities.EndCast(0);
        clock.Advance(1f);
        Assert.That(abilities.TryCast(0, default), Is.EqualTo(CastOutcome.OnCooldown));

        abilities.TryCast(1, default);
        Assert.That(abilities.TryCast(2, default), Is.EqualTo(CastOutcome.CastInProgress));
        abilities.EndCast(1);

        health.ApplyDamage(health.MaxHealth);
        Assert.That(abilities.TryCast(4, default), Is.EqualTo(CastOutcome.Dead));

        Assert.That(failures, Is.EqualTo(new[] { (3, CastOutcome.EmptySlot), (0, CastOutcome.OnCooldown), (2, CastOutcome.CastInProgress), (4, CastOutcome.Dead) }));
        Assert.That(started, Is.EqualTo(2));
        Assert.That(applied, Is.EqualTo(2));
        Assert.That(abilities.RemainingCooldown(2), Is.Zero, "A failed cast must not start a cooldown.");
        Assert.That(abilities.RemainingCooldown(4), Is.Zero);
    }

    [Test]
    public void EndCast_RaisesCastEnded_OnlyForTheRunningSlot()
    {
        List<int> ended = new();
        abilities.CastEnded += ended.Add;
        abilities.TryCast(1, default);

        abilities.EndCast(0);
        abilities.EndCast(1);
        abilities.EndCast(1);

        Assert.That(ended, Is.EqualTo(new[] { 1 }));
        Assert.That(abilities.IsCasting, Is.False);
    }

    [Test]
    public void WithoutAuthority_CastsRunLocally_ButAreNotApplied()
    {
        PlayerAbilities client = CreatePlayer(new FixedGameAuthority(false));

        Assert.That(client.TryCast(0, default), Is.EqualTo(CastOutcome.Started));
        Assert.That(applied, Is.Zero);
    }

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

    [Test]
    public void RecentCasts_RecordLocalAndRemoteCastsNewestFirst_ForTheGizmos()
    {
        Assert.That(abilities.RecentCastCount, Is.Zero);

        abilities.TryCast(0, new CastAim(Vector2.one, Vector2.up, default));
        abilities.EndCast(0);
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

    [Test]
    public void SlotKeyNames_FollowTheBar()
    {
        string[] keys = new string[AbilitySlots.Count];
        for (int i = 0; i < keys.Length; i++)
        {
            keys[i] = AbilitySlots.KeyName(i);
        }

        Assert.That(keys, Is.EqualTo(new[] { "Q", "W", "E", "R", "A", "S" }));
        Assert.That(AbilitySlots.KeyName(-1), Is.Empty);
        Assert.That(AbilitySlots.KeyName(AbilitySlots.Count), Is.Empty);
    }

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

    [Test]
    public void PlayerPrefab_HasAbilitiesWithThePlaceholderClass()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player/Player.prefab");
        PlayerAbilities prefabAbilities = prefab.GetComponent<PlayerAbilities>();

        Assert.That(prefabAbilities, Is.Not.Null);
        Assert.That(prefabAbilities.Class, Is.SameAs(AssetDatabase.LoadAssetAtPath<PlayerClass>("Assets/Data/Classes/Class_Placeholder.asset")));
        Assert.That(new SerializedObject(prefab.GetComponent<PlayerController>()).FindProperty("abilities").objectReferenceValue, Is.SameAs(prefabAbilities));
    }

    private PlayerAbilities CreatePlayer(IGameAuthority authority)
    {
        GameObject player = Track(new GameObject("Player"));
        player.AddComponent<Rigidbody2D>();
        health = player.AddComponent<Health>();
        weapon = player.AddComponent<PlayerWeaponController>();
        weapon.Construct(new SlashSpawner(new ContainerBuilder().Build(), new DamageService(new CombatEvents(), authority)), clock);
        weapon.Equip(Track(PlayerWeapon.Create("Sword", 1, 0.35f, 0.55f, new Vector2[4], weaponAbilities: weaponAbilities)));
        service = new AbilityService(authority);
        service.CastApplied += (_, _) => applied++;
        PlayerAbilities playerAbilities = player.AddComponent<PlayerAbilities>();
        playerAbilities.Construct(clock, service);
        playerAbilities.Configure(Track(PlayerClass.Create("Test", classAbilities)));
        return playerAbilities;
    }

    private AbilityDefinition Ability(string name, float cooldown = 1f, AbilityUnitFilter unitFilter = AbilityUnitFilter.Enemy)
    {
        return Track(AbilityDefinition.Create(name, cooldown, unitFilter: unitFilter));
    }

    private T Track<T>(T created) where T : Object
    {
        createdObjects.Add(created);
        return created;
    }
}
