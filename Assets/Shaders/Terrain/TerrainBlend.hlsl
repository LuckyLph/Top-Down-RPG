#ifndef TOPDOWNRPG_TERRAIN_BLEND_INCLUDED
#define TOPDOWNRPG_TERRAIN_BLEND_INCLUDED

#include "Assets/Shaders/Terrain/TerrainBlendInputs.hlsl"
#include "Assets/Shaders/Terrain/TerrainBlendShape.hlsl"
#include "Assets/Shaders/Terrain/TerrainBlendRuns.hlsl"
#include "Assets/Shaders/Terrain/TerrainBlendEdges.hlsl"
#include "Assets/Shaders/Terrain/TerrainBlendCells.hlsl"
#include "Assets/Shaders/Terrain/TerrainBlendAnimation.hlsl"
#include "Assets/Shaders/Terrain/TerrainBlendSway.hlsl"
#include "Assets/Shaders/Terrain/TerrainBlendBanks.hlsl"
#include "Assets/Shaders/Terrain/TerrainBlendInterior.hlsl"
#include "Assets/Shaders/Terrain/TerrainBlendLayer.hlsl"
#include "Assets/Shaders/Terrain/TerrainBlendDebug.hlsl"

// The terrain at a world position: snaps to the 32 px grid, reads the four cells around the nearest corner and
// composites the layers bottom up, each drawn over the layer below it with that pair's edge, in the crisp look or,
// with the keyword _TERRAIN_SOFT, the soft one. Info.x is the number of layers, Info.y > 0.5 outputs the topmost
// visible layer instead as TBDebugColour (pure colours, which the 2D lights scale but never mix), and Info.z > 0.5
// draws animated materials at the time Info.w instead of the frame's.
void TerrainBlend_float(float3 WorldPos, UnityTexture2D ControlMap, UnityTexture2D Lut, UnityTexture2DArray Albedo, UnityTexture2DArray Field, UnityTexture2DArray Height, float4 ControlRect, float4 Info, out float3 Color, out float Alpha)
{
    TBInputs ins;
    ins.control = ControlMap;
    ins.lut = Lut;
    ins.albedo = Albedo;
    ins.field = Field;
    ins.height = Height;
    ins.rect = ControlRect;
    ins.layers = (uint)clamp(Info.x, 0.0, (float)TB_MAX_LAYERS);
    ins.globals = TBLut(ins, 0, 0);
    ins.time = Info.z > 0.5 ? Info.w : _TimeParameters.x;
    ins.bridges = (uint)(ins.globals.w + 0.5);

    float2 worldPixel = SnapToArtPixel(WorldPos.xy);
    float2 pixel = worldPixel - ControlRect.xy * ART_PIXELS_PER_UNIT;
    TBCorner corner = TBLoadCorner(ins, pixel);
    bool debugView = Info.y > 0.5;
    Color = float3(0.0, 0.0, 0.0);
    Alpha = debugView ? 1.0 : 0.0;
    if (all(corner.cells == 0u))
    {
        return;
    }

    float d[TB_MAX_LAYERS];
    TBDistances(ins, corner, worldPixel, d);
    uint count = TBLayersAt(ins, corner);

    uint start = 0u;
    [loop]
    for (uint m = 0u; m < count; m++)
    {
        if (d[m] <= -0.5 && TBVisible(ins, d, m) && TBOpaqueInside(ins, m))
        {
            start = m;
        }
    }

    float4 acc = float4(0.0, 0.0, 0.0, 0.0);
    int top = -1;
    [loop]
    for (uint n = start; n < count; n++)
    {
        [branch]
        if (TBVisible(ins, d, n))
        {
            float w[TB_MAX_LAYERS + 1];
            TBPairWeights(ins, d, n, w);
            TBEdge e = TBGetEdge(ins, n, w);
            float4 c = TBLayer(ins, n, e, w, d[n], pixel, worldPixel, corner);
            acc = c + acc * (1.0 - c.a);
            if (c.a >= 0.5)
            {
                top = (int)n;
            }
        }
    }

    if (debugView)
    {
        Color = TBDebugColour(top);
        return;
    }
    Color = TBToOutput(acc.a > 0.0 ? acc.rgb / acc.a : float3(0.0, 0.0, 0.0));
    Alpha = acc.a;
}

// The half-precision entry point of the Custom Function node: runs TerrainBlend_float.
void TerrainBlend_half(half3 WorldPos, UnityTexture2D ControlMap, UnityTexture2D Lut, UnityTexture2DArray Albedo, UnityTexture2DArray Field, UnityTexture2DArray Height, half4 ControlRect, half4 Info, out half3 Color, out half Alpha)
{
    float3 colour;
    float alpha;
    TerrainBlend_float(WorldPos, ControlMap, Lut, Albedo, Field, Height, ControlRect, Info, colour, alpha);
    Color = colour;
    Alpha = alpha;
}

#endif
