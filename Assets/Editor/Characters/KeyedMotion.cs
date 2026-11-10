using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// An editor-only keyed motion, such as an attack or a cast: poses at times from 0 to 1, each eased into the next,
/// sampled at the frames' times. A frame can carry a smear (the area the blade's outer part swept over a span of motion
/// time, sampled <see cref="smearSamples"/> times) and effects, among them a cast's light in the motion's light colours.
/// The contact frame is when the attack lands or the cast releases; the runtime pins it to the moment the hit resolves
/// or the cast completes. Frame durations pace the inspector's preview and keep each phase's proportions: the runtime
/// stretches each phase to its timeline (a channelling cast plays its gather at their pace). The keys are posed on one
/// body (<see cref="PosedOn"/>); baked on another, each pose is fitted to it (<see cref="FitToBody"/>). An idle
/// animation (<see cref="IsIdleAnimation"/>) plays at its frames' durations, with no contact frame. Its Style settings,
/// off by default, have the head and the hair's tips follow the head's own rise and fall late, and a key can stretch or
/// squash the torso and spin the whole character.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/Characters/Keyed Motion", fileName = "Motion_Keyed")]
public sealed class KeyedMotion : CharacterMotion
{
    [Tooltip("The poses, in order of time from 0 to 1. Each key's easing leads to the next one.")]
    [SerializeField] private List<MotionKey> keys = new();
    [Tooltip("The motion time of each frame, from 0 to 1, in order.")]
    [SerializeField] private float[] frameTimes = Array.Empty<float>();
    [Tooltip("How long each frame shows in the inspector's preview, in milliseconds.")]
    [SerializeField] private int[] frameMilliseconds = Array.Empty<int>();
    [Tooltip("The frame the attack lands on, or the cast releases on.")]
    [SerializeField, Min(0)] private int contactFrame;
    [Tooltip("The hips turn by each key's hip twist; otherwise they turn a third against the shoulders.")]
    [SerializeField] private bool hipsFollowKeys;
    [Tooltip("How far the cape flows back and up, 0 hanging to 1 streaming.")]
    [SerializeField, Range(0f, 1f)] private float capeFlow = 0.15f;
    [Tooltip("Smears: on a frame, the area the blade's outer part swept over a span of motion time.")]
    [SerializeField] private List<MotionSmear> smears = new();
    [Tooltip("How many poses a smear samples across its span.")]
    [SerializeField, Range(2, 64)] private int smearSamples = 24;
    [Tooltip("Effects drawn on frames: dust where the blade's tip meets the ground, an arrow's release, a cast's light.")]
    [SerializeField] private List<MotionEffect> effects = new();
    [Tooltip("The colours of a cast's light, darkest first: its outline, rim, glow and core.")]
    [SerializeField] private ColourRamp light;
    [Tooltip("The body the keys were posed on. Baked on a body of other sizes, each pose is fitted to it: the pelvis and the feet scaled by the legs' length, the hands kept where they were from the shoulders, scaled by the arms' length. None uses the keys as they are.")]
    [SerializeField] private CharacterRecipe posedOn;
    [Tooltip("An idle animation: plays once on its own after the character has stood still a while, at its frames' durations, without a contact frame. Its first and last poses should be the idle's.")]
    [SerializeField] private bool idleAnimation;

    [Header("Style")]
    [Tooltip("How much of the head's own rise and fall it follows late, 0 none to 1 all of it.")]
    [SerializeField, Range(0f, 1f)] private float headFollow;
    [Tooltip("How late the head follows, as a share of the motion's time.")]
    [SerializeField, Range(0f, 0.25f)] private float headLag = 0.05f;
    [Tooltip("How much of the head's rise and fall the hair's tips follow late, per head radius of lock.")]
    [SerializeField, Range(0f, 2f)] private float hairFollow;
    [Tooltip("How late the hair follows, as a share of the motion's time.")]
    [SerializeField, Range(0f, 0.25f)] private float hairLag = 0.08f;

    private readonly List<MotionEffect> frameEffects = new();

    public override int FrameCount => frameTimes.Length;
    public override int ContactFrame => idleAnimation ? -1 : contactFrame;
    public override bool IsIdleAnimation => idleAnimation;
    public override int SmearSamples => smearSamples;
    public override ColourRamp Light => light;
    public CharacterRecipe PosedOn => posedOn;

