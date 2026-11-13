using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The first run's Welcome panel (Docs/UiRevamp.md, Phase 11): a raised panel at the centre over the title view, titled
/// "Welcome", holding the options screen's three Accessibility rows (UI size and the Reduce flashing and Reduce motion
/// toggles, their Reset columns gone), the focused row's description under them, and Continue. The panel is placed on
/// whole UI px as it shows and when the canvas changes size. Passive: <see cref="WelcomeController"/> fills it, wires it
/// and selects.
/// </summary>
[DisallowMultipleComponent]
public class WelcomeView : MonoBehaviour
{
    [SerializeField, Tooltip("The raised panel, hidden until the first run shows it.")] private RectTransform panel;
    [SerializeField] private KeyBindingRowView uiSizeRow;
    [SerializeField] private KeyBindingRowView reduceFlashingRow;
    [SerializeField] private KeyBindingRowView reduceMotionRow;
    [SerializeField, Tooltip("The focused row's description, a DescriptionLine.")] private TMP_Text description;
    [SerializeField] private Button continueButton;

    public KeyBindingRowView UiSizeRow => uiSizeRow;
    public KeyBindingRowView ReduceFlashingRow => reduceFlashingRow;
    public KeyBindingRowView ReduceMotionRow => reduceMotionRow;
    public Button ContinueButton => continueButton;
    public bool IsShown => panel != null && panel.gameObject.activeSelf;
    internal RectTransform Panel => panel;
    internal TMP_Text Description => description;

    /// <summary>
    /// Logs an error when a part is missing.
    /// </summary>
    private void Awake()
    {
        if (panel == null || uiSizeRow == null || reduceFlashingRow == null || reduceMotionRow == null || description == null || continueButton == null)
        {
            Debug.LogError($"{name} is missing one of its Welcome references.", this);
        }
    }

    /// <summary>
    /// Places the panel again when the canvas changes size (the UI size picked on it, the game's window).
    /// </summary>
    private void OnRectTransformDimensionsChange()
    {
        Place();
    }

    /// <summary>
    /// Shows or hides the panel; the component's own object stays active.
    /// </summary>
    public void SetShown(bool shown)
    {
        panel.gameObject.SetActive(shown);
        if (shown)
        {
            Place();
        }
    }

    /// <summary>
    /// Shows <paramref name="text"/> as the focused row's description.
    /// </summary>
    public void SetDescription(string text)
    {
        description.text = text ?? string.Empty;
    }

    /// <summary>
    /// While shown, centres the panel on the canvas on whole UI px and lays its rows out.
    /// </summary>
    internal void Place()
    {
        if (panel == null || !panel.gameObject.activeInHierarchy || !(transform is RectTransform root))
        {
            return;
        }

        Rect area = root.rect;
        Vector2 size = panel.sizeDelta;
        panel.anchorMin = new Vector2(0f, 1f);
        panel.anchorMax = new Vector2(0f, 1f);
        panel.pivot = new Vector2(0f, 1f);
        panel.anchoredPosition = new Vector2(Mathf.Floor((area.width - size.x) * 0.5f), -Mathf.Floor((area.height - size.y) * 0.5f));
        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)uiSizeRow.transform.parent);
    }
}
