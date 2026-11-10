using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// How the project's UI uses the theme (Docs/DesignSystem.md, Accessibility: Contrast): the text and background role
/// and the size of every <see cref="ThemedText"/> in the prefabs and the menu scenes (<see cref="Collect"/>), and the
/// contrast of each pair of roles against the threshold its smallest size must meet (<see cref="Measure"/>).
/// <c>DesignTables</c> writes the pairs into UiTheme.md and <c>UiThemeTests</c> checks them.
/// </summary>
public static class UiThemeUsage
{
    public const string ThemePath = "Assets/Data/UI/UiTheme.asset";
    public const double NormalTextContrast = 4.5;
    public const double LargeTextContrast = 3.0;
    public const double DisabledTextContrast = 3.0;

    public static readonly string[] PrefabFolders = { "Assets/Prefabs" };
    public static readonly string[] Scenes = { "Assets/Scenes/MainMenu.unity", "Assets/Scenes/Gameplay.unity" };

    /// <summary>
    /// The states a control is read in, with the role each shows: its disabled state needs only the disabled
    /// contrast, and a placeholder, which shows only in a field the player can type in, is not read in it. Pressed is
    /// left out: it shows while a press is held.
    /// </summary>
    private static readonly (string label, UiColour role, bool disabled)[] ControlStates =
    {
        ("ControlNormal", UiColour.ControlNormal, false),
        ("ControlHighlighted", UiColour.ControlHighlighted, false),
        ("Accent (selected)", UiColour.Accent, false),
        ("ControlDisabled", UiColour.ControlDisabled, true),
    };

    /// <summary>
    /// Every themed text of the project: those in the prefabs under <see cref="PrefabFolders"/> and those in the
    /// <see cref="Scenes"/>, which are opened beside the open scenes, read and closed again when they were not open.
    /// </summary>
    public static List<TextUse> Collect()
    {
        List<TextUse> uses = new();
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", PrefabFolders))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            foreach (ThemedText text in prefab.GetComponentsInChildren<ThemedText>(true))
            {
                uses.Add(new TextUse(text, path));
            }
        }

        foreach (string path in Scenes)
        {
            UnityEngine.SceneManagement.Scene scene = EditorSceneManager.GetSceneByPath(path);
            bool opened = !scene.isLoaded;
            if (opened)
            {
                scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            }

            try
            {
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (ThemedText text in root.GetComponentsInChildren<ThemedText>(true))
                    {
                        uses.Add(new TextUse(text, path));
                    }
                }
            }
            finally
            {
                if (opened)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        return uses;
    }

    /// <summary>
    /// The contrast of each pair of text and background roles in <paramref name="uses"/> under
    /// <paramref name="theme"/>, at the smallest size the pair is used at, sorted by text role then background role;
    /// a control background is measured in each state it is read in.
    /// </summary>
    public static List<PairContrast> Measure(UiTheme theme, IEnumerable<TextUse> uses)
    {
        SortedDictionary<(UiColour text, UiColour background), UiTextSize> pairs = new();
        foreach (TextUse use in uses)
        {
            (UiColour, UiColour) key = (use.Colour, use.Background);
            if (!pairs.TryGetValue(key, out UiTextSize smallest) || use.Size < smallest)
            {
                pairs[key] = use.Size;
            }
        }

        List<PairContrast> measured = new();
        foreach (KeyValuePair<(UiColour text, UiColour background), UiTextSize> pair in pairs)
        {
            Color text = theme.Colour(pair.Key.text);
            if (pair.Key.background == UiColour.ControlNormal)
            {
                foreach ((string label, UiColour role, bool disabled) in ControlStates)
                {
                    if (disabled && pair.Key.text == UiColour.Placeholder)
                    {
                        continue;
                    }

                    double threshold = disabled ? DisabledTextContrast : Threshold(pair.Key.text, pair.Value);
                    measured.Add(new PairContrast(pair.Key.text, label, pair.Value, Ratio(text, theme.Colour(role)), threshold));
                }

                continue;
            }

            measured.Add(new PairContrast(pair.Key.text, pair.Key.background.ToString(), pair.Value, Ratio(text, theme.Colour(pair.Key.background)), Threshold(pair.Key.text, pair.Value)));
        }

        return measured;
    }

    /// <summary>
    /// The contrast ratio of opaque <paramref name="text"/> on <paramref name="background"/>; a translucent background
    /// is laid over white and over black, and the lower ratio counts, since the world shows through it.
    /// </summary>
    public static double Ratio(Color text, Color background)
    {
        if (background.a >= 1f)
        {
            return DesignMath.ContrastRatio(text, background);
        }

        return Math.Min(DesignMath.ContrastRatio(text, Over(background, Color.white)), DesignMath.ContrastRatio(text, Over(background, Color.black)));
    }

    /// <summary>
    /// The contrast text of <paramref name="role"/> at <paramref name="size"/> needs: 3:1 for disabled text and for
    /// Title and above (large text), 4.5:1 otherwise.
    /// </summary>
    public static double Threshold(UiColour role, UiTextSize size)
    {
        return role == UiColour.TextDisabled ? DisabledTextContrast : size >= UiTextSize.Title ? LargeTextContrast : NormalTextContrast;
    }

    /// <summary>
    /// The body height (the top of a d to the bottom of a p) of <paramref name="style"/>'s font at its size, in UI px.
    /// </summary>
    public static float BodyHeight(UiTheme.TextStyle style)
    {
        TMP_FontAsset font = style.Font;
        if (font == null || !font.characterLookupTable.TryGetValue('d', out TMP_Character d) || !font.characterLookupTable.TryGetValue('p', out TMP_Character p))
        {
            return 0f;
        }

        float top = d.glyph.metrics.horizontalBearingY;
        float bottom = p.glyph.metrics.horizontalBearingY - p.glyph.metrics.height;
        return (top - bottom) / font.faceInfo.pointSize * style.Pixels;
    }

    /// <summary>
    /// <paramref name="top"/> laid over the opaque <paramref name="under"/>.
    /// </summary>
    private static Color Over(Color top, Color under)
    {
        Color result = Color.Lerp(under, top, top.a);
        result.a = 1f;
        return result;
    }

    /// <summary>
    /// One themed text: its roles, its size and where it is.
    /// </summary>
    public readonly struct TextUse
    {
        /// <summary>
        /// Reads <paramref name="text"/>'s roles and size; <paramref name="asset"/> is the prefab or scene holding it.
        /// </summary>
        public TextUse(ThemedText text, string asset)
        {
            Colour = text.Colour;
            Background = text.Background;
            Size = text.Size;
            Theme = text.Theme;
            Where = $"{asset}: {text.name}";
        }

        public UiColour Colour { get; }
        public UiColour Background { get; }
        public UiTextSize Size { get; }
        public UiTheme Theme { get; }
        public string Where { get; }
    }

    /// <summary>
    /// The contrast of one text role on one background (a role, or a control state), at the smallest size the pair is
    /// used at, and the threshold it must meet.
    /// </summary>
    public readonly struct PairContrast
    {
        /// <summary>
        /// Records the pair, its ratio and its threshold.
        /// </summary>
        public PairContrast(UiColour text, string background, UiTextSize size, double ratio, double threshold)
        {
            Text = text;
            Background = background;
            Size = size;
            Ratio = ratio;
            Threshold = threshold;
        }

        public UiColour Text { get; }
        public string Background { get; }
        public UiTextSize Size { get; }
        public double Ratio { get; }
        public double Threshold { get; }
    }
}
