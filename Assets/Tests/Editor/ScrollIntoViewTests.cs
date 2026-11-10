using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Tests for <see cref="ScrollIntoView"/> (Docs/DesignSystem.md, Phase 3): a list 100 UI px tall of ten rows 20 tall
/// scrolls by the least that shows a row whole, and not at all for a row already shown.
/// </summary>
public class ScrollIntoViewTests
{
    private GameObject list;
    private ScrollRect scroll;
    private RectTransform content;
    private RectTransform[] rows;

    /// <summary>
    /// Builds the list: a viewport over a content of ten rows, each 20 UI px tall, the first at the top.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        list = new GameObject("List", typeof(RectTransform));
        ((RectTransform)list.transform).sizeDelta = new Vector2(100f, 100f);
        RectTransform viewport = Child(list.transform, "Viewport");
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.pivot = new Vector2(0f, 1f);
        viewport.sizeDelta = Vector2.zero;
        content = Child(viewport, "Content");
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = Vector2.one;
        content.pivot = new Vector2(0.5f, 1f);
        content.sizeDelta = new Vector2(0f, 200f);
        rows = new RectTransform[10];
        for (int i = 0; i < rows.Length; i++)
        {
            rows[i] = Child(content, "Row" + i);
            rows[i].anchorMin = new Vector2(0f, 1f);
            rows[i].anchorMax = Vector2.one;
            rows[i].pivot = new Vector2(0.5f, 1f);
            rows[i].anchoredPosition = new Vector2(0f, -20f * i);
            rows[i].sizeDelta = new Vector2(0f, 20f);
        }

        scroll = list.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        content.anchoredPosition = Vector2.zero;
    }

    /// <summary>
    /// Destroys the list.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(list);
    }

    /// <summary>
    /// A row below the viewport scrolls up until its bottom meets the viewport's; a row above scrolls down until its
    /// top meets the viewport's; a row already shown moves nothing.
    /// </summary>
    [Test]
    public void ARow_ScrollsIntoViewByTheLeast()
    {
        ScrollIntoView.Reveal(scroll, rows[7]);
        Assert.That(content.anchoredPosition.y, Is.EqualTo(60f).Within(0.001f), "Row 7's bottom, 160 down, meets the viewport's, 100 down.");

        ScrollIntoView.Reveal(scroll, rows[1]);
        Assert.That(content.anchoredPosition.y, Is.EqualTo(20f).Within(0.001f), "Row 1's top, 20 down, meets the viewport's top.");

        ScrollIntoView.Reveal(scroll, rows[3]);
        Assert.That(content.anchoredPosition.y, Is.EqualTo(20f).Within(0.001f), "Row 3 is already shown.");
    }

    /// <summary>
    /// A new UI object under <paramref name="parent"/>.
    /// </summary>
    private static RectTransform Child(Transform parent, string name)
    {
        GameObject child = new(name, typeof(RectTransform));
        child.transform.SetParent(parent, false);
        return (RectTransform)child.transform;
    }
}
