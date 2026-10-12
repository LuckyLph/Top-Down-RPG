#ifndef TOPDOWNRPG_TERRAIN_BLEND_INCLUDED
#define TOPDOWNRPG_TERRAIN_BLEND_INCLUDED

#include "Assets/Shaders/Include/PixelSnap.hlsl"
#include "Assets/Shaders/Include/PeriodicNoise.hlsl"
#include "Assets/Shaders/Include/RoundedCorners.hlsl"

#define TB_MAX_LAYERS 8
#define TB_FAR 100.0
#define TB_RAMP_COLUMN 5
#define TB_RAMP_TEXELS 32
#define TB_RAMP_STEP 0.5
#define TB_DIAGONAL_GAP 4.0
#define TB_WHOLE_CLEARANCE 10.0

static const float2 TB_LIGHT = float2(-0.6, 0.8);

// The renderer's inputs. The lookup texture (see TerrainBlendLut) holds the style as data: row 0 the shared settings,
// then a row per layer (its material's shape, interior and height scale), then a row per pair of an upper layer over a
// lower one or over nothing (its edge in both looks). rect.xy is the world position of control map cell (0, 0),
// rect.zw its size. The soft look (keyword _TERRAIN_SOFT) reads the height array; the crisp look never does.
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
    float4 shapes[TB_MAX_LAYERS];
};

// The four cells around the corner nearest a pixel (down-left, down-right, up-left, up-right) and the pixel centre
// relative to that corner, in pixels.
struct TBCorner
{
    uint4 cells;
    float2 uv;
};

// How an upper layer's edge is drawn at a pixel: the settings of each pair it may be drawn over, blended by weight.
// In the soft look a pair with a band draws no outline and fills with the band's colour; edge is the colour of the
// opaque edge pixel, each pair's own (its outline where it draws one, else its fill) blended by weight, and
// bandWeight is how much of the edge pairs with a band make up.
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

// The layer bits of one cell of the control map; cells outside the map are empty.
uint TBCell(TBInputs ins, int2 c)
{
    if (c.x < 0 || c.y < 0 || c.x >= (int)ins.rect.z || c.y >= (int)ins.rect.w)
    {
        return 0u;
    }
    return (uint)(LOAD_TEXTURE2D(ins.control.tex, c).r * 255.0 + 0.5);
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
    return corner;
}

// Which of the four cells a layer covers: a cell counts for a layer when that layer or any layer above it is filled,
// so a lower layer continues under the upper ones.
uint TBMask(TBCorner corner, uint layer)
{
    uint mask = 0u;
    [unroll]
    for (uint k = 0u; k < 4u; k++)
    {
        if ((corner.cells[k] >> layer) != 0u)
        {
            mask |= 1u << k;
        }
    }
    return mask;
}

// The highest layer filled in a cell (0 for an empty cell).
uint TBTopLayer(uint cell)
{
    uint top = 0u;
    [unroll]
    for (uint l = 1u; l < (uint)TB_MAX_LAYERS; l++)
    {
        if ((cell >> l) != 0u)
        {
            top = l;
        }
    }
    return top;
}

// Soft: each of the four cells' height term in pixels at a pixel, from the material of the cell's top layer: its
// height (the height texture's red channel) from -1 to 1 times its height scale, which is 0 for a material without a
// height texture.
float4 TBCellHeights(TBInputs ins, TBCorner corner, float2 worldPixel)
{
    float4 heights = float4(0.0, 0.0, 0.0, 0.0);
    [unroll]
    for (uint k = 0u; k < 4u; k++)
    {
        [branch]
        if (corner.cells[k] != 0u)
        {
            uint top = TBTopLayer(corner.cells[k]);
            float scale = TBLut(ins, 3, 1 + (int)top).x;
            int2 texel = (int2)WrapPixel(worldPixel, ins.shapes[top].z);
            heights[k] = scale * (2.0 * LOAD_TEXTURE2D_ARRAY(ins.height.tex, texel, top).r - 1.0);
        }
    }
    return heights;
}

