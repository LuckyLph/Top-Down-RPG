using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Tests for the HUD's screen regions (Docs/DesignSystem.md, Foundations: Screen regions; Docs/UiRevamp.md, Phase 2), on
/// a copy of <c>PlayerHudCanvas</c> laid out at a canvas size, every status icon and the message line shown: everything
/// the HUD draws, its tooltip aside, keeps to the command bar's region at the bottom centre at 480 x 270 and at the UI
/// size of every display in the Scale table, every size step included, and the four regions never overlap there.
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
    /// At 480 x 270, the smallest layout, every graphic of the HUD but its tooltip lies in the command bar's region,
    /// 378 UI px wide at the bottom centre and 68 tall, clear of the notice band, the party frames and the top right.
    /// </summary>
    [Test]
    public void AtTheSmallestLayout_TheHudKeepsToTheCommandBarsRegion()
    {
        List<string> problems = new();
        CheckAt(480, 270, problems);

        Assert.That(problems, Is.Empty, string.Join("\n", problems));
    }

    /// <summary>
    /// At the UI size of every display in the Scale table, at every size step it fits, the HUD keeps to the command
    /// bar's region and the regions do not overlap.
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
    /// and adds to <paramref name="problems"/> each graphic outside the command bar's region and each pair of regions
    /// that overlap.
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
        int checkedGraphics = 0;
        foreach (Graphic graphic in root.GetComponentsInChildren<Graphic>(false))
        {
            if (graphic.transform.IsChildOf(tooltip.transform))
            {
                continue;
            }

            Rect rect = Bounds(graphic.rectTransform, root);
            checkedGraphics++;
            if (rect.xMin < commandBar.xMin - 0.01f || rect.xMax > commandBar.xMax + 0.01f || rect.yMin < commandBar.yMin - 0.01f || rect.yMax > commandBar.yMax + 0.01f)
            {
                problems.Add($"{at}: {graphic.name} at {rect} leaves the command bar's region {commandBar}.");
            }
        }

        if (checkedGraphics < 40)
        {
            problems.Add($"{at}: only {checkedGraphics} graphics were laid out.");
        }

        Object.DestroyImmediate(hud);
        hud = null;
    }

    /// <summary>
    /// A copy of the HUD on a world-space canvas of <paramref name="width"/> x <paramref name="height"/> UI px, its
    /// message line and every status icon shown and its layout groups laid out; returns its root.
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
