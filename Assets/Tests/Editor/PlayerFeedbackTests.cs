using System.Collections.Generic;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using UnityEngine.UI;
using VContainer;
using Is = UnityEngine.TestTools.Constraints.Is;

public class PlayerFeedbackTests
{
    private readonly List<Object> createdObjects = new();
    private ManualClock clock;
    private LocalPlayerTracker localPlayer;
    private AbilityBarView bar;
    private AbilitySlotView[] slots;
    private AbilityBarPresenter barPresenter;

    [SetUp]
    public void SetUp()
    {
        clock = new ManualClock();
        localPlayer = new LocalPlayerTracker();
    }

    [TearDown]
    public void TearDown()
    {
        barPresenter?.Dispose();
        barPresenter = null;
        foreach (Object created in createdObjects)
        {
            if (created != null)
            {
                Object.DestroyImmediate(created);
            }
        }

        createdObjects.Clear();
    }

    [Test]
    public void AbilityBar_ShowsTheLocalPlayersSlots_WithKeyLabelsAndAnEmptyState()
    {
        CreateBar();
        PlayerAbilities abilities = CreatePlayer(new[] { Ability("Bolt", 2f), Ability("Leap", 4f), Ability("Mend", 6f), null }, new[] { Ability("Whirl", 1f), null });

        localPlayer.Assign(new LocalPlayer(abilities.GetComponent<PlayerController>()));

        Assert.That(slots[0].NameText.text, Is.EqualTo("Bolt"), "An ability without an icon shows its name.");
        Assert.That(slots[4].NameText.text, Is.EqualTo("Whirl"));
        Assert.That(slots[3].IsEmpty, Is.True);
        Assert.That(slots[5].IsEmpty, Is.True);
        Assert.That(slots[0].IsEmpty, Is.False);
        Assert.That(slots[3].NameText.text, Is.Empty);
        Assert.That(slots[0].Icon.enabled, Is.False);
    }

    [Test]
    public void AbilityBar_ShowsTheCooldownFillAndSecondsUntilReady()
    {
        CreateBar();
        PlayerAbilities abilities = CreatePlayer(new[] { Ability("Bolt", 4f), null, null, null }, new AbilityDefinition[2]);
        localPlayer.Assign(new LocalPlayer(abilities.GetComponent<PlayerController>()));

        abilities.TryCast(0, default);
        abilities.EndCast(0);
        clock.Advance(1f);
        barPresenter.Tick();

        Assert.That(slots[0].CooldownFill.fillAmount, Is.EqualTo(0.75f).Within(0.001f));
        Assert.That(slots[0].CooldownText.text, Is.EqualTo("3"));
        Assert.That(slots[1].CooldownText.text, Is.Empty);

        clock.Advance(2.5f);
        barPresenter.Tick();
        Assert.That(slots[0].CooldownText.text, Is.EqualTo("1"));

        clock.Advance(1f);
        barPresenter.Tick();
        Assert.That(slots[0].CooldownFill.fillAmount, Is.Zero);
        Assert.That(slots[0].CooldownText.text, Is.Empty);
    }

    [Test]
    public void AbilityBar_FlashesAFailedSlot_AndFadesIt()
    {
        CreateBar();
        PlayerAbilities abilities = CreatePlayer(new[] { Ability("Bolt", 4f), null, null, null }, new AbilityDefinition[2]);
        localPlayer.Assign(new LocalPlayer(abilities.GetComponent<PlayerController>()));
        clock.Advance(10f);

        abilities.TryCast(3, default);
        Assert.That(slots[3].Flash.color.a, Is.GreaterThan(0.5f), "An empty slot flashes when pressed.");

        clock.Advance(AbilityBarPresenter.FlashSeconds / 2f);
        barPresenter.Tick();
        float halfway = slots[3].Flash.color.a;
        Assert.That(halfway, Is.GreaterThan(0f).And.LessThan(0.5f));

        clock.Advance(AbilityBarPresenter.FlashSeconds);
        barPresenter.Tick();
        Assert.That(slots[3].Flash.color.a, Is.Zero);
        Assert.That(slots[0].Flash.color.a, Is.Zero);
    }

