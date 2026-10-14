#ifndef TOPDOWNRPG_TERRAIN_BLEND_SOFT_INCLUDED
#define TOPDOWNRPG_TERRAIN_BLEND_SOFT_INCLUDED

#include "Assets/Shaders/Terrain/TerrainBlendShape.hlsl"

// Each of the four cells' height term in pixels at a pixel, from the material of the cell's top layer: its
// height (the height texture's red channel) from -1 to 1 times its height scale, which is 0 for a material without a
// height texture.
float4 TBCellHeights(TBInputs ins, TBCorner corner, float2 worldPixel)
{
    float4 heights = float4(0.0, 0.0, 0.0, 0.0);
    [unroll]
    for (uint k = 0u; k < 4u; k++)
    {
        [branch]
        if (corner.cells[k] != 0u)
        {
            uint top = TBTopLayer(corner.cells[k]);
            float scale = TBLut(ins, 3, 1 + (int)top).x;
            int2 texel = (int2)WrapPixel(worldPixel, TBShape(ins, top).z);
            heights[k] = scale * (2.0 * LOAD_TEXTURE2D_ARRAY(ins.height.tex, texel, top).r - 1.0);
        }
    }
    return heights;
}

// Layer l's signed distance in pixels at a corner patch, positive outside. Its weight is interpolated
// bilinearly from the four cells (1 where the layer or one above it is filled), less a bump on diagonal pairs so two
// diagonal cells never join through the saddle point; the cells' heights, mixed by the same weights over the filled
// cells, are added, so edges that coincide stay equal. The layer covers a pixel where its weight, in pixels across a
// cell, plus the height passes one half: there the distance falls below 0.5, where crisp's edge pixel ends, so the
// rest of the pipeline is shared. Slope is the weight's slope per pixel, in pixels (32 times the weight). TB_FAR
// where the layer covers none of the four cells, -TB_FAR where it covers all.
float TBSoftDistance(TBCorner corner, uint l, float4 heights, out float2 slope)
{
    uint mask = TBMask(corner, l);
    slope = float2(0.0, 0.0);
    if (mask == 0u)
    {
        return TB_FAR;
    }
    if (mask == 15u)
    {
        return -TB_FAR;
    }
    float2 st = corner.uv / ART_PIXELS_PER_UNIT + 0.5;
    float s = st.x;
    float t = st.y;
    float4 b = float4((1.0 - s) * (1.0 - t), s * (1.0 - t), (1.0 - s) * t, s * t);
    float4 bs = float4(t - 1.0, 1.0 - t, -t, t);
    float4 bt = float4(s - 1.0, -s, 1.0 - s, s);
    float w = 0.0;
    float h = 0.0;
    [unroll]
    for (uint k = 0u; k < 4u; k++)
    {
        if ((mask & (1u << k)) != 0u)
        {
            w += b[k];
            h += b[k] * heights[k];
            slope += float2(bs[k], bt[k]);
        }
    }
    h /= max(w, 1e-4);
    if (mask == 6u || mask == 9u)
    {
        w -= TB_DIAGONAL_GAP * s * (1.0 - s) * t * (1.0 - t);
        slope -= TB_DIAGONAL_GAP * float2((1.0 - 2.0 * s) * t * (1.0 - t), (1.0 - 2.0 * t) * s * (1.0 - s));
    }
    return 0.5 - (ART_PIXELS_PER_UNIT * (w - 0.5) + h);
}

// What a layer's signed distance at a corner patch needs besides its own cells: the four cells' heights
// (TBCellHeights).
float4 TBDistanceTerms(TBInputs ins, TBCorner corner, float2 worldPixel)
{
    return TBCellHeights(ins, corner, worldPixel);
}

// Layer l's signed distance in pixels at a corner patch, positive outside; TB_FAR where it covers none of the four
// cells: TBSoftDistance with the cells' heights (terms).
float TBLayerDistance(TBInputs ins, TBCorner corner, float4 terms, uint l)
{
    float2 slope;
    return TBSoftDistance(corner, l, terms, slope);
}

// The outward direction of layer m's edge at a pixel of a corner patch: against the slope of the layer's weight.
float2 TBOutward(TBInputs ins, TBCorner corner, uint m, float2 worldPixel)
{
    float2 slope;
    TBSoftDistance(corner, m, float4(0.0, 0.0, 0.0, 0.0), slope);
    float length2 = dot(slope, slope);
    return length2 > 1e-6 ? -slope * rsqrt(length2) : float2(0.0, 0.0);
}

