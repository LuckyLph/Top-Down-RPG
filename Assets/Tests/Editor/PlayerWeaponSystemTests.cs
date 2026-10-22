using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Tests for the player's weapon and HUD: equipping and the weapon icon, the HUD presenter, attacks on their timeline
/// (the hit at contact, the cooldown, the events the owner replicates), the controller's attack orders under the cancel
/// rules and crowd control, the hit area <see cref="AttackService"/> resolves, and the Main scope's input asset.
/// </summary>
public class PlayerWeaponSystemTests
{
    private GameObject root;
    private PlayerHudPresenter hudPresenter;
    private PlayerControlSettings settings;
    private GameObject enemyRoot;

    /// <summary>
    /// Disposes the HUD presenter and destroys the test HUD canvases, the player and enemy roots and the control
    /// settings the test created.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        hudPresenter?.Dispose();
        hudPresenter = null;

        foreach (Canvas canvas in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include))
        {
            if (canvas != null && (canvas.name == "WeaponHudCanvas" || canvas.name == "PlayerHudCanvas"))
            {
                Object.DestroyImmediate(canvas.gameObject);
            }
        }

        if (root != null)
        {
            Object.DestroyImmediate(root);
        }

        if (settings != null)
        {
            Object.DestroyImmediate(settings);
        }

        if (enemyRoot != null)
        {
            Object.DestroyImmediate(enemyRoot);
        }
    }

    /// <summary>
    /// Equipping a weapon with an icon sets the weapon name and shows the icon sprite fully opaque on the HUD.
    /// </summary>
    [Test]
    public void Equip_UpdatesEquippedWeaponIcon()
    {
        root = new GameObject("PlayerRoot");
        root.AddComponent<Rigidbody2D>();
        root.AddComponent<PlayerController>();
        PlayerWeaponController weaponController = root.AddComponent<PlayerWeaponController>();

        Image iconImage = CreatePlayerHudCanvas(weaponController).WeaponIcon;
        Sprite iconSprite = LoadWeaponHudIconSprite();

        weaponController.Equip(CreateTestWeapon("Sword", icon: iconSprite));

        Assert.That(weaponController.CurrentWeaponName, Is.EqualTo("Sword"));
        Assert.That(iconImage.sprite, Is.SameAs(iconSprite));
        Assert.That(iconImage.color.a, Is.EqualTo(1f).Within(0.001f));
    }

    /// <summary>
    /// Equipping a weapon without an icon clears the HUD icon sprite and dims it to the fallback alpha.
    /// </summary>
    [Test]
    public void Equip_WithoutIcon_UsesHudFallbackTintAndClearsSprite()
    {
        root = new GameObject("PlayerRoot");
        root.AddComponent<Rigidbody2D>();
        root.AddComponent<PlayerController>();
        PlayerWeaponController weaponController = root.AddComponent<PlayerWeaponController>();

        Image iconImage = CreatePlayerHudCanvas(weaponController).WeaponIcon;

        weaponController.Equip(CreateTestWeapon("Training Sword", icon: null));

        Assert.That(iconImage.sprite, Is.Null);
        Assert.That(iconImage.color.a, Is.EqualTo(0.25f).Within(0.001f));
    }

    /// <summary>
    /// The HUD presenter shows the local player's health text and fill on damage and heals, and stops updating
    /// once disposed.
    /// </summary>
    [Test]
    public void HudPresenter_ShowsHealthChangesUntilDisposed()
    {
        root = new GameObject("PlayerRoot");
        root.AddComponent<Rigidbody2D>();
        root.AddComponent<PlayerController>();
        Health health = root.AddComponent<Health>();
        PlayerWeaponController weaponController = root.AddComponent<PlayerWeaponController>();

        HudElements hud = CreatePlayerHudCanvas(weaponController);
        Assert.That(hud.HealthText.text, Is.EqualTo($"HP {health.MaxHealth}"));
        Assert.That(hud.HealthFill.fillAmount, Is.EqualTo(1f).Within(0.001f));

        health.ApplyDamage(3);
        Assert.That(hud.HealthText.text, Is.EqualTo($"HP {health.CurrentHealth}"));
        Assert.That(hud.HealthFill.fillAmount, Is.EqualTo((float)health.CurrentHealth / health.MaxHealth).Within(0.001f));

        health.Heal(2);
        Assert.That(hud.HealthText.text, Is.EqualTo($"HP {health.MaxHealth - 1}"), "Heals show too.");

        hudPresenter.Dispose();
        hudPresenter = null;
        string textAfterDispose = hud.HealthText.text;
        health.ApplyDamage(1);
        Assert.That(hud.HealthText.text, Is.EqualTo(textAfterDispose), "A disposed presenter should stop listening.");
    }

    /// <summary>
    /// <see cref="PlayerWeaponController.TryAttack"/> starts an attack toward the given direction rather than the
    /// facing: nothing lands during the windup, the unit in front takes the hit once at contact and the one beside it
    /// none, and no attack starts while one runs or before the cooldown from its start has passed.
    /// </summary>
    [Test]
    public void TryAttack_HitsAtContact_AndRespectsTheCooldown()
    {
        ManualClock clock = new();
        PlayerWeaponController weaponController = CreatePlayer(clock, out PlayerController playerController);
        weaponController.Equip(CreateTestWeapon("Sword", damage: 2, cooldown: 0.5f, distance: 0.75f));
        playerController.Face(Vector2.up);
        enemyRoot = new GameObject("EnemyRoot");
        Health ahead = CreateDamageable("Ahead", new Vector2(0.9f, 0f), enemyRoot.transform).GetComponent<Health>();
        Health above = CreateDamageable("Above", new Vector2(0f, 1.5f), enemyRoot.transform).GetComponent<Health>();
        Physics2D.SyncTransforms();

        Assert.That(weaponController.TryAttack(new Vector2(3f, 0f)), Is.True);
        Assert.That(weaponController.TryAttack(Vector2.right), Is.False, "An attack is running.");
        Assert.That(weaponController.AttackPhase, Is.EqualTo(AttackPhase.Windup));
        weaponController.Tick();
        clock.Advance(0.0625f);
        weaponController.Tick();
        Assert.That(ahead.CurrentHealth, Is.EqualTo(ahead.MaxHealth), "Nothing lands during the windup.");

        clock.Advance(0.0625f);
        weaponController.Tick();
        Assert.That(weaponController.AttackPhase, Is.EqualTo(AttackPhase.Contact));
        Assert.That(ahead.CurrentHealth, Is.EqualTo(ahead.MaxHealth - 2), "The hit lands at contact.");
        Assert.That(above.CurrentHealth, Is.EqualTo(above.MaxHealth), "Toward the given direction, not the facing.");
        weaponController.Tick();
        clock.Advance(0.25f);
        weaponController.Tick();
        Assert.That(ahead.CurrentHealth, Is.EqualTo(ahead.MaxHealth - 2), "Only once.");
        Assert.That(weaponController.IsAttacking, Is.False);

        clock.Advance(0.0625f);
        Assert.That(weaponController.TryAttack(Vector2.right), Is.False, "The cooldown runs from the start.");
        clock.Advance(0.0625f);
        Assert.That(weaponController.TryAttack(Vector2.right), Is.True);
    }

    /// <summary>
    /// A click on an enemy in range makes the <see cref="PlayerController"/> take an attack order, face the enemy and
    /// start the windup toward it on the same tick, holding the motor still; the enemy takes the hit at contact, and the
    /// motor moves again once the attack is over.
    /// </summary>
    [Test]
    public void PlayerController_AttackOrderOnAnEnemyInRange_StartsTheWindup_AndHitsAtContact()
    {
        ManualClock clock = new();
        PlayerWeaponController weaponController = CreatePlayer(clock, out PlayerController playerController);
        weaponController.Equip(CreateTestWeapon("Sword"));
        GameObject enemy = CreateEnemyInRange();
        Health enemyHealth = enemy.GetComponent<Health>();
        FakeCommandSource commands = new();
        playerController.SetCommandSource(commands);
        PlayerMotor2D motor = root.GetComponent<PlayerMotor2D>();

        commands.Next = ClickOn(enemy);
        playerController.Tick();
        Assert.That(playerController.CurrentOrder, Is.EqualTo(PlayerOrderKind.Attack));
        Assert.That(playerController.FacingDirection, Is.EqualTo(Vector2.left));
        Assert.That(weaponController.AttackPhase, Is.EqualTo(AttackPhase.Windup));
        Assert.That(motor.SpeedScale, Is.Zero, "The motor holds still while the attack runs.");
        Assert.That(enemyHealth.CurrentHealth, Is.EqualTo(enemyHealth.MaxHealth));

        commands.Next = default;
        clock.Advance(0.125f);
        playerController.Tick();
        Assert.That(enemyHealth.CurrentHealth, Is.EqualTo(enemyHealth.MaxHealth - 1), "The hit lands at contact.");

        clock.Advance(0.1875f);
        playerController.Tick();
        Assert.That(weaponController.IsAttacking, Is.False);
        Assert.That(motor.SpeedScale, Is.EqualTo(1f));
    }

    /// <summary>
    /// Through the <see cref="PlayerController"/>, a ground click during the windup cancels the attack (nothing lands,
    /// the cooldown is given back so the next click on the enemy attacks at once), and a ground click during the
    /// recovery ends it at once, both under the weapon's default cancel rules.
    /// </summary>
    [Test]
    public void PlayerController_AMoveClickCancelsTheWindup_AndAnyClickEndsTheRecovery()
    {
        ManualClock clock = new();
        PlayerWeaponController weaponController = CreatePlayer(clock, out PlayerController playerController);
        weaponController.Equip(CreateTestWeapon("Sword"));
        GameObject enemy = CreateEnemyInRange();
        Health enemyHealth = enemy.GetComponent<Health>();
        FakeCommandSource commands = new();
        playerController.SetCommandSource(commands);
        int cancelled = 0;
        weaponController.AttackCancelled += () => cancelled++;

        commands.Next = ClickOn(enemy);
        playerController.Tick();
        clock.Advance(0.0625f);
        commands.Next = new PlayerCommand(new Vector2(5f, 0f), default, movePressed: true, moveHeld: true, stopPressed: false);
        playerController.Tick();
        Assert.That(playerController.CurrentOrder, Is.EqualTo(PlayerOrderKind.Move));
        Assert.That(weaponController.IsAttacking, Is.False, "The move cancels the windup.");
        Assert.That(cancelled, Is.EqualTo(1));
        commands.Next = default;
        clock.Advance(0.25f);
        playerController.Tick();
        Assert.That(enemyHealth.CurrentHealth, Is.EqualTo(enemyHealth.MaxHealth), "Nothing lands.");

        commands.Next = ClickOn(enemy);
        playerController.Tick();
        Assert.That(weaponController.AttackPhase, Is.EqualTo(AttackPhase.Windup), "The cooldown was given back.");
        commands.Next = default;
        clock.Advance(0.25f);
        playerController.Tick();
        Assert.That(weaponController.AttackPhase, Is.EqualTo(AttackPhase.Recovery));
        Assert.That(enemyHealth.CurrentHealth, Is.EqualTo(enemyHealth.MaxHealth - 1));
        commands.Next = new PlayerCommand(new Vector2(5f, 0f), default, movePressed: true, moveHeld: true, stopPressed: false);
        playerController.Tick();
        Assert.That(weaponController.IsAttacking, Is.False, "The click ends the recovery.");
        Assert.That(cancelled, Is.EqualTo(2));
    }

    /// <summary>
    /// The <see cref="PlayerController"/> passes its statuses to its orders, motor and attack: a stun refuses orders
    /// and swings and zeroes the motor speed scale, a slow scales it, an attack holds it still whatever the slow, and a
    /// stun during the windup cancels the attack so nothing lands.
    /// </summary>
    [Test]
    public void PlayerController_FeedsStatusCrowdControlToItsOrdersMotorAndAttack()
    {
        ManualClock clock = new();
        PlayerWeaponController weaponController = CreatePlayer(clock, out PlayerController playerController);
        StatusEffects statuses = root.AddComponent<StatusEffects>();
        weaponController.Equip(CreateTestWeapon("Sword"));
        GameObject enemy = CreateEnemyInRange();
        Health enemyHealth = enemy.GetComponent<Health>();
        FakeCommandSource commands = new();
        playerController.SetCommandSource(commands);
        StatusEffectDefinition stun = StatusEffectDefinition.Create("Stun", StatusKind.Debuff, 5f, controls: StatusControls.Stun);
        StatusEffectDefinition chill = StatusEffectDefinition.Create("Chill", StatusKind.Debuff, 5f, moveSpeedMultiplier: 0.6f);
        PlayerMotor2D motor = root.GetComponent<PlayerMotor2D>();

        try
        {
            statuses.Set.Apply(stun, null, 1f);
            commands.Next = ClickOn(enemy);
            playerController.Tick();

            Assert.That(playerController.CurrentOrder, Is.EqualTo(PlayerOrderKind.Idle), "A stunned player takes no orders.");
            Assert.That(weaponController.IsAttacking, Is.False, "Nor swings.");
            Assert.That(motor.SpeedScale, Is.Zero);

            statuses.Set.Remove(stun);
            statuses.Set.Apply(chill, null, 1f);
            commands.Next = default;
            playerController.Tick();
            Assert.That(motor.SpeedScale, Is.EqualTo(0.6f).Within(0.0001f));

            commands.Next = ClickOn(enemy);
            playerController.Tick();
            Assert.That(weaponController.AttackPhase, Is.EqualTo(AttackPhase.Windup));
            Assert.That(motor.SpeedScale, Is.Zero, "The attack holds the motor still.");

            statuses.Set.Apply(stun, null, 1f);
            commands.Next = default;
            playerController.Tick();
            Assert.That(weaponController.IsAttacking, Is.False, "A stun cancels the attack.");
            clock.Advance(0.25f);
            playerController.Tick();
            Assert.That(enemyHealth.CurrentHealth, Is.EqualTo(enemyHealth.MaxHealth), "Nothing lands.");
        }
        finally
        {
            Object.DestroyImmediate(stun);
            Object.DestroyImmediate(chill);
        }
    }

    /// <summary>
    /// A local attack raises its start (a zero direction attacks where the player faces, at attack speed 1) and, at
    /// contact, its contact with where the player stands; a recovery cut short by an earlier order raises an early end.
    /// Replaying another player's attack starts nothing here and raises nothing.
    /// </summary>
    [Test]
    public void TryAttack_RaisesItsStartContactAndEarlyEnd_ButRemoteAttacksDoNot()
    {
        ManualClock clock = new();
        PlayerWeaponController weaponController = CreatePlayer(clock, out PlayerController playerController);
        weaponController.Equip(CreateTestWeapon("Sword"));
        playerController.Face(Vector2.up);
        Vector2? started = null;
        float startedSpeed = 0f;
        Vector2? contactAt = null;
        int cancelled = 0;
        weaponController.AttackStarted += (_, direction, speed) =>
        {
            started = direction;
            startedSpeed = speed;
        };
        weaponController.AttackContacted += (_, _, position) => contactAt = position;
        weaponController.AttackCancelled += () => cancelled++;

        weaponController.PlayRemoteAttack(0, Vector2.left, 1f);
        weaponController.PlayRemoteContact();
        weaponController.StopRemoteAttack();
        Assert.That(started, Is.Null, "Replaying another player's attack must not be sent back out.");
        Assert.That(weaponController.IsAttacking, Is.False);

        Assert.That(weaponController.TryAttack(Vector2.zero), Is.True);
        Assert.That(started, Is.EqualTo(Vector2.up), "A zero direction attacks where the player faces.");
        Assert.That(startedSpeed, Is.EqualTo(1f));
        root.transform.position = new Vector3(1f, 2f, 0f);
        clock.Advance(0.125f);
        weaponController.Tick();
        Assert.That(contactAt, Is.EqualTo(new Vector2(1f, 2f)));
        weaponController.Order(false);
        Assert.That(cancelled, Is.Zero, "An order during the contact waits for the recovery.");
        clock.Advance(0.0625f);
        weaponController.Tick();
        Assert.That(cancelled, Is.EqualTo(1));
        Assert.That(weaponController.IsAttacking, Is.False);
    }

    /// <summary>
    /// When the local player is reassigned, the HUD presenter shows the new player's health and ignores the
    /// previous one.
    /// </summary>
    [Test]
    public void HudPresenter_FollowsTheLocalPlayerWhenItChanges()
    {
        root = new GameObject("PlayerRoot");
        root.AddComponent<Rigidbody2D>();
        root.AddComponent<PlayerController>();
        Health firstHealth = root.AddComponent<Health>();
        root.AddComponent<PlayerWeaponController>();
        GameObject second = new("SecondPlayer");
        second.transform.SetParent(root.transform);
        second.AddComponent<Rigidbody2D>();
        PlayerController secondController = second.AddComponent<PlayerController>();
        Health secondHealth = second.AddComponent<Health>();
        second.AddComponent<PlayerWeaponController>();

        HudElements hud = CreatePlayerHudCanvas(root.GetComponent<PlayerWeaponController>());
        LocalPlayerTracker localPlayer = new();
        hudPresenter.Dispose();
        hudPresenter = new PlayerHudPresenter(localPlayer, Object.FindAnyObjectByType<PlayerHudView>());
        hudPresenter.Start();

        localPlayer.Assign(new LocalPlayer(secondController));
        secondHealth.ApplyDamage(4);
        firstHealth.ApplyDamage(1);

        Assert.That(hud.HealthText.text, Is.EqualTo($"HP {secondHealth.CurrentHealth}"));
    }

    /// <summary>
    /// A <see cref="PlayerController"/> ticked without a command source does nothing and keeps facing down.
    /// </summary>
    [Test]
    public void PlayerController_WithoutCommandSource_StaysIdle()
    {
        root = new GameObject("PlayerRoot");
        root.AddComponent<Rigidbody2D>();
        PlayerController playerController = root.AddComponent<PlayerController>();

        playerController.Tick();

        Assert.That(playerController.FacingDirection, Is.EqualTo(Vector2.down));
    }

    /// <summary>
    /// An attack hits every unit in its area once, each with the weapon's damage, and never the attacker or a unit
    /// outside it; without authority it hits nothing.
    /// </summary>
    [Test]
    public void AnAttack_HitsEachUnitInItsAreaOnce_AndNeverTheAttacker()
    {
        root = new GameObject("CombatRoot");
        GameObject owner = CreateDamageable("Owner", Vector2.zero, root.transform);
        Health first = CreateDamageable("FirstTarget", new Vector2(0.45f, 0.18f), root.transform).GetComponent<Health>();
        Health second = CreateDamageable("SecondTarget", new Vector2(0.45f, -0.18f), root.transform).GetComponent<Health>();
        Health behind = CreateDamageable("Behind", new Vector2(-1.2f, 0f), root.transform).GetComponent<Health>();
        Physics2D.SyncTransforms();
        PlayerWeapon weapon = CreateTestWeapon("Sword", damage: 2, size: new Vector2(1.2f, 1.2f));

        Assert.That(CreateAttackService(new FixedGameAuthority(false)).Resolve(owner.transform, weapon, Vector2.right, Vector2.zero), Is.Zero);
        Assert.That(first.CurrentHealth, Is.EqualTo(first.MaxHealth), "No authority, no hit.");

        Assert.That(CreateAttackService().Resolve(owner.transform, weapon, Vector2.right, Vector2.zero), Is.EqualTo(2));
        Assert.That(first.CurrentHealth, Is.EqualTo(first.MaxHealth - 2));
        Assert.That(second.CurrentHealth, Is.EqualTo(second.MaxHealth - 2));
        Assert.That(behind.CurrentHealth, Is.EqualTo(behind.MaxHealth));
        Assert.That(owner.GetComponent<Health>().CurrentHealth, Is.EqualTo(owner.GetComponent<Health>().MaxHealth));
    }

    /// <summary>
    /// An attack that only meets a wall collider with no receiver of its own damages nothing, not even another
    /// receiver that shares the wall's root.
    /// </summary>
    [Test]
    public void AnAttack_HittingAWall_DoesNotDamageAnUnrelatedReceiverUnderTheSameRoot()
    {
        root = new GameObject("LevelRoot");
        Health distantEnemy = CreateDamageable("DistantEnemy", Vector2.right * 20f, root.transform).GetComponent<Health>();
        GameObject owner = CreateDamageable("Owner", Vector2.zero, root.transform);
        GameObject wall = new("Wall");
        wall.transform.SetParent(root.transform);
        wall.transform.position = Vector2.right * 0.6f;
        wall.AddComponent<BoxCollider2D>();
        Physics2D.SyncTransforms();

        Assert.That(CreateAttackService().Resolve(owner.transform, CreateTestWeapon("Sword", damage: 2), Vector2.right, Vector2.zero), Is.Zero);
        Assert.That(distantEnemy.CurrentHealth, Is.EqualTo(distantEnemy.MaxHealth));
        Assert.That(owner.GetComponent<Health>().CurrentHealth, Is.EqualTo(owner.GetComponent<Health>().MaxHealth));
    }

    /// <summary>
    /// The hit area's centre is the attacker's position plus its direction's offset (down, up, left, right) and the hit
    /// distance along the attack, and the area turns with the attack: a narrow, deep area hits the unit straight ahead
    /// in each direction and not the ones beside it.
    /// </summary>
    [Test]
    public void TheHitArea_LiesAlongTheAttack_FromItsDirectionsOffset()
    {
        Vector2[] offsets =
        {
            Vector2.up * 0.1f,
            Vector2.up * 0.3f,
            new Vector2(0.2f, 0.15f),
            new Vector2(0.2f, 0.15f)
        };
        PlayerWeapon placed = CreateTestWeapon("Sword", distance: 1f, offsets: offsets);
        Assert.That(Vector2.Distance(placed.HitCentre(Vector2.one, Vector2.down), new Vector2(1f, 0.1f)), Is.LessThan(0.001f));
        Assert.That(Vector2.Distance(placed.HitCentre(Vector2.one, Vector2.up), new Vector2(1f, 2.3f)), Is.LessThan(0.001f));
        Assert.That(Vector2.Distance(placed.HitCentre(Vector2.one, Vector2.left), new Vector2(0.2f, 1.15f)), Is.LessThan(0.001f));
        Assert.That(Vector2.Distance(placed.HitCentre(Vector2.one, Vector2.right), new Vector2(2.2f, 1.15f)), Is.LessThan(0.001f));

        root = new GameObject("CombatRoot");
        GameObject owner = new("Owner");
        owner.transform.SetParent(root.transform);
        Health ahead = CreateSmallDamageable("Ahead", new Vector2(1.5f, 0f)).GetComponent<Health>();
        Health beside = CreateSmallDamageable("Beside", new Vector2(1f, 0.6f)).GetComponent<Health>();
        Health above = CreateSmallDamageable("Above", new Vector2(0f, 1.5f)).GetComponent<Health>();
        Physics2D.SyncTransforms();
        PlayerWeapon narrow = CreateTestWeapon("Spear", damage: 1, distance: 1f, size: new Vector2(0.2f, 2f));
        AttackService attacks = CreateAttackService();

        Assert.That(attacks.Resolve(owner.transform, narrow, Vector2.right, Vector2.zero), Is.EqualTo(1));
        Assert.That(ahead.CurrentHealth, Is.EqualTo(ahead.MaxHealth - 1));
        Assert.That(beside.CurrentHealth, Is.EqualTo(beside.MaxHealth));
        Assert.That(above.CurrentHealth, Is.EqualTo(above.MaxHealth));
        Assert.That(attacks.Resolve(owner.transform, narrow, Vector2.up, Vector2.zero), Is.EqualTo(1));
        Assert.That(above.CurrentHealth, Is.EqualTo(above.MaxHealth - 1));
        Assert.That(beside.CurrentHealth, Is.EqualTo(beside.MaxHealth));
    }

    /// <summary>
    /// The <see cref="MainLifetimeScope"/> in the Main scene references the project's input action asset, which
    /// is the only one builds use; the scene is opened additively and closed again.
    /// </summary>
    [Test]
    public void MainScope_AssignsProjectInputActionAsset()
    {
        InputActionAsset actionsAsset = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/InputSystem_Actions.inputactions");
        Assert.That(actionsAsset, Is.Not.Null);

        SceneSetup[] previousSetup = EditorSceneManager.GetSceneManagerSetup();
        Scene mainScene = EditorSceneManager.OpenScene("Assets/Scenes/Main.unity", OpenSceneMode.Additive);
        try
        {
            MainLifetimeScope scope = mainScene.GetRootGameObjects()
                .Select(root => root.GetComponentInChildren<MainLifetimeScope>(true))
                .FirstOrDefault(found => found != null);
            Assert.That(scope, Is.Not.Null);

            SerializedProperty assetProperty = new SerializedObject(scope).FindProperty("inputActions");
            Assert.That(assetProperty.objectReferenceValue, Is.SameAs(actionsAsset), "Builds only use the asset assigned on the Main scope.");
        }
        finally
        {
            EditorSceneManager.CloseScene(mainScene, true);
            if (previousSetup.Length > 0)
            {
                EditorSceneManager.RestoreSceneManagerSetup(previousSetup);
            }
        }
    }

    /// <summary>
    /// An <see cref="IPlayerCommandSource"/> that hands out whatever command the test put in <see cref="Next"/>.
    /// </summary>
    private sealed class FakeCommandSource : IPlayerCommandSource
    {
        public PlayerCommand Next { get; set; }

        /// <summary>
        /// Returns <see cref="Next"/> every time, without clearing it.
        /// </summary>
        public PlayerCommand ReadCommand()
        {
            return Next;
        }
    }

    /// <summary>
    /// Creates an attack service over a hit service under <paramref name="authority"/>, authoritative by default, so
    /// attacks deal damage.
    /// </summary>
    private static AttackService CreateAttackService(IGameAuthority authority = null)
    {
        return TestCombat.CreateAttackService(authority ?? FixedGameAuthority.Authoritative);
    }

    /// <summary>
    /// Creates a weapon with the given stats, a timeline of 0.125 s, 0.0625 s and 0.125 s with both cancel rules on,
    /// zero hit offsets by default and a 0.9 by 0.9 hit area by default.
    /// </summary>
    private static PlayerWeapon CreateTestWeapon(
        string name,
        int damage = 1,
        float cooldown = 0.35f,
        float distance = 0.55f,
        Vector2? size = null,
        Vector2[] offsets = null,
        Sprite icon = null)
    {
        return PlayerWeapon.Create(
            name,
            damage,
            cooldown,
            distance,
            offsets ?? new Vector2[4],
            icon,
            size: size ?? new Vector2(0.9f, 0.9f),
            windup: 0.125f,
            contact: 0.0625f,
            recovery: 0.125f);
    }

    /// <summary>
    /// Creates the test player at the origin (a 0.4 by 0.2 collider, a kinematic body, a controller with test control
    /// settings and a weapon controller), with the controller and the weapon on <paramref name="clock"/> and the weapon
    /// resolving hits with authority.
    /// </summary>
    private PlayerWeaponController CreatePlayer(ManualClock clock, out PlayerController playerController)
    {
        root = new GameObject("PlayerRoot");
        root.AddComponent<BoxCollider2D>().size = new Vector2(0.4f, 0.2f);
        root.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
        playerController = root.AddComponent<PlayerController>();
        playerController.Construct(clock);
        PlayerWeaponController weaponController = root.AddComponent<PlayerWeaponController>();
        weaponController.Construct(CreateAttackService(), clock);
        settings = TestPlayerControlSettings.Create();
        TestPlayerControlSettings.Assign(playerController, settings);
        return weaponController;
    }

    /// <summary>
    /// Creates an enemy half a unit left of the origin with a 0.4 by 0.2 collider, within the test weapon's range and
    /// hit area.
    /// </summary>
    private GameObject CreateEnemyInRange()
    {
        enemyRoot = new GameObject("EnemyRoot");
        GameObject enemy = CreateDamageable("Enemy", new Vector2(-0.5f, 0f), enemyRoot.transform);
        enemy.GetComponent<BoxCollider2D>().size = new Vector2(0.4f, 0.2f);
        Physics2D.SyncTransforms();
        return enemy;
    }

    /// <summary>
    /// A click on <paramref name="enemy"/>, which picks it as an enemy target.
    /// </summary>
    private static PlayerCommand ClickOn(GameObject enemy)
    {
        return new PlayerCommand(
            enemy.transform.position,
            new UnitTarget(enemy.GetComponent<Health>(), enemy.GetComponent<Collider2D>(), UnitTeam.Enemy),
            movePressed: true,
            moveHeld: true,
            stopPressed: false);
    }

    /// <summary>
    /// Creates a damageable unit under the test root at <paramref name="position"/> with a 0.1 by 0.1 collider.
    /// </summary>
    private GameObject CreateSmallDamageable(string name, Vector2 position)
    {
        GameObject unit = CreateDamageable(name, position, root.transform);
        unit.GetComponent<BoxCollider2D>().size = new Vector2(0.1f, 0.1f);
        return unit;
    }

    /// <summary>
    /// Creates a damageable unit under <paramref name="parent"/> at <paramref name="position"/>: box collider,
    /// kinematic body, <see cref="Health"/> and <see cref="DamageReceiver"/>.
    /// </summary>
    private static GameObject CreateDamageable(string name, Vector2 position, Transform parent)
    {
        GameObject target = new(name);
        target.transform.SetParent(parent);
        target.transform.position = position;
        target.AddComponent<BoxCollider2D>();
        target.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
        target.AddComponent<Health>();
        target.AddComponent<DamageReceiver>();
        return target;
    }

    /// <summary>
    /// The HUD widgets a test HUD canvas exposes for assertions.
    /// </summary>
    private readonly struct HudElements
    {
        /// <summary>
        /// Captures the health fill, health text and weapon icon of a test HUD.
        /// </summary>
        public HudElements(Image healthFill, TextMeshProUGUI healthText, Image weaponIcon)
        {
            HealthFill = healthFill;
            HealthText = healthText;
            WeaponIcon = weaponIcon;
        }

        public Image HealthFill { get; }
        public TextMeshProUGUI HealthText { get; }
        public Image WeaponIcon { get; }
    }

    /// <summary>
    /// Builds a "PlayerHudCanvas" with a <see cref="PlayerHudView"/> and starts a <see cref="PlayerHudPresenter"/>
    /// (kept in <see cref="hudPresenter"/>) bound to a local player tracker that points at the controller's
    /// player.
    /// </summary>
    private HudElements CreatePlayerHudCanvas(PlayerWeaponController weaponController)
    {
        GameObject canvasObject = new("PlayerHudCanvas");
        canvasObject.AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        canvasObject.AddComponent<CanvasScaler>();
        canvasObject.AddComponent<GraphicRaycaster>();

        GameObject healthPanel = new("HealthPanel");
        healthPanel.transform.SetParent(canvasObject.transform, false);

        GameObject healthFillObject = new("HealthFill");
        healthFillObject.transform.SetParent(healthPanel.transform, false);
        healthFillObject.AddComponent<CanvasRenderer>();
        Image healthFill = healthFillObject.AddComponent<Image>();
        healthFill.type = Image.Type.Filled;
        healthFill.fillMethod = Image.FillMethod.Horizontal;

        GameObject healthTextObject = new("HealthText");
        healthTextObject.transform.SetParent(healthPanel.transform, false);
        healthTextObject.AddComponent<CanvasRenderer>();
        TextMeshProUGUI healthText = healthTextObject.AddComponent<TextMeshProUGUI>();
        healthText.text = "HP --";
        healthText.alignment = TextAlignmentOptions.Center;

        GameObject weaponSlotObject = new("WeaponSlot");
        weaponSlotObject.transform.SetParent(canvasObject.transform, false);

        GameObject weaponIconObject = new("WeaponIcon");
        weaponIconObject.transform.SetParent(weaponSlotObject.transform, false);
        weaponIconObject.AddComponent<CanvasRenderer>();
        Image weaponIcon = weaponIconObject.AddComponent<Image>();
        weaponIcon.preserveAspect = true;

        PlayerHudView view = canvasObject.AddComponent<PlayerHudView>();
        view.ConfigureReferences(healthFill, healthText, weaponIcon);

        LocalPlayerTracker localPlayer = new();
        localPlayer.Assign(new LocalPlayer(weaponController.GetComponent<PlayerController>()));
        hudPresenter = new PlayerHudPresenter(localPlayer, view);
        hudPresenter.Start();
        return new HudElements(healthFill, healthText, weaponIcon);
    }

    /// <summary>
    /// Loads the first sprite of the project's static slash texture to use as a weapon icon, failing the test
    /// when there is none.
    /// </summary>
    private static Sprite LoadWeaponHudIconSprite()
    {
        Object[] assets = AssetDatabase.LoadAllAssetsAtPath("Assets/Sprites/Weapons/StaticSlash.png");
        foreach (Object asset in assets)
        {
            if (asset is Sprite sprite)
            {
                return sprite;
            }
        }

        Assert.Fail("Expected at least one sprite in Assets/Sprites/Weapons/StaticSlash.png.");
        return null;
    }
}
