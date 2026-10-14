#ifndef TOPDOWNRPG_TERRAIN_BLEND_DEBUG_INCLUDED
#define TOPDOWNRPG_TERRAIN_BLEND_DEBUG_INCLUDED

#include "Assets/Shaders/Terrain/TerrainBlendInputs.hlsl"

// The debug colours of the layers. Each channel is off, at half or full, and at least one is full, so no two layers'
// colours are the same up to the 2D lights' intensity: layers 0 to 6 light red, green and blue fully by the bits of the
// layer plus one (red, green, yellow, blue, magenta, cyan, white), layers 7 to 12 one channel full and another at half
// (orange, rose, chartreuse, spring green, violet, azure), layers 13 to 15 two channels full and the third at half
// (light yellow, light magenta, light cyan). TerrainBlendProbe's DebugColours lists the same table.
static const float3 TB_DEBUG_COLOURS[TB_MAX_LAYERS] =
{
    float3(1.0, 0.0, 0.0),
    float3(0.0, 1.0, 0.0),
    float3(1.0, 1.0, 0.0),
    float3(0.0, 0.0, 1.0),
    float3(1.0, 0.0, 1.0),
    float3(0.0, 1.0, 1.0),
    float3(1.0, 1.0, 1.0),
    float3(1.0, 0.5, 0.0),
    float3(1.0, 0.0, 0.5),
    float3(0.5, 1.0, 0.0),
    float3(0.0, 1.0, 0.5),
    float3(0.5, 0.0, 1.0),
    float3(0.0, 0.5, 1.0),
    float3(1.0, 1.0, 0.5),
    float3(1.0, 0.5, 1.0),
    float3(0.5, 1.0, 1.0)
};

// The debug colour of a layer (TB_DEBUG_COLOURS), black for none.
float3 TBDebugColour(int layer)
{
    if (layer < 0)
    {
        return float3(0.0, 0.0, 0.0);
    }
    return TB_DEBUG_COLOURS[min(layer, TB_MAX_LAYERS - 1)];
}

#endif
