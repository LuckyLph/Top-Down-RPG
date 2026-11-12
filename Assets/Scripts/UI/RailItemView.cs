using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// An item of the options screen's rail (Docs/UiRevamp.md, Phase 8): a button naming a category, its label left-aligned
/// after a pip's slot. The current category's item is raised, its face in ControlNormal on the raised panel's lip, with
/// the skin's pip; the others are flat in ControlSecondary, so shape and the pip mark the current one, not colour alone.
/// Selected, it takes Accent like every control.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Button))]
public sealed class RailItemView : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private TMP_Text label;
    [SerializeField, Tooltip("The skin's pip before the label, shown on the current category's item.")]
    private Image pip;
    [SerializeField, Tooltip("The control's theme, whose roles colour its face.")]
    private ThemedSelectable themed;
    [SerializeField, Tooltip("The current item's face: the raised panel, its face on a 2 px lip.")]
    private Sprite raised;
    [SerializeField, Tooltip("The other items' face: the flat panel.")]
    private Sprite flat;

    public Button Button => button;
    public bool IsCurrent { get; private set; }
    internal TMP_Text Label => label;
    internal Image Pip => pip;

    /// <summary>
    /// Logs an error when a reference is missing.
    /// </summary>
    private void Awake()
    {
        if (button == null || label == null || pip == null || themed == null || raised == null || flat == null)
        {
            Debug.LogError($"{name} is missing its button, label, pip, theme or faces.", this);
        }
    }

    /// <summary>
    /// Shows <paramref name="text"/>, the category's name.
    /// </summary>
    public void SetLabel(string text)
    {
        label.text = text;
    }

    /// <summary>
    /// Raises the item, in ControlNormal with its pip, while it is the <paramref name="current"/> category's, and lays it
    /// flat in ControlSecondary otherwise; its label stays centred on the face.
    /// </summary>
    public void SetCurrent(bool current)
    {
        IsCurrent = current;
        button.image.sprite = current ? raised : flat;
        button.colors = ThemedSelectable.ControlColours(themed.Theme, button.colors, current ? UiColour.ControlNormal : UiColour.ControlSecondary);
        pip.enabled = current;
        RectTransform rect = label.rectTransform;
        rect.offsetMin = new Vector2(rect.offsetMin.x, current ? 2f : 0f);
        rect.offsetMax = new Vector2(rect.offsetMax.x, 0f);
    }
}
