using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Gives an Image, a RawImage or a SpriteRenderer its theme colour (Docs/DesignSystem.md, Components): when it wakes it
/// sets the colour of its <see cref="UiColour"/> role from the <see cref="UiTheme"/> on its own instance. Views that
/// recolour the graphic at runtime do so after; nothing reads the theme per frame. A bar's fill names the track it is
/// read against, so the theme's checks measure the pair (Docs/DesignSystem.md, Accessibility: Contrast).
/// </summary>
[DisallowMultipleComponent]
public class ThemedGraphic : MonoBehaviour
{
    [SerializeField] private UiTheme theme;
    [SerializeField] private UiColour role = UiColour.Surface;
    [SerializeField, Tooltip("For a bar's fill: the themed track it is read against, whose contrast with it must be 3:1. Empty for other graphics.")]
    private ThemedGraphic track;

    public UiTheme Theme => theme;
    public UiColour Role => role;
    public ThemedGraphic Track => track;

    /// <summary>
    /// Applies the role's colour; logs an error without a theme or a graphic to colour.
    /// </summary>
    private void Awake()
    {
        Apply();
    }

    /// <summary>
    /// Sets the theme, the role and, for a bar's fill, its track in code and applies them, for tests that build the
    /// graphic without a prefab.
    /// </summary>
    internal void Configure(UiTheme uiTheme, UiColour colourRole, ThemedGraphic readAgainst = null)
    {
        theme = uiTheme;
        role = colourRole;
        track = readAgainst;
        Apply();
    }

    /// <summary>
    /// Sets the graphic's or the sprite renderer's colour to the role's.
    /// </summary>
    private void Apply()
    {
        if (theme == null)
        {
            Debug.LogError($"{name} has no UI theme.", this);
            return;
        }

        Color colour = theme.Colour(role);
        if (TryGetComponent(out Graphic graphic))
        {
            graphic.color = colour;
        }
        else if (TryGetComponent(out SpriteRenderer spriteRenderer))
        {
            spriteRenderer.color = colour;
        }
        else
        {
            Debug.LogError($"{name} has no graphic or sprite renderer to colour.", this);
        }
    }
}
