#ifndef TOPDOWNRPG_TERRAIN_BLEND_SHAPE_INCLUDED
#define TOPDOWNRPG_TERRAIN_BLEND_SHAPE_INCLUDED

#include "Assets/Shaders/Include/PixelSnap.hlsl"
#include "Assets/Shaders/Include/PeriodicNoise.hlsl"
#include "Assets/Shaders/Include/RoundedCorners.hlsl"
#include "Assets/Shaders/Terrain/TerrainBlendInputs.hlsl"

// The layer bits of one cell of the control map, a 16-bit normalised texel; cells outside the map are empty.
uint TBCell(TBInputs ins, int2 c)
{
    if (c.x < 0 || c.y < 0 || c.x >= (int)ins.rect.z || c.y >= (int)ins.rect.w)
    {
        return 0u;
    }
    return (uint)(LOAD_TEXTURE2D(ins.control.tex, c).r * 65535.0 + 0.5);
}

// The corner nearest the centre of a map pixel and the four cells around it.
TBCorner TBLoadCorner(TBInputs ins, float2 pixel)
{
    TBCorner corner;
    float2 centre = pixel + 0.5;
    float2 c = floor(centre / ART_PIXELS_PER_UNIT + 0.5);
    corner.uv = centre - c * ART_PIXELS_PER_UNIT;
    int2 ci = (int2)c;
    corner.cells = uint4(TBCell(ins, ci + int2(-1, -1)), TBCell(ins, ci + int2(0, -1)), TBCell(ins, ci + int2(-1, 0)), TBCell(ins, ci));
    corner.bridges = ins.bridges;
    return corner;
}

// The bits of a cell that count for a layer's shape: every bit but those of the bridge layers above it, which lie on
// what is painted under them without carrying it on (a dock over the beach does not take the water under it).
uint TBCounted(uint cell, uint bridges, uint layer)
{
    return cell & ~(bridges & ~(1u << layer));
}

// Which of the four cells a layer covers: a cell counts for a layer when that layer or any layer above it is filled,
// so a lower layer continues under the upper ones, except under a bridge layer (TBCounted).
uint TBMask(TBCorner corner, uint layer)
{
    uint mask = 0u;
    [unroll]
    for (uint k = 0u; k < 4u; k++)
    {
        if ((TBCounted(corner.cells[k], corner.bridges, layer) >> layer) != 0u)
        {
            mask |= 1u << k;
        }
    }
    return mask;
}

// The highest layer filled in a cell (0 for an empty cell): its highest set bit.
uint TBTopLayer(uint cell)
{
    return cell != 0u ? firstbithigh(cell) : 0u;
}

// Soft: each of the four cells' height term in pixels at a pixel, from the material of the cell's top layer: its
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

// Soft: layer l's signed distance in pixels at a corner patch, positive outside. Its weight is interpolated
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

// What a layer's signed distance at a corner patch needs besides its own cells: in soft the four cells' heights
// (TBCellHeights), in crisp the shared edge noise at the pixel in x.
float4 TBDistanceTerms(TBInputs ins, TBCorner corner, float2 worldPixel)
{
#ifdef _TERRAIN_SOFT
    return TBCellHeights(ins, corner, worldPixel);
#else
    return float4(PeriodicEdgeNoise(worldPixel) * ins.globals.x, 0.0, 0.0, 0.0);
#endif
}

// Layer l's signed distance in pixels at a corner patch, positive outside; TB_FAR where it covers none of the four
// cells. Crisp: the rounded shape pushed by the shared edge noise (terms.x), so edges that coincide stay equal. Soft:
// TBSoftDistance with the cells' heights (terms).
float TBLayerDistance(TBInputs ins, TBCorner corner, float4 terms, uint l)
{
#ifdef _TERRAIN_SOFT
    float2 slope;
    return TBSoftDistance(corner, l, terms, slope);
#else
    float4 shape = TBShape(ins, l);
    float sd = CornerShapeDistance(TBMask(corner, l), corner.uv, shape.x, shape.y, TB_FAR);
    return abs(sd) >= TB_FAR ? sd : sd + terms.x;
#endif
}