    [Test]
    public void AbilityBar_FollowsTheLocalPlayerAndItsWeapon_AndClearsWithoutOne()
    {
        CreateBar();
        PlayerAbilities first = CreatePlayer(new[] { Ability("First", 1f), null, null, null }, new AbilityDefinition[2]);
        PlayerAbilities second = CreatePlayer(new[] { Ability("Second", 1f), null, null, null }, new AbilityDefinition[2]);
        localPlayer.Assign(new LocalPlayer(first.GetComponent<PlayerController>()));

        localPlayer.Assign(new LocalPlayer(second.GetComponent<PlayerController>()));
        Assert.That(slots[0].NameText.text, Is.EqualTo("Second"));

        second.GetComponent<PlayerWeaponController>().Equip(Track(PlayerWeapon.Create("Club", 1, 0.5f, 0.5f, new Vector2[4], weaponAbilities: new[] { null, Ability("Smash", 1f) })));
        Assert.That(slots[5].NameText.text, Is.EqualTo("Smash"));

        first.TryCast(5, default);
        Assert.That(slots[5].Flash.color.a, Is.Zero, "The previous local player no longer drives the bar.");

        localPlayer.Clear(localPlayer.Current.Handle);
        Assert.That(slots[0].IsEmpty, Is.True);
    }

    [Test]
    public void HudPrefab_HasAnAbilityBarOfSixWiredSlotsThatBlockClicks()
    {
        GameObject hud = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/PlayerHudCanvas.prefab");
        AbilityBarView prefabBar = hud.GetComponentInChildren<AbilityBarView>(true);

        Assert.That(prefabBar, Is.Not.Null);
        Assert.That(prefabBar.SlotCount, Is.EqualTo(AbilitySlots.Count));
        AbilitySlotView[] prefabSlots = prefabBar.GetComponentsInChildren<AbilitySlotView>(true);
        Assert.That(prefabSlots.Length, Is.EqualTo(AbilitySlots.Count));
        foreach (AbilitySlotView slot in prefabSlots)
        {
            Assert.That(slot.Icon, Is.Not.Null);
            Assert.That(slot.NameText, Is.Not.Null);
            Assert.That(slot.CooldownFill.type, Is.EqualTo(Image.Type.Filled));
            Assert.That(slot.CooldownText, Is.Not.Null);
            Assert.That(slot.Flash.raycastTarget, Is.False);
            Assert.That(slot.GetComponent<Image>().raycastTarget, Is.True, "Clicks on the bar must not reach the world.");
        }
    }

    [Test]
    public void Feedback_HighlightsTheEnemyUnderThePointer_WithTheAttackCursor()
    {
        (PointerFeedbackPresenter presenter, PointerFeedbackLayer layer, FakePlayerInput input) = CreateFeedback(alive: true);
        Health mob = CreateMob(new Vector2(10f, 20f));
        Physics2D.SyncTransforms();

        presenter.Tick(0.016f);
        Assert.That(layer.Hovered, Is.SameAs(mob.transform));
        Assert.That(layer.HoverRing.enabled, Is.True);
        Assert.That((Vector2)layer.HoverRing.transform.position, Is.EqualTo(new Vector2(10f, 20f)));
        Assert.That(layer.AttackCursorShown, Is.True);

        input.PointerScreenPosition = new Vector2(10f, 10f);
        presenter.Tick(0.016f);
        Assert.That(layer.Hovered, Is.Null);
        Assert.That(layer.HoverRing.enabled, Is.False);
        Assert.That(layer.AttackCursorShown, Is.False);

        input.PointerScreenPosition = new Vector2(100f, 50f);
        mob.ApplyDamage(mob.MaxHealth);
        presenter.Tick(0.016f);
        Assert.That(layer.Hovered, Is.Null, "Dead enemies are not highlighted.");
    }

