using UnityEngine;

/// <summary>
/// An editor-only weapon as the character baker draws it: a blade (grip, pommel, crossguard and a tapering blade, held
/// in the right hand or in both) or a bow (held in the left hand, its limbs bending as it is drawn, a string and a
/// nocked arrow), its colours and those of its smears and effects, and how it is carried while the character moves.
/// Sizes are in rig units.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/Characters/Weapon Art", fileName = "WeaponArt")]
public sealed class CharacterWeaponArt : ScriptableObject
{
    [SerializeField] private WeaponKind kind;

    [Header("Blade")]
    [Tooltip("Both hands hold the grip, the left below the right.")]
    [SerializeField] private bool twoHanded;
    [Tooltip("How far below the right hand the left one holds the grip.")]
    [SerializeField, Min(0f)] private float leftGripOffset = 2.7f;
    [Tooltip("From the right hand to the pommel.")]
    [SerializeField, Min(0f)] private float pommel = 1.6f;
    [Tooltip("From the right hand to the crossguard.")]
    [SerializeField, Min(0f)] private float guardAt = 1.6f;
    [Tooltip("Half the crossguard's width.")]
    [SerializeField, Min(0f)] private float guardHalfWidth = 2.2f;
    [Tooltip("From the right hand to the blade's tip.")]
    [SerializeField, Min(1f)] private float length = 15.5f;
    [Tooltip("The blade's radius at the guard and at the tip.")]
    [SerializeField] private Vector2 bladeRadius = new(0.75f, 0.5f);
    [Tooltip("A smear covers the blade from this share of its length to the tip at its oldest sample, and from the second share at its newest, so it tapers toward its tail.")]
    [SerializeField] private Vector2 smearInnerShare = new(0.78f, 0.3f);

    [Header("Bow")]
    [Tooltip("From the grip to each limb's tip.")]
    [SerializeField, Min(1f)] private float halfLength = 9.5f;
    [Tooltip("From the grip to the string at rest.")]
    [SerializeField, Min(0f)] private float brace = 2.2f;
    [Tooltip("How far the limbs' tips curl back at rest and fully drawn.")]
    [SerializeField] private Vector2 bend = new(1.4f, 4.2f);
    [Tooltip("How thick each limb is at the grip and at its tip, in rig units.")]
    [SerializeField] private Vector2 limbRadius = new(0.85f, 0.45f);
    [Tooltip("The arrow's length.")]
    [SerializeField, Min(1f)] private float arrowLength = 14f;

    [Header("Carried while moving")]
    [Tooltip("The hand's place: outward from the shoulder, forward of the shoulders, and below them (negative). A blade is carried in the right hand, a bow in the left.")]
    [SerializeField] private Vector3 carryOffset = new(0.6f, 1.2f, -7f);
    [Tooltip("How much of its arm's swing the carrying hand keeps.")]
    [SerializeField, Range(0f, 1f)] private float carrySwing = 0.45f;
    [Tooltip("The blade's direction, or the bow's axis, at the middle of the swing.")]
    [SerializeField] private Vector3 carryDirection = new(0.22f, 0.62f, 0.75f);
    [Tooltip("Added to the direction as the arm swings forward, taken away as it swings back.")]
    [SerializeField] private Vector3 carryDirectionSwing = new(0f, 0.1f, 0f);
    [Tooltip("How much of the gait's shoulder twist the character keeps while carrying it.")]
    [SerializeField, Range(0f, 1f)] private float carryTwist = 1f;
    [Tooltip("Use the elbow poles below while carrying it, for a two-handed carry.")]
    [SerializeField] private bool carryPoles;
    [SerializeField] private Vector3 carryRightPole = new(0.8f, -0.2f, -1f);
    [SerializeField] private Vector3 carryLeftPole = new(-0.8f, -0.2f, -1f);

    [Header("Colours")]
    [SerializeField] private ColourRamp steel = new(new Color32(40, 46, 60, 255), new Color32(118, 130, 150, 255), new Color32(186, 198, 214, 255), new Color32(240, 246, 252, 255));
    [SerializeField] private ColourRamp guard = new(new Color32(40, 44, 56, 255), new Color32(120, 128, 146, 255), new Color32(182, 190, 206, 255), new Color32(234, 240, 250, 255));
    [SerializeField] private ColourRamp grip = new(new Color32(30, 18, 14, 255), new Color32(70, 40, 28, 255), new Color32(100, 60, 40, 255), new Color32(130, 84, 56, 255));
    [SerializeField] private ColourRamp wood = new(new Color32(42, 22, 12, 255), new Color32(104, 62, 30, 255), new Color32(142, 92, 46, 255), new Color32(178, 126, 68, 255));
    [SerializeField] private ColourRamp bowString = new(new Color32(200, 200, 190, 255), new Color32(196, 196, 186, 255), new Color32(222, 222, 212, 255), new Color32(240, 240, 232, 255));
    [SerializeField] private ColourRamp shaft = new(new Color32(70, 48, 24, 255), new Color32(150, 112, 64, 255), new Color32(186, 148, 94, 255), new Color32(212, 180, 122, 255));
    [SerializeField] private ColourRamp fletching = new(new Color32(80, 16, 16, 255), new Color32(176, 40, 36, 255), new Color32(214, 64, 52, 255), new Color32(240, 110, 90, 255));
    [Tooltip("The smears' colours from the newest part of the sweep to the oldest, and the release flash's.")]
    [SerializeField] private Color32[] smear = { new(255, 255, 255, 255), new(214, 236, 250, 255), new(160, 204, 236, 255) };
    [SerializeField] private ColourRamp dust = new(new Color32(120, 104, 80, 255), new Color32(166, 150, 120, 255), new Color32(196, 182, 152, 255), new Color32(222, 212, 186, 255));

