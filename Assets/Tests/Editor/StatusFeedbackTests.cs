using System.Collections.Generic;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class StatusFeedbackTests
{
    private readonly List<Object> created = new();

    [TearDown]
    public void TearDown()
    {
        foreach (Object instance in created)
        {
            if (instance != null)
            {
                Object.DestroyImmediate(instance is Component component ? component.gameObject : instance);
            }
        }

        created.Clear();
    }

    [Test]
    public void Set_CountDown_RunsTimersDownWithoutTickingOrExpiring()
    {
        StatusEffectDefinition burn = Definition("Burn", StatusKind.Debuff, 3f);
        StatusEffectSet set = new();
        set.SyncFrom(new[] { new StatusSnapshot(burn, 1, 1, 2f) });
        int changes = 0;
        set.Changed += () => changes++;

        set.CountDown(0.5f);
        Assert.That(set.GetRemaining(burn), Is.EqualTo(1.5f).Within(0.0001f));

        set.CountDown(5f);
        Assert.That(set.GetRemaining(burn), Is.Zero, "Never below zero.");
        Assert.That(set.Count, Is.EqualTo(1), "Only the host removes a status.");
        Assert.That(changes, Is.Zero);
    }

    [Test]
    public void Set_GetRemaining_IsTheLongestInstance()
    {
        StatusEffectDefinition poison = Definition("Poison", StatusKind.Debuff, 6f, StatusStacking.Independent, 3);
        StatusEffectSet set = new();
        set.Apply(poison, null, 1f);
        set.Tick(2f, null);
        set.Apply(poison, null, 1f);
        set.Tick(1f, null);

        Assert.That(set.GetRemaining(poison), Is.EqualTo(5f).Within(0.0001f));
        Assert.That(set.GetRemaining(Definition("Other", StatusKind.Debuff, 1f)), Is.Zero);
    }

    [Test]
    public void Definition_Abbreviation_IsUpToTwoLettersOfTheName()
    {
        Assert.That(Definition("Burn", StatusKind.Debuff, 1f).Abbreviation, Is.EqualTo("Bu"));
        Assert.That(Definition("X", StatusKind.Debuff, 1f).Abbreviation, Is.EqualTo("X"));
    }

    [Test]
    public void StatusIcon_ShowsTheIconOrTheAbbreviation_InTheStatusColour_WithStacksAboveOne()
    {
        StatusIconView view = CreateIconView();
        Sprite sprite = Track(Sprite.Create(Track(new Texture2D(4, 4)), new Rect(0f, 0f, 4f, 4f), Vector2.one * 0.5f));
        StatusEffectDefinition burn = Definition("Burn", StatusKind.Debuff, 4f, color: new Color(1f, 0.5f, 0f));

        view.Show(burn, 1);
        Assert.That(view.Label.text, Is.EqualTo("Bu"));
        Assert.That(view.Icon.enabled, Is.False);
        Assert.That(view.StacksLabel.text, Is.Empty);
        Assert.That(view.Frame.color.r, Is.EqualTo(1f));
        Assert.That(view.Frame.color.g, Is.EqualTo(0.5f));

        view.Show(Definition("Iconic", StatusKind.Buff, 4f, icon: sprite), 3);
        Assert.That(view.Icon.enabled, Is.True);
        Assert.That(view.Icon.sprite, Is.SameAs(sprite));
        Assert.That(view.Label.text, Is.Empty);
        Assert.That(view.StacksLabel.text, Is.EqualTo("3"));

        view.SetElapsed(0.25f);
        Assert.That(view.ElapsedFill.fillAmount, Is.EqualTo(0.25f));
        view.Hide();
        Assert.That(view.gameObject.activeSelf, Is.False);
    }

    [Test]
    public void StatusBar_FollowsTheLocalPlayer_BuffsFirst_OneIconPerStatus()
    {
        StatusBarView bar = CreateBar(4);
        LocalPlayerTracker localPlayer = new();
        StatusBarPresenter presenter = new(localPlayer, bar);
        presenter.Start();
        StatusEffects statuses = CreatePlayer("Player");
        StatusEffectDefinition burn = Definition("Burn", StatusKind.Debuff, 4f, StatusStacking.AddStack, 5);
        StatusEffectDefinition poison = Definition("Poison", StatusKind.Debuff, 6f, StatusStacking.Independent, 3);
        StatusEffectDefinition fortify = Definition("Fortify", StatusKind.Buff, 8f);

        statuses.Set.Apply(burn, null, 1f);
        statuses.Set.Apply(burn, null, 1f);
        localPlayer.Assign(new LocalPlayer(statuses.GetComponent<PlayerController>()));
        Assert.That(ActiveIcons(bar), Is.EqualTo(1), "Binding shows what is already active.");

        statuses.Set.Apply(poison, null, 1f);
        statuses.Set.Apply(poison, null, 1f);
        statuses.Set.Apply(fortify, null, 1f);

        Assert.That(ActiveIcons(bar), Is.EqualTo(3), "Independent doses share one icon.");
        Assert.That(bar.Icon(0).Shown, Is.SameAs(fortify), "Buffs come first.");
        Assert.That(bar.Icon(1).Shown, Is.SameAs(burn));
        Assert.That(bar.Icon(1).StacksLabel.text, Is.EqualTo("2"));
        Assert.That(bar.Icon(2).Shown, Is.SameAs(poison));
        Assert.That(bar.Icon(2).StacksLabel.text, Is.EqualTo("2"));

        statuses.Set.Tick(2f, null);
        presenter.Tick();
        Assert.That(bar.Icon(1).ElapsedFill.fillAmount, Is.EqualTo(0.5f).Within(0.01f), "Burn has 2 of its 4 seconds left.");
        Assert.That(bar.Icon(0).ElapsedFill.fillAmount, Is.EqualTo(0.25f).Within(0.01f));

        statuses.Set.Remove(fortify);
        Assert.That(ActiveIcons(bar), Is.EqualTo(2));
        Assert.That(bar.Icon(0).Shown, Is.SameAs(burn));

        localPlayer.Assign(null);
        Assert.That(ActiveIcons(bar), Is.Zero, "No local player, no icons.");
        statuses.Set.Apply(fortify, null, 1f);
        Assert.That(ActiveIcons(bar), Is.Zero, "The old player is no longer followed.");
        presenter.Dispose();
    }

    [Test]
    public void StatusBar_CapsAtItsCapacity()
    {
        StatusBarView bar = CreateBar(2);
        LocalPlayerTracker localPlayer = new();
        StatusBarPresenter presenter = new(localPlayer, bar);
        presenter.Start();
        StatusEffects statuses = CreatePlayer("Player");
        localPlayer.Assign(new LocalPlayer(statuses.GetComponent<PlayerController>()));

        for (int i = 0; i < 4; i++)
        {
            statuses.Set.Apply(Definition("Status" + i, StatusKind.Debuff, 4f), null, 1f);
        }

        Assert.That(ActiveIcons(bar), Is.EqualTo(2));
        presenter.Dispose();
    }

    [Test]
    public void WorldIcons_ShowDistinctDebuffsOnly_UpToTheirCount()
    {
        StatusEffects statuses = CreatePlayer("Mob");
        SpriteRenderer[] renderers = new SpriteRenderer[2];
        for (int i = 0; i < renderers.Length; i++)
        {
            renderers[i] = new GameObject("Icon" + i).AddComponent<SpriteRenderer>();
            renderers[i].transform.SetParent(statuses.transform);
        }

        Sprite fallback = Track(Sprite.Create(Track(new Texture2D(4, 4)), new Rect(0f, 0f, 4f, 4f), Vector2.one * 0.5f));
        WorldStatusIcons icons = statuses.gameObject.AddComponent<WorldStatusIcons>();
        icons.Configure(statuses, renderers, fallback);
        StatusEffectDefinition poison = Definition("Poison", StatusKind.Debuff, 6f, StatusStacking.Independent, 3, color: Color.green);

        statuses.Set.Apply(Definition("Fortify", StatusKind.Buff, 8f), null, 1f);
        statuses.Set.Apply(poison, null, 1f);
        statuses.Set.Apply(poison, null, 1f);
        icons.Refresh();

        Assert.That(icons.ShownCount, Is.EqualTo(1));
        Assert.That(renderers[0].enabled, Is.True);
        Assert.That(renderers[0].sprite, Is.SameAs(fallback));
        Assert.That(renderers[0].color, Is.EqualTo(Color.green));
        Assert.That(renderers[1].enabled, Is.False);

        statuses.Set.Apply(Definition("Burn", StatusKind.Debuff, 4f), null, 1f);
        statuses.Set.Apply(Definition("Chill", StatusKind.Debuff, 4f), null, 1f);
        icons.Refresh();
        Assert.That(icons.ShownCount, Is.EqualTo(2));

        statuses.Set.Clear();
        icons.Refresh();
        Assert.That(icons.ShownCount, Is.Zero);
        Assert.That(renderers[0].enabled, Is.False);
    }

    [Test]
    public void Visuals_ShowOnePooledVisualPerStatusThatHasOne_AndReuseThem()
    {
        StatusVisual prefab = CreateVisualPrefab();
        StatusVisualPool pool = new(default(Scene));
        StatusEffects statuses = CreatePlayer("Unit");
        StatusVisuals visuals = statuses.gameObject.AddComponent<StatusVisuals>();
        visuals.Construct(pool);
        StatusEffectDefinition burn = Definition("Burn", StatusKind.Debuff, 4f, color: Color.red, visual: prefab);
        StatusEffectDefinition chill = Definition("Chill", StatusKind.Debuff, 4f, color: Color.cyan, visual: prefab);
        StatusEffectDefinition fortify = Definition("Fortify", StatusKind.Buff, 4f);

        try
        {
            statuses.Set.Apply(burn, null, 1f);
            statuses.Set.Apply(fortify, null, 1f);
            visuals.Refresh();
            Assert.That(visuals.ShownCount, Is.EqualTo(1), "Only statuses with a visual show one.");
            StatusVisual burnVisual = visuals.GetShown(0);
            Assert.That(burnVisual.gameObject.activeSelf, Is.True);
            Assert.That(burnVisual.Color, Is.EqualTo(Color.red));

            statuses.Set.Apply(chill, null, 1f);
            visuals.Refresh();
            Assert.That(visuals.ShownCount, Is.EqualTo(2));
            Assert.That(visuals.GetShown(1).Layer, Is.EqualTo(1), "A second visual is drawn larger.");
            Assert.That(visuals.GetShown(1).transform.localScale.x, Is.GreaterThan(burnVisual.transform.localScale.x));

            statuses.Set.Remove(burn);
            visuals.Refresh();
            Assert.That(visuals.ShownCount, Is.EqualTo(1));
            Assert.That(burnVisual.gameObject.activeSelf, Is.False, "An ended status returns its visual to the pool.");
            Assert.That(visuals.GetShown(0).Layer, Is.Zero);

            statuses.Set.Apply(burn, null, 1f);
            visuals.Refresh();
            Assert.That(visuals.GetShown(1), Is.SameAs(burnVisual), "The pooled visual is reused.");
            Assert.That(pool.CreatedCount, Is.EqualTo(2));

            visuals.ReleaseAll();
            Assert.That(visuals.ShownCount, Is.Zero);
            Assert.That(burnVisual.gameObject.activeSelf, Is.False);
        }
        finally
        {
            foreach (StatusVisual visual in Object.FindObjectsByType<StatusVisual>(FindObjectsInactive.Include))
            {
                if (visual != prefab)
                {
                    Object.DestroyImmediate(visual.gameObject);
                }
            }
        }
    }

    [Test]
    public void Content_HudPlayerAndWeaselCarryTheStatusFeedback()
    {
        GameObject hud = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/PlayerHudCanvas.prefab");
        StatusBarView bar = hud.GetComponentInChildren<StatusBarView>(true);
        Assert.That(bar, Is.Not.Null);
        Assert.That(bar.Capacity, Is.EqualTo(8));
        for (int i = 0; i < bar.Capacity; i++)
        {
            StatusIconView icon = bar.Icon(i);
            Assert.That(icon, Is.Not.Null);
            Assert.That(icon.Frame, Is.Not.Null);
            Assert.That(icon.Icon, Is.Not.Null);
            Assert.That(icon.Label, Is.Not.Null);
            Assert.That(icon.ElapsedFill, Is.Not.Null);
            Assert.That(icon.StacksLabel, Is.Not.Null);
            foreach (Graphic graphic in icon.GetComponentsInChildren<Graphic>(true))
            {
                Assert.That(graphic.raycastTarget, Is.False, $"{graphic.name} must not block clicks.");
            }
        }

        GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player/Player.prefab");
        GameObject weasel = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mobs/Weasel.prefab");
        Assert.That(player.GetComponent<StatusVisuals>(), Is.Not.Null);
        Assert.That(weasel.GetComponent<StatusVisuals>(), Is.Not.Null);
        WorldStatusIcons worldIcons = weasel.GetComponentInChildren<WorldStatusIcons>(true);
        Assert.That(worldIcons, Is.Not.Null);
        Assert.That(worldIcons.GetComponentInParent<WorldHealthBar>(true), Is.Not.Null, "The icons sit with the health bar.");
        SpriteRenderer[] iconRenderers = worldIcons.GetComponentsInChildren<SpriteRenderer>(true);
        int statusIcons = 0;
        foreach (SpriteRenderer renderer in iconRenderers)
        {
            if (renderer.name.StartsWith("StatusIcon"))
            {
                statusIcons++;
                Assert.That(renderer.sortingLayerName, Is.EqualTo("Overlay"));
                Assert.That(renderer.enabled, Is.False, "Hidden until a debuff lands.");
            }
        }

        Assert.That(statusIcons, Is.EqualTo(4));

        StatusEffectCatalog catalog = AssetDatabase.LoadAssetAtPath<CombatSettings>("Assets/Data/Combat/CombatSettings.asset").StatusCatalog;
        HashSet<Color> colours = new();
        for (int i = 0; i < catalog.Count; i++)
        {
            StatusEffectDefinition definition = catalog.Get(i);
            Assert.That(colours.Add(definition.Color), Is.True, $"{definition.name} shares its colour.");
            bool shouldHaveVisual = definition.Kind == StatusKind.Debuff
                ? definition.Controls != StatusControls.None || definition.HasPeriodicEffect || definition.MoveSpeedMultiplier < 1f
                : definition.GrantsDamageImmunity != DamageTypeMask.None || definition.GrantsStatusImmunity != StatusTags.None;
            Assert.That(definition.Visual != null, Is.EqualTo(shouldHaveVisual), $"{definition.name} visual.");
        }
    }

    private static int ActiveIcons(StatusBarView bar)
    {
        int active = 0;
        for (int i = 0; i < bar.Capacity; i++)
        {
            if (bar.Icon(i).gameObject.activeSelf)
            {
                active++;
            }
        }

        return active;
    }

    private StatusEffectDefinition Definition(
        string name,
        StatusKind kind,
        float duration,
        StatusStacking stacking = StatusStacking.Refresh,
        int maxStacks = 1,
        Color? color = null,
        StatusVisual visual = null,
        Sprite icon = null)
    {
        StatusEffectDefinition definition = StatusEffectDefinition.Create(name, kind, duration, stacking, maxStacks, color: color, visual: visual);
        if (icon != null)
        {
            SerializedObject serialized = new(definition);
            serialized.FindProperty("icon").objectReferenceValue = icon;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        return Track(definition);
    }

    private StatusEffects CreatePlayer(string name)
    {
        GameObject unit = Track(new GameObject(name));
        unit.AddComponent<Rigidbody2D>();
        unit.AddComponent<PlayerController>();
        return unit.AddComponent<StatusEffects>();
    }

    private StatusVisual CreateVisualPrefab()
    {
        GameObject prefab = Track(new GameObject("VisualPrefab"));
        prefab.AddComponent<SpriteRenderer>();
        return prefab.AddComponent<StatusVisual>();
    }

    private StatusBarView CreateBar(int capacity)
    {
        GameObject canvas = Track(new GameObject("Canvas", typeof(RectTransform), typeof(Canvas)));
        StatusIconView[] views = new StatusIconView[capacity];
        for (int i = 0; i < capacity; i++)
        {
            views[i] = CreateIconView();
            views[i].transform.SetParent(canvas.transform, false);
        }

        StatusBarView bar = canvas.AddComponent<StatusBarView>();
        bar.ConfigureReferences(views);
        return bar;
    }

    private StatusIconView CreateIconView()
    {
        GameObject slot = Track(new GameObject("Status", typeof(RectTransform), typeof(Image)));
        Image icon = new GameObject("Icon", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        TextMeshProUGUI label = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        Image fill = new GameObject("Fill", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        TextMeshProUGUI stacks = new GameObject("Stacks", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        icon.transform.SetParent(slot.transform, false);
        label.transform.SetParent(slot.transform, false);
        fill.transform.SetParent(slot.transform, false);
        stacks.transform.SetParent(slot.transform, false);
        StatusIconView view = slot.AddComponent<StatusIconView>();
        view.ConfigureReferences(slot.GetComponent<Image>(), icon, label, fill, stacks);
        return view;
    }

    private T Track<T>(T instance) where T : Object
    {
        created.Add(instance);
        return instance;
    }
}
