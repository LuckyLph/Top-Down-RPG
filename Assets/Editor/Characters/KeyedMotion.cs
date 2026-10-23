using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// An editor-only keyed motion, such as an attack or a cast: poses at times from 0 to 1, each eased into the next,
/// sampled at the frames' times. A frame can carry a smear (the area the blade's outer part swept over a span of motion
/// time, sampled <see cref="smearSamples"/> times) and effects, among them a cast's light in the motion's light colours.
/// The contact frame is when the attack lands or the cast releases; the runtime pins it to the moment the hit resolves
/// or the cast completes. Frame durations pace the inspector's preview and keep each phase's proportions: the runtime
/// stretches each phase to its timeline (a channelling cast plays its gather at their pace).
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

    private readonly List<MotionEffect> frameEffects = new();

    public override int FrameCount => frameTimes.Length;
    public override int ContactFrame => contactFrame;
    public override int SmearSamples => smearSamples;
    public override ColourRamp Light => light;

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
    /// The pose at <paramref name="time"/>: between the two keys around it, eased by the earlier one's easing.
    /// </summary>
    public override CharacterPose PoseAtTime(float time, CharacterRecipe recipe, CharacterWeaponArt weapon)
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

        return MotionKey.Blend(from, to, share, hipsFollowKeys, capeFlow);
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
            CapeFlow = capeFlow
        };
    }
}
