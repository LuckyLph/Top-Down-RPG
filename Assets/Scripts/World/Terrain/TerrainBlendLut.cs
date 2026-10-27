using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The terrain style and materials as data for the terrain blending shader (Assets/Shaders/Terrain/TerrainBlend.hlsl):
/// a small point-filtered RGBA float texture read with Load, for a renderer's layers and the materials they draw with,
/// holding both looks, so switching between them needs no rebuild. Its layout is named twice, by the constants here and
/// by the #defines of Assets/Shaders/Terrain/TerrainBlendLut.hlsl (TB_ and the constant's name in capitals with
/// underscores), which TerrainBlendLutTests checks agree. Row <see cref="SharedRow"/> holds the shared settings at
/// <see cref="SharedColumn"/>: (edge noise, pair margin, pair blend, the low 16 bits of the bridge layers: bit i for a
/// layer i whose material is a bridge), and at <see cref="SharedHighBridgesColumn"/> the high 16 bits of the bridge
/// layers in its first value, empty for 16 layers or fewer. Row <see cref="LayerRow"/> holds a layer's material: at
/// <see cref="LayerShapeColumn"/> (outer radius, inner radius, repeat in pixels, cell mode), at
/// <see cref="LayerChipsColumn"/> (keep margin, cut gap, cut bevel, 1 when the material has cut stones and one of the
/// layer's pairs shows them whole in the soft look), at
/// <see cref="LayerBaseColumn"/> its base colour, at <see cref="LayerHeightColumn"/> (soft height scale, 0 without a
/// height texture, 1 when the material turns to its runs), for an animated material at
/// <see cref="LayerAnimationColumn"/> (frames per second, glint period, glint duty, rim swell), at
/// <see cref="LayerGlintColumn"/> (glint colour, swell period), at <see cref="LayerSparkleColumn"/> (sparkle colour,
/// sparkle period) and at <see cref="LayerSparkleTimingColumn"/> (sparkle duty, swash), zero otherwise, at
/// <see cref="LayerBankColumn"/> (sink depth, bank shadow), zero for a material level with the ground, at
/// <see cref="LayerBankColourColumn"/> its bank colour, the face a sunken layer shows where it cuts into this one, and
/// for a swaying material at <see cref="LayerSwayColumn"/> (sway amplitude, sway period, gust length in cells, 1 when its
/// clumps lean whole), zero otherwise, and for a material with waves at <see cref="LayerWavesColumn"/> (spacing, period,
/// width, tangent of the angle), at <see cref="LayerWaveColourColumn"/> (wave colour, whole within), at
/// <see cref="LayerWaveCrestColumn"/> (crest colour, stretch), at <see cref="LayerWaveTroughColumn"/> the trough's colour, at
/// <see cref="LayerWashFoamColumn"/> (foam colour, wet), then the foam's shade, the film's and the film's back colours
/// at <see cref="LayerWashFoamShadeColumn"/>, <see cref="LayerWashFilmColumn"/> and <see cref="LayerWashFilmBackColumn"/>,
/// zero otherwise, and at <see cref="LayerSliceColumn"/> the slice of the texture arrays holding the material (its first
/// value; the layer's own index unless the renderer draws with arrays shared by its style, <see cref="SharedTerrainArrays"/>).
/// Then one row per pair of an upper layer u over a lower layer l, or over nothing (l
/// = -1), at <see cref="PairRow"/>: at <see cref="PairShadowColumn"/> (shadow colour, shadow alpha), at
/// <see cref="PairOutlineColumn"/> (outline colour, 1 when drawn), at <see cref="PairWidthsColumn"/> (shadow width, rim
/// width, tuft height, 1 when the soft look shows whole stones, only over a material with cut stones), at
/// <see cref="PairFillColumn"/> (the rim's outer colour, 0 when the edge is seamless, else 1), at
/// <see cref="PairBandColumn"/> (the soft band's colour, its width, 0 for none), then from <see cref="PairRampColumn"/>
/// the rim sampled every <see cref="RampStep"/> pixels from the edge inwards, <see cref="RampTexels"/> texels (colour,
/// how much of the interior it covers). Colours stay sRGB, as the shader expects.
/// </summary>
public static class TerrainBlendLut
{
    public const int SharedRow = 0;
    public const int SharedColumn = 0;
    public const int SharedCliffColumn = 1;
    public const int SharedCliffFaceColumn = 2;
    public const int SharedCliffEdgeColumn = 3;
    public const int SharedCliffShadowColumn = 4;
    public const int SharedCliffRimColumn = 5;
    public const int SharedHighBridgesColumn = 6;
    public const int LayerShapeColumn = 0;
    public const int LayerChipsColumn = 1;
    public const int LayerBaseColumn = 2;
    public const int LayerHeightColumn = 3;
    public const int LayerAnimationColumn = 4;
    public const int LayerGlintColumn = 5;
    public const int LayerSparkleColumn = 6;
    public const int LayerSparkleTimingColumn = 7;
    public const int LayerBankColumn = 8;
    public const int LayerBankColourColumn = 9;
    public const int LayerSwayColumn = 10;
    public const int LayerWavesColumn = 11;
    public const int LayerWaveColourColumn = 12;
    public const int LayerWaveCrestColumn = 13;
    public const int LayerWaveTroughColumn = 14;
    public const int LayerWashFoamColumn = 15;
    public const int LayerWashFoamShadeColumn = 16;
    public const int LayerWashFilmColumn = 17;
    public const int LayerWashFilmBackColumn = 18;
    public const int LayerSliceColumn = 19;
    public const int PairShadowColumn = 0;
    public const int PairOutlineColumn = 1;
    public const int PairWidthsColumn = 2;
    public const int PairFillColumn = 3;
    public const int PairBandColumn = 4;
    public const int PairRampColumn = 5;
    public const int RampTexels = 32;
    public const float RampStep = 0.5f;
    public const int Width = PairRampColumn + RampTexels;

