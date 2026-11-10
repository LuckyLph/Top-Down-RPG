#ifndef TOPDOWNRPG_CHARACTER_PALETTE_INCLUDED
#define TOPDOWNRPG_CHARACTER_PALETTE_INCLUDED

#define CHARACTER_PALETTE_TONES 4.0
#define CHARACTER_PALETTE_ROWS 32.0

// A player's colours (Docs/PlayerCharacter.md, Phase 5): a sprite's palette map, its secondary texture, holds per texel
// one byte, its dye times four plus its tone (0 where the sprite keeps its own colour; black, a sprite without one),
// and the palette, the material's, a row of four tones per dye (CharacterPaletteMap), opaque where it colours that dye
// and clear elsewhere (clear throughout without one).
TEXTURE2D(_PaletteMap);
SAMPLER(sampler_PaletteMap);
TEXTURE2D(_PaletteTex);
SAMPLER(palette_point_clamp_sampler);

// The colour of the sprite's texel at `uv` before it is lit: the palette's colour of the dye and tone its palette map
// names there, tinted by `tint` as the sprite is, or `colour`, the sprite's own, where it names none or the palette
// does not colour that dye.
half3 CharacterPaletteColour(half3 colour, half3 tint, float2 uv)
{
    uint code = (uint)round(SAMPLE_TEXTURE2D(_PaletteMap, sampler_PaletteMap, uv).r * 255.0);
    half3 shown = colour;
    [branch]
    if (code >= 4u)
    {
        float2 entry = (float2(code & 3u, code >> 2) + 0.5) / float2(CHARACTER_PALETTE_TONES, CHARACTER_PALETTE_ROWS);
        half4 dyed = SAMPLE_TEXTURE2D_LOD(_PaletteTex, palette_point_clamp_sampler, entry, 0);
        shown = dyed.a > 0.5 ? tint * dyed.rgb : colour;
    }

    return shown;
}

#endif