    /// <summary>
    /// The preview duration of <paramref name="frame"/>, 0.1 s when none is set.
    /// </summary>
    public override float FrameSeconds(int frame)
    {
        return frame < frameMilliseconds.Length ? frameMilliseconds[frame] / 1000f : 0.1f;
    }

    /// <summary>
    /// The pose at frame <paramref name="frame"/>'s time.
    /// </summary>
    public override CharacterPose PoseAt(int frame, CharacterRecipe recipe, CharacterWeaponArt weapon)
    {
        return PoseAtTime(frameTimes[frame], recipe, weapon);
    }

    /// <summary>
    /// The pose at <paramref name="time"/>: between the two keys around it, eased by the earlier one's easing, fitted to
    /// the body; with a style, the head and the hair's tips follow the head's rise and fall late.
    /// </summary>
    public override CharacterPose PoseAtTime(float time, CharacterRecipe recipe, CharacterWeaponArt weapon)
    {
        CharacterPose pose = KeyedPose(time, recipe, weapon);
        if (headFollow <= 0f && hairFollow <= 0f)
        {
            return pose;
        }

        float head = CharacterRig.Solve(recipe, pose, weapon).Head.z;
        pose.HeadLift = headFollow * (HeadHeight(time - headLag, recipe, weapon) - head);
        pose.HairLift = hairFollow * (HeadHeight(time - hairLag, recipe, weapon) - head);
        return pose;
    }

    /// <summary>
    /// The height of the head's middle at <paramref name="time"/> (the first key's before the motion starts), without
    /// the style.
    /// </summary>
    private float HeadHeight(float time, CharacterRecipe recipe, CharacterWeaponArt weapon)
    {
        return CharacterRig.Solve(recipe, KeyedPose(Mathf.Max(0f, time), recipe, weapon), weapon).Head.z;
    }

    /// <summary>
    /// The keyed pose at <paramref name="time"/>, fitted to the body: between the two keys around it, eased by the
    /// earlier one's easing.
    /// </summary>
    private CharacterPose KeyedPose(float time, CharacterRecipe recipe, CharacterWeaponArt weapon)
    {
        if (keys.Count == 0)
        {
            return CharacterRig.StandingPose(recipe);
        }

        MotionKey from = keys[0];
        MotionKey to = keys[0];
        float share = 0f;
        for (int i = 0; i + 1 < keys.Count; i++)
        {
            if (time >= keys[i].Time && time <= keys[i + 1].Time)
            {
                from = keys[i];
                to = keys[i + 1];
                float span = to.Time - from.Time;
                share = span > 0f ? Ease(from.Easing, (time - from.Time) / span) : 1f;
                break;
            }

            if (i + 2 == keys.Count)
            {
                from = to = keys[i + 1];
            }
        }

        return FitToBody(MotionKey.Blend(from, to, share, hipsFollowKeys, capeFlow), posedOn, recipe, weapon);
    }

    /// <summary>
    /// Fits <paramref name="pose"/>, posed on <paramref name="posed"/>'s body, to <paramref name="recipe"/>'s in place
    /// and returns it: the pelvis's height over the ankles and each foot's place from its hip (its height from the
    /// ankle's) scaled by the legs' length, and each hand kept where it was from its shoulder, scaled by the arms'
    /// length, so the feet stay on the ground and the hands where the body can reach. The pose is left as it is without
    /// a body it was posed on, or on a body of the same sizes.
    /// </summary>
    public static CharacterPose FitToBody(CharacterPose pose, CharacterRecipe posed, CharacterRecipe recipe, CharacterWeaponArt weapon)
    {
        if (posed == null || recipe == null || SameBody(posed, recipe))
        {
            return pose;
        }

        float legScale = (recipe.Thigh + recipe.Shin) / (posed.Thigh + posed.Shin);
        float armScale = (recipe.UpperArm + recipe.Forearm) / (posed.UpperArm + posed.Forearm);
        CharacterJoints before = CharacterRig.Solve(posed, pose, weapon);
        pose.PelvisHeight = recipe.Ankle + (pose.PelvisHeight - posed.Ankle) * legScale;
        CharacterJoints after = CharacterRig.Solve(recipe, pose, weapon);
        pose.LeftFoot = FitFoot(pose.LeftFoot, before.LeftHip, after.LeftHip, posed.Ankle, recipe.Ankle, legScale);
        pose.RightFoot = FitFoot(pose.RightFoot, before.RightHip, after.RightHip, posed.Ankle, recipe.Ankle, legScale);
        pose.LeftHand = after.LeftShoulder + (pose.LeftHand - before.LeftShoulder) * armScale;
        pose.RightHand = after.RightShoulder + (pose.RightHand - before.RightShoulder) * armScale;
        return pose;
    }

