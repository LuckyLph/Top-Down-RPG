#ifndef TERRAIN_SPIKE_INCLUDED
#define TERRAIN_SPIKE_INCLUDED

#define TS_PPU 32.0
#define TS_REPEAT 128.0
#define TS_FAR 100.0
#define TS_MARGIN 1.5
#define TS_R_OUT 14.0
#define TS_R_IN 11.0
#define TS_DIAGONAL_GAP 0.25
#define TS_SOFT_RIM 1.5
#define TS_STONE_JITTER 3.0

static const float2 TS_LIGHT = float2(-0.6, 0.8);
static const float3 TS_HEIGHT_AMPLITUDE = float3(3.0, 2.5, 2.0);

static const float3 TS_GRASS_OUTLINE = float3(45.0, 92.0, 43.0) / 255.0;
static const float3 TS_GRASS_FRINGE = float3(60.0, 112.0, 50.0) / 255.0;
static const float3 TS_GRASS_HIGHLIGHT = float3(165.0, 222.0, 104.0) / 255.0;
static const float3 TS_GRASS_LIME = float3(125.0, 205.0, 83.0) / 255.0;
static const float3 TS_GRASS_DARK = float3(31.0, 108.0, 36.0) / 255.0;
static const float3 TS_GRASS_MID = float3(60.0, 152.0, 66.0) / 255.0;

static const float3 TS_GRAVEL_DIRT = float3(146.0, 122.0, 88.0) / 255.0;
static const float3 TS_GRAVEL_RIM = float3(126.0, 104.0, 76.0) / 255.0;
static const float3 TS_GRAVEL_OUTLINE = float3(102.0, 84.0, 60.0) / 255.0;
static const float3 TS_GRAVEL_SHADOW = float3(38.0, 52.0, 24.0) / 255.0;

static const float3 TS_STONE_MORTAR = float3(98.0, 88.0, 76.0) / 255.0;
static const float3 TS_STONE_RIM = float3(112.0, 98.0, 80.0) / 255.0;
static const float3 TS_STONE_OUTLINE = float3(76.0, 68.0, 58.0) / 255.0;
static const float3 TS_STONE_SHADOW = float3(34.0, 44.0, 22.0) / 255.0;
static const float3 TS_STONE_GRAVEL_SHADOW = float3(46.0, 40.0, 32.0) / 255.0;

// The textures and the control map's placement: rect.xy is the world position of cell (0, 0), rect.zw its size.
struct TSInputs
{
    UnityTexture2D control;
    UnityTexture2DArray albedo;
    UnityTexture2DArray field;
    UnityTexture2DArray height;
    float4 rect;
};

// The four cells around the corner nearest a pixel (down-left, down-right, up-left, up-right), the pixel centre
// relative to that corner in pixels, and its bilinear position between the four cell centres.
struct TSCorner
{
    uint4 cells;
    float2 uv;
    float2 st;
};

// How an upper layer's edge is drawn over a lower one: rim, outline and shadow.
struct TSPair
{
    float3 rimColour;
    float rim;
    float3 outline;
    float hasOutline;
    float3 shadow;
    float shadowAlpha;
    float shadowWidth;
};

// sRGB colour to the colour the target expects.
float3 TSToOutput(float3 c)
{
#ifdef UNITY_COLORSPACE_GAMMA
    return c;
#else
    float3 low = c / 12.92;
    float3 high = pow((max(c, 0.0) + 0.055) / 1.055, 2.4);
    return lerp(high, low, step(c, 0.04045));
#endif
}

// A pixel coordinate wrapped into the materials' repeat.
float2 TSWrap(float2 p)
{
    return p - TS_REPEAT * floor(p / TS_REPEAT);
}

// The layer bits of one cell of the control map; cells outside the map are empty.
uint TSCell(TSInputs ins, int2 c)
{
    if (c.x < 0 || c.y < 0 || c.x >= (int)ins.rect.z || c.y >= (int)ins.rect.w)
    {
        return 0;
    }
    return (uint)(LOAD_TEXTURE2D(ins.control.tex, c).r * 255.0 + 0.5);
}