    [Test]
    public void Feedback_DropsAMoveMarkerOnGroundClicks_ButNotOnEnemiesOrOverUI()
    {
        (PointerFeedbackPresenter presenter, PointerFeedbackLayer layer, FakePlayerInput input) = CreateFeedback(alive: true);
        input.PointerScreenPosition = new Vector2(150f, 50f);
        input.MovePressedThisFrame = true;

        presenter.Tick(0.016f);
        Assert.That(layer.ActiveMarkerCount, Is.EqualTo(1));

        input.IsPointerOverUI = true;
        presenter.Tick(0.016f);
        Assert.That(layer.ActiveMarkerCount, Is.EqualTo(1), "Clicks over UI never reach gameplay.");

        input.IsPointerOverUI = false;
        input.PointerScreenPosition = new Vector2(100f, 50f);
        CreateMob(new Vector2(10f, 20f));
        Physics2D.SyncTransforms();
        presenter.Tick(0.016f);
        Assert.That(layer.ActiveMarkerCount, Is.EqualTo(1), "A click on an enemy is an attack, not a move.");
    }

    [Test]
    public void Feedback_ShowsNothingForADeadPlayer()
    {
        (PointerFeedbackPresenter presenter, PointerFeedbackLayer layer, FakePlayerInput input) = CreateFeedback(alive: false);
        CreateMob(new Vector2(10f, 20f));
        Physics2D.SyncTransforms();
        input.MovePressedThisFrame = true;

        presenter.Tick(0.016f);

        Assert.That(layer.Hovered, Is.Null);
        Assert.That(layer.ActiveMarkerCount, Is.Zero);
    }

    [Test]
    public void MoveMarkers_ReturnToThePoolWhenFinished_AndAreReused()
    {
        (PointerFeedbackPresenter presenter, PointerFeedbackLayer layer, FakePlayerInput input) = CreateFeedback(alive: true);
        input.PointerScreenPosition = new Vector2(150f, 50f);
        input.MovePressedThisFrame = true;
        presenter.Tick(0.016f);
        input.MovePressedThisFrame = false;
        int pooled = layer.PooledMarkerCount;

        presenter.Tick(10f);

        Assert.That(layer.ActiveMarkerCount, Is.Zero);
        Assert.That(layer.PooledMarkerCount, Is.EqualTo(pooled + 1));

        input.MovePressedThisFrame = true;
        presenter.Tick(0.016f);
        Assert.That(layer.PooledMarkerCount, Is.EqualTo(pooled), "The next marker comes from the pool.");
    }

    [Test]
    public void Feedback_DoesNotAllocateWhileHovering()
    {
        var (presenter, layer, _) = CreateFeedback(alive: true);
        CreateMob(new Vector2(10f, 20f));
        Physics2D.SyncTransforms();
        presenter.Tick(0.016f);

        TestDelegate tick = () => presenter.Tick(0.016f);

        Assert.That(tick, Is.Not.AllocatingGCMemory());
        Assert.That(layer.AttackCursorShown, Is.True);
    }

    private void CreateBar()
    {
        GameObject barObject = Track(new GameObject("AbilityBar"));
        slots = new AbilitySlotView[AbilitySlots.Count];
        for (int i = 0; i < slots.Length; i++)
        {
            GameObject slotObject = new($"Slot{i + 1}");
            slotObject.transform.SetParent(barObject.transform);
            Image frame = slotObject.AddComponent<Image>();
            slots[i] = slotObject.AddComponent<AbilitySlotView>();
            slots[i].ConfigureReferences(
                frame,
                Child<Image>(slotObject, "Icon"),
                Child<TextMeshProUGUI>(slotObject, "Name"),
                Child<Image>(slotObject, "CooldownFill"),
                Child<TextMeshProUGUI>(slotObject, "CooldownText"),
                Child<TextMeshProUGUI>(slotObject, "Key"),
                Child<Image>(slotObject, "Flash"));
        }

        bar = barObject.AddComponent<AbilityBarView>();
        bar.ConfigureReferences(slots);
        barPresenter = new AbilityBarPresenter(localPlayer, bar, clock);
        barPresenter.Start();
    }