    /// <summary>
    /// Whether <paramref name="a"/> and <paramref name="b"/> have the same legs, arms, torso, ankles, shoulders and
    /// hips, which are all a pose's fitting reads.
    /// </summary>
    private static bool SameBody(CharacterRecipe a, CharacterRecipe b)
    {
        return a.Thigh == b.Thigh && a.Shin == b.Shin && a.Ankle == b.Ankle && a.UpperArm == b.UpperArm && a.Forearm == b.Forearm
            && a.Torso == b.Torso && a.ShoulderHalfWidth == b.ShoulderHalfWidth && a.HipHalfWidth == b.HipHalfWidth;
    }

    /// <summary>
    /// <paramref name="foot"/>, placed from a hip at <paramref name="hipBefore"/> over ankles
    /// <paramref name="ankleBefore"/> high, placed as far from <paramref name="hipAfter"/> over ankles
    /// <paramref name="ankleAfter"/> high, times <paramref name="scale"/>.
    /// </summary>
    private static Vector3 FitFoot(Vector3 foot, Vector3 hipBefore, Vector3 hipAfter, float ankleBefore, float ankleAfter, float scale)
    {
        return new Vector3(hipAfter.x + (foot.x - hipBefore.x) * scale, hipAfter.y + (foot.y - hipBefore.y) * scale, ankleAfter + (foot.z - ankleBefore) * scale);
    }

    /// <summary>
    /// The smear listed for <paramref name="frame"/>, when there is one.
    /// </summary>
    public override bool TryGetSmear(int frame, out MotionSmear smear)
    {
        foreach (MotionSmear candidate in smears)
        {
            if (candidate.Frame == frame)
            {
                smear = candidate;
                return true;
            }
        }

        smear = default;
        return false;
    }

    /// <summary>
    /// The effects listed for <paramref name="frame"/>, in their order.
    /// </summary>
    public override IReadOnlyList<MotionEffect> EffectsOn(int frame)
    {
        frameEffects.Clear();
        foreach (MotionEffect effect in effects)
        {
            if (effect.Frame == frame)
            {
                frameEffects.Add(effect);
            }
        }

        return frameEffects;
    }

    /// <summary>
    /// <paramref name="t"/> (0 to 1) eased by <paramref name="easing"/>.
    /// </summary>
    public static float Ease(MotionEasing easing, float t)
    {
        switch (easing)
        {
            case MotionEasing.InOut:
                t = Mathf.Clamp01(t);
                return t * t * (3f - 2f * t);
            case MotionEasing.Out:
                return 1f - (1f - t) * (1f - t);
            case MotionEasing.In:
                return t * t;
            case MotionEasing.Step:
                return t >= 1f ? 1f : 0f;
            default:
                return t;
        }
    }
}

