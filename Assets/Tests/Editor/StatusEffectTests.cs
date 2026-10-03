using System.Collections.Generic;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

public class StatusEffectTests
{
    private readonly List<Object> created = new();

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
    public void Set_Refresh_ResetsTheDurationOfItsOneInstance()
    {
        StatusEffectDefinition chill = Definition("Chill", StatusKind.Debuff, 5f);
        StatusEffectSet set = new();

        Assert.That(set.Apply(chill, null, 1f), Is.EqualTo(StatusApplyOutcome.Landed));
        set.Tick(3f, null);
        Assert.That(set.GetSnapshot(0).Remaining, Is.EqualTo(2f).Within(0.001f));

        Assert.That(set.Apply(chill, null, 1f), Is.EqualTo(StatusApplyOutcome.Refreshed));
        Assert.That(set.Count, Is.EqualTo(1));
        Assert.That(set.GetSnapshot(0).Remaining, Is.EqualTo(5f).Within(0.001f));
    }

    [Test]
    public void Set_AddStack_StacksUpToTheMaximumAndResetsTheDuration()
    {
        StatusEffectDefinition burn = Definition("Burn", StatusKind.Debuff, 4f, StatusStacking.AddStack, maxStacks: 3);
        StatusEffectSet set = new();

        Assert.That(set.Apply(burn, null, 1f), Is.EqualTo(StatusApplyOutcome.Landed));
        set.Tick(2f, null);
        Assert.That(set.Apply(burn, null, 1f), Is.EqualTo(StatusApplyOutcome.Stacked));
        Assert.That(set.Apply(burn, null, 1f), Is.EqualTo(StatusApplyOutcome.Stacked));
        Assert.That(set.Apply(burn, null, 1f), Is.EqualTo(StatusApplyOutcome.Refreshed), "At the cap a new application only refreshes.");

        Assert.That(set.Count, Is.EqualTo(1));
        Assert.That(set.GetStacks(burn), Is.EqualTo(3));
        Assert.That(set.GetSnapshot(0).Remaining, Is.EqualTo(4f).Within(0.001f));
    }

    [Test]
    public void Set_Independent_KeepsSeparateTimersAndReplacesTheOneClosestToExpiring()
    {
        StatusEffectDefinition poison = Definition("Poison", StatusKind.Debuff, 6f, StatusStacking.Independent, maxStacks: 2);
        StatusEffectSet set = new();

        set.Apply(poison, null, 1f);
        set.Tick(2f, null);
        set.Apply(poison, null, 1f);
        set.Tick(1f, null);

        Assert.That(set.Count, Is.EqualTo(2));
        Assert.That(RemainingTimes(set), Is.EquivalentTo(new[] { 3f, 5f }));

        Assert.That(set.Apply(poison, null, 1f), Is.EqualTo(StatusApplyOutcome.Landed));
        Assert.That(set.Count, Is.EqualTo(2), "The cap holds.");
        Assert.That(RemainingTimes(set), Is.EquivalentTo(new[] { 6f, 5f }), "The dose closest to expiring was replaced.");
        Assert.That(set.GetStacks(poison), Is.EqualTo(2));
    }

    [Test]
    public void Set_PeriodicTicks_StartAfterOneIntervalAndIncludeTheExpiryInstant()
    {
        StatusEffectDefinition burn = Definition("Burn", StatusKind.Debuff, 3f, periodic: StatusPeriodic.Damage, periodicAmount: 4, tickInterval: 1f);
        StatusEffectSet set = new();
        List<StatusTick> ticks = new();
        int changes = 0;
        set.Apply(burn, null, 1.5f);
        set.Changed += () => changes++;

        set.Tick(0.5f, ticks);
        Assert.That(ticks, Is.Empty, "Nothing ticks on landing.");

        for (int i = 0; i < 5; i++)
        {
            set.Tick(0.5f, ticks);
        }

        Assert.That(ticks.Count, Is.EqualTo(3));
        Assert.That(ticks[0].Amount, Is.EqualTo(4));
        Assert.That(ticks[0].DealtMultiplier, Is.EqualTo(1.5f));
        Assert.That(set.Count, Is.Zero, "Expired at its duration.");
        Assert.That(changes, Is.EqualTo(1), "Plain ticks are not changes; the expiry is.");
    }

