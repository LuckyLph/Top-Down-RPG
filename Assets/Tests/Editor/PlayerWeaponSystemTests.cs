using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using VContainer;

public class PlayerWeaponSystemTests
{
    private GameObject root;
    private PlayerHudPresenter hudPresenter;

    [TearDown]
    public void TearDown()
    {
        hudPresenter?.Dispose();
        hudPresenter = null;

        foreach (SwordSlashAttack slashAttack in Object.FindObjectsByType<SwordSlashAttack>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            Object.DestroyImmediate(slashAttack.gameObject);
        }

        foreach (Canvas canvas in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
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
    }

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

        hudPresenter.Dispose();
        hudPresenter = null;
        string textAfterDispose = hud.HealthText.text;
        health.ApplyDamage(1);
        Assert.That(hud.HealthText.text, Is.EqualTo(textAfterDispose), "A disposed presenter should stop listening.");
    }

    [Test]
    public void TryAttack_UsesFacingDirectionAndRespectsCooldown()
    {
        root = new GameObject("PlayerRoot");
        root.transform.position = Vector3.zero;

        root.AddComponent<BoxCollider2D>();
        root.AddComponent<Rigidbody2D>();
        PlayerController playerController = root.AddComponent<PlayerController>();
        PlayerWeaponController weaponController = root.AddComponent<PlayerWeaponController>();
        ManualClock clock = new();
        weaponController.Construct(CreateSpawner(), clock);

        weaponController.Equip(CreateTestWeapon("Sword", damage: 2, cooldown: 0.5f, spawnDistance: 0.75f));
        playerController.Face(Vector2.right);

        bool firstAttack = weaponController.TryAttack();
        bool secondAttack = weaponController.TryAttack();
        SwordSlashAttack slashAttack = Object.FindAnyObjectByType<SwordSlashAttack>();
        clock.Advance(0.49f);
        bool attackBeforeCooldown = weaponController.TryAttack();
        clock.Advance(0.01f);
        bool attackAfterCooldown = weaponController.TryAttack();

        Assert.That(firstAttack, Is.True);
        Assert.That(secondAttack, Is.False);
        Assert.That(attackBeforeCooldown, Is.False);
        Assert.That(attackAfterCooldown, Is.True);
        Assert.That(slashAttack, Is.Not.Null);
        Assert.That(slashAttack.Direction.x, Is.GreaterThan(0.9f));
        Assert.That(slashAttack.transform.position.x, Is.GreaterThan(root.transform.position.x));
    }

    [Test]
    public void PlayerController_FacesAndAttacksFromItsCommandSource()
    {
        root = new GameObject("PlayerRoot");
        root.AddComponent<BoxCollider2D>();
        root.AddComponent<Rigidbody2D>();
        PlayerController playerController = root.AddComponent<PlayerController>();
        PlayerWeaponController weaponController = root.AddComponent<PlayerWeaponController>();
        weaponController.Construct(CreateSpawner(), new ManualClock());
        weaponController.Equip(CreateTestWeapon("Sword"));
        SerializedObject serializedController = new(playerController);
        serializedController.FindProperty("weaponController").objectReferenceValue = weaponController;
        serializedController.ApplyModifiedPropertiesWithoutUndo();
        FakeCommandSource commands = new();
        playerController.SetCommandSource(commands);

        commands.Next = new PlayerCommand(new Vector2(-3f, 0f), attack: false);
        playerController.Tick();
        Assert.That(playerController.FacingDirection, Is.EqualTo(Vector2.left));
        Assert.That(Object.FindAnyObjectByType<SwordSlashAttack>(), Is.Null);

        commands.Next = new PlayerCommand(Vector2.zero, attack: true);
        playerController.Tick();
        SwordSlashAttack slashAttack = Object.FindAnyObjectByType<SwordSlashAttack>();
        Assert.That(playerController.FacingDirection, Is.EqualTo(Vector2.left), "Standing still keeps the last facing.");
        Assert.That(slashAttack, Is.Not.Null);
        Assert.That(slashAttack.Direction.x, Is.LessThan(-0.9f));
    }

