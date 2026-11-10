using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds UI themes for tests with <see cref="UiTheme.Create"/>: every role in a colour of its own, and each text size
/// in the project's Galmuri font assets (read, never written) at its own UI px.
/// </summary>
internal static class TestThemes
{
    private static UiTheme shared;

    /// <summary>
    /// A theme shared by the tests that only read it; never change it.
    /// </summary>
    public static UiTheme Shared
    {
        get
        {
            if (shared == null)
            {
                shared = Create();
            }

            return shared;
        }
    }

    /// <summary>
    /// The colours <see cref="Create"/> gives each role.
    /// </summary>
    public static Dictionary<UiColour, Color> Roles()
    {
        return new Dictionary<UiColour, Color>
        {
            [UiColour.Text] = Color.white,
            [UiColour.TextOnControl] = new Color(0.1f, 0.1f, 0.12f),
            [UiColour.TextMuted] = new Color(0.75f, 0.75f, 0.78f),
            [UiColour.TextDisabled] = new Color(0.5f, 0.5f, 0.54f),
            [UiColour.Placeholder] = new Color(0.35f, 0.35f, 0.35f),
            [UiColour.Accent] = new Color(1f, 0.85f, 0.4f),
            [UiColour.Surface] = new Color(0.12f, 0.12f, 0.16f, 0.85f),
            [UiColour.SurfaceEmpty] = new Color(0.12f, 0.12f, 0.16f, 0.75f),
            [UiColour.SurfaceRaised] = new Color(0.08f, 0.09f, 0.12f),
            [UiColour.Scrim] = new Color(0f, 0f, 0f, 0.6f),
            [UiColour.ScrimModal] = new Color(0f, 0f, 0f, 0.85f),
            [UiColour.Shade] = new Color(0f, 0f, 0f, 0.65f),
            [UiColour.ControlNormal] = new Color(0.85f, 0.85f, 0.8f),
            [UiColour.ControlHighlighted] = new Color(0.99f, 0.99f, 0.99f),
            [UiColour.ControlPressed] = new Color(0.7f, 0.7f, 0.65f),
            [UiColour.ControlDisabled] = new Color(0.43f, 0.43f, 0.45f),
            [UiColour.Danger] = new Color(1f, 0.3f, 0.25f),
            [UiColour.Positive] = new Color(0.45f, 1f, 0.55f),
            [UiColour.Health] = new Color(0.85f, 0.13f, 0.13f),
            [UiColour.HealthTrack] = new Color(0.08f, 0.08f, 0.08f, 0.85f),
            [UiColour.Icon] = new Color(1f, 1f, 0.98f),
            [UiColour.IconMissing] = new Color(1f, 1f, 1f, 0.25f),
            [UiColour.Border] = new Color(0.98f, 0.98f, 1f),
            [UiColour.Outline] = Color.black,
            [UiColour.ControlSecondary] = new Color(0.62f, 0.62f, 0.59f),
        };
    }

    /// <summary>
    /// A new theme with <see cref="Roles"/> and the project's fonts: Galmuri7 at 8 for Caption and Body, Galmuri9 at 10,
    /// Galmuri11 at 12 and Galmuri14 at 15.
    /// </summary>
    public static UiTheme Create()
    {
        return UiTheme.Create(Roles(), new[]
        {
            Style(UiTextSize.Caption, "Galmuri7", 8),
            Style(UiTextSize.Body, "Galmuri7", 8),
            Style(UiTextSize.Heading, "Galmuri9", 10),
            Style(UiTextSize.Title, "Galmuri11", 12),
            Style(UiTextSize.Display, "Galmuri14", 15),
        });
    }

    /// <summary>
    /// The text style of <paramref name="size"/> in the project's <paramref name="font"/> at <paramref name="pixels"/>.
    /// </summary>
    private static UiTheme.TextStyle Style(UiTextSize size, string font, int pixels)
    {
        TMP_FontAsset asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(UiFonts.FontAssetPath(font));
        Material outlined = AssetDatabase.LoadAssetAtPath<Material>(UiFonts.OutlinePath(font));
        return new UiTheme.TextStyle(size, asset, pixels, outlined);
    }
}
