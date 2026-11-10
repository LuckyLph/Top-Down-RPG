using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

/// <summary>
/// Tests for the UI theme (Docs/DesignSystem.md, Phase 4): <see cref="UiTheme"/> and the themed components on themes
/// from <see cref="UiTheme.Create"/>, the contrast rules of <see cref="UiThemeUsage"/>, then the project's theme: every
/// role and size, its Galmuri fonts and their outlined presets, Caption readable at every display, and every pair of
/// roles a themed text uses, and the gameplay popup colours, meeting their contrast.
/// </summary>
public class UiThemeTests
{
    private readonly List<Object> created = new();

    /// <summary>
    /// Destroys every object the test created.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        foreach (Object instance in created)
        {
            if (instance != null)
            {
                Object.DestroyImmediate(instance);
            }
        }

        created.Clear();
    }

    /// <summary>
    /// A theme from <see cref="UiTheme.Create"/> gives each role its colour and each text size its font, size and
    /// outlined material.
    /// </summary>
    [Test]
    public void Create_GivesEachRoleItsColour_AndEachSizeItsStyle()
    {
        UiTheme theme = Track(TestThemes.Create());

        foreach (KeyValuePair<UiColour, Color> role in TestThemes.Roles())
        {
            Assert.That(theme.Colour(role.Key), Is.EqualTo(role.Value), role.Key.ToString());
        }

        UiTheme.TextStyle heading = theme.Style(UiTextSize.Heading);
        Assert.That(heading.Size, Is.EqualTo(UiTextSize.Heading));
        Assert.That(heading.Font, Is.EqualTo(AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(UiFonts.FontAssetPath("Galmuri9"))));
        Assert.That(heading.Pixels, Is.EqualTo(10));
        Assert.That(heading.Outlined, Is.EqualTo(AssetDatabase.LoadAssetAtPath<Material>(UiFonts.OutlinePath("Galmuri9"))));
    }

    /// <summary>
    /// A role or a size the theme does not hold logs an error and falls back to magenta or an empty style.
    /// </summary>
    [Test]
    public void AMissingRoleOrSize_LogsAnError_AndFallsBack()
    {
        UiTheme theme = Track(UiTheme.Create(new Dictionary<UiColour, Color> { [UiColour.Text] = Color.white }));

        LogAssert.Expect(LogType.Error, new Regex("no colour for the Danger role"));
        Assert.That(theme.Colour(UiColour.Danger), Is.EqualTo(Color.magenta));
        LogAssert.Expect(LogType.Error, new Regex("no text style for the Body size"));
        Assert.That(theme.Style(UiTextSize.Body).Font, Is.Null);
    }

    /// <summary>
    /// Text on an opaque background is measured as it is; on a translucent one, over white and over black, and the
    /// lower ratio counts.
    /// </summary>
    [Test]
    public void Ratio_TakesTheLowerOfATranslucentBackgroundOverWhiteAndBlack()
    {
        Assert.That(UiThemeUsage.Ratio(Color.white, Color.black), Is.EqualTo(21.0).Within(1e-6));
        Assert.That(UiThemeUsage.Ratio(Color.white, new Color(0f, 0f, 0f, 0.5f)), Is.EqualTo(DesignMath.ContrastRatio(Color.white, new Color(0.5f, 0.5f, 0.5f))).Within(1e-6));
        Assert.That(UiThemeUsage.Ratio(Color.black, new Color(1f, 1f, 1f, 0.5f)), Is.EqualTo(DesignMath.ContrastRatio(Color.black, new Color(0.5f, 0.5f, 0.5f))).Within(1e-6));
    }

    /// <summary>
    /// Text needs 4.5:1 below Title, 3:1 at Title and above, and 3:1 when disabled.
    /// </summary>
    [Test]
    public void Threshold_IsLowerForLargeAndDisabledText()
    {
        Assert.That(UiThemeUsage.Threshold(UiColour.Text, UiTextSize.Caption), Is.EqualTo(4.5));
        Assert.That(UiThemeUsage.Threshold(UiColour.Text, UiTextSize.Heading), Is.EqualTo(4.5));
        Assert.That(UiThemeUsage.Threshold(UiColour.Text, UiTextSize.Title), Is.EqualTo(3.0));
        Assert.That(UiThemeUsage.Threshold(UiColour.TextDisabled, UiTextSize.Body), Is.EqualTo(3.0));
    }

    /// <summary>
    /// Each pair of roles is measured once, at the smallest size it is used at; a text on a control is read in the
    /// normal, highlighted, selected and disabled states, the last at the disabled contrast, and a placeholder in all
    /// but the disabled one.
    /// </summary>
    [Test]
    public void Measure_KeepsEachPairAtItsSmallestSize_AndReadsAControlInEachState()
    {
        UiTheme theme = Track(TestThemes.Create());
        List<UiThemeUsage.TextUse> uses = new()
        {
            Use(theme, UiColour.Text, UiColour.SurfaceRaised, UiTextSize.Title),
            Use(theme, UiColour.Text, UiColour.SurfaceRaised, UiTextSize.Body),
            Use(theme, UiColour.TextOnControl, UiColour.ControlNormal, UiTextSize.Body),
            Use(theme, UiColour.Placeholder, UiColour.ControlNormal, UiTextSize.Heading),
        };

        List<UiThemeUsage.PairContrast> measured = UiThemeUsage.Measure(theme, uses);

        List<string> rows = measured.ConvertAll(pair => $"{pair.Text} {pair.Background} {pair.Size} {pair.Threshold}");
        Assert.That(rows, Is.EqualTo(new[]
        {
            "Text SurfaceRaised Body 4.5",
            "TextOnControl ControlNormal Body 4.5",
            "TextOnControl ControlHighlighted Body 4.5",
            "TextOnControl Accent (selected) Body 4.5",
            "TextOnControl ControlDisabled Body 3",
            "Placeholder ControlNormal Heading 4.5",
            "Placeholder ControlHighlighted Heading 4.5",
            "Placeholder Accent (selected) Heading 4.5",
        }));
        Assert.That(measured[4].Ratio, Is.EqualTo(DesignMath.ContrastRatio(theme.Colour(UiColour.TextOnControl), theme.Colour(UiColour.ControlDisabled))).Within(1e-6));
    }

    /// <summary>
    /// A themed text takes its size's font, UI px and its role's colour, the outlined material when read against
    /// Outline, and changes size until reset.
    /// </summary>
    [Test]
    public void ThemedText_SetsFontSizeColourAndOutline_AndChangesSize()
    {
        UiTheme theme = Track(TestThemes.Create());
        TextMeshProUGUI text = Track(new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI))).GetComponent<TextMeshProUGUI>();
        ThemedText themed = text.gameObject.AddComponent<ThemedText>();

        themed.Configure(theme, UiColour.Accent, UiColour.Outline, UiTextSize.Heading);

        Assert.That(text.font, Is.EqualTo(theme.Style(UiTextSize.Heading).Font));
        Assert.That(text.fontSize, Is.EqualTo(10f));
        Assert.That(text.color, Is.EqualTo(theme.Colour(UiColour.Accent)));
        Assert.That(text.fontSharedMaterial, Is.EqualTo(theme.Style(UiTextSize.Heading).Outlined));

        themed.SetSize(UiTextSize.Caption);
        Assert.That(text.font, Is.EqualTo(theme.Style(UiTextSize.Caption).Font));
        Assert.That(text.fontSize, Is.EqualTo(8f));
        Assert.That(text.fontSharedMaterial, Is.EqualTo(theme.Style(UiTextSize.Caption).Outlined));
        Assert.That(themed.ShownSize, Is.EqualTo(UiTextSize.Caption));

        themed.ResetSize();
        Assert.That(themed.ShownSize, Is.EqualTo(UiTextSize.Heading));

        themed.Configure(theme, UiColour.Text, UiColour.SurfaceRaised, UiTextSize.Body);
        Assert.That(text.fontSharedMaterial, Is.EqualTo(theme.Style(UiTextSize.Body).Font.material));
    }

    /// <summary>
    /// A themed text moves each glyph by less than a UI px so that its origin lands on a whole UI px counted from its
    /// root canvas's corner, wherever its rect lies.
    /// </summary>
    [Test]
    public void ThemedText_SnapsEveryGlyphToAWholeUiPixelOfItsCanvas()
    {
        UiTheme theme = Track(TestThemes.Create());
        GameObject canvasObject = Track(new GameObject("Canvas", typeof(RectTransform), typeof(Canvas)));
        canvasObject.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        RectTransform canvasRect = (RectTransform)canvasObject.transform;
        canvasRect.sizeDelta = new Vector2(101f, 51f);
        TextMeshProUGUI text = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        text.rectTransform.SetParent(canvasRect, false);
        text.rectTransform.sizeDelta = new Vector2(61f, 21f);
        text.rectTransform.anchoredPosition = new Vector2(0.3f, 0.7f);
        text.alignment = TextAlignmentOptions.Center;
        ThemedText themed = text.gameObject.AddComponent<ThemedText>();
        themed.Configure(theme, UiColour.Text, UiColour.SurfaceRaised, UiTextSize.Body);
        text.text = "Fortify";
        text.ForceMeshUpdate(true, true);
        TMP_TextInfo info = text.textInfo;
        Vector3[] before = (Vector3[])info.meshInfo[0].vertices.Clone();

        themed.SnapToPixels(info);

        Vector2 corner = canvasRect.rect.min;
        int checkedGlyphs = 0;
        for (int i = 0; i < info.characterCount; i++)
        {
            TMP_CharacterInfo character = info.characterInfo[i];
            if (!character.isVisible)
            {
                continue;
            }

            Vector3 offset = info.meshInfo[0].vertices[character.vertexIndex] - before[character.vertexIndex];
            Vector3 origin = canvasRect.InverseTransformPoint(text.transform.TransformPoint(new Vector3(character.origin, character.baseLine, 0f) + offset));
            Assert.That(Mathf.Abs(offset.x), Is.LessThanOrEqualTo(0.5f), $"Glyph {i}");
            Assert.That(origin.x - corner.x, Is.EqualTo(Mathf.Round(origin.x - corner.x)).Within(1e-3f), $"Glyph {i} across");
            Assert.That(origin.y - corner.y, Is.EqualTo(Mathf.Round(origin.y - corner.y)).Within(1e-3f), $"Glyph {i} up");
            checkedGlyphs++;
        }

        Assert.That(checkedGlyphs, Is.EqualTo(7));
    }

    /// <summary>
    /// A themed graphic colours an Image or a SpriteRenderer in its role; a themed selectable sets its colour block
    /// from the control roles, Accent when selected.
    /// </summary>
    [Test]
    public void ThemedGraphicAndSelectable_TakeTheirRoles()
    {
        UiTheme theme = Track(TestThemes.Create());
        Image image = Track(new GameObject("Image", typeof(RectTransform), typeof(Image))).GetComponent<Image>();
        image.gameObject.AddComponent<ThemedGraphic>().Configure(theme, UiColour.Shade);
        SpriteRenderer sprite = Track(new GameObject("Sprite", typeof(SpriteRenderer))).GetComponent<SpriteRenderer>();
        sprite.gameObject.AddComponent<ThemedGraphic>().Configure(theme, UiColour.Danger);
        Button button = Track(new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button))).GetComponent<Button>();
        button.gameObject.AddComponent<ThemedSelectable>().Configure(theme);

        Assert.That(image.color, Is.EqualTo(theme.Colour(UiColour.Shade)));
        Assert.That(sprite.color, Is.EqualTo(theme.Colour(UiColour.Danger)));
        ColorBlock colours = button.colors;
        Assert.That(colours.normalColor, Is.EqualTo(theme.Colour(UiColour.ControlNormal)));
        Assert.That(colours.highlightedColor, Is.EqualTo(theme.Colour(UiColour.ControlHighlighted)));
        Assert.That(colours.pressedColor, Is.EqualTo(theme.Colour(UiColour.ControlPressed)));
        Assert.That(colours.selectedColor, Is.EqualTo(theme.Colour(UiColour.Accent)));
        Assert.That(colours.disabledColor, Is.EqualTo(theme.Colour(UiColour.ControlDisabled)));
        Assert.That(colours.colorMultiplier, Is.EqualTo(1f));
    }

    /// <summary>
    /// A themed component without a theme logs an error naming its object and leaves it as saved.
    /// </summary>
    [Test]
    public void ThemedComponents_WithoutATheme_LogAnError()
    {
        Image image = Track(new GameObject("Unthemed", typeof(RectTransform), typeof(Image))).GetComponent<Image>();
        image.color = Color.cyan;

        LogAssert.Expect(LogType.Error, new Regex("Unthemed has no UI theme"));
        image.gameObject.AddComponent<ThemedGraphic>().Configure(null, UiColour.Shade);

        Assert.That(image.color, Is.EqualTo(Color.cyan));
    }

    /// <summary>
    /// The project's theme holds every colour role and every text size once, each size with a font, a whole number of
    /// UI px and an outlined material.
    /// </summary>
    [Test]
    public void TheProjectTheme_HoldsEveryRoleAndSizeOnce()
    {
        UiTheme theme = ProjectTheme();

        foreach (UiColour role in (UiColour[])System.Enum.GetValues(typeof(UiColour)))
        {
            int count = 0;
            foreach (UiTheme.ColourRole entry in theme.Colours)
            {
                count += entry.Role == role ? 1 : 0;
            }

            Assert.That(count, Is.EqualTo(1), role.ToString());
        }

        foreach (UiTextSize size in (UiTextSize[])System.Enum.GetValues(typeof(UiTextSize)))
        {
            int count = 0;
            foreach (UiTheme.TextStyle entry in theme.TextSizes)
            {
                count += entry.Size == size ? 1 : 0;
            }

            Assert.That(count, Is.EqualTo(1), size.ToString());
            UiTheme.TextStyle style = theme.Style(size);
            Assert.That(style.Font, Is.Not.Null, size.ToString());
            Assert.That(style.Pixels, Is.GreaterThan(0), size.ToString());
            Assert.That(style.Outlined, Is.Not.Null, size.ToString());
        }

        Assert.That(theme.TextSizes.Count, Is.EqualTo(System.Enum.GetValues(typeof(UiTextSize)).Length));
        Assert.That(theme.Colours.Count, Is.EqualTo(System.Enum.GetValues(typeof(UiColour)).Length));
    }

    /// <summary>
    /// Each size's font is a static atlas holding every character the UI shows, sampled at
    /// <see cref="UiFonts.AtlasPixelsPerFontPixel"/> atlas pixels per font pixel at the theme's size, so one font pixel
    /// is one UI px: every glyph's box and advance is a whole number of font pixels.
    /// </summary>
    [Test]
    public void EachFont_IsAStaticPixelGrid_HoldingEveryUiCharacter()
    {
        UiTheme theme = ProjectTheme();
        foreach (UiTheme.TextStyle style in theme.TextSizes)
        {
            TMP_FontAsset font = style.Font;
            string name = $"{style.Size} ({font.name})";
            Assert.That(font.atlasPopulationMode, Is.EqualTo(AtlasPopulationMode.Static), name);
            Assert.That(font.atlasPadding, Is.EqualTo(UiFonts.Padding), name);
            Assert.That(font.faceInfo.pointSize, Is.EqualTo(style.Pixels * UiFonts.AtlasPixelsPerFontPixel), name);
            foreach (char character in UiFonts.Characters)
            {
                Assert.That(font.characterLookupTable.ContainsKey(character), Is.True, $"{name} lacks U+{(int)character:X4}");
            }

            foreach (char character in "AHMWgjpqy01!")
            {
                UnityEngine.TextCore.GlyphMetrics metrics = font.characterLookupTable[character].glyph.metrics;
                foreach (float value in new[] { metrics.width, metrics.height, metrics.horizontalBearingX, metrics.horizontalBearingY, metrics.horizontalAdvance })
                {
                    Assert.That(value % UiFonts.AtlasPixelsPerFontPixel, Is.EqualTo(0f).Within(1e-3f), $"{name} '{character}' is off the pixel grid");
                }
            }
        }
    }

    /// <summary>
    /// Each outlined material is its font's atlas with an outline in the Outline role at least one UI px outside the
    /// glyph, the face dilated by as much so it stays whole, within the atlas padding.
    /// </summary>
    [Test]
    public void EachOutlinedMaterial_OutlinesOneUiPxOutsideAWholeFace()
    {
        UiTheme theme = ProjectTheme();
        foreach (UiTheme.TextStyle style in theme.TextSizes)
        {
            Material outline = style.Outlined;
            string name = $"{style.Size} ({outline.name})";
            float width = outline.GetFloat(ShaderUtilities.ID_OutlineWidth);
            float dilate = outline.GetFloat(ShaderUtilities.ID_FaceDilate);
            float weight = Mathf.Max(outline.GetFloat(ShaderUtilities.ID_WeightNormal), outline.GetFloat(ShaderUtilities.ID_WeightBold)) / 4f;
            float reach = 2f * width * style.Font.atlasPadding / (style.Font.faceInfo.pointSize / style.Pixels);

            Assert.That(outline.IsKeywordEnabled(ShaderUtilities.Keyword_Outline), Is.True, name);
            Assert.That(outline.GetTexture(ShaderUtilities.ID_MainTex), Is.EqualTo(style.Font.atlasTexture), name);
            Assert.That(outline.GetColor(ShaderUtilities.ID_OutlineColor), Is.EqualTo(theme.Colour(UiColour.Outline)), name);
            Assert.That(dilate, Is.EqualTo(width), $"{name} keeps the face whole");
            Assert.That(reach, Is.GreaterThanOrEqualTo(1f), $"{name} outline in UI px");
            Assert.That(weight + width + dilate, Is.LessThanOrEqualTo(1f), $"{name} stays within the padding");
        }
    }

    /// <summary>
    /// Caption text, from the theme's font (the top of a d to the bottom of a p), has a body height of at least 18 px
    /// per 1080 display lines at the zoom every display of the Scale table gets (XAG 101).
    /// </summary>
    [Test]
    public void CaptionText_IsReadableAtEveryDisplay()
    {
        UiScaleSettings settings = AssetDatabase.LoadAssetAtPath<UiScaleSettings>("Assets/Data/UI/UiScale.asset");
        float body = UiThemeUsage.BodyHeight(ProjectTheme().Style(UiTextSize.Caption));

        Assert.That(body, Is.GreaterThan(0f));
        foreach (int[] display in UiScaleTests.Displays)
        {
            int zoom = UiScale.ZoomFor(display[0], display[1], 0, settings);
            Assert.That(body * zoom, Is.GreaterThanOrEqualTo(18f * display[1] / 1080f), $"{display[0]} x {display[1]}");
        }
    }

    /// <summary>
    /// Every pair of text and background roles a themed text of the project uses meets its contrast in the project's
    /// theme (the pairs UiTheme.md lists).
    /// </summary>
    [Test]
    public void EveryTextPair_MeetsItsContrast()
    {
        List<UiThemeUsage.PairContrast> pairs = UiThemeUsage.Measure(ProjectTheme(), UiThemeUsage.Collect());

        Assert.That(pairs.Count, Is.GreaterThan(5));
        foreach (UiThemeUsage.PairContrast pair in pairs)
        {
            Assert.That(pair.Ratio, Is.GreaterThanOrEqualTo(pair.Threshold), $"{pair.Text} on {pair.Background} at {pair.Size}");
        }
    }

    /// <summary>
    /// The popup colours on the gameplay assets (each damage type, heal, immune and the waystone notice) read against
    /// the Outline role at the contrast of text under Title.
    /// </summary>
    [Test]
    public void GameplayPopupColours_MeetTheirContrastOnTheOutline()
    {
        Color outline = ProjectTheme().Colour(UiColour.Outline);
        CombatSettings combat = AssetDatabase.LoadAssetAtPath<CombatSettings>(DesignContent.CombatSettingsPath);
        GameplaySettings gameplay = AssetDatabase.LoadAssetAtPath<GameplaySettings>("Assets/Data/GameplaySettings.asset");
        Dictionary<string, Color> colours = new()
        {
            ["Heal"] = combat.HealColor,
            ["Immune"] = combat.ImmuneColor,
            ["Waystone discovered"] = gameplay.WaystoneDiscoveredColor,
        };
        foreach (DamageType type in (DamageType[])System.Enum.GetValues(typeof(DamageType)))
        {
            colours[type.ToString()] = combat.GetColor(type);
        }

        foreach (KeyValuePair<string, Color> colour in colours)
        {
            Assert.That(DesignMath.ContrastRatio(colour.Value, outline), Is.GreaterThanOrEqualTo(UiThemeUsage.NormalTextContrast), colour.Key);
        }
    }

    /// <summary>
    /// The project's theme asset.
    /// </summary>
    private static UiTheme ProjectTheme()
    {
        UiTheme theme = AssetDatabase.LoadAssetAtPath<UiTheme>(UiThemeUsage.ThemePath);
        Assert.That(theme, Is.Not.Null, UiThemeUsage.ThemePath);
        return theme;
    }

    /// <summary>
    /// A themed text in <paramref name="theme"/> with the given roles and size, as a use.
    /// </summary>
    private UiThemeUsage.TextUse Use(UiTheme theme, UiColour colour, UiColour background, UiTextSize size)
    {
        GameObject owner = Track(new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI)));
        ThemedText themed = owner.AddComponent<ThemedText>();
        themed.Configure(theme, colour, background, size);
        return new UiThemeUsage.TextUse(themed, "test");
    }

    /// <summary>
    /// Keeps <paramref name="instance"/> for <see cref="TearDown"/> and returns it.
    /// </summary>
    private T Track<T>(T instance) where T : Object
    {
        created.Add(instance);
        return instance;
    }
}
