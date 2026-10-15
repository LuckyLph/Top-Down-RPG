#ifndef TOPDOWNRPG_TERRAIN_BLEND_LAYER_SOFT_INCLUDED
#define TOPDOWNRPG_TERRAIN_BLEND_LAYER_SOFT_INCLUDED

#include "Assets/Shaders/Terrain/TerrainBlendInterior.hlsl"

// Layer m's colour at a pixel in the soft look, opaque or clear. Where a pair with whole stones meets the stone the
// pixel belongs to (decided at the stone's centre), the stone is drawn whole, inside or outside the shape, when its
// centre moved by its own height (the height texture's green channel times the material's height scale) lies the
// keep margin inside the rim, and is clear otherwise, so the layer below shows. Elsewhere, from the outside in: the
// tufts where the pair has them, as in crisp; the edge pixel (the outline, or the band's or the rim's outer colour),
// opaque, or a sunken layer's bank (TBEdgeColour); then the interior under the band or the rim (TBInside, with a
// sunken layer's bank). No shadow is drawn; past its edge, a layer with swash draws its wash where it covers at least
// half of a pixel, opaque. Bands are shaded by the cell field's grit like the base colour. A layer that
// turns to its runs reads its cell field turned where its cell runs across (TBTurned), as its interior does. The
// interior is worked out once for the edge pixel and the inside (TBLayerInterior).
float4 TBLayer(TBInputs ins, uint m, TBEdge e, float w[TB_MAX_LAYERS + 1], float d, float2 pixel, float2 worldPixel, TBCorner corner)
{
    float4 shape = TBShape(ins, m);
    float grit = 0.0;
    float4 result = float4(0.0, 0.0, 0.0, 0.0);
    bool decided = false;
    bool hasCentre = false;
    TBCentre centre = (TBCentre)0;
    [branch]
    if (shape.w > 0.5)
    {
        bool turned = TBTurned(ins, m, pixel);
        int2 texel = (int2)WrapPixel(TBSampled(worldPixel, turned), shape.z);
        float4 f = LOAD_TEXTURE2D_ARRAY(ins.field.tex, texel, m);
        grit = (f.b * 255.0 - 128.0) / 255.0;
        float4 chip = TBLut(ins, TB_LAYER_CHIPS_COLUMN, TBLayerRow(m));
        [branch]
        if (chip.w > 0.5)
        {
            float2 offset = TBUnturn(round(f.rg * 255.0) - 128.0, turned);
            centre = TBCentreAt(ins, m, pixel + offset, worldPixel + offset, false);
            hasCentre = true;
            if (centre.whole >= 0.5)
            {
                decided = true;
                float jitter = TBLut(ins, TB_LAYER_HEIGHT_COLUMN, TBLayerRow(m)).x * (2.0 * LOAD_TEXTURE2D_ARRAY(ins.height.tex, texel, m).g - 1.0);
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

        float bank;
        float3 interior = TBLayerInterior(ins, m, e, w, d, tuft <= 0.5 && d > -0.5 && d < 0.5, tuft <= 0.5 && d <= -0.5, pixel, worldPixel, corner, hasCentre, centre, bank);
        if (tuft > 0.5)
        {
            result = float4(tuft > 1.5 ? edge : e.fill, 1.0);
        }
        else if (d > -0.5 && d < 0.5)
        {
            float3 own = e.seamless > 0.0 ? TBSeamlessEdge(ins, m, e, w, interior) : edge;
            result = float4(TBEdgeColour(ins, m, w, corner, worldPixel, lerp(edge, own, e.seamless)), 1.0);
        }
        else if (d <= -0.5)
        {
            float band;
            float3 colour = TBInside(ins, m, e, w, -d, worldPixel, interior, bank, band);
            result = float4(colour + grit * band, 1.0);
        }
        else
        {
            float4 wash = TBWash(ins, m, w, d - 0.5, worldPixel);
            if (wash.a >= 0.5)
            {
                result = float4(wash.rgb / wash.a, 1.0);
            }
        }
    }
    return result;
}

#endif