// How many of the area's layers can cover a corner patch: up to the highest layer filled in any of its four cells,
// since a layer covers a cell only where it or a layer above it is filled. 0 for a patch of empty cells.
uint TBLayersAt(TBInputs ins, TBCorner corner)
{
    uint cells = corner.cells.x | corner.cells.y | corner.cells.z | corner.cells.w;
    return cells != 0u ? min(ins.layers, TBTopLayer(cells) + 1u) : 0u;
}

// Every layer's signed distance in pixels at a corner patch (TBLayerDistance); TB_FAR for the layers above the
// patch's highest filled layer (TBLayersAt), which cover none of its cells, and past the area's layers.
void TBDistances(TBInputs ins, TBCorner corner, float2 worldPixel, out float d[TB_MAX_LAYERS])
{
    float4 terms = TBDistanceTerms(ins, corner, worldPixel);
    [unroll]
    for (uint u = 0u; u < (uint)TB_MAX_LAYERS; u++)
    {
        d[u] = TB_FAR;
    }
    uint count = TBLayersAt(ins, corner);
    [loop]
    for (uint l = 0u; l < count; l++)
    {
        d[l] = TBLayerDistance(ins, corner, terms, l);
    }
}

// Whether a layer is drawn at a pixel: it covers the pixel's corner patch and its edge lies at least the pair margin
// beyond the edge of the layer above it, so where two edges coincide only the upper one is drawn.
bool TBVisible(TBInputs ins, float d[TB_MAX_LAYERS], uint m)
{
    bool present = d[m] < TB_FAR;
    uint up = min(m + 1u, (uint)TB_MAX_LAYERS - 1u);
    bool covered = m + 1u < ins.layers && d[up] < TB_FAR && d[up] - d[m] < ins.globals.y;
    return present && !covered;
}

// The outward direction of layer m's shape at a pixel of a corner patch, from distances one pixel apart, with the
// shared edge noise scaled by amplitude (0 for the bare shape).
float2 TBGradient(TBInputs ins, TBCorner corner, uint m, float2 worldPixel, float amplitude)
{
    uint mask = TBMask(corner, m);
    float4 shape = TBShape(ins, m);
    float rOut = shape.x;
    float rIn = shape.y;
    float right = CornerShapeDistance(mask, corner.uv + float2(1.0, 0.0), rOut, rIn, TB_FAR) + PeriodicEdgeNoise(worldPixel + float2(1.0, 0.0)) * amplitude;
    float left = CornerShapeDistance(mask, corner.uv - float2(1.0, 0.0), rOut, rIn, TB_FAR) + PeriodicEdgeNoise(worldPixel - float2(1.0, 0.0)) * amplitude;
    float up = CornerShapeDistance(mask, corner.uv + float2(0.0, 1.0), rOut, rIn, TB_FAR) + PeriodicEdgeNoise(worldPixel + float2(0.0, 1.0)) * amplitude;
    float down = CornerShapeDistance(mask, corner.uv - float2(0.0, 1.0), rOut, rIn, TB_FAR) + PeriodicEdgeNoise(worldPixel - float2(0.0, 1.0)) * amplitude;
    float2 g = float2(right - left, up - down);
    float length2 = dot(g, g);
    return length2 > 1e-6 ? g * rsqrt(length2) : float2(0.0, 0.0);
}

// The outward direction of layer m's edge at a pixel of a corner patch: in crisp the gradient of the shape with the
// edge noise, in soft against the slope of the layer's weight.
float2 TBOutward(TBInputs ins, TBCorner corner, uint m, float2 worldPixel)
{
#ifdef _TERRAIN_SOFT
    float2 slope;
    TBSoftDistance(corner, m, float4(0.0, 0.0, 0.0, 0.0), slope);
    float length2 = dot(slope, slope);
    return length2 > 1e-6 ? -slope * rsqrt(length2) : float2(0.0, 0.0);
#else
    return TBGradient(ins, corner, m, worldPixel, ins.globals.x);
#endif
}

// How deep inside layer m a map pixel lies, from the corner patch around it.
float TBDepthAt(TBInputs ins, uint m, float2 pixel, float2 worldPixel)
{
    TBCorner corner = TBLoadCorner(ins, pixel);
    return -TBLayerDistance(ins, corner, TBDistanceTerms(ins, corner, worldPixel), m);
}

#endif
