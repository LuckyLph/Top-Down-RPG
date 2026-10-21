using UnityEngine;

/// <summary>
/// An editor-only gait: one cycle of walking, running or sprinting, each a setting of the same parametric stride. Each
/// foot slides back along the ground while planted (the first <see cref="duty"/> of its cycle), then swings forward
/// through the air along a curve; the other foot is half a cycle behind. The pelvis bobs twice a cycle (highest
/// mid-stance when walking, lowest when running), the body leans, the arms swing against the legs and the shoulders
/// twist with them, and the cape flows. Frame 0 has the left foot planted in front, so every gait starts at the same
/// place in the stride. Strides scale with the recipe's legs and arm swings with its arms; a carried weapon changes the
/// arm that holds it (<see cref="CharacterWeaponArt.Carry"/>). It names its gait (walk, run or sprint), and its ground
/// speed is how fast a planted foot slides back: the speed at which the feet do not slide.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/Characters/Gait Motion", fileName = "Motion_Gait")]
public sealed class GaitMotion : CharacterMotion
{
    public const float TunedLegLength = 14f;
    public const float TunedArmLength = 10.4f;

    [Header("Cycle")]
    [Tooltip("Which gait this is: the runtime picks it by speed between the others.")]
    [SerializeField] private CharacterGait gait = CharacterGait.Run;
    [Tooltip("Frames in one cycle (two steps).")]
    [SerializeField, Range(4, 16)] private int frames = 8;
    [Tooltip("Frames per second at the gait's natural speed.")]
    [SerializeField, Min(1f)] private float framesPerSecond = 12f;

    [Header("Feet (rig units for 14-unit legs)")]
    [Tooltip("Share of the cycle a foot is planted. Under half, both feet leave the ground between steps.")]
    [SerializeField, Range(0.1f, 0.8f)] private float duty = 0.38f;
    [Tooltip("How far forward of the hips a foot lands.")]
    [SerializeField] private float front = 3.6f;
    [Tooltip("How far behind the hips a foot leaves the ground (negative).")]
    [SerializeField] private float back = -5.6f;
    [Tooltip("The swing curve's first control point: forward offset from where the foot left, and height.")]
    [SerializeField] private Vector2 liftBack = new(-1.5f, 8f);
    [Tooltip("The swing curve's second control point: forward offset from where the foot lands, and height.")]
    [SerializeField] private Vector2 liftFront = new(3f, 5.5f);

    [Header("Body")]
    [Tooltip("The pelvis's height, the bob's middle.")]
    [SerializeField, Min(1f)] private float pelvisHeight = 13.7f;
    [Tooltip("How far the pelvis bobs either way.")]
    [SerializeField, Min(0f)] private float bob = 0.9f;
    [Tooltip("The pelvis is highest mid-stance (a walk); otherwise lowest (a run).")]
    [SerializeField] private bool bobRisesMidStance;
    [Tooltip("Forward lean of the spine, in degrees.")]
    [SerializeField] private float lean = 10f;
    [Tooltip("How far the shoulders twist with the stride, in degrees.")]
    [SerializeField, Min(0f)] private float twist = 9f;

    [Header("Arms (rig units for 10.4-unit arms)")]
    [Tooltip("How far a hand swings forward and back of the shoulders.")]
    [SerializeField, Min(0f)] private float armSwing = 4.2f;
    [Tooltip("The hands' height below the shoulders (negative).")]
    [SerializeField] private float armHeight = -6.4f;
    [Tooltip("How far out from the shoulders the hands swing.")]
    [SerializeField] private float armOut = 1.2f;
    [Tooltip("How far a hand rises as it swings forward, per 4 units of swing: the pumping of a run.")]
    [SerializeField, Min(0f)] private float armPump = 1.2f;

    [Header("Cape")]
    [Tooltip("How far the cape flows back and up, 0 hanging to 1 streaming.")]
    [SerializeField, Range(0f, 1f)] private float capeFlow = 0.55f;

    public override int FrameCount => frames;
    public override CharacterGait Gait => gait;
    public float FramesPerSecond => framesPerSecond;

