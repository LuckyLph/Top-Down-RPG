using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// An editor-only recipe for a character's sprite sheets (Docs/Animation.md, The character rig), made like the
/// terrain and wall recipes: the body's sizes in rig units, the rogue's look (its hood and face, cape, palette and
/// shading), the height the standing character is drawn at, and the clips to bake, each a motion with an optional
/// weapon in a frame of its own size. <see cref="CharacterBaker"/> draws them without touching an asset, the same
/// recipe always giving the same pixels; <see cref="CharacterSheetWriter"/> writes each clip as
/// <c>Assets/Sprites/Characters/&lt;sheet name&gt;/&lt;sheet name&gt;_&lt;clip&gt;.png</c> and slices it, and the
/// inspector previews them.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/Characters/Character Recipe", fileName = "Recipe_Character")]
public sealed class CharacterRecipe : ScriptableObject
{
    [Tooltip("The character's name in its sheets' folder, file and slice names. Letters and digits only.")]
    [SerializeField] private string sheetName = "Rogue";
    [Tooltip("How tall the standing character is drawn, outline included, in pixels at 32 per unit. Sets the scale of every clip.")]
    [SerializeField, Range(16, 96)] private int targetHeight = 46;
    [Tooltip("The clips to bake, one sheet each.")]
    [SerializeField] private List<CharacterClip> clips = new();

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
    [Tooltip("Scales the boots.")]
    [SerializeField, Min(0.1f)] private float bootSize = 1.1f;

    [Header("Hood and face")]
    [Tooltip("The hood's radius, in head radii.")]
    [SerializeField, Min(1f)] private float hoodSize = 1.08f;
    [Tooltip("Where the hood's point reaches behind the head: back, and up, in head radii.")]
    [SerializeField] private Vector2 hoodTip = new(1.35f, 0.6f);
    [Tooltip("How far the head tips toward the camera, in degrees, so the face is not foreshortened by the view.")]
    [SerializeField, Range(0f, 45f)] private float faceTilt = 20f;
    [Tooltip("The hood's opening, in degrees from the face's middle: half its width, its middle's height and half its height.")]
    [SerializeField] private Vector3 opening = new(50f, -6f, 31f);
    [Tooltip("The outer share of the opening in the hood's shadow.")]
    [SerializeField, Range(0f, 1f)] private float rimShare = 0.3f;
    [Tooltip("Above this height, in degrees, the opening is all shadow.")]
    [SerializeField] private float rimTop = 19f;
    [Tooltip("Above this height the face is in the hood's shade.")]
    [SerializeField] private float foreheadShade;
    [Tooltip("The bangs' lowest edge in degrees, how far their points drop below it, the points' spacing in degrees, and how much they rise per degree from the middle.")]
    [SerializeField] private Vector4 bangs = new(5f, 7f, 13f, 0.05f);
    [Tooltip("The mask's top edge in degrees at the cheeks, how far it rises to the nose, and over how many degrees from the middle.")]
    [SerializeField] private Vector3 mask = new(-18f, 5f, 18f);
    [Tooltip("The eyes' place: degrees each side of the middle, and height in degrees.")]
    [SerializeField] private Vector2 eyes = new(15f, -7.5f);
    [Tooltip("How many pixels tall each eye is.")]
    [SerializeField, Range(1, 3)] private int eyeHeight = 2;

    [Header("Cape")]
    [Tooltip("How many strips hang from the shoulders.")]
    [SerializeField, Range(2, 9)] private int capeStrips = 5;
    [Tooltip("The hem's height as a share of the pelvis's while the cape hangs.")]
    [SerializeField, Range(0f, 1f)] private float capeHem = 0.45f;
    [Tooltip("The cape is two layers, the lining inside, showing at the hem and wherever the inside faces the camera.")]
    [SerializeField] private bool capeLining = true;

    [Header("Shading")]
    [Tooltip("A surface facing the light more than this takes the light tone.")]
    [SerializeField, Range(-1f, 1f)] private float lightThreshold = 0.64f;
    [Tooltip("A surface facing the light this much or less takes the shadow tone.")]
    [SerializeField, Range(-1f, 1f)] private float shadowThreshold = 0.28f;
    [Tooltip("A pixel beside another part nearer by more than this many depth pixels is lined.")]
    [SerializeField, Min(0f)] private float innerLineGap = 2.6f;

