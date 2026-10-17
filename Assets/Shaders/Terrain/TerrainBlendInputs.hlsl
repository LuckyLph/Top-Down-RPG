#ifndef TOPDOWNRPG_TERRAIN_BLEND_INPUTS_INCLUDED
#define TOPDOWNRPG_TERRAIN_BLEND_INPUTS_INCLUDED

#include "Assets/Shaders/Terrain/TerrainBlendLut.hlsl"

#ifdef _TERRAIN_WIDE
#define TB_MAX_LAYERS 32
#else
#define TB_MAX_LAYERS 16
#endif
#define TB_FAR 100.0
#define TB_DIAGONAL_GAP 4.0
#define TB_WHOLE_CLEARANCE 10.0
#define TB_RUN_REACH 3

static const float2 TB_LIGHT = float2(-0.6, 0.8);

// The renderer's inputs. The lookup texture (see TerrainBlendLut) holds the style as data: row 0 the shared settings,
// then a row per layer (its material's shape, interior and height scale), then a row per pair of an upper layer over a
// lower one or over nothing (its edge in both looks). rect.xy is the world position of control map cell (0, 0),
// rect.zw its size. The soft look (keyword _TERRAIN_SOFT) reads the height array's red and green channels; both looks
// read its blue channel, the glint and sparkle phases of animated materials. time is the time animated materials are
// drawn at. bridges holds a bit for each layer whose material is a bridge (row 0's last value, and with _TERRAIN_WIDE
// the high half from TB_SHARED_HIGH_BRIDGES_COLUMN). A layer's shape
// (radii, repeat, cell mode) is read from its row when needed (TBShape).
struct TBInputs
{
    UnityTexture2D control;
    UnityTexture2D lut;
    UnityTexture2DArray albedo;
    UnityTexture2DArray field;
    UnityTexture2DArray height;
    float4 rect;
    uint layers;
    float4 globals;
    float time;
    uint bridges;
};

// The four cells around the corner nearest a pixel (down-left, down-right, up-left, up-right), the pixel centre
// relative to that corner, in pixels, and the bridge layers' bits (TBInputs.bridges).
struct TBCorner
{
    uint4 cells;
    float2 uv;
    uint bridges;
};

// How an upper layer's edge is drawn at a pixel: the settings of each pair it may be drawn over, blended by weight.
// In the soft look a pair with a band draws no outline and fills with the band's colour; edge is the colour of the
// opaque edge pixel, each pair's own (its outline where it draws one, else its fill) blended by weight, and
// bandWeight is how much of the edge pairs with a band make up. seamless is how much of the edge seamless pairs make
// up, whose edge pixel shows the layer's own interior.
struct TBEdge
{
    float3 shadow;
    float shadowAlpha;
    float shadowWidth;
    float3 outline;
    float outlineWeight;
    float3 fill;
    float3 edge;
    float tuftHeight;
    float bandWeight;
    float seamless;
    int firstRow;
};

// What a chip or stone sees at its centre pixel, decided from the corner patch around that centre, so every pixel of
// it agrees: how deep inside layer m the centre lies, the rim width of the pair drawn there (in the soft look the
// band's width where the pair has one, and none where it shows whole stones), how much of that pair shows whole
// stones in the soft look, and the outward direction of the shape there (soft only). A pair shows whole stones only
// where the centre lies at least TB_WHOLE_CLEARANCE pixels inside the lower layer, so a stone never reaches past a
// strip of the lower layer that thins out (where stone, gravel and grass meet) and cuts it into pieces; nearer, the
// pair's rim cuts the stones as in crisp.
struct TBCentre
{
    float depth;
    float rim;
    float whole;
    float2 outward;
};

// One texel of the lookup texture.
float4 TBLut(TBInputs ins, int column, int row)
{
    return LOAD_TEXTURE2D(ins.lut.tex, int2(column, row));
}

// The lookup texture's row of layer l (TerrainBlendLut.LayerRow).
int TBLayerRow(uint l)
{
    return 1 + (int)l;
}

// The lookup texture's first pair row of upper layer m, its pair over nothing; its pair over lower layer j follows at
// j + 1 rows further (TerrainBlendLut.PairRow).
int TBFirstPairRow(TBInputs ins, uint m)
{
    return 1 + (int)ins.layers + (int)m * ((int)ins.layers + 1);
}

// Layer l's shape from its row of the lookup texture: outer and inner corner radii, repeat in pixels, cell mode.
float4 TBShape(TBInputs ins, uint l)
{
    return TBLut(ins, TB_LAYER_SHAPE_COLUMN, TBLayerRow(l));
}

// sRGB colour to the colour the target expects.
float3 TBToOutput(float3 c)
{
#ifdef UNITY_COLORSPACE_GAMMA
    return c;
#else
    float3 low = c / 12.92;
    float3 high = pow((max(c, 0.0) + 0.055) / 1.055, 2.4);
    return lerp(high, low, step(c, 0.04045));
#endif
}

#endif