// Soft: layer l's signed distance in pixels at a corner patch, positive outside. Its weight is interpolated
// bilinearly from the four cells (1 where the layer or one above it is filled), less a bump on diagonal pairs so two
// diagonal cells never join through the saddle point; the cells' heights, mixed by the same weights over the filled
// cells, are added, so edges that coincide stay equal. The layer covers a pixel where its weight, in pixels across a
// cell, plus the height passes one half: there the distance falls below 0.5, where crisp's edge pixel ends, so the
// rest of the pipeline is shared. Slope is the weight's slope per pixel, in pixels (32 times the weight). TB_FAR
// where the layer covers none of the four cells, -TB_FAR where it covers all.
float TBSoftDistance(TBCorner corner, uint l, float4 heights, out float2 slope)
{
    uint mask = TBMask(corner, l);
    slope = float2(0.0, 0.0);
    if (mask == 0u)
    {
        return TB_FAR;
    }
    if (mask == 15u)
    {
        return -TB_FAR;
    }
    float2 st = corner.uv / ART_PIXELS_PER_UNIT + 0.5;
    float s = st.x;
    float t = st.y;
    float4 b = float4((1.0 - s) * (1.0 - t), s * (1.0 - t), (1.0 - s) * t, s * t);
    float4 bs = float4(t - 1.0, 1.0 - t, -t, t);
    float4 bt = float4(s - 1.0, -s, 1.0 - s, s);
    float w = 0.0;
    float h = 0.0;
    [unroll]
    for (uint k = 0u; k < 4u; k++)
    {
        if ((mask & (1u << k)) != 0u)
        {
            w += b[k];
            h += b[k] * heights[k];
            slope += float2(bs[k], bt[k]);
        }
    }
    h /= max(w, 1e-4);
    if (mask == 6u || mask == 9u)
    {
        w -= TB_DIAGONAL_GAP * s * (1.0 - s) * t * (1.0 - t);
        slope -= TB_DIAGONAL_GAP * float2((1.0 - 2.0 * s) * t * (1.0 - t), (1.0 - 2.0 * t) * s * (1.0 - s));
    }
    return 0.5 - (ART_PIXELS_PER_UNIT * (w - 0.5) + h);
}

// Every layer's signed distance in pixels at a corner patch, positive outside; TB_FAR where a layer covers none of
// the four cells. Crisp: the rounded shape pushed by the shared edge noise, so edges that coincide stay equal. Soft:
// TBSoftDistance.
void TBDistances(TBInputs ins, TBCorner corner, float2 worldPixel, out float d[TB_MAX_LAYERS])
{
#ifdef _TERRAIN_SOFT
    float4 heights = TBCellHeights(ins, corner, worldPixel);
    float2 slope;
#else
    float noise = PeriodicEdgeNoise(worldPixel) * ins.globals.x;
#endif
    [unroll]
    for (uint l = 0u; l < (uint)TB_MAX_LAYERS; l++)
    {
        d[l] = TB_FAR;
        if (l < ins.layers)
        {
#ifdef _TERRAIN_SOFT
            d[l] = TBSoftDistance(corner, l, heights, slope);
#else
            float sd = CornerShapeDistance(TBMask(corner, l), corner.uv, ins.shapes[l].x, ins.shapes[l].y, TB_FAR);
            d[l] = abs(sd) >= TB_FAR ? sd : sd + noise;
#endif
        }
    }
}

// Whether a layer is drawn at a pixel: it covers the pixel's corner patch and its edge lies at least the pair margin
// beyond the edge of the layer above it, so where two edges coincide only the upper one is drawn.
bool TBVisible(TBInputs ins, float d[TB_MAX_LAYERS], uint m)
{
    bool present = d[m] < TB_FAR;
    uint up = min(m + 1u, (uint)TB_MAX_LAYERS - 1u);
    bool covered = m + 1u < ins.layers && d[up] < TB_FAR && d[up] - d[m] < ins.globals.y;
    return present && !covered;
}

