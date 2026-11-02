using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// An editor-only cast drawn over a gait's legs (Docs/Animation.md, Phase 5): every frame of a keyed motion, the upper
/// body, over every frame of a gait, the legs, so a cast made while running keeps the stride. Frame
/// <c>g * K + f</c> is the keyed motion's frame f (of K) over gait frame g. The feet, the pelvis's height, the cape's
/// flow and ripple and a carried lantern's swing come from the gait; the shoulders' twist, the head, the hands, elbows and weapon from the keyed
/// motion, the hands kept where they were relative to the shoulders; the spine leans as the keyed motion plus a share
/// of the gait's lean, and the hips turn against the gait's shoulders, as the run's do. Frame durations, the release
/// frame, smears, effects and light are the keyed motion's; smears sample it over the frame's own gait frame.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/Characters/Over Gait Motion", fileName = "Motion_OverGait")]
public sealed class OverGaitMotion : CharacterMotion
{
    [Tooltip("The upper body: a keyed motion, usually a cast.")]
    [SerializeField] private KeyedMotion upper;
    [Tooltip("The legs: a gait, usually the run.")]
    [SerializeField] private GaitMotion gait;
    [Tooltip("The share of the gait's forward lean added to the keyed motion's.")]
    [SerializeField, Range(0f, 1f)] private float gaitLean = 0.5f;

    public KeyedMotion Upper => upper;
    public GaitMotion Legs => gait;
    public override int FrameCount => upper != null && gait != null ? upper.FrameCount * gait.FrameCount : 0;
    public override int GaitFrames => gait != null ? gait.FrameCount : 0;
    public override int ContactFrame => upper != null ? upper.ContactFrame : -1;
    public override int SmearSamples => upper != null ? upper.SmearSamples : base.SmearSamples;
    public override ColourRamp Light => upper != null ? upper.Light : default;

    /// <summary>
    /// The keyed motion's duration of the frame's own frame.
    /// </summary>
    public override float FrameSeconds(int frame)
    {
        return upper.FrameSeconds(frame % upper.FrameCount);
    }

    /// <summary>
    /// The keyed motion's pose of the frame over the frame's gait frame.
    /// </summary>
    public override CharacterPose PoseAt(int frame, CharacterRecipe recipe, CharacterWeaponArt weapon)
    {
        int frames = upper.FrameCount;
        return Combine(gait.PoseAt(frame / frames, recipe, null), upper.PoseAt(frame % frames, recipe, weapon), recipe, weapon, gaitLean);
    }

    /// <summary>
    /// The keyed motion's pose at <paramref name="time"/> over the gait's first frame.
    /// </summary>
    public override CharacterPose PoseAtTime(float time, CharacterRecipe recipe, CharacterWeaponArt weapon)
    {
        return Combine(gait.PoseAt(0, recipe, null), upper.PoseAtTime(time, recipe, weapon), recipe, weapon, gaitLean);
    }

    /// <summary>
    /// The keyed motion's pose at <paramref name="time"/> over <paramref name="frame"/>'s gait frame.
    /// </summary>
    public override CharacterPose SmearPoseAt(int frame, float time, CharacterRecipe recipe, CharacterWeaponArt weapon)
    {
        return Combine(gait.PoseAt(frame / upper.FrameCount, recipe, null), upper.PoseAtTime(time, recipe, weapon), recipe, weapon, gaitLean);
    }

    /// <summary>
    /// The keyed motion's smear of the frame's own frame.
    /// </summary>
    public override bool TryGetSmear(int frame, out MotionSmear smear)
    {
        return upper.TryGetSmear(frame % upper.FrameCount, out smear);
    }

    /// <summary>
    /// The keyed motion's effects on the frame's own frame.
    /// </summary>
    public override IReadOnlyList<MotionEffect> EffectsOn(int frame)
    {
        return upper.EffectsOn(frame % upper.FrameCount);
    }

    /// <summary>
    /// <paramref name="upperPose"/> over <paramref name="legs"/>: the legs' feet, pelvis height, cape and lantern swing, the upper
    /// pose's twist, head, arms and weapon with its hands moved as far as its shoulders moved, its lean plus
    /// <paramref name="leanShare"/> of the legs', and the hips turned against the legs' shoulders.
    /// </summary>
    public static CharacterPose Combine(CharacterPose legs, CharacterPose upperPose, CharacterRecipe recipe, CharacterWeaponArt weapon, float leanShare)
    {
        CharacterPose pose = new()
        {
            PelvisHeight = legs.PelvisHeight,
            Lean = upperPose.Lean + leanShare * legs.Lean,
            Twist = upperPose.Twist,
            HipTwist = -0.35f * legs.Twist,
            HeadTurn = upperPose.HeadTurn,
            LeftFoot = legs.LeftFoot,
            RightFoot = legs.RightFoot,
            LeftPole = upperPose.LeftPole,
            RightPole = upperPose.RightPole,
            Blade = upperPose.Blade,
            BowUp = upperPose.BowUp,
            BowAim = upperPose.BowAim,
            Draw = upperPose.Draw,
            Arrow = upperPose.Arrow,
            Kick = upperPose.Kick,
            CapeFlow = legs.CapeFlow,
            CapePhase = legs.CapePhase,
            LanternSwing = legs.LanternSwing
        };

        Vector3 moved = ShoulderMiddle(recipe, pose, weapon) - ShoulderMiddle(recipe, upperPose, weapon);
        pose.LeftHand = upperPose.LeftHand + moved;
        pose.RightHand = upperPose.RightHand + moved;
        return pose;
    }

    /// <summary>
    /// The point between <paramref name="pose"/>'s shoulders once solved.
    /// </summary>
    private static Vector3 ShoulderMiddle(CharacterRecipe recipe, CharacterPose pose, CharacterWeaponArt weapon)
    {
        CharacterJoints joints = CharacterRig.Solve(recipe, pose, weapon);
        return (joints.LeftShoulder + joints.RightShoulder) * 0.5f;
    }
}
