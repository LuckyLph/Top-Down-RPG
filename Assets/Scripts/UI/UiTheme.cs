using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// The UI theme (Docs/DesignSystem.md, Foundations and Components): a colour per <see cref="UiColour"/> role and, per
/// <see cref="UiTextSize"/>, the font, its size in UI px and its outlined material preset. Read-only: themed components
/// and views read it once when they wake, and nothing writes it at runtime. The project's is
/// <c>Assets/Data/UI/UiTheme.asset</c>; tests build their own with <see cref="Create"/>.
/// </summary>
[CreateAssetMenu(fileName = "UiTheme", menuName = "TopDownRPG/UI/UI Theme")]
public sealed class UiTheme : ScriptableObject
{
    [SerializeField, Tooltip("The colour of each role, each role once.")]
    private ColourRole[] colours = new ColourRole[0];

    [SerializeField, Tooltip("The font, size and outlined material of each text size, each size once.")]
    private TextStyle[] textSizes = new TextStyle[0];

    public IReadOnlyList<ColourRole> Colours => colours;
    public IReadOnlyList<TextStyle> TextSizes => textSizes;

    /// <summary>
    /// The colour of <paramref name="role"/>; logs an error and returns magenta when the theme has none.
    /// </summary>
    public Color Colour(UiColour role)
    {
        for (int i = 0; i < colours.Length; i++)
        {
            if (colours[i].Role == role)
            {
                return colours[i].Colour;
            }
        }

        Debug.LogError($"{name} has no colour for the {role} role.", this);
        return Color.magenta;
    }

    /// <summary>
    /// The font, size and outlined material of <paramref name="size"/>; logs an error and returns an empty style when
    /// the theme has none.
    /// </summary>
    public TextStyle Style(UiTextSize size)
    {
        for (int i = 0; i < textSizes.Length; i++)
        {
            if (textSizes[i].Size == size)
            {
                return textSizes[i];
            }
        }

        Debug.LogError($"{name} has no text style for the {size} size.", this);
        return default;
    }

    /// <summary>
    /// Builds an unsaved, hidden theme with <paramref name="roles"/> and <paramref name="sizes"/>, for tests that must
    /// not touch project assets.
    /// </summary>
    public static UiTheme Create(IReadOnlyDictionary<UiColour, Color> roles, IEnumerable<TextStyle> sizes = null)
    {
        UiTheme theme = CreateInstance<UiTheme>();
        List<ColourRole> entries = new();
        foreach (KeyValuePair<UiColour, Color> role in roles)
        {
            entries.Add(new ColourRole(role.Key, role.Value));
        }

        theme.colours = entries.ToArray();
        theme.textSizes = sizes != null ? new List<TextStyle>(sizes).ToArray() : new TextStyle[0];
        theme.hideFlags = HideFlags.HideAndDontSave;
        return theme;
    }

    /// <summary>
    /// One role's colour.
    /// </summary>
    [Serializable]
    public struct ColourRole
    {
        [SerializeField] private UiColour role;
        [SerializeField] private Color colour;

        /// <summary>
        /// Pairs <paramref name="colourRole"/> with <paramref name="value"/>.
        /// </summary>
        public ColourRole(UiColour colourRole, Color value)
        {
            role = colourRole;
            colour = value;
        }

        public UiColour Role => role;
        public Color Colour => colour;
    }

    /// <summary>
    /// One text size: its font, the font size in whole UI px at which the font draws one UI px per font pixel, and the
    /// font's outlined material preset for text read against <see cref="UiColour.Outline"/>.
    /// </summary>
    [Serializable]
    public struct TextStyle
    {
        [SerializeField] private UiTextSize size;
        [SerializeField] private TMP_FontAsset font;
        [SerializeField, Min(1), Tooltip("The font size in UI px.")] private int pixels;
        [SerializeField, Tooltip("The font's material with the Outline role's outline, for text over the world.")] private Material outlined;

        /// <summary>
        /// Describes <paramref name="textSize"/> as <paramref name="fontAsset"/> at <paramref name="uiPixels"/> UI px,
        /// outlined with <paramref name="outlinedMaterial"/>.
        /// </summary>
        public TextStyle(UiTextSize textSize, TMP_FontAsset fontAsset, int uiPixels, Material outlinedMaterial)
        {
            size = textSize;
            font = fontAsset;
            pixels = uiPixels;
            outlined = outlinedMaterial;
        }

        public UiTextSize Size => size;
        public TMP_FontAsset Font => font;
        public int Pixels => pixels;
        public Material Outlined => outlined;
    }
}
