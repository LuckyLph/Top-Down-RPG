using System.Collections.Generic;
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
/// material's sway reaches its layer's row and a seamless edge marks its rim's outer colour, and the project's weeds
/// sway and meet grass seamlessly with nothing drawn; the project's dirt, dry grass and plaza meet the other materials
/// with the edges and overrides TerrainTypes.md's Phase 3 decided. The project's style and materials are only read.
/// </summary>
public class TerrainBlendLutTests
{
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

        Assert.That(texels[0], Is.EqualTo(new Color(style.EdgeNoise, style.PairMargin, style.PairBlend, 0f)));
        Color stoneLayer = texels[1 * TerrainBlendLut.Width];
        Assert.That(stoneLayer.b, Is.EqualTo(64f));
        Assert.That(stoneLayer.a, Is.EqualTo((float)TerrainMaterial.CellMode.CutStones));
        Assert.That(texels[1 * TerrainBlendLut.Width + 2], Is.EqualTo(new Color(0.4f, 0.3f, 0.2f)));
        Color grassLayer = texels[2 * TerrainBlendLut.Width];
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

        int ramp = TerrainBlendLut.PairRow(1, 0, -1) * TerrainBlendLut.Width + TerrainBlendLut.RampColumn;
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

        Assert.That(LayerTexel(texels, 0, 3).r, Is.EqualTo(0f), "Grass has no height texture.");
        Assert.That(LayerTexel(texels, 1, 3).r, Is.EqualTo(1.25f));
        Assert.That(LayerTexel(texels, 2, 3).r, Is.EqualTo(TerrainMaterial.MaxHeightScale), "Stone's 9 px is held to the bound.");
        Assert.That(PairTexel(texels, 3, 1, 0, 4), Is.EqualTo(new Color(dirt.r, dirt.g, dirt.b, 1.5f)));
        Assert.That(PairTexel(texels, 3, 2, 1, 2).a, Is.EqualTo(1f), "Stone over gravel shows whole stones.");
        Assert.That(PairTexel(texels, 3, 2, 0, 2).a, Is.EqualTo(0f));
        Assert.That(PairTexel(texels, 3, 2, -1, 2).a, Is.EqualTo(0f));
        Assert.That(PairTexel(texels, 3, 1, 0, 2).a, Is.EqualTo(0f), "Gravel has chips, not cut stones.");
        Assert.That(LayerTexel(texels, 2, 1).a, Is.EqualTo(1f));
        Assert.That(LayerTexel(texels, 1, 1).a, Is.EqualTo(0f));
        Assert.That(LayerTexel(texels, 0, 1).a, Is.EqualTo(0f));
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
    /// colour and swell period, its sparkle colour and period, and its sparkle duty; a still material's holds nothing
    /// there, so the shader neither glints, sparkles nor swells it.
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
        settings.ApplyModifiedPropertiesWithoutUndo();

        Color[] texels = TerrainBlendLut.BuildTexels(Style(), new[] { grass, water });

        Assert.That(grass.IsAnimated, Is.False);
        Assert.That(water.IsAnimated, Is.True);
        Assert.That(LayerTexel(texels, 0, TerrainBlendLut.AnimationColumn), Is.EqualTo(Color.clear));
        Assert.That(LayerTexel(texels, 0, TerrainBlendLut.GlintColumn), Is.EqualTo(Color.clear));
        Assert.That(LayerTexel(texels, 1, TerrainBlendLut.AnimationColumn), Is.EqualTo(new Color(8f, 3.2f, 0.18f, 1.5f)));
        Assert.That(LayerTexel(texels, 1, TerrainBlendLut.GlintColumn), Is.EqualTo(new Color(0.8f, 0.9f, 1f, 4.5f)));
        Assert.That(LayerTexel(texels, 1, TerrainBlendLut.SparkleColumn), Is.EqualTo(new Color(0.9f, 1f, 0.95f, 4f)));
        Assert.That(LayerTexel(texels, 1, TerrainBlendLut.SparkleTimingColumn), Is.EqualTo(new Color(0.25f, 0f, 0f, 0f)));
        Assert.That(LayerTexel(texels, 0, TerrainBlendLut.SparkleColumn), Is.EqualTo(Color.clear));
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