// The corner nearest the centre of a map pixel and the four cells around it.
TSCorner TSLoadCorner(TSInputs ins, float2 pixel)
{
    TSCorner corner;
    float2 centre = pixel + 0.5;
    float2 c = floor(centre / TS_PPU + 0.5);
    corner.uv = centre - c * TS_PPU;
    corner.st = corner.uv / TS_PPU + 0.5;
    int2 ci = (int2)c;
    corner.cells = uint4(TSCell(ins, ci + int2(-1, -1)), TSCell(ins, ci + int2(0, -1)), TSCell(ins, ci + int2(-1, 0)), TSCell(ins, ci));
    return corner;
}

// Which of the four cells a layer covers: a cell counts for a layer when that layer or any layer above it is filled,
// so a lower layer continues under the upper ones.
uint TSMask(TSCorner corner, uint layer)
{
    uint mask = 0;
    [unroll]
    for (uint k = 0; k < 4; k++)
    {
        if ((corner.cells[k] >> layer) != 0)
        {
            mask |= 1u << k;
        }
    }
    return mask;
}

// The highest layer filled in a cell.
uint TSTopLayer(uint cell)
{
    return (cell & 4u) != 0 ? 2u : ((cell & 2u) != 0 ? 1u : 0u);
}

uint TSBitCount(uint m)
{
    return (m & 1u) + ((m >> 1) & 1u) + ((m >> 2) & 1u) + ((m >> 3) & 1u);
}

uint TSLowestBit(uint m)
{
    return (m & 1u) != 0 ? 0u : ((m & 2u) != 0 ? 1u : ((m & 4u) != 0 ? 2u : 3u));
}

// The direction from the corner to cell k of a corner.
float2 TSDirection(uint k)
{
    return float2((k & 1u) != 0 ? 1.0 : -1.0, (k & 2u) != 0 ? 1.0 : -1.0);
}

float TSSdQuarter(float x, float y)
{
    if (x >= 0.0 && y >= 0.0)
    {
        return -min(x, y);
    }
    return length(float2(min(x, 0.0), min(y, 0.0)));
}

float TSConvex(float2 p, float r)
{
    return TSSdQuarter(p.x - r, p.y - r) - r;
}

// Exact signed distance in pixels to the filled cells around a corner, positive outside: outer corners rounded,
// inner corners filleted, straight where a boundary crosses a cell border (pathtiles.py's shape_sd).
float TSShapeSd(uint mask, float2 uv)
{
    if (mask == 0)
    {
        return TS_FAR;
    }
    if (mask == 15)
    {
        return -TS_FAR;
    }
    uint count = TSBitCount(mask);
    if (count == 1)
    {
        return TSConvex(TSDirection(TSLowestBit(mask)) * uv, TS_R_OUT);
    }
    if (count == 3)
    {
        return -TSConvex(TSDirection(TSLowestBit(~mask & 15u)) * uv, TS_R_IN);
    }
    if (mask == 3)
    {
        return uv.y;
    }
    if (mask == 12)
    {
        return -uv.y;
    }
    if (mask == 5)
    {
        return uv.x;
    }
    if (mask == 10)
    {
        return -uv.x;
    }
    uint first = TSLowestBit(mask);
    uint second = TSLowestBit(mask & ~(1u << first));
    return min(TSConvex(TSDirection(first) * uv, TS_R_OUT), TSConvex(TSDirection(second) * uv, TS_R_OUT));
}

// Smooth edge noise of long waves that repeat with the materials, about a pixel high, slope under 1.
float TSEdgeNoise(float2 worldPixel)
{
    float2 q = (TSWrap(worldPixel) + 0.5) * (6.28318530718 / TS_REPEAT);
    return 0.45 * sin(dot(q, float2(4.0, 4.0)) + 1.3)
         + 0.25 * sin(dot(q, float2(8.0, -4.0)) + 4.1)
         + 0.2 * sin(dot(q, float2(4.0, 8.0)) + 2.6);
}

