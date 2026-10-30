#ifndef TOPDOWNRPG_TERRAIN_BLEND_RAISED_INCLUDED
#define TOPDOWNRPG_TERRAIN_BLEND_RAISED_INCLUDED

#include "Assets/Shaders/Terrain/TerrainBlendLook.hlsl"

#define TB_LEVEL 0u
#define TB_RAISED 1u
#define TB_RAMP 2u

TEXTURE2D(_TerrainRaised);

// A cell's raised ground (TerrainElevationMap): TB_LEVEL, TB_RAISED or TB_RAMP, from the elevation map, which covers
// the control map's cells; cells outside the map are level.
uint TBRaisedCell(TBInputs ins, int2 c)
{
    if (c.x < 0 || c.y < 0 || c.x >= (int)ins.rect.z || c.y >= (int)ins.rect.w)
    {
        return TB_LEVEL;
    }
    return (uint)(LOAD_TEXTURE2D(_TerrainRaised, c).r * 255.0 + 0.5);
}

// The raised ground of the cell holding a map pixel.
uint TBRaisedCellAt(TBInputs ins, float2 pixel)
{
    return TBRaisedCell(ins, (int2)floor((pixel + 0.5) / ART_PIXELS_PER_UNIT));
}

// Which of the four cells around the corner nearest a map pixel are raised ground or a ramp, as bits in
// CornerCellDirection's order, with the pixel's centre from that corner in uv.
uint TBRaisedMask(TBInputs ins, float2 pixel, out float2 uv)
{
    float2 centre = pixel + 0.5;
    float2 c = floor(centre / ART_PIXELS_PER_UNIT + 0.5);
    uv = centre - c * ART_PIXELS_PER_UNIT;
    int2 ci = (int2)c;
    uint mask = TBRaisedCell(ins, ci + int2(-1, -1)) != TB_LEVEL ? 1u : 0u;
    mask |= TBRaisedCell(ins, ci + int2(0, -1)) != TB_LEVEL ? 2u : 0u;
    mask |= TBRaisedCell(ins, ci + int2(-1, 0)) != TB_LEVEL ? 4u : 0u;
    mask |= TBRaisedCell(ins, ci) != TB_LEVEL ? 8u : 0u;
    return mask;
}

// The signed distance in pixels from a map pixel to raised ground, positive outside (TBRaisedDistance); a pixel lies
// on raised ground where it is under 0.5, as a layer covers its edge pixel.
float TBRaisedAt(TBInputs ins, float4 cliff, float2 pixel)
{
    float2 uv = float2(0.0, 0.0);
    uint mask = TBRaisedMask(ins, pixel, uv);
    return TBRaisedDistance(mask, uv, cliff);
}

// A hash of a world pixel in [0, 1).
float TBRaisedHash(float2 worldPixel)
{
    int2 p = (int2)worldPixel;
    uint seed = (uint)p.x * 0x8da6b343u ^ (uint)p.y * 0xd8163841u;
    seed ^= seed >> 13;
    seed *= 0x5bd1e995u;
    seed ^= seed >> 15;
    return (float)(seed & 0xffffu) / 65536.0;
}

// How many rows a pixel on raised ground lies above the cliff's foot: the fewest rows t from 1 down to a pixel off
// raised ground, found by halving, or height + 1 where that is further than height rows (the top of the plateau).
float TBRowsAboveFoot(TBInputs ins, float4 cliff, float2 pixel, float height)
{
    float rows = height + 1.0;
    [branch]
    if (TBRaisedAt(ins, cliff, pixel - float2(0.0, height)) >= 0.5)
    {
        float inside = 0.0;
        float outside = height;
        [loop]
        while (outside - inside > 1.0)
        {
            float middle = floor((inside + outside) * 0.5);
            if (TBRaisedAt(ins, cliff, pixel - float2(0.0, middle)) < 0.5)
            {
                inside = middle;
            }
            else
            {
                outside = middle;
            }
        }
        rows = outside;
    }
    return rows;
}