    [Test]
    public void Set_ATickLargerThanTheIntervalCatchesUpEveryTick()
    {
        StatusEffectDefinition burn = Definition("Burn", StatusKind.Debuff, 4f, periodic: StatusPeriodic.Damage, periodicAmount: 1, tickInterval: 1f);
        StatusEffectSet set = new();
        List<StatusTick> ticks = new();
        set.Apply(burn, null, 1f);

        set.Tick(2.5f, ticks);

        Assert.That(ticks.Count, Is.EqualTo(2));
    }

    [Test]
    public void Set_ReapplyingKeepsTheTickPhase()
    {
        StatusEffectDefinition burn = Definition("Burn", StatusKind.Debuff, 3f, periodic: StatusPeriodic.Damage, periodicAmount: 1, tickInterval: 1f);
        StatusEffectSet set = new();
        List<StatusTick> ticks = new();
        set.Apply(burn, null, 1f);

        set.Tick(0.9f, ticks);
        set.Apply(burn, null, 1f);
        set.Tick(0.1f, ticks);

        Assert.That(ticks.Count, Is.EqualTo(1), "A refresh does not push the next tick back.");
    }

    [Test]
    public void Set_StacksScaleTicksMultipliersAndResistances()
    {
        StatusEffectDefinition burn = Definition("Burn", StatusKind.Debuff, 4f, StatusStacking.AddStack, maxStacks: 5,
            periodic: StatusPeriodic.Damage, periodicAmount: 3, damageDealt: 0.8f, damageTaken: 1.1f,
            resistances: new[] { new DamageResistance(DamageType.Fire, -10) });
        StatusEffectSet set = new();
        List<StatusTick> ticks = new();
        set.Apply(burn, null, 1f);
        set.Apply(burn, null, 1f);

        set.Tick(1f, ticks);

        Assert.That(ticks[0].Amount, Is.EqualTo(6));
        Assert.That(set.DamageDealtMultiplier, Is.EqualTo(0.64f).Within(0.0001f));
        Assert.That(set.DamageTakenMultiplier, Is.EqualTo(1.21f).Within(0.0001f));
        Assert.That(set.GetResistanceDelta(DamageType.Fire), Is.EqualTo(-20));
    }

    [Test]
    public void Set_CombinesStatusesAndRecomputesWhenOneEnds()
    {
        StatusEffectDefinition empower = Definition("Empower", StatusKind.Buff, 5f, damageDealt: 1.25f);
        StatusEffectDefinition vulnerable = Definition("Vulnerable", StatusKind.Debuff, 2f, damageTaken: 1.2f);
        StatusEffectDefinition fortify = Definition("Fortify", StatusKind.Buff, 5f,
            resistances: new[] { new DamageResistance(DamageType.Fire, 30), new DamageResistance(DamageType.Frost, 30) });
        StatusEffectDefinition invulnerable = Definition("Invulnerable", StatusKind.Buff, 5f, damageImmunity: DamageTypeMask.All);
        StatusEffectSet set = new();

        set.Apply(empower, null, 1f);
        set.Apply(vulnerable, null, 1f);
        set.Apply(fortify, null, 1f);
        set.Apply(invulnerable, null, 1f);

        Assert.That(set.DamageDealtMultiplier, Is.EqualTo(1.25f).Within(0.0001f));
        Assert.That(set.DamageTakenMultiplier, Is.EqualTo(1.2f).Within(0.0001f));
        Assert.That(set.GetResistanceDelta(DamageType.Fire), Is.EqualTo(30));
        Assert.That(set.GetResistanceDelta(DamageType.Physical), Is.Zero);
        Assert.That(set.GrantedDamageImmunity, Is.EqualTo(DamageTypeMask.All));

        set.Tick(2f, null);
        Assert.That(set.DamageTakenMultiplier, Is.EqualTo(1f), "Vulnerable expired.");

        Assert.That(set.Remove(invulnerable), Is.True);
        Assert.That(set.GrantedDamageImmunity, Is.EqualTo(DamageTypeMask.None));
        Assert.That(set.Remove(invulnerable), Is.False);

        set.Clear();
        Assert.That(set.DamageDealtMultiplier, Is.EqualTo(1f));
        Assert.That(set.GetResistanceDelta(DamageType.Fire), Is.Zero);
    }