    [Test]
    public void TryAttack_RaisesAttacked_ButRemoteAttacksDoNot()
    {
        root = new GameObject("PlayerRoot");
        root.AddComponent<BoxCollider2D>();
        root.AddComponent<Rigidbody2D>();
        PlayerController playerController = root.AddComponent<PlayerController>();
        PlayerWeaponController weaponController = root.AddComponent<PlayerWeaponController>();
        weaponController.Construct(CreateSpawner(), new ManualClock());
        weaponController.Equip(CreateTestWeapon("Sword"));
        playerController.Face(Vector2.up);
        Vector2? attacked = null;
        weaponController.Attacked += direction => attacked = direction;

        weaponController.PlayRemoteAttack(Vector2.left);
        Assert.That(attacked, Is.Null, "Replaying another player's attack must not be sent back out.");
        Assert.That(Object.FindAnyObjectByType<SwordSlashAttack>().Direction.x, Is.LessThan(-0.9f));

        Assert.That(weaponController.TryAttack(), Is.True);
        Assert.That(attacked, Is.EqualTo(Vector2.up));
    }

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

    [Test]
    public void PlayerController_WithoutCommandSource_StaysIdle()
    {
        root = new GameObject("PlayerRoot");
        root.AddComponent<Rigidbody2D>();
        PlayerController playerController = root.AddComponent<PlayerController>();

        playerController.Tick();

        Assert.That(playerController.FacingDirection, Is.EqualTo(Vector2.down));
    }

    [Test]
    public void LocalPlayerCommandSource_ForwardsMoveAndAttackFromInput()
    {
        FakePlayerInput input = new() { Move = new Vector2(0.5f, 1f), AttackPressedThisFrame = true };
        LocalPlayerCommandSource source = new(input);

        PlayerCommand command = source.ReadCommand();

        Assert.That(command.Move, Is.EqualTo(new Vector2(0.5f, 1f)));
        Assert.That(command.Attack, Is.True);
    }

    [Test]
    public void PlayerSlashAttack_DamagesTargetOnlyOnceAndIgnoresOwner()
    {
        root = new GameObject("CombatRoot");

        GameObject owner = CreateDamageable("Owner", Vector2.zero, root.transform);
        GameObject target = CreateDamageable("Target", Vector2.right * 0.55f, root.transform);

        Health ownerHealth = owner.GetComponent<Health>();
        Health targetHealth = target.GetComponent<Health>();
        BoxCollider2D ownerCollider = owner.GetComponent<BoxCollider2D>();
        BoxCollider2D targetCollider = target.GetComponent<BoxCollider2D>();

        PlayerWeapon weapon = CreateTestWeapon("Sword", damage: 3, hitboxSize: new Vector2(1.2f, 1f));
        SwordSlashAttack slashAttack = CreateSpawner().Spawn(owner.transform, weapon, Vector2.right);

        Assert.That(targetHealth.CurrentHealth, Is.EqualTo(targetHealth.MaxHealth - 3));
        Assert.That(ownerHealth.CurrentHealth, Is.EqualTo(ownerHealth.MaxHealth));
        Assert.That(slashAttack.TryDamageCollider(targetCollider), Is.False);
        Assert.That(slashAttack.TryDamageCollider(ownerCollider), Is.False);
        Assert.That(targetHealth.CurrentHealth, Is.EqualTo(targetHealth.MaxHealth - 3));
    }

    [Test]
    public void PlayerSlashAttack_HittingWallDoesNotDamageUnrelatedReceiverUnderSameRoot()
    {
        root = new GameObject("LevelRoot");

        // The enemy is the first receiver under the shared root, which is what a root-wide search would find.
        GameObject distantEnemy = CreateDamageable("DistantEnemy", Vector2.right * 20f, root.transform);
        GameObject owner = CreateDamageable("Owner", Vector2.zero, root.transform);
        GameObject wall = new("Wall");
        wall.transform.SetParent(root.transform);
        wall.transform.position = Vector2.left * 20f;
        BoxCollider2D wallCollider = wall.AddComponent<BoxCollider2D>();

        Health enemyHealth = distantEnemy.GetComponent<Health>();
        Health ownerHealth = owner.GetComponent<Health>();
        PlayerWeapon weapon = CreateTestWeapon("Sword", damage: 2);
        SwordSlashAttack slashAttack = CreateSpawner().Spawn(owner.transform, weapon, Vector2.right);

        Assert.That(slashAttack.TryDamageCollider(wallCollider), Is.False);
        Assert.That(enemyHealth.CurrentHealth, Is.EqualTo(enemyHealth.MaxHealth));
        Assert.That(ownerHealth.CurrentHealth, Is.EqualTo(ownerHealth.MaxHealth));
    }

