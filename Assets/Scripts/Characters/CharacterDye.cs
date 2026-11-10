/// <summary>
/// A ramp of a character a player can colour (Docs/PlayerCharacter.md, Phase 5): a row of a player's palette, named by
/// the baked palette maps, so the values are fixed. <see cref="None"/> keeps the colour the sheet was baked in (a
/// weapon, its smears, a cast's light, the inside of the mouth).
/// </summary>
public enum CharacterDye
{
    None = 0,
    Skin = 1,
    Mouth = 2,
    Blush = 3,
    Hair = 4,
    Brows = 5,
    Eyes = 6,
    EyeGlint = 7,
    Tunic = 8,
    Shirt = 9,
    Trousers = 10,
    Boots = 11,
    Belt = 12,
    Buckle = 13,
    Gloves = 14,
    Strap = 15,
    Pouch = 16,
    Headwear = 17,
    Cape = 18,
    CapeLining = 19
}