// The direction banks and tufts follow along layer m's edge at a pixel of a corner patch: the edge's outward direction
// (TBOutward), already smooth in the soft look.
float2 TBEdgeDirection(TBInputs ins, TBCorner corner, uint m, float2 worldPixel)
{
    return TBOutward(ins, corner, m, worldPixel);
}

// How deep inside layer m a map pixel lies, from the corner patch around it.
float TBDepthAt(TBInputs ins, uint m, float2 pixel, float2 worldPixel)
{
    TBCorner corner = TBLoadCorner(ins, pixel);
    return -TBLayerDistance(ins, corner, TBDistanceTerms(ins, corner, worldPixel), m);
}

// Adds one pair's look-specific part to an edge's settings (TBGetEdge), by its weight: a pair with a band (its width
// above 0) draws no outline and fills with the band's colour; the opaque edge pixel takes each pair's own outline where
// it draws one, else its fill.
void TBAddPairEdge(TBInputs ins, int row, float weight, float4 outline, float4 fill, inout TBEdge e)
{
    float4 band = TBLut(ins, 4, row);
    float banded = band.a > 0.0 ? 1.0 : 0.0;
    float3 filled = lerp(fill.rgb, band.rgb, banded);
    e.fill += weight * filled;
    e.edge += weight * lerp(filled, outline.rgb, outline.a * (1.0 - banded));
    e.bandWeight += weight * banded;
}

// One pair's rim texel r at a depth (TBRamp): where the pair has a band, the band's colour covering the interior up to
// its width instead, added by weight to band, how much of the rim such bands make up.
float4 TBRampTexel(TBInputs ins, int row, float weight, float depth, float4 r, inout float band)
{
    float4 settings = TBLut(ins, 4, row);
    if (settings.a > 0.0)
    {
        float cover = depth < settings.a ? 1.0 : 0.0;
        r = float4(settings.rgb, cover);
        band += weight * cover;
    }
    return r;
}

// Adds one pair's part to what a chip or stone sees at its centre (TBCentre): the rim width of the pair at row, by its
// weight, or the band's width where it has one, or none where it shows whole stones, which it does only when clear says
// the centre lies TB_WHOLE_CLEARANCE inside the lower layer (always over nothing).
void TBAddCentrePair(TBInputs ins, int row, float weight, bool clear, inout TBCentre centre)
{
    float4 widths = TBLut(ins, 2, row);
    float band = TBLut(ins, 4, row).a;
    float whole = widths.w > 0.5 && clear ? 1.0 : 0.0;
    centre.rim += weight * (whole > 0.5 ? 0.0 : (band > 0.0 ? band : widths.y));
    centre.whole += weight * whole;
}

// What else a chip's centre needs (TBCentreAt): the outward direction of layer m's edge there, only when asked for,
// since only whole chips use it (TBChipNearest).
void TBCentreOutward(TBInputs ins, TBCorner corner, uint m, float2 worldPixel, bool outward, inout TBCentre centre)
{
    [branch]
    if (outward)
    {
        centre.outward = TBOutward(ins, corner, m, worldPixel);
    }
}

// How deep inside layer m the chip or stone with shape's cell mode, seen at its centre (centre, at the map pixel
// centrePixel), comes nearest the edge: for a whole chip the depth where it comes nearest the edge, its reach (border)
// from its centre along the edge's outward direction there (TBDepthAt); for a stone its centre's depth.
float TBChipNearest(TBInputs ins, uint m, float4 shape, TBCentre centre, float border, float2 centrePixel, float2 centreWorldPixel)
{
    float2 reach = shape.w < 1.5 ? border * centre.outward : float2(0.0, 0.0);
    return shape.w < 1.5 ? TBDepthAt(ins, m, centrePixel + reach, centreWorldPixel + reach) : centre.depth;
}

// Whether layer m hides everything under it wherever it lies inside its edge: not where it may show whole stones,
// which leave the layer below showing between them.
bool TBOpaqueInside(TBInputs ins, uint m)
{
    return TBLut(ins, 1, 1 + (int)m).w < 0.5;
}

#endif