    /// <summary>
    /// The row holding layer <paramref name="layer"/>'s material.
    /// </summary>
    public static int LayerRow(int layer)
    {
        return 1 + layer;
    }

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
    /// Builds the texels for layers drawing <paramref name="layerMaterials"/> (bottom layer first) with
    /// <paramref name="style"/>'s shared settings and pair overrides, row by row from the bottom, <see cref="Width"/>
    /// per row; each layer's slice of the texture arrays is its entry of <paramref name="slices"/>, or its own index
    /// without them.
    /// </summary>
    public static Color[] BuildTexels(TerrainStyle style, IReadOnlyList<TerrainMaterial> layerMaterials, bool raised = false, IReadOnlyList<int> slices = null)
    {
        int layers = layerMaterials.Count;
        Color[] texels = new Color[Width * Height(layers)];
        uint bridges = BridgeBits(layerMaterials);
        texels[SharedRow * Width + SharedColumn] = new Color(style.EdgeNoise, style.PairMargin, style.PairBlend, bridges & 0xFFFFu);
        texels[SharedRow * Width + SharedHighBridgesColumn] = new Color(bridges >> 16, 0f, 0f, 0f);
        if (raised)
        {
            int shared = SharedRow * Width;
            texels[shared + SharedCliffColumn] = new Color(style.CliffHeight, style.CliffFringe, style.CliffOuterRadius, style.CliffInnerRadius);
            texels[shared + SharedCliffFaceColumn] = new Color(style.CliffBandRows, style.CliffBandShade, style.CliffDepthShade, style.CliffPebbleChance);
            texels[shared + SharedCliffEdgeColumn] = new Color(style.CliffFootShade, style.CliffOutlineShade, style.CliffRimLight, style.CliffPebbleLight);
            texels[shared + SharedCliffShadowColumn] = new Color(style.CliffShadowAlpha, style.CliffShadowWidth, style.RampShade, 0f);
            texels[shared + SharedCliffRimColumn] = new Color(style.CliffRimWidth, 0f, 0f, 0f);
        }

        for (int layer = 0; layer < layers; layer++)
        {
            TerrainMaterial material = layerMaterials[layer];
            int row = LayerRow(layer) * Width;
            texels[row + LayerShapeColumn] = new Color(material.OuterRadius, material.InnerRadius, material.RepeatPixels, (float)material.Cells);
            texels[row + LayerChipsColumn] = new Color(material.KeepMargin, material.CutGap, material.CutBevel, 0f);
            texels[row + LayerBaseColumn] = material.BaseColour;
            texels[row + LayerHeightColumn] = new Color(material.Height != null ? material.HeightScale : 0f, material.TurnsToRuns ? 1f : 0f, 0f, 0f);
            texels[row + LayerSliceColumn] = new Color(slices != null ? slices[layer] : layer, 0f, 0f, 0f);
            if (material.IsAnimated)
            {
                Color glint = material.GlintColour;
                texels[row + LayerAnimationColumn] = new Color(material.FramesPerSecond, material.GlintPeriod, material.GlintDuty, material.RimSwell);
                texels[row + LayerGlintColumn] = new Color(glint.r, glint.g, glint.b, material.SwellPeriod);
                Color sparkle = material.SparkleColour;
                texels[row + LayerSparkleColumn] = new Color(sparkle.r, sparkle.g, sparkle.b, material.SparklePeriod);
                texels[row + LayerSparkleTimingColumn] = new Color(material.SparkleDuty, material.Swash, 0f, 0f);
            }

            if (material.HasWaves)
            {
                Color wave = material.WaveColour;
                Color foam = material.WashFoamColour;
                texels[row + LayerWavesColumn] = new Color(material.WaveSpacing, material.WavePeriod, material.WaveWidth, Mathf.Tan(material.WaveAngle * Mathf.Deg2Rad));
                texels[row + LayerWaveColourColumn] = new Color(wave.r, wave.g, wave.b, material.WaveWholeWithin);
                Color crest = material.WaveCrestColour;
                texels[row + LayerWaveCrestColumn] = new Color(crest.r, crest.g, crest.b, material.WaveStretch);
                texels[row + LayerWaveTroughColumn] = material.WaveTroughColour;
                texels[row + LayerWashFoamColumn] = new Color(foam.r, foam.g, foam.b, material.WashWet);
                texels[row + LayerWashFoamShadeColumn] = material.WashFoamShadeColour;
                texels[row + LayerWashFilmColumn] = material.WashFilmColour;
                texels[row + LayerWashFilmBackColumn] = material.WashFilmBackColour;
            }

            texels[row + LayerBankColourColumn] = material.BankColour;
            if (material.Sways)
            {
                texels[row + LayerSwayColumn] = new Color(material.SwayAmplitude, material.SwayPeriod, material.GustLength, material.WholeClumps ? 1f : 0f);
            }

            if (material.SinkDepth > 0f)
            {
                texels[row + LayerBankColumn] = new Color(material.SinkDepth, material.BankShadow, 0f, 0f);
            }
        }

        for (int upper = 0; upper < layers; upper++)
        {
            bool cutStones = layerMaterials[upper].Cells == TerrainMaterial.CellMode.CutStones;
            for (int lower = -1; lower < upper; lower++)
            {
                TerrainMaterial lowerMaterial = lower < 0 ? null : layerMaterials[lower];
                TerrainStyle.EdgeSettings edge = style.GetEdge(layerMaterials[upper], lowerMaterial);
                bool wholeStones = cutStones && edge.SoftWholeStones;
                WriteEdge(texels, PairRow(layers, upper, lower) * Width, edge, wholeStones);
                if (wholeStones)
                {
                    texels[LayerRow(upper) * Width + LayerChipsColumn].a = 1f;
                }
            }
        }

        return texels;
    }

