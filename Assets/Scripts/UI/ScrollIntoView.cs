using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// On a selectable inside a <see cref="ScrollRect"/>'s content: when it is selected, by keyboard, gamepad, mouse or code,
/// scrolls the list just enough to show it whole (Docs/DesignSystem.md, Phase 3).
/// </summary>
[DisallowMultipleComponent]
public sealed class ScrollIntoView : MonoBehaviour, ISelectHandler
{
    private ScrollRect scroll;

    /// <summary>
    /// Finds the list it scrolls.
    /// </summary>
    private void Awake()
    {
        scroll = GetComponentInParent<ScrollRect>(true);
    }

    /// <summary>
    /// Scrolls its list to show it.
    /// </summary>
    public void OnSelect(BaseEventData eventData)
    {
        if (scroll != null)
        {
            Reveal(scroll, (RectTransform)transform);
        }
    }

    /// <summary>
    /// Moves <paramref name="list"/>'s content vertically by the least that puts <paramref name="target"/> inside its
    /// viewport, top first when it is taller than the viewport.
    /// </summary>
    public static void Reveal(ScrollRect list, RectTransform target)
    {
        RectTransform content = list.content;
        if (content == null)
        {
            return;
        }

        RectTransform viewport = list.viewport != null ? list.viewport : (RectTransform)list.transform;
        Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(viewport, target);
        Rect view = viewport.rect;
        float shift = 0f;
        if (bounds.max.y > view.yMax)
        {
            shift = bounds.max.y - view.yMax;
        }
        else if (bounds.min.y < view.yMin)
        {
            shift = Mathf.Max(bounds.min.y - view.yMin, bounds.max.y - view.yMax);
        }

        if (shift != 0f)
        {
            list.StopMovement();
            content.anchoredPosition -= new Vector2(0f, shift);
        }
    }
}
