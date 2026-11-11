using System.Collections.Generic;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Tests for status feedback: client-side countdown and remaining time, the HUD status bar and its icons, the
/// world debuff icons over units, pooled status visuals, and the project content that carries them.
/// </summary>
public class StatusFeedbackTests
{
    private readonly List<Object> created = new();

    /// <summary>
    /// Destroys everything the test tracked, taking the whole game object for tracked components.
    /// </summary>
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

    /// <summary>
    /// <see cref="StatusEffectSet.CountDown"/> runs remaining times down to no lower than zero without removing
    /// statuses or raising changes, since only the host ends a status.
    /// </summary>
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

    /// <summary>
    /// <see cref="StatusEffectSet.GetRemaining"/> returns the longest remaining time among a status's instances,
    /// and zero for a status that is not active.
    /// </summary>
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

    /// <summary>
    /// A definition's abbreviation is the first two letters of its name, or the whole name when shorter.
    /// </summary>
    [Test]
    public void Definition_Abbreviation_IsUpToTwoLettersOfTheName()
    {
        Assert.That(Definition("Burn", StatusKind.Debuff, 1f).Abbreviation, Is.EqualTo("Bu"));
        Assert.That(Definition("X", StatusKind.Debuff, 1f).Abbreviation, Is.EqualTo("X"));
    }

    /// <summary>
    /// A status icon shows the definition's sprite, or its abbreviation when it has none, frames it in the
    /// status colour, labels stacks only above one, fills by elapsed fraction, and hides by deactivating.
    /// </summary>
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

    /// <summary>
    /// The status bar presenter shows the local player's statuses as they are bound and change, one icon per status in
    /// its kind's group, buffs left and debuffs right (Docs/UiRevamp.md, Phase 2), updates elapsed fills on tick, and
    /// drops the old player when the local player is cleared.
    /// </summary>
    [Test]
    public void StatusBar_FollowsTheLocalPlayer_InAGroupPerKind_OneIconPerStatus()
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
        Assert.That(ActiveIcons(bar, StatusKind.Debuff), Is.EqualTo(1), "Binding shows what is already active.");

        statuses.Set.Apply(poison, null, 1f);
        statuses.Set.Apply(poison, null, 1f);
        statuses.Set.Apply(fortify, null, 1f);

        Assert.That(ActiveIcons(bar, StatusKind.Buff), Is.EqualTo(1));
        Assert.That(ActiveIcons(bar, StatusKind.Debuff), Is.EqualTo(2), "Independent doses share one icon.");
        Assert.That(bar.Icon(StatusKind.Buff, 0).Shown, Is.SameAs(fortify), "Buffs in their own group.");
        Assert.That(bar.Icon(StatusKind.Debuff, 0).Shown, Is.SameAs(burn), "The first debuff nearest the abilities.");
        Assert.That(bar.Icon(StatusKind.Debuff, 0).StacksLabel.text, Is.EqualTo("2"));
        Assert.That(bar.Icon(StatusKind.Debuff, 1).Shown, Is.SameAs(poison));
        Assert.That(bar.Icon(StatusKind.Debuff, 1).StacksLabel.text, Is.EqualTo("2"));

        statuses.Set.Tick(2f, null);
        presenter.Tick();
        Assert.That(bar.Icon(StatusKind.Debuff, 0).ElapsedFill.fillAmount, Is.EqualTo(0.5f).Within(0.01f), "Burn has 2 of its 4 seconds left.");
        Assert.That(bar.Icon(StatusKind.Buff, 0).ElapsedFill.fillAmount, Is.EqualTo(0.25f).Within(0.01f));

        statuses.Set.Remove(fortify);
        Assert.That(ActiveIcons(bar, StatusKind.Buff), Is.Zero);
        Assert.That(bar.Icon(StatusKind.Debuff, 0).Shown, Is.SameAs(burn));

