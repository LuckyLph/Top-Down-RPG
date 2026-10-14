#ifndef TOPDOWNRPG_TERRAIN_BLEND_CRISP_INCLUDED
#define TOPDOWNRPG_TERRAIN_BLEND_CRISP_INCLUDED

#include "Assets/Shaders/Include/PeriodicNoise.hlsl"
#include "Assets/Shaders/Include/RoundedCorners.hlsl"
#include "Assets/Shaders/Terrain/TerrainBlendShape.hlsl"

// What a layer's signed distance at a corner patch needs besides its own cells: the shared edge noise at the pixel,
// in x.
float4 TBDistanceTerms(TBInputs ins, TBCorner corner, float2 worldPixel)
{
    return float4(PeriodicEdgeNoise(worldPixel) * ins.globals.x, 0.0, 0.0, 0.0);
}

// Layer l's signed distance in pixels at a corner patch, positive outside; TB_FAR where it covers none of the four
// cells: the rounded shape pushed by the shared edge noise (terms.x), so edges that coincide stay equal.
float TBLayerDistance(TBInputs ins, TBCorner corner, float4 terms, uint l)
{
    float4 shape = TBShape(ins, l);
    float sd = CornerShapeDistance(TBMask(corner, l), corner.uv, shape.x, shape.y, TB_FAR);
    return abs(sd) >= TB_FAR ? sd : sd + terms.x;
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

// The outward direction of layer m's edge at a pixel of a corner patch: the gradient of the shape with the edge noise.
float2 TBOutward(TBInputs ins, TBCorner corner, uint m, float2 worldPixel)
{
    return TBGradient(ins, corner, m, worldPixel, ins.globals.x);
}

// The direction banks and tufts follow along layer m's edge at a pixel of a corner patch: the bare shape's outward
// direction, without the edge noise, so a bank keeps one height and tufts stand straight along a straight edge.
float2 TBEdgeDirection(TBInputs ins, TBCorner corner, uint m, float2 worldPixel)
{
    return TBGradient(ins, corner, m, worldPixel, 0.0);
}

// Adds one pair's look-specific part to an edge's settings (TBGetEdge), by its weight: how much of the edge pixel is
// outline (outline.a, 1 when drawn) and the rim's outer colour (fill).
void TBAddPairEdge(TBInputs ins, int row, float weight, float4 outline, float4 fill, inout TBEdge e)
{
    e.outlineWeight += weight * outline.a;
    e.fill += weight * fill.rgb;
}

// One pair's rim texel r at a depth (TBRamp): the rim as it is; band, how much of the rim soft bands make up, stays.
float4 TBRampTexel(TBInputs ins, int row, float weight, float depth, float4 r, inout float band)
{
    return r;
}

// Adds one pair's part to what a chip or stone sees at its centre (TBCentre): the rim width of the pair at row, by its
// weight. clear, whether the centre lies TB_WHOLE_CLEARANCE inside the lower layer, only matters in the soft look.
void TBAddCentrePair(TBInputs ins, int row, float weight, bool clear, inout TBCentre centre)
{
    float4 widths = TBLut(ins, 2, row);
    centre.rim += weight * widths.y;
}

// What else a chip's centre needs (TBCentreAt): nothing in the crisp look, which stretches a whole chip's reach by the
// edge noise's steepest slope instead of measuring along the edge's direction (TBChipNearest).
void TBCentreOutward(TBInputs ins, TBCorner corner, uint m, float2 worldPixel, bool outward, inout TBCentre centre)
{
}

// How deep inside layer m the chip or stone with shape's cell mode, seen at its centre (centre, at the map pixel
// centrePixel), comes nearest the edge: for a whole chip its centre's depth less its reach (border) stretched by the
// edge noise's steepest slope, since the distance can fall that much faster than the pixels move away; for a stone its
// centre's depth.
float TBChipNearest(TBInputs ins, uint m, float4 shape, TBCentre centre, float border, float2 centrePixel, float2 centreWorldPixel)
{
    return centre.depth - (shape.w < 1.5 ? border * (1.0 + PERIODIC_NOISE_MAX_SLOPE * ins.globals.x) : 0.0);
}

// Whether layer m hides everything under it wherever it lies inside its edge: always in the crisp look.
bool TBOpaqueInside(TBInputs ins, uint m)
{
    return true;
}

#endif
