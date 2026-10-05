using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tests for <see cref="AbilityService"/>: when cast effects land, which units each targeting mode affects, the
/// faction rules for healing and hitting, authority, and the shipped ability assets.
/// </summary>
public class AbilityEffectTests
{
    private readonly List<Object> created = new();
    private CombatEvents events;
    private AbilityService service;

    /// <summary>
    /// Builds fresh combat events and an authoritative ability service for each test.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        events = new CombatEvents();
        service = TestCombat.CreateAbilityService(FixedGameAuthority.Authoritative, events);
    }

    /// <summary>
    /// Destroys every game object and asset instance the test created.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        foreach (Object instance in created)
        {
            if (instance != null)
            {
                Object.DestroyImmediate(instance);
            }
        }

        created.Clear();
    }

    /// <summary>
    /// Applying a cast only announces it; the damage lands when the cast resolves, which raises
    /// <see cref="AbilityService.CastResolved"/> once.
    /// </summary>
    [Test]
    public void StartingACast_HasNoEffect_ResolvingItDoes()
    {
        PlayerAbilities caster = Caster(Vector2.zero);
        DamageReceiver mob = Unit("Mob", Faction.Mobs, new Vector2(2f, 0f));
        AbilityDefinition smite = Ability(AbilityTargeting.Unit, new Hit(15, DamageType.Physical));
        int resolved = 0;
        service.CastResolved += (_, _) => resolved++;

        Assert.That(service.Apply(caster, Cast(smite, mob, UnitTeam.Enemy)), Is.True);
        Assert.That(mob.Health.CurrentHealth, Is.EqualTo(mob.Health.MaxHealth), "Effects wait for the cast to complete.");

        Assert.That(service.Resolve(caster, Cast(smite, mob, UnitTeam.Enemy), caster.transform.position), Is.True);
        Assert.That(mob.Health.CurrentHealth, Is.EqualTo(mob.Health.MaxHealth - 15));
        Assert.That(resolved, Is.EqualTo(1));
    }

    /// <summary>
    /// A unit-targeted ability on an enemy deals its hit damage and applies the hit's statuses to the target.
    /// </summary>
    [Test]
    public void AUnitAbility_DamagesItsEnemyTarget_AndAppliesItsStatuses()
    {
        PlayerAbilities caster = Caster(Vector2.zero);
        DamageReceiver mob = Unit("Mob", Faction.Mobs, new Vector2(2f, 0f));
        StatusEffectDefinition stun = Status("Stun", StatusKind.Debuff, StatusControls.Stun);
        AbilityDefinition smite = Ability(AbilityTargeting.Unit, new Hit(15, DamageType.Physical, new[] { stun }));

        service.Resolve(caster, Cast(smite, mob, UnitTeam.Enemy), caster.transform.position);

        Assert.That(mob.Health.CurrentHealth, Is.EqualTo(mob.Health.MaxHealth - 15));
        Assert.That(mob.Statuses.Controls, Is.EqualTo(StatusControls.Stun));
    }

    /// <summary>
    /// A unit-targeted ability on an ally heals it, applies its buff and raises one heal event, while a damaging
    /// ability on an ally does no damage.
    /// </summary>
    [Test]
    public void AUnitAbility_HealsAndBuffsAnAlly_ButNeverHurtsOne()
    {
        PlayerAbilities caster = Caster(Vector2.zero);
        DamageReceiver ally = Unit("Ally", Faction.Players, new Vector2(2f, 0f));
        ally.Health.ApplyDamage(40);
        StatusEffectDefinition fortify = Status("Fortify", StatusKind.Buff, StatusControls.None);
        AbilityDefinition mend = Ability(AbilityTargeting.Unit, new Hit(0, DamageType.Physical, new[] { fortify }), heal: 25);
        int heals = 0;
        events.HealApplied += _ => heals++;

        service.Resolve(caster, Cast(mend, ally, UnitTeam.Ally), caster.transform.position);
        Assert.That(ally.Health.CurrentHealth, Is.EqualTo(ally.Health.MaxHealth - 15));
        Assert.That(ally.Statuses.GetStacks(fortify), Is.EqualTo(1));
        Assert.That(heals, Is.EqualTo(1));

        service.Resolve(caster, Cast(Ability(AbilityTargeting.Unit, new Hit(15, DamageType.Physical)), ally, UnitTeam.Ally), caster.transform.position);
        Assert.That(ally.Health.CurrentHealth, Is.EqualTo(ally.Health.MaxHealth - 15), "No friendly fire from abilities either.");
    }

    /// <summary>
    /// An untargeted ability with no area radius heals and buffs only the caster, not a nearby ally.
    /// </summary>
    [Test]
    public void ASelfAbility_WithoutARadius_AffectsOnlyTheCaster()
    {
        PlayerAbilities caster = Caster(Vector2.zero);
        DamageReceiver casterUnit = caster.GetComponent<DamageReceiver>();
        DamageReceiver ally = Unit("Ally", Faction.Players, new Vector2(0.5f, 0f));
        casterUnit.Health.ApplyDamage(30);
        ally.Health.ApplyDamage(30);
        StatusEffectDefinition fortify = Status("Fortify", StatusKind.Buff, StatusControls.None);

        service.Resolve(caster, SelfCast(Ability(AbilityTargeting.None, new Hit(0, DamageType.Physical, new[] { fortify }), heal: 20)), caster.transform.position);

        Assert.That(casterUnit.Health.CurrentHealth, Is.EqualTo(casterUnit.Health.MaxHealth - 10));
        Assert.That(casterUnit.Statuses.GetStacks(fortify), Is.EqualTo(1));
        Assert.That(ally.Health.CurrentHealth, Is.EqualTo(ally.Health.MaxHealth - 30), "Only the caster.");
        Assert.That(ally.Statuses.Count, Is.Zero);
    }

    /// <summary>
    /// An untargeted ability with an area radius hits every enemy inside the circle around the caster, and spares
    /// enemies outside it, allies and the caster itself.
    /// </summary>
    [Test]
    public void ASelfAbility_WithARadius_HitsTheEnemiesAroundTheCaster()
    {
        PlayerAbilities caster = Caster(Vector2.zero);
        DamageReceiver near = Unit("Near", Faction.Mobs, new Vector2(1f, 0f));
        DamageReceiver behind = Unit("Behind", Faction.Mobs, new Vector2(-1f, 0.5f));
        DamageReceiver far = Unit("Far", Faction.Mobs, new Vector2(3f, 0f));
        DamageReceiver ally = Unit("Ally", Faction.Players, new Vector2(0f, 1f));
        Physics2D.SyncTransforms();

        service.Resolve(caster, SelfCast(Ability(AbilityTargeting.None, new Hit(10, DamageType.Physical), areaRadius: 1.5f)), caster.transform.position);

        Assert.That(near.Health.CurrentHealth, Is.EqualTo(near.Health.MaxHealth - 10));
        Assert.That(behind.Health.CurrentHealth, Is.EqualTo(behind.Health.MaxHealth - 10));
        Assert.That(far.Health.CurrentHealth, Is.EqualTo(far.Health.MaxHealth), "Outside the radius.");
        Assert.That(ally.Health.CurrentHealth, Is.EqualTo(ally.Health.MaxHealth), "Allies are not hurt.");
        Assert.That(caster.GetComponent<Health>().CurrentHealth, Is.EqualTo(caster.GetComponent<Health>().MaxHealth));
    }

    /// <summary>
    /// A point-targeted ability affects only the units inside its radius around the aimed point, damaging enemies
    /// and healing allies without damaging them.
    /// </summary>
    [Test]
    public void APointAbility_AffectsTheUnitsAroundThePoint_HealingAlliesAndHittingEnemies()
    {
        PlayerAbilities caster = Caster(Vector2.zero);
        DamageReceiver mob = Unit("Mob", Faction.Mobs, new Vector2(4f, 0f));
        DamageReceiver ally = Unit("Ally", Faction.Players, new Vector2(4.5f, 0.5f));
        DamageReceiver elsewhere = Unit("Elsewhere", Faction.Mobs, new Vector2(0f, 4f));
        ally.Health.ApplyDamage(30);
        Physics2D.SyncTransforms();
        AbilityDefinition quake = Ability(AbilityTargeting.Point, new Hit(10, DamageType.Physical), heal: 5, areaRadius: 1.25f);

        service.Resolve(caster, new AbilityCast(1, quake, new CastAim(new Vector2(4f, 0f), Vector2.right, default)), caster.transform.position);

        Assert.That(mob.Health.CurrentHealth, Is.EqualTo(mob.Health.MaxHealth - 10));
        Assert.That(ally.Health.CurrentHealth, Is.EqualTo(ally.Health.MaxHealth - 25), "Healed, and never damaged.");
        Assert.That(elsewhere.Health.CurrentHealth, Is.EqualTo(elsewhere.Health.MaxHealth));
    }

    /// <summary>
    /// A direction ability pierces every enemy on its line from the caster up to its range, missing units beyond
    /// it, beside it or behind the caster, and does nothing when the aim has no direction.
    /// </summary>
    [Test]
    public void ADirectionAbility_HitsTheEnemiesOnItsLine_UpToItsRange()
    {
        PlayerAbilities caster = Caster(Vector2.zero);
        DamageReceiver first = Unit("First", Faction.Mobs, new Vector2(2f, 0.1f));
        DamageReceiver second = Unit("Second", Faction.Mobs, new Vector2(5f, -0.2f));
        DamageReceiver beyond = Unit("Beyond", Faction.Mobs, new Vector2(8f, 0f));
        DamageReceiver beside = Unit("Beside", Faction.Mobs, new Vector2(3f, 2f));
        DamageReceiver behind = Unit("Behind", Faction.Mobs, new Vector2(-3f, 0f));
        Physics2D.SyncTransforms();
        AbilityDefinition bolt = Ability(AbilityTargeting.Direction, new Hit(12, DamageType.Frost), range: 6f, areaRadius: 0.35f);

        service.Resolve(caster, new AbilityCast(0, bolt, new CastAim(new Vector2(10f, 0f), Vector2.right, default)), caster.transform.position);

        Assert.That(first.Health.CurrentHealth, Is.EqualTo(first.Health.MaxHealth - 12));
        Assert.That(second.Health.CurrentHealth, Is.EqualTo(second.Health.MaxHealth - 12), "The line pierces.");
        Assert.That(beyond.Health.CurrentHealth, Is.EqualTo(beyond.Health.MaxHealth), "Past the range.");
        Assert.That(beside.Health.CurrentHealth, Is.EqualTo(beside.Health.MaxHealth), "Off the line.");
        Assert.That(behind.Health.CurrentHealth, Is.EqualTo(behind.Health.MaxHealth));

        service.Resolve(caster, new AbilityCast(0, bolt, new CastAim(Vector2.zero, Vector2.zero, default)), caster.transform.position);
        Assert.That(first.Health.CurrentHealth, Is.EqualTo(first.Health.MaxHealth - 12), "No direction, no line.");
    }

    /// <summary>
    /// An area around the caster is centred on the caster position passed to
    /// <see cref="AbilityService.Resolve"/>, not on where this machine's copy of the caster stands.
    /// </summary>
    [Test]
    public void AreasAroundTheCaster_StartWhereTheOwnerSawTheCaster_NotAtThisMachinesCopy()
    {
        PlayerAbilities caster = Caster(Vector2.zero);
        DamageReceiver ahead = Unit("Ahead", Faction.Mobs, new Vector2(3f, 0f));
        DamageReceiver atCopy = Unit("AtCopy", Faction.Mobs, new Vector2(-1f, 0f));
        Physics2D.SyncTransforms();

        service.Resolve(caster, SelfCast(Ability(AbilityTargeting.None, new Hit(10, DamageType.Physical), areaRadius: 1.25f)), new Vector2(2.5f, 0f));

        Assert.That(ahead.Health.CurrentHealth, Is.EqualTo(ahead.Health.MaxHealth - 10), "The circle is centred on the owner's position.");
        Assert.That(atCopy.Health.CurrentHealth, Is.EqualTo(atCopy.Health.MaxHealth), "Not on this machine's lagging copy.");
    }

    /// <summary>
    /// A non-authoritative service refuses to resolve a cast and leaves the target untouched.
    /// </summary>
    [Test]
    public void ResolvingWithoutAuthority_DoesNothing()
    {
        PlayerAbilities caster = Caster(Vector2.zero);
        DamageReceiver mob = Unit("Mob", Faction.Mobs, new Vector2(2f, 0f));
        AbilityService client = TestCombat.CreateAbilityService(new FixedGameAuthority(false));

        Assert.That(client.Resolve(caster, Cast(Ability(AbilityTargeting.Unit, new Hit(15, DamageType.Physical)), mob, UnitTeam.Enemy), caster.transform.position), Is.False);
        Assert.That(mob.Health.CurrentHealth, Is.EqualTo(mob.Health.MaxHealth));
    }

    /// <summary>
    /// A cast on a target with health but no <see cref="DamageReceiver"/> still resolves but deals no damage.
    /// </summary>
    [Test]
    public void ACastWhoseTargetHasNoReceiver_DoesNothingButStillResolves()
    {
        PlayerAbilities caster = Caster(Vector2.zero);
        Health bareHealth = Track(new GameObject("Bare")).AddComponent<Health>();
        AbilityDefinition smite = Ability(AbilityTargeting.Unit, new Hit(15, DamageType.Physical));

        Assert.That(service.Resolve(caster, new AbilityCast(3, smite, new CastAim(Vector2.zero, Vector2.right, new UnitTarget(bareHealth, null, UnitTeam.Enemy))), caster.transform.position), Is.True);
        Assert.That(bareHealth.CurrentHealth, Is.EqualTo(bareHealth.MaxHealth));
    }

    /// <summary>
    /// Through <see cref="PlayerAbilities"/>, a cancelled cast and a cast still in progress deal no damage; only a
    /// completed cast lands.
    /// </summary>
    [Test]
    public void ThroughPlayerAbilities_OnlyACompletedCastLands()
    {
        PlayerAbilities caster = Caster(Vector2.zero);
        DamageReceiver mob = Unit("Mob", Faction.Mobs, new Vector2(2f, 0f));
        AbilityDefinition smite = Ability(AbilityTargeting.Unit, new Hit(15, DamageType.Physical));
        ManualClock clock = new();
        caster.Construct(clock, service);
        caster.Configure(Track(PlayerClass.Create("Test", smite)));
        CastAim aim = new(mob.transform.position, Vector2.right, new UnitTarget(mob.Health, null, UnitTeam.Enemy));

        Assert.That(caster.TryCast(0, aim), Is.EqualTo(CastOutcome.Started));
        caster.CancelCast(0);
        Assert.That(mob.Health.CurrentHealth, Is.EqualTo(mob.Health.MaxHealth), "An interrupted cast never lands.");

        clock.Advance(5f);
        Assert.That(caster.TryCast(0, aim), Is.EqualTo(CastOutcome.Started));
        Assert.That(mob.Health.CurrentHealth, Is.EqualTo(mob.Health.MaxHealth), "Not while it is being cast.");
        caster.CompleteCast(0);
        Assert.That(mob.Health.CurrentHealth, Is.EqualTo(mob.Health.MaxHealth - 15));
    }

    /// <summary>
    /// The shipped Bolt, Whirl, Smite and Mend assets carry their expected hits, heals, statuses and shapes, and
    /// Leap and Lunge have no effect yet.
    /// </summary>
    [Test]
    public void Content_TheAbilitiesWithEffects_AndTheMovementBlanks()
    {
        AbilityDefinition bolt = Load("Blank_Bolt");
        AbilityDefinition whirl = Load("Blank_Whirl");
        AbilityDefinition smite = Load("Blank_Smite");
        AbilityDefinition mend = Load("Blank_Mend");

        Assert.That(bolt.Hit.Amount, Is.EqualTo(12));
        Assert.That(bolt.Hit.Type, Is.EqualTo(DamageType.Frost));
        Assert.That(bolt.Hit.GetStatus(0).DisplayName, Is.EqualTo("Chill"));
        Assert.That(bolt.Range, Is.EqualTo(6f));
        Assert.That(bolt.AreaRadius, Is.GreaterThan(0f));

        Assert.That(whirl.Hit.Amount, Is.EqualTo(10));
        Assert.That(whirl.Targeting, Is.EqualTo(AbilityTargeting.None));
        Assert.That(whirl.AreaRadius, Is.EqualTo(1.5f));

        Assert.That(smite.Hit.Amount, Is.EqualTo(15));
        Assert.That(smite.Hit.GetStatus(0).Controls, Is.EqualTo(StatusControls.Stun));
        Assert.That(mend.Heal, Is.EqualTo(25));
        Assert.That(mend.Hit.GetStatus(0).DisplayName, Is.EqualTo("Fortify"));

        Assert.That(Load("Blank_Leap").HasEffect, Is.False, "Leap and Lunge wait for abilities that move the caster.");
        Assert.That(Load("Blank_Lunge").HasEffect, Is.False);
    }

    /// <summary>
    /// Loads the project ability asset named <paramref name="name"/> from <c>Assets/Data/Abilities</c>.
    /// </summary>
    private static AbilityDefinition Load(string name)
    {
        return AssetDatabase.LoadAssetAtPath<AbilityDefinition>($"Assets/Data/Abilities/{name}.asset");
    }

    /// <summary>
    /// Builds a slot 3 cast of <paramref name="ability"/> aimed at <paramref name="target"/>, seen as
    /// <paramref name="team"/> from the caster.
    /// </summary>
    private static AbilityCast Cast(AbilityDefinition ability, DamageReceiver target, UnitTeam team)
    {
        return new AbilityCast(3, ability, new CastAim(target.transform.position, Vector2.right, new UnitTarget(target.Health, null, team)));
    }

    /// <summary>
    /// Builds a slot 4 cast of <paramref name="ability"/> with no aim point, direction or target.
    /// </summary>
    private static AbilityCast SelfCast(AbilityDefinition ability)
    {
        return new AbilityCast(4, ability, new CastAim(Vector2.zero, Vector2.zero, default));
    }

    /// <summary>
    /// Creates a player-faction unit at <paramref name="position"/> with a <see cref="PlayerAbilities"/> component.
    /// </summary>
    private PlayerAbilities Caster(Vector2 position)
    {
        DamageReceiver caster = Unit("Caster", Faction.Players, position);
        return caster.gameObject.AddComponent<PlayerAbilities>();
    }

    /// <summary>
    /// Creates a tracked unit of <paramref name="faction"/> at <paramref name="position"/> with a box collider,
    /// 100 max health, a <see cref="DamageReceiver"/> and <see cref="StatusEffects"/>.
    /// </summary>
    private DamageReceiver Unit(string name, Faction faction, Vector2 position)
    {
        GameObject unit = Track(new GameObject(name));
        unit.transform.position = position;
        unit.AddComponent<BoxCollider2D>().size = new Vector2(0.5f, 0.5f);
        SerializedObject health = new(unit.AddComponent<Health>());
        health.FindProperty("maxHealth").intValue = 100;
        health.ApplyModifiedPropertiesWithoutUndo();
        DamageReceiver receiver = unit.AddComponent<DamageReceiver>();
        unit.AddComponent<StatusEffects>();
        receiver.SetProfile(Track(CombatProfile.Create(faction)));
        return receiver;
    }

    /// <summary>
    /// Creates a tracked ability that accepts any unit, with the given targeting, hit, heal, range and area radius.
    /// </summary>
    private AbilityDefinition Ability(AbilityTargeting targeting, Hit hit, int heal = 0, float range = 3f, float areaRadius = 0f)
    {
        return Track(AbilityDefinition.Create("Test", targeting: targeting, unitFilter: AbilityUnitFilter.Any, range: range, hit: hit, heal: heal, areaRadius: areaRadius));
    }

    /// <summary>
    /// Creates a tracked five second status of <paramref name="kind"/> that applies <paramref name="controls"/>.
    /// </summary>
    private StatusEffectDefinition Status(string name, StatusKind kind, StatusControls controls)
    {
        return Track(StatusEffectDefinition.Create(name, kind, 5f, controls: controls));
    }

    /// <summary>
    /// Records <paramref name="instance"/> for destruction in <see cref="TearDown"/> and returns it.
    /// </summary>
    private T Track<T>(T instance) where T : Object
    {
        created.Add(instance);
        return instance;
    }
}
