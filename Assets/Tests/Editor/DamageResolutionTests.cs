using System.Collections.Generic;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tests for typed damage and healing: <see cref="DamageMath"/>, defenses from <see cref="DamageReceiver"/>
/// profiles, <see cref="DamageService"/> faction and authority rules, typed melee hits, popups, and the shipped
/// combat content.
/// </summary>
public class DamageResolutionTests
{
    private readonly List<Object> created = new();

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
    /// A hit against no defense resolves to its raw amount and type with no flags.
    /// </summary>
    [Test]
    public void DamageMath_WithoutDefense_DealsTheRawAmount()
    {
        DamageResult result = Resolve(20, DamageType.Physical, DefenseSnapshot.None);

        Assert.That(result.Amount, Is.EqualTo(20));
        Assert.That(result.RawAmount, Is.EqualTo(20));
        Assert.That(result.Type, Is.EqualTo(DamageType.Physical));
        Assert.That(result.Flags, Is.EqualTo(DamageFlags.None));
        Assert.That(result.Resolved, Is.True);
    }

    /// <summary>
    /// A positive resistance reduces the damage by its percentage and flags the hit as resisted.
    /// </summary>
    [Test]
    public void DamageMath_PositiveResistance_ReducesAndFlagsResisted()
    {
        DamageResult result = Resolve(20, DamageType.Fire, new DefenseSnapshot(50, false, 1f));

        Assert.That(result.Amount, Is.EqualTo(10));
        Assert.That(result.Flags, Is.EqualTo(DamageFlags.Resisted));
    }

    /// <summary>
    /// A negative resistance increases the damage by its percentage and flags the hit as a weakness.
    /// </summary>
    [Test]
    public void DamageMath_NegativeResistance_IncreasesAndFlagsWeakness()
    {
        DamageResult result = Resolve(20, DamageType.Frost, new DefenseSnapshot(-50, false, 1f));

        Assert.That(result.Amount, Is.EqualTo(30));
        Assert.That(result.Flags, Is.EqualTo(DamageFlags.Weakness));
    }

    /// <summary>
    /// Resistance is clamped to the settings' cap (80%) and floor (-100%).
    /// </summary>
    [Test]
    public void DamageMath_ClampsResistanceToTheCapAndTheFloor()
    {
        Assert.That(Resolve(20, DamageType.Fire, new DefenseSnapshot(100, false, 1f)).Amount, Is.EqualTo(4), "Resistance stops at 80%.");
        Assert.That(Resolve(20, DamageType.Fire, new DefenseSnapshot(-150, false, 1f)).Amount, Is.EqualTo(40), "Weakness stops at -100%.");
    }

    /// <summary>
    /// True damage ignores resistance and carries no resisted flag, but an immune target still takes none.
    /// </summary>
    [Test]
    public void DamageMath_TrueDamage_IgnoresResistanceButNotImmunity()
    {
        DamageResult resisted = Resolve(20, DamageType.True, new DefenseSnapshot(80, false, 1f));
        DamageResult immune = Resolve(20, DamageType.True, new DefenseSnapshot(0, true, 1f));

        Assert.That(resisted.Amount, Is.EqualTo(20));
        Assert.That(resisted.Flags, Is.EqualTo(DamageFlags.None));
        Assert.That(immune.Amount, Is.Zero);
        Assert.That(immune.IsImmune, Is.True);
    }

    /// <summary>
    /// An immune target takes zero damage, overriding weakness and multipliers, yet the hit still resolves with
    /// only the immune flag.
    /// </summary>
    [Test]
    public void DamageMath_Immune_DealsNothingButResolves()
    {
        DamageResult result = Resolve(20, DamageType.Poison, new DefenseSnapshot(-50, true, 2f));

        Assert.That(result.Amount, Is.Zero);
        Assert.That(result.Flags, Is.EqualTo(DamageFlags.Immune));
        Assert.That(result.Resolved, Is.True);
    }

    /// <summary>
    /// The attacker's dealt multiplier and the target's taken multiplier both scale the resisted amount.
    /// </summary>
    [Test]
    public void DamageMath_AppliesDealtAndTakenMultipliers()
    {
        DamageResult result = DamageMath.Resolve(20, DamageType.Fire, 1.25f, new DefenseSnapshot(50, false, 1.2f), TestCombat.Settings);

        Assert.That(result.Amount, Is.EqualTo(15));
    }

