using System;
using UnityEngine;

/// <summary>
/// A hairstyle of the adventurer (Docs/PlayerCharacter.md, Phase 6): the face's region on the hair (its hairline), the
/// fringe hanging over the forehead, and the locks standing off the head.
/// </summary>
[Serializable]
public sealed class AdventurerHair
{
    [SerializeField] private string name = "Hair";
    [Tooltip("The face, in degrees from its middle: half its width, its middle's height and half its height. Hair covers the rest of the head.")]
    [SerializeField] private Vector3 face = new(72f, -16f, 44f);
    [Tooltip("The fringe's lowest edge in degrees, how far its points drop below it, the points' spacing in degrees, and how much it rises per degree from the middle.")]
    [SerializeField] private Vector4 fringe = new(10f, 7f, 18f, 0.1f);
    [Tooltip("The locks standing off the head, drawn over the face. Headwear hides the locks whose roots it covers.")]
    [SerializeField] private HairLock[] locks = new HairLock[0];

    /// <summary>
    /// Creates a hairstyle, for Unity serialization and the inspector.
    /// </summary>
    public AdventurerHair()
    {
    }

    /// <summary>
    /// Creates a hairstyle, for tools and tests.
    /// </summary>
    public AdventurerHair(string styleName, Vector3 hairline, Vector4 hairFringe, HairLock[] hairLocks)
    {
        name = styleName;
        face = hairline;
        fringe = hairFringe;
        locks = hairLocks;
    }

    public string Name => name;
    public Vector3 Face => face;
    public Vector4 Fringe => fringe;
    public HairLock[] Locks => locks ?? new HairLock[0];
}

/// <summary>
/// What the adventurer wears on its head (Docs/PlayerCharacter.md, Phase 6): nothing, a hood whose opening shows the
/// face, or a cap with a brim, with their sizes.
/// </summary>
[Serializable]
public sealed class AdventurerHeadwearPart
{
    [SerializeField] private string name = "None";
    [SerializeField] private AdventurerHeadwear kind;
    [Tooltip("The hood: its radius in hair radii, and its point behind the head, back and up, in head radii.")]
    [SerializeField] private Vector3 hood = new(1.18f, 1.2f, 0.45f);
    [Tooltip("The hood's opening, in degrees from the face's middle: half its width, its middle's height and half its height.")]
    [SerializeField] private Vector3 hoodOpening = new(62f, -10f, 48f);
    [Tooltip("The cap: its crown's radius in hair radii, its rim's height in degrees up from the face's middle, and its brim's reach forward in hair radii.")]
    [SerializeField] private Vector3 cap = new(1.1f, 20f, 0.6f);

    /// <summary>
    /// Creates headwear, for Unity serialization and the inspector.
    /// </summary>
    public AdventurerHeadwearPart()
    {
    }

    /// <summary>
    /// Creates headwear of <paramref name="headwearKind"/> at the default sizes, for tools and tests.
    /// </summary>
    public AdventurerHeadwearPart(string partName, AdventurerHeadwear headwearKind)
    {
        name = partName;
        kind = headwearKind;
    }

    public string Name => name;
    public AdventurerHeadwear Kind => kind;
    public Vector3 Hood => hood;
    public Vector3 HoodOpening => hoodOpening;
    public Vector3 Cap => cap;
}

/// <summary>
/// What the adventurer wears on its body (Docs/PlayerCharacter.md, Phase 6): a tunic over the shirt with its skirt, or
/// the shirt alone, and how the arms are dressed.
/// </summary>
[Serializable]
public sealed class AdventurerTop
{
    [SerializeField] private string name = "Tunic";
    [Tooltip("Whether a tunic covers the shirt; without one the shirt shows from the collar down to the belt, its sleeves whole.")]
    [SerializeField] private bool tunic = true;
    [Tooltip("How the arms are dressed.")]
    [SerializeField] private AdventurerSleeves sleeves = AdventurerSleeves.Long;
    [Tooltip("How far the tunic's skirt hangs below the hips, in rig units; none at 0.")]
    [SerializeField, Min(0f)] private float skirtLength = 1.4f;

    /// <summary>
    /// Creates a top, for Unity serialization and the inspector.
    /// </summary>
    public AdventurerTop()
    {
    }

    /// <summary>
    /// Creates a top, for tools and tests.
    /// </summary>
    public AdventurerTop(string partName, bool withTunic, AdventurerSleeves arms, float skirt)
    {
        name = partName;
        tunic = withTunic;
        sleeves = arms;
        skirtLength = skirt;
    }

    public string Name => name;
    public bool Tunic => tunic;
    public AdventurerSleeves Sleeves => sleeves;
    public float SkirtLength => skirtLength;
}

