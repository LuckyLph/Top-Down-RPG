using UnityEngine;

/// <summary>
/// The Weasel's <see cref="CharacterRecipe"/>: a furred bandit with a snout, ears, a tail, a pale belly and muzzle, a red
/// band across its eyes knotted behind its head, and wrapped forearms and shins, drawn by a
/// <see cref="WeaselPainter"/>. Angles on the head are in degrees from the face's middle, as the rogue's face is laid
/// out.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/Characters/Weasel Recipe", fileName = "Recipe_Weasel")]
public sealed class WeaselRecipe : CharacterRecipe
{
    [Header("Paws")]
    [Tooltip("Scales the paws.")]
    [SerializeField, Min(0.1f)] private float pawSize = 1f;

    [Header("Head")]
    [Tooltip("How far the head tips toward the camera, in degrees, so the face is not foreshortened by the view.")]
    [SerializeField, Range(0f, 45f)] private float faceTilt = 20f;
    [Tooltip("How far the snout reaches from the head's middle, in head radii.")]
    [SerializeField, Min(0f)] private float snoutLength = 1.35f;
    [Tooltip("The snout's radius at its base and at its tip, in head radii.")]
    [SerializeField] private Vector2 snoutRadius = new(0.55f, 0.3f);
    [Tooltip("How far the snout points below level, in degrees.")]
    [SerializeField, Range(-30f, 60f)] private float snoutDrop = 18f;
    [SerializeField, Min(0.1f)] private float noseRadius = 0.85f;
    [Tooltip("The pale chin and cheeks: below this height, in degrees, within the given half width.")]
    [SerializeField] private Vector2 chin = new(-30f, 60f);

    [Header("Ears")]
    [Tooltip("How long each ear is, in head radii.")]
    [SerializeField, Min(0f)] private float earLength = 0.7f;
    [Tooltip("Each ear's radius at its base, in rig units.")]
    [SerializeField, Min(0.1f)] private float earRadius = 1.5f;
    [Tooltip("Where an ear sits on the head, in degrees from the top toward the side.")]
    [SerializeField, Range(0f, 90f)] private float earPlace = 42f;
    [Tooltip("How far an ear points out from straight up, in degrees.")]
    [SerializeField, Range(0f, 90f)] private float earSpread = 25f;
    [Tooltip("How far an ear leans back, in head radii over its length.")]
    [SerializeField] private float earBack = 0.15f;
    [Tooltip("A pixel of an ear facing forward more than this shows the ear's inside.")]
    [SerializeField, Range(-1f, 1f)] private float earInsideFacing = 0.3f;
    [Tooltip("A ring at the right ear's base.")]
    [SerializeField] private bool earring = true;

    [Header("Band and eyes")]
    [Tooltip("The band across the eyes: its lowest and highest edge, in degrees.")]
    [SerializeField] private Vector2 band = new(-14f, 6f);
    [Tooltip("How long the band's two tails hang from the knot behind the head, in rig units.")]
    [SerializeField, Min(0f)] private float bandTails = 3.5f;
    [Tooltip("The eyes' place: degrees each side of the middle, and height in degrees.")]
    [SerializeField] private Vector2 eyes = new(24f, -5f);
    [Tooltip("How many pixels tall each eye is.")]
    [SerializeField, Range(1, 3)] private int eyeHeight = 1;

    [Header("Belly and wraps")]
    [Tooltip("A pixel of the torso facing forward more than this is the pale belly.")]
    [SerializeField, Range(-1f, 1f)] private float bellyFacing = 0.35f;
    [Tooltip("The share of the forearm, from the wrist, wrapped.")]
    [SerializeField, Range(0f, 1f)] private float forearmWraps = 0.7f;
    [Tooltip("The share of the shin, from the ankle, wrapped.")]
    [SerializeField, Range(0f, 1f)] private float shinWraps = 0.6f;
    [Tooltip("How many bands of the second wrap colour cross each wrap.")]
    [SerializeField, Range(0, 4)] private int wrapBands = 2;