// The weight of each pair layer m may be drawn over at a pixel: index j + 1 for lower layer j, index 0 for nothing.
// Going down from m, a lower layer that covers the pixel takes the remaining weight in proportion to how far its edge
// lies beyond the next layer up, ramping across the pair blend width around the pair margin, so the pair settings
// change smoothly instead of switching where the margin is crossed.
void TBPairWeights(TBInputs ins, float d[TB_MAX_LAYERS], uint m, out float w[TB_MAX_LAYERS + 1])
{
    [unroll]
    for (uint i = 0u; i <= (uint)TB_MAX_LAYERS; i++)
    {
        w[i] = 0.0;
    }
    float remaining = 1.0;
    float blend = max(ins.globals.z, 0.001);
    [unroll]
    for (int j = TB_MAX_LAYERS - 2; j >= 0; j--)
    {
        if (j < (int)m && d[j] < 0.5)
        {
            float v = saturate((d[j + 1] - d[j] - ins.globals.y) / blend + 0.5);
            w[j + 1] = remaining * v;
            remaining -= w[j + 1];
        }
    }
    w[0] = remaining;
}

// The edge settings of layer m at a pixel, blended from the pairs it is drawn over. In the soft look a pair with a
// band draws no outline and fills its edge pixel with the band's colour.
TBEdge TBGetEdge(TBInputs ins, uint m, float w[TB_MAX_LAYERS + 1])
{
    TBEdge e = (TBEdge)0;
    e.firstRow = 1 + (int)ins.layers + (int)m * ((int)ins.layers + 1);
    [unroll]
    for (uint k = 0u; k <= (uint)TB_MAX_LAYERS; k++)
    {
        [branch]
        if (k <= m && w[k] > 0.0)
        {
            int row = e.firstRow + (int)k;
            float4 shadow = TBLut(ins, 0, row);
            float4 outline = TBLut(ins, 1, row);
            float4 widths = TBLut(ins, 2, row);
            float4 fill = TBLut(ins, 3, row);
            e.shadow += w[k] * shadow.rgb;
            e.shadowAlpha += w[k] * shadow.a;
            e.outline += w[k] * outline.rgb;
            e.shadowWidth += w[k] * widths.x;
            e.tuftHeight += w[k] * widths.z;
#ifdef _TERRAIN_SOFT
            float4 band = TBLut(ins, 4, row);
            float banded = band.a > 0.0 ? 1.0 : 0.0;
            float3 filled = lerp(fill.rgb, band.rgb, banded);
            e.fill += w[k] * filled;
            e.edge += w[k] * lerp(filled, outline.rgb, outline.a * (1.0 - banded));
            e.bandWeight += w[k] * banded;
#else
            e.outlineWeight += w[k] * outline.a;
            e.fill += w[k] * fill.rgb;
#endif
        }
    }
    return e;
}

// The rim at a depth inside the edge, premultiplied: its colour and how much of the interior it covers. In the soft
// look a pair with a band covers the interior with the band's colour up to the band's width instead; band is how much
// of the result such bands make up (0 in crisp).
float4 TBRamp(TBInputs ins, TBEdge e, uint m, float w[TB_MAX_LAYERS + 1], float depth, out float band)
{
    float4 result = float4(0.0, 0.0, 0.0, 0.0);
    band = 0.0;
    int column = TB_RAMP_COLUMN + (int)min(floor(depth / TB_RAMP_STEP), TB_RAMP_TEXELS - 1.0);
    [unroll]
    for (uint k = 0u; k <= (uint)TB_MAX_LAYERS; k++)
    {
        [branch]
        if (k <= m && w[k] > 0.0)
        {
            float4 r = TBLut(ins, column, e.firstRow + (int)k);
#ifdef _TERRAIN_SOFT
            float4 settings = TBLut(ins, 4, e.firstRow + (int)k);
            if (settings.a > 0.0)
            {
                float cover = depth < settings.a ? 1.0 : 0.0;
                r = float4(settings.rgb, cover);
                band += w[k] * cover;
            }
#endif
            result += w[k] * float4(r.rgb * r.a, r.a);
        }
    }
    return result;
}

