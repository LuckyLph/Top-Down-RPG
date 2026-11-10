using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Tests for the core combat components: <see cref="Health"/>, <see cref="DamageService"/>,
/// <see cref="MeleeDamageDealer"/>, and the damage popup layer and presenter.
/// </summary>
public class CombatComponentTests
{
    private GameObject root;

    /// <summary>
    /// Destroys any damage popups and popup canvases left in the scene and the test's root object.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        foreach (FloatingDamageText popup in Object.FindObjectsByType<FloatingDamageText>(FindObjectsInactive.Exclude))
        {
            Object.DestroyImmediate(popup.gameObject);
        }

        foreach (Canvas canvas in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude))
        {
            if (canvas != null && canvas.name == "DamagePopupCanvas")
            {
                Object.DestroyImmediate(canvas.gameObject);
            }
        }

        if (root != null)
        {
            Object.DestroyImmediate(root);
        }
    }

    /// <summary>
    /// Damage is clamped to the remaining health, a hit on a dead unit applies nothing, and the death event fires
    /// exactly once.
    /// </summary>
    [Test]
    public void Health_ApplyDamage_ClampsAndDiesOnlyOnce()
    {
        root = new GameObject("HealthTest");
        Health health = root.AddComponent<Health>();

        int damageEventCount = 0;
        int deathEventCount = 0;
        int lastDamageAmount = 0;

        health.Damaged += info =>
        {
            damageEventCount++;
            lastDamageAmount = info.Amount;
        };
        health.Died += _ => deathEventCount++;

        int firstHit = health.ApplyDamage(4);
        int lethalHit = health.ApplyDamage(20);
        int ignoredHit = health.ApplyDamage(1);

        Assert.That(firstHit, Is.EqualTo(4));
        Assert.That(lethalHit, Is.EqualTo(6));
        Assert.That(ignoredHit, Is.EqualTo(0));
        Assert.That(health.CurrentHealth, Is.EqualTo(0));
        Assert.That(lastDamageAmount, Is.EqualTo(6));
        Assert.That(damageEventCount, Is.EqualTo(2));
        Assert.That(deathEventCount, Is.EqualTo(1));
    }

    /// <summary>
    /// Restoring full health is a no-op, restoring a dead unit refills it and raises one restore event, and a
    /// restored unit can die again.
    /// </summary>
    [Test]
    public void Health_Restore_RefillsADeadHealthOnceAndAllowsDyingAgain()
    {
        root = new GameObject("HealthRestoreTest");
        Health health = root.AddComponent<Health>();
        int restoredCount = 0;
        int deathCount = 0;
        health.Restored += _ => restoredCount++;
        health.Died += _ => deathCount++;

        health.Restore();
        Assert.That(restoredCount, Is.EqualTo(0), "Restoring full health is a no-op.");

        health.ApplyDamage(health.MaxHealth);
        health.Restore();
        Assert.That(health.IsDead, Is.False);
        Assert.That(health.CurrentHealth, Is.EqualTo(health.MaxHealth));
        Assert.That(restoredCount, Is.EqualTo(1));

        health.ApplyDamage(health.MaxHealth);
        Assert.That(deathCount, Is.EqualTo(2), "A restored Health can die again.");
    }

    /// <summary>
    /// Syncing health sets and clamps the value without damage or restore events, and raises the death event only
    /// when it takes a living unit to zero.
    /// </summary>
    [Test]
    public void Health_SyncTo_SetsAndClampsWithoutDamaged_AndRaisesDiedOnlyWhenItKills()
    {
        root = new GameObject("HealthSyncTest");
        Health health = root.AddComponent<Health>();
        int damagedEvents = 0;
        int deaths = 0;
        int restores = 0;
        health.Damaged += _ => damagedEvents++;
        health.Died += _ => deaths++;
        health.Restored += _ => restores++;

        health.SyncTo(4);
        Assert.That(health.CurrentHealth, Is.EqualTo(4));
        Assert.That(deaths, Is.Zero);

        health.SyncTo(-3);
        Assert.That(health.CurrentHealth, Is.EqualTo(0));
        Assert.That(health.IsDead, Is.True);
        Assert.That(deaths, Is.EqualTo(1), "Syncing a living unit to zero is a death.");

        health.SyncTo(0);
        Assert.That(deaths, Is.EqualTo(1), "Syncing an already dead unit to zero again is not another death.");

        health.SyncTo(health.MaxHealth + 5);
        Assert.That(health.CurrentHealth, Is.EqualTo(health.MaxHealth));
        Assert.That(damagedEvents, Is.Zero);
        Assert.That(restores, Is.Zero, "Coming back to life goes through Restore, not SyncTo.");
    }

    /// <summary>
    /// Without authority the service refuses to decide damage, but applies a replicated hit and publishes it with
    /// the host's amount, type and flags.
    /// </summary>
    [Test]
    public void DamageService_ReplicatedDamage_AppliesAndPublishesWithoutAuthority()
    {
        root = new GameObject("ReplicatedDamageTest");
        Health health = root.AddComponent<Health>();
        DamageReceiver receiver = root.AddComponent<DamageReceiver>();
        CombatEvents combatEvents = new();
        DamageReport? published = null;
        combatEvents.DamageApplied += report => published = report;
        DamageService damageService = TestCombat.CreateDamageService(new FixedGameAuthority(false), combatEvents);

        Assert.That(damageService.ApplyDamage(receiver, 3).Resolved, Is.False, "A client never decides damage itself.");
        Assert.That(damageService.ApplyReplicatedHit(receiver, 3, DamageType.Fire, DamageFlags.Resisted).Amount, Is.EqualTo(3), "Damage the host already decided is mirrored.");

        Assert.That(health.CurrentHealth, Is.EqualTo(health.MaxHealth - 3));
        Assert.That(published.HasValue, Is.True);
        Assert.That(published.Value.Amount, Is.EqualTo(3));
        Assert.That(published.Value.Type, Is.EqualTo(DamageType.Fire));
        Assert.That(published.Value.Flags, Is.EqualTo(DamageFlags.Resisted));
        Assert.That(published.Value.PopupWorldPosition, Is.EqualTo(receiver.PopupWorldPosition));
    }

    /// <summary>
    /// The service ignores a missing target, zero or negative amounts and hits on a dead unit, and publishes only
    /// the damage it applied.
    /// </summary>
    [Test]
    public void DamageService_IgnoresInvalidDamageMissingTargetsAndHitsAfterDeath()
    {
        root = new GameObject("DamageReceiverTest");
        Health health = root.AddComponent<Health>();
        DamageReceiver receiver = root.AddComponent<DamageReceiver>();
        CombatEvents combatEvents = new();
        int publishedCount = 0;
        combatEvents.DamageApplied += _ => publishedCount++;
        DamageService damageService = TestCombat.CreateDamageService(FixedGameAuthority.Authoritative, combatEvents);

        Assert.That(damageService.ApplyDamage(null, 5).Resolved, Is.False);
        Assert.That(damageService.ApplyDamage(receiver, 0).Resolved, Is.False);
        Assert.That(damageService.ApplyDamage(receiver, -5).Resolved, Is.False);
        Assert.That(health.CurrentHealth, Is.EqualTo(10));

        Assert.That(damageService.ApplyDamage(receiver, 10).Amount, Is.EqualTo(10));
        Assert.That(health.IsDead, Is.True);
        Assert.That(damageService.ApplyDamage(receiver, 1).Resolved, Is.False);
        Assert.That(publishedCount, Is.EqualTo(1), "Only damage that was applied is reported.");
    }

    /// <summary>
    /// The melee dealer starts its attack on the receiver above a child transform, lands it at contact, refuses another
    /// inside its attack interval and starts again once the interval has passed.
    /// </summary>
    [Test]
    public void MeleeDamageDealer_RespectsItsCooldownAndFindsReceiverOnRoot()
    {
        root = new GameObject("DealerRoot");
        GameObject attacker = new("Attacker");
        attacker.transform.SetParent(root.transform);
        MeleeDamageDealer dealer = attacker.AddComponent<MeleeDamageDealer>();

        GameObject target = new("Target");
        target.transform.SetParent(root.transform);
        Health health = target.AddComponent<Health>();
        target.AddComponent<DamageReceiver>();
        TestCombat.StandInReach(target.transform);

        GameObject visuals = new("Visuals");
        visuals.transform.SetParent(target.transform, false);

        ManualClock clock = new();
        dealer.Construct(clock, TestCombat.CreateAttackService(FixedGameAuthority.Authoritative));

        bool first = TestCombat.Strike(dealer, clock, visuals.transform);
        bool second = dealer.TryStartAttack(visuals.transform);
        clock.Advance(dealer.AttackInterval - dealer.Timeline.Windup);
        dealer.Tick();
        bool afterInterval = TestCombat.Strike(dealer, clock, visuals.transform);

        Assert.That(first, Is.True);
        Assert.That(second, Is.False);
        Assert.That(afterInterval, Is.True);
        Assert.That(health.CurrentHealth, Is.EqualTo(8));
    }

    /// <summary>
    /// A freshly added health component reports full, living health before anything initialises or damages it.
    /// </summary>
    [Test]
    public void Health_ReportsFullHealth_BeforeAwakeOrDamage()
    {
        root = new GameObject("FreshHealth");
        Health health = root.AddComponent<Health>();

        Assert.That(health.CurrentHealth, Is.EqualTo(health.MaxHealth));
        Assert.That(health.IsDead, Is.False);
    }

    /// <summary>
    /// An attack on a target with no receiver does not start, and does not damage a sibling object's receiver under the
    /// same root.
    /// </summary>
    [Test]
    public void MeleeDamageDealer_DoesNotDamageSiblingReceiver_WhenTargetHasNoReceiver()
    {
        root = new GameObject("LevelRoot");
        GameObject attacker = new("Attacker");
        attacker.transform.SetParent(root.transform);
        MeleeDamageDealer dealer = attacker.AddComponent<MeleeDamageDealer>();

        GameObject enemy = new("Enemy");
        enemy.transform.SetParent(root.transform);
        Health enemyHealth = enemy.AddComponent<Health>();
        enemy.AddComponent<DamageReceiver>();

        GameObject wall = new("Wall");
        wall.transform.SetParent(root.transform);

        dealer.Construct(new ManualClock(), TestCombat.CreateAttackService(FixedGameAuthority.Authoritative));

        Assert.That(dealer.TryStartAttack(wall.transform), Is.False);
        Assert.That(dealer.IsAttacking, Is.False);
        Assert.That(enemyHealth.CurrentHealth, Is.EqualTo(enemyHealth.MaxHealth));
    }

    /// <summary>
    /// A melee dealer injected with its clock but no <see cref="AttackService"/> logs an error and starts no attack.
    /// </summary>
    [Test]
    public void MeleeDamageDealer_WithoutAttackService_LogsErrorAndStartsNothing()
    {
        root = new GameObject("DealerRoot");
        MeleeDamageDealer dealer = root.AddComponent<MeleeDamageDealer>();
        dealer.Construct(new ManualClock(), null);
        GameObject target = new("Target");
        target.transform.SetParent(root.transform);
        Health health = target.AddComponent<Health>();
        target.AddComponent<DamageReceiver>();

        LogAssert.Expect(LogType.Error, "MeleeDamageDealer was not injected with an AttackService.");
        Assert.That(dealer.TryStartAttack(target.transform), Is.False);
        Assert.That(health.CurrentHealth, Is.EqualTo(health.MaxHealth));
    }

    /// <summary>
    /// A spawned popup shows its text under the layer's overlay canvas at the projected world position, then rises
    /// and fades as it ages.
    /// </summary>
    [Test]
    public void DamagePopupLayer_SpawnsUnderOverlayCanvasAndProjectsWorldPosition()
    {
        root = new GameObject("PopupRoot");
        Camera camera = CreateCamera();
        DamagePopupLayer layer = CreatePopupLayer();

        FloatingDamageText popup = layer.Spawn("5", Color.red, new Vector3(0f, 2f, 0f), camera);

        RectTransform popupRect = popup.GetComponent<RectTransform>();
        Canvas parentCanvas = popup.GetComponentInParent<Canvas>();
        float spawnY = popupRect.anchoredPosition.y;

        Assert.That(popup.GetComponent<TextMeshProUGUI>().text, Is.EqualTo("5"));
        Assert.That(parentCanvas, Is.SameAs(layer.GetComponent<Canvas>()));
        Assert.That(parentCanvas.renderMode, Is.EqualTo(RenderMode.ScreenSpaceOverlay));
        Assert.That(spawnY, Is.GreaterThan(0f));

        layer.Tick(popup.Lifetime * 0.5f);
        layer.RefreshPositions();

        Assert.That(popupRect.anchoredPosition.y, Is.GreaterThan(spawnY), "Popup should rise as it ages.");
        Assert.That(popup.GetComponent<CanvasGroup>().alpha, Is.LessThan(1f), "Popup should fade as it ages.");
        Vector2 fromCorner = popupRect.anchoredPosition - ((RectTransform)layer.transform).rect.min;
        Assert.That(fromCorner.x, Is.EqualTo(Mathf.Round(fromCorner.x)).Within(1e-3f), "Popups sit on whole UI px, so their pixel font stays crisp.");
        Assert.That(fromCorner.y, Is.EqualTo(Mathf.Round(fromCorner.y)).Within(1e-3f), "Popups sit on whole UI px, so their pixel font stays crisp.");
    }

    /// <summary>
    /// While this machine's Reduce motion is on (Docs/DesignSystem.md, Phase 6), a popup spawned then fades where it
    /// showed instead of rising, while one shown before keeps rising; once it is off, new popups rise again.
    /// </summary>
    [Test]
    public void DamagePopupLayer_PopupsFadeInPlace_WhileReduceMotionIsOn()
    {
        root = new GameObject("PopupMotionRoot");
        Camera camera = CreateCamera();
        DamagePopupLayer layer = CreatePopupLayer();
        UserSettings settings = new(new MemorySaveStore());
        layer.Construct(settings);

        FloatingDamageText rising = layer.Spawn("1", Color.red, new Vector3(-2f, 2f, 0f), camera);
        settings.SetReduceMotion(true);
        FloatingDamageText still = layer.Spawn("2", Color.red, new Vector3(2f, 2f, 0f), camera);
        Vector2 risingStart = rising.GetComponent<RectTransform>().anchoredPosition;
        Vector2 stillStart = still.GetComponent<RectTransform>().anchoredPosition;
        Assert.That(rising.Rises, Is.True);
        Assert.That(still.Rises, Is.False);

        layer.Tick(still.Lifetime * 0.5f);
        layer.RefreshPositions();
        Assert.That(rising.GetComponent<RectTransform>().anchoredPosition.y, Is.GreaterThan(risingStart.y), "A popup shown before keeps rising.");
        Assert.That(still.GetComponent<RectTransform>().anchoredPosition, Is.EqualTo(stillStart), "It stays where it showed.");
        Assert.That(still.GetComponent<CanvasGroup>().alpha, Is.LessThan(1f), "And fades as it ages.");

        settings.SetReduceMotion(false);
        layer.Tick(still.Lifetime);
        Assert.That(layer.Spawn("3", Color.red, Vector3.zero, camera).Rises, Is.True);
    }

    /// <summary>
    /// A popup stays active within its lifetime, is deactivated and released once past it, and is reused by the
    /// next spawn.
    /// </summary>
    [Test]
    public void DamagePopupLayer_TickPastLifetime_ReleasesPopupForReuse()
    {
        root = new GameObject("PopupPoolRoot");
        Camera camera = CreateCamera();
        DamagePopupLayer layer = CreatePopupLayer();

        FloatingDamageText first = layer.Spawn("1", Color.red, Vector3.zero, camera);
        layer.Tick(first.Lifetime * 0.5f);

        Assert.That(first.gameObject.activeSelf, Is.True);
        Assert.That(layer.ActiveCount, Is.EqualTo(1));

        layer.Tick(first.Lifetime);

        Assert.That(first.gameObject.activeSelf, Is.False, "Finished popup should be released to the pool.");
        Assert.That(layer.ActiveCount, Is.EqualTo(0));

        FloatingDamageText second = layer.Spawn("2", Color.red, Vector3.zero, camera);

        Assert.That(second, Is.SameAs(first), "Spawning again should reuse the pooled popup.");
        Assert.That(second.gameObject.activeSelf, Is.True);
        Assert.That(second.GetComponent<TextMeshProUGUI>().text, Is.EqualTo("2"));
        Assert.That(layer.ActiveCount, Is.EqualTo(1));
    }

    /// <summary>
    /// Applying damage publishes a report with the target, amount, source and a popup position above the receiver.
    /// </summary>
    [Test]
    public void DamageService_PublishesDamageReport()
    {
        root = new GameObject("DamageReceiverRoot");
        root.transform.position = new Vector3(2f, 3f, 0f);
        Health health = root.AddComponent<Health>();
        DamageReceiver receiver = root.AddComponent<DamageReceiver>();
        GameObject source = new("Source");
        source.transform.SetParent(root.transform);

        CombatEvents combatEvents = new();
        DamageReport? published = null;
        combatEvents.DamageApplied += report => published = report;
        DamageService damageService = TestCombat.CreateDamageService(FixedGameAuthority.Authoritative, combatEvents);

        damageService.ApplyDamage(receiver, 2, DamageType.Physical, source);

        Assert.That(published.HasValue, Is.True);
        Assert.That(published.Value.Target, Is.SameAs(health));
        Assert.That(published.Value.Amount, Is.EqualTo(2));
        Assert.That(published.Value.Source, Is.SameAs(source));
        Assert.That(published.Value.PopupWorldPosition, Is.EqualTo(receiver.PopupWorldPosition));
        Assert.That(published.Value.PopupWorldPosition.y, Is.GreaterThan(root.transform.position.y));
    }

    /// <summary>
    /// The presenter spawns a popup for each published damage report after it starts, and stops once disposed.
    /// </summary>
    [Test]
    public void DamagePopupPresenter_SpawnsPopupsOnlyWhileStarted()
    {
        root = new GameObject("PresenterRoot");
        Camera camera = CreateCamera();
        DamagePopupLayer layer = CreatePopupLayer();
        CombatEvents combatEvents = new();
        DamagePopupPresenter presenter = new(combatEvents, layer, camera, TestCombat.Settings);
        DamageReport report = new(null, 3, DamageType.Physical, DamageFlags.None, null, Vector3.zero);

        presenter.Start();
        combatEvents.Publish(report);
        Assert.That(CountActivePopups(layer), Is.EqualTo(1));

        presenter.Dispose();
        combatEvents.Publish(report);
        Assert.That(CountActivePopups(layer), Is.EqualTo(1));
        Assert.That(layer.ActiveCount, Is.EqualTo(1));
    }

    /// <summary>
    /// Creates an orthographic camera under the root, looking at the origin from z = -10.
    /// </summary>
    private Camera CreateCamera()
    {
        GameObject cameraObject = new("Camera");
        cameraObject.transform.SetParent(root.transform);
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.orthographic = true;
        camera.transform.position = new Vector3(0f, 0f, -10f);
        return camera;
    }

    /// <summary>
    /// Creates a screen-space overlay canvas with a <see cref="DamagePopupLayer"/> configured with an inactive
    /// text popup template.
    /// </summary>
    private DamagePopupLayer CreatePopupLayer()
    {
        GameObject canvasObject = new("DamagePopupCanvas", typeof(RectTransform), typeof(Canvas));
        canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        DamagePopupLayer layer = canvasObject.AddComponent<DamagePopupLayer>();

        GameObject template = new("DamagePopupTemplate", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI), typeof(CanvasGroup));
        template.transform.SetParent(root.transform, false);
        template.SetActive(false);
        RectTransform templateRect = template.GetComponent<RectTransform>();
        templateRect.anchorMin = new Vector2(0.5f, 0.5f);
        templateRect.anchorMax = new Vector2(0.5f, 0.5f);
        layer.Configure(template.AddComponent<FloatingDamageText>());
        return layer;
    }

    /// <summary>
    /// Counts the active popups parented under <paramref name="layer"/>.
    /// </summary>
    private static int CountActivePopups(DamagePopupLayer layer)
    {
        return layer.GetComponentsInChildren<FloatingDamageText>(false).Length;
    }
}
