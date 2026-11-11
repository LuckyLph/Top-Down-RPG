using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The HUD's tooltip (Docs/DesignSystem.md, Components: Tooltip): a raised panel with a title, a line of numbers and a
/// description, shown by a <see cref="TooltipTrigger"/> just above the slot or icon it describes (below it where there
/// is no room above) and kept inside its canvas, on whole UI px. The numbers and the description hide when empty. Hidden until shown, and never a raycast
/// target, so it cannot take the pointer from what it describes. Local to this machine.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public class HudTooltip : MonoBehaviour
{
    [SerializeField] private TMP_Text titleText;
    [SerializeField, Tooltip("The numbers under the title: an ability's cooldown, cast time and range, a weapon's damage and range.")]
    private TMP_Text detailsText;
    [SerializeField] private TMP_Text descriptionText;
    [SerializeField, Min(0f), Tooltip("UI px between the tooltip and what it describes.")]
    private float gap = 2f;

    private RectTransform target;

    public bool IsShown => gameObject.activeSelf;
    internal RectTransform Target => target;
    internal string Title => titleText != null ? titleText.text : null;
    internal string Details => detailsText != null && detailsText.gameObject.activeSelf ? detailsText.text : null;
    internal string Description => descriptionText != null && descriptionText.gameObject.activeSelf ? descriptionText.text : null;

    /// <summary>
    /// Logs an error when the title, numbers or description text is missing.
    /// </summary>
    private void Awake()
    {
        if (titleText == null || detailsText == null || descriptionText == null)
        {
            Debug.LogError($"{name} is missing its title, details or description text.", this);
        }
    }

    /// <summary>
    /// Shows <paramref name="title"/>, <paramref name="details"/> and <paramref name="description"/> above
    /// <paramref name="over"/>, leaving out the numbers or the description when empty.
    /// </summary>
    public void Show(RectTransform over, string title, string details, string description)
    {
        target = over;
        if (titleText != null)
        {
            titleText.text = title;
        }

        SetLine(detailsText, details);
        SetLine(descriptionText, description);
        gameObject.SetActive(true);
        Place();
    }

    /// <summary>
    /// Hides the tooltip when it shows <paramref name="over"/>'s; another object's tooltip stays.
    /// </summary>
    public void Hide(RectTransform over)
    {
        if (target != over)
        {
            return;
        }

        target = null;
        gameObject.SetActive(false);
    }

    /// <summary>
    /// Assigns the texts in code, for tests that build the tooltip without a prefab.
    /// </summary>
    internal void ConfigureReferences(TMP_Text title, TMP_Text details, TMP_Text description)
    {
        titleText = title;
        detailsText = details;
        descriptionText = description;
    }

    /// <summary>
    /// Shows <paramref name="value"/> on <paramref name="line"/>, or hides the line when it is empty, so the layout
    /// leaves no gap for it.
    /// </summary>
    private static void SetLine(TMP_Text line, string value)
    {
        if (line == null)
        {
            return;
        }

        bool shown = !string.IsNullOrEmpty(value);
        if (shown)
        {
            line.text = value;
        }

        line.gameObject.SetActive(shown);
    }

    /// <summary>
    /// Lays the tooltip out at its content's size and puts it <see cref="gap"/> above the middle of the target's top
    /// edge, or below its bottom edge when there is no room above (the weapon slot in the top corner), moved inside its
    /// parent where it would leave it, on whole UI px from the parent's corner.
    /// </summary>
    private void Place()
    {
        RectTransform box = (RectTransform)transform;
        RectTransform parent = box.parent as RectTransform;
        if (parent == null || target == null)
        {
            return;
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(box);
        box.anchorMin = Vector2.zero;
        box.anchorMax = Vector2.zero;
        box.pivot = Vector2.zero;
        Rect over = target.rect;
        Vector2 top = (Vector2)parent.InverseTransformPoint(target.TransformPoint(new Vector3(over.center.x, over.yMax, 0f))) - parent.rect.min;
        Vector2 bottom = (Vector2)parent.InverseTransformPoint(target.TransformPoint(new Vector3(over.center.x, over.yMin, 0f))) - parent.rect.min;
        Vector2 size = box.rect.size;
        Vector2 room = parent.rect.size;
        float x = Mathf.Clamp(top.x - size.x * 0.5f, 0f, Mathf.Max(0f, room.x - size.x));
        float y = top.y + gap + size.y <= room.y ? top.y + gap : bottom.y - gap - size.y;
        y = Mathf.Clamp(y, 0f, Mathf.Max(0f, room.y - size.y));
        box.anchoredPosition = new Vector2(Mathf.Round(x), Mathf.Round(y));
    }
}