    [Test]
    public void Set_StatusImmunities_BlockByTagAndAGrantRemovesWhatItNowBlocks()
    {
        StatusEffectDefinition stun = Definition("Stun", StatusKind.Debuff, 2f, tags: StatusTags.Stun);
        StatusEffectDefinition burn = Definition("Burn", StatusKind.Debuff, 4f, tags: StatusTags.Burn);
        StatusEffectDefinition unstoppable = Definition("Unstoppable", StatusKind.Buff, 3f, statusImmunity: StatusTags.Stun | StatusTags.Root);
        StatusEffectSet set = new();

        Assert.That(set.Apply(burn, null, 1f, StatusTags.Burn), Is.EqualTo(StatusApplyOutcome.Immune), "A base immunity blocks the tag.");
        Assert.That(set.Count, Is.Zero);

        set.Apply(stun, null, 1f);
        set.Apply(burn, null, 1f);
        set.Apply(unstoppable, null, 1f);

        Assert.That(set.GetStacks(stun), Is.Zero, "Landing Unstoppable removes the active stun.");
        Assert.That(set.GetStacks(burn), Is.EqualTo(1));
        Assert.That(set.Apply(stun, null, 1f), Is.EqualTo(StatusApplyOutcome.Immune), "And blocks new ones while it lasts.");
    }

    [Test]
    public void Set_SyncFrom_MirrorsSnapshotsWithoutTicking()
    {
        StatusEffectDefinition fortify = Definition("Fortify", StatusKind.Buff, 8f, resistances: new[] { new DamageResistance(DamageType.Fire, 30) });
        StatusEffectSet set = new();
        int changes = 0;
        set.Changed += () => changes++;

        set.SyncFrom(new[] { new StatusSnapshot(fortify, 7, 2, 4.5f), new StatusSnapshot(null, 8, 1, 1f) });

        Assert.That(set.Count, Is.EqualTo(1), "Unknown definitions are skipped.");
        Assert.That(set.GetSnapshot(0).InstanceId, Is.EqualTo(7));
        Assert.That(set.GetSnapshot(0).Remaining, Is.EqualTo(4.5f));
        Assert.That(set.GetResistanceDelta(DamageType.Fire), Is.EqualTo(60));
        Assert.That(changes, Is.EqualTo(1));
    }

    [Test]
    public void Set_TickDoesNotAllocateOnceWarm()
    {
        StatusEffectDefinition burn = Definition("Burn", StatusKind.Debuff, 100f, periodic: StatusPeriodic.Damage, periodicAmount: 1, tickInterval: 0.5f);
        StatusEffectDefinition fortify = Definition("Fortify", StatusKind.Buff, 100f);
        StatusEffectSet set = new();
        List<StatusTick> ticks = new(16);
        set.Apply(burn, null, 1f);
        set.Apply(fortify, null, 1f);
        set.Tick(1f, ticks);

        Assert.That(() =>
        {
            ticks.Clear();
            set.Tick(1f, ticks);
        }, Is.Not.AllocatingGCMemory());
    }

    [Test]
    public void Service_WithoutAuthority_AppliesNothing()
    {
        DamageReceiver unit = Unit("Unit", Faction.Mobs);
        Services services = new(false);

        Assert.That(services.Statuses.Apply(unit, Definition("Burn", StatusKind.Debuff, 4f), null), Is.EqualTo(StatusApplyOutcome.NoAuthority));
        Assert.That(unit.Statuses.Count, Is.Zero);
    }

