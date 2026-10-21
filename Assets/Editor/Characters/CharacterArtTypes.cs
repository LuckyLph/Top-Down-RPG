using System;
using UnityEngine;

/// <summary>
/// What a pixel of a character frame is made of. Each material is drawn in its own <see cref="ColourRamp"/>: the
/// character's from its <see cref="CharacterRecipe"/>, the weapon's and the effects' from its
/// <see cref="CharacterWeaponArt"/>. <see cref="CharacterMaterials"/> says which are flat and which draw no outline.
/// </summary>
public enum CharacterMaterial
{
    Cloak,
    HoodInside,
    Face,
    FaceShade,
    Eye,
    Bangs,
    Mask,
    Jerkin,
    Shirt,
    Trousers,
    Boots,
    Bracers,
    Gloves,
    Belt,
    Fittings,
    Sash,
    Lining,
    DaggerGrip,
    Steel,
    Guard,
    Grip,
    Wood,
    BowString,
    Shaft,
    Fletching,
    SmearNew,
    SmearMiddle,
    SmearOld,
    Dust
}

/// <summary>
/// The part of the character a pixel belongs to. A pixel beside another part that lies nearer by more than the inner
/// line gap is drawn in its material's outline colour; pixels of one part never line each other.
/// </summary>
public enum CharacterPart
{
    Torso,
    Head,
    LeftThigh,
    LeftShin,
    LeftFoot,
    RightThigh,
    RightShin,
    RightFoot,
    LeftUpperArm,
    LeftForearm,
    LeftHand,
    RightUpperArm,
    RightForearm,
    RightHand,
    Belt,
    Strap,
    Mantle,
    Cape,
    Weapon,
    Bow,
    Arrow,
    Smear,
    Effect
}

/// <summary>
/// How a keyed motion moves from one key to the next: smoothly in and out, fast out of the key, slowly out of it,
/// evenly, or holding the key until the next one's time.
/// </summary>
public enum MotionEasing
{
    InOut,
    Out,
    In,
    Linear,
    Step
}

/// <summary>
/// A weapon's shape: a blade held in the right hand (or both), or a bow held in the left.
/// </summary>
public enum WeaponKind
{
    Blade,
    Bow
}

/// <summary>
/// An effect drawn on a frame of a keyed motion: dust where the blade's tip meets the ground, or the flash and streak
/// of an arrow leaving the bow.
/// </summary>
public enum MotionEffectKind
{
    Dust,
    Release
}

/// <summary>
/// The four colours a material is drawn in, darkest first: its outline, then its shadow, middle and light tones.
/// </summary>
[Serializable]
public struct ColourRamp
{
    [SerializeField] private Color32 outline;
    [SerializeField] private Color32 shadow;
    [SerializeField] private Color32 middle;
    [SerializeField] private Color32 light;

    /// <summary>
    /// Creates a ramp of the four colours, darkest first.
    /// </summary>
    public ColourRamp(Color32 outline, Color32 shadow, Color32 middle, Color32 light)
    {
        this.outline = outline;
        this.shadow = shadow;
        this.middle = middle;
        this.light = light;
    }

    public Color32 Outline => outline;
    public Color32 Shadow => shadow;
    public Color32 Middle => middle;
    public Color32 Light => light;
}

/// <summary>
/// Which character materials are flat (always their middle tone) and which draw no outline.
/// </summary>
public static class CharacterMaterials
{
    public static readonly int Count = Enum.GetValues(typeof(CharacterMaterial)).Length;

    /// <summary>
    /// Whether the weapon's art gives <paramref name="material"/> its colours: the weapon's own materials, the smears
    /// and the dust. The recipe gives the others.
    /// </summary>
    public static bool IsWeapons(CharacterMaterial material)
    {
        return material >= CharacterMaterial.Steel;
    }

