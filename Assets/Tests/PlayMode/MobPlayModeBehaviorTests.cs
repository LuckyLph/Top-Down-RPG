using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using VContainer;
using VContainer.Unity;

/// <summary>
/// PlayMode tests for mobs in Testlands, the booted test world: detection, spawning through the chunk scopes, melee
/// attacks and their pacing, the Weasel's baked animation (its run and its attack, the contact frame showing as its hit
/// lands), dodging its windup by stepping out of reach, a stun stopping its windup, deaths on both sides, and the player
/// HUD.
/// </summary>
public class MobPlayModeBehaviorTests
{
    /// <summary>
    /// A mob chases or attacks a player moved next to it, and returns, idles or patrols once the player is moved far
    /// out of range.
    /// </summary>
    [UnityTest]
    public IEnumerator MobInTestlands_DetectsAndLosesTarget()
    {
        yield return SceneBootTestHelper.BootIntoTestlands();
        yield return null;

        MobController brain = KeepOneMobActive();
        Assert.That(brain, Is.Not.Null, "Testlands' start chunks should have at least one mob.");

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        Assert.That(player, Is.Not.Null, "The session should have a Player tag target.");

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

    /// <summary>
    /// Each mob spawn point of a loaded chunk spawns exactly one mob at its position, in that chunk's scene and with its
    /// world's navigation and players injected.
    /// </summary>
    [UnityTest]
    public IEnumerator ChunkMobs_AreSpawnedFromSpawnPointsIntoTheirChunkScene()
    {
        yield return SceneBootTestHelper.BootIntoTestlands();

        LoadedWorld loaded = CurrentWorld();
        IPlayerRegistry worldPlayers = loaded.Scope.Container.Resolve<IPlayerRegistry>();
        int spawnPointCount = 0;
        foreach (KeyValuePair<Vector2Int, Scene> chunk in loaded.Chunks)
        {
            MobSpawnPoint[] spawnPoints = SceneQuery.FindAll<MobSpawnPoint>(chunk.Value);
            MobController[] mobs = SceneQuery.FindAll<MobController>(chunk.Value);
            spawnPointCount += spawnPoints.Length;
            Assert.That(mobs.Length, Is.EqualTo(spawnPoints.Length), $"Each spawn point of chunk {chunk.Key} should spawn exactly one mob.");
            foreach (MobSpawnPoint spawnPoint in spawnPoints)
            {
                bool spawnedHere = false;
                foreach (MobController mob in mobs)
                {
                    if (Vector2.Distance(mob.Patrol.SpawnPosition, spawnPoint.transform.position) < 0.01f)
                    {
                        spawnedHere = true;
                        Assert.That(mob.Navigation, Is.SameAs(loaded.Navigation));
                        Assert.That(mob.Players, Is.SameAs(worldPlayers));
                    }
                }

                Assert.That(spawnedHere, Is.True, $"No mob was spawned at {spawnPoint.name}.");
            }
        }

        Assert.That(spawnPointCount, Is.GreaterThan(0), "The loaded chunks should define at least one mob spawn point.");
    }

    /// <summary>
    /// A mob instantiated at runtime through a chunk scope's container shares the spawned mobs' world navigation and
    /// players.
    /// </summary>
    [UnityTest]
    public IEnumerator MobsSpawnedThroughAChunkScope_ShareTheWorldsNavigationAndPlayers()
    {
        yield return SceneBootTestHelper.BootIntoTestlands();
        yield return null;

        MobController firstMob = Object.FindAnyObjectByType<MobController>();
        Assert.That(firstMob, Is.Not.Null, "Testlands' start chunks should have at least one mob.");
        Assert.That(firstMob.Navigation, Is.SameAs(CurrentWorld().Navigation));

        IObjectResolver chunkResolver = LifetimeScope.Find<ChunkLifetimeScope>(firstMob.gameObject.scene).Container;
        MobController secondMob = chunkResolver.Instantiate(firstMob, firstMob.transform.position + Vector3.right * 2f, Quaternion.identity);
        yield return null;

        Assert.That(secondMob.Navigation, Is.SameAs(firstMob.Navigation));
        Assert.That(secondMob.Players, Is.SameAs(firstMob.Players));

        Object.Destroy(secondMob.gameObject);
    }

    /// <summary>
    /// A mob's melee hit takes exactly its damage from the player and shows a damage popup on the Gameplay popup
    /// layer, which fades out and returns to the pool.
    /// </summary>
    [UnityTest]
    public IEnumerator MobInTestlands_AttackDamagesPlayerAndPopupFadesOut()
    {
        yield return SceneBootTestHelper.BootIntoTestlands();
        yield return null;

        MobController brain = KeepOneMobActive();
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        Health health = player.GetComponent<Health>();

        Assert.That(brain, Is.Not.Null);
        Assert.That(health, Is.Not.Null, "Player should have Health wired in the Gameplay scene.");

        Vector3 originalPlayerPosition = player.transform.position;
        int startingHealth = health.CurrentHealth;
        int hit = brain.GetComponent<MeleeDamageDealer>().DamageAmount;

        player.transform.position = brain.transform.position + Vector3.right * 0.1f;
        yield return WaitForHealthChange(health, startingHealth - hit, 2f);

        Assert.That(health.CurrentHealth, Is.EqualTo(startingHealth - hit));

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

    /// <summary>
    /// A mob in melee range does not hit again within 0.2 s of its first hit, but has hit again 0.9 s after it.
    /// </summary>
    [UnityTest]
    public IEnumerator MobInTestlands_AttackRespectsConfiguredInterval()
    {
        yield return SceneBootTestHelper.BootIntoTestlands();
        yield return null;

        MobController brain = KeepOneMobActive();
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        Health health = player.GetComponent<Health>();

        Assert.That(brain, Is.Not.Null);
        Assert.That(health, Is.Not.Null);

        Vector3 originalPlayerPosition = player.transform.position;
        int startingHealth = health.CurrentHealth;
        int hit = brain.GetComponent<MeleeDamageDealer>().DamageAmount;

        player.transform.position = brain.transform.position + Vector3.right * 0.1f;
        yield return WaitForHealthChange(health, startingHealth - hit, 2f);
        Assert.That(health.CurrentHealth, Is.EqualTo(startingHealth - hit));

        yield return new WaitForSeconds(0.2f);
        Assert.That(health.CurrentHealth, Is.EqualTo(startingHealth - hit), "Health should not drop again before the attack interval elapses.");

        yield return new WaitForSeconds(0.7f);
        Assert.That(health.CurrentHealth, Is.LessThanOrEqualTo(startingHealth - 2 * hit), "Health should drop again after the configured attack interval.");

        player.transform.position = originalPlayerPosition;
    }

    /// <summary>
    /// The Weasel draws its baked set (Docs/Animation.md, Phase 7): chasing a player it runs, and in reach it plays its
    /// dagger attack, whose contact frame shows on the frame its hit lands on the player.
    /// </summary>
    [UnityTest]
    public IEnumerator TheWeasel_RunsAndAttacksFromItsBakedSet_ItsContactFrameShowingAsItsHitLands()
    {
        yield return SceneBootTestHelper.BootIntoTestlands();
        yield return null;

        MobController brain = KeepOneMobActive();
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        Health health = player.GetComponent<Health>();
        CharacterVisuals visuals = brain.GetComponentInChildren<CharacterVisuals>();
        SpriteRenderer body = visuals.GetComponent<SpriteRenderer>();
        Assert.That(visuals, Is.Not.Null);

        MovePlayer(player, brain.transform.position + Vector3.right * 3f);
        yield return WaitForSecondsUntil(() => visuals.Animator.Gait == CharacterGait.Run, 2f);
        Assert.That(visuals.Animator.Gait, Is.EqualTo(CharacterGait.Run), "The chasing Weasel runs.");
        Assert.That(SpriteName(body.sprite), Does.StartWith("Weasel_Run_"));

        int hitFrame = -1;
        System.Action<Health.DamageEvent> onDamaged = _ => hitFrame = Time.frameCount;
        health.Damaged += onDamaged;
        try
        {
            float give = Time.realtimeSinceStartup + 4f;
            while (hitFrame < 0 && Time.realtimeSinceStartup < give)
            {
                yield return new WaitForEndOfFrame();
                if (hitFrame < 0 && visuals.Animator.IsActing)
                {
                    Assert.That(visuals.Animator.Frame, Is.LessThan(visuals.Animator.Clip.ContactFrame), "Before its hit lands the Weasel shows its windup.");
                }
            }

            Assert.That(hitFrame, Is.GreaterThan(0), "The Weasel's hit lands.");
            Assert.That(visuals.Animator.IsActing, Is.True);
            CharacterAnimationClip clip = visuals.Animator.Clip;
            Assert.That(clip.Name, Is.EqualTo("DaggerSlash"));
            Assert.That(visuals.Animator.Frame, Is.EqualTo(clip.ContactFrame), "The contact frame shows on the frame the hit lands.");
            Assert.That(body.sprite == clip.SpriteAt(visuals.Animator.Direction, clip.ContactFrame), Is.True, "The renderer shows the contact frame.");
        }
        finally
        {
            health.Damaged -= onDamaged;
        }
    }

    /// <summary>
    /// A player who steps out of the Weasel's reach during its windup takes nothing from that attack.
    /// </summary>
    [UnityTest]
    public IEnumerator APlayerSteppingOutOfReachMidWindup_TakesNoDamage()
    {
        yield return SceneBootTestHelper.BootIntoTestlands();
        yield return null;

        MobController brain = KeepOneMobActive();
        MeleeDamageDealer dealer = brain.GetComponent<MeleeDamageDealer>();
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        Health health = player.GetComponent<Health>();

        MovePlayer(player, brain.transform.position + Vector3.right * 0.7f);
        yield return WaitForSecondsUntil(() => dealer.Phase == AttackPhase.Windup, 3f);
        Assert.That(dealer.Phase, Is.EqualTo(AttackPhase.Windup));
        int before = health.CurrentHealth;
        MovePlayer(player, brain.transform.position + Vector3.right * 3f);
        yield return WaitForSecondsUntil(() => dealer.Phase != AttackPhase.Windup, 1f);
        yield return WaitFrames(2);

        Assert.That(dealer.Phase, Is.Not.EqualTo(AttackPhase.Windup), "The windup ran out.");
        Assert.That(health.CurrentHealth, Is.EqualTo(before), "Out of reach at contact, the player takes nothing.");
    }

    /// <summary>
    /// A stun during the Weasel's windup stops its attack at once, on screen too, and nothing lands.
    /// </summary>
    [UnityTest]
    public IEnumerator AStunnedWeasel_StopsItsWindup()
    {
        yield return SceneBootTestHelper.BootIntoTestlands();
        yield return null;

        MobController brain = KeepOneMobActive();
        MeleeDamageDealer dealer = brain.GetComponent<MeleeDamageDealer>();
        CharacterVisuals visuals = brain.GetComponentInChildren<CharacterVisuals>();
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        Health health = player.GetComponent<Health>();
        StatusEffectService statuses = Object.FindAnyObjectByType<GameplayLifetimeScope>().Container.Resolve<StatusEffectService>();

        MovePlayer(player, brain.transform.position + Vector3.right * 0.7f);
        yield return WaitForSecondsUntil(() => dealer.Phase == AttackPhase.Windup && visuals.Animator.IsActing, 3f);
        int before = health.CurrentHealth;
        Assert.That(statuses.Apply(brain.GetComponent<DamageReceiver>(), SceneBootTestHelper.FindStatus("Stun")), Is.EqualTo(StatusApplyOutcome.Landed));
        yield return WaitFrames(2);

        Assert.That(dealer.IsAttacking, Is.False, "The stun cancels the attack.");
        Assert.That(visuals.Animator.IsActing, Is.False, "The Weasel stops showing it.");
        yield return new WaitForSeconds(dealer.Timeline.Windup);
        Assert.That(health.CurrentHealth, Is.EqualTo(before), "Nothing lands.");
    }

    /// <summary>
    /// When the player dies under attack, its movement and weapon controllers are disabled and the mob stops both
    /// attacking and chasing.
    /// </summary>
    [UnityTest]
    public IEnumerator PlayerInTestlands_DeathDisablesControlsAndMobDisengages()
    {
        yield return SceneBootTestHelper.BootIntoTestlands();
        yield return null;

        MobController brain = KeepOneMobActive();
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        Health health = player.GetComponent<Health>();
        PlayerController playerController = player.GetComponent<PlayerController>();
        PlayerWeaponController weaponController = player.GetComponent<PlayerWeaponController>();

        Assert.That(brain, Is.Not.Null);
        Assert.That(health, Is.Not.Null);

        player.transform.position = brain.transform.position + Vector3.right * 0.1f;
        yield return WaitForHealthChange(health, health.CurrentHealth - 1, 2f);
        Assert.That(brain.CurrentStateId, Is.EqualTo(MobStateId.AttackRange));

        health.ApplyDamage(health.CurrentHealth);
        yield return WaitFrames(2);
        yield return WaitForSecondsUntil(() => brain.CurrentStateId != MobStateId.AttackRange, 1.5f);

        Assert.That(health.IsDead, Is.True);
        Assert.That(playerController.enabled, Is.False, "Player movement should stop on death.");
        Assert.That(weaponController.enabled, Is.False, "Player attacks should stop on death.");
        Assert.That(brain.CurrentStateId, Is.Not.EqualTo(MobStateId.AttackRange), "Mob should stop attacking a dead player.");
        Assert.That(brain.CurrentStateId, Is.Not.EqualTo(MobStateId.Chase), "Mob should not chase a dead player.");
    }

    /// <summary>
    /// Lethal damage destroys the mob within a frame and plays its pooled death effect where the mob stood, unscaled and
    /// sorted by its feet, which goes back to the pool after playback.
    /// </summary>
    [UnityTest]
    public IEnumerator MobInTestlands_DeathSpawnsAnimationAndDestroysMob()
    {
        yield return SceneBootTestHelper.BootIntoTestlands();
        yield return null;

        MobController mob = Object.FindAnyObjectByType<MobController>();
        Assert.That(mob, Is.Not.Null);

        DamageReceiver receiver = mob.GetComponent<DamageReceiver>();
        Health health = mob.GetComponent<Health>();
        Assert.That(receiver, Is.Not.Null);
        Assert.That(health, Is.Not.Null);

        DamageService damageService = Object.FindAnyObjectByType<GameplayLifetimeScope>().Container.Resolve<DamageService>();
        EffectSprites deathSprites = mob.GetComponent<DestroyMobOnDeath>().DeathEffect.Sprites;
        Vector2 feet = mob.transform.position;
        damageService.ApplyDamage(receiver, health.CurrentHealth);
        EffectPlayer deathEffect = FindPlaying(deathSprites);
        yield return null;

        Assert.That(health.IsDead, Is.True);
        Assert.That(mob == null, Is.True, "Mob root should be destroyed after death.");
        Assert.That(deathEffect, Is.Not.Null, "The death effect should play when the mob dies.");
        Assert.That(Vector2.Distance(deathEffect.transform.position, feet), Is.LessThan(1e-3f), "The death effect stands where the mob stood.");
        Assert.That(deathEffect.transform.lossyScale, Is.EqualTo(Vector3.one), "The death effect is drawn unscaled.");
        Assert.That(deathEffect.GetComponent<YPositionSorter>().SortingY, Is.EqualTo(feet.y).Within(1e-3f), "The death effect sorts where the mob stood.");

        yield return new WaitForSeconds(deathSprites.Duration + 0.5f);
        Assert.That(deathEffect.IsPlaying, Is.False, "The death effect ends after playback and goes back to its pool.");
        Assert.That(deathEffect.GetComponent<SpriteRenderer>().enabled, Is.False, "A pooled effect is hidden.");
    }

    /// <summary>
    /// The effect playing <paramref name="sprites"/> now, or null.
    /// </summary>
    private static EffectPlayer FindPlaying(EffectSprites sprites)
    {
        foreach (EffectPlayer effect in Object.FindObjectsByType<EffectPlayer>())
        {
            if (effect.IsPlaying && effect.Sprites == sprites)
            {
                return effect;
            }
        }

        return null;
    }

    /// <summary>
    /// The HUD shows the player's health text and fill and the equipped weapon icon, and updates after damage; a sword
    /// attack deals the weapon's damage to an adjacent mob at contact, not before its windup is over.
    /// </summary>
    [UnityTest]
    public IEnumerator PlayerInTestlands_HudDisplaysWeaponIconAndHealthAndSlashDamageStillWorks()
    {
        yield return SceneBootTestHelper.BootIntoTestlands();
        yield return null;

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        MobController mob = KeepOneMobActive();
        PlayerHudView hud = Object.FindAnyObjectByType<PlayerHudView>();

        Assert.That(player, Is.Not.Null);
        Assert.That(mob, Is.Not.Null);
        Assert.That(hud, Is.Not.Null, "The Gameplay scene should contain the reusable PlayerHudView.");

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

        int startingHealth = mobHealth.CurrentHealth;
        int expectedDamage = weaponController.CurrentWeapon != null ? weaponController.CurrentWeapon.Damage : 1;
        float windup = weaponController.CurrentWeapon.Timeline.Windup;

        playerController.Teleport(mob.transform.position + Vector3.left * 0.45f);
        Assert.That(weaponController.TryAttack(Vector2.right), Is.True);
        float started = Time.time;
        Assert.That(mobHealth.CurrentHealth, Is.EqualTo(startingHealth), "Nothing lands as the attack starts.");
        float deadline = Time.realtimeSinceStartup + 5f;
        while (mobHealth.CurrentHealth == startingHealth)
        {
            Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "The sword attack should land at contact.");
            yield return null;
        }

        Assert.That(Time.time - started, Is.GreaterThanOrEqualTo(windup - 0.0001f), "The hit lands at contact, after the windup.");
        Assert.That(mobHealth.CurrentHealth, Is.EqualTo(startingHealth - expectedDamage));
    }

    /// <summary>
    /// The loaded world the local player is in.
    /// </summary>
    private static LoadedWorld CurrentWorld()
    {
        Assert.That(SceneBootTestHelper.ResolveFromGameplay<Worlds>().TryGet(SceneBootTestHelper.ResolveGameFlow().CurrentWorld, out LoadedWorld loaded), Is.True);
        return loaded;
    }

    /// <summary>
    /// Picks one of the loaded mobs and deactivates the others, so the test sees only that mob's hits and states.
    /// Returns null when there is none.
    /// </summary>
    private static MobController KeepOneMobActive()
    {
        MobController kept = Object.FindAnyObjectByType<MobController>();
        foreach (MobController mob in Object.FindObjectsByType<MobController>())
        {
            mob.gameObject.SetActive(mob == kept);
        }

        return kept;
    }

    /// <summary>
    /// The first <see cref="Image"/> under <paramref name="root"/>, inactive ones included, on an object named
    /// <paramref name="objectName"/>, or null.
    /// </summary>
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

    /// <summary>
    /// The first <see cref="TextMeshProUGUI"/> under <paramref name="root"/>, inactive ones included, on an object
    /// named <paramref name="objectName"/>, or null.
    /// </summary>
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

    /// <summary>
    /// Yields <paramref name="frameCount"/> frames.
    /// </summary>
    private static IEnumerator WaitFrames(int frameCount)
    {
        for (int i = 0; i < frameCount; i++)
        {
            yield return null;
        }
    }

    /// <summary>
    /// Yields frames until <paramref name="health"/> is at or below <paramref name="expectedHealth"/>, giving up
    /// silently after <paramref name="maxSeconds"/> seconds; callers assert the result.
    /// </summary>
    private static IEnumerator WaitForHealthChange(Health health, int expectedHealth, float maxSeconds)
    {
        yield return WaitForSecondsUntil(() => health.CurrentHealth <= expectedHealth, maxSeconds);
    }

    /// <summary>
    /// Yields frames until <paramref name="condition"/> holds, giving up silently after <paramref name="maxSeconds"/>
    /// seconds; callers assert the result.
    /// </summary>
    private static IEnumerator WaitForSecondsUntil(System.Func<bool> condition, float maxSeconds)
    {
        float giveUp = Time.realtimeSinceStartup + maxSeconds;
        while (!condition() && Time.realtimeSinceStartup < giveUp)
        {
            yield return null;
        }
    }

    /// <summary>
    /// Puts the player's body and transform at <paramref name="position"/>.
    /// </summary>
    private static void MovePlayer(GameObject player, Vector3 position)
    {
        player.transform.position = position;
        Rigidbody2D body = player.GetComponent<Rigidbody2D>();
        if (body != null)
        {
            body.position = position;
            body.linearVelocity = Vector2.zero;
        }

        Physics2D.SyncTransforms();
    }

    /// <summary>
    /// The name of <paramref name="sprite"/>, or empty without one.
    /// </summary>
    private static string SpriteName(Sprite sprite)
    {
        return sprite != null ? sprite.name : string.Empty;
    }
}