    [Test]
    public void Service_Debuffs_LandAcrossFactions_AndBuffs_WithinOne()
    {
        DamageReceiver player = Unit("Player", Faction.Players);
        DamageReceiver teammate = Unit("Teammate", Faction.Players);
        DamageReceiver mob = Unit("Mob", Faction.Mobs);
        StatusEffectDefinition burn = Definition("Burn", StatusKind.Debuff, 4f);
        StatusEffectDefinition fortify = Definition("Fortify", StatusKind.Buff, 4f);
        StatusEffectService statuses = new Services(true).Statuses;

        Assert.That(statuses.Apply(teammate, burn, player.gameObject), Is.EqualTo(StatusApplyOutcome.WrongFaction));
        Assert.That(statuses.Apply(mob, fortify, player.gameObject), Is.EqualTo(StatusApplyOutcome.WrongFaction));
        Assert.That(statuses.Apply(mob, burn, player.gameObject), Is.EqualTo(StatusApplyOutcome.Landed));
        Assert.That(statuses.Apply(player, burn, mob.gameObject), Is.EqualTo(StatusApplyOutcome.Landed));
        Assert.That(statuses.Apply(teammate, fortify, player.gameObject), Is.EqualTo(StatusApplyOutcome.Landed));
        Assert.That(statuses.Apply(player, fortify, player.gameObject), Is.EqualTo(StatusApplyOutcome.Landed), "A unit can buff itself.");
        Assert.That(statuses.Apply(teammate, burn, null), Is.EqualTo(StatusApplyOutcome.Landed), "A status without a source lands on anyone.");
    }

    [Test]
    public void Service_RejectsDeadTargetsAndUnitsWithoutStatuses()
    {
        DamageReceiver dead = Unit("Dead", Faction.Mobs);
        dead.Health.ApplyDamage(dead.Health.MaxHealth);
        GameObject bare = Track(new GameObject("Bare"));
        bare.AddComponent<Health>();
        DamageReceiver bareReceiver = bare.AddComponent<DamageReceiver>();
        StatusEffectDefinition burn = Definition("Burn", StatusKind.Debuff, 4f);
        StatusEffectService statuses = new Services(true).Statuses;

        Assert.That(statuses.Apply(dead, burn, null), Is.EqualTo(StatusApplyOutcome.TargetDead));
        Assert.That(statuses.Apply(bareReceiver, burn, null), Is.EqualTo(StatusApplyOutcome.Invalid));
        Assert.That(statuses.Apply(null, burn, null), Is.EqualTo(StatusApplyOutcome.Invalid));
        Assert.That(statuses.Apply(dead, null, null), Is.EqualTo(StatusApplyOutcome.Invalid));
    }

    [Test]
    public void Service_PeriodicDamage_UsesTheCapturedMultiplierAfterTheSourceIsGone()
    {
        DamageReceiver player = Unit("Player", Faction.Players);
        DamageReceiver mob = Unit("Mob", Faction.Mobs, new DamageResistance(DamageType.Fire, 50));
        StatusEffectDefinition empower = Definition("Empower", StatusKind.Buff, 30f, damageDealt: 2f);
        StatusEffectDefinition burn = Definition("Burn", StatusKind.Debuff, 4f, periodic: StatusPeriodic.Damage, periodicAmount: 3, periodicType: DamageType.Fire);
        Services services = new(true);
        List<DamageReport> reports = new();
        services.Events.DamageApplied += reports.Add;

        services.Statuses.Apply(player, empower, player.gameObject);
        services.Statuses.Apply(mob, burn, player.gameObject);
        Object.DestroyImmediate(player.gameObject);
        services.Statuses.Advance(1f);

        Assert.That(reports.Count, Is.EqualTo(1));
        Assert.That(reports[0].Amount, Is.EqualTo(3), "3 x 2 (captured Empower) x 0.5 (Fire resistance).");
        Assert.That(reports[0].Type, Is.EqualTo(DamageType.Fire));
        Assert.That(reports[0].Flags & DamageFlags.Periodic, Is.EqualTo(DamageFlags.Periodic));
    }