    /// <summary>
    /// Fractional results round to the nearest whole number, with halves rounding up.
    /// </summary>
    [Test]
    public void DamageMath_RoundsHalfAwayFromZero()
    {
        Assert.That(Resolve(5, DamageType.Fire, new DefenseSnapshot(50, false, 1f)).Amount, Is.EqualTo(3));
        Assert.That(Resolve(3, DamageType.Fire, new DefenseSnapshot(50, false, 1f)).Amount, Is.EqualTo(2));
        Assert.That(Resolve(9, DamageType.Fire, new DefenseSnapshot(70, false, 1f)).Amount, Is.EqualTo(3));
    }

    /// <summary>
    /// A non-immune hit reduced below the minimum by resistance or a zero dealt multiplier still deals the
    /// minimum of 1.
    /// </summary>
    [Test]
    public void DamageMath_HitsThatAreNotImmuneDealAtLeastTheMinimum()
    {
        Assert.That(Resolve(1, DamageType.Fire, new DefenseSnapshot(80, false, 1f)).Amount, Is.EqualTo(1));
        Assert.That(DamageMath.Resolve(20, DamageType.Fire, 0f, DefenseSnapshot.None, TestCombat.Settings).Amount, Is.EqualTo(1));
    }

    /// <summary>
    /// Zero and negative raw amounts do not resolve.
    /// </summary>
    [Test]
    public void DamageMath_NonPositiveAmounts_AreRejected()
    {
        Assert.That(Resolve(0, DamageType.Physical, DefenseSnapshot.None).Resolved, Is.False);
        Assert.That(Resolve(-5, DamageType.Physical, DefenseSnapshot.None).Resolved, Is.False);
    }

    /// <summary>
    /// Flags passed in, such as periodic, are kept and combined with the flags the resolution adds.
    /// </summary>
    [Test]
    public void DamageMath_KeepsTheFlagsItWasGiven()
    {
        DamageResult result = DamageMath.Resolve(10, DamageType.Poison, 1f, new DefenseSnapshot(50, false, 1f), TestCombat.Settings, DamageFlags.Periodic);

        Assert.That(result.Flags, Is.EqualTo(DamageFlags.Periodic | DamageFlags.Resisted));
    }

    /// <summary>
    /// A receiver takes its faction, summed per-type resistances and immunities from its profile; one without a
    /// profile has no faction and no defense.
    /// </summary>
    [Test]
    public void DamageReceiver_DefenseComesFromItsProfile()
    {
        DamageReceiver receiver = CreateUnit("Unit", Profile(Faction.Mobs, DamageTypeMask.Poison,
            new DamageResistance(DamageType.Fire, 30), new DamageResistance(DamageType.Fire, 20), new DamageResistance(DamageType.Frost, -25)));
        DamageReceiver bare = CreateUnit("Bare", null);

        Assert.That(receiver.Faction, Is.EqualTo(Faction.Mobs));
        Assert.That(receiver.GetDefense(DamageType.Fire).ResistancePercent, Is.EqualTo(50), "Entries for one type add up.");
        Assert.That(receiver.GetDefense(DamageType.Frost).ResistancePercent, Is.EqualTo(-25));
        Assert.That(receiver.GetDefense(DamageType.Physical).ResistancePercent, Is.Zero);
        Assert.That(receiver.GetDefense(DamageType.Poison).Immune, Is.True);
        Assert.That(receiver.GetDefense(DamageType.Fire).Immune, Is.False);
        Assert.That(bare.Faction, Is.EqualTo(Faction.None));
        Assert.That(bare.GetDefense(DamageType.Fire).ResistancePercent, Is.Zero);
        Assert.That(bare.GetDefense(DamageType.Fire).Immune, Is.False);
    }

