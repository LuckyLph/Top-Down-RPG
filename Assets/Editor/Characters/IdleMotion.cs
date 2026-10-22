using UnityEngine;

/// <summary>
/// An editor-only idle: a short breathing loop on the standing pose. The pelvis sinks and rises once a loop (the knees
/// give a little), the body leans with the breath, the hands sway slightly forward and back, and the cape ripples a
/// whole number of times so the loop closes. The feet stay planted, the left one a little in front, as every gait's
/// first frame has it. Sizes are in rig units for the default body and scale with the recipe's legs and arms.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/Characters/Idle Motion", fileName = "Motion_Idle")]
public sealed class IdleMotion : CharacterMotion
{
    [Header("Loop")]
    [Tooltip("Frames in one breath; 1 holds the standing pose still.")]
    [SerializeField, Range(1, 16)] private int frames = 8;
    [Tooltip("Frames per second.")]
    [SerializeField, Min(0.5f)] private float framesPerSecond = 6f;

    [Header("Stance (rig units for 14-unit legs)")]
    [Tooltip("How far each foot stands out from under its hip.")]
    [SerializeField, Min(0f)] private float stanceOut = 0.5f;
    [Tooltip("How far the left foot stands in front of the right.")]
    [SerializeField] private float leftFootForward = 1f;
    [Tooltip("How far the pelvis sits below the standing pose's at the top of the breath.")]
    [SerializeField, Min(0f)] private float crouch = 0.3f;
    [Tooltip("How far the pelvis sinks at the bottom of the breath.")]
    [SerializeField, Min(0f)] private float breath = 0.6f;

    [Header("Body")]
    [Tooltip("Forward lean of the spine, in degrees.")]
    [SerializeField] private float lean = 2f;
    [Tooltip("How far the lean grows with the breath, in degrees.")]
    [SerializeField] private float leanSway = 1f;

    [Header("Arms (rig units for 10.4-unit arms)")]
    [Tooltip("How far out from the shoulders the hands hang.")]
    [SerializeField] private float armOut = 1f;
    [Tooltip("How far the hands sway forward and back with the breath.")]
    [SerializeField, Min(0f)] private float armSway = 0.4f;

    [Header("Cape")]
    [Tooltip("How far the cape flows back and up, 0 hanging to 1 streaming.")]
    [SerializeField, Range(0f, 1f)] private float capeFlow = 0.1f;
    [Tooltip("How many times the cape ripples in one loop.")]
    [SerializeField, Range(0, 4)] private int capeWaves = 1;

    public override int FrameCount => frames;
    public override CharacterGait Gait => CharacterGait.Idle;
    public float FramesPerSecond => framesPerSecond;

    /// <summary>
    /// One frame lasts a constant share of the loop.
    /// </summary>
    public override float FrameSeconds(int frame)
    {
        return 1f / framesPerSecond;
    }

    /// <summary>
    /// The pose a share <paramref name="frame"/> / <see cref="FrameCount"/> into the loop: the breath is lowest half
    /// way through.
    /// </summary>
    public override CharacterPose PoseAt(int frame, CharacterRecipe recipe, CharacterWeaponArt weapon)
    {
        float phase = frame / (float)frames;
        float legScale = (recipe.Thigh + recipe.Shin) / GaitMotion.TunedLegLength;
        float armScale = (recipe.UpperArm + recipe.Forearm) / GaitMotion.TunedArmLength;
        float sink = 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * phase);
        CharacterPose standing = CharacterRig.StandingPose(recipe);
        float side = recipe.ShoulderHalfWidth + armOut;
        float sway = armSway * armScale * Mathf.Sin(2f * Mathf.PI * phase);
        float footSide = recipe.HipHalfWidth + stanceOut * legScale;
        float footForward = leftFootForward * legScale * 0.5f;
        return new CharacterPose
        {
            PelvisHeight = standing.PelvisHeight - (crouch + breath * sink) * legScale,
            Lean = lean + leanSway * sink,
            LeftFoot = new Vector3(-footSide, footForward, recipe.Ankle),
            RightFoot = new Vector3(footSide, -footForward, recipe.Ankle),
            LeftHand = new Vector3(-side, sway, standing.LeftHand.z - (crouch + breath * sink) * legScale),
            RightHand = new Vector3(side, -sway, standing.RightHand.z - (crouch + breath * sink) * legScale),
            LeftPole = standing.LeftPole,
            RightPole = standing.RightPole,
            CapeFlow = capeFlow,
            CapePhase = phase * capeWaves * 0.5f
        };
    }
}
