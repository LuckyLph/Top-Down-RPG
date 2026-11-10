using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// An editor-only recipe for a character's sprite sheets (Docs/Animation.md, The character rig), made like the
/// terrain and wall recipes: the skeleton's sizes in rig units, the shading, the height the standing character is drawn
/// at, the clips to bake, each a motion with an optional weapon in a frame of its own size, and the lantern it carries,
/// if any, baked as a layer of its own over every clip (Docs/Lighting.md, Phase 9). Each body plan is a kind
/// of recipe holding its own look and palette and making its own <see cref="CharacterPainter"/>: the rogue
/// (<see cref="RogueRecipe"/>) and the Weasel (<see cref="WeaselRecipe"/>); the rig, the motions, the canvas and the
/// shading are shared. <see cref="CharacterBaker"/> draws them without touching an asset, the same recipe always giving
/// the same pixels; <see cref="CharacterSheetWriter"/> writes each clip as
/// <c>Assets/Sprites/Characters/&lt;sheet name&gt;/&lt;sheet name&gt;_&lt;clip&gt;.png</c> and slices it, and the
/// inspector previews them.
/// </summary>
public abstract class CharacterRecipe : ScriptableObject
{
    [Tooltip("The character's name in its sheets' folder, file and slice names. Letters and digits only.")]
    [SerializeField] private string sheetName = "Rogue";
    [Tooltip("How tall the standing character is drawn, outline included, in pixels at 32 per unit. Sets the scale of every clip.")]
    [SerializeField, Range(16, 96)] private int targetHeight = 46;
    [Tooltip("The clips to bake, one sheet each.")]
    [SerializeField] private List<CharacterClip> clips = new();
    [Tooltip("The lantern the character carries, baked as a layer of its own over every clip, or none.")]
    [SerializeField] private CharacterLanternArt lantern;

    [Header("Body (rig units)")]
    [SerializeField, Min(1f)] private float thigh = 7f;
    [SerializeField, Min(1f)] private float shin = 7f;
    [Tooltip("The ankle's height over the ground when the foot is planted.")]
    [SerializeField, Min(0f)] private float ankle = 1.5f;
    [SerializeField, Min(1f)] private float upperArm = 5.4f;
    [SerializeField, Min(1f)] private float forearm = 5f;
    [Tooltip("From the pelvis to the neck.")]
    [SerializeField, Min(1f)] private float torso = 10.5f;
    [Tooltip("From the neck to the head's middle.")]
    [SerializeField, Min(0f)] private float neckToHead = 6.6f;
    [SerializeField, Min(1f)] private float headRadius = 6.6f;
    [SerializeField, Min(0f)] private float shoulderHalfWidth = 4.4f;
    [SerializeField, Min(0f)] private float hipHalfWidth = 2.5f;
    [Tooltip("The torso's radius at the waist and at the chest.")]
    [SerializeField] private Vector2 torsoRadius = new(3.2f, 3.5f);
    [Tooltip("Scales the arms' and legs' thickness.")]
    [SerializeField, Min(0.1f)] private float limbThickness = 1f;
    [SerializeField, Min(0.1f)] private float handRadius = 1.5f;

    [Header("Shading")]
    [Tooltip("A surface facing the light more than this takes the light tone.")]
    [SerializeField, Range(-1f, 1f)] private float lightThreshold = 0.64f;
    [Tooltip("A surface facing the light this much or less takes the shadow tone.")]
    [SerializeField, Range(-1f, 1f)] private float shadowThreshold = 0.28f;
    [Tooltip("A pixel beside another part nearer by more than this many depth pixels is lined.")]
    [SerializeField, Min(0f)] private float innerLineGap = 2.6f;

    public string SheetName => sheetName;
    public int TargetHeight => targetHeight;
    public IReadOnlyList<CharacterClip> Clips => clips;
    public CharacterLanternArt Lantern => lantern;
    public float Thigh => thigh;
    public float Shin => shin;
    public float Ankle => ankle;
    public float UpperArm => upperArm;
    public float Forearm => forearm;
    public float Torso => torso;
    public float NeckToHead => neckToHead;
    public float HeadRadius => headRadius;
    public float ShoulderHalfWidth => shoulderHalfWidth;
    public float HipHalfWidth => hipHalfWidth;
    public float WaistRadius => torsoRadius.x;
    public float ChestRadius => torsoRadius.y;
    public float LimbThickness => limbThickness;
    public float HandRadius => handRadius;
    public float LightThreshold => lightThreshold;
    public float ShadowThreshold => shadowThreshold;
    public float InnerLineGap => innerLineGap;

    /// <summary>
    /// The ramp the recipe gives one of the character's own materials (the weapon gives the others).
    /// </summary>
    public abstract ColourRamp Ramp(CharacterMaterial material);

    /// <summary>
    /// The dye a player's colours give one of the character's own materials (Docs/PlayerCharacter.md, Phase 5), or
    /// <see cref="CharacterDye.None"/> where the sheets keep their colour: everywhere, for a body plan whose colours are
    /// its own.
    /// </summary>
    public virtual CharacterDye DyeOf(CharacterMaterial material)
    {
        return CharacterDye.None;
    }