        localPlayer.Assign(null);
        Assert.That(ActiveIcons(bar, StatusKind.Debuff), Is.Zero, "No local player, no icons.");
        statuses.Set.Apply(fortify, null, 1f);
        Assert.That(ActiveIcons(bar, StatusKind.Buff), Is.Zero, "The old player is no longer followed.");
        presenter.Dispose();
    }

    /// <summary>
    /// A group with more statuses of its kind than icons shows all but the last that fit, and on its last icon "+n"
    /// for the rest, whose tooltip names them; the other group is untouched, and the "+n" goes once they fit again.
    /// </summary>
    [Test]
    public void StatusBar_ShowsPlusN_ForTheStatusesThatDoNotFit()
    {
        StatusBarView bar = CreateBar(3);
        LocalPlayerTracker localPlayer = new();
        StatusBarPresenter presenter = new(localPlayer, bar);
        presenter.Start();
        StatusEffects statuses = CreatePlayer("Player");
        localPlayer.Assign(new LocalPlayer(statuses.GetComponent<PlayerController>()));
        StatusEffectDefinition[] debuffs = new StatusEffectDefinition[5];
        for (int i = 0; i < debuffs.Length; i++)
        {
            debuffs[i] = Definition("Status" + i, StatusKind.Debuff, 4f);
            statuses.Set.Apply(debuffs[i], null, 1f);
        }

        Assert.That(ActiveIcons(bar, StatusKind.Debuff), Is.EqualTo(3));
        Assert.That(bar.Icon(StatusKind.Debuff, 1).Shown, Is.SameAs(debuffs[1]));
        StatusIconView more = bar.Icon(StatusKind.Debuff, 2);
        Assert.That(more.More, Is.EqualTo(3));
        Assert.That(more.Label.text, Is.EqualTo("+3"));
        Assert.That(more.TryGetTooltip(out string title, out _, out string description), Is.True);
        Assert.That(title, Is.EqualTo("3 more"));
        Assert.That(description, Is.EqualTo("Status2, Status3, Status4"));
        Assert.That(ActiveIcons(bar, StatusKind.Buff), Is.Zero);

        statuses.Set.Remove(debuffs[4]);
        statuses.Set.Remove(debuffs[3]);
        Assert.That(more.More, Is.Zero);
        Assert.That(more.Shown, Is.SameAs(debuffs[2]), "Three fit in three icons.");
        presenter.Dispose();
    }

    /// <summary>
    /// World status icons show one tinted renderer per distinct debuff (buffs are skipped), using the fallback
    /// sprite for debuffs without an icon, up to the number of renderers, and hide them all when cleared.
    /// </summary>
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

    /// <summary>
    /// <see cref="StatusVisuals"/> shows one pooled, tinted visual per active status that defines one, layers
    /// later visuals larger, returns ended ones to the pool for reuse, and releases all of them on demand.
    /// </summary>
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

    /// <summary>
    /// The HUD prefab has two six-icon status groups (Docs/UiRevamp.md, Phase 2) whose icons are fully wired and block
    /// clicks with their frame alone (so a click on a shown icon never reaches the world, Docs/DesignSystem.md's Phase 5), the player and
    /// weasel carry status visuals, the weasel has four hidden overlay status icons with its health
    /// bar, and every catalog status has a unique colour and a visual exactly when it should.
    /// </summary>
    [Test]
    public void Content_HudPlayerAndWeaselCarryTheStatusFeedback()
    {
        GameObject hud = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/PlayerHudCanvas.prefab");
        StatusBarView bar = hud.GetComponentInChildren<StatusBarView>(true);
        Assert.That(bar, Is.Not.Null);
        Assert.That(bar.GroupCapacity(StatusKind.Buff), Is.EqualTo(6));
        Assert.That(bar.GroupCapacity(StatusKind.Debuff), Is.EqualTo(6));
        for (int i = 0; i < bar.Capacity; i++)
        {
            StatusIconView icon = bar.Icon(i < 6 ? StatusKind.Buff : StatusKind.Debuff, i % 6);
            Assert.That(icon, Is.Not.Null);
            Assert.That(icon.Frame, Is.Not.Null);
            Assert.That(icon.Icon, Is.Not.Null);
            Assert.That(icon.Label, Is.Not.Null);
            Assert.That(icon.ElapsedFill, Is.Not.Null);
            Assert.That(icon.StacksLabel, Is.Not.Null);
            foreach (Graphic graphic in icon.GetComponentsInChildren<Graphic>(true))
            {
                Assert.That(graphic.raycastTarget, Is.EqualTo(graphic == icon.Frame), graphic == icon.Frame ? "The frame blocks clicks." : $"{graphic.name} must not block clicks.");
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

    /// <summary>
    /// Counts the bar's icon slots whose game object is active.
    /// </summary>
    private static int ActiveIcons(StatusBarView bar, StatusKind kind)
    {
        int active = 0;
        for (int i = 0; i < bar.GroupCapacity(kind); i++)
        {
            if (bar.Icon(kind, i).gameObject.activeSelf)
            {
                active++;
            }
        }

        return active;
    }

    /// <summary>
    /// Creates a tracked status definition with the given colour and visual, assigning <paramref name="icon"/>
    /// through serialization because the factory does not take one.
    /// </summary>
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

    /// <summary>
    /// Creates a tracked unit with a rigidbody, a <see cref="PlayerController"/> and <see cref="StatusEffects"/>,
    /// and returns its statuses.
    /// </summary>
    private StatusEffects CreatePlayer(string name)
    {
        GameObject unit = Track(new GameObject(name));
        unit.AddComponent<Rigidbody2D>();
        unit.AddComponent<PlayerController>();
        return unit.AddComponent<StatusEffects>();
    }

    /// <summary>
    /// Creates a tracked scene object with a sprite renderer and a <see cref="StatusVisual"/> to stand in for a
    /// visual prefab.
    /// </summary>
    private StatusVisual CreateVisualPrefab()
    {
        GameObject prefab = Track(new GameObject("VisualPrefab"));
        prefab.AddComponent<SpriteRenderer>();
        return prefab.AddComponent<StatusVisual>();
    }

    /// <summary>
    /// Creates a tracked canvas holding a <see cref="StatusBarView"/> with <paramref name="capacity"/> icons in each
    /// group.
    /// </summary>
    private StatusBarView CreateBar(int capacity)
    {
        GameObject canvas = Track(new GameObject("Canvas", typeof(RectTransform), typeof(Canvas)));
        StatusIconView[] buffs = new StatusIconView[capacity];
        StatusIconView[] debuffs = new StatusIconView[capacity];
        for (int i = 0; i < capacity; i++)
        {
            buffs[i] = CreateIconView();
            buffs[i].transform.SetParent(canvas.transform, false);
            debuffs[i] = CreateIconView();
            debuffs[i].transform.SetParent(canvas.transform, false);
        }

        StatusBarView bar = canvas.AddComponent<StatusBarView>();
        bar.ConfigureReferences(buffs, debuffs);
        return bar;
    }

    /// <summary>
    /// Creates a tracked status icon slot with its frame, icon, label, elapsed fill and stacks label wired up.
    /// </summary>
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
        view.ConfigureReferences(slot.GetComponent<Image>(), icon, label, fill, stacks, TestThemes.Shared);
        return view;
    }

    /// <summary>
    /// Records <paramref name="instance"/> for destruction in <see cref="TearDown"/> and returns it.
    /// </summary>
    private T Track<T>(T instance) where T : Object
    {
        created.Add(instance);
        return instance;
    }
}
