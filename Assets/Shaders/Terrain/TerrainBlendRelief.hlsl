#ifndef TOPDOWNRPG_TERRAIN_BLEND_RELIEF_INCLUDED
#define TOPDOWNRPG_TERRAIN_BLEND_RELIEF_INCLUDED

#include "Assets/Shaders/Include/PixelRelief.hlsl"
#include "Assets/Shaders/Terrain/TerrainBlendInputs.hlsl"
#include "Assets/Shaders/Terrain/TerrainBlendRuns.hlsl"

#define TB_RELIEF_LOCAL 3.0

// A bevel along a stone's edge (Docs/Lighting.md, Phase 11): the way it faces on the ground (a unit vector, x right and
// y up the screen), how many 0-255 steps a light from straight that way brightens it, and darkens it from the other
// way, how many more steps it darkens while it faces away from the light, and how far away it must face for that, from
// 0 (sideways) to 1 (straight away). All zero for no bevel.
struct TBBevel
{
    float2 facing;
    float strength;
    float shade;
    float from;
};

// What a stone's pixel shows of its relief: its stone's own bevel, and where the stone is cut along a rim the cut's.
struct TBRelief
{
    TBBevel own;
    TBBevel cut;
};

// How many 0-255 steps a light from toLight (a unit vector on the ground) changes a pixel with this bevel, as
// StoneCobblesRecipe paints it: by its strength as much as it faces the light, less its shade where it faces away by
// more than its from.
float TBBevelAmount(TBBevel bevel, float2 toLight)
{
    float lit = dot(bevel.facing, toLight);
    return bevel.strength * lit - (lit < -bevel.from ? bevel.shade : 0.0);
}

// How many 0-255 steps a light from toLight changes a pixel with this relief: its own bevel's and its cut's together.
float TBReliefAmount(TBRelief relief, float2 toLight)
{
    return TBBevelAmount(relief.own, toLight) + TBBevelAmount(relief.cut, toLight);
}

// Whether a pixel with this relief changes with the light at all.
bool TBHasRelief(TBRelief relief)
{
    return relief.own.strength > 0.0 || relief.own.shade > 0.0 || relief.cut.strength > 0.0;
}

// Whether layer m's material has relief: its stones' bevels turn to each light (TB_LAYER_RELIEF_COLUMN's bevel or
// radial above 0).
bool TBLayerHasRelief(TBInputs ins, uint m)
{
    float4 look = TBLut(ins, TB_LAYER_RELIEF_COLUMN, TBLayerRow(m));
    return look.x > 0.0 || look.z > 0.0;
}

// The own bevel of the stone layer m shows at a texel of its textures, with its cell field f there, for a material
// with relief (TBLayerHasRelief): which way it faces from the height texture's alpha (0 for none, the mortar's; else 1
// plus the direction from the stone's seed to the pixel in 255 steps of a turn anticlockwise from right, turned back
// into the world where the layer turns to its runs), its bevel strongest within the bevel width of the mortar (the
// field's alpha: the pixel's distance to its stone's cell edge, times 16) and its rounded look strongest at the radial
// radius from its stone's centre (the field's offset to it), from the material's settings in
// TB_LAYER_RELIEF_COLUMN (bevel, bevel width, radial, radial radius) and TB_LAYER_RELIEF_SHADE_COLUMN (shaded side,
// shaded side from, mortar width), as StoneCobblesRecipe paints the pixel lit from the top left.
TBBevel TBStoneBevel(TBInputs ins, uint m, int2 texel, float4 f, bool turned)
{
    TBBevel bevel = (TBBevel)0;
    float code = round(LOAD_TEXTURE2D_ARRAY(ins.height.tex, texel, TBSlice(ins, m)).a * 255.0);
    [branch]
    if (code > 0.5)
    {
        int row = TBLayerRow(m);
        float4 look = TBLut(ins, TB_LAYER_RELIEF_COLUMN, row);
        float4 shade = TBLut(ins, TB_LAYER_RELIEF_SHADE_COLUMN, row);
        float angle = (code - 1.0) * (6.28318530718 / 255.0);
        float border = f.a * 255.0 / 16.0;
        float bevelAmount = saturate(1.0 - (border - shade.z) / max(look.y, 0.001));
        float radialAmount = saturate(length(round(f.rg * 255.0) - 128.0) / max(look.w, 0.001));
        bevel.facing = TBUnturn(float2(cos(angle), sin(angle)), turned);
        bevel.strength = look.x * bevelAmount + look.z * radialAmount;
        bevel.shade = shade.x * bevelAmount;
        bevel.from = shade.y;
    }
    return bevel;
}

// The light the relief of an art pixel at the world position centre changes, to add to its lit colour (Docs/Lighting.md,
// Phase 11): colour is the pixel's colour with its bevels lit from the top left (TB_LIGHT), as its material's textures
// paint them, unlit the same in the target's space, and weight how much of colour the relief's layer makes. The sky
// keeps the painted light. The sun lights the bevels from its own side instead where no sun shadow covers the art
// pixel (shadowed). The local lights (PixelLocalLight) light them from where they come (PixelLocalLightDirection): not
// at all from straight above, TB_RELIEF_LOCAL times as strongly as they shine from the side. None while relief is off
// (_LightRelief.w) or without USE_SHAPE_LIGHT_TYPE_0.
float3 TBReliefLight(TBRelief relief, float weight, float3 colour, float3 unlit, float2 centre, half shadowed)
{
    float3 added = float3(0.0, 0.0, 0.0);
#if USE_SHAPE_LIGHT_TYPE_0
    [branch]
    if (_LightRelief.w > 0.5)
    {
        float3 flat = colour - weight * TBReliefAmount(relief, TB_LIGHT) / 255.0;
        [branch]
        if (_SunShadow.z > 0.5)
        {
            float3 sunlit = TBToOutput(saturate(flat + weight * TBReliefAmount(relief, -normalize(_SunShadow.xy)) / 255.0));
            added += (sunlit - unlit) * _SunColour.rgb * (1.0 - shadowed);
        }

        half3 local = PixelLocalLight(centre);
        half here = PixelBrightest(local);
        [branch]
        if (PixelLocalLightReaches(here))
        {
            float3 light = PixelLocalLightDirection(centre, here);
            float side = length(light.xy);
            float3 bare = TBToOutput(saturate(flat));
            added += (bare - unlit) * local;
            [branch]
            if (side > 0.0)
            {
                float3 turned = TBToOutput(saturate(flat + weight * TBReliefAmount(relief, light.xy / side) / 255.0));
                added += (turned - bare) * local * saturate(side / max(light.z, 0.05)) * TB_RELIEF_LOCAL;
            }
        }

        added *= _HDREmulationScale * _ShapeLightBlendFactors0.x;
    }
#endif
    return added;
}

#endif
