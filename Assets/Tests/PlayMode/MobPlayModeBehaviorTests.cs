using System.Collections;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using VContainer;
using VContainer.Unity;

public class MobPlayModeBehaviorTests
{
    [UnityTest]
    public IEnumerator MobInClearing_DetectsAndLosesTarget()
    {
        yield return SceneBootTestHelper.BootIntoStartingArea();
        yield return null;

        MobController brain = Object.FindAnyObjectByType<MobController>();
        Assert.That(brain, Is.Not.Null, "The clearing slice should contain at least one MobController.");

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        Assert.That(player, Is.Not.Null, "The clearing slice should contain a Player tag target.");

        Vector3 originalPlayerPosition = player.transform.position;
        Vector3 mobPosition = brain.transform.position;

        player.transform.position = mobPosition + Vector3.right * 2f;
        yield return WaitFrames(6);
        Assert.That(
            brain.CurrentStateId == MobStateId.Chase || brain.CurrentStateId == MobStateId.AttackRange,
            Is.True,
            $"Expected chase or attack state, got {brain.CurrentStateId}.");

        player.transform.position = mobPosition + Vector3.right * 30f;
        yield return WaitFrames(10);
        Assert.That(
            brain.CurrentStateId == MobStateId.Return || brain.CurrentStateId == MobStateId.Idle || brain.CurrentStateId == MobStateId.Patrol,
            Is.True,
            $"Expected return/idle/patrol after losing target, got {brain.CurrentStateId}.");

        player.transform.position = originalPlayerPosition;
    }

    [UnityTest]
    public IEnumerator AreaMobs_AreSpawnedFromSpawnPointsIntoTheAreaScene()
    {
        yield return SceneBootTestHelper.BootIntoStartingArea();

        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        MobSpawnPoint[] spawnPoints = Object.FindObjectsByType<MobSpawnPoint>();
        MobController[] mobs = Object.FindObjectsByType<MobController>();

        Assert.That(spawnPoints, Is.Not.Empty, "The clearing should define at least one mob spawn point.");
        Assert.That(mobs.Length, Is.EqualTo(spawnPoints.Length), "Each spawn point should spawn exactly one mob.");

        foreach (MobSpawnPoint spawnPoint in spawnPoints)
        {
            bool spawnedHere = false;
            foreach (MobController mob in mobs)
            {
                if (Vector2.Distance(mob.Patrol.SpawnPosition, spawnPoint.transform.position) < 0.01f)
                {
                    spawnedHere = true;
                    Assert.That(mob.gameObject.scene, Is.EqualTo(gameFlow.AreaScene));
                    Assert.That(mob.NavigationGrid, Is.Not.Null);
                    Assert.That(mob.Players, Is.Not.Null);
                }
            }

            Assert.That(spawnedHere, Is.True, $"No mob was spawned at {spawnPoint.name}.");
        }
    }

    [UnityTest]
    public IEnumerator MobsSpawnedThroughAreaScope_ShareGridAndPlayer()
    {
        yield return SceneBootTestHelper.BootIntoStartingArea();
        yield return null;

        NavigationGrid2D navGrid = Object.FindAnyObjectByType<NavigationGrid2D>();
        MobController firstMob = Object.FindAnyObjectByType<MobController>();

        Assert.That(navGrid, Is.Not.Null, "The clearing slice should contain a NavigationGrid2D.");
        Assert.That(firstMob, Is.Not.Null, "The clearing slice should contain at least one MobController.");
        Assert.That(firstMob.NavigationGrid, Is.SameAs(navGrid));

        // Runtime spawns go through the area's container so they receive the same dependencies.
        IObjectResolver areaResolver = LifetimeScope.Find<AreaLifetimeScope>(navGrid.gameObject.scene).Container;
        MobController secondMob = areaResolver.Instantiate(firstMob, firstMob.transform.position + Vector3.right * 2f, Quaternion.identity);
        yield return null;

        Assert.That(secondMob.NavigationGrid, Is.SameAs(navGrid));
        Assert.That(secondMob.Players, Is.SameAs(firstMob.Players));
        Assert.That(secondMob.gameObject.scene, Is.EqualTo(navGrid.gameObject.scene));

        Object.Destroy(secondMob.gameObject);
    }

