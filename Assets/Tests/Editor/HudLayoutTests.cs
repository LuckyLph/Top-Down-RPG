using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Tests for the HUD's screen regions (Docs/DesignSystem.md, Foundations: Screen regions; Docs/UiRevamp.md, Phases 2 and
/// 3), on a copy of <c>PlayerHudCanvas</c> laid out at a canvas size, every status icon, the message line and a notice
/// long enough to fill both of its lines shown: the notice line keeps to the notice band at the top centre and
/// everything else the HUD draws, its tooltip aside, to the command bar's region at the bottom centre, at 480 x 270 and
/// at the UI size of every display in the Scale table, every size step included, and the four regions never overlap
/// there.
/// </summary>
public class HudLayoutTests
{
    private const string HudPath = "Assets/Prefabs/UI/PlayerHudCanvas.prefab";
    private const float CommandBarWidth = 378f;
    private const float CommandBarTop = 68f;
    private const float Margin = 8f;
    private const float NoticeWidth = 240f;
    private const float NoticeHeight = 24f;
    private const float PartyWidth = 104f;
    private const float PartyHeight = 82f;
    private const string LongNotice = "A notice long enough to fill the notice line: it wraps to a second line, then stops at the line's width and its two lines.";

    private GameObject hud;

    /// <summary>
    /// Destroys the HUD copy.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        if (hud != null)
        {
            Object.DestroyImmediate(hud);
        }
    }

    /// <summary>
    /// At 480 x 270, the smallest layout, every graphic of the HUD but its tooltip and its notice line lies in the command
    /// bar's region, 378 UI px wide at the bottom centre and 68 tall, and the notice line, at its widest and tallest, in
    /// the notice band, 240 x 24 UI px 8 below the top edge, both clear of the party frames and the top right.
    /// </summary>
    [Test]
    public void AtTheSmallestLayout_TheHudKeepsToTheCommandBarsRegion_AndTheNoticeToItsBand()
    {
        List<string> problems = new();
        CheckAt(480, 270, problems);

        Assert.That(problems, Is.Empty, string.Join("\n", problems));
    }

    /// <summary>
    /// At the UI size of every display in the Scale table, at every size step it fits, the HUD keeps to the command
    /// bar's region, its notice line to the notice band, and the regions do not overlap.
    /// </summary>
    [Test]
    public void AtEveryDisplay_TheHudKeepsToItsRegion_AndTheRegionsDoNotOverlap()
    {
        UiScaleSettings settings = AssetDatabase.LoadAssetAtPath<UiScaleSettings>("Assets/Data/UI/UiScale.asset");
        List<string> problems = new();
        HashSet<Vector2Int> sizes = new();
        foreach (int[] display in UiScaleTests.Displays)
        {
            for (int step = 0; step <= UiScale.StepsFor(display[0], display[1], settings); step++)
            {
                int zoom = UiScale.ZoomFor(display[0], display[1], step, settings);
                sizes.Add(new Vector2Int(display[0] / zoom, display[1] / zoom));
            }
        }

        foreach (Vector2Int size in sizes)
        {
            CheckAt(size.x, size.y, problems);
        }

        Assert.That(sizes.Count, Is.GreaterThan(5));
        Assert.That(problems, Is.Empty, string.Join("\n", problems));
    }

    /// <summary>
    /// Lays the HUD out on a <paramref name="width"/> x <paramref name="height"/> UI px canvas with everything shown,
    /// and adds to <paramref name="problems"/> each graphic of the notice line outside the notice band, each other
    /// graphic outside the command bar's region and each pair of regions that overlap.
    /// </summary>
    private void CheckAt(int width, int height, List<string> problems)
    {
        RectTransform root = Layout(width, height);
        Rect commandBar = new((width - CommandBarWidth) * 0.5f, 0f, CommandBarWidth, CommandBarTop);
        Rect notice = new((width - NoticeWidth) * 0.5f, height - Margin - NoticeHeight, NoticeWidth, NoticeHeight);
        Rect party = new(Margin, height - Margin - PartyHeight, PartyWidth, PartyHeight);
        Rect topRight = new(width - Margin - PartyWidth, height - Margin - PartyHeight, PartyWidth, PartyHeight);
        string at = $"{width} x {height}";
        foreach ((string name, Rect region) in new[] { ("notice band", notice), ("party frames", party), ("top right", topRight) })
        {
            if (region.Overlaps(commandBar))
            {
                problems.Add($"{at}: the command bar overlaps the {name}.");
            }
        }

        if (party.Overlaps(notice) || topRight.Overlaps(notice))
        {
            problems.Add($"{at}: the notice band overlaps a top corner.");
        }

        HudTooltip tooltip = root.GetComponentInChildren<HudTooltip>(true);
        NoticeView noticeLine = root.GetComponentInChildren<NoticeView>(true);
        int checkedGraphics = 0;
        int noticeGraphics = 0;
        foreach (Graphic graphic in root.GetComponentsInChildren<Graphic>(false))
        {
            if (graphic.transform.IsChildOf(tooltip.transform))
            {
                continue;
            }

            Rect rect = Bounds(graphic.rectTransform, root);
            checkedGraphics++;
            bool inNotice = graphic.transform.IsChildOf(noticeLine.transform);
            noticeGraphics += inNotice ? 1 : 0;
            Rect region = inNotice ? notice : commandBar;
            if (rect.xMin < region.xMin - 0.01f || rect.xMax > region.xMax + 0.01f || rect.yMin < region.yMin - 0.01f || rect.yMax > region.yMax + 0.01f)
            {
                problems.Add($"{at}: {graphic.name} at {rect} leaves its region {region}.");
            }
        }

        if (checkedGraphics < 40)
        {
            problems.Add($"{at}: only {checkedGraphics} graphics were laid out.");
        }

        if (noticeGraphics < 3 || noticeLine.Label.textInfo.lineCount != 2)
        {
            problems.Add($"{at}: the notice line showed {noticeGraphics} graphics and {noticeLine.Label.textInfo.lineCount} lines, not a panel, its frame and two lines of text.");
        }

        Object.DestroyImmediate(hud);
        hud = null;
    }

    /// <summary>
    /// A copy of the HUD on a world-space canvas of <paramref name="width"/> x <paramref name="height"/> UI px, its
    /// message line, every status icon and a notice filling the notice line shown and its layout groups laid out;
    /// returns its root.
    /// </summary>
    private RectTransform Layout(int width, int height)
    {
        hud = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(HudPath));
        hud.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        RectTransform root = (RectTransform)hud.transform;
        root.anchorMin = new Vector2(0.5f, 0.5f);
        root.anchorMax = new Vector2(0.5f, 0.5f);
        root.sizeDelta = new Vector2(width, height);
        root.localScale = Vector3.one;
        hud.GetComponentInChildren<AbilityBarView>(true).MessageText.gameObject.SetActive(true);
        foreach (StatusIconView icon in hud.GetComponentsInChildren<StatusIconView>(true))
        {
            icon.gameObject.SetActive(true);
        }

        hud.GetComponentInChildren<NoticeView>(true).Show(LongNotice);

        foreach (LayoutGroup group in hud.GetComponentsInChildren<LayoutGroup>(true))
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)group.transform);
        }

        Canvas.ForceUpdateCanvases();
        return root;
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
