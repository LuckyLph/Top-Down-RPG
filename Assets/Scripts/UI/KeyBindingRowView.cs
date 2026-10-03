using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One row of the options screen's Controls section: the action's label, a button showing its binding that starts
/// a rebind, and a button that resets it.
/// </summary>
[DisallowMultipleComponent]
public class KeyBindingRowView : MonoBehaviour
{
    [SerializeField] private TMP_Text label;
    [SerializeField] private Button bindingButton;
    [SerializeField] private TMP_Text bindingText;
    [SerializeField] private Button resetButton;

    public Button BindingButton => bindingButton;
    public Button ResetButton => resetButton;
    internal TMP_Text Label => label;
    internal TMP_Text BindingText => bindingText;

    private void Awake()
    {
        if (label == null || bindingButton == null || bindingText == null || resetButton == null)
        {
            Debug.LogError($"{name} is missing a label, binding button, binding text or reset button.", this);
        }
    }

    public void SetLabel(string text)
    {
        if (label != null)
        {
            label.text = text;
        }
    }

    public void SetBinding(string text)
    {
        if (bindingText != null)
        {
            bindingText.text = text;
        }
    }
}
