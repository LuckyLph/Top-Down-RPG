#ifndef TOPDOWNRPG_TERRAIN_BLEND_SHAPE_INCLUDED
#define TOPDOWNRPG_TERRAIN_BLEND_SHAPE_INCLUDED

#include "Assets/Shaders/Include/PixelSnap.hlsl"
#include "Assets/Shaders/Terrain/TerrainBlendInputs.hlsl"

// The layer bits of one cell of the control map: a 16-bit normalised texel, or with _TERRAIN_WIDE an RGBA32 one whose
// four bytes hold the bits from red up. Cells outside the map are empty.
uint TBCell(TBInputs ins, int2 c)
{
    if (c.x < 0 || c.y < 0 || c.x >= (int)ins.rect.z || c.y >= (int)ins.rect.w)
    {
        return 0u;
    }
#ifdef _TERRAIN_WIDE
    uint4 bytes = (uint4)(LOAD_TEXTURE2D(ins.control.tex, c) * 255.0 + 0.5);
    return bytes.r | (bytes.g << 8) | (bytes.b << 16) | (bytes.a << 24);
#else
    return (uint)(LOAD_TEXTURE2D(ins.control.tex, c).r * 65535.0 + 0.5);
#endif
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

// How many of the area's layers can cover a corner patch: up to the highest layer filled in any of its four cells,
// since a layer covers a cell only where it or a layer above it is filled. 0 for a patch of empty cells.
uint TBLayersAt(TBInputs ins, TBCorner corner)
{
    uint cells = corner.cells.x | corner.cells.y | corner.cells.z | corner.cells.w;
    return cells != 0u ? min(ins.layers, TBTopLayer(cells) + 1u) : 0u;
}

#endif
