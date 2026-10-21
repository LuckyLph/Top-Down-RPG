using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// An editor-only motion a <see cref="CharacterRecipe"/> bakes into a clip: a pose for each of its frames, and for a
/// keyed motion the smears and effects drawn on some of them. Poses are in rig units tuned for the default body and
/// scaled to the recipe's where it matters (a gait's strides follow the legs' length).
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
    /// The effects drawn on <paramref name="frame"/>.
    /// </summary>
    public virtual IReadOnlyList<MotionEffect> EffectsOn(int frame)
    {
        return NoEffects;
    }
}