// The outward direction of layer m's shape at a pixel of a corner patch, from distances one pixel apart, with the
// shared edge noise scaled by amplitude (0 for the bare shape).
float2 TBGradient(TBInputs ins, TBCorner corner, uint m, float2 worldPixel, float amplitude)
{
    uint mask = TBMask(corner, m);
    float rOut = ins.shapes[m].x;
    float rIn = ins.shapes[m].y;
    float right = CornerShapeDistance(mask, corner.uv + float2(1.0, 0.0), rOut, rIn, TB_FAR) + PeriodicEdgeNoise(worldPixel + float2(1.0, 0.0)) * amplitude;
    float left = CornerShapeDistance(mask, corner.uv - float2(1.0, 0.0), rOut, rIn, TB_FAR) + PeriodicEdgeNoise(worldPixel - float2(1.0, 0.0)) * amplitude;
    float up = CornerShapeDistance(mask, corner.uv + float2(0.0, 1.0), rOut, rIn, TB_FAR) + PeriodicEdgeNoise(worldPixel + float2(0.0, 1.0)) * amplitude;
    float down = CornerShapeDistance(mask, corner.uv - float2(0.0, 1.0), rOut, rIn, TB_FAR) + PeriodicEdgeNoise(worldPixel - float2(0.0, 1.0)) * amplitude;
    float2 g = float2(right - left, up - down);
    float length2 = dot(g, g);
    return length2 > 1e-6 ? g * rsqrt(length2) : float2(0.0, 0.0);
}

// The outward direction of layer m's edge at a pixel of a corner patch: in crisp the gradient of the shape with the
// edge noise, in soft against the slope of the layer's weight.
float2 TBOutward(TBInputs ins, TBCorner corner, uint m, float2 worldPixel)
{
#ifdef _TERRAIN_SOFT
    float2 slope;
    TBSoftDistance(corner, m, float4(0.0, 0.0, 0.0, 0.0), slope);
    float length2 = dot(slope, slope);
    return length2 > 1e-6 ? -slope * rsqrt(length2) : float2(0.0, 0.0);
#else
    return TBGradient(ins, corner, m, worldPixel, ins.globals.x);
#endif
}

// How deep inside layer m a map pixel lies, from the corner patch around it.
float TBDepthAt(TBInputs ins, uint m, float2 pixel, float2 worldPixel)
{
    TBCorner corner = TBLoadCorner(ins, pixel);
    float d[TB_MAX_LAYERS];
    TBDistances(ins, corner, worldPixel, d);
    return -d[m];
}

// What the chip or stone whose centre is a map pixel sees there (see TBCentre); in the soft look the outward direction
// only when asked for, since only whole chips use it.
TBCentre TBCentreAt(TBInputs ins, uint m, float2 pixel, float2 worldPixel, bool outward)
{
    TBCentre centre = (TBCentre)0;
    TBCorner corner = TBLoadCorner(ins, pixel);
    float d[TB_MAX_LAYERS];
    TBDistances(ins, corner, worldPixel, d);
    float w[TB_MAX_LAYERS + 1];
    TBPairWeights(ins, d, m, w);
    int firstRow = 1 + (int)ins.layers + (int)m * ((int)ins.layers + 1);
    [unroll]
    for (uint k = 0u; k <= (uint)TB_MAX_LAYERS; k++)
    {
        [branch]
        if (k <= m && w[k] > 0.0)
        {
            float4 widths = TBLut(ins, 2, firstRow + (int)k);
#ifdef _TERRAIN_SOFT
            float band = TBLut(ins, 4, firstRow + (int)k).a;
            float whole = widths.w > 0.5 && (k == 0u || d[max((int)k - 1, 0)] <= -TB_WHOLE_CLEARANCE) ? 1.0 : 0.0;
            centre.rim += w[k] * (whole > 0.5 ? 0.0 : (band > 0.0 ? band : widths.y));
            centre.whole += w[k] * whole;
#else
            centre.rim += w[k] * widths.y;
#endif
        }
    }
    centre.depth = -d[m];
#ifdef _TERRAIN_SOFT
    [branch]
    if (outward)
    {
        centre.outward = TBOutward(ins, corner, m, worldPixel);
    }
#endif
    return centre;
}