    /// <summary>
    /// Creates the lookup texture for <paramref name="style"/> and <paramref name="layerMaterials"/>, with each layer's
    /// slice of the texture arrays from <paramref name="slices"/> (its own index without them), flagged so it is never
    /// saved. The caller destroys it.
    /// </summary>
    public static Texture2D Create(TerrainStyle style, IReadOnlyList<TerrainMaterial> layerMaterials, string textureName, bool raised = false, IReadOnlyList<int> slices = null)
    {
        Texture2D texture = new(Width, Height(layerMaterials.Count), TextureFormat.RGBAFloat, false, true)
        {
            name = textureName,
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.DontSave
        };
        texture.SetPixels(BuildTexels(style, layerMaterials, raised, slices));
        texture.Apply(false, false);
        return texture;
    }

    /// <summary>
    /// The bits of the layers drawing a bridge material (<see cref="TerrainMaterial.Bridge"/>), bit i for layer i. Row 0
    /// holds them in two halves of 16 bits, since a float holds every one of 16 bits exactly but not of 32.
    /// </summary>
    public static uint BridgeBits(IReadOnlyList<TerrainMaterial> layerMaterials)
    {
        uint bits = 0u;
        for (int layer = 0; layer < layerMaterials.Count; layer++)
        {
            if (layerMaterials[layer] != null && layerMaterials[layer].Bridge)
            {
                bits |= 1u << layer;
            }
        }

        return bits;
    }

    /// <summary>
    /// Writes one pair row starting at texel <paramref name="start"/>.
    /// </summary>
    private static void WriteEdge(Color[] texels, int start, TerrainStyle.EdgeSettings edge, bool wholeStones)
    {
        Color shadow = edge.ShadowColour;
        Color outline = edge.OutlineColour;
        Color band = edge.SoftBandColour;
        texels[start + PairShadowColumn] = new Color(shadow.r, shadow.g, shadow.b, edge.ShadowAlpha);
        texels[start + PairOutlineColumn] = new Color(outline.r, outline.g, outline.b, edge.DrawOutline ? 1f : 0f);
        texels[start + PairWidthsColumn] = new Color(edge.ShadowWidth, edge.RimWidth, edge.TuftHeight, wholeStones ? 1f : 0f);
        Color outer = edge.EvaluateRim(0f);
        texels[start + PairFillColumn] = new Color(outer.r, outer.g, outer.b, edge.Seamless ? 0f : 1f);
        texels[start + PairBandColumn] = new Color(band.r, band.g, band.b, edge.SoftBandWidth);
        for (int i = 0; i < RampTexels; i++)
        {
            float depth = (i + 0.5f) * RampStep;
            texels[start + PairRampColumn + i] = depth < edge.RimWidth ? edge.EvaluateRim(depth / edge.RimWidth) : Color.clear;
        }
    }
}
