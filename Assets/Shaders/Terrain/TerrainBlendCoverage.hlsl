#ifndef TOPDOWNRPG_TERRAIN_BLEND_COVERAGE_INCLUDED
#define TOPDOWNRPG_TERRAIN_BLEND_COVERAGE_INCLUDED

#include "Assets/Shaders/Terrain/TerrainBlendLook.hlsl"

// Every layer's signed distance in pixels at a corner patch (TBLayerDistance); TB_FAR for the layers above the
// patch's highest filled layer (TBLayersAt), which cover none of its cells, and past the area's layers.
void TBDistances(TBInputs ins, TBCorner corner, float2 worldPixel TB_D_OUT_PARAM)
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

// The bridge layers (TBInputs.bridges) that cover the corner patch, as bits: those whose distance is not TB_FAR.
uint TBBridgesAt(TBInputs ins TB_D_PARAM)
{
    uint found = 0u;
    uint bridges = ins.bridges;
    [loop]
    while (bridges != 0u)
    {
        uint b = firstbitlow(bridges);
        bridges &= bridges - 1u;
        found |= b < ins.layers && d[b] < TB_FAR ? 1u << b : 0u;
    }
    return found;
}

// Whether a bridge layer among bridges (TBBridgesAt) higher up than the layer above m has its edge within the pair
// margin of m's edge at a pixel. The layer above m counts every layer over it but the bridges (TBCounted), so where a
// bridge ends on the edge of a layer painted under it, TBVisible does not hide that layer there; it draws its edge
// pixel and what lies inside, which fill the bridge's corners, but neither its shadow nor its tufts, which would show
// past the bridge's own edge. With _TERRAIN_WIDE the mask of the bridges from m + 2 up never shifts by 32 or more.
bool TBNearBridgeEdge(TBInputs ins TB_D_PARAM, uint bridges, uint m)
{
    bool found = false;
#ifdef _TERRAIN_WIDE
    uint above = bridges & ~((2u << min(m + 1u, 31u)) - 1u);
#else
    uint above = bridges & ~((1u << min(m + 2u, 31u)) - 1u);
#endif
    [loop]
    while (above != 0u && !found)
    {
        uint b = firstbitlow(above);
        above &= above - 1u;
        found = d[b] - d[m] < ins.globals.y;
    }
    return found;
}

// Whether a layer is drawn at a pixel: it covers the pixel's corner patch and its edge lies at least the pair margin
// beyond the edge of the next layer above it that covers the patch (present, TBPresentLayers), so where two edges
// coincide only the upper one is drawn. Layers that cover none of the patch's cells are skipped, so a bridge hides the
// layer it lies on at their common edge whatever empty layers are listed between them.
bool TBVisible(TBInputs ins TB_D_PARAM, uint present, uint m)
{
    uint up = TBNextPresent(present, m);
    bool covered = up != m && d[up] - d[m] < ins.globals.y;
    return d[m] < TB_FAR && !covered;
}

#endif