    public WeaponKind Kind => kind;
    public bool TwoHanded => twoHanded;

    /// <summary>
    /// Whether the weapon takes the left hand: a bow, held in it, or a two-handed blade, gripped with it. A carried
    /// lantern hangs at the hip meanwhile (<see cref="CharacterLanternArt"/>).
    /// </summary>
    public bool TakesLeftHand => kind == WeaponKind.Bow || twoHanded;

    public float LeftGripOffset => leftGripOffset;
    public float Pommel => pommel;
    public float GuardAt => guardAt;
    public float GuardHalfWidth => guardHalfWidth;
    public float Length => length;
    public Vector2 BladeRadius => bladeRadius;
    public Vector2 SmearInnerShare => smearInnerShare;
    public float HalfLength => halfLength;
    public float Brace => brace;
    public Vector2 Bend => bend;
    public Vector2 LimbRadius => limbRadius;
    public float ArrowLength => arrowLength;

    /// <summary>
    /// Changes a gait's <paramref name="pose"/> for carrying the weapon: the holding hand (the right for a blade, the
    /// left for a bow) goes to its carry place, keeping part of its swing (<paramref name="swingCos"/> is the stride's
    /// cosine, positive while the right arm swings forward, <paramref name="swing"/> the arm swing), the blade or the
    /// bow takes its carry direction, the shoulders keep part of their twist, and a bow carries no arrow.
    /// </summary>
    public void Carry(CharacterPose pose, CharacterRecipe recipe, float swingCos, float swing, float shoulderHeight, float shoulderForward)
    {
        pose.Twist *= carryTwist;
        if (carryPoles)
        {
            pose.RightPole = carryRightPole;
            pose.LeftPole = carryLeftPole;
        }

        Vector3 direction = (carryDirection + carryDirectionSwing * swingCos).normalized;
        float outward = recipe.ShoulderHalfWidth + carryOffset.x;
        if (kind == WeaponKind.Blade)
        {
            pose.RightHand = new Vector3(outward, shoulderForward + carryOffset.y + carrySwing * swing * swingCos, shoulderHeight + carryOffset.z);
            pose.Blade = direction;
            return;
        }

        pose.LeftHand = new Vector3(-outward, shoulderForward + carryOffset.y - carrySwing * swing * swingCos, shoulderHeight + carryOffset.z);
        pose.BowUp = direction;
        pose.BowAim = new Vector3(0f, 1f, 0f);
        pose.Draw = 0f;
        pose.Arrow = 0f;
    }

    /// <summary>
    /// The ramp the weapon gives <paramref name="material"/>, for the weapon's, the smears' and the dust's materials.
    /// Each smear colour fills all four tones of its ramp.
    /// </summary>
    public ColourRamp Ramp(CharacterMaterial material)
    {
        switch (material)
        {
            case CharacterMaterial.Steel:
                return steel;
            case CharacterMaterial.Guard:
                return guard;
            case CharacterMaterial.Grip:
                return grip;
            case CharacterMaterial.Wood:
                return wood;
            case CharacterMaterial.BowString:
                return bowString;
            case CharacterMaterial.Shaft:
                return shaft;
            case CharacterMaterial.Fletching:
                return fletching;
            case CharacterMaterial.SmearNew:
                return Flat(SmearColour(0));
            case CharacterMaterial.SmearMiddle:
                return Flat(SmearColour(1));
            case CharacterMaterial.SmearOld:
                return Flat(SmearColour(2));
            default:
                return dust;
        }
    }

    /// <summary>
    /// Smear colour <paramref name="index"/>, or the last one when fewer are set, white when none are.
    /// </summary>
    private Color32 SmearColour(int index)
    {
        if (smear == null || smear.Length == 0)
        {
            return new Color32(255, 255, 255, 255);
        }

        return smear[Mathf.Min(index, smear.Length - 1)];
    }

    /// <summary>
    /// A ramp of one colour.
    /// </summary>
    private static ColourRamp Flat(Color32 colour)
    {
        return new ColourRamp(colour, colour, colour, colour);
    }
}
