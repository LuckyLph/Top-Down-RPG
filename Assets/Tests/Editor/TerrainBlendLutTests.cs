using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Tests for <see cref="TerrainBlendLut"/>, the terrain style and materials as shader data: a pair override replaces
/// the upper material's default edge for its own pair of material assets only, one whose lower material is missing is
/// not taken for one over nothing, layers find their materials' rows whatever the order, the rim is sampled every half
/// pixel up to its width, the soft look's height scale (held to its bound), band and whole stones reach their rows, an
/// animated material's animation reaches its layer's row and a still one's leaves it empty, a sunken material's bank
/// reaches its layer's row and a level one's leaves it empty, the project style's stone over gravel override changes
/// that pair's row only, and the project's water is animated, sunken and meets sand without an outline; a swaying
/// material's sway reaches its layer's row and a seamless edge marks its rim's outer colour, and the project's grass
/// sways and keeps its edge over nothing; the project's dirt, dry grass and plaza meet the other materials
/// with the edges and overrides TerrainTypes.md's Phase 3 decided, and its shallows glint, sink less than the water and
/// take the deep water seamlessly (Phase 4); bridges reach row 0 as bits (their high half past 16 layers in a column
/// of its own, TerrainLayers32.md's Phase 2) and materials that turn to their runs mark
/// their layer's row, and the project's planks are a level bridge drawn last with a shadow of their own on the water
/// (Phase 5); raised ground's cliff settings reach row 0 only for layers drawn over raised ground (Phase 10); each
/// layer's slice of the texture arrays reaches its row, its own index unless one is given (WorldScale.md's Phase 2); a
/// material's reflection reaches its row, and of the project's materials only the water, the shallows at half and the sea
/// reflect (Lighting.md's Phase 10); the shader's layout defines and the constants agree. The project's style and
/// materials are only read.
/// </summary>
public class TerrainBlendLutTests
{
    private const string LayoutPath = "Assets/Shaders/Terrain/TerrainBlendLut.hlsl";

    private readonly List<Object> created = new();

    /// <summary>
    /// Destroys the style and materials the test created.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        foreach (Object createdObject in created)
        {
            if (createdObject != null)
            {
                Object.DestroyImmediate(createdObject);
            }
        }

