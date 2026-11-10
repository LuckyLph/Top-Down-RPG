/// <summary>
/// Where a part of a character goes (Docs/PlayerCharacter.md, Phases 6 and 7), in the order a player's picks name them on
/// the wire and the composer lays their layers over the body. The weapon's is not the player's pick: every machine
/// shows the gear of the weapon the player has equipped.
/// </summary>
public enum CharacterSlot
{
    Hair,
    Headwear,
    Top,
    Cape,
    Legs,
    Boots,
    Hands,
    Bag,
    Weapon
}
