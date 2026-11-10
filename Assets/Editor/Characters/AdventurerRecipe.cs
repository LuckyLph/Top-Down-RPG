using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// How an adventurer's arms are dressed: the shirt's long sleeves, the tunic's short sleeves over bare forearms, or
/// the tunic's short sleeves over the shirt's long ones.
/// </summary>
public enum AdventurerSleeves
{
    Long,
    Short,
    Layered
}

/// <summary>
/// What an adventurer wears on its head: nothing, a hood whose opening shows the face, or a cap with a brim.
/// </summary>
public enum AdventurerHeadwear
{
    None,
    Hood,
    Cap
}

/// <summary>
/// The adventurer's <see cref="CharacterRecipe"/>, the player's base body: a bare head with its ears and the face laid
/// out on it by angle, its belt, its parts (Docs/PlayerCharacter.md, Phase 6: the hairstyles, headwear, tops, capes,
/// legs and boots it can wear; Phase 7: its hands, bare or gloved, a bag, and the gear of each weapon it can equip; the
/// first of each its own), and its palette, drawn by an
/// <see cref="AdventurerPainter"/>, a layer at a time: the body, then each part over it. Every material but the inside of the mouth takes a player's colours
/// (<see cref="DyeOf"/>, Docs/PlayerCharacter.md, Phase 5), its ramps here being the ones the sheets are baked in.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/Characters/Adventurer Recipe", fileName = "Recipe_Adventurer")]
public sealed class AdventurerRecipe : CharacterRecipe
{
    [Header("Parts (Docs/PlayerCharacter.md, Phases 6 and 7), the first of each the look's own")]
    [Tooltip("The hairstyles.")]
    [SerializeField] private List<AdventurerHair> hairOptions = new() { new AdventurerHair() };
    [Tooltip("What the head wears; it hides the locks whose roots it covers.")]
    [SerializeField] private List<AdventurerHeadwearPart> headwearOptions = new() { new AdventurerHeadwearPart() };
    [Tooltip("What the body wears.")]
    [SerializeField] private List<AdventurerTop> topOptions = new() { new AdventurerTop() };
    [Tooltip("The capes, or none.")]
    [SerializeField] private List<AdventurerCape> capeOptions = new() { new AdventurerCape() };
    [Tooltip("What the legs wear.")]
    [SerializeField] private List<AdventurerLegs> legOptions = new() { new AdventurerLegs() };
    [Tooltip("The boots.")]
    [SerializeField] private List<AdventurerBoots> bootOptions = new() { new AdventurerBoots() };
    [Tooltip("The hands, bare or gloved (Docs/PlayerCharacter.md, Phase 7).")]
    [SerializeField] private List<AdventurerHands> handOptions = new() { new AdventurerHands() };
    [Tooltip("The bags, or none (Docs/PlayerCharacter.md, Phase 7).")]
    [SerializeField] private List<AdventurerBag> bagOptions = new() { new AdventurerBag() };
    [Tooltip("The gear of each weapon the player can equip, the first the one it starts with (Docs/PlayerCharacter.md, Phase 7). Not a player's pick: the equipped weapon's shows.")]
    [SerializeField] private List<AdventurerCarry> weaponOptions = new() { new AdventurerCarry() };

    [Header("Head and face")]
    [Tooltip("The head's radius with its hair, in head radii.")]
    [SerializeField, Min(1f)] private float hairSize = 1.1f;
    [Tooltip("How far the head tips toward the camera, in degrees, so the face is not foreshortened by the view.")]
    [SerializeField, Range(0f, 45f)] private float faceTilt = 20f;
    [Tooltip("The ears, in degrees from the face's middle (across and up), and their radius in head radii; none at 0.")]
    [SerializeField] private Vector3 ears = new(88f, -12f, 0f);
    [Tooltip("The eyes' place: degrees each side of the middle, and height in degrees.")]
    [SerializeField] private Vector2 eyes = new(17f, -8f);
    [Tooltip("How many pixels tall each eye is.")]
    [SerializeField, Range(1, 3)] private int eyeHeight = 2;
    [Tooltip("How many pixels wide each eye is, growing toward the face's middle.")]
    [SerializeField, Range(1, 2)] private int eyeWidth = 1;
    [Tooltip("Whether the top pixel of each eye nearest the light catches it.")]
    [SerializeField] private bool eyeGlint;
    [Tooltip("How many pixels above each eye its brow sits; none at 0.")]
    [SerializeField, Range(0, 3)] private int browGap;
    [Tooltip("How many pixels wide each brow is, from above the eye's inner edge outward.")]
    [SerializeField, Range(1, 4)] private int browWidth = 3;
    [Tooltip("How many pixels each brow's outer end sits above (positive) or below its inner end.")]
    [SerializeField, Range(-1, 1)] private int browTilt;
    [Tooltip("The mouth's height in degrees from the face's middle, and its width in pixels; none at 0 wide.")]
    [SerializeField] private Vector2 mouth = new(-30f, 0f);
    [Tooltip("How the mouth curves: its corners a pixel up (1, a smile), level (0) or down (-1).")]
    [SerializeField, Range(-1, 1)] private int mouthCurve;
    [Tooltip("The cheeks' blush, in degrees each side of the middle and in height; none at 0 across.")]
    [SerializeField] private Vector2 blush = new(0f, -20f);

