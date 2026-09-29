using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

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
    public void DamageReceiver_ReceiveDamage_IgnoresInvalidDamageAndHitsAfterDeath()
    {
        root = new GameObject("DamageReceiverTest");
        Health health = root.AddComponent<Health>();
        DamageReceiver receiver = root.AddComponent<DamageReceiver>();

        Assert.That(receiver.ReceiveDamage(0), Is.EqualTo(0));
        Assert.That(receiver.ReceiveDamage(-5), Is.EqualTo(0));
        Assert.That(health.CurrentHealth, Is.EqualTo(10));

        Assert.That(receiver.ReceiveDamage(10), Is.EqualTo(10));
        Assert.That(health.IsDead, Is.True);
        Assert.That(receiver.ReceiveDamage(1), Is.EqualTo(0));
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

        dealer.ResetCooldown();

        bool firstHit = dealer.TryDealDamage(visuals.transform);
        bool secondHit = dealer.TryDealDamage(visuals.transform);

        Assert.That(firstHit, Is.True);
        Assert.That(secondHit, Is.False);
        Assert.That(health.CurrentHealth, Is.EqualTo(9));
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

        dealer.ResetCooldown();

        Assert.That(dealer.TryDealDamage(wall.transform), Is.False);
        Assert.That(enemyHealth.CurrentHealth, Is.EqualTo(enemyHealth.MaxHealth));
    }

    [Test]
    public void FloatingDamageText_SpawnsUnderOverlayCanvasAndProjectsWorldPosition()
    {
        GameObject cameraObject = new("Main Camera");
        cameraObject.tag = "MainCamera";

        Camera camera = cameraObject.AddComponent<Camera>();
        camera.orthographic = true;
        camera.transform.position = new Vector3(0f, 0f, -10f);

        FloatingDamageText popup = FloatingDamageText.Spawn(5, new Vector3(0f, 2f, 0f));

        try
        {
            MethodInfo lateUpdate = typeof(FloatingDamageText).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(lateUpdate, Is.Not.Null);
            lateUpdate.Invoke(popup, null);

            Component popupText = popup.GetComponent("TextMeshProUGUI");
            RectTransform popupRect = popup.GetComponent<RectTransform>();
            Canvas parentCanvas = popup.GetComponentInParent<Canvas>();

            Assert.That(popupText, Is.Not.Null);
            Assert.That(popupRect, Is.Not.Null);
            Assert.That(parentCanvas, Is.Not.Null);
            Assert.That(parentCanvas.renderMode, Is.EqualTo(RenderMode.ScreenSpaceOverlay));
            Assert.That(popupRect.anchoredPosition.y, Is.GreaterThan(0f));
        }
        finally
        {
            Object.DestroyImmediate(popup.gameObject);
            Object.DestroyImmediate(cameraObject);
        }
    }

    [Test]
    public void DamageReceiver_ReceiveDamage_SpawnsUiPopup()
    {
        root = new GameObject("DamageReceiverRoot");
        root.transform.position = Vector3.zero;

        GameObject cameraObject = new("Main Camera");
        cameraObject.tag = "MainCamera";
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.orthographic = true;
        camera.transform.position = new Vector3(0f, 0f, -10f);

        Health health = root.AddComponent<Health>();
        DamageReceiver receiver = root.AddComponent<DamageReceiver>();

        try
        {
            receiver.ReceiveDamage(1);

            FloatingDamageText popup = Object.FindAnyObjectByType<FloatingDamageText>();
            Assert.That(popup, Is.Not.Null);

            Assert.That(popup.GetComponent("TextMeshProUGUI"), Is.Not.Null);
            Assert.That(popup.GetComponent<Renderer>(), Is.Null);
            Assert.That(popup.GetComponentInParent<Canvas>(), Is.Not.Null);
        }
        finally
        {
            Object.DestroyImmediate(cameraObject);
        }
    }
}
