using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// A swatch chip (Docs/UiRevamp.md, Phase 7): a 12 UI px button showing a colour, framed in Border. Its button changes
/// no colour, so the chip always shows its swatch; while it is selected a frame in Accent, framed in Border, rings it in
/// the 2 UI px between chips. The Character screen's rows show a part's colours as chips, and the swatch picker its
/// swatches.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Button))]
public sealed class SwatchChipView : MonoBehaviour, ISelectHandler, IDeselectHandler
{
    [SerializeField] private Button button;
    [SerializeField, Tooltip("The chip's face, the button's target graphic, which shows the swatch's colour.")]
    private Image fill;
    [SerializeField, Tooltip("The Accent frame shown around the chip while it is selected.")]
    private GameObject focus;

    /// <summary>
    /// Raised when the chip is selected, by keyboard, gamepad or pointer.
    /// </summary>
    public event Action<SwatchChipView> Selected;

    public Button Button => button;
    internal Image Fill => fill;
    internal bool IsFocusShown => focus != null && focus.activeSelf;

    /// <summary>
    /// Logs an error without its button, face or focus frame, and hides the focus frame.
    /// </summary>
    private void Awake()
    {
        if (button == null || fill == null || focus == null)
        {
            Debug.LogError($"{name} needs its button, its face and its focus frame.", this);
            return;
        }

        focus.SetActive(false);
    }

    /// <summary>
    /// Shows <paramref name="colour"/> on the chip's face.
    /// </summary>
    public void SetColour(Color colour)
    {
        if (fill != null)
        {
            fill.color = colour;
        }
    }

    /// <summary>
    /// Shows the focus frame and raises <see cref="Selected"/>.
    /// </summary>
    public void OnSelect(BaseEventData eventData)
    {
        if (focus != null)
        {
            focus.SetActive(true);
        }

        Selected?.Invoke(this);
    }

    /// <summary>
    /// Hides the focus frame.
    /// </summary>
    public void OnDeselect(BaseEventData eventData)
    {
        if (focus != null)
        {
            focus.SetActive(false);
        }
    }
}
