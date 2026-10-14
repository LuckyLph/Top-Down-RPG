#ifndef TOPDOWNRPG_TERRAIN_BLEND_EDGES_INCLUDED
#define TOPDOWNRPG_TERRAIN_BLEND_EDGES_INCLUDED

#include "Assets/Shaders/Terrain/TerrainBlendShape.hlsl"

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
    [loop]
    for (int j = (int)m - 1; j >= 0; j--)
    {
        if (d[j] < 0.5)
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
    [loop]
    for (uint k = 0u; k <= m; k++)
    {
        [branch]
        if (w[k] > 0.0)
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
            e.seamless += w[k] * (1.0 - fill.a);
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
    [loop]
    for (uint k = 0u; k <= m; k++)
    {
        [branch]
        if (w[k] > 0.0)
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

// How much of layer m's edge at a pixel lies over another layer along a seamless pair (deep water over shallows): the
// two are one surface there, so a sunken layer shows no bank over it.
float TBSeamlessBelow(TBInputs ins, uint m, float w[TB_MAX_LAYERS + 1])
{
    int firstRow = 1 + (int)ins.layers + (int)m * ((int)ins.layers + 1);
    float seamless = 0.0;
    [loop]
    for (uint k = 1u; k <= m; k++)
    {
        [branch]
        if (w[k] > 0.0)
        {
            seamless += w[k] * (1.0 - TBLut(ins, 3, firstRow + (int)k).a);
        }
    }
    return seamless;
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

#endif
