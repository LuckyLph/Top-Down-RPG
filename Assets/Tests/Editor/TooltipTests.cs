using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Tests for the HUD's tooltips (Docs/DesignSystem.md, Phase 5), on the Tooltip, SlotAbility and StatusIcon component
/// prefabs under a canvas of their own: a slot or a status icon shows its name and description while the pointer is
/// over it or it is selected and hides it when neither holds, an empty slot shows nothing, the tooltip follows what
/// its source shows and stays inside its canvas, a shown status icon takes the click, and the HUD prefab wires every
/// slot, the weapon slot and every status icon to its one tooltip. Since Docs/UiRevamp.md's Phase 1 an ability's
/// tooltip gives its cooldown, cast time and range and the weapon slot's its damage, type and range, read from their
/// definitions (<see cref="TooltipDetails"/>), and a tooltip with no room above its target shows below it.
/// </summary>
public class TooltipTests
{
    private const string Components = "Assets/Prefabs/UI/Components/";

    private readonly List<Object> createdObjects = new();
    private Canvas canvas;
    private HudTooltip tooltip;

    /// <summary>
    /// Builds an overlay canvas holding a hidden tooltip.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        GameObject canvasObject = Track(new GameObject("Canvas", typeof(RectTransform), typeof(Canvas)));
        canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        tooltip = Spawn("Tooltip").GetComponent<HudTooltip>();
        tooltip.gameObject.SetActive(false);
        Canvas.ForceUpdateCanvases();
    }

    /// <summary>
    /// Destroys the canvas and the definitions the test made.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        foreach (Object created in createdObjects)
        {
            if (created != null)
            {
                Object.DestroyImmediate(created);
            }
        }

        createdObjects.Clear();
    }

    /// <summary>
    /// An ability slot shows its ability's name and description above it while the pointer is over it, and hides
    /// them when the pointer leaves.
    /// </summary>
    [Test]
    public void AnAbilitySlot_ShowsItsAbility_WhileThePointerIsOverIt()
    {
        AbilitySlotView slot = Spawn("SlotAbility").GetComponent<AbilitySlotView>();
        TooltipTrigger trigger = Trigger(slot);
        slot.SetAbility(Track(AbilityDefinition.Create("Test Bolt", description: "Fires a test bolt.")));

        trigger.OnPointerEnter(null);

        Assert.That(tooltip.IsShown, Is.True);
        Assert.That(tooltip.Title, Is.EqualTo("Test Bolt"));
        Assert.That(tooltip.Details, Is.EqualTo("Cooldown 1 s · Instant\nRange 3"));
        Assert.That(tooltip.Description, Is.EqualTo("Fires a test bolt."));
        Assert.That(tooltip.Target, Is.EqualTo(slot.transform));
        trigger.OnPointerExit(null);
        Assert.That(tooltip.IsShown, Is.False);
    }

    /// <summary>
    /// A status icon shows its status's name and description while it is selected, and hides them when deselected;
    /// the pointer leaving does not hide a selected icon's tooltip.
    /// </summary>
    [Test]
    public void AStatusIcon_ShowsItsStatus_WhileSelected()
    {
        StatusIconView icon = Spawn("StatusIcon").GetComponent<StatusIconView>();
        TooltipTrigger trigger = Trigger(icon);
        icon.Show(Status("Test Burn", "Burns."), 1);

        trigger.OnSelect(null);
        Assert.That(tooltip.IsShown, Is.True);
        Assert.That(tooltip.Title, Is.EqualTo("Test Burn"));
        Assert.That(tooltip.Details, Is.Null, "A status has no numbers, so the line hides.");
        Assert.That(tooltip.Description, Is.EqualTo("Burns."));

        trigger.OnPointerEnter(null);
        trigger.OnPointerExit(null);
        Assert.That(tooltip.IsShown, Is.True, "Still selected.");

        trigger.OnDeselect(null);
        Assert.That(tooltip.IsShown, Is.False);
    }

    /// <summary>
    /// An ability's numbers are its cooldown and cast time ("Instant" for none), then its range on a second line when
    /// it is aimed at a point, a unit or along a line of some length; a weapon's are its damage and type, then its range
    /// for a bow, or melee.
    /// </summary>
    [Test]
    public void TheNumbers_ComeFromTheDefinitions()
    {
        Assert.That(TooltipDetails.For(Track(AbilityDefinition.Create("Bolt", cooldown: 2f, castTime: 0.25f, range: 6f))), Is.EqualTo("Cooldown 2 s · Cast 0.25 s\nRange 6"));
        Assert.That(TooltipDetails.For(Track(AbilityDefinition.Create("Mend", cooldown: 6f, castTime: 0.5f, targeting: AbilityTargeting.Unit, range: 4f))), Is.EqualTo("Cooldown 6 s · Cast 0.5 s\nRange 4"));
        Assert.That(TooltipDetails.For(Track(AbilityDefinition.Create("Leap", cooldown: 4f, targeting: AbilityTargeting.Point, range: 5f))), Is.EqualTo("Cooldown 4 s · Instant\nRange 5"));
        Assert.That(TooltipDetails.For(Track(AbilityDefinition.Create("Whirl", cooldown: 1.5f, castTime: 0.3f, targeting: AbilityTargeting.None))), Is.EqualTo("Cooldown 1.5 s · Cast 0.3 s"));
        Assert.That(TooltipDetails.For(Track(AbilityDefinition.Create("Roll", cooldown: 5f, castTime: 0.15f, range: 0f))), Is.EqualTo("Cooldown 5 s · Cast 0.15 s"));
        Assert.That(TooltipDetails.For((AbilityDefinition)null), Is.Null);
        Assert.That(TooltipDetails.For(Track(PlayerWeapon.Create("Sword", 20, 0.35f, 0.65f, new Vector2[4]))), Is.EqualTo("20 Physical · Melee"));
        PlayerWeapon bow = Track(PlayerWeapon.Create("Bow", 24, 0.8f, 0.55f, new Vector2[4], range: 5f, type: DamageType.Frost, projectile: new ProjectileSpec(18f, 6.5f, 0.1f, 0.5f, true, null)));
        Assert.That(TooltipDetails.For(bow), Is.EqualTo("24 Frost · Range 5"));
        Assert.That(TooltipDetails.For((PlayerWeapon)null), Is.Null);
    }

    /// <summary>
    /// The weapon slot shows its weapon's name and numbers, without a description, while the pointer is over it, and
    /// nothing without a weapon.
    /// </summary>
    [Test]
    public void TheWeaponSlot_ShowsItsWeaponsNumbers()
    {
        GameObject slotObject = Track(new GameObject("WeaponSlot", typeof(RectTransform), typeof(Image)));
        slotObject.transform.SetParent(canvas.transform, false);
        Image icon = new GameObject("Icon", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        icon.transform.SetParent(slotObject.transform, false);
        WeaponSlotView slot = slotObject.AddComponent<WeaponSlotView>();
        slot.ConfigureReferences(icon, TestThemes.Shared);
        TooltipTrigger trigger = slotObject.AddComponent<TooltipTrigger>();
        trigger.Configure(tooltip);
        Lifecycle(trigger, "OnEnable");
        slot.SetWeapon(Track(PlayerWeapon.Create("Test Sword", 20, 0.35f, 0.65f, new Vector2[4])));

        trigger.OnPointerEnter(null);
        Assert.That(tooltip.IsShown, Is.True);
        Assert.That(tooltip.Title, Is.EqualTo("Test Sword"));
        Assert.That(tooltip.Details, Is.EqualTo("20 Physical · Melee"));
        Assert.That(tooltip.Description, Is.Null, "A weapon has no description, so the line hides.");

        slot.SetWeapon(null);
        Assert.That(tooltip.IsShown, Is.False, "The tooltip follows the slot, which has nothing to show without a weapon.");
        Assert.That(icon.color, Is.EqualTo(TestThemes.Shared.Colour(UiColour.IconMissing)));
    }

    /// <summary>
    /// A tooltip whose target sits at the top of its canvas, as the weapon slot does, shows below it instead of
    /// covering it, on whole UI px.
    /// </summary>
    [Test]
    public void TheTooltip_ShowsBelowATargetWithNoRoomAbove()
    {
        AbilitySlotView slot = Spawn("SlotAbility").GetComponent<AbilitySlotView>();
        RectTransform slotRect = (RectTransform)slot.transform;
        RectTransform canvasRect = (RectTransform)canvas.transform;
        slotRect.anchorMin = new Vector2(1f, 1f);
        slotRect.anchorMax = new Vector2(1f, 1f);
        slotRect.pivot = new Vector2(1f, 1f);
        slotRect.anchoredPosition = new Vector2(-10f, -10f);
        TooltipTrigger trigger = Trigger(slot);
        slot.SetAbility(Track(AbilityDefinition.Create("Test Whirl", description: "Spins around and around, hitting everything close by.")));
        Canvas.ForceUpdateCanvases();

        trigger.OnPointerEnter(null);

        RectTransform box = (RectTransform)tooltip.transform;
        Rect inCanvas = Bounds(box, canvasRect);
        Rect target = Bounds(slotRect, canvasRect);
        Assert.That(inCanvas.yMax, Is.LessThanOrEqualTo(target.yMin + 0.001f), "Below the slot.");
        Assert.That(inCanvas.yMin, Is.GreaterThanOrEqualTo(canvasRect.rect.yMin - 0.001f), "Inside the canvas.");
        Assert.That(box.anchoredPosition, Is.EqualTo(new Vector2(Mathf.Round(box.anchoredPosition.x), Mathf.Round(box.anchoredPosition.y))), "On whole UI px.");
    }

    /// <summary>
    /// A trigger that keeps a rect clear (the command bar, Docs/UiRevamp.md's Phase 2) shows its tooltip above that rect
    /// too, still centred over its own object.
    /// </summary>
    [Test]
    public void TheTooltip_ShowsAboveTheRectItsTriggerKeepsClear()
    {
        RectTransform canvasRect = (RectTransform)canvas.transform;
        RectTransform cluster = Track(new GameObject("CommandBar", typeof(RectTransform))).GetComponent<RectTransform>();
        cluster.SetParent(canvasRect, false);
        cluster.anchorMin = new Vector2(0.5f, 0f);
        cluster.anchorMax = new Vector2(0.5f, 0f);
        cluster.pivot = new Vector2(0.5f, 0f);
        cluster.anchoredPosition = new Vector2(0f, 8f);
        cluster.sizeDelta = new Vector2(378f, 60f);
        AbilitySlotView slot = Spawn("SlotAbility").GetComponent<AbilitySlotView>();
        RectTransform slotRect = (RectTransform)slot.transform;
        slotRect.SetParent(cluster, false);
        slotRect.anchorMin = new Vector2(0.5f, 0f);
        slotRect.anchorMax = new Vector2(0.5f, 0f);
        slotRect.anchoredPosition = new Vector2(-60f, 16f);
        TooltipTrigger trigger = slot.GetComponent<TooltipTrigger>();
        trigger.Configure(tooltip, cluster);
        slot.SetAbility(Track(AbilityDefinition.Create("Test Bolt", description: "Fires a test bolt.")));
        Canvas.ForceUpdateCanvases();

        trigger.OnPointerEnter(null);

        Rect box = Bounds((RectTransform)tooltip.transform, canvasRect);
        Rect kept = Bounds(cluster, canvasRect);
        Rect target = Bounds(slotRect, canvasRect);
        Assert.That(box.yMin, Is.GreaterThanOrEqualTo(kept.yMax), "Above the command bar.");
        Assert.That(box.center.x, Is.EqualTo(target.center.x).Within(1f), "Centred over its slot.");
    }

    /// <summary>
    /// An empty slot has nothing to show, so pointing at it shows no tooltip.
    /// </summary>
    [Test]
    public void AnEmptySlot_ShowsNoTooltip()
    {
        AbilitySlotView slot = Spawn("SlotAbility").GetComponent<AbilitySlotView>();
        TooltipTrigger trigger = Trigger(slot);
        slot.SetAbility(null);

        trigger.OnPointerEnter(null);

        Assert.That(tooltip.IsShown, Is.False);
    }

    /// <summary>
    /// A shown tooltip follows its icon when the icon shows another status, and hides when the icon is hidden; one
    /// object hiding its tooltip leaves another's shown.
    /// </summary>
    [Test]
    public void TheTooltip_FollowsItsSource_AndHidesWithIt()
    {
        StatusIconView icon = Spawn("StatusIcon").GetComponent<StatusIconView>();
        TooltipTrigger trigger = Trigger(icon);
        StatusIconView other = Spawn("StatusIcon").GetComponent<StatusIconView>();
        TooltipTrigger otherTrigger = Trigger(other);
        icon.Show(Status("Test Burn", "Burns."), 1);
        other.Show(Status("Test Chill", "Chills."), 1);
        Lifecycle(trigger, "OnEnable");

        trigger.OnPointerEnter(null);
        icon.Show(Status("Test Root", "Roots."), 1);
        Assert.That(tooltip.Title, Is.EqualTo("Test Root"));

        otherTrigger.OnPointerExit(null);
        Assert.That(tooltip.IsShown, Is.True, "Another icon leaves this one's tooltip alone.");

        icon.Hide();
        Lifecycle(trigger, "OnDisable");
        Assert.That(tooltip.IsShown, Is.False);
    }

    /// <summary>
    /// The tooltip sits above what it describes, and is moved inside its canvas where it would leave it, on whole UI
    /// px.
    /// </summary>
    [Test]
    public void TheTooltip_SitsAboveItsTarget_InsideItsCanvas()
    {
        AbilitySlotView slot = Spawn("SlotAbility").GetComponent<AbilitySlotView>();
        RectTransform slotRect = (RectTransform)slot.transform;
        RectTransform canvasRect = (RectTransform)canvas.transform;
        slotRect.anchorMin = new Vector2(1f, 0f);
        slotRect.anchorMax = new Vector2(1f, 0f);
        slotRect.pivot = new Vector2(1f, 0f);
        slotRect.anchoredPosition = new Vector2(-2f, 8f);
        TooltipTrigger trigger = Trigger(slot);
        slot.SetAbility(Track(AbilityDefinition.Create("Test Whirl", description: "Spins around and around, hitting everything close by.")));
        Canvas.ForceUpdateCanvases();

        trigger.OnPointerEnter(null);

        RectTransform box = (RectTransform)tooltip.transform;
        Rect inCanvas = Bounds(box, canvasRect);
        Rect target = Bounds(slotRect, canvasRect);
        Assert.That(inCanvas.xMax, Is.LessThanOrEqualTo(canvasRect.rect.xMax + 0.001f), "Kept inside the canvas's right edge.");
        Assert.That(inCanvas.yMin, Is.GreaterThanOrEqualTo(target.yMax), "Above the slot.");
        Assert.That(box.anchoredPosition, Is.EqualTo(new Vector2(Mathf.Round(box.anchoredPosition.x), Mathf.Round(box.anchoredPosition.y))), "On whole UI px.");
    }

    /// <summary>
    /// A shown status icon's frame is an active raycast target that takes a click at its centre, which the graphic
    /// raycaster then hits instead of the world; a hidden icon's frame is inactive, so the raycaster skips it.
    /// </summary>
    [Test]
    public void AShownStatusIcon_TakesTheClick_AndAHiddenOneDoesNot()
    {
        StatusIconView icon = Spawn("StatusIcon").GetComponent<StatusIconView>();
        icon.Show(Status("Test Burn", "Burns."), 1);
        Canvas.ForceUpdateCanvases();
        Vector2 centre = RectTransformUtility.WorldToScreenPoint(null, icon.transform.position);

        Assert.That(icon.Frame.raycastTarget, Is.True);
        Assert.That(icon.Frame.IsActive(), Is.True);
        Assert.That(RectTransformUtility.RectangleContainsScreenPoint(icon.Frame.rectTransform, centre, null), Is.True);
        Assert.That(icon.Frame.Raycast(centre, null), Is.True, "Nothing on the icon filters the click out.");

        icon.Hide();
        Assert.That(icon.Frame.IsActive(), Is.False, "A hidden icon takes no click.");
    }

    /// <summary>
    /// The HUD prefab holds one hidden tooltip with its numbers line, drawn over everything else on it, and every
    /// ability slot, the weapon slot and every status icon shows it.
    /// </summary>
    [Test]
    public void TheHudPrefab_WiresEverySlotAndStatusIcon_ToItsTooltip()
    {
        GameObject hud = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/PlayerHudCanvas.prefab");
        HudTooltip[] tooltips = hud.GetComponentsInChildren<HudTooltip>(true);
        Assert.That(tooltips.Length, Is.EqualTo(1));
        Assert.That(tooltips[0].gameObject.activeSelf, Is.False, "Hidden until shown.");
        Assert.That(tooltips[0].transform.GetSiblingIndex(), Is.EqualTo(hud.transform.childCount - 1), "Drawn last, over the HUD.");
        Assert.That(new SerializedObject(tooltips[0]).FindProperty("detailsText").objectReferenceValue, Is.Not.Null, "The tooltip has its numbers line.");
        foreach (Graphic graphic in tooltips[0].GetComponentsInChildren<Graphic>(true))
        {
            Assert.That(graphic.raycastTarget, Is.False, $"{graphic.name} never takes the pointer from what it describes.");
        }

        TooltipTrigger[] triggers = hud.GetComponentsInChildren<TooltipTrigger>(true);
        Assert.That(triggers.Length, Is.EqualTo(AbilitySlots.Count + 1 + hud.GetComponentInChildren<StatusBarView>(true).Capacity));
        Assert.That(hud.GetComponent<PlayerHudView>().WeaponSlot.GetComponent<TooltipTrigger>(), Is.Not.Null, "The weapon slot shows the tooltip.");
        Transform commandBar = hud.transform.Find("CommandBar");
        foreach (TooltipTrigger trigger in triggers)
        {
            Assert.That(new SerializedObject(trigger).FindProperty("clearOf").objectReferenceValue, Is.EqualTo(commandBar), $"{trigger.name}'s tooltip keeps the command bar in view.");
        }
        foreach (TooltipTrigger trigger in triggers)
        {
            Assert.That(trigger.Tooltip, Is.EqualTo(tooltips[0]), $"{trigger.name} shows the HUD's tooltip.");
            Assert.That(trigger.GetComponent<ITooltipSource>(), Is.Not.Null, $"{trigger.name} has a source.");
            Assert.That(trigger.GetComponent<Image>().raycastTarget, Is.True, $"{trigger.name} takes the pointer.");
        }
    }

    /// <summary>
    /// The rect of <paramref name="rect"/> in <paramref name="space"/>'s local space.
    /// </summary>
    private static Rect Bounds(RectTransform rect, RectTransform space)
    {
        Vector3[] corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        Vector2 min = space.InverseTransformPoint(corners[0]);
        Vector2 max = space.InverseTransformPoint(corners[2]);
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    /// <summary>
    /// A copy of the component prefab named <paramref name="prefab"/> under the canvas.
    /// </summary>
    private GameObject Spawn(string prefab)
    {
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>($"{Components}{prefab}.prefab");
        return Object.Instantiate(asset, canvas != null ? canvas.transform : null, false);
    }

    /// <summary>
    /// The tooltip trigger on <paramref name="source"/>'s object, set to show the test's tooltip.
    /// </summary>
    private TooltipTrigger Trigger(Component source)
    {
        TooltipTrigger trigger = source.GetComponent<TooltipTrigger>();
        trigger.Configure(tooltip);
        return trigger;
    }

    /// <summary>
    /// A debuff named <paramref name="name"/> with <paramref name="description"/>.
    /// </summary>
    private StatusEffectDefinition Status(string name, string description)
    {
        StatusEffectDefinition status = Track(StatusEffectDefinition.Create(name, StatusKind.Debuff, 3f));
        SerializedObject serialized = new(status);
        serialized.FindProperty("description").stringValue = description;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return status;
    }

    /// <summary>
    /// Runs <paramref name="target"/>'s <paramref name="method"/>, which EditMode does not run for components without
    /// <c>[ExecuteAlways]</c>.
    /// </summary>
    private static void Lifecycle(Object target, string method)
    {
        target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
    }

    /// <summary>
    /// Records <paramref name="created"/> for destruction in <see cref="TearDown"/> and returns it.
    /// </summary>
    private T Track<T>(T created) where T : Object
    {
        createdObjects.Add(created);
        return created;
    }
}