// Each material's soft-mode height term in pixels at a texel.
float3 TSHeights(TSInputs ins, float2 texel)
{
#ifdef _TERRAIN_SOFT
    int2 t = (int2)texel;
    float3 h = float3(LOAD_TEXTURE2D_ARRAY(ins.height.tex, t, 0).r, LOAD_TEXTURE2D_ARRAY(ins.height.tex, t, 1).r, LOAD_TEXTURE2D_ARRAY(ins.height.tex, t, 2).r);
    return TS_HEIGHT_AMPLITUDE * (2.0 * h - 1.0);
#else
    return float3(0.0, 0.0, 0.0);
#endif
}

// Soft mode: the layer's weight interpolated from the four cells, pushed by the heights of the materials on top of
// those cells, as a distance in pixels (positive outside). Diagonal pairs are pulled apart so they never join.
float TSSoftDistance(TSCorner corner, uint layer, float3 heights)
{
    uint mask = TSMask(corner, layer);
    if (mask == 0)
    {
        return TS_FAR;
    }
    if (mask == 15)
    {
        return -TS_FAR;
    }
    float s = corner.st.x;
    float t = corner.st.y;
    float4 b = float4((1.0 - s) * (1.0 - t), s * (1.0 - t), (1.0 - s) * t, s * t);
    float w = 0.0;
    float h = 0.0;
    [unroll]
    for (uint k = 0; k < 4; k++)
    {
        uint cell = corner.cells[k];
        if ((cell >> layer) != 0)
        {
            w += b[k];
            h += b[k] * heights[TSTopLayer(cell)];
        }
    }
    h /= max(w, 1e-4);
    if (mask == 6 || mask == 9)
    {
        w -= 16.0 * TS_DIAGONAL_GAP * s * (1.0 - s) * t * (1.0 - t);
    }
    return -(TS_PPU * (w - 0.5) + h);
}

// A layer's distance at a pixel in the current look.
float TSDistance(TSCorner corner, uint layer, float2 worldPixel, float3 heights)
{
#ifdef _TERRAIN_SOFT
    return TSSoftDistance(corner, layer, heights);
#else
    uint mask = TSMask(corner, layer);
    float d = TSShapeSd(mask, corner.uv);
    return abs(d) >= TS_FAR ? d : d + TSEdgeNoise(worldPixel);
#endif
}

// A layer's distance at another map pixel, used to decide whole chips and stones from their centre.
float TSDistanceAt(TSInputs ins, float2 pixel, uint layer)
{
    float2 worldPixel = pixel + ins.rect.xy * TS_PPU;
    TSCorner corner = TSLoadCorner(ins, pixel);
    return TSDistance(corner, layer, worldPixel, TSHeights(ins, TSWrap(worldPixel)));
}

// The outward direction of a layer's shape at a pixel, from differences one pixel apart.
float2 TSGradient(TSCorner corner, uint layer, float2 worldPixel, float3 heights)
{
    TSCorner right = corner;
    TSCorner left = corner;
    TSCorner up = corner;
    TSCorner down = corner;
    right.uv.x += 1.0;
    right.st.x += 1.0 / TS_PPU;
    left.uv.x -= 1.0;
    left.st.x -= 1.0 / TS_PPU;
    up.uv.y += 1.0;
    up.st.y += 1.0 / TS_PPU;
    down.uv.y -= 1.0;
    down.st.y -= 1.0 / TS_PPU;
    float2 g = float2(
        TSDistance(right, layer, worldPixel + float2(1.0, 0.0), heights) - TSDistance(left, layer, worldPixel - float2(1.0, 0.0), heights),
        TSDistance(up, layer, worldPixel + float2(0.0, 1.0), heights) - TSDistance(down, layer, worldPixel - float2(0.0, 1.0), heights));
    float length2 = dot(g, g);
    return length2 > 1e-6 ? g * rsqrt(length2) : float2(0.0, 0.0);
}