// Layer m's material inside its shape, before the rim: the albedo, and where the material has chips or stones (a cell
// field) the base colour under any chip or stone left out. A whole chip is left out unless all of it lies the keep
// margin clear of the rim: its field's alpha holds its reach from its centre pixel. In crisp that reach is stretched
// by the edge noise's steepest slope, since the distance can fall that much faster than the pixels move away; in soft
// the depth is measured where the chip comes nearest the edge, its reach from the centre along the edge's outward
// direction there. A stone is left out when its centre lies too near the rim, and the others are cut along the rim's
// line, shaded to the light like the stones' own edges. With hasCentre, known is what the pixel's chip or stone sees
// at its centre, already worked out by the caller.
float3 TBInterior(TBInputs ins, uint m, float depth, float2 pixel, float2 worldPixel, TBCorner corner, bool hasCentre, TBCentre known)
{
    float4 shape = ins.shapes[m];
    int2 texel = (int2)WrapPixel(worldPixel, shape.z);
    float3 colour = LOAD_TEXTURE2D_ARRAY(ins.albedo.tex, texel, m).rgb;
    [branch]
    if (shape.w > 0.5)
    {
        float4 f = LOAD_TEXTURE2D_ARRAY(ins.field.tex, texel, m);
        [branch]
        if (f.a > 0.0)
        {
            int row = 1 + (int)m;
            float4 chip = TBLut(ins, 1, row);
            float3 base = TBLut(ins, 2, row).rgb + (f.b * 255.0 - 128.0) / 255.0;
            float2 offset = round(f.rg * 255.0) - 128.0;
            TBCentre centre = known;
            [branch]
            if (!hasCentre)
            {
                centre = TBCentreAt(ins, m, pixel + offset, worldPixel + offset, shape.w < 1.5);
            }
            float inside = depth - centre.rim;
            float border = f.a * 255.0 / 16.0;
#ifdef _TERRAIN_SOFT
            float2 reach = shape.w < 1.5 ? border * centre.outward : float2(0.0, 0.0);
            float nearest = shape.w < 1.5 ? TBDepthAt(ins, m, pixel + offset + reach, worldPixel + offset + reach) : centre.depth;
#else
            float nearest = centre.depth - (shape.w < 1.5 ? border * (1.0 + PERIODIC_NOISE_MAX_SLOPE * ins.globals.x) : 0.0);
#endif
            if (nearest < centre.rim + chip.x)
            {
                colour = base;
            }
            else if (shape.w > 1.5 && inside < chip.y)
            {
                colour = base;
            }
            else if (shape.w > 1.5 && inside < border)
            {
                float lit = dot(TBOutward(ins, corner, m, worldPixel), TB_LIGHT);
                float bevel = saturate(1.0 - (inside - chip.y) / 1.25);
                float amount = lit * chip.z * bevel - (lit < -0.35 ? 0.45 * chip.z * bevel : 0.0);
                colour += amount / 255.0;
            }
        }
    }
    return colour;
}

// A hash of the world cell at the corner of a map pixel's corner patch.
uint TBCornerSeed(TBInputs ins, float2 pixel)
{
    int2 cell = (int2)floor((pixel + 0.5) / ART_PIXELS_PER_UNIT + 0.5) + (int2)ins.rect.xy;
    uint seed = (uint)cell.x * 0x8da6b343u ^ (uint)cell.y * 0xd8163841u;
    seed ^= seed >> 13;
    seed *= 0x5bd1e995u;
    return seed ^ (seed >> 15);
}

// How far tufts reach out of an edge at position u along it, in pixels: five triangular spikes per corner patch, as
// wide at the base as about their height, placed, sized and heightened from the patch's seed, clear of the patch's
// border (|u| near 16), where patches meet.
float TBTuftReach(float u, uint seed, float height)
{
    float reach = 0.0;
    [unroll]
    for (uint i = 0u; i < 5u; i++)
    {
        uint bits = seed >> (i * 6u);
        float centre = -10.0 + 5.0 * (float)i + ((float)(bits & 3u) - 1.5) * 0.6;
        float halfWidth = height * (0.46 + (float)((bits >> 2) & 1u) * 0.17);
        float spike = height * (((bits >> 3) & 1u) != 0u ? 1.0 : 0.6);
        reach = max(reach, spike * saturate(1.0 - abs(u - centre) / halfWidth));
    }
    return reach * saturate((14.0 - abs(u)) * 0.5);
}