    [Header("Colours")]
    [SerializeField] private ColourRamp skin = Colours(92, 52, 40, 200, 136, 106, 236, 182, 146, 250, 214, 184);
    [SerializeField] private ColourRamp eye = Colours(30, 22, 34, 30, 22, 34, 30, 22, 34, 30, 22, 34);
    [SerializeField] private ColourRamp eyeGlintColour = Colours(250, 248, 240, 250, 248, 240, 250, 248, 240, 250, 248, 240);
    [SerializeField] private ColourRamp brow = Colours(70, 40, 22, 70, 40, 22, 70, 40, 22, 70, 40, 22);
    [SerializeField] private ColourRamp mouthColour = Colours(170, 96, 80, 170, 96, 80, 170, 96, 80, 170, 96, 80);
    [Tooltip("The inside of an open mouth.")]
    [SerializeField] private ColourRamp mouthInside = Colours(58, 32, 18, 58, 32, 18, 58, 32, 18, 58, 32, 18);
    [SerializeField] private ColourRamp blushColour = Colours(238, 150, 130, 238, 150, 130, 238, 150, 130, 238, 150, 130);
    [SerializeField] private ColourRamp hair = Colours(42, 24, 14, 96, 56, 30, 140, 84, 44, 184, 124, 70);
    [SerializeField] private ColourRamp tunic = Colours(24, 30, 46, 70, 96, 140, 96, 128, 176, 140, 172, 214);
    [SerializeField] private ColourRamp shirt = Colours(36, 32, 30, 150, 140, 122, 196, 186, 164, 232, 224, 204);
    [SerializeField] private ColourRamp trousers = Colours(28, 24, 20, 80, 70, 58, 110, 98, 82, 146, 132, 112);
    [SerializeField] private ColourRamp boots = Colours(22, 14, 10, 70, 46, 32, 96, 64, 44, 130, 90, 62);
    [SerializeField] private ColourRamp belt = Colours(26, 16, 10, 82, 52, 30, 112, 74, 44, 150, 104, 64);
    [SerializeField] private ColourRamp fittings = Colours(60, 44, 14, 150, 116, 40, 206, 168, 70, 244, 220, 140);
    [SerializeField] private ColourRamp glovesColour = Colours(22, 14, 10, 70, 46, 32, 96, 64, 44, 130, 90, 62);
    [Tooltip("The satchel's strap.")]
    [SerializeField] private ColourRamp strapColour = Colours(26, 16, 10, 82, 52, 30, 112, 74, 44, 150, 104, 64);
    [Tooltip("The satchel's pouch.")]
    [SerializeField] private ColourRamp pouch = Colours(40, 26, 14, 132, 90, 52, 170, 120, 72, 206, 160, 104);
    [Tooltip("The hood or the cap.")]
    [SerializeField] private ColourRamp headwearColour = Colours(18, 36, 22, 52, 98, 58, 74, 132, 76, 116, 170, 104);
    [Tooltip("The inside of the hood, in its shadow.")]
    [SerializeField] private ColourRamp hoodInside = Colours(24, 18, 16, 24, 18, 16, 24, 18, 16, 24, 18, 16);
    [Tooltip("The cape.")]
    [SerializeField] private ColourRamp cloak = Colours(46, 16, 18, 128, 38, 40, 170, 56, 52, 214, 96, 80);
    [Tooltip("The cape's lining.")]
    [SerializeField] private ColourRamp lining = Colours(36, 32, 30, 150, 140, 122, 196, 186, 164, 232, 224, 204);

    public IReadOnlyList<AdventurerHair> Hairs => hairOptions;
    public IReadOnlyList<AdventurerHeadwearPart> Headwear => headwearOptions;
    public IReadOnlyList<AdventurerTop> Tops => topOptions;
    public IReadOnlyList<AdventurerCape> Capes => capeOptions;
    public IReadOnlyList<AdventurerLegs> Legs => legOptions;
    public IReadOnlyList<AdventurerBoots> Boots => bootOptions;
    public IReadOnlyList<AdventurerHands> Hands => handOptions;
    public IReadOnlyList<AdventurerBag> Bags => bagOptions;
    public IReadOnlyList<AdventurerCarry> Weapons => weaponOptions;
    public float HairSize => hairSize;
    public float FaceTilt => faceTilt;
    public Vector3 Ears => ears;
    public Vector2 Eyes => eyes;
    public int EyeHeight => eyeHeight;
    public int EyeWidth => eyeWidth;
    public bool EyeGlint => eyeGlint;
    public int BrowGap => browGap;
    public int BrowWidth => browWidth;
    public int BrowTilt => browTilt;
    public Vector2 Mouth => mouth;
    public int MouthCurve => mouthCurve;
    public Vector2 Blush => blush;

