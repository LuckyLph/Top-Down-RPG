using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Tests for the main menu's, Play together's and Saved games' layout (Docs/UiRevamp.md, Phases 9 and 10) at the UI size
/// of every display in the Scale table, every size step included: the title view covering the canvas (Phase 11); the
/// menu's panel on the left with the logo and every button inside it, Saved games included, and the version clear of it;
/// the first run's Welcome panel; Play together's two columns centred, full (a save's line, Host
/// another saved game, two addresses, five recent hosts) and clear of a two-line notice over Back; the host-gone
/// dialog's box; and Saved games' list of eight cards centred above a two-line notice and Back, with its Delete
/// confirmation's box; all inside the screen and on whole UI px.
/// </summary>
public class MainMenuLayoutTests
{
    private MenuTestRig rig;
    private MainMenuTestScreen screen;

    /// <summary>
    /// Disposes the screen and the rig.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        screen?.Dispose();
        screen = null;
        rig?.Dispose();
        rig = null;
    }

    /// <summary>
    /// Every display's main menu, Play together, dialog and Saved games keep their parts inside the screen and apart,
    /// 480 x 270 among them.
    /// </summary>
    [Test]
    public void AtEveryDisplay_TheMenuPlayTogetherAndSavedGamesFit()
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
    /// Lays the menu out on a <paramref name="width"/> x <paramref name="height"/> UI px parent with eight saves, then Play
    /// together with its columns full and a two-line notice, then the host-gone dialog, then Saved games with a two-line
    /// notice and its Delete confirmation, and checks each part.
    /// </summary>
    private void CheckAt(int width, int height, List<string> problems)
    {
        rig = new MenuTestRig();
        for (int i = 0; i < 8; i++)
        {
            rig.WallClock.Advance(60d);
            rig.WriteSave(MenuTestRig.NearNorth, 3d * 3600d + 12d * 60d + i, $"save-{i}");
        }

        rig.AddAddresses();
        for (int i = 0; i < UserSettings.MaxRecentHosts; i++)
        {
            rig.Settings.AddRecentHost($"192.168.1.{20 + i}:7778");
        }

        screen = new MainMenuTestScreen(rig, width, height);
        screen.Start();
        string where = $"{width} x {height}";
        Rect area = screen.Area.rect;
        Rect panel = screen.RectOf(screen.Menu.Panel);
        CheckInside(where, "the menu's panel", panel, 0f, 0f, area.width, area.height, problems);
        if (panel.xMin != MainMenuController.PanelLeft)
        {
            problems.Add($"{where}: the menu's panel starts at {panel.xMin}, not {MainMenuController.PanelLeft}.");
        }

        foreach (Transform child in screen.Menu.Panel)
        {
            if (child.gameObject.activeSelf && child.TryGetComponent(out Selectable selectable))
            {
                CheckInside(where, child.name, screen.RectOf((RectTransform)child), panel.xMin, panel.yMin, panel.xMax, panel.yMax, problems);
            }
        }

        Rect version = screen.RectOf(screen.Menu.VersionText.rectTransform);
        CheckInside(where, "the version", version, panel.xMax, 0f, area.width, area.height, problems);
        Rect logo = screen.RectOf((RectTransform)screen.Menu.Panel.Find("Logo"));
        CheckInside(where, "the logo", logo, panel.xMin, panel.yMin, panel.xMax, panel.yMax, problems);
        screen.TitleView.Place();
        Rect view = screen.RectOf(screen.TitleView.Picture.rectTransform);
        if (view.xMin > 0.01f || view.yMin > 0.01f || view.xMax < area.width - 0.01f || view.yMax < area.height - 0.01f)
        {
            problems.Add($"{where}: the title view spans {view}, short of the canvas.");
        }

        if (Mathf.Abs(view.xMin - Mathf.Round(view.xMin)) > 0.01f || Mathf.Abs(view.yMin - Mathf.Round(view.yMin)) > 0.01f)
        {
            problems.Add($"{where}: the title view is not on whole UI px at {view.position}.");
        }

        screen.Welcome.Open(null);
        Rect welcome = screen.RectOf(screen.WelcomePanel.Panel);
        CheckInside(where, "the Welcome panel", welcome, 0f, 0f, area.width, area.height, problems);
        foreach (KeyBindingRowView row in new[] { screen.WelcomePanel.UiSizeRow, screen.WelcomePanel.ReduceFlashingRow, screen.WelcomePanel.ReduceMotionRow })
        {
            CheckInside(where, $"the Welcome's {row.name}", screen.RectOf((RectTransform)row.transform), welcome.xMin, welcome.yMin, welcome.xMax, welcome.yMax, problems);
        }

        CheckInside(where, "the Welcome's description", screen.RectOf(screen.WelcomePanel.Description.rectTransform), welcome.xMin, welcome.yMin, welcome.xMax, welcome.yMax, problems);
        CheckInside(where, "the Welcome's Continue", screen.RectOf((RectTransform)screen.WelcomePanel.ContinueButton.transform), welcome.xMin, welcome.yMin, welcome.xMax, welcome.yMax, problems);
        screen.Welcome.HandleBack();

        screen.Menu.OpenPlayTogether();
        screen.Together.SetNotice(string.Format(MenuSessionStarter.UnreachableFormat, "100.101.102.103:7777", 10));
        screen.Together.Place();
        Rect notice = screen.RectOf(screen.Together.Notice.rectTransform);
        Rect back = screen.RectOf((RectTransform)screen.Together.BackButton.transform);
        float left = Mathf.Floor((area.width - PlayTogetherView.BlockWidth) * 0.5f);
        CheckColumn(where, "the host column", screen.Together.HostColumn, left, notice.yMin, problems);
        CheckColumn(where, "the join column", screen.Together.JoinColumn, left + PlayTogetherView.ColumnWidth + PlayTogetherView.ColumnGap, notice.yMin, problems);
        if (screen.Together.Notice.GetPreferredValues(screen.Together.Notice.text, notice.width, 0f).y > notice.height + 0.01f)
        {
            problems.Add($"{where}: the two-line notice does not fit the notice line.");
        }

        CheckInside(where, "the notice line", notice, 0f, 0f, area.width, back.yMin, problems);
        CheckInside(where, "Back", back, 0f, notice.yMax, area.width, area.height, problems);

        screen.Together.BackButton.onClick.Invoke();
        screen.Menu.ShowNotice(MenuNoticeKind.HostGone, "100.101.102.103:7777");
        Rect box = screen.RectOf(screen.Menu.Dialog.Box);
        CheckInside(where, "the dialog's box", box, 0f, 0f, area.width, area.height, problems);
        screen.Menu.HandleBack();

        CheckSavedGames(where, area, problems);
        TearDown();
    }

    /// <summary>
    /// Opens Saved games over eight saves and checks its list (in the 472 UI px block from 28 down, every card as wide as
    /// the list less the scrollbar's room), a two-line notice and Back under it, and the Delete confirmation's box.
    /// </summary>
    private void CheckSavedGames(string where, Rect area, List<string> problems)
    {
        screen.Menu.OpenSavedGames();
        SavedGamesView saved = screen.Saved;
        if (!saved.IsShown || saved.CardCount != 8)
        {
            problems.Add($"{where}: Saved games did not open on eight cards.");
            return;
        }

        saved.SetNotice(string.Format(MenuSessionStarter.HostFailedFormat, 7777));
        saved.Place();
        Rect list = screen.RectOf(saved.List);
        Rect notice = screen.RectOf(saved.Notice.rectTransform);
        Rect back = screen.RectOf((RectTransform)saved.BackButton.transform);
        float left = Mathf.Floor((area.width - SavedGamesView.BlockWidth) * 0.5f);
        if (list.xMin != left || list.yMin != SavedGamesView.ContentTop || list.width != SavedGamesView.BlockWidth)
        {
            problems.Add($"{where}: the saved games' list spans {list}, not {SavedGamesView.BlockWidth} wide from ({left}, {SavedGamesView.ContentTop}).");
        }

        CheckInside(where, "the saved games' list", list, 0f, 0f, area.width, notice.yMin, problems);
        if (saved.Notice.GetPreferredValues(saved.Notice.text, notice.width, 0f).y > notice.height + 0.01f)
        {
            problems.Add($"{where}: the saved games' two-line notice does not fit its notice line.");
        }

        CheckInside(where, "the saved games' notice line", notice, 0f, list.yMax, area.width, back.yMin, problems);
        CheckInside(where, "the saved games' Back", back, 0f, notice.yMax, area.width, area.height, problems);
        Rect card = screen.RectOf((RectTransform)saved.Card(0).transform);
        if (card.xMin != list.xMin || card.yMin != list.yMin || card.width != SavedGamesView.BlockWidth - 6f || card.height != 30f)
        {
            problems.Add($"{where}: the first card spans {card}, not 466 x 30 at the list's top left.");
        }

        Rect actions = screen.RectOf((RectTransform)saved.Card(0).Actions.transform);
        CheckInside(where, "the first card's actions", actions, card.xMin, card.yMin, card.xMax, card.yMax, problems);
        saved.Card(0).DeleteButton.onClick.Invoke();
        Rect box = screen.RectOf(saved.ConfirmBox);
        CheckInside(where, "the Delete confirmation's box", box, 0f, 0f, area.width, area.height, problems);
        if (box.height < 62f)
        {
            problems.Add($"{where}: the Delete confirmation's box is {box.height} UI px tall, too short for its two lines.");
        }
    }

    /// <summary>
    /// Adds problems when <paramref name="column"/> does not start at <paramref name="left"/> on whole UI px, or one of its
    /// shown parts leaves it or reaches past <paramref name="bottom"/>.
    /// </summary>
    private void CheckColumn(string where, string what, RectTransform column, float left, float bottom, List<string> problems)
    {
        Rect rect = screen.RectOf(column);
        if (rect.xMin != left || rect.yMin != PlayTogetherView.ContentTop)
        {
            problems.Add($"{where}: {what} starts at {rect.position}, not ({left}, {PlayTogetherView.ContentTop}).");
        }

        foreach (Transform child in column)
        {
            if (child.gameObject.activeSelf)
            {
                CheckInside(where, $"{what}'s {child.name}", screen.RectOf((RectTransform)child), rect.xMin, rect.yMin, rect.xMax, bottom, problems);
            }
        }
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
}