// Whether a pixel near layer m's edge (d its distance) lies in a tuft, and on the tuft's border. Its position along
// the edge is its position along the bare shape's tangent, so each spike stands straight out of the edge and every
// pixel between its tip and the edge belongs to it, and that position agrees between patches along the straight edge
// where they meet. Spikes keep to straight edges, fading out within a few degrees of curve, since around a corner the
// normals spread or close up and a spike's pixels could meet the edge by a corner alone.
// Returns 0 outside a tuft, 1 inside, 2 on its border.
float TBTuft(TBInputs ins, TBCorner corner, uint m, float d, float2 pixel, float2 worldPixel, float height)
{
#ifdef _TERRAIN_SOFT
    float2 n = TBOutward(ins, corner, m, worldPixel);
#else
    float2 n = TBGradient(ins, corner, m, worldPixel, 0.0);
#endif
    float u = dot(corner.uv, float2(-n.y, n.x));
    uint seed = TBCornerSeed(ins, pixel);
    float scaled = height * saturate(1.0 - 8.0 * abs(n.x * n.y));
    float reach = TBTuftReach(u, seed, scaled);
    float outside = d - 0.5;
    float result = 0.0;
    if (reach > 0.5 && reach > outside)
    {
        bool border = reach - outside < 1.0
            || (outside >= 0.0 && (TBTuftReach(u - 1.0, seed, scaled) <= outside || TBTuftReach(u + 1.0, seed, scaled) <= outside));
        result = border ? 2.0 : 1.0;
    }
    return result;
}

#ifdef _TERRAIN_SOFT
// Layer m's colour at a pixel in the soft look, opaque or clear. Where a pair with whole stones meets the stone the
// pixel belongs to (decided at the stone's centre), the stone is drawn whole, inside or outside the shape, when its
// centre moved by its own height (the height texture's green channel times the material's height scale) lies the
// keep margin inside the rim, and is clear otherwise, so the layer below shows. Elsewhere, from the outside in: the
// tufts where the pair has them, as in crisp; the edge pixel (the outline, or the band's or the rim's outer colour),
// opaque; then the interior under the band or the rim. No shadow is drawn. Bands are shaded by the cell field's grit
// like the base colour.
float4 TBLayer(TBInputs ins, uint m, TBEdge e, float w[TB_MAX_LAYERS + 1], float d, float2 pixel, float2 worldPixel, TBCorner corner)
{
    float4 shape = ins.shapes[m];
    int2 texel = (int2)WrapPixel(worldPixel, shape.z);
    float grit = 0.0;
    float4 result = float4(0.0, 0.0, 0.0, 0.0);
    bool decided = false;
    bool hasCentre = false;
    TBCentre centre = (TBCentre)0;
    [branch]
    if (shape.w > 0.5)
    {
        float4 f = LOAD_TEXTURE2D_ARRAY(ins.field.tex, texel, m);
        grit = (f.b * 255.0 - 128.0) / 255.0;
        float4 chip = TBLut(ins, 1, 1 + (int)m);
        [branch]
        if (chip.w > 0.5)
        {
            float2 offset = round(f.rg * 255.0) - 128.0;
            centre = TBCentreAt(ins, m, pixel + offset, worldPixel + offset, false);
            hasCentre = true;
            if (centre.whole >= 0.5)
            {
                decided = true;
                float jitter = TBLut(ins, 3, 1 + (int)m).x * (2.0 * LOAD_TEXTURE2D_ARRAY(ins.height.tex, texel, m).g - 1.0);
                if (centre.depth + jitter >= centre.rim + chip.x)
                {
                    result = float4(LOAD_TEXTURE2D_ARRAY(ins.albedo.tex, texel, m).rgb, 1.0);
                }
            }
        }
    }

    [branch]
    if (!decided)
    {
        float3 edge = e.edge + grit * e.bandWeight;
        float tuft = 0.0;
        [branch]
        if (e.tuftHeight > 0.0 && d > -0.5 && d < 0.5 + e.tuftHeight)
        {
            tuft = TBTuft(ins, corner, m, d, pixel, worldPixel, e.tuftHeight);
        }

        if (tuft > 0.5)
        {
            result = float4(tuft > 1.5 ? edge : e.fill, 1.0);
        }
        else if (d > -0.5 && d < 0.5)
        {
            result = float4(edge, 1.0);
        }
        else if (d <= -0.5)
        {
            float depth = -d;
            float3 colour = TBInterior(ins, m, depth, pixel, worldPixel, corner, hasCentre, centre);
            float band;
            float4 rim = TBRamp(ins, e, m, w, depth, band);
            colour = colour * (1.0 - rim.a) + rim.rgb + grit * band;
            result = float4(colour, 1.0);
        }
    }
    return result;
}
#else
// Layer m's colour at a pixel, premultiplied: from the outside in, the translucent shadow, the antialiased edge pixel
// (the outline, or the rim's outer colour where the pair draws none), then the interior under the rim, with the
// outline fading into it over the first pixel. Where the pair has tufts, pixels of the edge and of the band outside
// it that a tuft covers are opaque: the outline along the tuft's border, the rim's outer colour inside it.
float4 TBLayer(TBInputs ins, uint m, TBEdge e, float w[TB_MAX_LAYERS + 1], float d, float2 pixel, float2 worldPixel, TBCorner corner)
{
    float3 edge = lerp(e.fill, e.outline, e.outlineWeight);
    float tuft = 0.0;
    [branch]
    if (e.tuftHeight > 0.0 && d > -0.5 && d < 0.5 + e.tuftHeight)
    {
        tuft = TBTuft(ins, corner, m, d, pixel, worldPixel, e.tuftHeight);
    }

    float4 result = float4(0.0, 0.0, 0.0, 0.0);
    [branch]
    if (tuft > 0.5)
    {
        result = float4(tuft > 1.5 ? edge : e.fill, 1.0);
    }
    else if (d >= 0.5 + e.shadowWidth)
    {
        result = float4(0.0, 0.0, 0.0, 0.0);
    }
    else if (d >= 0.5)
    {
        float shadowAlpha = e.shadowAlpha * (1.0 - (d - 0.5) / max(e.shadowWidth, 0.001));
        result = float4(e.shadow * shadowAlpha, shadowAlpha);
    }
    else if (d > -0.5)
    {
        float edgeAlpha = e.shadowAlpha + (1.0 - e.shadowAlpha) * (0.5 - d);
        result = float4(edge * edgeAlpha, edgeAlpha);
    }
    else
    {
        float depth = -d;
        float3 colour = TBInterior(ins, m, depth, pixel, worldPixel, corner, false, (TBCentre)0);
        float band;
        float4 rim = TBRamp(ins, e, m, w, depth, band);
        colour = colour * (1.0 - rim.a) + rim.rgb;
        if (depth < 1.5)
        {
            colour = lerp(lerp(colour, e.outline, e.outlineWeight), colour, saturate(depth - 0.5));
        }
        result = float4(colour, 1.0);
    }
    return result;
}
#endif

