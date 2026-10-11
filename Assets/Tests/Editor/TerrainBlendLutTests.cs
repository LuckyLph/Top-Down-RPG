using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Tests for <see cref="TerrainBlendLut"/>, the terrain style as shader data: a pair override replaces the upper
/// material's default edge for its pair only, layers find their materials' rows whatever the order, and the rim is
/// sampled every half pixel up to its width.
/// </summary>
public class TerrainBlendLutTests
{
    private TerrainStyle style;

    /// <summary>
    /// Destroys the style the test created.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        if (style != null)
        {
            Object.DestroyImmediate(style);
        }
    }

    /// <summary>
    /// Stone over gravel draws its override, while stone over grass, stone over nothing and gravel over grass draw
    /// their upper material's default edge; a layer list without gravel never uses the override.
    /// </summary>
    [Test]
    public void APairOverride_ReplacesTheDefaultEdge_ForItsPairOnly()
    {
        style = TerrainStyle.Create(null, new[] { Material("Grass", 0.1f), Material("Gravel", 0.2f), Material("Stone", 0.3f) },
            new TerrainStyle.PairOverride(2, 1, Edge(0.9f, 1f)));

        Color[] texels = TerrainBlendLut.BuildTexels(style, new[] { 0, 1, 2 });

        Assert.That(ShadowAlpha(texels, 3, 2, 1), Is.EqualTo(0.9f).Within(1e-5f));
        Assert.That(ShadowAlpha(texels, 3, 2, 0), Is.EqualTo(0.3f).Within(1e-5f));
        Assert.That(ShadowAlpha(texels, 3, 2, -1), Is.EqualTo(0.3f).Within(1e-5f));
        Assert.That(ShadowAlpha(texels, 3, 1, 0), Is.EqualTo(0.2f).Within(1e-5f));
        Assert.That(ShadowAlpha(texels, 3, 0, -1), Is.EqualTo(0.1f).Within(1e-5f));

        Color[] withoutGravel = TerrainBlendLut.BuildTexels(style, new[] { 0, 2 });
        Assert.That(ShadowAlpha(withoutGravel, 2, 1, 0), Is.EqualTo(0.3f).Within(1e-5f));
    }

    /// <summary>
    /// Row 0 holds the shared settings and each layer's row its own material's repeat and cell mode.
    /// </summary>
    [Test]
    public void SharedAndLayerRows_HoldTheStylesSettings()
    {
        style = TerrainStyle.Create(null, new[] { Material("Grass", 0.1f), Material("Stone", 0.3f, 2, TerrainStyle.CellMode.CutStones) });

        Color[] texels = TerrainBlendLut.BuildTexels(style, new[] { 1, 0 });

        Assert.That(texels[0], Is.EqualTo(new Color(style.EdgeNoise, style.PairMargin, style.PairBlend, 0f)));
        Color stoneLayer = texels[1 * TerrainBlendLut.Width];
        Assert.That(stoneLayer.b, Is.EqualTo(64f));
        Assert.That(stoneLayer.a, Is.EqualTo((float)TerrainStyle.CellMode.CutStones));
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
        style = TerrainStyle.Create(null, new[] { new TerrainStyle.MaterialSettings("Grass", null, null, 4, TerrainStyle.CellMode.None, Color.gray,
            new TerrainStyle.EdgeSettings(2f, rim, true, Color.black, Color.black, 0.3f, 1f)) });

        Color[] texels = TerrainBlendLut.BuildTexels(style, new[] { 0 });

        int ramp = TerrainBlendLut.PairRow(1, 0, -1) * TerrainBlendLut.Width + TerrainBlendLut.RampColumn;
        Assert.That(texels[ramp], Is.EqualTo(Color.red));
        Assert.That(texels[ramp + 1], Is.EqualTo(Color.red));
        Assert.That(texels[ramp + 2], Is.EqualTo(Color.blue));
        Assert.That(texels[ramp + 3], Is.EqualTo(Color.blue));
        Assert.That(texels[ramp + 4], Is.EqualTo(Color.clear));
    }

    /// <summary>
    /// The shadow alpha stored for layer <paramref name="upper"/> over layer <paramref name="lower"/>.
    /// </summary>
    private static float ShadowAlpha(Color[] texels, int layers, int upper, int lower)
    {
        return texels[TerrainBlendLut.PairRow(layers, upper, lower) * TerrainBlendLut.Width].a;
    }

    /// <summary>
    /// A material without textures whose default edge has the given shadow alpha.
    /// </summary>
    private static TerrainStyle.MaterialSettings Material(string name, float shadowAlpha, int repeatCells = 4, TerrainStyle.CellMode mode = TerrainStyle.CellMode.None)
    {
        return new TerrainStyle.MaterialSettings(name, null, null, repeatCells, mode, Color.gray, Edge(shadowAlpha, 1.5f));
    }

    /// <summary>
    /// An edge with the given shadow alpha and rim width.
    /// </summary>
    private static TerrainStyle.EdgeSettings Edge(float shadowAlpha, float rimWidth)
    {
        return new TerrainStyle.EdgeSettings(rimWidth, new Gradient(), true, Color.black, Color.black, shadowAlpha, 1f);
    }
}