    /// <summary>
    /// A resisted hit removes the reduced amount from health, and both the published report and the health damage
    /// event carry its type and resisted flag.
    /// </summary>
    [Test]
    public void DamageService_ResistedHit_ReportsTypeAndFlags()
    {
        DamageReceiver target = CreateUnit("Weasel", Profile(Faction.Mobs, DamageTypeMask.None, new DamageResistance(DamageType.Fire, 50)));
        CombatEvents events = new();
        DamageReport? report = null;
        events.DamageApplied += published => report = published;
        Health.DamageEvent? damaged = null;
        target.Health.Damaged += damage => damaged = damage;

        DamageResult result = TestCombat.CreateDamageService(FixedGameAuthority.Authoritative, events).ApplyDamage(target, 20, DamageType.Fire);

        Assert.That(result.Amount, Is.EqualTo(10));
        Assert.That(target.Health.CurrentHealth, Is.EqualTo(target.Health.MaxHealth - 10));
        Assert.That(report.HasValue, Is.True);
        Assert.That(report.Value.Amount, Is.EqualTo(10));
        Assert.That(report.Value.Type, Is.EqualTo(DamageType.Fire));
        Assert.That(report.Value.Flags, Is.EqualTo(DamageFlags.Resisted));
        Assert.That(damaged.HasValue, Is.True);
        Assert.That(damaged.Value.Type, Is.EqualTo(DamageType.Fire));
        Assert.That(damaged.Value.Flags, Is.EqualTo(DamageFlags.Resisted));
    }

    /// <summary>
    /// An overkill hit reports the health actually lost as its amount and the full hit as its raw amount.
    /// </summary>
    [Test]
    public void DamageService_ReportsTheHpActuallyLost()
    {
        DamageReceiver target = CreateUnit("Weasel", null);
        DamageService damage = TestCombat.CreateDamageService(FixedGameAuthority.Authoritative);
        damage.ApplyDamage(target, target.Health.MaxHealth - 3);

        DamageResult result = damage.ApplyDamage(target, 20);

        Assert.That(result.Amount, Is.EqualTo(3));
        Assert.That(result.RawAmount, Is.EqualTo(20));
        Assert.That(target.Health.IsDead, Is.True);
    }

    /// <summary>
    /// An immune hit resolves without health loss or a damage event, raises the receiver's immune event with the
    /// type, and is published as an immune report.
    /// </summary>
    [Test]
    public void DamageService_ImmuneHit_ResolvesWithoutHpLossAndIsReported()
    {
        DamageReceiver target = CreateUnit("Golem", Profile(Faction.Mobs, DamageTypeMask.Fire));
        CombatEvents events = new();
        DamageReport? report = null;
        events.DamageApplied += published => report = published;
        int damagedEvents = 0;
        target.Health.Damaged += _ => damagedEvents++;
        DamageType? immuneType = null;
        target.ImmuneHit += (type, _) => immuneType = type;

        DamageResult result = TestCombat.CreateDamageService(FixedGameAuthority.Authoritative, events).ApplyDamage(target, 20, DamageType.Fire);

        Assert.That(result.Resolved, Is.True);
        Assert.That(result.IsImmune, Is.True);
        Assert.That(target.Health.CurrentHealth, Is.EqualTo(target.Health.MaxHealth));
        Assert.That(damagedEvents, Is.Zero);
        Assert.That(immuneType, Is.EqualTo(DamageType.Fire));
        Assert.That(report.HasValue, Is.True);
        Assert.That(report.Value.IsImmune, Is.True);
        Assert.That(report.Value.Amount, Is.Zero);
    }

