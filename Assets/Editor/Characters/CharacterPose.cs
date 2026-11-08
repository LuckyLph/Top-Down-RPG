using UnityEngine;

/// <summary>
/// One pose of the character rig, in its own space: x to its right, y forward, z up, in rig units, the origin between
/// its feet on the ground. Feet and hands are IK targets with a pole each for the knee or elbow; the spine leans
/// forward by <see cref="Lean"/> degrees and turns the shoulders by <see cref="Twist"/> (positive turns the right
/// shoulder back); the hips turn by <see cref="HipTwist"/>, or against the shoulders by a third when it is null. It also
/// carries the weapon's state, the cape's flow and phase and a carried lantern's swing. <see cref="CharacterRig.Solve"/>
/// turns it into joints.
/// </summary>
public sealed class CharacterPose
{
    public float PelvisHeight { get; set; } = 14.5f;
    public float Lean { get; set; }
    public float Twist { get; set; }
    public float? HipTwist { get; set; }
    public float HeadTurn { get; set; }
    public Vector3 LeftFoot { get; set; } = new(-2.5f, 0f, 1.5f);
    public Vector3 RightFoot { get; set; } = new(2.5f, 0f, 1.5f);
    public Vector3 LeftHand { get; set; } = new(-6f, 0f, 15f);
    public Vector3 RightHand { get; set; } = new(6f, 0f, 15f);
    public Vector3 LeftPole { get; set; } = new(-0.3f, -1f, -0.4f);
    public Vector3 RightPole { get; set; } = new(0.3f, -1f, -0.4f);

    /// <summary>
    /// The blade's direction from the right hand toward its tip.
    /// </summary>
    public Vector3 Blade { get; set; } = new(0f, 1f, 0f);

    /// <summary>
    /// The bow's axis from its grip toward its upper tip.
    /// </summary>
    public Vector3 BowUp { get; set; } = new(0f, 0f, 1f);

    /// <summary>
    /// Where the bow shoots; only its part across <see cref="BowUp"/> counts.
    /// </summary>
    public Vector3 BowAim { get; set; } = new(0f, 1f, 0f);

    /// <summary>
    /// How far the string is drawn, 0 at rest to 1 at the right hand.
    /// </summary>
    public float Draw { get; set; }

    /// <summary>
    /// An arrow is nocked above 0.5.
    /// </summary>
    public float Arrow { get; set; }

    /// <summary>
    /// How far the string snaps past rest after a release, 0 to 1.
    /// </summary>
    public float Kick { get; set; }

    /// <summary>
    /// How far the cape flows back and up, 0 hanging to 1 streaming.
    /// </summary>
    public float CapeFlow { get; set; } = 0.15f;

    /// <summary>
    /// Where the cape's ripple is, in strides (0 to 1).
    /// </summary>
    public float CapePhase { get; set; }

    /// <summary>
    /// How far a carried lantern swings with the stride, -1 to 1 of its swing: 0 standing and in keyed motions.
    /// </summary>
    public float LanternSwing { get; set; }

    /// <summary>
    /// The share the torso is stretched by (negative squashes it).
    /// </summary>
    public float Stretch { get; set; }

    /// <summary>
    /// How far the head rises from its place on the neck, in rig units: a gait's head lagging the body's bob.
    /// </summary>
    public float HeadLift { get; set; }

    /// <summary>
    /// How far the tips of the hair rise, in rig units for a lock one head radius long: the hair lagging the head.
    /// </summary>
    public float HairLift { get; set; }

    /// <summary>
    /// How far the face tips back, chin up, past the recipe's face tilt, in degrees.
    /// </summary>
    public float HeadTilt { get; set; }

    /// <summary>
    /// The eyes are shut above 0.5, each drawn as a line.
    /// </summary>
    public float EyesClosed { get; set; }

    /// <summary>
    /// How many rows the mouth opens below its line.
    /// </summary>
    public float MouthOpen { get; set; }
}
