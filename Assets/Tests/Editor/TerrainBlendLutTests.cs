using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Tests for <see cref="TerrainBlendLut"/>, the terrain style and materials as shader data: a pair override replaces
/// the upper material's default edge for its own pair of material assets only, one whose lower material is missing is
/// not taken for one over nothing, layers find their materials' rows whatever the order, the rim is sampled every half
/// pixel up to its width, the soft look's height scale (held to its bound), band and whole stones reach their rows, and
/// the project style's stone over gravel override changes that pair's row only. The project's style and materials are
/// only read.
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