/// <summary>
/// One key of a <see cref="KeyedMotion"/>: its time, the easing to the next key and the pose there (see
/// <see cref="CharacterPose"/>; positions in rig units for the default body).
/// </summary>
[Serializable]
public sealed class MotionKey
{
    [SerializeField, Range(0f, 1f)] private float time;
    [SerializeField] private MotionEasing easing;
    [SerializeField] private float pelvisHeight = 14f;
    [SerializeField] private float lean;
    [SerializeField] private float twist;
    [SerializeField] private float hipTwist;
    [SerializeField] private float headTurn;
    [SerializeField] private Vector3 leftFoot = new(-3f, 2.4f, 1.5f);
    [SerializeField] private Vector3 rightFoot = new(3.2f, -2.4f, 1.5f);
    [SerializeField] private Vector3 leftHand = new(-5.6f, 1.6f, 14.6f);
    [SerializeField] private Vector3 rightHand = new(5f, 4.2f, 15.5f);
    [SerializeField] private Vector3 leftPole = new(-0.3f, -1f, -0.5f);
    [SerializeField] private Vector3 rightPole = new(0.6f, -0.6f, -0.6f);
    [Tooltip("The blade's direction from the right hand.")]
    [SerializeField] private Vector3 blade = new(0f, 1f, 0f);
    [Tooltip("The bow's axis toward its upper tip.")]
    [SerializeField] private Vector3 bowUp = new(0f, 0f, 1f);
    [Tooltip("Where the bow shoots.")]
    [SerializeField] private Vector3 bowAim = new(0f, 1f, 0f);
    [Tooltip("How far the string is drawn, 0 to 1.")]
    [SerializeField] private float draw;
    [Tooltip("An arrow is nocked above 0.5.")]
    [SerializeField] private float arrow;
    [Tooltip("How far the string snaps past rest after a release, 0 to 1.")]
    [SerializeField] private float kick;
    [Tooltip("The share the torso stretches by; negative squashes it.")]
    [SerializeField] private float stretch;
    [Tooltip("How far the face tips back, chin up, in degrees.")]
    [SerializeField] private float headTilt;
    [Tooltip("The eyes are shut above 0.5.")]
    [SerializeField, Range(0f, 1f)] private float eyesClosed;
    [Tooltip("How many rows the mouth opens below its line.")]
    [SerializeField, Min(0f)] private float mouthOpen;
    [Tooltip("How far the whole character is turned from the way it faces, in degrees counterclockwise seen from above; 360 is a whole spin.")]
    [SerializeField] private float spin;

    public float Time => time;
    public MotionEasing Easing => easing;

    /// <summary>
    /// The pose <paramref name="share"/> of the way from <paramref name="a"/> to <paramref name="b"/>: every value
    /// interpolated, the blade's and bow's directions kept unit length.
    /// </summary>
    public static CharacterPose Blend(MotionKey a, MotionKey b, float share, bool hipsFollowKeys, float capeFlow)
    {
        return new CharacterPose
        {
            PelvisHeight = Mathf.LerpUnclamped(a.pelvisHeight, b.pelvisHeight, share),
            Lean = Mathf.LerpUnclamped(a.lean, b.lean, share),
            Twist = Mathf.LerpUnclamped(a.twist, b.twist, share),
            HipTwist = hipsFollowKeys ? Mathf.LerpUnclamped(a.hipTwist, b.hipTwist, share) : null,
            HeadTurn = Mathf.LerpUnclamped(a.headTurn, b.headTurn, share),
            LeftFoot = Vector3.LerpUnclamped(a.leftFoot, b.leftFoot, share),
            RightFoot = Vector3.LerpUnclamped(a.rightFoot, b.rightFoot, share),
            LeftHand = Vector3.LerpUnclamped(a.leftHand, b.leftHand, share),
            RightHand = Vector3.LerpUnclamped(a.rightHand, b.rightHand, share),
            LeftPole = Vector3.LerpUnclamped(a.leftPole, b.leftPole, share),
            RightPole = Vector3.LerpUnclamped(a.rightPole, b.rightPole, share),
            Blade = Vector3.LerpUnclamped(a.blade, b.blade, share).normalized,
            BowUp = Vector3.LerpUnclamped(a.bowUp, b.bowUp, share).normalized,
            BowAim = Vector3.LerpUnclamped(a.bowAim, b.bowAim, share).normalized,
            Draw = Mathf.LerpUnclamped(a.draw, b.draw, share),
            Arrow = Mathf.LerpUnclamped(a.arrow, b.arrow, share),
            Kick = Mathf.LerpUnclamped(a.kick, b.kick, share),
            Stretch = Mathf.LerpUnclamped(a.stretch, b.stretch, share),
            HeadTilt = Mathf.LerpUnclamped(a.headTilt, b.headTilt, share),
            EyesClosed = Mathf.LerpUnclamped(a.eyesClosed, b.eyesClosed, share),
            MouthOpen = Mathf.LerpUnclamped(a.mouthOpen, b.mouthOpen, share),
            Spin = Mathf.LerpUnclamped(a.spin, b.spin, share),
            CapeFlow = capeFlow
        };
    }
}