    /// <summary>
    /// A hit from a source of the same faction is refused and unreported, while hits across factions, from a
    /// source without a faction, on a target without one, or without a source all land.
    /// </summary>
    [Test]
    public void DamageService_HostileHits_OnlyLandAcrossFactions()
    {
        DamageReceiver player = CreateUnit("Player", Profile(Faction.Players));
        DamageReceiver teammate = CreateUnit("Teammate", Profile(Faction.Players));
        DamageReceiver mob = CreateUnit("Mob", Profile(Faction.Mobs));
        DamageReceiver neutral = CreateUnit("Neutral", null);
        CombatEvents events = new();
        int reports = 0;
        events.DamageApplied += _ => reports++;
        DamageService damage = TestCombat.CreateDamageService(FixedGameAuthority.Authoritative, events);

        Assert.That(damage.ApplyDamage(teammate, 5, DamageType.Physical, player.gameObject).Resolved, Is.False, "No friendly fire.");
        Assert.That(teammate.Health.CurrentHealth, Is.EqualTo(teammate.Health.MaxHealth));
        Assert.That(reports, Is.Zero);

        Assert.That(damage.ApplyDamage(mob, 5, DamageType.Physical, player.gameObject).Amount, Is.EqualTo(5));
        Assert.That(damage.ApplyDamage(player, 5, DamageType.Physical, mob.gameObject).Amount, Is.EqualTo(5));
        Assert.That(damage.ApplyDamage(player, 5, DamageType.Physical, neutral.gameObject).Amount, Is.EqualTo(5), "A source without a faction hits anyone.");
        Assert.That(damage.ApplyDamage(neutral, 5, DamageType.Physical, player.gameObject).Amount, Is.EqualTo(5));
        Assert.That(damage.ApplyDamage(teammate, 5).Amount, Is.EqualTo(5), "A hit without a source hits anyone.");
    }

    /// <summary>
    /// A source object with no receiver of its own takes its faction from a parent, so a player's weapon cannot
    /// hit a teammate.
    /// </summary>
    [Test]
    public void DamageService_FindsTheSourceFactionOnItsParents()
    {
        DamageReceiver player = CreateUnit("Player", Profile(Faction.Players));
        DamageReceiver teammate = CreateUnit("Teammate", Profile(Faction.Players));
        GameObject weapon = new("Weapon");
        weapon.transform.SetParent(player.transform);

        DamageResult result = TestCombat.CreateDamageService(FixedGameAuthority.Authoritative).ApplyDamage(teammate, 5, DamageType.Physical, weapon);

        Assert.That(result.Resolved, Is.False);
    }

    /// <summary>
    /// Without authority a replicated immune hit leaves health untouched and is published as an immune report of
    /// its type.
    /// </summary>
    [Test]
    public void DamageService_ReplicatedImmuneHit_IsReportedWithoutAuthority()
    {
        DamageReceiver target = CreateUnit("Golem", null);
        CombatEvents events = new();
        DamageReport? report = null;
        events.DamageApplied += published => report = published;

        DamageResult result = TestCombat.CreateDamageService(new FixedGameAuthority(false), events)
            .ApplyReplicatedHit(target, 0, DamageType.Fire, DamageFlags.Immune);

        Assert.That(result.IsImmune, Is.True);
        Assert.That(target.Health.CurrentHealth, Is.EqualTo(target.Health.MaxHealth));
        Assert.That(report.HasValue, Is.True);
        Assert.That(report.Value.IsImmune, Is.True);
        Assert.That(report.Value.Type, Is.EqualTo(DamageType.Fire));
    }

    /// <summary>
    /// Healing returns and reports only the health restored up to the maximum, ignores non-positive amounts and
    /// full health, and never revives a dead unit.
    /// </summary>
    [Test]
    public void Health_Heal_CapsAtMaxAndNeverRevives()
    {
        DamageReceiver unit = CreateUnit("Unit", null);
        Health health = unit.Health;
        int healedEvents = 0;
        int lastHealed = 0;
        health.Healed += (_, amount) =>
        {
            healedEvents++;
            lastHealed = amount;
        };

        health.ApplyDamage(4);
        Assert.That(health.Heal(10), Is.EqualTo(4));
        Assert.That(lastHealed, Is.EqualTo(4));
        Assert.That(health.CurrentHealth, Is.EqualTo(health.MaxHealth));
        Assert.That(health.Heal(10), Is.Zero, "Nothing to heal at full health.");
        Assert.That(health.Heal(0), Is.Zero);
        Assert.That(health.Heal(-3), Is.Zero);

        health.ApplyDamage(health.MaxHealth);
        Assert.That(health.Heal(5), Is.Zero, "Healing never brings a dead unit back.");
        Assert.That(health.IsDead, Is.True);
        Assert.That(healedEvents, Is.EqualTo(1));
    }

