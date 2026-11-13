using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Tests for the Character screen's layout (Docs/UiRevamp.md, Phase 7) at the UI size of every display in the Scale
/// table, every size step included: the preview's box at the largest whole multiple of the stage's art pixels that
/// fits (x3 at 640 x 360, x2 at 480 x 270), its controls under it, the rows beside it with every row of the most worn
/// outfit, the footer, the foot line, the vitals strip and the swatch picker opened on the lowest chip all inside the
/// screen and clear of each other, on whole UI px.
/// </summary>
public class CharacterScreenLayoutTests
{
    private const string ScreenPath = "Assets/Prefabs/UI/CharacterScreen.prefab";

    private GameObject canvas;
    private GameObject eventObject;
    private CharacterScreenController controller;

    /// <summary>
    /// Destroys the screen and the event system.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        controller?.Dispose();
        controller = null;
        if (canvas != null)
        {
            Object.DestroyImmediate(canvas);
        }

        if (eventObject != null)
        {
            Object.DestroyImmediate(eventObject);
        }
    }

    /// <summary>
    /// The multiple is x3 at 640 x 360 and x2 at 480 x 270, and every display's screen keeps its parts inside it and
    /// apart.
    /// </summary>
    [Test]
    public void AtEveryDisplay_TheScreenFits()
    {
        Assert.That(CharacterScreenView.MultipleFor(640f, 360f), Is.EqualTo(3));
        Assert.That(CharacterScreenView.MultipleFor(480f, 270f), Is.EqualTo(2));
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
            CheckAt(size.x, size.y, problems);
        }

        Assert.That(sizes, Does.Contain(new Vector2Int(480, 270)));
        Assert.That(problems, Is.Empty, string.Join("\n", problems));
    }

    /// <summary>
    /// Lays the screen out on a <paramref name="width"/> x <paramref name="height"/> UI px parent, every part worn and the
    /// vitals strip shown, opens the picker on the Bag row's last chip, and checks each part.
    /// </summary>
    private void CheckAt(int width, int height, List<string> problems)
    {
        eventObject = new GameObject("EventSystem");
        EventSystem events = eventObject.AddComponent<EventSystem>();
        canvas = new GameObject("Canvas", typeof(RectTransform));
        ((RectTransform)canvas.transform).sizeDelta = new Vector2(width, height);
        GameObject screen = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ScreenPath), canvas.transform);
        CharacterScreenView view = screen.GetComponent<CharacterScreenView>();
        UserSettings settings = new(new MemorySaveStore());
        settings.SetCharacterPart("Headwear", "Hood");
        settings.SetCharacterPart("Cape", "Cape");
        settings.SetCharacterPart("Hands", "Gloves");
        settings.SetCharacterPart("Bag", "Satchel");
        controller = new CharacterScreenController(view, new FakeCharacterPreview(), new MenuStack(new FakeMenuInput()), new UISelection(events), settings);
        controller.Start();
        controller.Open(null);
        view.Vitals.SetShown(true);
        view.Vitals.SetSession(VitalsStripPresenter.SessionLine);
        view.Vitals.SetHealth(100, 100);
        view.Place();
        string where = $"{width} x {height}";
        RectTransform root = (RectTransform)screen.transform;
        Rect area = root.rect;
        Rect box = RectOf(view.PreviewBox, root);
        Rect controls = RectOf(view.Controls, root);
        Rect rows = RectOf(view.Rows, root);
        int expected = CharacterScreenView.MultipleFor(width, height);
        if (box.width != CharacterPreviewStage.Size * expected + 2f || view.Multiple != expected)
        {
            problems.Add($"{where}: the box is {box.width} wide at x{view.Multiple}, not x{expected}.");
        }

        float contentBottom = area.height - CharacterScreenView.ContentBottom;
        CheckInside(where, "the box", box, 0f, CharacterScreenView.ContentTop, area.width, contentBottom, problems);
        CheckInside(where, "the preview's controls", RectOf((RectTransform)view.TurnLeftButton.transform, root), controls.xMin, controls.yMin, controls.xMax, contentBottom, problems);
        CheckInside(where, "the preview's controls", RectOf((RectTransform)view.TurnRightButton.transform, root), controls.xMin, controls.yMin, controls.xMax, contentBottom, problems);
        CheckInside(where, "the rows", rows, box.xMax + 1f, CharacterScreenView.ContentTop, area.width, contentBottom, problems);
        for (int i = 0; i < controller.RowCount; i++)
        {
            RowChipsView row = controller.GetRow(i);
            if (!row.gameObject.activeSelf)
            {
                continue;
            }

            CheckInside(where, $"row {row.Label.text}", RectOf((RectTransform)row.transform, root), rows.xMin, rows.yMin, rows.xMax, rows.yMax, problems);
            CheckInside(where, $"row {row.Label.text}'s Reset", RectOf((RectTransform)row.ResetButton.transform, root), rows.xMin, rows.yMin, rows.xMax, rows.yMax, problems);
        }

        Rect back = RectOf((RectTransform)view.BackButton.transform, root);
        Rect resetAll = RectOf((RectTransform)view.ResetAllButton.transform, root);
        CheckInside(where, "Back", back, 0f, contentBottom, area.width, area.height, problems);
        CheckInside(where, "Reset all", resetAll, 0f, contentBottom, area.width, area.height, problems);
        CheckInside(where, "the foot line", RectOf(view.FocusLine.rectTransform, root), 0f, contentBottom, area.width, resetAll.yMin, problems);
        Rect strip = RectOf((RectTransform)view.Vitals.transform, root);
        CheckInside(where, "the vitals strip", strip, 0f, 0f, area.width, CharacterScreenView.ContentTop, problems);
        float sessionLeft = strip.xMax - 90f - view.Vitals.Session.GetPreferredValues(view.Vitals.Session.text).x;
        TMPro.TMP_Text title = screen.transform.Find("Panel/Title").GetComponent<TMPro.TMP_Text>();
        float titleRight = area.width * 0.5f + title.GetPreferredValues(title.text).x * 0.5f;
        if (sessionLeft < titleRight)
        {
            problems.Add($"{where}: the session line starts at {sessionLeft}, over the title.");
        }

        RowChipsView bag = controller.GetRow(controller.RowCount - 1);
        bag.GetChip(1).Button.onClick.Invoke();
        Rect picker = RectOf((RectTransform)view.Picker.transform, root);
        CheckInside(where, "the picker", picker, 0f, CharacterScreenView.ContentTop, area.width, contentBottom, problems);
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
