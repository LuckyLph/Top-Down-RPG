using UnityEngine;

/// <summary>
/// The rogue's <see cref="CharacterRecipe"/>: its boots, its hood and the face laid out on it by angle, its cape and its
/// palette (Docs/Animation.md's Look), drawn by a <see cref="RoguePainter"/>.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/Characters/Rogue Recipe", fileName = "Recipe_Rogue")]
public sealed class RogueRecipe : CharacterRecipe
{
    [Header("Boots")]
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

    /// <inheritdoc />
    public override ColourRamp Ramp(CharacterMaterial material)
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

    /// <inheritdoc />
    public override CharacterPainter CreatePainter(CharacterWeaponArt weapon, CharacterCanvas canvas)
    {
        return new RoguePainter(this, weapon, canvas);
    }
}
