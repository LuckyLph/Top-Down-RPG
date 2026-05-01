using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class PlayerWeaponSystemTests
{
    private GameObject root;

    [TearDown]
    public void TearDown()
    {
        foreach (SwordSlashAttack slashAttack in Object.FindObjectsByType<SwordSlashAttack>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            Object.DestroyImmediate(slashAttack.gameObject);
        }

        foreach (Canvas canvas in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (canvas != null && canvas.name == "WeaponHudCanvas")
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
    public void Equip_UpdatesEquippedWeaponLabel()
    {
        root = new GameObject("PlayerRoot");
        root.AddComponent<Rigidbody2D>();
        root.AddComponent<PlayerController>();
        PlayerWeaponController weaponController = root.AddComponent<PlayerWeaponController>();

        TextMeshProUGUI label = CreateWeaponHudLabel();

        weaponController.Equip(CreateTestWeapon("Sword"));

        Assert.That(weaponController.CurrentWeaponName, Is.EqualTo("Sword"));
        Assert.That(label.text, Is.EqualTo("Sword"));
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

        weaponController.Equip(CreateTestWeapon("Sword", damage: 2, cooldown: 0.5f, spawnDistance: 0.75f));
        SetPrivateField(playerController, "lastMoveDirection", Vector2.right);

        bool firstAttack = weaponController.TryAttack();
        bool secondAttack = weaponController.TryAttack();
        SwordSlashAttack slashAttack = Object.FindAnyObjectByType<SwordSlashAttack>();

        Assert.That(firstAttack, Is.True);
        Assert.That(secondAttack, Is.False);
        Assert.That(slashAttack, Is.Not.Null);
        Assert.That(slashAttack.Direction.x, Is.GreaterThan(0.9f));
        Assert.That(slashAttack.transform.position.x, Is.GreaterThan(root.transform.position.x));
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
        SwordSlashAttack slashAttack = SwordSlashAttack.Spawn(owner.transform, weapon, Vector2.right);

        Assert.That(targetHealth.CurrentHealth, Is.EqualTo(targetHealth.MaxHealth - 3));
        Assert.That(ownerHealth.CurrentHealth, Is.EqualTo(ownerHealth.MaxHealth));
        Assert.That(slashAttack.TryDamageCollider(targetCollider), Is.False);
        Assert.That(slashAttack.TryDamageCollider(ownerCollider), Is.False);
        Assert.That(targetHealth.CurrentHealth, Is.EqualTo(targetHealth.MaxHealth - 3));
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
        SwordSlashAttack.Spawn(owner.transform, weapon, Vector2.right);

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
        SwordSlashAttack slashAttack = SwordSlashAttack.Spawn(owner.transform, weapon, Vector2.right, spriteRenderer);

        Vector3 initialOffset = slashAttack.transform.position - visuals.transform.position;
        owner.transform.position += new Vector3(1.25f, 0.4f, 0f);

        InvokePrivateMethod(slashAttack, "Update");

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
        SwordSlashAttack eastSlash = SwordSlashAttack.Spawn(owner.transform, weapon, Vector2.right);
        SwordSlashAttack westSlash = SwordSlashAttack.Spawn(owner.transform, weapon, Vector2.left);

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
        SwordSlashAttack slashAttack = SwordSlashAttack.Spawn(owner.transform, weapon, Vector2.right);

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

    private static PlayerWeapon CreateTestWeapon(
        string name,
        int damage = 1,
        float cooldown = 0.35f,
        float spawnDistance = 0.55f,
        Vector2? hitboxSize = null,
        Vector2[] spawnOffsets = null)
    {
        AnimationClip slashAnimation = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Animations/Weapons/Slash.anim");
        Assert.That(slashAnimation, Is.Not.Null);

        return PlayerWeapon.Create(
            name,
            damage,
            cooldown,
            spawnDistance,
            spawnOffsets ?? new Vector2[4],
            CreateTestSlashPrefab(hitboxSize ?? new Vector2(0.9f, 0.9f), slashAnimation));
    }

    private static void AssertSpawnPosition(Transform owner, PlayerWeapon weapon, Vector2 direction, Vector3 expectedPosition)
    {
        SwordSlashAttack slashAttack = SwordSlashAttack.Spawn(owner, weapon, direction);
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
        target.AddComponent<Health>().ApplyDamage(0);
        target.AddComponent<DamageReceiver>();
        return target;
    }

    private static TextMeshProUGUI CreateWeaponHudLabel()
    {
        GameObject canvasObject = new("WeaponHudCanvas");
        canvasObject.AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        canvasObject.AddComponent<CanvasScaler>();

        GameObject labelObject = new("WeaponHudLabel");
        labelObject.transform.SetParent(canvasObject.transform, false);

        RectTransform rectTransform = labelObject.AddComponent<RectTransform>();
        rectTransform.sizeDelta = new Vector2(240f, 48f);
        labelObject.AddComponent<CanvasRenderer>();

        TextMeshProUGUI label = labelObject.AddComponent<TextMeshProUGUI>();
        labelObject.AddComponent<PlayerWeaponHud>();
        label.fontSize = 28f;
        label.alignment = TextAlignmentOptions.Center;
        return label;
    }

    private static void SetPrivateField<T>(Object target, string fieldName, T value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, $"Expected field '{fieldName}' to exist on {target.GetType().Name}.");
        field.SetValue(target, value);
    }

    private static void InvokePrivateMethod(Object target, string methodName)
    {
        MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null, $"Expected method '{methodName}' to exist on {target.GetType().Name}.");
        method.Invoke(target, null);
    }
}