// The edge settings for an upper layer over a lower one (-1 for nothing).
TSPair TSGetPair(uint upper, int lower)
{
    TSPair p = (TSPair)0;
    if (upper == 0)
    {
        p.rimColour = TS_GRASS_HIGHLIGHT;
        p.rim = 0.0;
        p.outline = TS_GRASS_OUTLINE;
        p.hasOutline = 1.0;
        p.shadow = TS_GRASS_FRINGE;
        p.shadowAlpha = 0.35;
        p.shadowWidth = 1.0;
    }
    else if (upper == 1)
    {
        p.rimColour = TS_GRAVEL_RIM;
        p.rim = 1.75;
        p.outline = TS_GRAVEL_OUTLINE;
        p.hasOutline = 1.0;
        p.shadow = TS_GRAVEL_SHADOW;
        p.shadowAlpha = 0.32;
        p.shadowWidth = 1.0;
    }
    else if (lower == 1)
    {
        p.rimColour = TS_STONE_MORTAR;
        p.rim = 0.75;
        p.outline = TS_STONE_MORTAR;
        p.hasOutline = 0.0;
        p.shadow = TS_STONE_GRAVEL_SHADOW;
        p.shadowAlpha = 0.28;
        p.shadowWidth = 1.0;
    }
    else
    {
        p.rimColour = TS_STONE_RIM;
        p.rim = 1.5;
        p.outline = TS_STONE_OUTLINE;
        p.hasOutline = 1.0;
        p.shadow = TS_STONE_SHADOW;
        p.shadowAlpha = 0.34;
        p.shadowWidth = 1.0;
    }
#ifdef _TERRAIN_SOFT
    if (upper != 0)
    {
        p.rim = TS_SOFT_RIM;
    }
#endif
    return p;
}

// Grass inside its edge: the light fringe bands of today's grass edge, then the albedo.
float3 TSGrass(TSInputs ins, float depth, int2 texel)
{
#ifdef _TERRAIN_SOFT
    if (depth < 1.0)
    {
        return TS_GRASS_OUTLINE;
    }
    depth -= 0.5;
#endif
    if (depth < 2.5)
    {
        return TS_GRASS_HIGHLIGHT;
    }
    if (depth < 5.5)
    {
        return TS_GRASS_LIME;
    }
    if (depth < 6.5)
    {
        return TS_GRASS_DARK;
    }
    if (depth < 9.0)
    {
        return TS_GRASS_MID;
    }
    return LOAD_TEXTURE2D_ARRAY(ins.albedo.tex, texel, 0).rgb;
}

// Gravel inside its edge: a dirt rim, then chips drawn whole only where their centre lies clear of the rim.
float3 TSGravel(TSInputs ins, TSPair p, float depth, float2 pixel, int2 texel)
{
    float4 f = LOAD_TEXTURE2D_ARRAY(ins.field.tex, texel, 1);
    float amount = (f.b * 255.0 - 128.0) / 255.0;
    float3 dirt = TS_GRAVEL_DIRT + amount;
    if (depth < p.rim)
    {
#ifdef _TERRAIN_SOFT
        return p.rimColour + amount;
#else
        return lerp(p.rimColour + amount, dirt, pow(saturate(depth / max(p.rim, 0.001)), 1.5));
#endif
    }
    if (f.a > 0.0)
    {
        float2 offset = round(f.rg * 255.0) - 128.0;
        if (-TSDistanceAt(ins, pixel + offset, 1) < p.rim + 1.25)
        {
            return dirt;
        }
    }
    return LOAD_TEXTURE2D_ARRAY(ins.albedo.tex, texel, 1).rgb;
}