    /// <summary>
    /// How many options of <paramref name="slot"/> the character can wear (Docs/PlayerCharacter.md, Phase 6), the first
    /// its own; none for a body plan whose parts are its own.
    /// </summary>
    public virtual int PartOptions(CharacterSlot slot)
    {
        return 0;
    }

    /// <summary>
    /// The name of option <paramref name="option"/> of <paramref name="slot"/>.
    /// </summary>
    public virtual string PartName(CharacterSlot slot, int option)
    {
        return null;
    }

    /// <summary>
    /// The slot whose option <paramref name="slot"/>'s layers are drawn under (as a <see cref="CharacterSlot"/> index),
    /// one layer per option of it; -1 for a slot drawn on its own.
    /// </summary>
    public virtual int PartVariesWith(CharacterSlot slot)
    {
        return -1;
    }

    /// <summary>
    /// The weapon option <paramref name="option"/> of <paramref name="slot"/> carries outside its attacks
    /// (Docs/PlayerCharacter.md, Phase 7), whose colours its layer draws it in where a clip holds no weapon; none for
    /// a part that carries none.
    /// </summary>
    public virtual CharacterWeaponArt PartWeapon(CharacterSlot slot, int option)
    {
        return null;
    }

    /// <summary>
    /// Whether the character wears parts, so it is drawn a layer at a time, the body then each part over it.
    /// </summary>
    public bool HasParts
    {
        get
        {
            foreach (CharacterSlot slot in (CharacterSlot[])System.Enum.GetValues(typeof(CharacterSlot)))
            {
                if (PartOptions(slot) > 0)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// A painter drawing only <paramref name="layer"/> of the character holding <paramref name="weapon"/> (or none)
    /// onto <paramref name="canvas"/>: the body's layer draws the weapon and a cast's light too. A body plan without
    /// parts draws all of it.
    /// </summary>
    public virtual CharacterPainter CreatePainter(CharacterWeaponArt weapon, CharacterCanvas canvas, CharacterLayerKey layer)
    {
        return CreatePainter(weapon, canvas);
    }

    /// <summary>
    /// Whether any of the character's materials takes a player's colours, so its sheets carry palette maps.
    /// </summary>
    public bool HasDyes
    {
        get
        {
            for (int i = 0; i < CharacterMaterials.Count; i++)
            {
                if (DyeOf((CharacterMaterial)i) != CharacterDye.None)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// A painter of the recipe's body plan holding <paramref name="weapon"/> (or none), drawing onto
    /// <paramref name="canvas"/>.
    /// </summary>
    public abstract CharacterPainter CreatePainter(CharacterWeaponArt weapon, CharacterCanvas canvas);

    /// <summary>
    /// Whether the recipe can bake its clips: a sheet name of letters and digits, at least one clip, and every clip
    /// with a name of letters, digits and underscores used once, a motion with frames, a frame of at least 8 px a side
    /// and its pivot inside it. Returns the reason in <paramref name="problem"/> when it cannot.
    /// </summary>
    public bool CanGenerate(out string problem)
    {
        problem = null;
        if (!IsName(sheetName, false))
        {
            problem = "the sheet name must be letters and digits, starting with a letter.";
            return false;
        }

        if (clips.Count == 0)
        {
            problem = "the recipe has no clips.";
            return false;
        }

        HashSet<string> names = new();
        foreach (CharacterClip clip in clips)
        {
            if (clip == null || !IsName(clip.Name, true) || !names.Add(clip.Name))
            {
                problem = $"each clip needs its own name of letters, digits and underscores ({(clip != null ? clip.Name : "none")}).";
                return false;
            }

            if (clip.Motion == null || clip.Motion.FrameCount == 0)
            {
                problem = $"clip {clip.Name} has no motion with frames.";
                return false;
            }

            Vector2Int size = clip.FrameSize;
            Vector2Int pivot = clip.Pivot;
            if (size.x < 8 || size.y < 8 || pivot.x < 0 || pivot.y < 0 || pivot.x >= size.x || pivot.y >= size.y)
            {
                problem = $"clip {clip.Name} needs a frame of at least 8 px a side with its pivot inside.";
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// A ramp from twelve channel values, darkest colour first.
    /// </summary>
    protected static ColourRamp Colours(byte r0, byte g0, byte b0, byte r1, byte g1, byte b1, byte r2, byte g2, byte b2, byte r3, byte g3, byte b3)
    {
        return new ColourRamp(new Color32(r0, g0, b0, 255), new Color32(r1, g1, b1, 255), new Color32(r2, g2, b2, 255), new Color32(r3, g3, b3, 255));
    }

    /// <summary>
    /// Whether <paramref name="value"/> starts with a letter and holds only letters, digits and, when
    /// <paramref name="underscores"/>, underscores.
    /// </summary>
    internal static bool IsName(string value, bool underscores)
    {
        if (string.IsNullOrEmpty(value) || !char.IsLetter(value[0]))
        {
            return false;
        }

        foreach (char c in value)
        {
            if (!(char.IsLetterOrDigit(c) && c < 128) && !(underscores && c == '_'))
            {
                return false;
            }
        }

        return true;
    }
}
