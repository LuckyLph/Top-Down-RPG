#ifndef TOPDOWNRPG_TERRAIN_BLEND_INCLUDED
#define TOPDOWNRPG_TERRAIN_BLEND_INCLUDED

#include "Assets/Shaders/Terrain/TerrainBlendInputs.hlsl"
#include "Assets/Shaders/Terrain/TerrainBlendShape.hlsl"
#include "Assets/Shaders/Terrain/TerrainBlendLook.hlsl"
#include "Assets/Shaders/Terrain/TerrainBlendCoverage.hlsl"
#include "Assets/Shaders/Terrain/TerrainBlendRuns.hlsl"
#include "Assets/Shaders/Terrain/TerrainBlendEdges.hlsl"
#include "Assets/Shaders/Terrain/TerrainBlendCells.hlsl"
#include "Assets/Shaders/Terrain/TerrainBlendAnimation.hlsl"
#include "Assets/Shaders/Terrain/TerrainBlendSway.hlsl"
#include "Assets/Shaders/Terrain/TerrainBlendBanks.hlsl"
#include "Assets/Shaders/Terrain/TerrainBlendInterior.hlsl"
#include "Assets/Shaders/Terrain/TerrainBlendLayer.hlsl"
#include "Assets/Shaders/Terrain/TerrainBlendRaised.hlsl"
#include "Assets/Shaders/Terrain/TerrainBlendDebug.hlsl"
#include "Assets/Shaders/Include/PixelLight.hlsl"

#if SHADERPASS == SHADERPASS_SPRITELIT
#pragma multi_compile _ USE_SHAPE_LIGHT_TYPE_0
#endif

// The terrain at a world position: snaps to the 32 px grid, reads the four cells around the nearest corner and
// composites the layers bottom up, each drawn over the layer below it with that pair's edge (without its shadow or
// tufts where a bridge ends on its edge, TBNearBridgeEdge), then, with the keyword _TERRAIN_RAISED, raised ground's
// cliffs over them (TBRaisedOver), in the crisp look or,
// with the keyword _TERRAIN_SOFT, the soft one. With the keyword _TERRAIN_WIDE it reads a wide control map (TBCell) and
// the high half of the bridge bits. Info.x is the number of layers, Info.y > 0.5 outputs the topmost
// visible layer instead as TBDebugColour (pure colours, which the 2D lights scale but never mix), and Info.z > 0.5
// draws animated materials at the time Info.w instead of the frame's. The colour is lit by the 2D lights read once at
// the art pixel's centre, without the sun where a sun shadow covers the art pixel (PixelLitShadowed, SunShadowAt), so the
// light changes only from one art pixel to the next.
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
    ins.globals = TBLut(ins, TB_SHARED_COLUMN, TB_SHARED_ROW);
    ins.time = Info.z > 0.5 ? Info.w : _TimeParameters.x;
    ins.bridges = (uint)(ins.globals.w + 0.5);
#ifdef _TERRAIN_WIDE
    ins.bridges |= (uint)(TBLut(ins, TB_SHARED_HIGH_BRIDGES_COLUMN, TB_SHARED_ROW).x + 0.5) << 16;
#endif

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

#ifndef _TERRAIN_WIDE
    float d[TB_MAX_LAYERS];
#endif
    TBDistances(ins, corner, worldPixel TB_D_ARG);
    uint count = TBLayersAt(ins, corner);

    uint start = 0u;
    [loop]
    for (uint m = 0u; m < count; m++)
    {
        if (d[m] <= -0.5 && TBVisible(ins TB_D_ARG, m) && TBOpaqueInside(ins, m))
        {
            start = m;
        }
    }

    uint bridges = 0u;
    [branch]
    if (ins.bridges != 0u)
    {
        bridges = TBBridgesAt(ins TB_D_ARG);
    }

    float4 acc = float4(0.0, 0.0, 0.0, 0.0);
    int top = -1;
    [loop]
    for (uint n = start; n < count; n++)
    {
        bool bridged = false;
        [branch]
        if (bridges != 0u)
        {
            bridged = TBNearBridgeEdge(ins TB_D_ARG, bridges, n);
        }

        [branch]
        if (TBVisible(ins TB_D_ARG, n) && (!bridged || d[n] < 0.5))
        {
#ifndef _TERRAIN_WIDE
            float w[TB_MAX_LAYERS + 1];
#endif
            TBPairWeights(ins TB_D_ARG, n TB_W_ARG);
            TBEdge e = TBGetEdge(ins, n TB_W_ARG);
            e.tuftHeight = bridged ? 0.0 : e.tuftHeight;
            float4 c = TBLayer(ins, n, e TB_W_ARG, d[n], pixel, worldPixel, corner);
            acc = c + acc * (1.0 - c.a);
            if (c.a >= 0.5)
            {
                top = (int)n;
            }
        }
    }

    float2 centre = (worldPixel + 0.5) / ART_PIXELS_PER_UNIT;
    if (debugView)
    {
        Color = PixelLit(TBDebugColour(top), centre);
        return;
    }
    float3 colour = acc.a > 0.0 ? acc.rgb / acc.a : float3(0.0, 0.0, 0.0);
    Color = PixelLitShadowed(TBToOutput(TBRaisedOver(ins, colour, top, pixel, worldPixel)), centre, SunShadowAt(worldPixel));
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