    /// <summary>
    /// The service refuses to heal across factions or without authority, and publishes a heal report with the
    /// target, amount, source and popup position when it heals a teammate.
    /// </summary>
    [Test]
    public void DamageService_Heals_OnlyWithinAFactionAndWithAuthority()
    {
        DamageReceiver player = CreateUnit("Player", Profile(Faction.Players));
        DamageReceiver teammate = CreateUnit("Teammate", Profile(Faction.Players));
        DamageReceiver mob = CreateUnit("Mob", Profile(Faction.Mobs));
        teammate.Health.ApplyDamage(6);
        mob.Health.ApplyDamage(6);
        CombatEvents events = new();
        HealReport? report = null;
        events.HealApplied += published => report = published;
        DamageService damage = TestCombat.CreateDamageService(FixedGameAuthority.Authoritative, events);

        Assert.That(damage.ApplyHeal(mob, 5, player.gameObject), Is.Zero, "Players never heal mobs.");
        Assert.That(report.HasValue, Is.False);
        Assert.That(TestCombat.CreateDamageService(new FixedGameAuthority(false)).ApplyHeal(teammate, 5, player.gameObject), Is.Zero);
        Assert.That(teammate.Health.CurrentHealth, Is.EqualTo(teammate.Health.MaxHealth - 6));

        Assert.That(damage.ApplyHeal(teammate, 5, player.gameObject), Is.EqualTo(5));
        Assert.That(report.HasValue, Is.True);
        Assert.That(report.Value.Target, Is.SameAs(teammate.Health));
        Assert.That(report.Value.Amount, Is.EqualTo(5));
        Assert.That(report.Value.Source, Is.SameAs(player.gameObject));
        Assert.That(report.Value.PopupWorldPosition, Is.EqualTo(teammate.PopupWorldPosition));
    }

    /// <summary>
    /// Without authority a replicated heal is applied and published once.
    /// </summary>
    [Test]
    public void DamageService_ReplicatedHeal_AppliesWithoutAuthority()
    {
        DamageReceiver unit = CreateUnit("Unit", null);
        unit.Health.ApplyDamage(6);
        CombatEvents events = new();
        int reports = 0;
        events.HealApplied += _ => reports++;

        int healed = TestCombat.CreateDamageService(new FixedGameAuthority(false), events).ApplyReplicatedHeal(unit, 4);

        Assert.That(healed, Is.EqualTo(4));
        Assert.That(unit.Health.CurrentHealth, Is.EqualTo(unit.Health.MaxHealth - 2));
        Assert.That(reports, Is.EqualTo(1));
    }

    /// <summary>
    /// A melee hit on an immune target counts as an attack and starts the cooldown, so it is reported once rather
    /// than every frame.
    /// </summary>
    [Test]
    public void MeleeDamageDealer_ImmuneTarget_StartsTheCooldown()
    {
        DamageReceiver attacker = CreateUnit("Attacker", Profile(Faction.Mobs));
        MeleeDamageDealer dealer = attacker.gameObject.AddComponent<MeleeDamageDealer>();
        DamageReceiver target = CreateUnit("Target", Profile(Faction.Players, DamageTypeMask.Physical));
        CombatEvents events = new();
        int reports = 0;
        events.DamageApplied += _ => reports++;
        ManualClock clock = new();
        dealer.Construct(clock, TestCombat.CreateHitService(FixedGameAuthority.Authoritative, events));
        dealer.ResetCooldown();

        Assert.That(dealer.TryDealDamage(target.transform), Is.True, "An immune hit still lands as an attack.");
        Assert.That(dealer.TryDealDamage(target.transform), Is.False, "So it waits for its cooldown instead of reporting Immune every frame.");
        Assert.That(reports, Is.EqualTo(1));
        Assert.That(target.Health.CurrentHealth, Is.EqualTo(target.Health.MaxHealth));
    }

