using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One row of the options screen: a label, a button showing its value (an action's binding, a choice, On or Off) and a
/// button that resets it, shown only while the row differs from its default (Docs/UiRevamp.md, Phase 8), its column
/// kept while hidden so every row's value lines up. A toggle row shows its state as a box, marked while on, beside the
/// On or Off its button reads.
/// </summary>
[DisallowMultipleComponent]
public class KeyBindingRowView : MonoBehaviour
{
    [SerializeField] private TMP_Text label;
    [SerializeField] private Button bindingButton;
    [SerializeField] private TMP_Text bindingText;
    [SerializeField] private Button resetButton;
    [SerializeField, Tooltip("The mark a toggle row shows in its box while it is on; none on other rows.")]
    private Image toggleMark;

    public Button BindingButton => bindingButton;
    public Button ResetButton => resetButton;
    public bool IsResetShown => resetButton != null && resetButton.gameObject.activeSelf;
    internal TMP_Text Label => label;
    internal TMP_Text BindingText => bindingText;
    internal Image ToggleMark => toggleMark;

    /// <summary>
    /// Logs an error when the label, binding button, binding text or reset button is missing.
    /// </summary>
    private void Awake()
    {
        if (label == null || bindingButton == null || bindingText == null || resetButton == null)
        {
            Debug.LogError($"{name} is missing a label, binding button, binding text or reset button.", this);
        }
    }

    /// <summary>
    /// Sets the action's display name shown at the start of the row.
    /// </summary>
    public void SetLabel(string text)
    {
        if (label != null)
        {
            label.text = text;
        }
    }

    /// <summary>
    /// Sets the text on the binding button, the label of the action's current binding.
    /// </summary>
    public void SetBinding(string text)
    {
        if (bindingText != null)
        {
            bindingText.text = text;
        }
    }

    /// <summary>
    /// Shows or hides the reset button; its column keeps its width while it is hidden.
    /// </summary>
    public void SetResetShown(bool shown)
    {
        if (resetButton != null && resetButton.gameObject.activeSelf != shown)
        {
            resetButton.gameObject.SetActive(shown);
        }
    }

    /// <summary>
    /// Shows the toggle's mark while <paramref name="on"/> and hides it while off, when the row has one.
    /// </summary>
    public void SetToggle(bool on)
    {
        if (toggleMark != null)
        {
            toggleMark.enabled = on;
        }
    }
}