    [Test]
    public void Service_PeriodicHeal_Heals()
    {
        DamageReceiver player = Unit("Player", Faction.Players);
        player.Health.ApplyDamage(6);
        StatusEffectDefinition regeneration = Definition("Regeneration", StatusKind.Buff, 5f, periodic: StatusPeriodic.Heal, periodicAmount: 4);
        Services services = new(true);
        int heals = 0;
        services.Events.HealApplied += _ => heals++;

        services.Statuses.Apply(player, regeneration, player.gameObject);
        services.Statuses.Advance(2f);

        Assert.That(player.Health.CurrentHealth, Is.EqualTo(player.Health.MaxHealth));
        Assert.That(heals, Is.EqualTo(2));
    }

    [Test]
    public void Service_TicksOnTheClockAndStopsWhenItStands()
    {
        DamageReceiver mob = Unit("Mob", Faction.Mobs);
        StatusEffectDefinition burn = Definition("Burn", StatusKind.Debuff, 10f, periodic: StatusPeriodic.Damage, periodicAmount: 1);
        ManualClock clock = new();
        Services services = new(true, clock);
        services.Statuses.Tick();
        services.Statuses.Apply(mob, burn, null);

        clock.Advance(2f);
        services.Statuses.Tick();
        Assert.That(mob.Health.CurrentHealth, Is.EqualTo(mob.Health.MaxHealth - 2));

        services.Statuses.Tick();
        services.Statuses.Tick();
        Assert.That(mob.Health.CurrentHealth, Is.EqualTo(mob.Health.MaxHealth - 2), "A clock that stands still (time scale 0) ticks nothing.");
        Assert.That(mob.Statuses.GetSnapshot(0).Remaining, Is.EqualTo(8f).Within(0.001f));
    }

    [Test]
    public void Service_StatusesChangeTheDamageUnitsDealAndTake()
    {
        DamageReceiver player = Unit("Player", Faction.Players);
        DamageReceiver mob = Unit("Mob", Faction.Mobs);
        Services services = new(true);

        services.Statuses.Apply(player, Definition("Empower", StatusKind.Buff, 10f, damageDealt: 1.25f), player.gameObject);
        services.Statuses.Apply(mob, Definition("Vulnerable", StatusKind.Debuff, 10f, damageTaken: 1.2f), player.gameObject);
        Assert.That(services.Damage.ApplyDamage(mob, 20, DamageType.Physical, player.gameObject).Amount, Is.EqualTo(30));

        services.Statuses.Apply(player, Definition("Fortify", StatusKind.Buff, 10f, resistances: new[] { new DamageResistance(DamageType.Physical, 50) }), player.gameObject);
        Assert.That(services.Damage.ApplyDamage(player, 20, DamageType.Physical, mob.gameObject).Amount, Is.EqualTo(10));

        services.Statuses.Apply(player, Definition("Invulnerable", StatusKind.Buff, 10f, damageImmunity: DamageTypeMask.All), player.gameObject);
        Assert.That(services.Damage.ApplyDamage(player, 20, DamageType.True, mob.gameObject).IsImmune, Is.True);
    }

    [Test]
    public void Service_BlockedStatus_IsReportedForAnImmunePopup()
    {
        DamageReceiver golem = Unit("Golem", Faction.Mobs);
        golem.SetProfile(Profile(Faction.Mobs, StatusTags.Stun));
        StatusEffectDefinition stun = Definition("Stun", StatusKind.Debuff, 2f, tags: StatusTags.Stun);
        Services services = new(true);
        StatusReport? report = null;
        services.Events.StatusBlocked += published => report = published;
        StatusEffectDefinition blocked = null;
        golem.Statuses.Blocked += definition => blocked = definition;

        Assert.That(services.Statuses.Apply(golem, stun, null), Is.EqualTo(StatusApplyOutcome.Immune));

        Assert.That(blocked, Is.SameAs(stun));
        Assert.That(report.HasValue, Is.True);
        Assert.That(report.Value.Definition, Is.SameAs(stun));
        Assert.That(report.Value.Target, Is.SameAs(golem.Health));
        Assert.That(golem.Statuses.Count, Is.Zero);
    }