// Stone inside its edge: a rim, stones whose centre lies near the rim left out, the rest cut along the rim's line
// and shaded to that cut.
float3 TSStone(TSInputs ins, TSPair p, float depth, float2 pixel, int2 texel, TSCorner corner, float2 worldPixel, float3 heights)
{
    float4 f = LOAD_TEXTURE2D_ARRAY(ins.field.tex, texel, 2);
    float grit = (f.b * 255.0 - 128.0) / 255.0;
    float3 mortar = TS_STONE_MORTAR + grit;
    if (depth < p.rim)
    {
#ifdef _TERRAIN_SOFT
        return p.rimColour + grit;
#else
        return lerp(p.rimColour, TS_STONE_MORTAR, saturate(depth / max(p.rim, 0.001))) + grit;
#endif
    }
    float2 offset = round(f.rg * 255.0) - 128.0;
    if (-TSDistanceAt(ins, pixel + offset, 2) < p.rim + 1.0)
    {
        return mortar;
    }
    float inside = depth - p.rim;
    if (inside < 0.5)
    {
        return mortar;
    }
    float3 albedo = LOAD_TEXTURE2D_ARRAY(ins.albedo.tex, texel, 2).rgb;
    float border = f.a * 255.0 / 16.0;
    if (inside < border)
    {
        float lit = dot(TSGradient(corner, 2, worldPixel, heights), TS_LIGHT);
        float bevel = saturate(1.0 - (inside - 0.5) / 1.25);
        float amountCut = lit * 18.0 * bevel - (lit < -0.35 ? 8.0 * bevel : 0.0);
        return albedo + amountCut / 255.0;
    }
    return albedo;
}

// Soft mode, stone over gravel: whole cobbles show where their centre lies inside the stone layer, give or take a
// random amount per cobble, so the cobbles' outlines make the edge.
float4 TSStonesOverGravel(TSInputs ins, float2 pixel, int2 texel)
{
    float4 f = LOAD_TEXTURE2D_ARRAY(ins.field.tex, texel, 2);
    float2 offset = round(f.rg * 255.0) - 128.0;
    float jitter = (LOAD_TEXTURE2D_ARRAY(ins.height.tex, texel, 2).g - 0.5) * 2.0 * TS_STONE_JITTER;
    float4 result = float4(0.0, 0.0, 0.0, 0.0);
    if (-TSDistanceAt(ins, pixel + offset, 2) + jitter > 1.0)
    {
        float grit = (f.b * 255.0 - 128.0) / 255.0;
        float3 colour = f.a * 255.0 / 16.0 < 0.5 ? TS_STONE_MORTAR + grit : LOAD_TEXTURE2D_ARRAY(ins.albedo.tex, texel, 2).rgb;
        result = float4(colour, 1.0);
    }
    return result;
}

// A layer's colour inside its shape.
float3 TSInterior(TSInputs ins, uint layer, TSPair p, float depth, float2 pixel, int2 texel, TSCorner corner, float2 worldPixel, float3 heights)
{
    [branch]
    if (layer == 0)
    {
        return TSGrass(ins, depth, texel);
    }
    [branch]
    if (layer == 1)
    {
        return TSGravel(ins, p, depth, pixel, texel);
    }
    return TSStone(ins, p, depth, pixel, texel, corner, worldPixel, heights);
}

// One layer's colour at a pixel, premultiplied: shadow, outline, rim and interior in crisp; a hard-edged shape with
// a rim band in soft.
float4 TSLayer(TSInputs ins, uint layer, int lower, float d, float2 pixel, int2 texel, TSCorner corner, float2 worldPixel, float3 heights)
{
    TSPair p = TSGetPair(layer, lower);
#ifdef _TERRAIN_SOFT
    float4 result = float4(0.0, 0.0, 0.0, 0.0);
    [branch]
    if (layer == 2 && lower == 1)
    {
        result = TSStonesOverGravel(ins, pixel, texel);
    }
    else if (d < 0.0)
    {
        result = float4(TSInterior(ins, layer, p, -d, pixel, texel, corner, worldPixel, heights), 1.0);
    }
    return result;
#else
    if (d >= 0.5 + p.shadowWidth)
    {
        return float4(0.0, 0.0, 0.0, 0.0);
    }
    if (d >= 0.5)
    {
        float shadowAlpha = p.shadowAlpha * (1.0 - (d - 0.5) / p.shadowWidth);
        return float4(p.shadow * shadowAlpha, shadowAlpha);
    }
    if (d > -0.5)
    {
        float edgeAlpha = p.shadowAlpha + (1.0 - p.shadowAlpha) * (0.5 - d);
        float3 edge = p.hasOutline > 0.5 ? p.outline : p.rimColour;
        return float4(edge * edgeAlpha, edgeAlpha);
    }
    float depth = -d;
    float3 colour = TSInterior(ins, layer, p, depth, pixel, texel, corner, worldPixel, heights);
    if (p.hasOutline > 0.5 && depth < 1.5)
    {
        colour = lerp(p.outline, colour, saturate(depth - 0.5));
    }
    return float4(colour, 1.0);
#endif
}

