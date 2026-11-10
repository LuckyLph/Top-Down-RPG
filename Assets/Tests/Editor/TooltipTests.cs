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
/// slot and status icon to its one tooltip.
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
        Assert.That(tooltip.Description, Is.EqualTo("Burns."));

        trigger.OnPointerEnter(null);
        trigger.OnPointerExit(null);
        Assert.That(tooltip.IsShown, Is.True, "Still selected.");

        trigger.OnDeselect(null);
        Assert.That(tooltip.IsShown, Is.False);
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
    /// The HUD prefab holds one hidden tooltip, drawn over everything else on it, and every ability slot and status
    /// icon shows it.
    /// </summary>
    [Test]
    public void TheHudPrefab_WiresEverySlotAndStatusIcon_ToItsTooltip()
    {
        GameObject hud = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/PlayerHudCanvas.prefab");
        HudTooltip[] tooltips = hud.GetComponentsInChildren<HudTooltip>(true);
        Assert.That(tooltips.Length, Is.EqualTo(1));
        Assert.That(tooltips[0].gameObject.activeSelf, Is.False, "Hidden until shown.");
        Assert.That(tooltips[0].transform.GetSiblingIndex(), Is.EqualTo(hud.transform.childCount - 1), "Drawn last, over the HUD.");
        foreach (Graphic graphic in tooltips[0].GetComponentsInChildren<Graphic>(true))
        {
            Assert.That(graphic.raycastTarget, Is.False, $"{graphic.name} never takes the pointer from what it describes.");
        }

        TooltipTrigger[] triggers = hud.GetComponentsInChildren<TooltipTrigger>(true);
        Assert.That(triggers.Length, Is.EqualTo(AbilitySlots.Count + hud.GetComponentInChildren<StatusBarView>(true).Capacity));
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