        created.Clear();
    }

    /// <summary>
    /// Stone over gravel and gravel over nothing draw their overrides, while stone over grass, stone over nothing,
    /// gravel over grass and grass over nothing draw their upper material's default edge. Overrides are keyed by the
    /// material assets: one for another gravel asset of the same name and settings applies to that asset only, and a
    /// layer list without gravel never uses gravel's.
    /// </summary>
    [Test]
    public void APairOverride_ReplacesTheDefaultEdge_ForItsOwnMaterialsOnly()
    {
        TerrainMaterial grass = Material("Grass", 0.1f);
        TerrainMaterial gravel = Material("Gravel", 0.2f);
        TerrainMaterial stone = Material("Stone", 0.3f);
        TerrainMaterial otherGravel = Material("Gravel", 0.2f);
        TerrainStyle style = Style(
            new TerrainStyle.PairOverride(stone, gravel, Edge(0.9f, 1f)),
            new TerrainStyle.PairOverride(stone, otherGravel, Edge(0.7f, 1f)),
            new TerrainStyle.PairOverride(gravel, null, Edge(0.6f, 1f)));

        Color[] texels = TerrainBlendLut.BuildTexels(style, new[] { grass, gravel, stone });

        Assert.That(ShadowAlpha(texels, 3, 2, 1), Is.EqualTo(0.9f).Within(1e-5f));
        Assert.That(ShadowAlpha(texels, 3, 2, 0), Is.EqualTo(0.3f).Within(1e-5f));
        Assert.That(ShadowAlpha(texels, 3, 2, -1), Is.EqualTo(0.3f).Within(1e-5f));
        Assert.That(ShadowAlpha(texels, 3, 1, 0), Is.EqualTo(0.2f).Within(1e-5f));
        Assert.That(ShadowAlpha(texels, 3, 1, -1), Is.EqualTo(0.6f).Within(1e-5f));
        Assert.That(ShadowAlpha(texels, 3, 0, -1), Is.EqualTo(0.1f).Within(1e-5f));

        Color[] withOtherGravel = TerrainBlendLut.BuildTexels(style, new[] { grass, otherGravel, stone });
        Assert.That(ShadowAlpha(withOtherGravel, 3, 2, 1), Is.EqualTo(0.7f).Within(1e-5f));
        Assert.That(ShadowAlpha(withOtherGravel, 3, 1, -1), Is.EqualTo(0.2f).Within(1e-5f), "Gravel's override over nothing is not the other gravel's.");

        Color[] withoutGravel = TerrainBlendLut.BuildTexels(style, new[] { grass, stone });
        Assert.That(ShadowAlpha(withoutGravel, 2, 1, 0), Is.EqualTo(0.3f).Within(1e-5f));
    }

    /// <summary>
    /// An override whose lower material is gone (its asset deleted or destroyed) is not taken for one over nothing,
    /// though Unity's == calls the missing material null: stone over nothing keeps its default edge, and the style warns
    /// about the override when validated.
    /// </summary>
    [Test]
    public void AnOverrideWhoseLowerMaterialIsMissing_DoesNotApplyOverNothing()
    {
        TerrainMaterial gravel = Material("Gravel", 0.2f);
        TerrainMaterial stone = Material("Stone", 0.3f);
        TerrainStyle style = Style(new TerrainStyle.PairOverride(stone, gravel, Edge(0.9f, 1f)));
        style.name = "Style";

        Object.DestroyImmediate(gravel);

        Assert.That(style.PairOverrides[0].HasMissingLower(), Is.True);
        Assert.That(style.GetEdge(stone, null), Is.SameAs(stone.Edge));
        Assert.That(ShadowAlpha(TerrainBlendLut.BuildTexels(style, new[] { stone }), 1, 0, -1), Is.EqualTo(0.3f).Within(1e-5f));
        LogAssert.Expect(LogType.Warning, "Style: pair override 0's lower material is missing, so it never applies. Clear it to mean over nothing (the map's edge).");
        SerializedObject serialized = new(style);
        serialized.FindProperty("edgeNoise").floatValue = 2f;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>
    /// An override with no lower material at all is one over nothing, not a missing one.
    /// </summary>
    [Test]
    public void AnOverrideWithNoLowerMaterial_AppliesOverNothing()
    {
        TerrainMaterial stone = Material("Stone", 0.3f);
        TerrainStyle style = Style(new TerrainStyle.PairOverride(stone, null, Edge(0.9f, 1f)));

        Assert.That(style.PairOverrides[0].HasMissingLower(), Is.False);
        Assert.That(ShadowAlpha(TerrainBlendLut.BuildTexels(style, new[] { stone }), 1, 0, -1), Is.EqualTo(0.9f).Within(1e-5f));
    }

    /// <summary>
    /// Row 0 holds the shared settings and each layer's row its own material's repeat, cell mode and base colour.
    /// </summary>
    [Test]
    public void SharedAndLayerRows_HoldTheStyleAndMaterialSettings()
    {
        TerrainStyle style = Style();
        TerrainMaterial grass = Material("Grass", 0.1f);
        TerrainMaterial stone = Material("Stone", 0.3f, 2, TerrainMaterial.CellMode.CutStones, new Color(0.4f, 0.3f, 0.2f));

        Color[] texels = TerrainBlendLut.BuildTexels(style, new[] { stone, grass });

        Assert.That(texels[TerrainBlendLut.SharedRow * TerrainBlendLut.Width + TerrainBlendLut.SharedColumn], Is.EqualTo(new Color(style.EdgeNoise, style.PairMargin, style.PairBlend, 0f)));
        Color stoneLayer = LayerTexel(texels, 0, TerrainBlendLut.LayerShapeColumn);
        Assert.That(stoneLayer.b, Is.EqualTo(64f));
        Assert.That(stoneLayer.a, Is.EqualTo((float)TerrainMaterial.CellMode.CutStones));
        Assert.That(LayerTexel(texels, 0, TerrainBlendLut.LayerBaseColumn), Is.EqualTo(new Color(0.4f, 0.3f, 0.2f)));
        Color grassLayer = LayerTexel(texels, 1, TerrainBlendLut.LayerShapeColumn);
        Assert.That(grassLayer.b, Is.EqualTo(128f));
        Assert.That(grassLayer.a, Is.EqualTo(0f));
    }

    /// <summary>
    /// The rim is sampled at the middle of each half pixel from the edge inwards, following its gradient, and is clear
    /// beyond its width.
    /// </summary>
    [Test]
    public void TheRim_IsSampledEveryHalfPixel_UpToItsWidth()
    {
        Gradient rim = new() { mode = GradientMode.Fixed };
        rim.SetKeys(new[] { new GradientColorKey(Color.red, 0.5f), new GradientColorKey(Color.blue, 1f) }, new[] { new GradientAlphaKey(1f, 1f) });
        TerrainMaterial grass = Track(TerrainMaterial.Create("Grass", null, null, 4, TerrainMaterial.CellMode.None, Color.gray,
            new TerrainStyle.EdgeSettings(2f, rim, true, Color.black, Color.black, 0.3f, 1f)));

        Color[] texels = TerrainBlendLut.BuildTexels(Style(), new[] { grass });

        int ramp = TerrainBlendLut.PairRow(1, 0, -1) * TerrainBlendLut.Width + TerrainBlendLut.PairRampColumn;
        Assert.That(texels[ramp], Is.EqualTo(Color.red));
        Assert.That(texels[ramp + 1], Is.EqualTo(Color.red));
        Assert.That(texels[ramp + 2], Is.EqualTo(Color.blue));
        Assert.That(texels[ramp + 3], Is.EqualTo(Color.blue));
        Assert.That(texels[ramp + 4], Is.EqualTo(Color.clear));
    }

    /// <summary>
    /// Each layer row holds its material's soft height scale, 0 without a height texture and held to
    /// <see cref="TerrainMaterial.MaxHeightScale"/>; each pair row holds the soft band's colour and width; whole stones
    /// are marked on the pair and on the upper layer's row only where the upper material has cut stones.
    /// </summary>
    [Test]
    public void TheSoftSettings_ReachTheLayerAndPairRows()
    {
        Texture2D height = Track(new Texture2D(4, 4, TextureFormat.RGBA32, false, true));
        Color dirt = new(0.5f, 0.4f, 0.3f);
        TerrainMaterial grass = Track(TerrainMaterial.Create("Grass", null, null, 4, TerrainMaterial.CellMode.None, Color.gray, Edge(0.1f, 1f), null, 2f));
        TerrainMaterial gravel = Track(TerrainMaterial.Create("Gravel", null, null, 4, TerrainMaterial.CellMode.WholeChips, Color.gray,
            new TerrainStyle.EdgeSettings(1.75f, new Gradient(), true, Color.black, Color.black, 0.3f, 1f, 0f, 1.5f, dirt, true), height, 1.25f));
        TerrainMaterial stone = Track(TerrainMaterial.Create("Stone", null, null, 4, TerrainMaterial.CellMode.CutStones, Color.gray, Edge(0.3f, 1.5f), height, 9f));
        TerrainStyle style = Style(new TerrainStyle.PairOverride(stone, gravel,
            new TerrainStyle.EdgeSettings(0.75f, new Gradient(), false, Color.black, Color.black, 0.28f, 1f, 0f, 0f, Color.gray, true)));

        Color[] texels = TerrainBlendLut.BuildTexels(style, new[] { grass, gravel, stone });

        Assert.That(LayerTexel(texels, 0, TerrainBlendLut.LayerHeightColumn).r, Is.EqualTo(0f), "Grass has no height texture.");
        Assert.That(LayerTexel(texels, 1, TerrainBlendLut.LayerHeightColumn).r, Is.EqualTo(1.25f));
        Assert.That(LayerTexel(texels, 2, TerrainBlendLut.LayerHeightColumn).r, Is.EqualTo(TerrainMaterial.MaxHeightScale), "Stone's 9 px is held to the bound.");
        Assert.That(PairTexel(texels, 3, 1, 0, TerrainBlendLut.PairBandColumn), Is.EqualTo(new Color(dirt.r, dirt.g, dirt.b, 1.5f)));
        Assert.That(PairTexel(texels, 3, 2, 1, TerrainBlendLut.PairWidthsColumn).a, Is.EqualTo(1f), "Stone over gravel shows whole stones.");
        Assert.That(PairTexel(texels, 3, 2, 0, TerrainBlendLut.PairWidthsColumn).a, Is.EqualTo(0f));
        Assert.That(PairTexel(texels, 3, 2, -1, TerrainBlendLut.PairWidthsColumn).a, Is.EqualTo(0f));
        Assert.That(PairTexel(texels, 3, 1, 0, TerrainBlendLut.PairWidthsColumn).a, Is.EqualTo(0f), "Gravel has chips, not cut stones.");
        Assert.That(LayerTexel(texels, 2, TerrainBlendLut.LayerChipsColumn).a, Is.EqualTo(1f));
        Assert.That(LayerTexel(texels, 1, TerrainBlendLut.LayerChipsColumn).a, Is.EqualTo(0f));
        Assert.That(LayerTexel(texels, 0, TerrainBlendLut.LayerChipsColumn).a, Is.EqualTo(0f));
    }

    /// <summary>
    /// The height scale is held to its bound in the inspector, in its getter and with a warning when an edited asset
    /// holds more (the expected warning is formatted like the material's, so the numbers match in any culture).
    /// </summary>
    [Test]
    public void TheHeightScale_IsHeldToItsBound()
    {
        UnityEngine.RangeAttribute range = (UnityEngine.RangeAttribute)System.Attribute.GetCustomAttribute(
            typeof(TerrainMaterial).GetField("heightScale", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic), typeof(UnityEngine.RangeAttribute));
        TerrainMaterial material = Track(TerrainMaterial.Create("Stone", null, null, 4, TerrainMaterial.CellMode.CutStones, Color.gray, Edge(0.3f, 1.5f), null, 4f));

        Assert.That(range.max, Is.EqualTo(TerrainMaterial.MaxHeightScale));
        Assert.That(TerrainMaterial.MaxHeightScale, Is.LessThanOrEqualTo(3f));
        Assert.That(material.HeightScale, Is.EqualTo(TerrainMaterial.MaxHeightScale));
        LogAssert.Expect(LogType.Warning, $"Stone: height scale {4f} is outside 0 to {TerrainMaterial.MaxHeightScale} px, the bound that keeps the soft look's blobs attached, so {TerrainMaterial.MaxHeightScale} is used.");
        SerializedObject serialized = new(material);
        serialized.FindProperty("cutGap").floatValue = 1f;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>
    /// An animated material's layer row holds its frames per second, glint period and duty and rim swell, its glint
    /// colour and swell period, its sparkle colour and period, and its sparkle duty and swash; a still material's holds
    /// nothing there, so the shader neither glints, sparkles, swells nor washes it.
    /// </summary>
    [Test]
    public void AnAnimatedMaterial_ReachesItsLayerRow_AndAStillOneLeavesItEmpty()
    {
        TerrainMaterial grass = Material("Grass", 0.1f);
        TerrainMaterial water = Material("Water", 0f);
        SerializedObject settings = new(water);
        settings.FindProperty("framesPerSecond").floatValue = 8f;
        settings.FindProperty("glintColour").colorValue = new Color(0.8f, 0.9f, 1f);
        settings.FindProperty("glintPeriod").floatValue = 3.2f;
        settings.FindProperty("glintDuty").floatValue = 0.18f;
        settings.FindProperty("rimSwell").floatValue = 1.5f;
        settings.FindProperty("swellPeriod").floatValue = 4.5f;
        settings.FindProperty("sparkleColour").colorValue = new Color(0.9f, 1f, 0.95f);
        settings.FindProperty("sparklePeriod").floatValue = 4f;
        settings.FindProperty("sparkleDuty").floatValue = 0.25f;
        settings.FindProperty("swash").floatValue = 5f;
        settings.ApplyModifiedPropertiesWithoutUndo();

        Color[] texels = TerrainBlendLut.BuildTexels(Style(), new[] { grass, water });

        Assert.That(grass.IsAnimated, Is.False);
        Assert.That(water.IsAnimated, Is.True);
        Assert.That(LayerTexel(texels, 0, TerrainBlendLut.LayerAnimationColumn), Is.EqualTo(Color.clear));
        Assert.That(LayerTexel(texels, 0, TerrainBlendLut.LayerGlintColumn), Is.EqualTo(Color.clear));
        Assert.That(LayerTexel(texels, 1, TerrainBlendLut.LayerAnimationColumn), Is.EqualTo(new Color(8f, 3.2f, 0.18f, 1.5f)));
        Assert.That(LayerTexel(texels, 1, TerrainBlendLut.LayerGlintColumn), Is.EqualTo(new Color(0.8f, 0.9f, 1f, 4.5f)));
        Assert.That(LayerTexel(texels, 1, TerrainBlendLut.LayerSparkleColumn), Is.EqualTo(new Color(0.9f, 1f, 0.95f, 4f)));
        Assert.That(LayerTexel(texels, 1, TerrainBlendLut.LayerSparkleTimingColumn), Is.EqualTo(new Color(0.25f, 5f, 0f, 0f)));
        Assert.That(LayerTexel(texels, 0, TerrainBlendLut.LayerSparkleColumn), Is.EqualTo(Color.clear));
    }

    /// <summary>
    /// An animated material with waves holds them in its layer row: (spacing, period, width, tangent of the angle), its
    /// wave colour with the distance within which waves run whole, its crest colour with its stretch, its trough colour,
    /// and its swash's foam colour with how wet it leaves the shore, then the foam's shade, the film's and the film's
    /// back colours. A material without waves, or with waves but no frames per second, holds nothing there, so the
    /// shader draws it no waves.
    /// </summary>
    [Test]
    public void AMaterialWithWaves_ReachesItsLayerRow_AndOneWithoutLeavesItEmpty()
    {
        TerrainMaterial water = Material("Water", 0f);
        TerrainMaterial ocean = Material("Ocean", 0f);
        TerrainMaterial still = Material("Still", 0f);
        SerializedObject animated = new(water);
        animated.FindProperty("framesPerSecond").floatValue = 8f;
        animated.ApplyModifiedPropertiesWithoutUndo();
        foreach (TerrainMaterial material in new[] { ocean, still })
        {
            SerializedObject settings = new(material);
            settings.FindProperty("framesPerSecond").floatValue = material == ocean ? 8f : 0f;
            settings.FindProperty("waveSpacing").floatValue = 48f;
            settings.FindProperty("wavePeriod").floatValue = 3.2f;
            settings.FindProperty("waveWidth").floatValue = 10f;
            settings.FindProperty("waveAngle").floatValue = 45f;
            settings.FindProperty("waveWholeWithin").floatValue = 40f;
            settings.FindProperty("waveStretch").floatValue = 0.05f;
            settings.FindProperty("waveColour").colorValue = new Color(0.1f, 0.2f, 0.3f);
            settings.FindProperty("waveCrestColour").colorValue = new Color(0.4f, 0.5f, 0.6f);
            settings.FindProperty("waveTroughColour").colorValue = new Color(0.7f, 0.8f, 0.9f);
            settings.FindProperty("washFoamColour").colorValue = new Color(1f, 0.9f, 0.8f);
            settings.FindProperty("washFoamShadeColour").colorValue = new Color(0.7f, 0.6f, 0.5f);
            settings.FindProperty("washFilmColour").colorValue = new Color(0.4f, 0.3f, 0.2f);
            settings.FindProperty("washFilmBackColour").colorValue = new Color(0.1f, 0f, 0.1f);
            settings.FindProperty("washWet").floatValue = 0.15f;
            settings.ApplyModifiedPropertiesWithoutUndo();
        }

        Color[] texels = TerrainBlendLut.BuildTexels(Style(), new[] { water, ocean, still });

        Assert.That(water.HasWaves, Is.False);
        Assert.That(ocean.HasWaves, Is.True);
        Assert.That(still.HasWaves, Is.False);
        Color waves = LayerTexel(texels, 1, TerrainBlendLut.LayerWavesColumn);
        Assert.That(new Vector3(waves.r, waves.g, waves.b), Is.EqualTo(new Vector3(48f, 3.2f, 10f)));
        Assert.That(waves.a, Is.EqualTo(1f).Within(1e-5f));
        Assert.That(LayerTexel(texels, 1, TerrainBlendLut.LayerWaveColourColumn), Is.EqualTo(new Color(0.1f, 0.2f, 0.3f, 40f)));
        Assert.That(LayerTexel(texels, 1, TerrainBlendLut.LayerWaveCrestColumn), Is.EqualTo(new Color(0.4f, 0.5f, 0.6f, 0.05f)));
        Assert.That(LayerTexel(texels, 1, TerrainBlendLut.LayerWaveTroughColumn), Is.EqualTo(new Color(0.7f, 0.8f, 0.9f, 1f)));
        Assert.That(LayerTexel(texels, 1, TerrainBlendLut.LayerWashFoamColumn), Is.EqualTo(new Color(1f, 0.9f, 0.8f, 0.15f)));
        Assert.That(LayerTexel(texels, 1, TerrainBlendLut.LayerWashFoamShadeColumn), Is.EqualTo(new Color(0.7f, 0.6f, 0.5f, 1f)));
        Assert.That(LayerTexel(texels, 1, TerrainBlendLut.LayerWashFilmColumn), Is.EqualTo(new Color(0.4f, 0.3f, 0.2f, 1f)));
        Assert.That(LayerTexel(texels, 1, TerrainBlendLut.LayerWashFilmBackColumn), Is.EqualTo(new Color(0.1f, 0f, 0.1f, 1f)));
        foreach (int layer in new[] { 0, 2 })
        {
            for (int column = TerrainBlendLut.LayerWavesColumn; column <= TerrainBlendLut.LayerWashFilmBackColumn; column++)
            {
                Assert.That(LayerTexel(texels, layer, column), Is.EqualTo(Color.clear), $"layer {layer}, column {column}");
            }
        }
    }

    /// <summary>
    /// A sunken material's layer row holds its sink depth and bank shadow; a level material's holds nothing there, so
    /// the shader draws it no bank. Every material's row holds its bank colour, which a sunken layer cutting into it
    /// shows.
    /// </summary>
    [Test]
    public void ASunkenMaterial_ReachesItsLayerRow_AndALevelOneLeavesItEmpty()
    {
        TerrainMaterial grass = Material("Grass", 0.1f);
        TerrainMaterial water = Material("Water", 0f);
        SerializedObject settings = new(water);
        settings.FindProperty("sinkDepth").floatValue = 4.5f;
        settings.FindProperty("bankShadow").floatValue = 0.35f;
        settings.ApplyModifiedPropertiesWithoutUndo();
        SerializedObject grassSettings = new(grass);
        grassSettings.FindProperty("bankColour").colorValue = new Color(0.4f, 0.3f, 0.2f);
        grassSettings.ApplyModifiedPropertiesWithoutUndo();

        Color[] texels = TerrainBlendLut.BuildTexels(Style(), new[] { grass, water });

        Assert.That(LayerTexel(texels, 0, TerrainBlendLut.LayerBankColumn), Is.EqualTo(Color.clear));
        Assert.That(LayerTexel(texels, 1, TerrainBlendLut.LayerBankColumn), Is.EqualTo(new Color(4.5f, 0.35f, 0f, 0f)));
        Assert.That(LayerTexel(texels, 0, TerrainBlendLut.LayerBankColourColumn), Is.EqualTo(new Color(0.4f, 0.3f, 0.2f)));
    }

    /// <summary>
    /// For layers drawn over raised ground, row 0 holds the style's cliff settings at its cliff columns: the cliff's
    /// height, fringe and corner radii; its bands, band shade, depth shade and pebble chance; its foot shade, outline
    /// shade, rim light and pebble light; its shadow's alpha and height, the ramp's shade; the ledge's width.
    /// Without raised ground they stay empty, which is what tells the shader to draw no cliffs.
    /// </summary>
    [Test]
    public void RaisedGround_ReachesTheSharedRow_AndLayersWithoutItLeaveItEmpty()
    {
        TerrainMaterial grass = Material("Grass", 0.1f);
        TerrainStyle style = Style();
        SerializedObject settings = new(style);
        settings.FindProperty("cliffHeight").floatValue = 12f;
        settings.FindProperty("cliffRimWidth").floatValue = 4f;
        settings.FindProperty("rampShade").floatValue = 0.5f;
        settings.ApplyModifiedPropertiesWithoutUndo();

        Color[] raised = TerrainBlendLut.BuildTexels(style, new[] { grass }, true);
        Color[] level = TerrainBlendLut.BuildTexels(style, new[] { grass });

        int row = TerrainBlendLut.SharedRow * TerrainBlendLut.Width;
        Assert.That(raised[row + TerrainBlendLut.SharedCliffColumn], Is.EqualTo(new Color(12f, style.CliffFringe, style.CliffOuterRadius, style.CliffInnerRadius)));
        Assert.That(raised[row + TerrainBlendLut.SharedCliffFaceColumn], Is.EqualTo(new Color(style.CliffBandRows, style.CliffBandShade, style.CliffDepthShade, style.CliffPebbleChance)));
        Assert.That(raised[row + TerrainBlendLut.SharedCliffEdgeColumn], Is.EqualTo(new Color(style.CliffFootShade, style.CliffOutlineShade, style.CliffRimLight, style.CliffPebbleLight)));
        Assert.That(raised[row + TerrainBlendLut.SharedRampColumn], Is.EqualTo(new Color(0.5f, 0f, 0f, 0f)));
        Assert.That(raised[row + TerrainBlendLut.SharedCliffRimColumn], Is.EqualTo(new Color(4f, 0f, 0f, 0f)));
        for (int column = TerrainBlendLut.SharedCliffColumn; column <= TerrainBlendLut.SharedCliffRimColumn; column++)
        {
            Assert.That(level[row + column], Is.EqualTo(Color.clear), $"column {column}");
        }

        for (int i = TerrainBlendLut.Width; i < raised.Length; i++)
        {
            Assert.That(raised[i], Is.EqualTo(level[i]), $"texel {i}");
        }
    }

    /// <summary>
    /// A swaying material's layer row holds its sway amplitude, sway period and gust length, and 1 last when its clumps
    /// lean whole (0 by default), and a still or merely animated one's holds nothing there; a pair with a seamless edge
    /// writes its rim's outer colour with alpha 0, any other pair with alpha 1.
    /// </summary>
    [Test]
    public void ASwayingMaterial_ReachesItsLayerRow_AndASeamlessEdgeMarksItsPair()
    {
        TerrainMaterial grass = Material("Grass", 0.1f);
        TerrainMaterial weeds = Material("Weeds", 0f);
        SerializedObject settings = new(weeds);
        settings.FindProperty("framesPerSecond").floatValue = 8f;
        settings.FindProperty("swayAmplitude").floatValue = 2f;
        settings.FindProperty("swayPeriod").floatValue = 2.5f;
        settings.FindProperty("gustLength").floatValue = 12f;
        settings.FindProperty("edge").FindPropertyRelative("seamless").boolValue = true;
        settings.ApplyModifiedPropertiesWithoutUndo();

        Color[] texels = TerrainBlendLut.BuildTexels(Style(), new[] { grass, weeds });

        Assert.That(weeds.Sways, Is.True);
        Assert.That(grass.Sways, Is.False);
        Assert.That(LayerTexel(texels, 0, TerrainBlendLut.LayerSwayColumn), Is.EqualTo(Color.clear));
        Assert.That(LayerTexel(texels, 1, TerrainBlendLut.LayerSwayColumn), Is.EqualTo(new Color(2f, 2.5f, 12f, 0f)));
        Assert.That(PairTexel(texels, 2, 1, 0, TerrainBlendLut.PairFillColumn).a, Is.Zero, "weeds over grass is seamless");
        Assert.That(PairTexel(texels, 2, 0, -1, TerrainBlendLut.PairFillColumn).a, Is.EqualTo(1f), "grass over nothing is not");

        settings.FindProperty("wholeClumps").boolValue = true;
        settings.ApplyModifiedPropertiesWithoutUndo();
        Assert.That(LayerTexel(TerrainBlendLut.BuildTexels(Style(), new[] { grass, weeds }), 1, TerrainBlendLut.LayerSwayColumn), Is.EqualTo(new Color(2f, 2.5f, 12f, 1f)));
    }

    /// <summary>
    /// The project's grass is still (TerrainTypes.md, Phase 12): not animated, with no clumps and so no cell field,
    /// level with the ground, keeping its outlined edge with tufts over nothing, the map's edge. The weeds, right over
    /// the grass and the dry grass in the shared order, sway in the grass's old wind (2 px, 2.5 s, gusts 12 cells long)
    /// with their clumps' weights in the height and the ground under them in a cell field, keep the grass's edge over
    /// nothing, and are seamless over the grass, whose ground they share, and over dry grass with green tufts; the
    /// meadow is seamless over them.
    /// </summary>
    [Test]
    public void TheProjectsGrass_IsStill_AndItsWeedsSwayOverIt()
    {
        TerrainStyle style = AssetDatabase.LoadAssetAtPath<TerrainStyle>("Assets/Settings/Terrain/TerrainStyle.asset");
        TerrainMaterial grass = ProjectMaterial("Grass");
        TerrainMaterial dryGrass = ProjectMaterial("DryGrass");
        TerrainMaterial weeds = ProjectMaterial("Weeds");
        TerrainMaterial meadow = ProjectMaterial("Meadow");

        Assert.That(grass.IsAnimated, Is.False);
        Assert.That(grass.Sways, Is.False);
        Assert.That(grass.CellField, Is.Null);
        Assert.That(grass.SinkDepth, Is.Zero);
        Assert.That(style.GetEdge(grass, null), Is.SameAs(grass.Edge));
        Assert.That(grass.Edge.DrawOutline, Is.True);
        Assert.That(grass.Edge.TuftHeight, Is.GreaterThan(0f));

        Assert.That(style.LayerOrderIndexOf(weeds), Is.EqualTo(style.LayerOrderIndexOf(dryGrass) + 1));
        Assert.That(weeds.Sways, Is.True);
        Assert.That(weeds.SwayAmplitude, Is.EqualTo(2f));
        Assert.That(weeds.SwayPeriod, Is.EqualTo(2.5f));
        Assert.That(weeds.GustLength, Is.EqualTo(12f));
        Assert.That(weeds.WholeClumps, Is.False, "The weeds sway per cell, as the grass did.");
        Assert.That(weeds.Height, Is.Not.Null, "The sway weights live in the height texture.");
        Assert.That(weeds.CellField, Is.Not.Null, "The ground under the clumps.");
        Assert.That(weeds.BankColour, Is.EqualTo(grass.BankColour));
        Assert.That(style.GetEdge(weeds, null), Is.SameAs(weeds.Edge));
        Assert.That(weeds.Edge.DrawOutline, Is.EqualTo(grass.Edge.DrawOutline));
        Assert.That(weeds.Edge.TuftHeight, Is.EqualTo(grass.Edge.TuftHeight));
        TerrainStyle.EdgeSettings overGrass = style.GetEdge(weeds, grass);
        Assert.That(overGrass.Seamless, Is.True);
        Assert.That(overGrass.TuftHeight, Is.Zero, "The same ground: nothing to fray.");
        TerrainStyle.EdgeSettings overDryGrass = style.GetEdge(weeds, dryGrass);
        Assert.That(overDryGrass.Seamless, Is.True);
        Assert.That(overDryGrass.TuftHeight, Is.GreaterThan(0f), "Green tufts fray into the straw.");
        Assert.That(style.GetEdge(meadow, weeds).Seamless, Is.True);

        Color[] texels = TerrainBlendLut.BuildTexels(style, new[] { grass, dryGrass, weeds, meadow });
        Assert.That(PairTexel(texels, 4, 2, 0, TerrainBlendLut.PairFillColumn).a, Is.Zero, "weeds over grass is seamless");
        Assert.That(PairTexel(texels, 4, 2, 1, TerrainBlendLut.PairFillColumn).a, Is.Zero, "weeds over dry grass is seamless");
        Assert.That(PairTexel(texels, 4, 3, 2, TerrainBlendLut.PairFillColumn).a, Is.Zero, "the meadow over weeds is seamless");
        Assert.That(LayerTexel(texels, 0, TerrainBlendLut.LayerSwayColumn), Is.EqualTo(Color.clear), "the grass is still");
        Assert.That(LayerTexel(texels, 2, TerrainBlendLut.LayerSwayColumn), Is.EqualTo(new Color(2f, 2.5f, 12f, 0f)));
    }

    /// <summary>
    /// The project's water is animated, sunken and drawn over every other material: over sand its style override draws no
    /// outline and a wet sand shadow, while over grass, mud, gravel, stone and nothing it keeps its own default edge,
    /// an outline with a shore rim. Its texture's glint phases are what it animates.
    /// </summary>
    [Test]
    public void TheProjectsWater_IsAnimated_AndMeetsSandWithoutAnOutline()
    {
        TerrainStyle style = AssetDatabase.LoadAssetAtPath<TerrainStyle>("Assets/Settings/Terrain/TerrainStyle.asset");
        string[] names = { "Grass", "Sand", "Mud", "Gravel", "Stone", "Water" };
        TerrainMaterial[] materials = new TerrainMaterial[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            materials[i] = AssetDatabase.LoadAssetAtPath<TerrainMaterial>($"Assets/Settings/Terrain/Materials/{names[i]}.asset");
            Assert.That(materials[i], Is.Not.Null, names[i]);
        }

        TerrainMaterial water = materials[5];
        TerrainStyle.EdgeSettings overSand = style.GetEdge(water, materials[1]);

        Assert.That(water.IsAnimated, Is.True);
        Assert.That(water.SinkDepth, Is.GreaterThan(0f), "Water lies below the ground around it.");
        Assert.That(water.Height, Is.Not.Null, "The glint phases live in the height texture.");
        Assert.That(overSand, Is.Not.SameAs(water.Edge));
        Assert.That(overSand.DrawOutline, Is.False);
        Assert.That(overSand.ShadowAlpha, Is.GreaterThan(0f));
        Assert.That(water.Edge.DrawOutline, Is.True);
        Assert.That(water.Edge.RimWidth, Is.GreaterThan(4f));
        foreach (TerrainMaterial lower in new[] { null, materials[0], materials[2], materials[3], materials[4] })
        {
            Assert.That(style.GetEdge(water, lower), Is.SameAs(water.Edge), lower != null ? lower.name : "nothing");
        }

        for (int i = 0; i < 5; i++)
        {
            Assert.That(materials[i].IsAnimated, Is.EqualTo(materials[i].Sways), $"{names[i]} is animated only to sway, without glints");
            Assert.That(materials[i].SinkDepth, Is.Zero, names[i]);
        }
    }

    /// <summary>
    /// The project's style gives stone over gravel its own edge (a thin mortar rim, no outline, a neutral shadow, whole
    /// stones in the soft look) and leaves every other pair of the project's materials on its upper material's default
    /// edge.
    /// </summary>
    [Test]
    public void TheProjectStonesOverGravelOverride_AppliesToItsPairOnly()
    {
        TerrainStyle style = AssetDatabase.LoadAssetAtPath<TerrainStyle>("Assets/Settings/Terrain/TerrainStyle.asset");
        TerrainMaterial[] materials =
        {
            AssetDatabase.LoadAssetAtPath<TerrainMaterial>("Assets/Settings/Terrain/Materials/Grass.asset"),
            AssetDatabase.LoadAssetAtPath<TerrainMaterial>("Assets/Settings/Terrain/Materials/Gravel.asset"),
            AssetDatabase.LoadAssetAtPath<TerrainMaterial>("Assets/Settings/Terrain/Materials/Stone.asset")
        };
        Assert.That(style, Is.Not.Null);
        Assert.That(materials, Has.All.Not.Null);

        Color[] texels = TerrainBlendLut.BuildTexels(style, materials);
        TerrainStyle.EdgeSettings stoneOverGravel = style.GetEdge(materials[2], materials[1]);

        Assert.That(stoneOverGravel, Is.Not.SameAs(materials[2].Edge));
        Assert.That(stoneOverGravel.DrawOutline, Is.False);
        Assert.That(stoneOverGravel.RimWidth, Is.LessThan(materials[2].Edge.RimWidth));
        Assert.That(stoneOverGravel.SoftWholeStones, Is.True);
        Assert.That(PairTexel(texels, 3, 2, 1, TerrainBlendLut.PairOutlineColumn).a, Is.EqualTo(0f));
        Assert.That(PairTexel(texels, 3, 2, 1, TerrainBlendLut.PairWidthsColumn).a, Is.EqualTo(1f));
        for (int upper = 0; upper < 3; upper++)
        {
            for (int lower = -1; lower < upper; lower++)
            {
                if (upper == 2 && lower == 1)
                {
                    continue;
                }

                TerrainMaterial lowerMaterial = lower < 0 ? null : materials[lower];
                Assert.That(style.GetEdge(materials[upper], lowerMaterial), Is.SameAs(materials[upper].Edge), $"{upper} over {lower}");
                Assert.That(PairTexel(texels, 3, upper, lower, TerrainBlendLut.PairWidthsColumn).a, Is.EqualTo(0f), $"{upper} over {lower}");
            }
        }
    }

    /// <summary>
    /// The project's dirt, dry grass and plaza (TerrainTypes.md, Phase 3) meet the other materials as decided: dry grass
    /// over grass is seamless, with no outline, rim or shadow but tufts, and marked seamless in the lookup texture, while
    /// over nothing it keeps an outlined edge; dirt draws its default edge, an outline and a fringe of tufts, over grass,
    /// dry grass and nothing; sand, mud, gravel and stone draw their own rim and outline over dirt with an earth
    /// shadow in place of their green one; the plaza cuts its slabs like stone, draws stone's default edge over grass,
    /// and its pairs with gravel and stone copy stone over gravel's edge.
    /// </summary>
    [Test]
    public void TheProjectsDirtDryGrassAndPlaza_MeetTheOtherMaterialsAsDecided()
    {
        TerrainStyle style = AssetDatabase.LoadAssetAtPath<TerrainStyle>("Assets/Settings/Terrain/TerrainStyle.asset");
        TerrainMaterial grass = ProjectMaterial("Grass");
        TerrainMaterial dryGrass = ProjectMaterial("DryGrass");
        TerrainMaterial dirt = ProjectMaterial("Dirt");
        TerrainMaterial plaza = ProjectMaterial("Plaza");
        TerrainMaterial gravel = ProjectMaterial("Gravel");
        TerrainMaterial stone = ProjectMaterial("Stone");

        TerrainStyle.EdgeSettings dryOverGrass = style.GetEdge(dryGrass, grass);
        Assert.That(dryOverGrass.Seamless, Is.True);
        Assert.That(dryOverGrass.DrawOutline, Is.False);
        Assert.That(dryOverGrass.RimWidth, Is.Zero);
        Assert.That(dryOverGrass.ShadowAlpha, Is.Zero);
        Assert.That(dryOverGrass.TuftHeight, Is.GreaterThan(0f), "Straw tufts fray into the grass.");
        Assert.That(style.GetEdge(dryGrass, null), Is.SameAs(dryGrass.Edge));
        Assert.That(dryGrass.Sways, Is.True, "Its dry stalks sway like the grass's clumps.");
        Assert.That(dryGrass.SinkDepth, Is.Zero);
        Assert.That(dryGrass.Edge.DrawOutline, Is.True);
        Color[] texels = TerrainBlendLut.BuildTexels(style, new[] { grass, dryGrass });
        Assert.That(PairTexel(texels, 2, 1, 0, TerrainBlendLut.PairFillColumn).a, Is.Zero, "dry grass over grass is seamless");
        Assert.That(PairTexel(texels, 2, 1, -1, TerrainBlendLut.PairFillColumn).a, Is.EqualTo(1f), "dry grass over nothing is not");

        foreach (TerrainMaterial lower in new[] { null, grass, dryGrass })
        {
            Assert.That(style.GetEdge(dirt, lower), Is.SameAs(dirt.Edge), lower != null ? lower.name : "nothing");
        }

        Assert.That(dirt.Edge.DrawOutline, Is.True);
        Assert.That(dirt.Edge.TuftHeight, Is.GreaterThan(0f), "A fringe of dry blades.");
        Assert.That(dirt.Cells, Is.EqualTo(TerrainMaterial.CellMode.None));

        foreach (string upperName in new[] { "Sand", "Mud", "Gravel", "Stone" })
        {
            TerrainMaterial upper = ProjectMaterial(upperName);
            TerrainStyle.EdgeSettings overDirt = style.GetEdge(upper, dirt);
            Assert.That(overDirt, Is.Not.SameAs(upper.Edge), upperName);
            Assert.That(overDirt.RimWidth, Is.EqualTo(upper.Edge.RimWidth), upperName);
            Assert.That(overDirt.DrawOutline, Is.EqualTo(upper.Edge.DrawOutline), upperName);
            Assert.That(upper.Edge.ShadowColour.g, Is.GreaterThan(upper.Edge.ShadowColour.r), $"{upperName}'s default shadow is green");
            Assert.That(overDirt.ShadowColour.r, Is.GreaterThan(overDirt.ShadowColour.g), $"{upperName}'s shadow over dirt is earth");
        }

        Assert.That(plaza.Cells, Is.EqualTo(TerrainMaterial.CellMode.CutStones));
        Assert.That(style.GetEdge(plaza, grass), Is.SameAs(plaza.Edge));
        Assert.That(plaza.Edge.RimWidth, Is.EqualTo(stone.Edge.RimWidth));
        Assert.That(plaza.Edge.SoftBandWidth, Is.EqualTo(stone.Edge.SoftBandWidth));
        TerrainStyle.EdgeSettings stoneOverGravel = style.GetEdge(stone, gravel);
        foreach (TerrainStyle.EdgeSettings pair in new[] { style.GetEdge(plaza, gravel), style.GetEdge(stone, plaza) })
        {
            Assert.That(pair, Is.Not.SameAs(stoneOverGravel));
            Assert.That(pair.RimWidth, Is.EqualTo(stoneOverGravel.RimWidth));
            Assert.That(pair.DrawOutline, Is.False);
            Assert.That(pair.ShadowColour, Is.EqualTo(stoneOverGravel.ShadowColour));
            Assert.That(pair.SoftWholeStones, Is.True);
        }
    }

    /// <summary>
    /// The project's shallows (TerrainTypes.md, Phase 4) glint like water, sink less than it and share its corner radii,
    /// so their edges coincide where both meet the shore; they meet every material with a soft shore, no outline, and
    /// over sand with a wet sand shadow; the deep water meets them seamlessly, with no outline or shadow but a rim that
    /// fades it in, marked seamless in the lookup texture, while it keeps its own edge over sand and its default one
    /// elsewhere.
    /// </summary>
    [Test]
    public void TheProjectsShallows_GlintSinkLessThanWater_AndTheWaterFadesIntoThem()
    {
        TerrainStyle style = AssetDatabase.LoadAssetAtPath<TerrainStyle>("Assets/Settings/Terrain/TerrainStyle.asset");
        TerrainMaterial sand = ProjectMaterial("Sand");
        TerrainMaterial shallows = ProjectMaterial("Shallows");
        TerrainMaterial water = ProjectMaterial("Water");

        Assert.That(shallows.IsAnimated && !shallows.Sways, Is.True, "The shallows glint.");
        Assert.That(shallows.Height, Is.Not.Null, "The glint phases live in the height texture.");
        Assert.That(shallows.SinkDepth, Is.InRange(1f, 2f));
        Assert.That(shallows.SinkDepth, Is.LessThan(water.SinkDepth));
        Assert.That(shallows.OuterRadius, Is.EqualTo(water.OuterRadius));
        Assert.That(shallows.InnerRadius, Is.EqualTo(water.InnerRadius));
        Assert.That(shallows.Edge.DrawOutline, Is.False, "A soft shore.");
        foreach (string lowerName in new[] { "Grass", "Mud", "Gravel", "Stone" })
        {
            Assert.That(style.GetEdge(shallows, ProjectMaterial(lowerName)), Is.SameAs(shallows.Edge), lowerName);
        }

        TerrainStyle.EdgeSettings overSand = style.GetEdge(shallows, sand);
        Assert.That(overSand, Is.Not.SameAs(shallows.Edge));
        Assert.That(overSand.DrawOutline, Is.False);
        Assert.That(overSand.ShadowColour.r, Is.GreaterThan(overSand.ShadowColour.b), "A wet sand shadow.");

        TerrainStyle.EdgeSettings intoShallows = style.GetEdge(water, shallows);
        Assert.That(intoShallows.Seamless, Is.True);
        Assert.That(intoShallows.DrawOutline, Is.False);
        Assert.That(intoShallows.ShadowAlpha, Is.Zero);
        Assert.That(intoShallows.RimWidth, Is.GreaterThan(0f), "The deep water fades in.");
        Assert.That(style.GetEdge(water, sand), Is.Not.SameAs(intoShallows));
        Color[] texels = TerrainBlendLut.BuildTexels(style, new[] { sand, shallows, water });
        Assert.That(PairTexel(texels, 3, 2, 1, TerrainBlendLut.PairFillColumn).a, Is.Zero, "water over shallows is seamless");
        Assert.That(PairTexel(texels, 3, 2, 0, TerrainBlendLut.PairFillColumn).a, Is.EqualTo(1f), "water over sand is not");
        Assert.That(PairTexel(texels, 3, 1, 0, TerrainBlendLut.PairFillColumn).a, Is.EqualTo(1f), "shallows over sand is not");
        Assert.That(LayerTexel(texels, 1, TerrainBlendLut.LayerBankColumn).r, Is.EqualTo(shallows.SinkDepth));
    }

    /// <summary>
    /// Row 0's last value holds a bit for every layer whose material is a bridge, and a layer row's height column marks
    /// a material that turns to its runs; neither is set for ordinary materials, so their rows and row 0 are as before.
    /// </summary>
    [Test]
    public void BridgesAndMaterialsThatTurnToTheirRuns_ReachTheirRows()
    {
        TerrainMaterial grass = Material("Grass", 0.1f);
        TerrainMaterial water = Material("Water", 0.2f);
        TerrainMaterial dock = Material("Dock", 0.3f);
        TerrainMaterial walk = Material("Walk", 0.3f);
        foreach (TerrainMaterial material in new[] { dock, walk })
        {
            SerializedObject settings = new(material);
            settings.FindProperty("bridge").boolValue = material == dock;
            settings.FindProperty("turnToRuns").boolValue = true;
            settings.ApplyModifiedPropertiesWithoutUndo();
        }

        TerrainStyle style = Style();

        Color[] plain = TerrainBlendLut.BuildTexels(style, new[] { grass, water });
        Color[] texels = TerrainBlendLut.BuildTexels(style, new[] { grass, water, walk, dock });

        Assert.That(plain[0].a, Is.Zero, "No bridge.");
        Assert.That(TerrainBlendLut.BridgeBits(new[] { grass, water }), Is.Zero);
        Assert.That(texels[TerrainBlendLut.SharedRow * TerrainBlendLut.Width + TerrainBlendLut.SharedColumn].a, Is.EqualTo(8f), "Layer 3 is a bridge.");
        Assert.That(texels[TerrainBlendLut.SharedRow * TerrainBlendLut.Width + TerrainBlendLut.SharedHighBridgesColumn], Is.EqualTo(Color.clear), "No bridge past layer 15.");
        Assert.That(TerrainBlendLut.BridgeBits(new[] { dock, grass, dock }), Is.EqualTo(5u));
        Assert.That(LayerTexel(texels, 0, TerrainBlendLut.LayerHeightColumn).g, Is.Zero);
        Assert.That(LayerTexel(texels, 1, TerrainBlendLut.LayerHeightColumn).g, Is.Zero);
        Assert.That(LayerTexel(texels, 2, TerrainBlendLut.LayerHeightColumn).g, Is.EqualTo(1f), "The walk turns to its runs.");
        Assert.That(LayerTexel(texels, 3, TerrainBlendLut.LayerHeightColumn).g, Is.EqualTo(1f), "The dock turns to its runs.");
    }

    /// <summary>
    /// With 32 layers, bridges at layers 3, 17 and 31 reach both halves of row 0's bridge bits: layer 3's bit in the
    /// shared texel's last value and layers 17's and 31's as bits 1 and 15 of the high half's column, each half a whole
    /// number a float holds exactly; nothing else in that column.
    /// </summary>
    [Test]
    public void TheBridgeBitsOfLayersPastSixteen_ReachTheHighHalf()
    {
        TerrainMaterial ground = Material("Ground", 0.1f);
        TerrainMaterial dock = Material("Dock", 0.3f);
        SerializedObject settings = new(dock);
        settings.FindProperty("bridge").boolValue = true;
        settings.ApplyModifiedPropertiesWithoutUndo();
        TerrainMaterial[] layers = new TerrainMaterial[32];
        for (int i = 0; i < layers.Length; i++)
        {
            layers[i] = i == 3 || i == 17 || i == 31 ? dock : ground;
        }

        Color[] texels = TerrainBlendLut.BuildTexels(Style(), layers);
        int shared = TerrainBlendLut.SharedRow * TerrainBlendLut.Width;

        Assert.That(TerrainBlendLut.BridgeBits(layers), Is.EqualTo((1u << 3) | (1u << 17) | (1u << 31)));
        Assert.That(texels.Length, Is.EqualTo(TerrainBlendLut.Width * TerrainBlendLut.Height(32)));
        Assert.That(texels[shared + TerrainBlendLut.SharedColumn].a, Is.EqualTo(8f), "The low half holds layer 3.");
        Assert.That(texels[shared + TerrainBlendLut.SharedHighBridgesColumn], Is.EqualTo(new Color((1 << 1) | (1 << 15), 0f, 0f, 0f)), "The high half holds layers 17 and 31.");
    }

    /// <summary>
    /// Each layer's slice of the texture arrays reaches its row as the first value of its slice column: the slice given
    /// for it (World scale's Phase 2, arrays shared by the style's renderers), and its own index when none is given.
    /// </summary>
    [Test]
    public void EachLayersSlice_ReachesItsRow_ItsOwnIndexWhenNoneIsGiven()
    {
        TerrainMaterial[] layers = { Material("Grass", 0.1f), Material("Gravel", 0.2f), Material("Stone", 0.3f) };

        Color[] shared = TerrainBlendLut.BuildTexels(Style(), layers, false, new[] { 4, 0, 17 });
        Color[] own = TerrainBlendLut.BuildTexels(Style(), layers);

        Assert.That(LayerTexel(shared, 0, TerrainBlendLut.LayerSliceColumn), Is.EqualTo(new Color(4f, 0f, 0f, 0f)));
        Assert.That(LayerTexel(shared, 1, TerrainBlendLut.LayerSliceColumn), Is.EqualTo(new Color(0f, 0f, 0f, 0f)));
        Assert.That(LayerTexel(shared, 2, TerrainBlendLut.LayerSliceColumn), Is.EqualTo(new Color(17f, 0f, 0f, 0f)));
        for (int layer = 0; layer < layers.Length; layer++)
        {
            Assert.That(LayerTexel(own, layer, TerrainBlendLut.LayerSliceColumn), Is.EqualTo(new Color(layer, 0f, 0f, 0f)), $"layer {layer}");
        }
    }

    /// <summary>
    /// A material's reflection reaches its layer's row as the first value of its reflection column, and one that reflects
    /// nothing holds nothing there (Docs/Lighting.md, Phase 10). Of the project's materials the water and the sea reflect
    /// fully, the shallows at half, and nothing else does.
    /// </summary>
    [Test]
    public void AMaterialsReflection_ReachesItsRow_AndOnlyTheProjectsWaterReflects()
    {
        TerrainMaterial grass = Material("Grass", 0.1f);
        TerrainMaterial water = Material("Water", 0f);
        water.SetReflection(0.6f);

        Color[] texels = TerrainBlendLut.BuildTexels(Style(), new[] { grass, water });

        Assert.That(LayerTexel(texels, 0, TerrainBlendLut.LayerReflectionColumn), Is.EqualTo(Color.clear));
        Assert.That(LayerTexel(texels, 1, TerrainBlendLut.LayerReflectionColumn), Is.EqualTo(new Color(0.6f, 0f, 0f, 0f)));
        Dictionary<string, float> reflecting = new() { ["Water"] = 1f, ["Shallows"] = 0.5f, ["Ocean"] = 1f };
        foreach (string path in Directory.GetFiles("Assets/Settings/Terrain/Materials", "*.asset"))
        {
            string name = Path.GetFileNameWithoutExtension(path);
            float expected = reflecting.TryGetValue(name, out float strength) ? strength : 0f;
            Assert.That(ProjectMaterial(name).Reflection, Is.EqualTo(expected), name);
        }
    }

    /// <summary>
    /// The project's planks (TerrainTypes.md, Phase 5) are a level, still bridge that turns to its runs, drawn last in
    /// the shared order, with nearly square corners and boards cut along the rim; they draw a dark outline and an earthy
    /// shadow over every material, and over the water and the shallows a wider, darker shadow of their own.
    /// </summary>
    [Test]
    public void TheProjectsPlanks_AreALevelBridgeDrawnLast_WithAShadowOnTheWater()
    {
        TerrainStyle style = AssetDatabase.LoadAssetAtPath<TerrainStyle>("Assets/Settings/Terrain/TerrainStyle.asset");
        TerrainMaterial planks = ProjectMaterial("Planks");

        Assert.That(style.LayerOrderIndexOf(planks), Is.EqualTo(style.LayerOrder.Count - 1), "Planks are drawn last.");
        Assert.That(planks.Bridge && planks.TurnsToRuns, Is.True);
        Assert.That(planks.SinkDepth, Is.Zero);
        Assert.That(planks.IsAnimated, Is.False);
        Assert.That(planks.Cells, Is.EqualTo(TerrainMaterial.CellMode.CutStones));
        Assert.That(planks.OuterRadius, Is.LessThanOrEqualTo(3f));
        Assert.That(planks.Edge.DrawOutline, Is.True);
        foreach (string lowerName in new[] { "Grass", "Sand", "Gravel", "Stone", "Mud", "Dirt" })
        {
            Assert.That(style.GetEdge(planks, ProjectMaterial(lowerName)), Is.SameAs(planks.Edge), lowerName);
        }

        foreach (string lowerName in new[] { "Water", "Shallows" })
        {
            TerrainStyle.EdgeSettings overWater = style.GetEdge(planks, ProjectMaterial(lowerName));
            Assert.That(overWater, Is.Not.SameAs(planks.Edge), lowerName);
            Assert.That(overWater.DrawOutline, Is.True, lowerName);
            Assert.That(overWater.ShadowWidth, Is.GreaterThan(planks.Edge.ShadowWidth), lowerName);
            Assert.That(overWater.ShadowAlpha, Is.GreaterThan(planks.Edge.ShadowAlpha), lowerName);
            Assert.That(overWater.ShadowColour.b, Is.GreaterThan(overWater.ShadowColour.r), $"{lowerName}: a shadow on the water");
        }
    }

    /// <summary>
    /// The project's meadow and tall grass (TerrainTypes.md, Phase 6) come right after the weeds in the shared order,
    /// level, with no cell mode. The meadow is still (Phase 12): flowers on the grass's ground, not animated. Tall grass
    /// sways in the weeds' wind, further than the weeds, its clumps leaning whole (the weeds' and dry grass's do not).
    /// The meadow is
    /// seamless over the grass, with no outline, rim, shadow or tufts (its ground is the grass's), and seamless over dry
    /// grass with green tufts, marked seamless in the lookup texture, and keeps the grass's edge over nothing; tall grass
    /// draws its default edge over every grass and over nothing: an outline, a shadow and a fringe of tufts, not
    /// seamless. Both show the grass's bank where water cuts into them.
    /// </summary>
    [Test]
    public void TheProjectsMeadowAndTallGrass_SwayInTheGrasssWind_AndMeetTheGrassAsDecided()
    {
        TerrainStyle style = AssetDatabase.LoadAssetAtPath<TerrainStyle>("Assets/Settings/Terrain/TerrainStyle.asset");
        TerrainMaterial grass = ProjectMaterial("Grass");
        TerrainMaterial dryGrass = ProjectMaterial("DryGrass");
        TerrainMaterial weeds = ProjectMaterial("Weeds");
        TerrainMaterial meadow = ProjectMaterial("Meadow");
        TerrainMaterial tallGrass = ProjectMaterial("TallGrass");

        Assert.That(style.LayerOrderIndexOf(meadow), Is.EqualTo(style.LayerOrderIndexOf(weeds) + 1));
        Assert.That(style.LayerOrderIndexOf(tallGrass), Is.EqualTo(style.LayerOrderIndexOf(weeds) + 2));
        foreach (TerrainMaterial material in new[] { meadow, tallGrass })
        {
            Assert.That(material.SinkDepth, Is.Zero, material.name);
            Assert.That(material.Cells, Is.EqualTo(TerrainMaterial.CellMode.None), material.name);
            Assert.That(material.BankColour, Is.EqualTo(grass.BankColour), material.name);
        }

        Assert.That(meadow.IsAnimated, Is.False, "The meadow's flowers stand still.");
        Assert.That(meadow.Sways, Is.False);
        Assert.That(tallGrass.Sways, Is.True);
        Assert.That(tallGrass.SwayPeriod, Is.EqualTo(weeds.SwayPeriod));
        Assert.That(tallGrass.GustLength, Is.EqualTo(weeds.GustLength));
        Assert.That(tallGrass.SwayAmplitude, Is.GreaterThan(weeds.SwayAmplitude));
        Assert.That(tallGrass.WholeClumps, Is.True, "Its tall blades lean whole over still ground.");
        Assert.That(weeds.WholeClumps || dryGrass.WholeClumps, Is.False, "The weeds and dry grass sway per cell.");

        TerrainStyle.EdgeSettings meadowOverGrass = style.GetEdge(meadow, grass);
        Assert.That(meadowOverGrass.Seamless, Is.True);
        Assert.That(meadowOverGrass.DrawOutline, Is.False);
        Assert.That(meadowOverGrass.RimWidth, Is.Zero);
        Assert.That(meadowOverGrass.ShadowAlpha, Is.Zero);
        Assert.That(meadowOverGrass.TuftHeight, Is.Zero, "The same ground: nothing to fray.");
        TerrainStyle.EdgeSettings meadowOverDryGrass = style.GetEdge(meadow, dryGrass);
        Assert.That(meadowOverDryGrass, Is.Not.SameAs(meadowOverGrass));
        Assert.That(meadowOverDryGrass.Seamless, Is.True);
        Assert.That(meadowOverDryGrass.TuftHeight, Is.GreaterThan(0f), "Green tufts fray into the straw.");
        Assert.That(style.GetEdge(meadow, null), Is.SameAs(meadow.Edge));
        Assert.That(meadow.Edge.DrawOutline, Is.EqualTo(grass.Edge.DrawOutline));
        Assert.That(meadow.Edge.RimWidth, Is.EqualTo(grass.Edge.RimWidth));
        Assert.That(meadow.Edge.TuftHeight, Is.EqualTo(grass.Edge.TuftHeight));

        foreach (TerrainMaterial lower in new[] { null, grass, dryGrass, weeds, meadow })
        {
            Assert.That(style.GetEdge(tallGrass, lower), Is.SameAs(tallGrass.Edge), lower != null ? lower.name : "nothing");
        }

        Assert.That(tallGrass.Edge.Seamless, Is.False);
        Assert.That(tallGrass.Edge.DrawOutline, Is.True);
        Assert.That(tallGrass.Edge.ShadowAlpha, Is.GreaterThan(0f));
        Assert.That(tallGrass.Edge.TuftHeight, Is.GreaterThan(grass.Edge.TuftHeight), "A fringe of blades.");

        Color[] texels = TerrainBlendLut.BuildTexels(style, new[] { grass, dryGrass, meadow, tallGrass });
        Assert.That(PairTexel(texels, 4, 2, 0, TerrainBlendLut.PairFillColumn).a, Is.Zero, "meadow over grass is seamless");
        Assert.That(PairTexel(texels, 4, 2, 1, TerrainBlendLut.PairFillColumn).a, Is.Zero, "meadow over dry grass is seamless");
        Assert.That(PairTexel(texels, 4, 2, -1, TerrainBlendLut.PairFillColumn).a, Is.EqualTo(1f), "meadow over nothing is not");
        Assert.That(PairTexel(texels, 4, 3, 0, TerrainBlendLut.PairFillColumn).a, Is.EqualTo(1f), "tall grass over grass is not");
        Assert.That(PairTexel(texels, 4, 3, 2, TerrainBlendLut.PairFillColumn).a, Is.EqualTo(1f), "tall grass over the meadow is not");
        Assert.That(LayerTexel(texels, 3, TerrainBlendLut.LayerSwayColumn).r, Is.EqualTo(tallGrass.SwayAmplitude));
    }

    /// <summary>
    /// The project's ground material named <paramref name="name"/>, only read.
    /// </summary>
    private static TerrainMaterial ProjectMaterial(string name)
    {
        TerrainMaterial material = AssetDatabase.LoadAssetAtPath<TerrainMaterial>($"Assets/Settings/Terrain/Materials/{name}.asset");
        Assert.That(material, Is.Not.Null, name);
        return material;
    }

    /// <summary>
    /// The lookup texture's layout is named twice, by <see cref="TerrainBlendLut"/>'s constants and by the #defines of the
    /// shader's TerrainBlendLut.hlsl: every constant has a define named TB_ and its name in capitals with underscores,
    /// with the same value, and every define is one of the constants.
    /// </summary>
    [Test]
    public void TheShadersLayoutDefines_MatchTheConstants()
    {
        Dictionary<string, string> defines = new();
        foreach (string line in File.ReadAllLines(LayoutPath))
        {
            Match match = Regex.Match(line, @"^#define (TB_\w+) (\S+)$");
            if (match.Success)
            {
                defines.Add(match.Groups[1].Value, match.Groups[2].Value);
            }
        }

        List<string> problems = new();
        HashSet<string> named = new();
        foreach (FieldInfo field in typeof(TerrainBlendLut).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (!field.IsLiteral)
            {
                continue;
            }

            string define = "TB_" + Regex.Replace(field.Name, "(?<=[a-z])(?=[A-Z])", "_").ToUpperInvariant();
            named.Add(define);
            double expected = System.Convert.ToDouble(field.GetRawConstantValue(), CultureInfo.InvariantCulture);
            if (!defines.TryGetValue(define, out string value))
            {
                problems.Add($"{define} is not defined");
            }
            else if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double actual) || actual != expected)
            {
                problems.Add($"{define} is {value}, but {nameof(TerrainBlendLut)}.{field.Name} is {expected.ToString(CultureInfo.InvariantCulture)}");
            }
        }

        foreach (string define in defines.Keys)
        {
            if (!named.Contains(define))
            {
                problems.Add($"{define} is no constant of {nameof(TerrainBlendLut)}");
            }
        }

        Assert.That(named, Is.Not.Empty);
        Assert.That(problems, Is.Empty);
    }

    /// <summary>
    /// The texel at <paramref name="column"/> of layer <paramref name="layer"/>'s row.
    /// </summary>
    private static Color LayerTexel(Color[] texels, int layer, int column)
    {
        return texels[TerrainBlendLut.LayerRow(layer) * TerrainBlendLut.Width + column];
    }

    /// <summary>
    /// The texel at <paramref name="column"/> of the row of layer <paramref name="upper"/> over layer
    /// <paramref name="lower"/>, for <paramref name="layers"/> layers.
    /// </summary>
    private static Color PairTexel(Color[] texels, int layers, int upper, int lower, int column)
    {
        return texels[TerrainBlendLut.PairRow(layers, upper, lower) * TerrainBlendLut.Width + column];
    }

    /// <summary>
    /// The shadow alpha stored for layer <paramref name="upper"/> over layer <paramref name="lower"/>.
    /// </summary>
    private static float ShadowAlpha(Color[] texels, int layers, int upper, int lower)
    {
        return texels[TerrainBlendLut.PairRow(layers, upper, lower) * TerrainBlendLut.Width + TerrainBlendLut.PairShadowColumn].a;
    }

    /// <summary>
    /// A style without a shader and with the given overrides.
    /// </summary>
    private TerrainStyle Style(params TerrainStyle.PairOverride[] pairOverrides)
    {
        return Track(TerrainStyle.Create(null, pairOverrides));
    }

    /// <summary>
    /// A material without textures whose default edge has the given shadow alpha.
    /// </summary>
    private TerrainMaterial Material(string name, float shadowAlpha, int repeatCells = 4, TerrainMaterial.CellMode mode = TerrainMaterial.CellMode.None, Color? baseColour = null)
    {
        return Track(TerrainMaterial.Create(name, null, null, repeatCells, mode, baseColour ?? Color.gray, Edge(shadowAlpha, 1.5f)));
    }

    /// <summary>
    /// An edge with the given shadow alpha and rim width.
    /// </summary>
    private static TerrainStyle.EdgeSettings Edge(float shadowAlpha, float rimWidth)
    {
        return new TerrainStyle.EdgeSettings(rimWidth, new Gradient(), true, Color.black, Color.black, shadowAlpha, 1f);
    }

    /// <summary>
    /// Records <paramref name="createdObject"/> for destruction in <see cref="TearDown"/> and returns it.
    /// </summary>
    private T Track<T>(T createdObject) where T : Object
    {
        created.Add(createdObject);
        return createdObject;
    }
}