    [Test]
    public void PlayerSlashAttack_CanDamageMultipleTargetsOnceEach()
    {
        root = new GameObject("CombatRoot");

        GameObject owner = CreateDamageable("Owner", Vector2.zero, root.transform);
        GameObject firstTarget = CreateDamageable("FirstTarget", new Vector2(0.45f, 0.18f), root.transform);
        GameObject secondTarget = CreateDamageable("SecondTarget", new Vector2(0.45f, -0.18f), root.transform);

        Health firstHealth = firstTarget.GetComponent<Health>();
        Health secondHealth = secondTarget.GetComponent<Health>();

        PlayerWeapon weapon = CreateTestWeapon("Sword", damage: 2, hitboxSize: new Vector2(1.2f, 1.2f));
        CreateSpawner().Spawn(owner.transform, weapon, Vector2.right);

        Assert.That(firstHealth.CurrentHealth, Is.EqualTo(firstHealth.MaxHealth - 2));
        Assert.That(secondHealth.CurrentHealth, Is.EqualTo(secondHealth.MaxHealth - 2));
    }

    [Test]
    public void PlayerSlashAttack_FollowsOwnerVisualAnchorForLifetime()
    {
        root = new GameObject("CombatRoot");

        GameObject owner = new("Owner");
        owner.transform.SetParent(root.transform);
        owner.transform.position = Vector3.zero;
        owner.AddComponent<BoxCollider2D>();
        owner.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;

        GameObject visuals = new("Visuals");
        visuals.transform.SetParent(owner.transform);
        visuals.transform.localPosition = new Vector3(0f, 0.5f, 0f);
        SpriteRenderer spriteRenderer = visuals.AddComponent<SpriteRenderer>();

        PlayerWeapon weapon = CreateTestWeapon("Sword", spawnDistance: 0.55f);
        SwordSlashAttack slashAttack = CreateSpawner().Spawn(owner.transform, weapon, Vector2.right, spriteRenderer);

        Vector3 initialOffset = slashAttack.transform.position - visuals.transform.position;
        owner.transform.position += new Vector3(1.25f, 0.4f, 0f);

        slashAttack.Tick(0f);

        Vector3 followedOffset = slashAttack.transform.position - visuals.transform.position;
        Assert.That(Vector3.Distance(followedOffset, initialOffset), Is.LessThan(0.001f));
    }

    [Test]
    public void PlayerSlashAttack_UsesDirectionalSpawnOffsets()
    {
        root = new GameObject("CombatRoot");

        GameObject owner = new("Owner");
        owner.transform.SetParent(root.transform);
        owner.AddComponent<BoxCollider2D>();
        owner.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;

        const float spawnDistance = 1f;
        Vector2[] spawnOffsets =
        {
            Vector2.up * 0.1f,
            Vector2.up * 0.3f,
            new Vector2(0.2f, 0.15f),
            new Vector2(0.2f, 0.15f)
        };
        PlayerWeapon weapon = CreateTestWeapon("Sword", spawnDistance: spawnDistance, spawnOffsets: spawnOffsets);

        AssertSpawnPosition(owner.transform, weapon, Vector2.down, new Vector3(0f, -0.9f, 0f));
        AssertSpawnPosition(owner.transform, weapon, Vector2.up, new Vector3(0f, 1.3f, 0f));
        AssertSpawnPosition(owner.transform, weapon, Vector2.left, new Vector3(-0.8f, 0.15f, 0f));
        AssertSpawnPosition(owner.transform, weapon, Vector2.right, new Vector3(1.2f, 0.15f, 0f));
    }

    [Test]
    public void PlayerSlashAttack_MirrorsRootWhenFacingEast()
    {
        root = new GameObject("CombatRoot");

        GameObject owner = new("Owner");
        owner.transform.SetParent(root.transform);
        owner.AddComponent<BoxCollider2D>();
        owner.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;

        PlayerWeapon weapon = CreateTestWeapon("Sword");
        SwordSlashAttack eastSlash = CreateSpawner().Spawn(owner.transform, weapon, Vector2.right);
        SwordSlashAttack westSlash = CreateSpawner().Spawn(owner.transform, weapon, Vector2.left);

        Assert.That(eastSlash.transform.localScale.x, Is.LessThan(0f));
        Assert.That(westSlash.transform.localScale.x, Is.GreaterThan(0f));
        Assert.That(eastSlash.Hitbox.transform, Is.SameAs(eastSlash.transform));
    }

