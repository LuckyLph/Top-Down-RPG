using UnityEngine;

/// <summary>
/// An editor-only lantern a character carries (Docs/Lighting.md, Phase 9), drawn by its <see cref="CharacterPainter"/>
/// on every frame of its clips and baked as a layer of its own over them: hanging from the left hand, or from a hook at
/// the left hip while the clip's weapon takes that hand (<see cref="CharacterWeaponArt.TakesLeftHand"/>). A brass bail,
/// an iron pyramid roof with a knob, a box of glowing glass between four iron corner bars with the wick showing through,
/// and an iron base, in rig units times <see cref="Size"/>; it hangs plumb, swinging with a gait's stride
/// (<see cref="CharacterPose.LanternSwing"/>), and its light shines from the glass's middle.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/Characters/Lantern Art", fileName = "Lantern")]
public sealed class CharacterLanternArt : ScriptableObject
{
    [Header("Shape (rig units, times the size)")]
    [Tooltip("Scales every part of the lantern but the bail's thickness.")]
    [SerializeField, Min(0.1f)] private float size = 1.4f;
    [Tooltip("How far the bail drops from the hand or the hook to the roof's knob.")]
    [SerializeField, Min(0f)] private float bailDrop = 1.1f;
    [Tooltip("The bail's radius, in rig units whatever the size.")]
    [SerializeField, Min(0.05f)] private float bailRadius = 0.25f;
    [SerializeField, Min(0.1f)] private float roofHeight = 1.3f;
    [Tooltip("How far the roof's eaves reach past the glass.")]
    [SerializeField, Min(0f)] private float eave = 0.5f;
    [Tooltip("Half the glass's width.")]
    [SerializeField, Min(0.1f)] private float glassHalfWidth = 1.7f;
    [SerializeField, Min(0.1f)] private float glassHeight = 4f;
    [SerializeField, Min(0.05f)] private float baseHeight = 0.6f;
    [Tooltip("How far the base reaches past the glass.")]
    [SerializeField, Min(0f)] private float baseOverhang = 0.35f;
    [SerializeField, Min(0.05f)] private float barRadius = 0.4f;
    [SerializeField, Min(0.05f)] private float knobRadius = 0.55f;
    [SerializeField, Min(0.05f)] private float wickRadius = 0.7f;
    [Tooltip("How far the wick sits below the glass's middle.")]
    [SerializeField] private float wickDrop = 0.3f;

    [Header("Carrying (rig units)")]
    [Tooltip("How far below the hand's middle the bail hangs, in hand radii.")]
    [SerializeField, Min(0f)] private float handDrop = 0.4f;
    [Tooltip("Where the hook at the left hip hangs it from: along the hips' axis (negative to the left), forward of the hips, and up the spine from the pelvis.")]
    [SerializeField] private Vector3 hipHook = new(-5.6f, 0.6f, 1f);
    [Tooltip("How far it hangs back from the hip hook, in degrees, and how far it leans out from the leg, as a share.")]
    [SerializeField] private Vector2 hipTilt = new(6f, 0.15f);
    [Tooltip("How far it swings with a gait's stride at the hip, in degrees.")]
    [SerializeField, Min(0f)] private float swingDegrees = 12f;
    [Tooltip("The share of the hip's swing it swings in the hand.")]
    [SerializeField, Range(0f, 1f)] private float handSwingShare = 0.6f;

    [Header("Colours")]
    [SerializeField] private ColourRamp iron = new(new Color32(20, 20, 26, 255), new Color32(44, 46, 54, 255), new Color32(72, 76, 86, 255), new Color32(108, 112, 122, 255));
    [SerializeField] private ColourRamp brass = new(new Color32(42, 26, 12, 255), new Color32(106, 74, 30, 255), new Color32(154, 114, 52, 255), new Color32(200, 160, 80, 255));
    [Tooltip("The glass, glowing, the lamp posts' glass.")]
    [SerializeField] private Color32 glass = new(255, 204, 110, 255);
    [Tooltip("The wick's flame showing through the glass, glowing.")]
    [SerializeField] private Color32 wick = new(255, 242, 196, 255);

    public float Size => size;
    public float BailDrop => bailDrop;
    public float BailRadius => bailRadius;
    public float RoofHeight => roofHeight;
    public float Eave => eave;
    public float GlassHalfWidth => glassHalfWidth;
    public float GlassHeight => glassHeight;
    public float BaseHeight => baseHeight;
    public float BaseOverhang => baseOverhang;
    public float BarRadius => barRadius;
    public float KnobRadius => knobRadius;
    public float WickRadius => wickRadius;
    public float WickDrop => wickDrop;
    public float HandDrop => handDrop;
    public Vector3 HipHook => hipHook;
    public Vector2 HipTilt => hipTilt;
    public float SwingDegrees => swingDegrees;
    public float HandSwingShare => handSwingShare;

    /// <summary>
    /// The ramp of one of the lantern's materials: its iron and brass shaded, its glass and wick flat.
    /// </summary>
    public ColourRamp Ramp(CharacterMaterial material)
    {
        switch (material)
        {
            case CharacterMaterial.LanternIron:
                return iron;
            case CharacterMaterial.LanternBrass:
                return brass;
            case CharacterMaterial.LanternGlass:
                return new ColourRamp(glass, glass, glass, glass);
            default:
                return new ColourRamp(wick, wick, wick, wick);
        }
    }
}