    [Test]
    public void Service_DeathClearsStatusesAndTheUnitStopsBeingTracked()
    {
        DamageReceiver mob = Unit("Mob", Faction.Mobs);
        Services services = new(true);
        services.Statuses.Apply(mob, Definition("Burn", StatusKind.Debuff, 10f, periodic: StatusPeriodic.Damage, periodicAmount: 1), null);
        Assert.That(services.Statuses.TrackedUnitCount, Is.EqualTo(1));

        services.Damage.ApplyDamage(mob, mob.Health.MaxHealth);
        services.Statuses.Advance(1f);

        Assert.That(mob.Statuses.Count, Is.Zero);
        Assert.That(services.Statuses.TrackedUnitCount, Is.Zero);
    }

    [Test]
    public void Service_HeldUnits_NeitherTickNorRunDown()
    {
        DamageReceiver player = Unit("Player", Faction.Players);
        FakeHold hold = new() { IsHeld = true };
        player.Statuses.SetHold(hold);
        Services services = new(true);
        services.Statuses.Apply(player, Definition("Burn", StatusKind.Debuff, 4f, periodic: StatusPeriodic.Damage, periodicAmount: 1), null);

        services.Statuses.Advance(2f);
        Assert.That(player.Health.CurrentHealth, Is.EqualTo(player.Health.MaxHealth));
        Assert.That(player.Statuses.GetSnapshot(0).Remaining, Is.EqualTo(4f).Within(0.001f));

        hold.IsHeld = false;
        services.Statuses.Advance(2f);
        Assert.That(player.Health.CurrentHealth, Is.EqualTo(player.Health.MaxHealth - 2));
    }

    [Test]
    public void Service_RemoveAndClearAll()
    {
        DamageReceiver mob = Unit("Mob", Faction.Mobs);
        StatusEffectDefinition burn = Definition("Burn", StatusKind.Debuff, 4f);
        StatusEffectDefinition chill = Definition("Chill", StatusKind.Debuff, 4f);
        Services services = new(true);
        services.Statuses.Apply(mob, burn, null);
        services.Statuses.Apply(mob, chill, null);

        Assert.That(services.Statuses.Remove(mob, burn), Is.True);
        Assert.That(mob.Statuses.GetStacks(burn), Is.Zero);
        services.Statuses.ClearAll(mob);
        Assert.That(mob.Statuses.Count, Is.Zero);
    }

    [Test]
    public void HitService_AppliesStatusesOnlyToSurvivorsOfAHitThatLanded()
    {
        DamageReceiver player = Unit("Player", Faction.Players);
        DamageReceiver teammate = Unit("Teammate", Faction.Players);
        DamageReceiver mob = Unit("Mob", Faction.Mobs);
        DamageReceiver golem = Unit("Golem", Faction.Mobs);
        golem.SetProfile(Profile(Faction.Mobs, StatusTags.None, DamageTypeMask.Fire));
        StatusEffectDefinition burn = Definition("Burn", StatusKind.Debuff, 4f);
        Services services = new(true);
        Hit fireHit = new(20, DamageType.Fire, new[] { burn });

        Assert.That(services.Hits.ApplyHit(mob, fireHit, player.gameObject).Amount, Is.EqualTo(20));
        Assert.That(mob.Statuses.GetStacks(burn), Is.EqualTo(1));

        services.Hits.ApplyHit(teammate, fireHit, player.gameObject);
        Assert.That(teammate.Statuses.Count, Is.Zero, "No friendly fire, so no statuses either.");

        services.Hits.ApplyHit(golem, fireHit, player.gameObject);
        Assert.That(golem.Statuses.GetStacks(burn), Is.EqualTo(1), "An immune hit still tries its statuses.");

        DamageReceiver weak = Unit("Weak", Faction.Mobs);
        services.Hits.ApplyHit(weak, new Hit(weak.Health.MaxHealth, DamageType.Fire, new[] { burn }), player.gameObject);
        Assert.That(weak.Statuses.Count, Is.Zero, "A target the hit killed gets no statuses.");

        DamageReceiver other = Unit("Other", Faction.Mobs);
        Assert.That(services.Hits.ApplyHit(other, new Hit(0, DamageType.Physical, new[] { burn }), player.gameObject).Resolved, Is.False);
        Assert.That(other.Statuses.GetStacks(burn), Is.EqualTo(1), "A hit without damage only applies its statuses.");
        Assert.That(other.Health.CurrentHealth, Is.EqualTo(other.Health.MaxHealth));
    }