    /// <summary>
    /// A melee dealer initialised from a <see cref="MobConfig"/> deals its configured damage type, which the
    /// target's resistance to that type reduces.
    /// </summary>
    [Test]
    public void MeleeDamageDealer_DealsTheConfiguredDamageType()
    {
        MobConfig config = MobConfig.Create(attackDamage: 10, attackDamageType: DamageType.Poison);
        created.Add(config);
        DamageReceiver attacker = CreateUnit("Attacker", null);
        MeleeDamageDealer dealer = attacker.gameObject.AddComponent<MeleeDamageDealer>();
        DamageReceiver target = CreateUnit("Target", Profile(Faction.Players, DamageTypeMask.None, new DamageResistance(DamageType.Poison, 50)));
        dealer.Construct(new ManualClock(), TestCombat.CreateHitService(FixedGameAuthority.Authoritative));
        dealer.Initialize(config);
        dealer.ResetCooldown();

        dealer.TryDealDamage(target.transform);

        Assert.That(dealer.DamageType, Is.EqualTo(DamageType.Poison));
        Assert.That(target.Health.CurrentHealth, Is.EqualTo(target.Health.MaxHealth - 5));
    }

    /// <summary>
    /// The presenter colours damage popups by type, shows the immune text and "+amount" heals in their colours,
    /// and scales periodic hits by the periodic popup scale.
    /// </summary>
    [Test]
    public void DamagePopupPresenter_ColoursByTypeShowsImmuneAndHeals_AndShrinksPeriodicHits()
    {
        GameObject cameraObject = Track(new GameObject("Camera"));
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.orthographic = true;
        DamagePopupLayer layer = CreatePopupLayer();
        CombatEvents events = new();
        CombatSettings settings = TestCombat.Settings;
        DamagePopupPresenter presenter = new(events, layer, camera, settings);
        presenter.Start();

        events.Publish(new DamageReport(null, 20, DamageType.Fire, DamageFlags.None, null, Vector3.zero));
        events.Publish(new DamageReport(null, 0, DamageType.Frost, DamageFlags.Immune, null, Vector3.zero));
        events.Publish(new HealReport(null, 5, null, Vector3.zero));
        events.Publish(new DamageReport(null, 3, DamageType.Poison, DamageFlags.Periodic, null, Vector3.zero));
        presenter.Dispose();

        TextMeshProUGUI[] texts = layer.GetComponentsInChildren<TextMeshProUGUI>();
        Assert.That(texts.Length, Is.EqualTo(4));
        foreach (TextMeshProUGUI text in texts)
        {
            float expectedScale = text.text == "3" ? settings.PeriodicPopupScale : 1f;
            Assert.That(text.transform.localScale.x, Is.EqualTo(expectedScale), $"Popup '{text.text}' scale.");
        }

        AssertPopup(texts, "20", settings.GetColor(DamageType.Fire));
        AssertPopup(texts, settings.ImmuneText, settings.ImmuneColor);
        AssertPopup(texts, "+5", settings.HealColor);
    }

    /// <summary>
    /// Every damage type has a colour distinct from all the others.
    /// </summary>
    [Test]
    public void CombatSettings_GivesEachDamageTypeItsOwnColour()
    {
        CombatSettings settings = TestCombat.Settings;
        HashSet<Color> colours = new();
        foreach (DamageType type in System.Enum.GetValues(typeof(DamageType)))
        {
            colours.Add(settings.GetColor(type));
        }

        Assert.That(colours.Count, Is.EqualTo(System.Enum.GetValues(typeof(DamageType)).Length));
    }

    /// <summary>
    /// The player and weasel prefabs have 100 health and their factions, and the sword and default mob config deal
    /// their rescaled physical damage.
    /// </summary>
    [Test]
    public void Content_UsesTheRescaledNumbersAndFactionProfiles()
    {
        GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player/Player.prefab");
        GameObject weasel = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mobs/Weasel.prefab");
        PlayerWeapon sword = AssetDatabase.LoadAssetAtPath<PlayerWeapon>("Assets/Data/Weapons/Sword.asset");
        MobConfig mob = AssetDatabase.LoadAssetAtPath<MobConfig>("Assets/Settings/AI/Mob_Default.asset");

        Assert.That(player.GetComponent<Health>().MaxHealth, Is.EqualTo(100));
        Assert.That(weasel.GetComponent<Health>().MaxHealth, Is.EqualTo(100));
        Assert.That(player.GetComponent<DamageReceiver>().Faction, Is.EqualTo(Faction.Players));
        Assert.That(weasel.GetComponent<DamageReceiver>().Faction, Is.EqualTo(Faction.Mobs));
        Assert.That(sword.Damage, Is.EqualTo(20));
        Assert.That(sword.DamageType, Is.EqualTo(DamageType.Physical));
        Assert.That(mob.AttackDamage, Is.EqualTo(10));
        Assert.That(mob.AttackDamageType, Is.EqualTo(DamageType.Physical));
    }

