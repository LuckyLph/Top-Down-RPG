using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The terrain style as data for the terrain blending shader (Assets/Shaders/Terrain/TerrainBlend.hlsl): a small
/// point-filtered RGBA float texture read with Load, for a renderer's layers and the materials they draw with.
/// Row 0 holds the shared settings (edge noise, pair margin, pair blend). Rows 1 to L hold each layer's material:
/// (outer radius, inner radius, repeat in pixels, cell mode), (keep margin, cut gap, cut bevel), (base colour). Then
/// one row per pair of an upper layer u over a lower layer l, or over nothing (l = -1), at
/// <see cref="PairRow"/>: (shadow colour, shadow alpha), (outline colour, 1 when drawn), (shadow width, rim width, tuft height),
/// (the rim's outer colour), then the rim sampled every half pixel from the edge inwards (colour, how much of the
/// interior it covers). Colours stay sRGB, as the shader expects.
/// </summary>
public static class TerrainBlendLut
{
    public const int RampColumn = 4;
    public const int RampTexels = 32;
    public const float RampStep = 0.5f;
    public const int Width = RampColumn + RampTexels;

    /// <summary>
    /// The row holding layer <paramref name="upper"/>'s edge over layer <paramref name="lower"/> (-1 for nothing) for
    /// <paramref name="layerCount"/> layers.
    /// </summary>
    public static int PairRow(int layerCount, int upper, int lower)
    {
        return 1 + layerCount + upper * (layerCount + 1) + lower + 1;
    }

    /// <summary>
    /// The number of rows for <paramref name="layerCount"/> layers.
    /// </summary>
    public static int Height(int layerCount)
    {
        return 1 + layerCount + layerCount * (layerCount + 1);
    }

    /// <summary>
    /// Builds the texels for layers drawing <paramref name="layerMaterials"/> (material indices in
    /// <paramref name="style"/>, bottom layer first), row by row from the bottom, <see cref="Width"/> per row.
    /// </summary>
    public static Color[] BuildTexels(TerrainStyle style, IReadOnlyList<int> layerMaterials)
    {
        int layers = layerMaterials.Count;
        Color[] texels = new Color[Width * Height(layers)];
        texels[0] = new Color(style.EdgeNoise, style.PairMargin, style.PairBlend, 0f);
        for (int layer = 0; layer < layers; layer++)
        {
            TerrainStyle.MaterialSettings material = style.Materials[layerMaterials[layer]];
            int row = (1 + layer) * Width;
            texels[row] = new Color(material.OuterRadius, material.InnerRadius, material.RepeatPixels, (float)material.Cells);
            texels[row + 1] = new Color(material.KeepMargin, material.CutGap, material.CutBevel, 0f);
            texels[row + 2] = material.BaseColour;
        }

        for (int upper = 0; upper < layers; upper++)
        {
            for (int lower = -1; lower < upper; lower++)
            {
                int lowerMaterial = lower < 0 ? TerrainStyle.NoMaterial : layerMaterials[lower];
                WriteEdge(texels, PairRow(layers, upper, lower) * Width, style.GetEdge(layerMaterials[upper], lowerMaterial));
            }
        }

        return texels;
    }

    /// <summary>
    /// Creates the lookup texture for <paramref name="style"/> and <paramref name="layerMaterials"/>, flagged so it is
    /// never saved. The caller destroys it.
    /// </summary>
    public static Texture2D Create(TerrainStyle style, IReadOnlyList<int> layerMaterials, string textureName)
    {
        Texture2D texture = new(Width, Height(layerMaterials.Count), TextureFormat.RGBAFloat, false, true)
        {
            name = textureName,
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.DontSave
        };
        texture.SetPixels(BuildTexels(style, layerMaterials));
        texture.Apply(false, false);
        return texture;
    }

    /// <summary>
    /// Writes one pair row starting at texel <paramref name="start"/>.
    /// </summary>
    private static void WriteEdge(Color[] texels, int start, TerrainStyle.EdgeSettings edge)
    {
        Color shadow = edge.ShadowColour;
        Color outline = edge.OutlineColour;
        texels[start] = new Color(shadow.r, shadow.g, shadow.b, edge.ShadowAlpha);
        texels[start + 1] = new Color(outline.r, outline.g, outline.b, edge.DrawOutline ? 1f : 0f);
        texels[start + 2] = new Color(edge.ShadowWidth, edge.RimWidth, edge.TuftHeight, 0f);
        Color outer = edge.EvaluateRim(0f);
        texels[start + 3] = new Color(outer.r, outer.g, outer.b, 1f);
        for (int i = 0; i < RampTexels; i++)
        {
            float depth = (i + 0.5f) * RampStep;
            texels[start + RampColumn + i] = depth < edge.RimWidth ? edge.EvaluateRim(depth / edge.RimWidth) : Color.clear;
        }
    }
}