    [Header("Tail")]
    [Tooltip("The tail's length, in rig units.")]
    [SerializeField, Min(0f)] private float tailLength = 13f;
    [Tooltip("The tail's radius at its root, its middle and its tip.")]
    [SerializeField] private Vector3 tailRadius = new(1.4f, 1.8f, 0.7f);
    [Tooltip("How far below level the tail leaves the body, in degrees.")]
    [SerializeField, Range(-90f, 90f)] private float tailDroop = 50f;
    [Tooltip("How far the tail curls up along its length, in degrees.")]
    [SerializeField, Range(-180f, 180f)] private float tailCurl = 80f;
    [Tooltip("How far the tail's tip sways sideways with the stride, in rig units.")]
    [SerializeField, Min(0f)] private float tailSway = 1.5f;
    [Tooltip("How far the tail lifts as the body moves fast, in degrees at full flow.")]
    [SerializeField] private float tailLift = 25f;
    [Tooltip("The share of the tail, from the tip, in the dark fur.")]
    [SerializeField, Range(0f, 1f)] private float tailTip;

    [Header("Colours")]
    [SerializeField] private ColourRamp fur = Colours(40, 26, 24, 104, 80, 70, 146, 118, 100, 190, 160, 132);
    [SerializeField] private ColourRamp furLight = Colours(72, 56, 52, 186, 170, 156, 222, 210, 194, 246, 240, 228);
    [SerializeField] private ColourRamp furDark = Colours(14, 10, 10, 38, 30, 30, 58, 46, 44, 86, 72, 66);
    [SerializeField] private ColourRamp nose = Colours(10, 8, 8, 24, 18, 20, 40, 30, 32, 84, 70, 72);
    [SerializeField] private ColourRamp earInside = Colours(90, 46, 50, 196, 128, 128, 214, 150, 148, 232, 178, 172);
    [SerializeField] private ColourRamp eye = Colours(20, 14, 6, 250, 214, 80, 250, 214, 80, 250, 214, 80);
    [SerializeField] private ColourRamp bandColours = Colours(60, 6, 22, 150, 24, 40, 212, 46, 52, 250, 112, 82);
    [SerializeField] private ColourRamp wraps = Colours(14, 18, 40, 40, 58, 120, 62, 90, 170, 110, 140, 214);
    [SerializeField] private ColourRamp wrapsBand = Colours(60, 26, 10, 168, 82, 30, 216, 122, 44, 244, 172, 82);
    [SerializeField] private ColourRamp fittings = Colours(70, 44, 8, 170, 120, 30, 226, 178, 60, 250, 226, 130);

    public float PawSize => pawSize;
    public float FaceTilt => faceTilt;
    public float SnoutLength => snoutLength;
    public Vector2 SnoutRadius => snoutRadius;
    public float SnoutDrop => snoutDrop;
    public float NoseRadius => noseRadius;
    public Vector2 Chin => chin;
    public float EarLength => earLength;
    public float EarRadius => earRadius;
    public float EarPlace => earPlace;
    public float EarSpread => earSpread;
    public float EarBack => earBack;
    public float EarInsideFacing => earInsideFacing;
    public bool Earring => earring;
    public Vector2 Band => band;
    public float BandTails => bandTails;
    public Vector2 Eyes => eyes;
    public int EyeHeight => eyeHeight;
    public float BellyFacing => bellyFacing;
    public float ForearmWraps => forearmWraps;
    public float ShinWraps => shinWraps;
    public int WrapBands => wrapBands;
    public float TailLength => tailLength;
    public Vector3 TailRadius => tailRadius;
    public float TailDroop => tailDroop;
    public float TailCurl => tailCurl;
    public float TailSway => tailSway;
    public float TailLift => tailLift;
    public float TailTip => tailTip;

    /// <inheritdoc />
    public override ColourRamp Ramp(CharacterMaterial material)
    {
        switch (material)
        {
            case CharacterMaterial.FurLight:
                return furLight;
            case CharacterMaterial.FurDark:
                return furDark;
            case CharacterMaterial.Nose:
                return nose;
            case CharacterMaterial.EarInside:
                return earInside;
            case CharacterMaterial.Eye:
                return eye;
            case CharacterMaterial.Sash:
                return bandColours;
            case CharacterMaterial.Wraps:
                return wraps;
            case CharacterMaterial.WrapsBand:
                return wrapsBand;
            case CharacterMaterial.Fittings:
                return fittings;
            default:
                return fur;
        }
    }

    /// <inheritdoc />
    public override CharacterPainter CreatePainter(CharacterWeaponArt weapon, CharacterCanvas canvas)
    {
        return new WeaselPainter(this, weapon, canvas);
    }
}