    [UnityTest]
    public IEnumerator MobInClearing_AttackDamagesPlayerAndPopupFadesOut()
    {
        yield return SceneBootTestHelper.BootIntoStartingArea();
        yield return null;

        MobController brain = Object.FindAnyObjectByType<MobController>();
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        Health health = player.GetComponent<Health>();

        Assert.That(brain, Is.Not.Null);
        Assert.That(health, Is.Not.Null, "Player should have Health wired in the Gameplay scene.");

        Vector3 originalPlayerPosition = player.transform.position;
        int startingHealth = health.CurrentHealth;

        player.transform.position = brain.transform.position + Vector3.right * 0.1f;
        yield return WaitForHealthChange(health, startingHealth - 1, 30);

        Assert.That(health.CurrentHealth, Is.EqualTo(startingHealth - 1));

        FloatingDamageText popup = Object.FindAnyObjectByType<FloatingDamageText>();
        Assert.That(popup, Is.Not.Null, "A floating damage popup should be spawned when damage is applied.");
        DamagePopupLayer layer = popup.GetComponentInParent<DamagePopupLayer>();
        Assert.That(layer, Is.Not.Null, "Popups should live on the Gameplay scene's popup layer.");
        Assert.That(layer.ActiveCount, Is.GreaterThan(0), "The layer should be driving the spawned popup.");

        player.transform.position = originalPlayerPosition;
        yield return new WaitForSeconds(1.2f);

        Assert.That(popup.gameObject.activeSelf, Is.False, "Damage popup should return to the pool after fading out.");
        Assert.That(layer.ActiveCount, Is.EqualTo(0));
        Assert.That(Object.FindAnyObjectByType<FloatingDamageText>(), Is.Null);
    }

    [UnityTest]
    public IEnumerator MobInClearing_AttackRespectsConfiguredInterval()
    {
        yield return SceneBootTestHelper.BootIntoStartingArea();
        yield return null;

        MobController brain = Object.FindAnyObjectByType<MobController>();
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        Health health = player.GetComponent<Health>();

        Assert.That(brain, Is.Not.Null);
        Assert.That(health, Is.Not.Null);

        Vector3 originalPlayerPosition = player.transform.position;
        int startingHealth = health.CurrentHealth;

        player.transform.position = brain.transform.position + Vector3.right * 0.1f;
        yield return WaitForHealthChange(health, startingHealth - 1, 30);
        Assert.That(health.CurrentHealth, Is.EqualTo(startingHealth - 1));

        yield return new WaitForSeconds(0.2f);
        Assert.That(health.CurrentHealth, Is.EqualTo(startingHealth - 1), "Health should not drop again before the attack interval elapses.");

        yield return new WaitForSeconds(0.7f);
        Assert.That(health.CurrentHealth, Is.LessThanOrEqualTo(startingHealth - 2), "Health should drop again after the configured attack interval.");

        player.transform.position = originalPlayerPosition;
    }

    [UnityTest]
    public IEnumerator PlayerInClearing_DeathDisablesControlsAndMobDisengages()
    {
        yield return SceneBootTestHelper.BootIntoStartingArea();
        yield return null;

        MobController brain = Object.FindAnyObjectByType<MobController>();
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        Health health = player.GetComponent<Health>();
        PlayerController playerController = player.GetComponent<PlayerController>();
        PlayerWeaponController weaponController = player.GetComponent<PlayerWeaponController>();

        Assert.That(brain, Is.Not.Null);
        Assert.That(health, Is.Not.Null);

        player.transform.position = brain.transform.position + Vector3.right * 0.1f;
        yield return WaitForHealthChange(health, health.CurrentHealth - 1, 30);
        Assert.That(brain.CurrentStateId, Is.EqualTo(MobStateId.AttackRange));

        health.ApplyDamage(health.CurrentHealth);
        yield return WaitFrames(2);

        Assert.That(health.IsDead, Is.True);
        Assert.That(playerController.enabled, Is.False, "Player movement should stop on death.");
        Assert.That(weaponController.enabled, Is.False, "Player attacks should stop on death.");
        Assert.That(brain.CurrentStateId, Is.Not.EqualTo(MobStateId.AttackRange), "Mob should stop attacking a dead player.");
        Assert.That(brain.CurrentStateId, Is.Not.EqualTo(MobStateId.Chase), "Mob should not chase a dead player.");
    }

    [UnityTest]
    public IEnumerator MobInClearing_DeathSpawnsAnimationAndDestroysMob()
    {
        yield return SceneBootTestHelper.BootIntoStartingArea();
        yield return null;

        MobController mob = Object.FindAnyObjectByType<MobController>();
        Assert.That(mob, Is.Not.Null);

        DamageReceiver receiver = mob.GetComponent<DamageReceiver>();
        Health health = mob.GetComponent<Health>();
        Assert.That(receiver, Is.Not.Null);
        Assert.That(health, Is.Not.Null);

        DamageService damageService = Object.FindAnyObjectByType<GameplayLifetimeScope>().Container.Resolve<DamageService>();
        damageService.ApplyDamage(receiver, health.CurrentHealth);
        yield return null;

        Assert.That(health.IsDead, Is.True);
        Assert.That(mob == null, Is.True, "Mob root should be destroyed after death.");

        MobDeathAnimation deathAnimation = Object.FindAnyObjectByType<MobDeathAnimation>();
        Assert.That(deathAnimation, Is.Not.Null, "A death animation object should be spawned when the mob dies.");

        yield return new WaitForSeconds(1.5f);
        Assert.That(Object.FindAnyObjectByType<MobDeathAnimation>(), Is.Null, "Death animation object should clean itself up after playback.");
    }