        Assert.That(LayerTexel(texels, 0, TerrainBlendLut.BankColumn), Is.EqualTo(Color.clear));
        Assert.That(LayerTexel(texels, 1, TerrainBlendLut.BankColumn), Is.EqualTo(new Color(4.5f, 0.35f, 0f, 0f)));
        Assert.That(LayerTexel(texels, 0, TerrainBlendLut.BankColourColumn), Is.EqualTo(new Color(0.4f, 0.3f, 0.2f)));
    }

    /// <summary>
    /// A swaying material's layer row holds its sway amplitude, sway period and gust length, and a still or merely
    /// animated one's holds nothing there; a pair with a seamless edge writes its rim's outer colour with alpha 0, any
    /// other pair with alpha 1.
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
        Assert.That(LayerTexel(texels, 0, TerrainBlendLut.SwayColumn), Is.EqualTo(Color.clear));
        Assert.That(LayerTexel(texels, 1, TerrainBlendLut.SwayColumn), Is.EqualTo(new Color(2f, 2.5f, 12f, 0f)));
        Assert.That(texels[TerrainBlendLut.PairRow(2, 1, 0) * TerrainBlendLut.Width + 3].a, Is.Zero, "weeds over grass is seamless");
        Assert.That(texels[TerrainBlendLut.PairRow(2, 0, -1) * TerrainBlendLut.Width + 3].a, Is.EqualTo(1f), "grass over nothing is not");
    }

    /// <summary>
    /// The project's weeds sway and lie level with the ground, and their edge over grass is seamless with no outline,
    /// rim, shadow or tufts, so a weeds patch shows no edge on grass; over nothing they keep an outlined edge.
    /// </summary>
    [Test]
    public void TheProjectsWeeds_SwayAndMeetGrassSeamlessly()
    {
        TerrainStyle style = AssetDatabase.LoadAssetAtPath<TerrainStyle>("Assets/Settings/Terrain/TerrainStyle.asset");
        TerrainMaterial grass = AssetDatabase.LoadAssetAtPath<TerrainMaterial>("Assets/Settings/Terrain/Materials/Grass.asset");
        TerrainMaterial weeds = AssetDatabase.LoadAssetAtPath<TerrainMaterial>("Assets/Settings/Terrain/Materials/Weeds.asset");

        TerrainStyle.EdgeSettings overGrass = style.GetEdge(weeds, grass);

        Assert.That(weeds.Sways, Is.True);
        Assert.That(weeds.SinkDepth, Is.Zero);
        Assert.That(weeds.Height, Is.Not.Null, "The sway weights live in the height texture.");
        Assert.That(overGrass.Seamless, Is.True);
        Assert.That(overGrass.DrawOutline, Is.False);
        Assert.That(overGrass.RimWidth, Is.Zero);
        Assert.That(overGrass.ShadowAlpha, Is.Zero);
        Assert.That(overGrass.TuftHeight, Is.Zero);
        Assert.That(style.GetEdge(weeds, null).DrawOutline, Is.True);
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
            Assert.That(materials[i].IsAnimated, Is.False, names[i]);
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
        Assert.That(PairTexel(texels, 3, 2, 1, 1).a, Is.EqualTo(0f));
        Assert.That(PairTexel(texels, 3, 2, 1, 2).a, Is.EqualTo(1f));
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
                Assert.That(PairTexel(texels, 3, upper, lower, 2).a, Is.EqualTo(0f), $"{upper} over {lower}");
            }
        }
    }

    /// <summary>
    /// The project's dirt, dry grass and plaza (TerrainTypes.md, Phase 3) meet the other materials as decided: dry grass
    /// over grass is seamless, with no outline, rim or shadow but tufts, and marked seamless in the lookup texture, while
    /// over nothing it keeps an outlined edge; dirt draws its default edge, an outline and a fringe of tufts, over grass,
    /// dry grass, weeds and nothing; sand, mud, gravel and stone draw their own rim and outline over dirt with an earth
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
        Assert.That(dryGrass.Edge.DrawOutline, Is.True);
        Color[] texels = TerrainBlendLut.BuildTexels(style, new[] { grass, dryGrass });
        Assert.That(PairTexel(texels, 2, 1, 0, 3).a, Is.Zero, "dry grass over grass is seamless");
        Assert.That(PairTexel(texels, 2, 1, -1, 3).a, Is.EqualTo(1f), "dry grass over nothing is not");

        foreach (TerrainMaterial lower in new[] { null, grass, dryGrass, ProjectMaterial("Weeds") })
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
    /// The project's ground material named <paramref name="name"/>, only read.
    /// </summary>
    private static TerrainMaterial ProjectMaterial(string name)
    {
        TerrainMaterial material = AssetDatabase.LoadAssetAtPath<TerrainMaterial>($"Assets/Settings/Terrain/Materials/{name}.asset");
        Assert.That(material, Is.Not.Null, name);
        return material;
    }

    /// <summary>
    /// The texel at <paramref name="column"/> of layer <paramref name="layer"/>'s row.
    /// </summary>
    private static Color LayerTexel(Color[] texels, int layer, int column)
    {
        return texels[(1 + layer) * TerrainBlendLut.Width + column];
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
        return texels[TerrainBlendLut.PairRow(layers, upper, lower) * TerrainBlendLut.Width].a;
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
