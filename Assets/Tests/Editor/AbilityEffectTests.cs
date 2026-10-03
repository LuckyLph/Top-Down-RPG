using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class AbilityEffectTests
{
    private readonly List<Object> created = new();
    private CombatEvents events;
    private AbilityService service;

    [SetUp]
    public void SetUp()
    {
        events = new CombatEvents();
        service = TestCombat.CreateAbilityService(FixedGameAuthority.Authoritative, events);
    }

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

    [Test]
    public void AUnitAbility_DamagesItsEnemyTarget_AndAppliesItsStatuses()
    {
        PlayerAbilities caster = Caster();
        DamageReceiver mob = Unit("Mob", Faction.Mobs);
        StatusEffectDefinition stun = Status("Stun", StatusKind.Debuff, StatusControls.Stun);
        AbilityDefinition smite = Ability(AbilityTargeting.Unit, AbilityUnitFilter.Enemy, new Hit(15, DamageType.Physical, new[] { stun }));

        Assert.That(service.Apply(caster, Cast(smite, mob, UnitTeam.Enemy)), Is.True);

        Assert.That(mob.Health.CurrentHealth, Is.EqualTo(mob.Health.MaxHealth - 15));
        Assert.That(mob.Statuses.GetStacks(stun), Is.EqualTo(1));
        Assert.That(mob.Statuses.Controls, Is.EqualTo(StatusControls.Stun));
    }

    [Test]
    public void AUnitAbility_HealsAndBuffsAnAlly_ButNeverHurtsOne()
    {
        PlayerAbilities caster = Caster();
        DamageReceiver ally = Unit("Ally", Faction.Players);
        ally.Health.ApplyDamage(40);
        StatusEffectDefinition fortify = Status("Fortify", StatusKind.Buff, StatusControls.None);
        AbilityDefinition mend = Ability(AbilityTargeting.Unit, AbilityUnitFilter.Ally, new Hit(0, DamageType.Physical, new[] { fortify }), heal: 25);
        int heals = 0;
        events.HealApplied += _ => heals++;

        service.Apply(caster, Cast(mend, ally, UnitTeam.Ally));
        Assert.That(ally.Health.CurrentHealth, Is.EqualTo(ally.Health.MaxHealth - 15));
        Assert.That(ally.Statuses.GetStacks(fortify), Is.EqualTo(1));
        Assert.That(heals, Is.EqualTo(1));

        AbilityDefinition smite = Ability(AbilityTargeting.Unit, AbilityUnitFilter.Any, new Hit(15, DamageType.Physical));
        service.Apply(caster, Cast(smite, ally, UnitTeam.Ally));
        Assert.That(ally.Health.CurrentHealth, Is.EqualTo(ally.Health.MaxHealth - 15), "No friendly fire from abilities either.");
    }

    [Test]
    public void AbilitiesWithoutAUnitTargetOrWithoutAuthority_HaveNoEffect()
    {
        PlayerAbilities caster = Caster();
        DamageReceiver mob = Unit("Mob", Faction.Mobs);
        Hit hit = new(15, DamageType.Physical);
        int applied = 0;
        service.CastApplied += (_, _) => applied++;

        Assert.That(service.Apply(caster, Cast(Ability(AbilityTargeting.Direction, AbilityUnitFilter.Enemy, hit), mob, UnitTeam.Enemy)), Is.True);
        Assert.That(mob.Health.CurrentHealth, Is.EqualTo(mob.Health.MaxHealth), "Only Unit targeting has effects for now.");
        Assert.That(applied, Is.EqualTo(1), "The cast itself is still applied once.");

        AbilityService client = TestCombat.CreateAbilityService(new FixedGameAuthority(false));
        Assert.That(client.Apply(caster, Cast(Ability(AbilityTargeting.Unit, AbilityUnitFilter.Enemy, hit), mob, UnitTeam.Enemy)), Is.False);
        Assert.That(mob.Health.CurrentHealth, Is.EqualTo(mob.Health.MaxHealth));
    }

    [Test]
    public void ACastWhoseTargetHasNoReceiver_DoesNothingButStillApplies()
    {
        PlayerAbilities caster = Caster();
        GameObject bare = Track(new GameObject("Bare"));
        Health bareHealth = bare.AddComponent<Health>();
        AbilityDefinition smite = Ability(AbilityTargeting.Unit, AbilityUnitFilter.Enemy, new Hit(15, DamageType.Physical));

        Assert.That(service.Apply(caster, new AbilityCast(3, smite, new CastAim(Vector2.zero, Vector2.right, new UnitTarget(bareHealth, null, UnitTeam.Enemy)))), Is.True);
        Assert.That(bareHealth.CurrentHealth, Is.EqualTo(bareHealth.MaxHealth));
    }

    [Test]
    public void Content_SmiteDamagesAndStuns_MendHealsAndFortifies_TheOtherBlanksDoNothing()
    {
        AbilityDefinition smite = AssetDatabase.LoadAssetAtPath<AbilityDefinition>("Assets/Data/Abilities/Blank_Smite.asset");
        AbilityDefinition mend = AssetDatabase.LoadAssetAtPath<AbilityDefinition>("Assets/Data/Abilities/Blank_Mend.asset");

        Assert.That(smite.Hit.Amount, Is.EqualTo(15));
        Assert.That(smite.Hit.Type, Is.EqualTo(DamageType.Physical));
        Assert.That(smite.Hit.StatusCount, Is.EqualTo(1));
        Assert.That(smite.Hit.GetStatus(0).Controls, Is.EqualTo(StatusControls.Stun));
        Assert.That(smite.Heal, Is.Zero);

        Assert.That(mend.Heal, Is.EqualTo(25));
        Assert.That(mend.Hit.Amount, Is.Zero);
        Assert.That(mend.Hit.StatusCount, Is.EqualTo(1));
        Assert.That(mend.Hit.GetStatus(0).DisplayName, Is.EqualTo("Fortify"));

        foreach (string guid in AssetDatabase.FindAssets("t:AbilityDefinition", new[] { "Assets/Data/Abilities" }))
        {
            AbilityDefinition ability = AssetDatabase.LoadAssetAtPath<AbilityDefinition>(AssetDatabase.GUIDToAssetPath(guid));
            if (ability != smite && ability != mend)
            {
                Assert.That(ability.HasEffect, Is.False, $"{ability.name} is still a blank.");
            }
        }
    }

    private static AbilityCast Cast(AbilityDefinition ability, DamageReceiver target, UnitTeam team)
    {
        return new AbilityCast(3, ability, new CastAim(target.transform.position, Vector2.right, new UnitTarget(target.Health, null, team)));
    }

    private PlayerAbilities Caster()
    {
        DamageReceiver caster = Unit("Caster", Faction.Players);
        return caster.gameObject.AddComponent<PlayerAbilities>();
    }

    private DamageReceiver Unit(string name, Faction faction)
    {
        GameObject unit = Track(new GameObject(name));
        SerializedObject health = new(unit.AddComponent<Health>());
        health.FindProperty("maxHealth").intValue = 100;
        health.ApplyModifiedPropertiesWithoutUndo();
        DamageReceiver receiver = unit.AddComponent<DamageReceiver>();
        unit.AddComponent<StatusEffects>();
        receiver.SetProfile(Track(CombatProfile.Create(faction)));
        return receiver;
    }

    private AbilityDefinition Ability(AbilityTargeting targeting, AbilityUnitFilter filter, Hit hit, int heal = 0)
    {
        return Track(AbilityDefinition.Create("Test", targeting: targeting, unitFilter: filter, hit: hit, heal: heal));
    }

    private StatusEffectDefinition Status(string name, StatusKind kind, StatusControls controls)
    {
        return Track(StatusEffectDefinition.Create(name, kind, 5f, controls: controls));
    }

    private T Track<T>(T instance) where T : Object
    {
        created.Add(instance);
        return instance;
    }
}
