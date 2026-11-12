using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Tests for the in-game menu's place (Docs/UiRevamp.md, Phase 6), on a copy of <c>GameMenuCanvas</c> laid out at the UI
/// size of every display in the Scale table, every size step included: the menu with every button shown, the Party page
/// at its tallest (four players and the host's two addresses) and the Controls page with its Chat row each stand at the
/// centre of the space between the notice band and the command bar's region, inside it, on whole UI px, with every
/// graphic of theirs inside their panel. Online the game stays in view around them, so they must never reach the HUD's
/// notice line or command bar.
/// </summary>
public class GameMenuLayoutTests
{
    private const string GameMenuCanvasPath = "Assets/Prefabs/UI/GameMenuCanvas.prefab";

    private GameObject canvas;

    /// <summary>
    /// Destroys the canvas copy.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        if (canvas != null)
        {
            Object.DestroyImmediate(canvas);
        }
    }

    /// <summary>
    /// At every display's UI size, the menu and both pages keep between the notice band and the command bar, centred,
    /// on whole UI px, their graphics inside their panels.
    /// </summary>
    [Test]
    public void AtEveryDisplay_TheMenuAndItsPages_KeepBetweenTheNoticeBandAndTheCommandBar()
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
            CheckAt(size.x, size.y, problems);
        }

        Assert.That(sizes, Does.Contain(new Vector2Int(480, 270)));
        Assert.That(problems, Is.Empty, string.Join("\n", problems));
    }

    /// <summary>
    /// Lays the menu out on a <paramref name="width"/> x <paramref name="height"/> UI px canvas and checks each page.
    /// </summary>
    private void CheckAt(int width, int height, List<string> problems)
    {
        canvas = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(GameMenuCanvasPath));
        canvas.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        RectTransform root = (RectTransform)canvas.transform;
        root.anchorMin = new Vector2(0.5f, 0.5f);
        root.anchorMax = new Vector2(0.5f, 0.5f);
        root.sizeDelta = new Vector2(width, height);
        root.localScale = Vector3.one;
        GameMenuView view = canvas.GetComponentInChildren<GameMenuView>(true);
        view.SetShown(true);
        view.SetButtons(true, true);

        PartyPageView party = view.PartyPage;
        for (int i = 0; i < party.RowCount; i++)
        {
            party.Row(i).ShowPlayer(i + 1, i == 0 ? PartyPagePresenter.Host : null);
            party.Row(i).ShowState(PartyFramesPresenter.InAnotherWorld);
        }

        party.SetRowCount(party.RowCount);
        party.ShowInvite(new[] { new HostAddress("100.101.102.103", HostAddresses.HomeNetwork), new HostAddress("100.101.102.104", HostAddresses.Tailscale) }, "port 7777", PartyPagePresenter.CopyLabel);
        view.ControlsPage.SetChatShown(true);

        string at = $"{width} x {height}";
        Check(view, GameMenuView.Page.Menu, view.MenuBox, width, height, $"{at}, menu", problems);
        Check(view, GameMenuView.Page.Party, party.Box, width, height, $"{at}, Party page", problems);
        Check(view, GameMenuView.Page.Controls, view.ControlsPage.Box, width, height, $"{at}, Controls page", problems);
        Object.DestroyImmediate(canvas);
        canvas = null;
    }

    /// <summary>
    /// Shows <paramref name="page"/> and adds to <paramref name="problems"/> where its panel, <paramref name="box"/>,
    /// leaves the space between the notice band and the command bar, stands off its centre or off whole UI px, or holds a
    /// graphic outside it.
    /// </summary>
    private static void Check(GameMenuView view, GameMenuView.Page page, RectTransform box, int width, int height, string at, List<string> problems)
    {
        view.ShowPage(page);
        foreach (LayoutGroup group in box.GetComponentsInChildren<LayoutGroup>(false))
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)group.transform);
        }

        view.Place();
        Canvas.ForceUpdateCanvases();
        RectTransform root = (RectTransform)view.transform;
        Rect panel = Bounds(box, root);
        float top = height - panel.yMax;
        float bottom = height - panel.yMin;
        if (top < GameMenuView.NoticeBandBottom - 0.01f || bottom > height - GameMenuView.CommandBarTop + 0.01f)
        {
            problems.Add($"{at}: the panel runs from {top} to {bottom} UI px down, outside {GameMenuView.NoticeBandBottom} to {height - GameMenuView.CommandBarTop}.");
        }

        float middle = (GameMenuView.NoticeBandBottom + height - GameMenuView.CommandBarTop) * 0.5f;
        if (Mathf.Abs((top + bottom) * 0.5f - middle) > 1f || Mathf.Abs(panel.center.x - width * 0.5f) > 1f)
        {
            problems.Add($"{at}: the panel at {panel} is off the centre of its space.");
        }

        if (!Whole(panel.xMin) || !Whole(panel.yMin) || !Whole(panel.width) || !Whole(panel.height))
        {
            problems.Add($"{at}: the panel at {panel} is off whole UI px.");
        }

        int graphics = 0;
        foreach (Graphic graphic in box.GetComponentsInChildren<Graphic>(false))
        {
            graphics++;
            Rect rect = Bounds(graphic.rectTransform, root);
            if (rect.xMin < panel.xMin - 0.01f || rect.xMax > panel.xMax + 0.01f || rect.yMin < panel.yMin - 0.01f || rect.yMax > panel.yMax + 0.01f)
            {
                problems.Add($"{at}: {graphic.name} at {rect} leaves its panel {panel}.");
            }
        }

        if (graphics < 10)
        {
            problems.Add($"{at}: only {graphics} graphics were laid out.");
        }
    }

    /// <summary>
    /// Whether <paramref name="value"/> is a whole number of UI px.
    /// </summary>
    private static bool Whole(float value)
    {
        return Mathf.Abs(value - Mathf.Round(value)) < 0.01f;
    }

    /// <summary>
    /// The rect of <paramref name="rect"/> in <paramref name="root"/>'s space, from its bottom left corner.
    /// </summary>
    private static Rect Bounds(RectTransform rect, RectTransform root)
    {
        Vector3[] corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        Vector2 min = (Vector2)root.InverseTransformPoint(corners[0]) - root.rect.min;
        Vector2 max = (Vector2)root.InverseTransformPoint(corners[2]) - root.rect.min;
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }
}
