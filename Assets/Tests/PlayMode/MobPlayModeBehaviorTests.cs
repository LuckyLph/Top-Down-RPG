using System.Collections;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

public class MobPlayModeBehaviorTests
{
    [UnityTest]
    public IEnumerator MobInSampleScene_DetectsAndLosesTarget()
    {
        yield return SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);
        yield return null;

        MobController brain = Object.FindAnyObjectByType<MobController>();
        Assert.That(brain, Is.Not.Null, "SampleScene should contain at least one MobBrain.");

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        Assert.That(player, Is.Not.Null, "SampleScene should contain a Player tag target.");

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
    public IEnumerator MultipleMobs_ShareSameNavigationGrid()
    {
        yield return SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);
        yield return null;

        NavigationGrid2D navGrid = Object.FindAnyObjectByType<NavigationGrid2D>();
        MobController firstMob = Object.FindAnyObjectByType<MobController>();

        Assert.That(navGrid, Is.Not.Null, "SampleScene should contain a NavigationGrid2D.");
        Assert.That(firstMob, Is.Not.Null, "SampleScene should contain at least one MobBrain.");
        Assert.That(firstMob.NavigationGrid, Is.SameAs(navGrid));

        GameObject clone = Object.Instantiate(firstMob.gameObject, firstMob.transform.position + Vector3.right * 2f, Quaternion.identity);
        yield return null;

        MobController secondMob = clone.GetComponent<MobController>();
        Assert.That(secondMob, Is.Not.Null);
        Assert.That(secondMob.NavigationGrid, Is.SameAs(navGrid));

        Object.Destroy(clone);
    }

    [UnityTest]
    public IEnumerator MobInSampleScene_AttackDamagesPlayerAndPopupFadesOut()
    {
        yield return SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);
        yield return null;

        MobController brain = Object.FindAnyObjectByType<MobController>();
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        Health health = player.GetComponent<Health>();

        Assert.That(brain, Is.Not.Null);
        Assert.That(health, Is.Not.Null, "Player should have Health wired in SampleScene.");

        Vector3 originalPlayerPosition = player.transform.position;
        int startingHealth = health.CurrentHealth;

        player.transform.position = brain.transform.position + Vector3.right * 0.1f;
        yield return WaitForHealthChange(health, startingHealth - 1, 30);

        Assert.That(health.CurrentHealth, Is.EqualTo(startingHealth - 1));

        FloatingDamageText popup = Object.FindAnyObjectByType<FloatingDamageText>();
        Assert.That(popup, Is.Not.Null, "A floating damage popup should be spawned when damage is applied.");

        player.transform.position = originalPlayerPosition;
        yield return new WaitForSeconds(1.2f);

        Assert.That(Object.FindAnyObjectByType<FloatingDamageText>(), Is.Null, "Damage popup should clean itself up after fading out.");
    }

    [UnityTest]
    public IEnumerator MobInSampleScene_AttackRespectsConfiguredInterval()
    {
        yield return SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);
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
    public IEnumerator MobInSampleScene_DeathSpawnsAnimationAndDestroysMob()
    {
        yield return SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);
        yield return null;

        MobController mob = Object.FindAnyObjectByType<MobController>();
        Assert.That(mob, Is.Not.Null);

        DamageReceiver receiver = mob.GetComponent<DamageReceiver>();
        Health health = mob.GetComponent<Health>();
        Assert.That(receiver, Is.Not.Null);
        Assert.That(health, Is.Not.Null);

        receiver.ReceiveDamage(health.CurrentHealth);
        yield return null;

        Assert.That(health.IsDead, Is.True);
        Assert.That(mob == null, Is.True, "Mob root should be destroyed after death.");

        MobDeathAnimation deathAnimation = Object.FindAnyObjectByType<MobDeathAnimation>();
        Assert.That(deathAnimation, Is.Not.Null, "A death animation object should be spawned when the mob dies.");

        yield return new WaitForSeconds(1.5f);
        Assert.That(Object.FindAnyObjectByType<MobDeathAnimation>(), Is.Null, "Death animation object should clean itself up after playback.");
    }

    [UnityTest]
    public IEnumerator PlayerInSampleScene_HudDisplaysWeaponIconAndHealthAndSlashDamageStillWorks()
    {
        yield return SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);
        yield return null;

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        MobController mob = Object.FindAnyObjectByType<MobController>();
        PlayerHudController hud = Object.FindAnyObjectByType<PlayerHudController>();

        Assert.That(player, Is.Not.Null);
        Assert.That(mob, Is.Not.Null);
        Assert.That(hud, Is.Not.Null, "SampleScene should contain the reusable PlayerHudController.");

        PlayerController playerController = player.GetComponent<PlayerController>();
        PlayerWeaponController weaponController = player.GetComponent<PlayerWeaponController>();
        Health playerHealth = player.GetComponent<Health>();
        Health mobHealth = mob.GetComponent<Health>();
        TextMeshProUGUI healthText = FindTextByName("HealthText");
        Image healthFill = FindImageByName("HealthFill");
        Image weaponIcon = FindImageByName("WeaponIcon");

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
        Assert.That(weaponIcon.sprite, Is.SameAs(weaponController.CurrentWeapon.HudIcon), "HUD should show the equipped weapon icon.");

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
        SetPrivateField(playerController, "lastMoveDirection", Vector2.right);

        weaponController.TryAttack();
        yield return null;

        Assert.That(mobHealth.CurrentHealth, Is.EqualTo(startingHealth - expectedDamage));
        Assert.That(Object.FindAnyObjectByType<SwordSlashAttack>(), Is.Not.Null);

        player.transform.position = originalPlayerPosition;
        mob.transform.position = originalMobPosition;
    }

    private static Image FindImageByName(string objectName)
    {
        Image[] images = Object.FindObjectsByType<Image>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < images.Length; i++)
        {
            if (images[i] != null && images[i].name == objectName)
            {
                return images[i];
            }
        }

        return null;
    }

    private static TextMeshProUGUI FindTextByName(string objectName)
    {
        TextMeshProUGUI[] texts = Object.FindObjectsByType<TextMeshProUGUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
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

    private static void SetPrivateField<T>(Object target, string fieldName, T value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, $"Expected field '{fieldName}' to exist on {target.GetType().Name}.");
        field.SetValue(target, value);
    }
}