// The ramp's slope at a pixel of its face, row rows below its top, over the face's whole height above the ramp: the
// ground there darkening by the ramp shade (ramp.x) towards the foot, and a cheek of the cliff's foot colour along a
// side where the ramp meets a cliff. pixel is the slope's lowest pixel in its column, in the ramp's cell.
float3 TBRampFace(TBInputs ins, float4 ramp, float3 colour, float3 foot, float row, float height, float2 pixel)
{
    int2 cell = (int2)floor((pixel + 0.5) / ART_PIXELS_PER_UNIT);
    float x = pixel.x - (float)cell.x * ART_PIXELS_PER_UNIT;
    bool cheek = (x < 1.0 && TBRaisedCell(ins, cell + int2(-1, 0)) == TB_RAISED)
        || (x >= ART_PIXELS_PER_UNIT - 1.0 && TBRaisedCell(ins, cell + int2(1, 0)) == TB_RAISED);
    return cheek ? foot : colour * (1.0 - ramp.x * row / height);
}

// The cliff face at a pixel row rows below its top (1 to the cliff's height): the ground on top hanging over it by up
// to the fringe (cliff.y) rows, varying from column to column, lightened by edge.z as the plateau's rim is, its last
// row darker; then bands of face.x rows of the
// bank colour, every other one darkened to face.y, the whole face darkening by face.z towards its foot, with pebbles
// lightened to edge.w on a share face.w of its pixels; the foot row darkened to edge.x.
float3 TBCliffFace(float4 cliff, float4 face, float4 edge, float3 colour, float3 bank, float row, float height, float2 worldPixel)
{
    float fringe = cliff.y > 0.0 ? 1.0 + floor(TBRaisedHash(float2(worldPixel.x, 0.0)) * cliff.y) : 0.0;
    float3 result;
    if (row <= fringe)
    {
        result = row == fringe ? colour * 0.85 : colour * (1.0 + edge.z);
    }
    else if (row >= height)
    {
        result = bank * edge.x;
    }
    else
    {
        float band = fmod(floor((row - fringe - 1.0) / max(face.x, 1.0)), 2.0);
        result = bank * (band > 0.5 ? face.y : 1.0) * (1.0 - face.z * row / height);
        result = TBRaisedHash(worldPixel) < face.w ? bank * edge.w : result;
    }
    return result;
}

// The ledge along raised ground's sides and back at a pixel depth pixels inside its edge, up to the rim's width
// (rim.x): the ground on top hanging over its inner side by up to the fringe (cliff.y) pixels but no more than half
// the ledge, varying along the edge,
// its outermost pixel darker; then bands of face.x pixels of the bank colour, every other one darkened to face.y,
// darkening by face.z towards the edge, with pebbles lightened to edge.w on a share face.w of its pixels. Inside the
// ledge the ground on top, its first pixel lightened by edge.z.
float3 TBCliffRim(float4 cliff, float4 face, float4 edge, float4 rim, float3 colour, float3 bank, float depth, float2 worldPixel)
{
    float width = rim.x;
    float most = min(cliff.y, floor(width * 0.5));
    float fringe = most > 0.0 ? 1.0 + floor(TBRaisedHash(float2(worldPixel.x + worldPixel.y, 1.0)) * most) : 0.0;
    float inner = width - fringe;
    float3 result;
    if (depth >= width)
    {
        result = depth < width + 1.0 ? colour * (1.0 + edge.z) : colour;
    }
    else if (depth >= inner)
    {
        result = depth < inner + 1.0 ? colour * 0.85 : colour * (1.0 + edge.z);
    }
    else
    {
        float band = fmod(floor(depth / max(face.x, 1.0)), 2.0);
        result = bank * (band > 0.5 ? face.y : 1.0) * (1.0 - face.z * (1.0 - depth / max(width, 1.0)));
        result = TBRaisedHash(worldPixel) < face.w ? bank * edge.w : result;
    }
    return result;
}