// Whether layer m hides everything under it wherever it lies inside its edge: always in crisp; in soft not where it
// may show whole stones, which leave the layer below showing between them.
bool TBOpaqueInside(TBInputs ins, uint m)
{
#ifdef _TERRAIN_SOFT
    return TBLut(ins, 1, 1 + (int)m).w < 0.5;
#else
    return true;
#endif
}

// The debug colour of a layer: grass red, gravel green, stone blue (layers 0 to 2), white above, black for none.
float3 TBDebugColour(int layer)
{
    if (layer < 0)
    {
        return float3(0.0, 0.0, 0.0);
    }
    if (layer > 2)
    {
        return float3(1.0, 1.0, 1.0);
    }
    return float3(layer == 0 ? 1.0 : 0.0, layer == 1 ? 1.0 : 0.0, layer == 2 ? 1.0 : 0.0);
}

// The terrain at a world position: snaps to the 32 px grid, reads the four cells around the nearest corner and
// composites the layers bottom up, each drawn over the layer below it with that pair's edge, in the crisp look or,
// with the keyword _TERRAIN_SOFT, the soft one. Info.x is the number of layers, Info.y > 0.5 outputs the topmost
// visible layer instead as TBDebugColour (pure colours, which the 2D lights scale but never mix).
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
    [unroll]
    for (uint s = 0u; s < (uint)TB_MAX_LAYERS; s++)
    {
        ins.shapes[s] = s < ins.layers ? TBLut(ins, 0, 1 + (int)s) : float4(0.0, 0.0, 128.0, 0.0);
    }

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

    uint start = 0u;
    [unroll]
    for (uint m = 0u; m < (uint)TB_MAX_LAYERS; m++)
    {
        if (m < ins.layers && d[m] <= -0.5 && TBVisible(ins, d, m) && TBOpaqueInside(ins, m))
        {
            start = m;
        }
    }

    float4 acc = float4(0.0, 0.0, 0.0, 0.0);
    int top = -1;
    [loop]
    for (uint n = start; n < ins.layers; n++)
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
