using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;

public class CombatComponentTests
{
    private GameObject root;

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

    [Test]
    public void DamageService_IgnoresInvalidDamageMissingTargetsAndHitsAfterDeath()
    {
        root = new GameObject("DamageReceiverTest");
        Health health = root.AddComponent<Health>();
        DamageReceiver receiver = root.AddComponent<DamageReceiver>();
        CombatEvents combatEvents = new();
        int publishedCount = 0;
        combatEvents.DamageApplied += _ => publishedCount++;
        DamageService damageService = new(combatEvents);

        Assert.That(damageService.ApplyDamage(null, 5), Is.EqualTo(0));
        Assert.That(damageService.ApplyDamage(receiver, 0), Is.EqualTo(0));
        Assert.That(damageService.ApplyDamage(receiver, -5), Is.EqualTo(0));
        Assert.That(health.CurrentHealth, Is.EqualTo(10));

        Assert.That(damageService.ApplyDamage(receiver, 10), Is.EqualTo(10));
        Assert.That(health.IsDead, Is.True);
        Assert.That(damageService.ApplyDamage(receiver, 1), Is.EqualTo(0));
        Assert.That(publishedCount, Is.EqualTo(1), "Only damage that was applied is reported.");
    }

    [Test]
    public void MeleeDamageDealer_RespectsImmediateCooldownAndFindsReceiverOnRoot()
    {
        root = new GameObject("DealerRoot");
        GameObject attacker = new("Attacker");
        attacker.transform.SetParent(root.transform);
        MeleeDamageDealer dealer = attacker.AddComponent<MeleeDamageDealer>();

        GameObject target = new("Target");
        target.transform.SetParent(root.transform);
        Health health = target.AddComponent<Health>();
        target.AddComponent<DamageReceiver>();

        GameObject visuals = new("Visuals");
        visuals.transform.SetParent(target.transform);

        ManualClock clock = new();
        dealer.Construct(clock, new DamageService(new CombatEvents()));
        dealer.ResetCooldown();

        bool firstHit = dealer.TryDealDamage(visuals.transform);
        bool secondHit = dealer.TryDealDamage(visuals.transform);
        clock.Advance(dealer.AttackInterval);
        bool hitAfterInterval = dealer.TryDealDamage(visuals.transform);

        Assert.That(firstHit, Is.True);
        Assert.That(secondHit, Is.False);
        Assert.That(hitAfterInterval, Is.True);
        Assert.That(health.CurrentHealth, Is.EqualTo(8));
    }

    [Test]
    public void Health_ReportsFullHealth_BeforeAwakeOrDamage()
    {
        root = new GameObject("FreshHealth");
        Health health = root.AddComponent<Health>();

        Assert.That(health.CurrentHealth, Is.EqualTo(health.MaxHealth));
        Assert.That(health.IsDead, Is.False);
    }

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

        dealer.Construct(new ManualClock(), new DamageService(new CombatEvents()));
        dealer.ResetCooldown();

        Assert.That(dealer.TryDealDamage(wall.transform), Is.False);
        Assert.That(enemyHealth.CurrentHealth, Is.EqualTo(enemyHealth.MaxHealth));
    }

    [Test]
    public void MeleeDamageDealer_WithoutDamageService_LogsErrorAndDealsNoDamage()
    {
        root = new GameObject("DealerRoot");
        MeleeDamageDealer dealer = root.AddComponent<MeleeDamageDealer>();
        GameObject target = new("Target");
        target.transform.SetParent(root.transform);
        Health health = target.AddComponent<Health>();
        target.AddComponent<DamageReceiver>();
        dealer.ResetCooldown();

        LogAssert.Expect(LogType.Error, "MeleeDamageDealer was not injected with a DamageService.");
        Assert.That(dealer.TryDealDamage(target.transform), Is.False);
        Assert.That(health.CurrentHealth, Is.EqualTo(health.MaxHealth));
    }

    [Test]
    public void DamagePopupLayer_SpawnsUnderOverlayCanvasAndProjectsWorldPosition()
    {
        root = new GameObject("PopupRoot");
        Camera camera = CreateCamera();
        DamagePopupLayer layer = CreatePopupLayer();

        FloatingDamageText popup = layer.Spawn(5, new Vector3(0f, 2f, 0f), camera);

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
    }

    [Test]
    public void DamagePopupLayer_TickPastLifetime_ReleasesPopupForReuse()
    {
        root = new GameObject("PopupPoolRoot");
        Camera camera = CreateCamera();
        DamagePopupLayer layer = CreatePopupLayer();

        FloatingDamageText first = layer.Spawn(1, Vector3.zero, camera);
        layer.Tick(first.Lifetime * 0.5f);

        Assert.That(first.gameObject.activeSelf, Is.True);
        Assert.That(layer.ActiveCount, Is.EqualTo(1));

        layer.Tick(first.Lifetime);

        Assert.That(first.gameObject.activeSelf, Is.False, "Finished popup should be released to the pool.");
        Assert.That(layer.ActiveCount, Is.EqualTo(0));

        FloatingDamageText second = layer.Spawn(2, Vector3.zero, camera);

        Assert.That(second, Is.SameAs(first), "Spawning again should reuse the pooled popup.");
        Assert.That(second.gameObject.activeSelf, Is.True);
        Assert.That(second.GetComponent<TextMeshProUGUI>().text, Is.EqualTo("2"));
        Assert.That(layer.ActiveCount, Is.EqualTo(1));
    }

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
        DamageService damageService = new(combatEvents);

        damageService.ApplyDamage(receiver, 2, source);

        Assert.That(published.HasValue, Is.True);
        Assert.That(published.Value.Target, Is.SameAs(health));
        Assert.That(published.Value.Amount, Is.EqualTo(2));
        Assert.That(published.Value.Source, Is.SameAs(source));
        Assert.That(published.Value.PopupWorldPosition, Is.EqualTo(receiver.PopupWorldPosition));
        Assert.That(published.Value.PopupWorldPosition.y, Is.GreaterThan(root.transform.position.y));
    }

    [Test]
    public void DamagePopupPresenter_SpawnsPopupsOnlyWhileStarted()
    {
        root = new GameObject("PresenterRoot");
        Camera camera = CreateCamera();
        DamagePopupLayer layer = CreatePopupLayer();
        CombatEvents combatEvents = new();
        DamagePopupPresenter presenter = new(combatEvents, layer, camera);
        DamageReport report = new(null, 3, null, Vector3.zero);

        presenter.Start();
        combatEvents.Publish(report);
        Assert.That(CountActivePopups(layer), Is.EqualTo(1));

        presenter.Dispose();
        combatEvents.Publish(report);
        Assert.That(CountActivePopups(layer), Is.EqualTo(1));
        Assert.That(layer.ActiveCount, Is.EqualTo(1));
    }

    private Camera CreateCamera()
    {
        GameObject cameraObject = new("Camera");
        cameraObject.transform.SetParent(root.transform);
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.orthographic = true;
        camera.transform.position = new Vector3(0f, 0f, -10f);
        return camera;
    }

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

    private static int CountActivePopups(DamagePopupLayer layer)
    {
        return layer.GetComponentsInChildren<FloatingDamageText>(false).Length;
    }
}