// Whether a layer's own colour hides everything under it at this pixel.
bool TSOpaque(uint layer, int lower, float d)
{
#ifdef _TERRAIN_SOFT
    return d < 0.0 && !(layer == 2 && lower == 1);
#else
    return d <= -0.5;
#endif
}

// The terrain at a world position: snaps to the 32 px grid, reads the four cells around the nearest corner and
// composites grass, gravel and stone bottom up with each pair's edge. DebugView.x > 0.5 outputs the index of the
// topmost visible layer instead, as (index + 1) / 4, and 0 where nothing is drawn.
void TerrainSpike_float(float3 WorldPos, UnityTexture2D ControlMap, UnityTexture2DArray Albedo, UnityTexture2DArray Field, UnityTexture2DArray Height, float4 ControlRect, float4 Backdrop, float4 DebugView, out float3 Color, out float Alpha)
{
    TSInputs ins;
    ins.control = ControlMap;
    ins.albedo = Albedo;
    ins.field = Field;
    ins.height = Height;
    ins.rect = ControlRect;

    float2 worldPixel = floor(WorldPos.xy * TS_PPU);
    float2 pixel = worldPixel - ControlRect.xy * TS_PPU;
    int2 texel = (int2)TSWrap(worldPixel);
    TSCorner corner = TSLoadCorner(ins, pixel);
    float3 heights = TSHeights(ins, (float2)texel);

    float d[3];
    bool present[3];
    [unroll]
    for (uint l = 0; l < 3; l++)
    {
        d[l] = TSDistance(corner, l, worldPixel, heights);
        present[l] = d[l] < TS_FAR;
    }

    bool suppressed[3];
    int lowers[3];
    uint start = 0;
    [unroll]
    for (uint m = 0; m < 3; m++)
    {
        uint up = min(m + 1u, 2u);
        suppressed[m] = !present[m] || (m < 2u && present[up] && d[up] - d[m] < TS_MARGIN);
        lowers[m] = -1;
        [unroll]
        for (int j = (int)m - 1; j >= 0; j--)
        {
            if (lowers[m] < 0 && !suppressed[j] && d[j] < 0.5)
            {
                lowers[m] = j;
            }
        }
        if (!suppressed[m] && TSOpaque(m, lowers[m], d[m]))
        {
            start = m;
        }
    }

    float4 acc = float4(Backdrop.rgb * Backdrop.a, Backdrop.a);
    float visible = 0.0;
    [unroll]
    for (uint n = 0; n < 3; n++)
    {
        [branch]
        if (n >= start && !suppressed[n])
        {
            float4 c = TSLayer(ins, n, lowers[n], d[n], pixel, texel, corner, worldPixel, heights);
            acc = c + acc * (1.0 - c.a);
            if (c.a >= 0.5)
            {
                visible = (n + 1.0) * 0.25;
            }
        }
    }

    if (DebugView.x > 0.5)
    {
        Color = float3(visible, visible, visible);
        Alpha = 1.0;
        return;
    }
    Color = TSToOutput(acc.a > 0.0 ? acc.rgb / acc.a : float3(0.0, 0.0, 0.0));
    Alpha = acc.a;
}

void TerrainSpike_half(half3 WorldPos, UnityTexture2D ControlMap, UnityTexture2DArray Albedo, UnityTexture2DArray Field, UnityTexture2DArray Height, half4 ControlRect, half4 Backdrop, half4 DebugView, out half3 Color, out half Alpha)
{
    float3 colour;
    float alpha;
    TerrainSpike_float(WorldPos, ControlMap, Albedo, Field, Height, ControlRect, Backdrop, DebugView, colour, alpha);
    Color = colour;
    Alpha = alpha;
}

#endif