    [Test]
    public void MeleeDamageDealer_AppliesTheConfiguredStatuses()
    {
        StatusEffectDefinition poison = Definition("Poison", StatusKind.Debuff, 6f);
        MobConfig config = ScriptableObject.CreateInstance<MobConfig>();
        created.Add(config);
        config.attackDamage = 10;
        config.attackStatuses = new[] { poison };
        DamageReceiver mob = Unit("Mob", Faction.Mobs);
        DamageReceiver player = Unit("Player", Faction.Players);
        MeleeDamageDealer dealer = mob.gameObject.AddComponent<MeleeDamageDealer>();
        Services services = new(true);
        dealer.Construct(new ManualClock(), services.Hits);
        dealer.Initialize(config);
        dealer.ResetCooldown();

        Assert.That(dealer.TryDealDamage(player.transform), Is.True);
        Assert.That(player.Statuses.GetStacks(poison), Is.EqualTo(1));
    }

    [Test]
    public void DamagePopupPresenter_ShowsImmuneForABlockedStatus()
    {
        Camera camera = Track(new GameObject("Camera")).AddComponent<Camera>();
        GameObject canvasObject = Track(new GameObject("DamagePopupCanvas", typeof(RectTransform), typeof(Canvas)));
        canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        GameObject popupObject = Track(new GameObject("Popup", typeof(RectTransform), typeof(CanvasGroup), typeof(TextMeshProUGUI)));
        popupObject.SetActive(false);
        FloatingDamageText popupPrefab = popupObject.AddComponent<FloatingDamageText>();
        DamagePopupLayer layer = canvasObject.AddComponent<DamagePopupLayer>();
        layer.Configure(popupPrefab);
        CombatEvents events = new();
        DamagePopupPresenter presenter = new(events, layer, camera, TestCombat.Settings);
        presenter.Start();

        events.PublishBlocked(new StatusReport(null, Definition("Stun", StatusKind.Debuff, 1f), null, Vector3.zero));
        presenter.Dispose();

        TextMeshProUGUI[] texts = layer.GetComponentsInChildren<TextMeshProUGUI>();
        Assert.That(texts.Length, Is.EqualTo(1));
        Assert.That(texts[0].text, Is.EqualTo(TestCombat.Settings.ImmuneText));
        Assert.That(texts[0].color, Is.EqualTo(TestCombat.Settings.ImmuneColor));
    }

    [Test]
    public void Content_CatalogListsEveryStatusOnceAndTheSettingsUseIt()
    {
        CombatSettings settings = AssetDatabase.LoadAssetAtPath<CombatSettings>("Assets/Data/Combat/CombatSettings.asset");
        StatusEffectCatalog catalog = settings.StatusCatalog;
        Assert.That(catalog, Is.Not.Null);

        HashSet<StatusEffectDefinition> listed = new();
        for (int i = 0; i < catalog.Count; i++)
        {
            StatusEffectDefinition definition = catalog.Get(i);
            Assert.That(definition, Is.Not.Null, $"Catalog entry {i} is empty.");
            Assert.That(listed.Add(definition), Is.True, $"{definition.name} is listed twice.");
            Assert.That(catalog.IndexOf(definition), Is.EqualTo(i));
        }

        foreach (string guid in AssetDatabase.FindAssets("t:StatusEffectDefinition"))
        {
            StatusEffectDefinition definition = AssetDatabase.LoadAssetAtPath<StatusEffectDefinition>(AssetDatabase.GUIDToAssetPath(guid));
            Assert.That(listed.Contains(definition), Is.True, $"{definition.name} is not in the catalog.");
        }

        string[] expected = { "Burn", "Poison", "Regeneration", "Fortify", "Vulnerable", "Empower", "Invulnerable" };
        foreach (string displayName in expected)
        {
            Assert.That(FindByName(catalog, displayName), Is.Not.Null, $"Missing test status {displayName}.");
        }

        Assert.That(FindByName(catalog, "Burn").Stacking, Is.EqualTo(StatusStacking.AddStack));
        Assert.That(FindByName(catalog, "Poison").Stacking, Is.EqualTo(StatusStacking.Independent));
        Assert.That(FindByName(catalog, "Invulnerable").GrantsDamageImmunity, Is.EqualTo(DamageTypeMask.All));
    }

