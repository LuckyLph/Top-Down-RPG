using UnityEngine;

/// <summary>
/// The joints of a solved <see cref="CharacterPose"/>, in the character's own space (x right, y forward, z up), with
/// the axes the spine, shoulders and hips settled on.
/// </summary>
public sealed class CharacterJoints
{
    public Vector3 Pelvis { get; set; }
    public Vector3 Neck { get; set; }
    public Vector3 Head { get; set; }
    public Vector3 Spine { get; set; }
    public Vector3 ShoulderAxis { get; set; }
    public Vector3 HipAxis { get; set; }
    public Vector3 TorsoForward { get; set; }
    public Vector3 HipForward { get; set; }
    public Vector3 LeftShoulder { get; set; }
    public Vector3 RightShoulder { get; set; }
    public Vector3 LeftElbow { get; set; }
    public Vector3 RightElbow { get; set; }
    public Vector3 LeftHand { get; set; }
    public Vector3 RightHand { get; set; }
    public Vector3 LeftHip { get; set; }
    public Vector3 RightHip { get; set; }
    public Vector3 LeftKnee { get; set; }
    public Vector3 RightKnee { get; set; }
    public Vector3 LeftAnkle { get; set; }
    public Vector3 RightAnkle { get; set; }
    public float HeadTurn { get; set; }
}

/// <summary>
/// The character's skeleton: turns a <see cref="CharacterPose"/> into joints with the body sizes of a
/// <see cref="CharacterRecipe"/>. The spine leans and twists from the pelvis; legs and arms are two-bone chains that
/// bend toward their poles to reach their targets, straightening when a target is out of reach. A two-handed blade puts
/// the left hand on the grip below the right.
/// </summary>
public static class CharacterRig
{
    private const float ShoulderDrop = 1.6f;
    private const float CounterHipShare = -0.35f;

    /// <summary>
    /// Solves <paramref name="pose"/> for <paramref name="recipe"/>'s body, holding <paramref name="weapon"/> (or none).
    /// </summary>
    public static CharacterJoints Solve(CharacterRecipe recipe, CharacterPose pose, CharacterWeaponArt weapon)
    {
        Vector3 up = new(0f, 0f, 1f);
        Vector3 pelvis = new(0f, 0f, pose.PelvisHeight);
        float lean = pose.Lean * Mathf.Deg2Rad;
        float twist = pose.Twist * Mathf.Deg2Rad;
        float hipTwist = (pose.HipTwist ?? CounterHipShare * pose.Twist) * Mathf.Deg2Rad;
        Vector3 spine = new(0f, Mathf.Sin(lean), Mathf.Cos(lean));
        Vector3 neck = pelvis + spine * recipe.Torso;
        Vector3 shoulderAxis = new(Mathf.Cos(twist), -Mathf.Sin(twist), 0f);
        Vector3 hipAxis = new(Mathf.Cos(hipTwist), -Mathf.Sin(hipTwist), 0f);
        CharacterJoints joints = new()
        {
            Pelvis = pelvis,
            Neck = neck,
            Spine = spine,
            ShoulderAxis = shoulderAxis,
            HipAxis = hipAxis,
            TorsoForward = Vector3.Cross(spine, shoulderAxis).normalized,
            HipForward = Vector3.Cross(up, hipAxis).normalized,
            Head = neck + (spine + new Vector3(0f, 0f, 1.2f)).normalized * recipe.NeckToHead,
            LeftShoulder = neck - spine * ShoulderDrop - shoulderAxis * recipe.ShoulderHalfWidth,
            RightShoulder = neck - spine * ShoulderDrop + shoulderAxis * recipe.ShoulderHalfWidth,
            LeftHip = pelvis - hipAxis * recipe.HipHalfWidth,
            RightHip = pelvis + hipAxis * recipe.HipHalfWidth,
            HeadTurn = pose.HeadTurn
        };

        Vector3 kneePole = new(0f, 1f, 0.15f);
        Reach(joints.LeftHip, pose.LeftFoot, recipe.Thigh, recipe.Shin, kneePole, out Vector3 leftKnee, out Vector3 leftAnkle);
        Reach(joints.RightHip, pose.RightFoot, recipe.Thigh, recipe.Shin, kneePole, out Vector3 rightKnee, out Vector3 rightAnkle);
        joints.LeftKnee = leftKnee;
        joints.LeftAnkle = leftAnkle;
        joints.RightKnee = rightKnee;
        joints.RightAnkle = rightAnkle;

        Reach(joints.RightShoulder, pose.RightHand, recipe.UpperArm, recipe.Forearm, pose.RightPole, out Vector3 rightElbow, out Vector3 rightHand);
        joints.RightElbow = rightElbow;
        joints.RightHand = rightHand;
        Vector3 leftTarget = pose.LeftHand;
        if (weapon != null && weapon.Kind == WeaponKind.Blade && weapon.TwoHanded)
        {
            leftTarget = rightHand - pose.Blade.normalized * weapon.LeftGripOffset;
        }

        Reach(joints.LeftShoulder, leftTarget, recipe.UpperArm, recipe.Forearm, pose.LeftPole, out Vector3 leftElbow, out Vector3 leftHand);
        joints.LeftElbow = leftElbow;
        joints.LeftHand = leftHand;
        return joints;
    }