    /// <summary>
    /// The combat settings asset has the expected resistance cap, floor and minimum damage, and the Gameplay scene
    /// references it.
    /// </summary>
    [Test]
    public void Content_GameplaySceneHasCombatSettings()
    {
        CombatSettings settings = AssetDatabase.LoadAssetAtPath<CombatSettings>("Assets/Data/Combat/CombatSettings.asset");
        string scene = System.IO.File.ReadAllText("Assets/Scenes/Gameplay.unity");
        string guid = AssetDatabase.AssetPathToGUID("Assets/Data/Combat/CombatSettings.asset");

        Assert.That(settings, Is.Not.Null);
        Assert.That(settings.ResistanceCap, Is.EqualTo(80));
        Assert.That(settings.ResistanceFloor, Is.EqualTo(-100));
        Assert.That(settings.MinimumDamage, Is.EqualTo(1));
        Assert.That(scene, Does.Contain("combatSettings: {fileID: 11400000, guid: " + guid));
    }

    /// <summary>
    /// Resolves a hit through <see cref="DamageMath"/> with a dealt multiplier of 1 and the test combat settings.
    /// </summary>
    private static DamageResult Resolve(int raw, DamageType type, DefenseSnapshot defense)
    {
        return DamageMath.Resolve(raw, type, 1f, defense, TestCombat.Settings);
    }

    /// <summary>
    /// Asserts that the first popup reading <paramref name="message"/> has <paramref name="color"/>, and fails
    /// when none does.
    /// </summary>
    private static void AssertPopup(TextMeshProUGUI[] texts, string message, Color color)
    {
        foreach (TextMeshProUGUI text in texts)
        {
            if (text.text == message)
            {
                Assert.That(text.color, Is.EqualTo(color), message);
                return;
            }
        }

        Assert.Fail($"No popup reads '{message}'.");
    }

    /// <summary>
    /// Creates a tracked combat profile with the given faction, immunities and resistances and no status tags.
    /// </summary>
    private CombatProfile Profile(Faction faction, DamageTypeMask immunities = DamageTypeMask.None, params DamageResistance[] resistances)
    {
        CombatProfile profile = CombatProfile.Create(faction, immunities, StatusTags.None, resistances);
        created.Add(profile);
        return profile;
    }

    /// <summary>
    /// Creates a tracked unit with default health and a receiver using <paramref name="profile"/> (null for none).
    /// </summary>
    private DamageReceiver CreateUnit(string name, CombatProfile profile)
    {
        GameObject unit = Track(new GameObject(name));
        unit.AddComponent<Health>();
        DamageReceiver receiver = unit.AddComponent<DamageReceiver>();
        receiver.SetProfile(profile);
        return receiver;
    }

    /// <summary>
    /// Creates a tracked screen-space overlay canvas with a <see cref="DamagePopupLayer"/> configured with an
    /// inactive text popup template.
    /// </summary>
    private DamagePopupLayer CreatePopupLayer()
    {
        GameObject canvasObject = Track(new GameObject("DamagePopupCanvas", typeof(RectTransform), typeof(Canvas)));
        canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        GameObject popupObject = Track(new GameObject("Popup", typeof(RectTransform), typeof(CanvasGroup), typeof(TextMeshProUGUI)));
        popupObject.SetActive(false);
        FloatingDamageText popupPrefab = popupObject.AddComponent<FloatingDamageText>();
        DamagePopupLayer layer = canvasObject.AddComponent<DamagePopupLayer>();
        layer.Configure(popupPrefab);
        return layer;
    }

    /// <summary>
    /// Records <paramref name="gameObject"/> for destruction in <see cref="TearDown"/> and returns it.
    /// </summary>
    private GameObject Track(GameObject gameObject)
    {
        created.Add(gameObject);
        return gameObject;
    }
}