    private static T Child<T>(GameObject parent, string name) where T : Component
    {
        GameObject child = new(name);
        child.transform.SetParent(parent.transform);
        return child.AddComponent<T>();
    }

    private PlayerAbilities CreatePlayer(AbilityDefinition[] classAbilities, AbilityDefinition[] weaponAbilities)
    {
        GameObject player = Track(new GameObject("Player"));
        player.AddComponent<Health>();
        player.AddComponent<PlayerController>();
        PlayerWeaponController weapon = player.AddComponent<PlayerWeaponController>();
        AbilityService service = new(FixedGameAuthority.Authoritative);
        weapon.Construct(new SlashSpawner(new ContainerBuilder().Build(), TestCombat.CreateDamageService(FixedGameAuthority.Authoritative)), clock);
        weapon.Equip(Track(PlayerWeapon.Create("Sword", 1, 0.35f, 0.55f, new Vector2[4], weaponAbilities: weaponAbilities)));
        PlayerAbilities abilities = player.AddComponent<PlayerAbilities>();
        abilities.Construct(clock, service);
        abilities.Configure(Track(PlayerClass.Create("Test", classAbilities)));
        return abilities;
    }

    private (PointerFeedbackPresenter, PointerFeedbackLayer, FakePlayerInput) CreateFeedback(bool alive)
    {
        GameObject markerTemplate = Track(new GameObject("MarkerTemplate"));
        markerTemplate.SetActive(false);
        markerTemplate.AddComponent<SpriteRenderer>();
        MoveMarker markerPrefab = markerTemplate.AddComponent<MoveMarker>();
        GameObject layerObject = Track(new GameObject("PointerFeedback"));
        SpriteRenderer ring = new GameObject("HoverRing").AddComponent<SpriteRenderer>();
        ring.transform.SetParent(layerObject.transform);
        PointerFeedbackLayer layer = layerObject.AddComponent<PointerFeedbackLayer>();
        layer.Configure(markerPrefab, ring, AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Sprites/UI/AttackCursor.png"));

        GameObject player = Track(new GameObject("Player"));
        player.AddComponent<PlayerController>();
        Health health = player.AddComponent<Health>();
        if (!alive)
        {
            health.ApplyDamage(health.MaxHealth);
        }

        localPlayer.Assign(new LocalPlayer(player.GetComponent<PlayerController>()));
        PlayerControlSettings settings = Track(TestPlayerControlSettings.Create());
        PointerTargetPicker picker = new(settings, new PlayerRegistry());
        FakePlayerInput input = new() { PointerScreenPosition = new Vector2(100f, 50f) };
        return (new PointerFeedbackPresenter(input, CreateCamera(new Vector2(10f, 20f)), picker, localPlayer, layer), layer, input);
    }

    private Camera CreateCamera(Vector2 position)
    {
        GameObject cameraObject = Track(new GameObject("Camera"));
        cameraObject.transform.position = new Vector3(position.x, position.y, -10f);
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 5f;
        camera.targetTexture = Track(new RenderTexture(200, 100, 0));
        return camera;
    }

    private Health CreateMob(Vector2 position)
    {
        GameObject mob = Track(new GameObject("Mob"));
        mob.layer = LayerMask.NameToLayer("Enemy");
        mob.transform.position = position;
        mob.AddComponent<BoxCollider2D>().size = new Vector2(0.3f, 0.2f);
        Health health = mob.AddComponent<Health>();
        mob.AddComponent<DamageReceiver>();
        return health;
    }

    private AbilityDefinition Ability(string name, float cooldown)
    {
        return Track(AbilityDefinition.Create(name, cooldown));
    }

    private T Track<T>(T created) where T : Object
    {
        createdObjects.Add(created);
        return created;
    }
}