    [Header("Colours")]
    [SerializeField] private ColourRamp cloak = Colours(6, 6, 10, 20, 20, 32, 36, 38, 56, 78, 86, 120);
    [SerializeField] private ColourRamp hoodInside = Colours(6, 6, 10, 6, 6, 10, 6, 6, 10, 6, 6, 10);
    [SerializeField] private ColourRamp face = Colours(232, 170, 130, 232, 170, 130, 232, 170, 130, 232, 170, 130);
    [SerializeField] private ColourRamp faceShade = Colours(184, 116, 96, 184, 116, 96, 184, 116, 96, 184, 116, 96);
    [SerializeField] private ColourRamp eye = Colours(14, 10, 14, 14, 10, 14, 14, 10, 14, 14, 10, 14);
    [SerializeField] private ColourRamp hair = Colours(60, 62, 78, 150, 156, 178, 196, 202, 220, 236, 240, 248);
    [SerializeField] private ColourRamp maskColours = Colours(60, 6, 22, 150, 24, 40, 212, 46, 52, 250, 112, 82);
    [SerializeField] private ColourRamp jerkin = Colours(10, 8, 10, 40, 32, 36, 62, 52, 54, 100, 88, 86);
    [SerializeField] private ColourRamp shirt = Colours(6, 6, 10, 20, 20, 28, 34, 34, 46, 62, 64, 84);
    [SerializeField] private ColourRamp trousers = Colours(6, 6, 10, 26, 26, 34, 42, 42, 54, 74, 76, 96);
    [SerializeField] private ColourRamp boots = Colours(8, 6, 6, 34, 26, 24, 54, 42, 38, 94, 78, 68);
    [SerializeField] private ColourRamp bracers = Colours(8, 6, 6, 28, 21, 21, 46, 35, 33, 78, 62, 56);
    [SerializeField] private ColourRamp gloves = Colours(6, 6, 8, 22, 20, 24, 38, 34, 38, 64, 58, 62);
    [SerializeField] private ColourRamp belt = Colours(10, 8, 8, 32, 26, 24, 50, 42, 38, 80, 70, 62);
    [SerializeField] private ColourRamp fittings = Colours(40, 44, 56, 120, 128, 146, 182, 190, 206, 234, 240, 250);
    [SerializeField] private ColourRamp sash = Colours(60, 6, 22, 150, 24, 40, 212, 46, 52, 250, 112, 82);
    [SerializeField] private ColourRamp lining = Colours(40, 4, 14, 112, 16, 32, 170, 32, 44, 222, 74, 64);
    [SerializeField] private ColourRamp daggerGrip = Colours(30, 18, 14, 70, 40, 28, 100, 60, 40, 130, 84, 56);

    public string SheetName => sheetName;
    public int TargetHeight => targetHeight;
    public IReadOnlyList<CharacterClip> Clips => clips;
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
    public float BootSize => bootSize;
    public float HoodSize => hoodSize;
    public Vector2 HoodTip => hoodTip;
    public float FaceTilt => faceTilt;
    public Vector3 Opening => opening;
    public float RimShare => rimShare;
    public float RimTop => rimTop;
    public float ForeheadShade => foreheadShade;
    public Vector4 Bangs => bangs;
    public Vector3 Mask => mask;
    public Vector2 Eyes => eyes;
    public int EyeHeight => eyeHeight;
    public int CapeStrips => capeStrips;
    public float CapeHem => capeHem;
    public bool CapeLining => capeLining;
    public float LightThreshold => lightThreshold;
    public float ShadowThreshold => shadowThreshold;
    public float InnerLineGap => innerLineGap;

    /// <summary>
    /// The ramp the recipe gives one of the character's own materials (the weapon gives the others).
    /// </summary>
    public ColourRamp Ramp(CharacterMaterial material)
    {
        switch (material)
        {
            case CharacterMaterial.Cloak:
                return cloak;
            case CharacterMaterial.HoodInside:
                return hoodInside;
            case CharacterMaterial.Face:
                return face;
            case CharacterMaterial.FaceShade:
                return faceShade;
            case CharacterMaterial.Eye:
                return eye;
            case CharacterMaterial.Bangs:
                return hair;
            case CharacterMaterial.Mask:
                return maskColours;
            case CharacterMaterial.Jerkin:
                return jerkin;
            case CharacterMaterial.Shirt:
                return shirt;
            case CharacterMaterial.Trousers:
                return trousers;
            case CharacterMaterial.Boots:
                return boots;
            case CharacterMaterial.Bracers:
                return bracers;
            case CharacterMaterial.Gloves:
                return gloves;
            case CharacterMaterial.Belt:
                return belt;
            case CharacterMaterial.Fittings:
                return fittings;
            case CharacterMaterial.Sash:
                return sash;
            case CharacterMaterial.Lining:
                return lining;
            default:
                return daggerGrip;
        }
    }

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
    /// Whether <paramref name="value"/> starts with a letter and holds only letters, digits and, when
    /// <paramref name="underscores"/>, underscores.
    /// </summary>
    private static bool IsName(string value, bool underscores)
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

    /// <summary>
    /// A ramp from twelve channel values, darkest colour first.
    /// </summary>
    private static ColourRamp Colours(byte r0, byte g0, byte b0, byte r1, byte g1, byte b1, byte r2, byte g2, byte b2, byte r3, byte g3, byte b3)
    {
        return new ColourRamp(new Color32(r0, g0, b0, 255), new Color32(r1, g1, b1, 255), new Color32(r2, g2, b2, 255), new Color32(r3, g3, b3, 255));
    }
}