    /// <summary>
    /// A plain standing pose for <paramref name="recipe"/>'s body: feet under the hips, legs nearly straight, arms down
    /// at the sides. The baker measures the character's height on it.
    /// </summary>
    public static CharacterPose StandingPose(CharacterRecipe recipe)
    {
        float legs = recipe.Thigh + recipe.Shin;
        float pelvis = recipe.Ankle + legs * 0.95f;
        float shoulders = pelvis + recipe.Torso - ShoulderDrop;
        float hands = shoulders - (recipe.UpperArm + recipe.Forearm) * 0.9f;
        float side = recipe.ShoulderHalfWidth + 1f;
        return new CharacterPose
        {
            PelvisHeight = pelvis,
            LeftFoot = new Vector3(-recipe.HipHalfWidth, 0f, recipe.Ankle),
            RightFoot = new Vector3(recipe.HipHalfWidth, 0f, recipe.Ankle),
            LeftHand = new Vector3(-side, 0f, hands),
            RightHand = new Vector3(side, 0f, hands),
            LeftPole = new Vector3(-0.2f, -1f, -0.5f),
            RightPole = new Vector3(0.2f, -1f, -0.5f)
        };
    }

    /// <summary>
    /// A two-bone chain from <paramref name="root"/> toward <paramref name="target"/> with bones of
    /// <paramref name="first"/> and <paramref name="second"/>, its middle joint bent toward <paramref name="pole"/>. A
    /// target beyond reach straightens the chain toward it; one too near folds it as far as it goes.
    /// </summary>
    public static void Reach(Vector3 root, Vector3 target, float first, float second, Vector3 pole, out Vector3 middle, out Vector3 end)
    {
        Vector3 toTarget = target - root;
        float distance = toTarget.magnitude;
        Vector3 direction = distance > 1e-6f ? toTarget / distance : new Vector3(0f, 0f, -1f);
        distance = Mathf.Clamp(distance, Mathf.Abs(first - second) + 1e-3f, first + second - 1e-3f);
        float along = (first * first + distance * distance - second * second) / (2f * distance);
        float across = Mathf.Sqrt(Mathf.Max(first * first - along * along, 0f));
        Vector3 bend = pole - direction * Vector3.Dot(pole, direction);
        bend = bend.sqrMagnitude > 1e-12f ? bend.normalized : Vector3.Cross(direction, Vector3.right).normalized;
        middle = root + direction * along + bend * across;
        end = root + direction * distance;
    }
}