    /// <summary>
    /// Whether <paramref name="material"/> is drawn in its middle tone whatever its surface faces: the face, the inside
    /// of the hood, the eyes and the smears.
    /// </summary>
    public static bool IsFlat(CharacterMaterial material)
    {
        switch (material)
        {
            case CharacterMaterial.HoodInside:
            case CharacterMaterial.Face:
            case CharacterMaterial.FaceShade:
            case CharacterMaterial.Eye:
            case CharacterMaterial.SmearNew:
            case CharacterMaterial.SmearMiddle:
            case CharacterMaterial.SmearOld:
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Whether <paramref name="material"/> lines its edges: everything but the face's materials, the thin bow string,
    /// shaft and fletching, the smears and the dust.
    /// </summary>
    public static bool HasOutline(CharacterMaterial material)
    {
        switch (material)
        {
            case CharacterMaterial.HoodInside:
            case CharacterMaterial.Face:
            case CharacterMaterial.FaceShade:
            case CharacterMaterial.Eye:
            case CharacterMaterial.Bangs:
            case CharacterMaterial.Mask:
            case CharacterMaterial.BowString:
            case CharacterMaterial.Shaft:
            case CharacterMaterial.Fletching:
            case CharacterMaterial.SmearNew:
            case CharacterMaterial.SmearMiddle:
            case CharacterMaterial.SmearOld:
            case CharacterMaterial.Dust:
                return false;
            default:
                return true;
        }
    }
}

/// <summary>
/// One clip of a character's sheets: a motion, the weapon drawn with it (none for an unarmed one), and the frame it is
/// drawn in, its size and the pixel the feet stand on, from the bottom left.
/// </summary>
[Serializable]
public sealed class CharacterClip
{
    [Tooltip("The clip's name in its sheet's file and slice names: <character>_<clip>.png, <character>_<clip>_<direction>_<frame>.")]
    [SerializeField] private string name = "Clip";
    [Tooltip("The motion the clip draws.")]
    [SerializeField] private CharacterMotion motion;
    [Tooltip("The weapon drawn with it and carried while it moves, or none.")]
    [SerializeField] private CharacterWeaponArt weapon;
    [Tooltip("The size of each frame in pixels. Nothing the clip draws may reach its edge.")]
    [SerializeField] private Vector2Int frameSize = new(64, 64);
    [Tooltip("The pixel of the frame the feet stand on, from its bottom left: the sprites' pivot.")]
    [SerializeField] private Vector2Int pivot = new(32, 12);

    /// <summary>
    /// Creates an empty clip, for Unity serialization and the inspector.
    /// </summary>
    public CharacterClip()
    {
    }

    /// <summary>
    /// Creates a clip, for tests and tools.
    /// </summary>
    public CharacterClip(string clipName, CharacterMotion clipMotion, CharacterWeaponArt clipWeapon, Vector2Int size, Vector2Int feet)
    {
        name = clipName;
        motion = clipMotion;
        weapon = clipWeapon;
        frameSize = size;
        pivot = feet;
    }

    public string Name => name;
    public CharacterMotion Motion => motion;
    public CharacterWeaponArt Weapon => weapon;
    public Vector2Int FrameSize => frameSize;
    public Vector2Int Pivot => pivot;
}

/// <summary>
/// The smear drawn on frame <see cref="Frame"/> of a keyed motion: the area the blade's outer part swept from motion
/// time <see cref="From"/> to <see cref="To"/>.
/// </summary>
[Serializable]
public struct MotionSmear
{
    [SerializeField, Min(0)] private int frame;
    [SerializeField, Range(0f, 1f)] private float from;
    [SerializeField, Range(0f, 1f)] private float to;

    /// <summary>
    /// Creates the smear of <paramref name="frameIndex"/> over the motion times <paramref name="start"/> to
    /// <paramref name="end"/>.
    /// </summary>
    public MotionSmear(int frameIndex, float start, float end)
    {
        frame = frameIndex;
        from = start;
        to = end;
    }

    public int Frame => frame;
    public float From => from;
    public float To => to;
}

/// <summary>
/// An effect drawn on frame <see cref="Frame"/> of a keyed motion, at a size (dust spreads with it).
/// </summary>
[Serializable]
public struct MotionEffect
{
    [SerializeField, Min(0)] private int frame;
    [SerializeField] private MotionEffectKind kind;
    [SerializeField, Min(0.1f)] private float size;

    /// <summary>
    /// Creates an effect of <paramref name="effectKind"/> on <paramref name="frameIndex"/>.
    /// </summary>
    public MotionEffect(int frameIndex, MotionEffectKind effectKind, float effectSize)
    {
        frame = frameIndex;
        kind = effectKind;
        size = effectSize;
    }

    public int Frame => frame;
    public MotionEffectKind Kind => kind;
    public float Size => size;
}