    /// <summary>
    /// One frame lasts a constant share of the cycle.
    /// </summary>
    public override float FrameSeconds(int frame)
    {
        return 1f / framesPerSecond;
    }

    /// <summary>
    /// The stride a planted foot slides back, from in front to behind, over the share of the cycle it is planted.
    /// </summary>
    public override float GroundSpeed(CharacterRecipe recipe)
    {
        float legScale = (recipe.Thigh + recipe.Shin) / TunedLegLength;
        float stanceSeconds = duty * frames / framesPerSecond;
        return (front - back) * legScale / stanceSeconds;
    }

    /// <summary>
    /// The pose a share <paramref name="frame"/> / <see cref="FrameCount"/> into the cycle.
    /// </summary>
    public override CharacterPose PoseAt(int frame, CharacterRecipe recipe, CharacterWeaponArt weapon)
    {
        return PoseAtPhase(frame / (float)frames, recipe, weapon);
    }

    /// <summary>
    /// The pose at <paramref name="phase"/> (0 to 1) into the cycle.
    /// </summary>
    public CharacterPose PoseAtPhase(float phase, CharacterRecipe recipe, CharacterWeaponArt weapon)
    {
        float legScale = (recipe.Thigh + recipe.Shin) / TunedLegLength;
        float armScale = (recipe.UpperArm + recipe.Forearm) / TunedArmLength;
        Vector2 left = FootAt(Mathf.Repeat(phase, 1f), legScale);
        Vector2 right = FootAt(Mathf.Repeat(phase + 0.5f, 1f), legScale);
        float wave = Mathf.Cos(2f * Mathf.PI * 2f * (phase - duty * 0.5f));
        float pelvis = recipe.Ankle + (pelvisHeight - recipe.Ankle) * legScale + (bobRisesMidStance ? 1f : -1f) * bob * legScale * wave;
        float swing = armSwing * armScale;
        float c = Mathf.Cos(2f * Mathf.PI * phase);
        float shoulderHeight = pelvis + recipe.Torso * Mathf.Cos(lean * Mathf.Deg2Rad) - 1.6f;
        float shoulderForward = recipe.Torso * Mathf.Sin(lean * Mathf.Deg2Rad);
        float handHeight = shoulderHeight + armHeight * armScale;
        float side = recipe.ShoulderHalfWidth + armOut;
        float pump = armPump * swing / 4f;
        CharacterPose pose = new()
        {
            PelvisHeight = pelvis,
            Lean = lean,
            Twist = -twist * c,
            LeftFoot = new Vector3(-recipe.HipHalfWidth, left.x, recipe.Ankle + left.y),
            RightFoot = new Vector3(recipe.HipHalfWidth, right.x, recipe.Ankle + right.y),
            LeftHand = new Vector3(-side, shoulderForward - swing * c, handHeight + pump * Mathf.Max(0f, -c)),
            RightHand = new Vector3(side, shoulderForward + swing * c, handHeight + pump * Mathf.Max(0f, c)),
            LeftPole = new Vector3(-0.2f, -1f, -0.5f),
            RightPole = new Vector3(0.2f, -1f, -0.5f),
            CapeFlow = capeFlow,
            CapePhase = phase
        };

        if (weapon != null)
        {
            weapon.Carry(pose, recipe, c, swing, shoulderHeight, shoulderForward);
        }

        return pose;
    }

    /// <summary>
    /// A foot's forward offset and height <paramref name="cycle"/> (0 to 1) into its own cycle: sliding back while
    /// planted, then along a cubic curve from behind to in front.
    /// </summary>
    private Vector2 FootAt(float cycle, float legScale)
    {
        if (cycle < duty)
        {
            return new Vector2(Mathf.Lerp(front, back, cycle / duty) * legScale, 0f);
        }

        float t = (cycle - duty) / (1f - duty);
        Vector2 p0 = new(back, 0f);
        Vector2 p1 = new(back + liftBack.x, liftBack.y);
        Vector2 p2 = new(front + liftFront.x, liftFront.y);
        Vector2 p3 = new(front, 0f);
        float u = 1f - t;
        return (u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3) * legScale;
    }
}
