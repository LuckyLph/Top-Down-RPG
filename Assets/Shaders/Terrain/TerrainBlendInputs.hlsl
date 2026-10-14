#ifndef TOPDOWNRPG_TERRAIN_BLEND_INPUTS_INCLUDED
#define TOPDOWNRPG_TERRAIN_BLEND_INPUTS_INCLUDED

#define TB_MAX_LAYERS 16
#define TB_FAR 100.0
#define TB_RAMP_COLUMN 5
#define TB_ANIMATION_COLUMN 4
#define TB_GLINT_COLUMN 5
#define TB_SPARKLE_COLUMN 6
#define TB_SPARKLE_TIMING_COLUMN 7
#define TB_BANK_COLUMN 8
#define TB_BANK_COLOUR_COLUMN 9
#define TB_SWAY_COLUMN 10
#define TB_RAMP_TEXELS 32
#define TB_RAMP_STEP 0.5
#define TB_DIAGONAL_GAP 4.0
#define TB_WHOLE_CLEARANCE 10.0
#define TB_RUN_REACH 3

static const float2 TB_LIGHT = float2(-0.6, 0.8);

// The renderer's inputs. The lookup texture (see TerrainBlendLut) holds the style as data: row 0 the shared settings,
// then a row per layer (its material's shape, interior and height scale), then a row per pair of an upper layer over a
// lower one or over nothing (its edge in both looks). rect.xy is the world position of control map cell (0, 0),
// rect.zw its size. The soft look (keyword _TERRAIN_SOFT) reads the height array's red and green channels; both looks
// read its blue channel, the glint and sparkle phases of animated materials. time is the time animated materials are
// drawn at. bridges holds a bit for each layer whose material is a bridge (row 0's last value). A layer's shape
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

// One texel of the lookup texture.
float4 TBLut(TBInputs ins, int column, int row)
{
    return LOAD_TEXTURE2D(ins.lut.tex, int2(column, row));
}

// Layer l's shape from its row of the lookup texture: outer and inner corner radii, repeat in pixels, cell mode.
float4 TBShape(TBInputs ins, uint l)
{
    return TBLut(ins, 0, 1 + (int)l);
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
