using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The HUD's tooltip (Docs/DesignSystem.md, Components: Tooltip): a raised panel with a title and a description, shown
/// by a <see cref="TooltipTrigger"/> just above the slot or icon it describes and kept inside its canvas, on whole UI px.
/// Hidden until shown, and never a raycast target, so it cannot take the pointer from what it describes. Local to this
/// machine.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public class HudTooltip : MonoBehaviour
{
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text descriptionText;
    [SerializeField, Min(0f), Tooltip("UI px between the tooltip and what it describes.")]
    private float gap = 2f;

    private RectTransform target;

    public bool IsShown => gameObject.activeSelf;
    internal RectTransform Target => target;
    internal string Title => titleText != null ? titleText.text : null;
    internal string Description => descriptionText != null ? descriptionText.text : null;

    /// <summary>
    /// Logs an error when the title or description text is missing.
    /// </summary>
    private void Awake()
    {
        if (titleText == null || descriptionText == null)
        {
            Debug.LogError($"{name} is missing its title or description text.", this);
        }
    }

    /// <summary>
    /// Shows <paramref name="title"/> and <paramref name="description"/> above <paramref name="over"/>.
    /// </summary>
    public void Show(RectTransform over, string title, string description)
    {
        target = over;
        if (titleText != null)
        {
            titleText.text = title;
        }

        if (descriptionText != null)
        {
            descriptionText.text = description;
        }

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
    internal void ConfigureReferences(TMP_Text title, TMP_Text description)
    {
        titleText = title;
        descriptionText = description;
    }

    /// <summary>
    /// Lays the tooltip out at its content's size and puts it <see cref="gap"/> above the middle of the target's top
    /// edge, moved inside its parent where it would leave it, on whole UI px from the parent's corner.
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
        Vector2 size = box.rect.size;
        Vector2 room = parent.rect.size;
        float x = Mathf.Clamp(top.x - size.x * 0.5f, 0f, Mathf.Max(0f, room.x - size.x));
        float y = Mathf.Clamp(top.y + gap, 0f, Mathf.Max(0f, room.y - size.y));
        box.anchoredPosition = new Vector2(Mathf.Round(x), Mathf.Round(y));
    }
}
