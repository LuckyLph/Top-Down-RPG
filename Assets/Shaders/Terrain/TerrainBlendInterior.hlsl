#ifndef TOPDOWNRPG_TERRAIN_BLEND_INTERIOR_INCLUDED
#define TOPDOWNRPG_TERRAIN_BLEND_INTERIOR_INCLUDED

#include "Assets/Shaders/Terrain/TerrainBlendRuns.hlsl"
#include "Assets/Shaders/Terrain/TerrainBlendEdges.hlsl"
#include "Assets/Shaders/Terrain/TerrainBlendCells.hlsl"
#include "Assets/Shaders/Terrain/TerrainBlendAnimation.hlsl"
#include "Assets/Shaders/Terrain/TerrainBlendSway.hlsl"
#include "Assets/Shaders/Terrain/TerrainBlendBanks.hlsl"

// Layer m's material inside its shape, before the rim: the albedo, and where the material has chips or stones (a cell
// field) the base colour under any chip or stone left out. A whole chip is left out unless all of it lies the keep
// margin clear of the rim: its field's alpha holds its reach from its centre pixel. Its look measures where it comes
// nearest the edge (TBChipNearest). A stone is left out when its centre lies too near the rim, and the others are cut
// along the rim's line, shaded to the light like the stones' own edges. With hasCentre, known is what the pixel's chip
// or stone sees at its centre, already worked out by the caller. A swaying layer's weeds lean (TBSwayTexel); where they
// lean whole (its sway row's last value), a leaning pixel that would show a texel of the ground (one the cell field
// holds unchanged) shows the ground under itself instead, so only the weeds' own pixels move. A pixel of a weed's box
// (the height's green channel above 0: its row from the row under the weed's base, plus one) shows the ground under it
// instead, from the cell field, unless the weed's foot and its top (the field's alpha: the box's rows above its base)
// both lie where the layer shows (TBOpenOn). A layer that turns to its runs reads all of it turned a quarter where its
// cell runs across (TBTurned), and turns the offsets it reads back into the world (TBUnturn).
float3 TBInterior(TBInputs ins, uint m, float depth, float2 pixel, float2 worldPixel, TBCorner corner, bool hasCentre, TBCentre known)
{
    float4 shape = TBShape(ins, m);
    bool turned = TBTurned(ins, m, pixel);
    float2 sampled = TBSampled(worldPixel, turned);
    int2 texel = (int2)WrapPixel(sampled, shape.z);
    float lean;
    int2 shown = TBSwayTexel(ins, m, sampled, texel, lean);
    float3 colour = TBGlint(ins, m, texel, LOAD_TEXTURE2D_ARRAY(ins.albedo.tex, shown, m).rgb);
    float4 sway = TBLut(ins, TB_LAYER_SWAY_COLUMN, TBLayerRow(m));
    [branch]
    if (sway.x > 0.0)
    {
        [branch]
        if (sway.w > 0.5 && lean != 0.0)
        {
            [branch]
            if (all(LOAD_TEXTURE2D_ARRAY(ins.field.tex, shown, m).rgb == colour))
            {
                colour = LOAD_TEXTURE2D_ARRAY(ins.field.tex, texel, m).rgb;
            }
        }

        float rows = round(LOAD_TEXTURE2D_ARRAY(ins.height.tex, shown, m).g * 255.0);
        [branch]
        if (rows > 0.5)
        {
            float2 down = TBUnturn(float2(-lean, 2.0 - rows), turned);
            float2 up = down + TBUnturn(float2(0.0, round(LOAD_TEXTURE2D_ARRAY(ins.field.tex, shown, m).a * 255.0)), turned);
            [branch]
            if (!TBOpenOn(ins, m, pixel + down, worldPixel + down) || !TBOpenOn(ins, m, pixel + up, worldPixel + up))
            {
                colour = LOAD_TEXTURE2D_ARRAY(ins.field.tex, texel, m).rgb;
            }
        }
    }
    [branch]
    if (shape.w > 0.5)
    {
        float4 f = LOAD_TEXTURE2D_ARRAY(ins.field.tex, texel, m);
        [branch]
        if (f.a > 0.0)
        {
            int row = TBLayerRow(m);
            float4 chip = TBLut(ins, TB_LAYER_CHIPS_COLUMN, row);
            float3 base = TBLut(ins, TB_LAYER_BASE_COLUMN, row).rgb + (f.b * 255.0 - 128.0) / 255.0;
            float2 offset = TBUnturn(round(f.rg * 255.0) - 128.0, turned);
            TBCentre centre = known;
            [branch]
            if (!hasCentre)
            {
                centre = TBCentreAt(ins, m, pixel + offset, worldPixel + offset, shape.w < 1.5);
            }
            float inside = depth - centre.rim;
            float border = f.a * 255.0 / 16.0;
            float nearest = TBChipNearest(ins, m, shape, centre, border, pixel + offset, worldPixel + offset);
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

// The edge pixel of layer m along its seamless pairs: its own interior under the rim's first texel, so a seamless pair
// with a rim (deep water fading in over shallows) starts its rim at the edge, and one without (dry grass over grass)
// shows the interior alone.
float3 TBSeamlessEdge(TBInputs ins, uint m, TBEdge e, float w[TB_MAX_LAYERS + 1], float d, float2 pixel, float2 worldPixel, TBCorner corner, bool hasCentre, TBCentre known)
{
    float3 own = TBInterior(ins, m, max(-d, 0.0), pixel, worldPixel, corner, hasCentre, known);
    float band;
    float4 rim = TBRamp(ins, e, m, w, 0.0, band);
    return own * (1.0 - rim.a) + rim.rgb;
}

// Layer m's colour at a depth inside its edge, before the edge pixel: within the bank, the bank colour of the layer it
// meets (TBBankFace), its lowest pixel darker at the waterline; below it the interior under the rim, which starts at
// the waterline, darkened by the bank's shadow over as many pixels as the bank is high. A level layer has no bank, so
// this is the interior under the rim. band is how much of the rim soft bands make up (0 in crisp and on the bank); bank
// is the bank's height there.
float3 TBInside(TBInputs ins, uint m, TBEdge e, float w[TB_MAX_LAYERS + 1], float depth, float2 pixel, float2 worldPixel, TBCorner corner, bool hasCentre, TBCentre known, out float band, out float bank)
{
    band = 0.0;
    bank = TBBankHeight(ins, m, w, corner, worldPixel, depth);
    float3 colour;
    [branch]
    if (depth < bank)
    {
        float3 face = TBBankFace(ins, m, w);
        colour = depth >= bank - 1.0 ? face * 0.75 : face;
    }
    else
    {
        float below = depth - bank;
        colour = TBInterior(ins, m, depth, pixel, worldPixel, corner, hasCentre, known);
        float4 rim = TBRamp(ins, e, m, w, max(below - TBSwell(ins, m, worldPixel), 0.0), band);
        colour = colour * (1.0 - rim.a) + rim.rgb;
        if (below < bank)
        {
            colour *= 1.0 - TBLut(ins, TB_LAYER_BANK_COLUMN, TBLayerRow(m)).y;
        }
    }
    return colour;
}

#endif