/// <summary>
/// The adventurer's cape (Docs/PlayerCharacter.md, Phase 6), or none: a mantle over the shoulders and strips hanging
/// to a hem that streams back with the pose's flow, lined or not.
/// </summary>
[Serializable]
public sealed class AdventurerCape
{
    [SerializeField] private string name = "None";
    [Tooltip("Whether a cape hangs from the shoulders.")]
    [SerializeField] private bool cape;
    [Tooltip("How many strips the cape hangs in.")]
    [SerializeField, Range(2, 9)] private int strips = 5;
    [Tooltip("The cape's hem while it hangs, as a share of the pelvis's height over the ankles.")]
    [SerializeField, Range(0f, 1f)] private float hem = 0.45f;
    [Tooltip("Whether the cape is lined, the lining showing at the hem and wherever its inside faces the camera.")]
    [SerializeField] private bool lining = true;

    /// <summary>
    /// Creates a cape, for Unity serialization and the inspector.
    /// </summary>
    public AdventurerCape()
    {
    }

    /// <summary>
    /// Creates a cape, for tools and tests.
    /// </summary>
    public AdventurerCape(string partName, bool hangs, float capeHem, bool lined)
    {
        name = partName;
        cape = hangs;
        hem = capeHem;
        lining = lined;
    }

    public string Name => name;
    public bool Cape => cape;
    public int Strips => strips;
    public float Hem => hem;
    public bool Lining => lining;
}

/// <summary>
/// What the adventurer wears on its legs (Docs/PlayerCharacter.md, Phase 6): trousers, or shorts baring the shins.
/// </summary>
[Serializable]
public sealed class AdventurerLegs
{
    [SerializeField] private string name = "Trousers";
    [Tooltip("Whether the trousers stop at the knee, the shins bare above the boots.")]
    [SerializeField] private bool shorts;

    /// <summary>
    /// Creates legs, for Unity serialization and the inspector.
    /// </summary>
    public AdventurerLegs()
    {
    }

    /// <summary>
    /// Creates legs, for tools and tests.
    /// </summary>
    public AdventurerLegs(string partName, bool shortTrousers)
    {
        name = partName;
        shorts = shortTrousers;
    }

    public string Name => name;
    public bool Shorts => shorts;
}

/// <summary>
/// The adventurer's boots (Docs/PlayerCharacter.md, Phase 6): their size and the height of their tops.
/// </summary>
[Serializable]
public sealed class AdventurerBoots
{
    [SerializeField] private string name = "Tall";
    [Tooltip("Scales the boots.")]
    [SerializeField, Min(0.1f)] private float size = 1.1f;
    [Tooltip("Where the boots' tops sit, from the knee (0) to the ankle (1).")]
    [SerializeField, Range(0f, 1f)] private float top = 0.35f;

    /// <summary>
    /// Creates boots, for Unity serialization and the inspector.
    /// </summary>
    public AdventurerBoots()
    {
    }

    /// <summary>
    /// Creates boots, for tools and tests.
    /// </summary>
    public AdventurerBoots(string partName, float bootSize, float bootTop)
    {
        name = partName;
        size = bootSize;
        top = bootTop;
    }

    public string Name => name;
    public float Size => size;
    public float Top => top;
}

/// <summary>
/// The adventurer's hands (Docs/PlayerCharacter.md, Phase 7): bare, or in gloves.
/// </summary>
[Serializable]
public sealed class AdventurerHands
{
    [SerializeField] private string name = "Bare";
    [Tooltip("Whether the hands wear gloves.")]
    [SerializeField] private bool gloves;

    /// <summary>
    /// Creates hands, for Unity serialization and the inspector.
    /// </summary>
    public AdventurerHands()
    {
    }

    /// <summary>
    /// Creates hands, for tools and tests.
    /// </summary>
    public AdventurerHands(string partName, bool gloved)
    {
        name = partName;
        gloves = gloved;
    }

    public string Name => name;
    public bool Gloves => gloves;
}

/// <summary>
/// The adventurer's bag (Docs/PlayerCharacter.md, Phase 7), or none: a satchel's strap across the chest from the left
/// shoulder to a pouch hanging behind the right hip, over the skirt.
/// </summary>
[Serializable]
public sealed class AdventurerBag
{
    [SerializeField] private string name = "None";
    [Tooltip("Whether a satchel hangs from a strap across the chest.")]
    [SerializeField] private bool satchel;
    [Tooltip("The pouch's place from the pelvis: along the hips toward the right, forward, and up the spine, in rig units.")]
    [SerializeField] private Vector3 pouch = new(4.4f, -1.6f, -0.6f);
    [Tooltip("The pouch's half sizes: across, deep and tall, in rig units.")]
    [SerializeField] private Vector3 pouchSize = new(1.5f, 2f, 1.9f);