    [UnityTest]
    public IEnumerator PlayerInClearing_HudDisplaysWeaponIconAndHealthAndSlashDamageStillWorks()
    {
        yield return SceneBootTestHelper.BootIntoStartingArea();
        yield return null;

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        MobController mob = Object.FindAnyObjectByType<MobController>();
        PlayerHudView hud = Object.FindAnyObjectByType<PlayerHudView>();

        Assert.That(player, Is.Not.Null);
        Assert.That(mob, Is.Not.Null);
        Assert.That(hud, Is.Not.Null, "The clearing slice should contain the reusable PlayerHudView.");

        PlayerController playerController = player.GetComponent<PlayerController>();
        PlayerWeaponController weaponController = player.GetComponent<PlayerWeaponController>();
        Health playerHealth = player.GetComponent<Health>();
        Health mobHealth = mob.GetComponent<Health>();
        TextMeshProUGUI healthText = FindTextByName(hud.transform, "HealthText");
        Image healthFill = FindImageByName(hud.transform, "HealthFill");
        Image weaponIcon = FindImageByName(hud.transform, "WeaponIcon");

        Assert.That(playerController, Is.Not.Null);
        Assert.That(weaponController, Is.Not.Null);
        Assert.That(playerHealth, Is.Not.Null);
        Assert.That(mobHealth, Is.Not.Null);
        Assert.That(weaponController.CurrentWeapon, Is.Not.Null);
        Assert.That(weaponController.CurrentWeapon.HudIcon, Is.Not.Null, "Default weapon should provide a HUD icon.");
        Assert.That(healthText, Is.Not.Null);
        Assert.That(healthFill, Is.Not.Null);
        Assert.That(weaponIcon, Is.Not.Null);

        yield return null;

        Assert.That(healthText.text, Is.EqualTo($"HP {playerHealth.CurrentHealth}"));
        float initialExpectedFill = (float)playerHealth.CurrentHealth / playerHealth.MaxHealth;
        Assert.That(healthFill.fillAmount, Is.EqualTo(initialExpectedFill).Within(0.001f));
        Assert.That(weaponIcon.sprite, Is.EqualTo(weaponController.CurrentWeapon.HudIcon), "HUD should show the equipped weapon icon.");

        float initialFillAmount = healthFill.fillAmount;
        int initialPlayerHealth = playerHealth.CurrentHealth;
        playerHealth.ApplyDamage(1);
        yield return null;
        Assert.That(healthText.text, Is.EqualTo($"HP {playerHealth.CurrentHealth}"));
        Assert.That(playerHealth.CurrentHealth, Is.EqualTo(initialPlayerHealth - 1));
        float expectedFillAfterDamage = (float)playerHealth.CurrentHealth / playerHealth.MaxHealth;
        Assert.That(healthFill.fillAmount, Is.EqualTo(expectedFillAfterDamage).Within(0.001f));
        Assert.That(healthFill.fillAmount, Is.LessThan(initialFillAmount));

        Vector3 originalPlayerPosition = player.transform.position;
        Vector3 originalMobPosition = mob.transform.position;
        int startingHealth = mobHealth.CurrentHealth;
        int expectedDamage = weaponController.CurrentWeapon != null ? weaponController.CurrentWeapon.Damage : 1;

        player.transform.position = mob.transform.position + Vector3.left * 0.45f;
        playerController.Face(Vector2.right);

        weaponController.TryAttack();
        yield return null;

        Assert.That(mobHealth.CurrentHealth, Is.EqualTo(startingHealth - expectedDamage));
        Assert.That(Object.FindAnyObjectByType<SwordSlashAttack>(), Is.Not.Null);

        player.transform.position = originalPlayerPosition;
        mob.transform.position = originalMobPosition;
    }

    private static Image FindImageByName(Transform root, string objectName)
    {
        Image[] images = root.GetComponentsInChildren<Image>(true);
        for (int i = 0; i < images.Length; i++)
        {
            if (images[i] != null && images[i].name == objectName)
            {
                return images[i];
            }
        }

        return null;
    }

    private static TextMeshProUGUI FindTextByName(Transform root, string objectName)
    {
        TextMeshProUGUI[] texts = root.GetComponentsInChildren<TextMeshProUGUI>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            if (texts[i] != null && texts[i].name == objectName)
            {
                return texts[i];
            }
        }

        return null;
    }

    private static IEnumerator WaitFrames(int frameCount)
    {
        for (int i = 0; i < frameCount; i++)
        {
            yield return null;
        }
    }

    private static IEnumerator WaitForHealthChange(Health health, int expectedHealth, int maxFrames)
    {
        for (int i = 0; i < maxFrames && health.CurrentHealth > expectedHealth; i++)
        {
            yield return null;
        }
    }
}
