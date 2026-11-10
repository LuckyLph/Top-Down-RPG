using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Gives a Selectable its theme colours (Docs/DesignSystem.md, Components): when it wakes it sets its own colour block
/// from the control roles of the <see cref="UiTheme"/>: its own normal role (<see cref="UiColour.ControlNormal"/> for a
/// primary button, <see cref="UiColour.ControlSecondary"/> or <see cref="UiColour.Danger"/> for the other buttons), and
/// the shared highlighted, pressed and disabled roles, with <see cref="UiColour.Accent"/> for selected. Its target graphic
/// stays white, so the block's colours show as they are.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Selectable))]
public class ThemedSelectable : MonoBehaviour
{
    [SerializeField] private UiTheme theme;
    [SerializeField, Tooltip("The role of the control's face when it is neither highlighted, pressed, selected nor disabled.")]
    private UiColour normal = UiColour.ControlNormal;

    public UiTheme Theme => theme;
    public UiColour Normal => normal;

    /// <summary>
    /// Applies the control roles; logs an error without a theme.
    /// </summary>
    private void Awake()
    {
        Apply();
    }

    /// <summary>
    /// Sets the theme in code and applies it, for tests that build the control without a prefab.
    /// </summary>
    internal void Configure(UiTheme uiTheme, UiColour normalRole = UiColour.ControlNormal)
    {
        theme = uiTheme;
        normal = normalRole;
        Apply();
    }

    /// <summary>
    /// The colour block of <paramref name="uiTheme"/>'s control roles with <paramref name="normalRole"/> as the normal
    /// colour, keeping <paramref name="block"/>'s fade duration.
    /// </summary>
    public static ColorBlock ControlColours(UiTheme uiTheme, ColorBlock block, UiColour normalRole = UiColour.ControlNormal)
    {
        block.normalColor = uiTheme.Colour(normalRole);
        block.highlightedColor = uiTheme.Colour(UiColour.ControlHighlighted);
        block.pressedColor = uiTheme.Colour(UiColour.ControlPressed);
        block.selectedColor = uiTheme.Colour(UiColour.Accent);
        block.disabledColor = uiTheme.Colour(UiColour.ControlDisabled);
        block.colorMultiplier = 1f;
        return block;
    }

    /// <summary>
    /// Sets the selectable's colour block from the theme.
    /// </summary>
    private void Apply()
    {
        if (theme == null)
        {
            Debug.LogError($"{name} has no UI theme.", this);
            return;
        }

        Selectable selectable = GetComponent<Selectable>();
        selectable.colors = ControlColours(theme, selectable.colors, normal);
    }
}
