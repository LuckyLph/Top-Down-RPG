using System.Collections.Generic;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Tests for the options screen's layout (Docs/UiRevamp.md, Phase 8) at the UI size of every display in the Scale table,
/// every size step included, on each of its categories: the 472 UI px block centred, the rail at its left and the pane
/// beside it, every rail item in the rail, every row and shown Reset in the pane clear of the scrollbar's room, the
/// description at the pane's foot, the notice line, the warning and the footer under it, and the vitals strip clear of
/// the title, all inside the screen and on whole UI px.
/// </summary>
public class OptionsMenuLayoutTests
{
    private const string ScreenPath = "Assets/Prefabs/UI/OptionsMenu.prefab";

    private GameObject canvas;
    private GameObject eventObject;
    private InputActionAsset actions;
    private InputBindings bindings;
    private OptionsMenuController controller;
    private UiScaleSettings scaleSettings;

    /// <summary>
    /// Destroys the screen, the event system and the copied input actions.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        controller?.Dispose();
        controller = null;
        bindings?.Dispose();
        bindings = null;
        if (canvas != null)
        {
            Object.DestroyImmediate(canvas);
        }

        if (eventObject != null)
        {
            Object.DestroyImmediate(eventObject);
        }

        if (actions != null)
        {
            Object.DestroyImmediate(actions);
        }