    [Test]
    public void PlayerSlashAttack_AppliesOpeningSpriteFromAnimationClip()
    {
        root = new GameObject("CombatRoot");

        GameObject owner = new("Owner");
        owner.transform.SetParent(root.transform);
        owner.AddComponent<BoxCollider2D>();
        owner.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;

        PlayerWeapon weapon = CreateTestWeapon("Sword");
        SwordSlashAttack slashAttack = CreateSpawner().Spawn(owner.transform, weapon, Vector2.right);

        Assert.That(slashAttack.SpriteRenderer, Is.Not.Null);
        Assert.That(slashAttack.SpriteRenderer.sprite, Is.Not.Null);
    }

    [Test]
    public void InputActionAsset_AttackActionIncludesSpaceBinding()
    {
        InputActionAsset actionsAsset = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/InputSystem_Actions.inputactions");
        Assert.That(actionsAsset, Is.Not.Null);

        InputAction attackAction = actionsAsset.FindAction("Player/Attack", true);
        Assert.That(attackAction.bindings.Any(binding => binding.path == "<Keyboard>/space"), Is.True);
    }

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

    private sealed class FakeCommandSource : IPlayerCommandSource
    {
        public PlayerCommand Next { get; set; }

        public PlayerCommand ReadCommand()
        {
            return Next;
        }
    }

    private sealed class FakePlayerInput : IPlayerInput
    {
        public Vector2 Move { get; set; }
        public bool AttackPressedThisFrame { get; set; }
        public bool GameplayEnabled { get; set; } = true;
    }

    private static SlashSpawner CreateSpawner()
    {
        return new SlashSpawner(new ContainerBuilder().Build(), new DamageService(new CombatEvents(), FixedGameAuthority.Authoritative));
    }

    private static PlayerWeapon CreateTestWeapon(
        string name,
        int damage = 1,
        float cooldown = 0.35f,
        float spawnDistance = 0.55f,
        Vector2? hitboxSize = null,
        Vector2[] spawnOffsets = null,
        Sprite icon = null)
    {
        AnimationClip slashAnimation = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Animations/Weapons/Slash.anim");
        Assert.That(slashAnimation, Is.Not.Null);

        return PlayerWeapon.Create(
            name,
            damage,
            cooldown,
            spawnDistance,
            spawnOffsets ?? new Vector2[4],
            CreateTestSlashPrefab(hitboxSize ?? new Vector2(0.9f, 0.9f), slashAnimation),
            icon);
    }

    private static void AssertSpawnPosition(Transform owner, PlayerWeapon weapon, Vector2 direction, Vector3 expectedPosition)
    {
        SwordSlashAttack slashAttack = CreateSpawner().Spawn(owner, weapon, direction);
        Assert.That(Vector3.Distance(slashAttack.transform.position, expectedPosition), Is.LessThan(0.001f));
        Object.DestroyImmediate(slashAttack.gameObject);
    }

    private static GameObject CreateTestSlashPrefab(Vector2 hitboxSize, AnimationClip slashAnimation)
    {
        GameObject root = new("SlashPrefab");
        root.SetActive(false);
        Animator animator = root.AddComponent<Animator>();

        BoxCollider2D hitbox = root.AddComponent<BoxCollider2D>();
        hitbox.isTrigger = true;
        hitbox.offset = new Vector2(0f, 0.1f);
        hitbox.size = hitboxSize;

        SwordSlashAttack slashAttack = root.AddComponent<SwordSlashAttack>();

        GameObject visual = new("Visual");
        visual.transform.SetParent(root.transform, false);
        visual.transform.localScale = Vector3.one * 0.14f;
        SpriteRenderer renderer = visual.AddComponent<SpriteRenderer>();

        slashAttack.ConfigureReferences(visual.transform, renderer, hitbox, animator, slashAnimation);
        return root;
    }

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

    private readonly struct HudElements
    {
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
