using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A row of the Character screen (Docs/UiRevamp.md, Phase 7): a label, a button showing a value (the look, a part's
/// option), up to four swatch chips (the row's colours) and Reset. The value's column keeps its place while the value is
/// hidden (the Body row has none), so every row's chips line up.
/// </summary>
[DisallowMultipleComponent]
public sealed class RowChipsView : MonoBehaviour
{
    [SerializeField] private TMP_Text label;
    [SerializeField, Tooltip("The value's column, which keeps its width while the value is hidden.")]
    private LayoutElement valueSlot;
    [SerializeField] private Button valueButton;
    [SerializeField] private TMP_Text valueText;
    [SerializeField, Tooltip("The row's chips, in order; hidden past the colours the row shows.")]
    private SwatchChipView[] chips;
    [SerializeField] private Button resetButton;

    public Button ValueButton => valueButton;
    public Button ResetButton => resetButton;
    public int ChipCount => chips != null ? chips.Length : 0;
    public bool IsValueShown => valueButton != null && valueButton.gameObject.activeSelf;
    internal TMP_Text Label => label;
    internal TMP_Text ValueText => valueText;

    /// <summary>
    /// Logs an error when a reference is missing.
    /// </summary>
    private void Awake()
    {
        if (label == null || valueSlot == null || valueButton == null || valueText == null || chips == null || chips.Length == 0 || resetButton == null)
        {
            Debug.LogError($"{name} is missing its label, value, chips or Reset.", this);
        }
    }

    /// <summary>
    /// The chip at <paramref name="index"/>.
    /// </summary>
    public SwatchChipView GetChip(int index)
    {
        return chips[index];
    }

    /// <summary>
    /// Sets the row's label.
    /// </summary>
    public void SetLabel(string text)
    {
        label.text = text;
    }

    /// <summary>
    /// Shows <paramref name="text"/> on the value's button, or hides the button, keeping its column, for null.
    /// </summary>
    public void SetValue(string text)
    {
        bool shown = text != null;
        if (valueButton.gameObject.activeSelf != shown)
        {
            valueButton.gameObject.SetActive(shown);
        }

        if (shown)
        {
            valueText.text = text;
        }
    }

    /// <summary>
    /// Sets the value's column to <paramref name="width"/> UI px.
    /// </summary>
    public void SetValueWidth(float width)
    {
        valueSlot.preferredWidth = width;
        valueSlot.minWidth = width;
    }

    /// <summary>
    /// Shows chip <paramref name="index"/> in <paramref name="colour"/>, or hides it when not <paramref name="shown"/>.
    /// </summary>
    public void SetChip(int index, bool shown, Color colour)
    {
        SwatchChipView chip = chips[index];
        if (chip.gameObject.activeSelf != shown)
        {
            chip.gameObject.SetActive(shown);
        }

        if (shown)
        {
            chip.SetColour(colour);
        }
    }
}