        if (scaleSettings != null)
        {
            Object.DestroyImmediate(scaleSettings);
        }
    }

    /// <summary>
    /// Every display's screen keeps its parts inside it and apart, on every category, 480 x 270 among them.
    /// </summary>
    [Test]
    public void AtEveryDisplay_TheScreenFits()
    {
        UiScaleSettings settings = AssetDatabase.LoadAssetAtPath<UiScaleSettings>("Assets/Data/UI/UiScale.asset");
        HashSet<Vector2Int> sizes = new();
        foreach (int[] display in UiScaleTests.Displays)
        {
            for (int step = 0; step <= UiScale.StepsFor(display[0], display[1], settings); step++)
            {
                int zoom = UiScale.ZoomFor(display[0], display[1], step, settings);
                sizes.Add(new Vector2Int(display[0] / zoom, display[1] / zoom));
            }
        }

        List<string> problems = new();
        foreach (Vector2Int size in sizes)
        {
            foreach (OptionsMenuController.Category category in new[] { OptionsMenuController.Category.Accessibility, OptionsMenuController.Category.Interface, OptionsMenuController.Category.Controls })
            {
                CheckAt(size.x, size.y, category, problems);
            }
        }

        Assert.That(sizes, Does.Contain(new Vector2Int(480, 270)));
        Assert.That(problems, Is.Empty, string.Join("\n", problems));
    }

    /// <summary>
    /// Lays the screen out on a <paramref name="width"/> x <paramref name="height"/> UI px parent, on
    /// <paramref name="category"/> with every row changed so each Reset shows, the vitals strip and the warning shown,
    /// and checks each part.
    /// </summary>
    private void CheckAt(int width, int height, OptionsMenuController.Category category, List<string> problems)
    {
        eventObject = new GameObject("EventSystem");
        EventSystem events = eventObject.AddComponent<EventSystem>();
        canvas = new GameObject("Canvas", typeof(RectTransform));
        ((RectTransform)canvas.transform).sizeDelta = new Vector2(width, height);
        GameObject screen = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ScreenPath), canvas.transform);
        OptionsMenuView view = screen.GetComponent<OptionsMenuView>();
        UserSettings settings = new(new MemorySaveStore());
        settings.SetUiSizeStep(1);
        settings.SetReduceFlashing(true);
        settings.SetReduceMotion(true);
        settings.SetAllyPopups(AllyPopups.Smaller);
        settings.SetChatTimestamps(true);
        settings.SetChatWindow(new ChatWindowPlace(ScreenCorner.BottomLeft, new Vector2Int(8, 80), new Vector2Int(200, 100)));
        actions = InputActionAsset.FromJson(AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/InputSystem_Actions.inputactions").ToJson());
        bindings = new InputBindings(actions, AssetDatabase.LoadAssetAtPath<RebindableActionList>("Assets/Data/Input/RebindableActions.asset"), settings);
        bindings.Initialize();
        for (int i = 0; i < bindings.Count; i++)
        {
            bindings.TryAssign(bindings.GetAction(i), $"<Keyboard>/f{i + 1}", out _);
        }

        scaleSettings = UiScaleSettings.Create();
        UiScale scale = new(scaleSettings, settings);
        scale.Apply(3840, 2160, 2);
        controller = new OptionsMenuController(view, bindings, new MenuStack(new FakeMenuInput()), new UISelection(events), settings, scale);
        controller.Start();
        controller.Open(null, category == OptionsMenuController.Category.Controls ? OptionsMenuController.Opening.Controls : OptionsMenuController.Opening.First);
        if (category == OptionsMenuController.Category.Interface)
        {
            controller.GetRailItem(category).Button.onClick.Invoke();
        }

        view.Vitals.SetShown(true);
        view.Vitals.SetSession(VitalsStripPresenter.SessionLine);
        view.Vitals.SetHealth(100, 100);
        view.SetWarning(VitalsStripPresenter.DamageWarning);
        view.Place();
        foreach (LayoutGroup group in screen.GetComponentsInChildren<LayoutGroup>(true))
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)group.transform);
        }

        string where = $"{width} x {height}, {category}";
        RectTransform root = (RectTransform)screen.transform;
        Rect area = root.rect;
        float left = Mathf.Floor((area.width - OptionsMenuView.BlockWidth) * 0.5f);
        float contentBottom = area.height - OptionsMenuView.ContentBottom;
        Rect rail = RectOf(view.Rail, root);
        Rect list = RectOf(view.List, root);
        Rect description = RectOf(view.Description.rectTransform, root);
        if (rail.xMin != left || list.xMax != left + OptionsMenuView.BlockWidth)
        {
            problems.Add($"{where}: the block spans {rail.xMin} to {list.xMax}, not centred from {left}.");
        }

        CheckInside(where, "the rail", rail, 0f, OptionsMenuView.ContentTop, list.xMin - OptionsMenuView.Gap, contentBottom, problems);
        CheckInside(where, "the pane", list, rail.xMax + OptionsMenuView.Gap, OptionsMenuView.ContentTop, area.width, description.yMin, problems);
        CheckInside(where, "the description", description, list.xMin, list.yMax, list.xMax, contentBottom, problems);
        if (view.Description.GetPreferredValues(OptionsMenuController.ReduceMotionDescription, description.width, 0f).y > description.height + 0.01f)
        {
            problems.Add($"{where}: the longest description does not fit the description's two lines.");
        }

        for (int i = 0; i < view.Rail.childCount; i++)
        {
            CheckInside(where, $"rail item {i}", RectOf((RectTransform)view.Rail.GetChild(i), root), rail.xMin, rail.yMin, rail.xMax, rail.yMax, problems);
        }

        int rows = 0;
        for (int i = 0; i < view.RowsContainer.childCount; i++)
        {
            Transform child = view.RowsContainer.GetChild(i);
            if (!child.gameObject.activeSelf || !child.TryGetComponent(out KeyBindingRowView row))
            {
                continue;
            }

            rows++;
            Rect rowRect = RectOf((RectTransform)row.transform, root);
            Rect reset = RectOf((RectTransform)row.ResetButton.transform, root);
            if (!row.IsResetShown)
            {
                problems.Add($"{where}: row {row.Label.text} hides its Reset though it was changed.");
            }

            if (rowRect.xMin < list.xMin - 0.01f || reset.xMax > list.xMax - OptionsMenuView.ScrollbarRoom + 0.01f)
            {
                problems.Add($"{where}: row {row.Label.text} spans {rowRect.xMin} to {reset.xMax}, outside the pane's {list.xMin} to {list.xMax - OptionsMenuView.ScrollbarRoom}.");
            }

            CheckWhole(where, $"row {row.Label.text}", rowRect, problems);
        }

        if (rows == 0)
        {
            problems.Add($"{where}: no row shows.");
        }

        Rect back = RectOf((RectTransform)view.BackButton.transform, root);
        Rect resetCategory = RectOf((RectTransform)view.CategoryResetButton.transform, root);
        if (!view.IsCategoryResetShown)
        {
            problems.Add($"{where}: the category's reset hides though its rows changed.");
        }

        CheckInside(where, "Back", back, 0f, contentBottom, area.width, area.height, problems);
        CheckInside(where, "the category's reset", resetCategory, 0f, contentBottom, back.xMin, area.height, problems);
        CheckInside(where, "the notice line", RectOf(view.NoticeText.rectTransform, root), 0f, contentBottom, area.width, back.yMin, problems);
        CheckInside(where, "the warning", RectOf(view.WarningText.rectTransform, root), 0f, contentBottom, area.width, back.yMin, problems);
        Rect strip = RectOf((RectTransform)view.Vitals.transform, root);
        CheckInside(where, "the vitals strip", strip, 0f, 0f, area.width, OptionsMenuView.ContentTop, problems);
        if (strip.xMax != list.xMax)
        {
            problems.Add($"{where}: the vitals strip ends at {strip.xMax}, not on the pane's right edge {list.xMax}.");
        }

        float sessionLeft = strip.xMax - 90f - view.Vitals.Session.GetPreferredValues(view.Vitals.Session.text).x;
        TMP_Text title = screen.transform.Find("Panel/Title").GetComponent<TMP_Text>();
        float titleRight = area.width * 0.5f + title.GetPreferredValues(title.text).x * 0.5f;
        if (sessionLeft < titleRight)
        {
            problems.Add($"{where}: the session line starts at {sessionLeft}, over the title.");
        }

        TearDown();
    }

    /// <summary>
    /// Adds a problem when <paramref name="rect"/> leaves the box from (<paramref name="left"/>, <paramref name="top"/>)
    /// to (<paramref name="right"/>, <paramref name="bottom"/>), or does not stand on whole UI px.
    /// </summary>
    private static void CheckInside(string where, string what, Rect rect, float left, float top, float right, float bottom, List<string> problems)
    {
        if (rect.xMin < left - 0.01f || rect.yMin < top - 0.01f || rect.xMax > right + 0.01f || rect.yMax > bottom + 0.01f)
        {
            problems.Add($"{where}: {what} spans {rect}, outside ({left}, {top}) to ({right}, {bottom}).");
        }

        CheckWhole(where, what, rect, problems);
    }

    /// <summary>
    /// Adds a problem when <paramref name="rect"/> does not stand on whole UI px.
    /// </summary>
    private static void CheckWhole(string where, string what, Rect rect, List<string> problems)
    {
        if (Mathf.Abs(rect.xMin - Mathf.Round(rect.xMin)) > 0.01f || Mathf.Abs(rect.yMin - Mathf.Round(rect.yMin)) > 0.01f)
        {
            problems.Add($"{where}: {what} is not on whole UI px at {rect.position}.");
        }
    }

    /// <summary>
    /// <paramref name="rect"/>'s rectangle in UI px from <paramref name="root"/>'s top left, y going down.
    /// </summary>
    private static Rect RectOf(RectTransform rect, RectTransform root)
    {
        Vector3[] corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        Rect area = root.rect;
        Vector2 bottomLeft = root.InverseTransformPoint(corners[0]);
        Vector2 topRight = root.InverseTransformPoint(corners[2]);
        return Rect.MinMaxRect(bottomLeft.x - area.xMin, area.yMax - topRight.y, topRight.x - area.xMin, area.yMax - bottomLeft.y);
    }
}
