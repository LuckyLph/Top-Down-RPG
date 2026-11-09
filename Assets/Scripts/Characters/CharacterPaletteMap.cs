/// <summary>
/// How a baked sheet names the colour of each pixel a player can colour (Docs/PlayerCharacter.md, Phase 5): its palette
/// map, the sheet's secondary texture <see cref="MapTextureName"/>, holds per pixel one byte, the pixel's
/// <see cref="CharacterDye"/> times four plus its tone (0 the outline, 1 the shadow, 2 the middle, 3 the light), and 0
/// where the sheet's own colour stays. The lit sprite shader reads that byte's colour in a player's palette,
/// <see cref="PaletteTextureName"/>: <see cref="ColourRamp.ToneCount"/> pixels a row, a row per dye, so a pixel's byte is
/// also its index in the palette's pixels.
/// </summary>
public static class CharacterPaletteMap
{
    public const string MapTextureName = "_PaletteMap";
    public const string PaletteTextureName = "_PaletteTex";
    public const int Rows = 32;
    public const int PaletteSize = Rows * ColourRamp.ToneCount;

    /// <summary>
    /// The byte a palette map holds for a pixel of <paramref name="dye"/> in <paramref name="tone"/>.
    /// </summary>
    public static byte Code(CharacterDye dye, int tone)
    {
        return (byte)((int)dye * ColourRamp.ToneCount + tone);
    }

    /// <summary>
    /// The dye a palette map's <paramref name="code"/> names, <see cref="CharacterDye.None"/> for the sheet's own colour.
    /// </summary>
    public static CharacterDye DyeOf(byte code)
    {
        return (CharacterDye)(code / ColourRamp.ToneCount);
    }

    /// <summary>
    /// The tone a palette map's <paramref name="code"/> names.
    /// </summary>
    public static int ToneOf(byte code)
    {
        return code % ColourRamp.ToneCount;
    }
}