// Raised ground's top at a pixel on it (d, its distance to raised ground's edge, under 0.5), over the ground's colour
// there (sRGB, its topmost opaque layer top): within the cliff's height (cliff.x) above its foot, the cliff face
// (TBCliffFace) in the bank colour of top's material, or, where the foot lies in a ramp's cell, the ramp's slope up
// through the cells above it (TBRampFace); above that the plateau's top, its
// edge pixel outlined in the bank colour darkened to edge.y and a ledge of earth inside it along its sides and back
// (TBCliffRim).
float3 TBRaisedTop(TBInputs ins, float4 cliff, float3 colour, uint top, float d, float2 pixel, float2 worldPixel)
{
    float4 face = TBLut(ins, TB_SHARED_CLIFF_FACE_COLUMN, TB_SHARED_ROW);
    float4 edge = TBLut(ins, TB_SHARED_CLIFF_EDGE_COLUMN, TB_SHARED_ROW);
    float3 bank = TBLut(ins, TB_LAYER_BANK_COLOUR_COLUMN, TBLayerRow(top)).rgb;
    float height = cliff.x;
    float rows = TBRowsAboveFoot(ins, cliff, pixel, height);
    float2 foot = pixel - float2(0.0, rows - 1.0);
    float3 result;
    [branch]
    if (rows <= height && TBRaisedCellAt(ins, foot) == TB_RAMP)
    {
        float4 ramp = TBLut(ins, TB_SHARED_RAMP_COLUMN, TB_SHARED_ROW);
        result = TBRampFace(ins, ramp, colour, bank * edge.x, height + 1.0 - rows, height, foot);
    }
    else if (d > -1.0)
    {
        result = bank * edge.y;
    }
    else if (rows <= height)
    {
        result = TBCliffFace(cliff, face, edge, colour, bank, height + 1.0 - rows, height, worldPixel);
    }
    else
    {
        float4 rim = TBLut(ins, TB_SHARED_CLIFF_RIM_COLUMN, TB_SHARED_ROW);
        result = TBCliffRim(cliff, face, edge, rim, colour, bank, -d, worldPixel);
    }
    return result;
}

// Raised ground over the ground's colour at a pixel (sRGB, its topmost opaque layer top): on it its top (TBRaisedTop);
// the ground's colour as it is off raised ground, where the cliffs' shadows are the sun's (the SunShadowCliffs shader).
// The cliff's settings come from the lookup texture's shared row (TB_SHARED_CLIFF_COLUMN to TB_SHARED_CLIFF_RIM_COLUMN).
float3 TBRaised(TBInputs ins, float4 cliff, float3 colour, uint top, float2 pixel, float2 worldPixel)
{
    float2 uv = float2(0.0, 0.0);
    uint mask = TBRaisedMask(ins, pixel, uv);
    float3 result = colour;
    [branch]
    if (mask != 0u)
    {
        float d = TBRaisedDistance(mask, uv, cliff);
        [branch]
        if (d < 0.5)
        {
            result = TBRaisedTop(ins, cliff, colour, top, d, pixel, worldPixel);
        }
    }
    return result;
}

// The ground's colour at a pixel (sRGB, its topmost opaque layer top, -1 for none) with raised ground over it: with
// the keyword _TERRAIN_RAISED, which the renderer sets when it has a raised tilemap, TBRaised wherever a layer is
// drawn; without it the colour as it is, so an area without raised ground compiles and pays for none of it.
float3 TBRaisedOver(TBInputs ins, float3 colour, int top, float2 pixel, float2 worldPixel)
{
#ifdef _TERRAIN_RAISED
    float4 cliff = TBLut(ins, TB_SHARED_CLIFF_COLUMN, TB_SHARED_ROW);
    [branch]
    if (cliff.x > 0.0 && top >= 0)
    {
        colour = TBRaised(ins, cliff, colour, (uint)top, pixel, worldPixel);
    }
#endif
    return colour;
}

#endif