    [Test]
    public void Content_UnitsCarryStatusEffectsAndReplicateThem()
    {
        foreach (string path in new[] { "Assets/Prefabs/Player/Player.prefab", "Assets/Prefabs/Mobs/Weasel.prefab" })
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab.GetComponent<StatusEffects>(), Is.Not.Null, path);
            Assert.That(prefab.GetComponent<NetworkStatusEffects>(), Is.Not.Null, path);
            Assert.That(prefab.GetComponent<DamageReceiver>().Statuses, Is.SameAs(prefab.GetComponent<StatusEffects>()), path);
        }
    }

    private static float[] RemainingTimes(StatusEffectSet set)
    {
        float[] times = new float[set.Count];
        for (int i = 0; i < times.Length; i++)
        {
            times[i] = Mathf.Round(set.GetSnapshot(i).Remaining * 1000f) / 1000f;
        }

        return times;
    }

    private static StatusEffectDefinition FindByName(StatusEffectCatalog catalog, string displayName)
    {
        for (int i = 0; i < catalog.Count; i++)
        {
            if (catalog.Get(i).DisplayName == displayName)
            {
                return catalog.Get(i);
            }
        }

        return null;
    }

    private StatusEffectDefinition Definition(
        string name,
        StatusKind kind,
        float duration,
        StatusStacking stacking = StatusStacking.Refresh,
        int maxStacks = 1,
        StatusTags tags = StatusTags.None,
        StatusPeriodic periodic = StatusPeriodic.None,
        int periodicAmount = 0,
        DamageType periodicType = DamageType.Physical,
        float tickInterval = 1f,
        float damageDealt = 1f,
        float damageTaken = 1f,
        DamageResistance[] resistances = null,
        DamageTypeMask damageImmunity = DamageTypeMask.None,
        StatusTags statusImmunity = StatusTags.None)
    {
        StatusEffectDefinition definition = StatusEffectDefinition.Create(name, kind, duration, stacking, maxStacks, tags, periodic, periodicAmount,
            periodicType, tickInterval, damageDealt, damageTaken, resistances, damageImmunity, statusImmunity);
        created.Add(definition);
        return definition;
    }

    private CombatProfile Profile(Faction faction, StatusTags statusImmunities, DamageTypeMask damageImmunities = DamageTypeMask.None, params DamageResistance[] resistances)
    {
        CombatProfile profile = CombatProfile.Create(faction, damageImmunities, statusImmunities, resistances);
        created.Add(profile);
        return profile;
    }

    private DamageReceiver Unit(string name, Faction faction, params DamageResistance[] resistances)
    {
        GameObject unit = Track(new GameObject(name));
        SerializedObject health = new(unit.AddComponent<Health>());
        health.FindProperty("maxHealth").intValue = 100;
        health.ApplyModifiedPropertiesWithoutUndo();
        DamageReceiver receiver = unit.AddComponent<DamageReceiver>();
        unit.AddComponent<StatusEffects>();
        receiver.SetProfile(Profile(faction, StatusTags.None, DamageTypeMask.None, resistances));
        return receiver;
    }

    private GameObject Track(GameObject gameObject)
    {
        created.Add(gameObject);
        return gameObject;
    }

    private sealed class Services
    {
        public Services(bool authoritative, IClock clock = null)
        {
            IGameAuthority authority = new FixedGameAuthority(authoritative);
            Events = new CombatEvents();
            Damage = TestCombat.CreateDamageService(authority, Events);
            Statuses = TestCombat.CreateStatusService(Damage, authority, Events, clock);
            Hits = new HitService(Damage, Statuses);
        }

        public CombatEvents Events { get; }
        public DamageService Damage { get; }
        public StatusEffectService Statuses { get; }
        public HitService Hits { get; }
    }

    private sealed class FakeHold : IStatusHold
    {
        public bool IsHeld { get; set; }
    }
}
