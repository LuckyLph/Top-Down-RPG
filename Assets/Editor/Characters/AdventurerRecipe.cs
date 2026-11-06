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
/// The adventurer's <see cref="CharacterRecipe"/>, the player's base body: boots, a tunic over a shirt, a bare head with
/// its ears, its hair and its locks, the face laid out on it by angle, and its palette, drawn by an
/// <see cref="AdventurerPainter"/>.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/Characters/Adventurer Recipe", fileName = "Recipe_Adventurer")]
public sealed class AdventurerRecipe : CharacterRecipe
{
    [Header("Outfit")]
    [Tooltip("Scales the boots.")]
    [SerializeField, Min(0.1f)] private float bootSize = 1.1f;
    [Tooltip("Where the boots' tops sit, from the knee (0) to the ankle (1).")]
    [SerializeField, Range(0f, 1f)] private float bootTop = 0.35f;
    [Tooltip("How far the tunic's skirt hangs below the hips, in rig units; none at 0.")]
    [SerializeField, Min(0f)] private float skirtLength = 1.4f;
    [Tooltip("How the arms are dressed.")]
    [SerializeField] private AdventurerSleeves sleeves = AdventurerSleeves.Long;
    [Tooltip("Whether the hands wear gloves.")]
    [SerializeField] private bool gloves;
    [Tooltip("Whether a satchel's strap crosses the chest from the left shoulder to a pouch at the right hip.")]
    [SerializeField] private bool strap;

    [Header("Head and hair")]
    [Tooltip("The head's radius with its hair, in head radii.")]
    [SerializeField, Min(1f)] private float hairSize = 1.1f;
    [Tooltip("How far the head tips toward the camera, in degrees, so the face is not foreshortened by the view.")]
    [SerializeField, Range(0f, 45f)] private float faceTilt = 20f;
    [Tooltip("The locks standing off the head, drawn over the face: the hairstyle.")]
    [SerializeField] private HairLock[] locks = new HairLock[0];
    [Tooltip("The ears, in degrees from the face's middle (across and up), and their radius in head radii; none at 0.")]
    [SerializeField] private Vector3 ears = new(88f, -12f, 0f);

    [Header("Face")]
    [Tooltip("The face, in degrees from its middle: half its width, its middle's height and half its height. Hair covers the rest of the head.")]
    [SerializeField] private Vector3 face = new(72f, -16f, 44f);
    [Tooltip("The fringe's lowest edge in degrees, how far its points drop below it, the points' spacing in degrees, and how much it rises per degree from the middle.")]
    [SerializeField] private Vector4 fringe = new(10f, 7f, 18f, 0.1f);
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
    [SerializeField] private ColourRamp blushColour = Colours(238, 150, 130, 238, 150, 130, 238, 150, 130, 238, 150, 130);
    [SerializeField] private ColourRamp hair = Colours(42, 24, 14, 96, 56, 30, 140, 84, 44, 184, 124, 70);
    [SerializeField] private ColourRamp tunic = Colours(24, 30, 46, 70, 96, 140, 96, 128, 176, 140, 172, 214);
    [SerializeField] private ColourRamp shirt = Colours(36, 32, 30, 150, 140, 122, 196, 186, 164, 232, 224, 204);
    [SerializeField] private ColourRamp trousers = Colours(28, 24, 20, 80, 70, 58, 110, 98, 82, 146, 132, 112);
    [SerializeField] private ColourRamp boots = Colours(22, 14, 10, 70, 46, 32, 96, 64, 44, 130, 90, 62);
    [SerializeField] private ColourRamp belt = Colours(26, 16, 10, 82, 52, 30, 112, 74, 44, 150, 104, 64);
    [SerializeField] private ColourRamp fittings = Colours(60, 44, 14, 150, 116, 40, 206, 168, 70, 244, 220, 140);
    [SerializeField] private ColourRamp glovesColour = Colours(22, 14, 10, 70, 46, 32, 96, 64, 44, 130, 90, 62);

    public float BootSize => bootSize;
    public float BootTop => bootTop;
    public float SkirtLength => skirtLength;
    public AdventurerSleeves Sleeves => sleeves;
    public bool Gloves => gloves;
    public bool Strap => strap;
    public float HairSize => hairSize;
    public float FaceTilt => faceTilt;
    public HairLock[] Locks => locks;
    public Vector3 Ears => ears;
    public Vector3 Face => face;
    public Vector4 Fringe => fringe;
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
            default:
                return fittings;
        }
    }

    /// <inheritdoc />
    public override CharacterPainter CreatePainter(CharacterWeaponArt weapon, CharacterCanvas canvas)
    {
        return new AdventurerPainter(this, weapon, canvas);
    }
}