    /// <summary>
    /// Creates a bag, for Unity serialization and the inspector.
    /// </summary>
    public AdventurerBag()
    {
    }

    /// <summary>
    /// Creates a bag, for tools and tests.
    /// </summary>
    public AdventurerBag(string partName, bool hasSatchel)
    {
        name = partName;
        satchel = hasSatchel;
    }

    public string Name => name;
    public bool Satchel => satchel;
    public Vector3 Pouch => pouch;
    public Vector3 PouchSize => pouchSize;
}

/// <summary>
/// Where the adventurer carries a weapon outside its attacks (Docs/PlayerCharacter.md, Phase 7): in a scabbard at a
/// hip, or slung on the back.
/// </summary>
public enum AdventurerCarryPlace
{
    Hip,
    Back
}

/// <summary>
/// The gear of a weapon the adventurer can equip (Docs/PlayerCharacter.md, Phase 7), or none: the weapon stowed where it
/// is carried, drawn in the clips without a weapon, and what stays on the body while the weapon is in hand (a blade's
/// scabbard, a sling's strap, a quiver), drawn in the weapon's own clips too.
/// </summary>
[Serializable]
public sealed class AdventurerCarry
{
    [SerializeField] private string name = "None";
    [Tooltip("The weapon carried; none draws nothing.")]
    [SerializeField] private CharacterWeaponArt weapon;
    [SerializeField] private AdventurerCarryPlace place;
    [Tooltip("Hip: which side, -1 left or 1 right. Back: the side the hilt or the bow's upper tip rises over, -1 left or 1 right.")]
    [SerializeField, Range(-1, 1)] private int side = -1;
    [Tooltip("Hip: the scabbard's mouth from the pelvis, along the hips toward the side, forward and up the spine. Back: the middle of the weapon from the neck, toward the side, behind the back's surface and down the spine. In rig units.")]
    [SerializeField] private Vector3 offset = new(3.6f, 1.2f, 1f);
    [Tooltip("Hip: degrees the scabbard tips back from hanging plumb, and out from the hip. Back: degrees the weapon leans from the spine toward the side.")]
    [SerializeField] private Vector2 angles = new(55f, 12f);
    [Tooltip("A blade: sheathed in a scabbard, its hilt showing. Otherwise it is bare.")]
    [SerializeField] private bool scabbard = true;
    [Tooltip("A strap across the chest from the shoulder on the side to the other hip, holding a weapon on the back.")]
    [SerializeField] private bool sling;
    [Tooltip("A bow: a quiver of arrows on the back, its mouth over the shoulder on the other side.")]
    [SerializeField] private bool quiver;
    [Tooltip("The quiver's middle from the neck, toward the other side, behind the back's surface and down the spine, and how far it leans from the spine, in rig units and degrees.")]
    [SerializeField] private Vector4 quiverPlace = new(1.6f, 1.6f, 4.6f, 22f);
    [Tooltip("How much further out the gear on the back sits over a cape, in rig units.")]
    [SerializeField, Min(0f)] private float capeDepth = 2.5f;

    /// <summary>
    /// Creates carried gear, for Unity serialization and the inspector.
    /// </summary>
    public AdventurerCarry()
    {
    }

    public string Name => name;
    public CharacterWeaponArt Weapon => weapon;
    public AdventurerCarryPlace Place => place;
    public int Side => side < 0 ? -1 : 1;
    public Vector3 Offset => offset;
    public Vector2 Angles => angles;
    public bool Scabbard => scabbard;
    public bool Sling => sling;
    public bool Quiver => quiver;
    public Vector4 QuiverPlace => quiverPlace;
    public float CapeDepth => capeDepth;
}

/// <summary>
/// Which layer of a character a painter draws (Docs/PlayerCharacter.md, Phase 6): its body (<see cref="Base"/>,
/// everything but its parts), or one option of a part slot, for a slot whose part depends on another's (the hair,
/// under each headwear) drawn under that slot's option <see cref="Variant"/>.
/// </summary>
public readonly struct CharacterLayerKey
{
    /// <summary>
    /// The body's layer.
    /// </summary>
    public static readonly CharacterLayerKey Base = new(-1, 0, 0);

    /// <summary>
    /// Creates the key of option <paramref name="option"/> of slot <paramref name="slot"/> (-1 for the body), under
    /// <paramref name="variant"/>.
    /// </summary>
    public CharacterLayerKey(int slot, int option, int variant)
    {
        Slot = slot;
        Option = option;
        Variant = variant;
    }

    public int Slot { get; }
    public int Option { get; }
    public int Variant { get; }
    public bool IsBase => Slot < 0;

    /// <summary>
    /// Whether this is a layer of <paramref name="slot"/>.
    /// </summary>
    public bool Is(CharacterSlot slot)
    {
        return Slot == (int)slot;
    }
}
