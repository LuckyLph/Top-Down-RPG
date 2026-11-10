using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// An editor-only motion a <see cref="CharacterRecipe"/> bakes into a clip: a pose for each of its frames, and for a
/// keyed motion the smears and effects drawn on some of them and the colours of a cast's light. Poses are in rig units
/// tuned for the default body and scaled to the recipe's where it matters (a gait's strides follow the legs' length). A
/// motion drawn over a gait (<see cref="GaitFrames"/>) has each of its own frames over each gait frame, gait-major.
/// </summary>
public abstract class CharacterMotion : ScriptableObject
{
    private static readonly List<MotionEffect> NoEffects = new();

    /// <summary>
    /// How many frames the motion has.
    /// </summary>
    public abstract int FrameCount { get; }

    /// <summary>
    /// How many poses a smear samples across its span.
    /// </summary>
    public virtual int SmearSamples => 24;

    /// <summary>
    /// The locomotion the motion shows: none for an attack.
    /// </summary>
    public virtual CharacterGait Gait => CharacterGait.None;

    /// <summary>
    /// The frame an attack lands on or a cast releases on, which the runtime pins to the moment its hit resolves or
    /// the cast completes; -1 for a motion that is neither.
    /// </summary>
    public virtual int ContactFrame => -1;

    /// <summary>
    /// Whether the motion is an idle animation, played once on its own after the character has stood still a while
    /// (Docs/PlayerCharacter.md, Phase 3).
    /// </summary>
    public virtual bool IsIdleAnimation => false;

    /// <summary>
    /// How many frames of a gait the motion is drawn over: 0 for a motion drawn by itself. Frame
    /// <c>g * (FrameCount / GaitFrames) + f</c> is its frame f over gait frame g.
    /// </summary>
    public virtual int GaitFrames => 0;

    /// <summary>
    /// The colours of a cast's light, darkest first: its outline, rim, glow and core.
    /// </summary>
    public virtual ColourRamp Light => default;

    /// <summary>
    /// How fast a planted foot moves back under <paramref name="recipe"/>'s body at the motion's natural speed, in rig
    /// units per second: the speed the character must move at for its feet not to slide. 0 for a motion that does not
    /// travel.
    /// </summary>
    public virtual float GroundSpeed(CharacterRecipe recipe)
    {
        return 0f;
    }

    /// <summary>
    /// The pose of frame <paramref name="frame"/> for <paramref name="recipe"/>'s body holding
    /// <paramref name="weapon"/> (or none).
    /// </summary>
    public abstract CharacterPose PoseAt(int frame, CharacterRecipe recipe, CharacterWeaponArt weapon);

    /// <summary>
    /// How long frame <paramref name="frame"/> shows at the motion's natural speed, in seconds; the inspector's
    /// preview plays it so.
    /// </summary>
    public abstract float FrameSeconds(int frame);

    /// <summary>
    /// The smear drawn on <paramref name="frame"/>, when it has one.
    /// </summary>
    public virtual bool TryGetSmear(int frame, out MotionSmear smear)
    {
        smear = default;
        return false;
    }

    /// <summary>
    /// The pose at motion time <paramref name="time"/> (0 to 1), which smears sample between frames. Only keyed
    /// motions have one between their frames; this one gives the nearest frame's.
    /// </summary>
    public virtual CharacterPose PoseAtTime(float time, CharacterRecipe recipe, CharacterWeaponArt weapon)
    {
        return PoseAt(Mathf.Clamp(Mathf.RoundToInt(time * FrameCount), 0, FrameCount - 1), recipe, weapon);
    }

    /// <summary>
    /// The pose a smear on <paramref name="frame"/> samples at motion time <paramref name="time"/>: the pose at that
    /// time, which a motion drawn over a gait puts over the frame's gait frame.
    /// </summary>
    public virtual CharacterPose SmearPoseAt(int frame, float time, CharacterRecipe recipe, CharacterWeaponArt weapon)
    {
        return PoseAtTime(time, recipe, weapon);
    }

    /// <summary>
    /// The effects drawn on <paramref name="frame"/>.
    /// </summary>
    public virtual IReadOnlyList<MotionEffect> EffectsOn(int frame)
    {
        return NoEffects;
    }
}