    /// <inheritdoc />
    public override ColourRamp Ramp(CharacterMaterial material)
    {
        switch (material)
        {
            case CharacterMaterial.Skin:
                return skin;
            case CharacterMaterial.Eye:
                return eye;
            case CharacterMaterial.EyeGlint:
                return eyeGlintColour;
            case CharacterMaterial.Brow:
                return brow;
            case CharacterMaterial.Mouth:
                return mouthColour;
            case CharacterMaterial.MouthInside:
                return mouthInside;
            case CharacterMaterial.Blush:
                return blushColour;
            case CharacterMaterial.Hair:
                return hair;
            case CharacterMaterial.Jerkin:
                return tunic;
            case CharacterMaterial.Shirt:
                return shirt;
            case CharacterMaterial.Trousers:
                return trousers;
            case CharacterMaterial.Boots:
                return boots;
            case CharacterMaterial.Belt:
                return belt;
            case CharacterMaterial.Gloves:
                return glovesColour;
            case CharacterMaterial.Strap:
                return strapColour;
            case CharacterMaterial.Pouch:
                return pouch;
            case CharacterMaterial.Headwear:
                return headwearColour;
            case CharacterMaterial.HoodInside:
                return hoodInside;
            case CharacterMaterial.Cloak:
                return cloak;
            case CharacterMaterial.Lining:
                return lining;
            default:
                return fittings;
        }
    }

    /// <inheritdoc />
    public override CharacterDye DyeOf(CharacterMaterial material)
    {
        return material switch
        {
            CharacterMaterial.Skin => CharacterDye.Skin,
            CharacterMaterial.Mouth => CharacterDye.Mouth,
            CharacterMaterial.Blush => CharacterDye.Blush,
            CharacterMaterial.Hair => CharacterDye.Hair,
            CharacterMaterial.Brow => CharacterDye.Brows,
            CharacterMaterial.Eye => CharacterDye.Eyes,
            CharacterMaterial.EyeGlint => CharacterDye.EyeGlint,
            CharacterMaterial.Jerkin => CharacterDye.Tunic,
            CharacterMaterial.Shirt => CharacterDye.Shirt,
            CharacterMaterial.Trousers => CharacterDye.Trousers,
            CharacterMaterial.Boots => CharacterDye.Boots,
            CharacterMaterial.Belt => CharacterDye.Belt,
            CharacterMaterial.Fittings => CharacterDye.Buckle,
            CharacterMaterial.Gloves => CharacterDye.Gloves,
            CharacterMaterial.Strap => CharacterDye.Strap,
            CharacterMaterial.Pouch => CharacterDye.Pouch,
            CharacterMaterial.Headwear => CharacterDye.Headwear,
            CharacterMaterial.Cloak => CharacterDye.Cape,
            CharacterMaterial.Lining => CharacterDye.CapeLining,
            _ => CharacterDye.None
        };
    }

    /// <inheritdoc />
    public override int PartOptions(CharacterSlot slot)
    {
        return slot switch
        {
            CharacterSlot.Hair => hairOptions.Count,
            CharacterSlot.Headwear => headwearOptions.Count,
            CharacterSlot.Top => topOptions.Count,
            CharacterSlot.Cape => capeOptions.Count,
            CharacterSlot.Legs => legOptions.Count,
            CharacterSlot.Boots => bootOptions.Count,
            CharacterSlot.Hands => handOptions.Count,
            CharacterSlot.Bag => bagOptions.Count,
            CharacterSlot.Weapon => weaponOptions.Count,
            _ => 0
        };
    }

    /// <inheritdoc />
    public override string PartName(CharacterSlot slot, int option)
    {
        return slot switch
        {
            CharacterSlot.Hair => hairOptions[option].Name,
            CharacterSlot.Headwear => headwearOptions[option].Name,
            CharacterSlot.Top => topOptions[option].Name,
            CharacterSlot.Cape => capeOptions[option].Name,
            CharacterSlot.Legs => legOptions[option].Name,
            CharacterSlot.Boots => bootOptions[option].Name,
            CharacterSlot.Hands => handOptions[option].Name,
            CharacterSlot.Bag => bagOptions[option].Name,
            CharacterSlot.Weapon => weaponOptions[option].Name,
            _ => null
        };
    }

    /// <inheritdoc />
    public override int PartVariesWith(CharacterSlot slot)
    {
        return slot switch
        {
            CharacterSlot.Hair => (int)CharacterSlot.Headwear,
            CharacterSlot.Weapon => (int)CharacterSlot.Cape,
            _ => -1
        };
    }

    /// <inheritdoc />
    public override CharacterWeaponArt PartWeapon(CharacterSlot slot, int option)
    {
        return slot == CharacterSlot.Weapon && option >= 0 && option < weaponOptions.Count ? weaponOptions[option].Weapon : null;
    }

    /// <inheritdoc />
    public override CharacterPainter CreatePainter(CharacterWeaponArt weapon, CharacterCanvas canvas)
    {
        return new AdventurerPainter(this, weapon, canvas, CharacterLayerKey.Base);
    }

    /// <inheritdoc />
    public override CharacterPainter CreatePainter(CharacterWeaponArt weapon, CharacterCanvas canvas, CharacterLayerKey layer)
    {
        return new AdventurerPainter(this, weapon, canvas, layer);
    }
}
