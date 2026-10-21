using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One sample of a blade's sweep for a smear: the right hand and the blade's direction, in the character's space.
/// </summary>
public readonly struct BladeSample
{
    /// <summary>
    /// Creates a sample of the blade held at <paramref name="hand"/> pointing along <paramref name="direction"/>.
    /// </summary>
    public BladeSample(Vector3 hand, Vector3 direction)
    {
        Hand = hand;
        Direction = direction;
    }

    public Vector3 Hand { get; }
    public Vector3 Direction { get; }
}

/// <summary>
/// Draws a solved pose of the rogue onto a <see cref="CharacterCanvas"/>, facing a direction, in the order the depth
/// test needs no help with: legs, torso, belt and its sash, pouch and dagger, arms, mantle, cape (lined), the scarf
/// around the neck, then the weapon with its smear and effects, and last the hooded head, whose face is placed by
/// direction (<see cref="DrawHead"/>). Sizes come from the <see cref="CharacterRecipe"/> and the
/// <see cref="CharacterWeaponArt"/>; the mantle, belt, dagger and scarf are fixed parts of the rogue's outfit.
/// </summary>
public sealed class CharacterPainter
{
    private static readonly Vector3 Up = new(0f, 0f, 1f);

    private readonly CharacterRecipe recipe;
    private readonly CharacterWeaponArt weapon;
    private readonly CharacterCanvas canvas;
    private Vector3 forward;
    private Vector3 right;

    /// <summary>
    /// Creates a painter of <paramref name="recipe"/>'s character holding <paramref name="weaponArt"/> (or none) onto
    /// <paramref name="target"/>.
    /// </summary>
    public CharacterPainter(CharacterRecipe recipe, CharacterWeaponArt weaponArt, CharacterCanvas target)
    {
        this.recipe = recipe;
        weapon = weaponArt;
        canvas = target;
    }

    /// <summary>
    /// Draws <paramref name="pose"/>, solved as <paramref name="joints"/>, facing <paramref name="facingDegrees"/>
    /// (counterclockwise from east), with the blade's <paramref name="smear"/> samples (none for no smear, oldest first)
    /// and <paramref name="effects"/>.
    /// </summary>
    public void Paint(float facingDegrees, CharacterPose pose, CharacterJoints joints, IReadOnlyList<BladeSample> smear, IReadOnlyList<MotionEffect> effects)
    {
        float facing = facingDegrees * Mathf.Deg2Rad;
        forward = new Vector3(Mathf.Cos(facing), Mathf.Sin(facing), 0f);
        right = new Vector3(Mathf.Sin(facing), -Mathf.Cos(facing), 0f);
        DrawBody(pose, joints);
        if (weapon != null && weapon.Kind == WeaponKind.Blade)
        {
            if (smear != null && smear.Count > 1)
            {
                DrawSmear(smear);
            }

            DrawBlade(joints.RightHand, pose.Blade.normalized, joints.ShoulderAxis);
            foreach (MotionEffect effect in effects)
            {
                if (effect.Kind == MotionEffectKind.Dust)
                {
                    Vector3 tip = joints.RightHand + pose.Blade.normalized * weapon.Length;
                    DrawDust(new Vector3(tip.x, tip.y, 0f), effect.Size);
                }
            }
        }
        else if (weapon != null)
        {
            DrawBow(pose, joints, out Vector3 grip, out Vector3 aim, out Vector3 bowUp);
            foreach (MotionEffect effect in effects)
            {
                if (effect.Kind == MotionEffectKind.Release)
                {
                    DrawRelease(grip, aim, bowUp);
                }
            }
        }

        DrawHead(joints);
    }

    /// <summary>
    /// A point of the character's space in the world, for the current facing.
    /// </summary>
    private Vector3 World(Vector3 local)
    {
        return local.x * right + local.y * forward + local.z * Up;
    }

    /// <summary>
    /// Draws everything below the head but the weapon.
    /// </summary>
    private void DrawBody(CharacterPose pose, CharacterJoints j)
    {
        DrawLeg(j.LeftHip, j.LeftKnee, j.LeftAnkle, CharacterPart.LeftThigh, CharacterPart.LeftShin, CharacterPart.LeftFoot);
        DrawLeg(j.RightHip, j.RightKnee, j.RightAnkle, CharacterPart.RightThigh, CharacterPart.RightShin, CharacterPart.RightFoot);

        Vector3 sa = j.ShoulderAxis;
        Vector3 ha = j.HipAxis;
        Vector3 pelvis = j.Pelvis;
        Vector3 neck = j.Neck;
        Vector3 spine = j.Spine;
        Vector3 tf = j.TorsoForward;
        Vector3 hf = j.HipForward;
        float waist = recipe.WaistRadius;
        for (int side = -1; side <= 1; side += 2)
        {
            canvas.Capsule(World(pelvis + ha * (1.3f * side) + spine * 0.4f), World(neck - spine * 1.4f + sa * (1.8f * side)), waist, recipe.ChestRadius, CharacterMaterial.Jerkin, CharacterPart.Torso);
            canvas.Capsule(World(pelvis + ha * (1.4f * side) - spine * 0.2f), World(pelvis + ha * (1.7f * side) - spine * 1.4f), waist + 0.1f, waist + 0.2f, CharacterMaterial.Jerkin, CharacterPart.Torso);
            Vector3 beltStart = pelvis + ha * (1.4f * side) + spine * 0.9f;
            canvas.Capsule(World(beltStart), World(beltStart + spine * 1.1f), waist + 0.22f, waist + 0.22f, CharacterMaterial.Belt, CharacterPart.Belt);
        }

        canvas.Sphere(World(pelvis + spine * 1.45f + hf * (waist + 0.6f)), 0.75f, CharacterMaterial.Fittings, CharacterPart.Belt);
        Vector3 knot = pelvis + spine * 1.3f - ha * 2.5f + hf * 2f;
        canvas.Capsule(World(knot), World(knot - spine * 3.8f - ha * 0.5f + hf * 0.5f), 1f, 0.6f, CharacterMaterial.Sash, CharacterPart.Belt);
        canvas.Sphere(World(knot), 1.2f, CharacterMaterial.Sash, CharacterPart.Belt);
        Vector3 sheath = pelvis + spine * 1.1f - ha * 1.2f - hf * (waist + 0.5f);
        canvas.Capsule(World(sheath), World(sheath + ha * 5f - spine * 0.7f), 0.95f, 0.7f, CharacterMaterial.Gloves, CharacterPart.Belt);
        Vector3 hilt = sheath - ha * 2.3f + spine * 0.3f;
        canvas.Capsule(World(sheath), World(hilt), 0.55f, 0.55f, CharacterMaterial.DaggerGrip, CharacterPart.Belt, minRadius: 0.6f);
        canvas.Sphere(World(hilt), 0.75f, CharacterMaterial.Fittings, CharacterPart.Belt);

        DrawArm(j.LeftShoulder, j.LeftElbow, j.LeftHand, CharacterPart.LeftUpperArm, CharacterPart.LeftForearm, CharacterPart.LeftHand);
        DrawArm(j.RightShoulder, j.RightElbow, j.RightHand, CharacterPart.RightUpperArm, CharacterPart.RightForearm, CharacterPart.RightHand);

        Vector3 mantle = neck - spine * 1.1f;
        for (int i = 0; i < 28; i++)
        {
            float angle = 2f * Mathf.PI * i / 28f;
            float drop = 0.3f + 0.6f * Mathf.Max(Mathf.Sin(angle), 0f);
            Vector3 p = mantle + sa * (Mathf.Cos(angle) * recipe.ShoulderHalfWidth * 1.05f) + tf * (Mathf.Sin(angle) * 3.1f) - spine * drop;
            canvas.Sphere(World(p), 2.65f, CharacterMaterial.Cloak, CharacterPart.Mantle);
        }

        DrawCape(pose, j);

        for (int i = 0; i < 16; i++)
        {
            float angle = 2f * Mathf.PI * i / 16f;
            Vector3 p = neck + spine * 0.5f + sa * (Mathf.Cos(angle) * 2.2f) + tf * (Mathf.Sin(angle) * 2f);
            canvas.Sphere(World(p), 1.5f, CharacterMaterial.Sash, CharacterPart.Strap);
        }
    }

    /// <summary>
    /// Draws a leg: thigh and shin in trousers, a tall boot over the shin, and the foot pointing forward.
    /// </summary>
    private void DrawLeg(Vector3 hipLocal, Vector3 kneeLocal, Vector3 ankleLocal, CharacterPart thigh, CharacterPart shin, CharacterPart foot)
    {
        float limb = recipe.LimbThickness;
        float boot = recipe.BootSize;
        Vector3 hip = World(hipLocal);
        Vector3 knee = World(kneeLocal);
        Vector3 ankle = World(ankleLocal);
        canvas.Capsule(hip, knee, 2.1f * limb, 1.8f * limb, CharacterMaterial.Trousers, thigh);
        canvas.Capsule(knee, ankle, 1.7f * limb, 1.45f * limb, CharacterMaterial.Trousers, shin);
        canvas.Capsule(knee + (ankle - knee) * 0.1f, ankle, 2.05f * boot, 1.65f * boot, CharacterMaterial.Boots, shin);
        canvas.Capsule(ankle, ankle + forward * (2.4f * boot) - Up * 0.5f, 1.7f * boot, 1.4f * boot, CharacterMaterial.Boots, foot);
    }

    /// <summary>
    /// Draws an arm: the sleeve, a bracer over most of the forearm, and the gloved hand.
    /// </summary>
    private void DrawArm(Vector3 shoulderLocal, Vector3 elbowLocal, Vector3 handLocal, CharacterPart upper, CharacterPart fore, CharacterPart hand)
    {
        float limb = recipe.LimbThickness;
        Vector3 shoulder = World(shoulderLocal);
        Vector3 elbow = World(elbowLocal);
        Vector3 wrist = World(handLocal);
        Vector3 cuff = elbow + (wrist - elbow) * 0.2f;
        canvas.Capsule(shoulder, elbow, 1.7f * limb, 1.5f * limb, CharacterMaterial.Shirt, upper);
        canvas.Capsule(elbow, cuff, 1.45f * limb, 1.45f * limb, CharacterMaterial.Shirt, fore);
        canvas.Capsule(cuff, wrist, 1.6f * limb, 1.35f * limb, CharacterMaterial.Bracers, fore);
        canvas.Sphere(wrist, recipe.HandRadius, CharacterMaterial.Gloves, hand);
    }

    /// <summary>
    /// Draws the cape: strips from behind the shoulders to a hem that rises and streams back with the pose's flow and
    /// ripples with its phase; with a lining, each strip is an outer layer and a lining a little inside it whose hem
    /// hangs lower.
    /// </summary>
    private void DrawCape(CharacterPose pose, CharacterJoints j)
    {
        float flow = pose.CapeFlow;
        Vector3 back = -j.HipForward;
        int strips = recipe.CapeStrips;
        float hemHeight = recipe.Ankle + (pose.PelvisHeight - recipe.Ankle) * recipe.CapeHem;
        for (int i = 0; i < strips; i++)
        {
            float k = -1f + 2f * i / (strips - 1);
            Vector3 top = j.Neck - j.Spine * 2f + j.ShoulderAxis * (3.3f * k) - j.TorsoForward * 3.1f;
            float ripple = 0.9f * Mathf.Sin(2f * Mathf.PI * pose.CapePhase * 2f + i * 1.1f) * (0.3f + flow);
            float hem = hemHeight + flow * 5f + 0.6f * ripple;
            Vector3 bottom = new Vector3(top.x, top.y, hem) + back * (1.4f + flow * 7f + ripple) + j.HipAxis * (0.7f * k);
            Vector3 middle = (top + bottom) * 0.5f + back * (0.6f + 0.8f * flow) + Up * (0.4f * flow);
            if (!recipe.CapeLining)
            {
                canvas.Capsule(World(top), World(middle), 1.8f, 2.2f, CharacterMaterial.Cloak, CharacterPart.Cape);
                canvas.Capsule(World(middle), World(bottom), 2.2f, 2.2f, CharacterMaterial.Cloak, CharacterPart.Cape);
                continue;
            }

            Vector3 outward = (back + j.ShoulderAxis * (0.6f * k)).normalized * 0.6f;
            Vector3 hemDrop = new(0f, 0f, -1.7f);
            canvas.Capsule(World(top + outward), World(middle + outward), 1.8f, 2.2f, CharacterMaterial.Cloak, CharacterPart.Cape);
            canvas.Capsule(World(middle + outward), World(bottom + outward), 2.2f, 2.2f, CharacterMaterial.Cloak, CharacterPart.Cape);
            canvas.Capsule(World(top - outward), World(middle - outward), 1.7f, 2.2f * 0.92f, CharacterMaterial.Lining, CharacterPart.Cape);
            canvas.Capsule(World(middle - outward), World(bottom - outward + hemDrop), 2.2f * 0.92f, 2.2f * 0.92f, CharacterMaterial.Lining, CharacterPart.Cape);
        }
    }

    /// <summary>
    /// Draws a blade held at <paramref name="hand"/> along <paramref name="direction"/>: the grip down to a pommel,
    /// the crossguard across it (level when it can be, else along the shoulders) and the tapering blade.
    /// </summary>
    private void DrawBlade(Vector3 hand, Vector3 direction, Vector3 shoulderAxis)
    {
        Vector3 across = Vector3.Cross(direction, Up);
        across = across.magnitude < 0.2f ? shoulderAxis.normalized : across.normalized;
        Vector3 guard = hand + direction * weapon.GuardAt;
        canvas.Capsule(World(hand - direction * weapon.Pommel), World(guard), 0.6f, 0.6f, CharacterMaterial.Grip, CharacterPart.Weapon, minRadius: 0.6f);
        canvas.Sphere(World(hand - direction * (weapon.Pommel + 0.4f)), 0.8f, CharacterMaterial.Guard, CharacterPart.Weapon, 0.7f);
        canvas.Capsule(World(guard - across * weapon.GuardHalfWidth), World(guard + across * weapon.GuardHalfWidth), 0.65f, 0.65f, CharacterMaterial.Guard, CharacterPart.Weapon, minRadius: 0.6f);
        canvas.Capsule(World(guard + direction * 0.4f), World(hand + direction * weapon.Length), weapon.BladeRadius.x, weapon.BladeRadius.y, CharacterMaterial.Steel, CharacterPart.Weapon, minRadius: 0.62f);
    }

    /// <summary>
    /// Draws the area the blade's outer part swept through its samples, oldest first: a band from an inner share of
    /// the blade to just past its tip, narrowing toward the oldest sample, in three colours from the newest part to the
    /// oldest.
    /// </summary>
    private void DrawSmear(IReadOnlyList<BladeSample> samples)
    {
        int count = samples.Count;
        float start = weapon.GuardAt;
        for (int i = 0; i + 1 < count; i++)
        {
            float share = (i + 0.5f) / (count - 1);
            CharacterMaterial colour = share > 0.62f ? CharacterMaterial.SmearNew : share > 0.28f ? CharacterMaterial.SmearMiddle : CharacterMaterial.SmearOld;
            SmearEdge(samples, i, start, out Vector3 innerA, out Vector3 outerA);
            SmearEdge(samples, i + 1, start, out Vector3 innerB, out Vector3 outerB);
            canvas.Triangle(innerA, outerA, outerB, colour, CharacterPart.Smear);
            canvas.Triangle(innerA, outerB, innerB, colour, CharacterPart.Smear);
        }
    }

    /// <summary>
    /// The smear's inner and outer edge at sample <paramref name="index"/>, in the world.
    /// </summary>
    private void SmearEdge(IReadOnlyList<BladeSample> samples, int index, float start, out Vector3 inner, out Vector3 outer)
    {
        BladeSample sample = samples[index];
        float age = index / (float)(samples.Count - 1);
        float share = Mathf.Lerp(weapon.SmearInnerShare.x, weapon.SmearInnerShare.y, age);
        inner = World(sample.Hand + sample.Direction * (start + (weapon.Length - start) * share));
        outer = World(sample.Hand + sample.Direction * (weapon.Length + 0.3f));
    }

    /// <summary>
    /// Draws a puff of dust on the ground at <paramref name="point"/>, spreading with <paramref name="size"/>.
    /// </summary>
    private void DrawDust(Vector3 point, float size)
    {
        DustPuff(point, size, 0f, 0f, 1.9f);
        DustPuff(point, size, 2.2f, 0.9f, 1.5f);
        DustPuff(point, size, -2f, 1.2f, 1.4f);
        DustPuff(point, size, 0.8f, -1.8f, 1.3f);
        DustPuff(point, size, -1f, 2.4f, 1.1f);
        DustPuff(point, size, 2.8f, -1f, 1f);
    }

    /// <summary>
    /// One ball of a dust puff, offset from its middle by (<paramref name="x"/>, <paramref name="y"/>) times its size.
    /// </summary>
    private void DustPuff(Vector3 point, float size, float x, float y, float radius)
    {
        Vector3 p = point + new Vector3(x * size, y * size, 0.9f * size);
        canvas.Sphere(World(p), radius * (0.75f + 0.35f * size), CharacterMaterial.Dust, CharacterPart.Effect);
    }

    /// <summary>
    /// Draws the bow in the left hand: its limbs curling back further as it is drawn (their tips flicking forward a
    /// little), the grip, the string from the tips to the nock (pulled to the right hand by the draw, snapping past
    /// rest by the kick), and the nocked arrow with its head and fletching. Returns the grip, the aim and the bow's
    /// axis.
    /// </summary>
    private void DrawBow(CharacterPose pose, CharacterJoints j, out Vector3 grip, out Vector3 aim, out Vector3 bowUp)
    {
        grip = j.LeftHand;
        bowUp = pose.BowUp.normalized;
        aim = (pose.BowAim - bowUp * Vector3.Dot(pose.BowAim, bowUp)).normalized;
        float drawn = Mathf.Clamp01(pose.Draw);
        float bend = Mathf.Lerp(weapon.Bend.x, weapon.Bend.y, drawn);
        const int segments = 12;
        Vector3 previous = Vector3.zero;
        Vector3 top = Vector3.zero;
        Vector3 bottom = Vector3.zero;
        for (int i = 0; i <= segments; i++)
        {
            float u = -1f + 2f * i / segments;
            float curl = bend * u * u - 0.9f * Mathf.Max(Mathf.Abs(u) - 0.8f, 0f) / 0.2f * (pose.Draw < 0.5f ? 1f : 0.4f);
            Vector3 point = grip + bowUp * (weapon.HalfLength * u) - aim * curl;
            if (i == 0)
            {
                bottom = point;
            }
            else
            {
                float radius = 0.85f - 0.4f * Mathf.Abs(-1f + 2f * (i - 0.5f) / segments);
                canvas.Capsule(World(previous), World(point), radius, radius, CharacterMaterial.Wood, CharacterPart.Bow, minRadius: 0.62f);
            }

            previous = point;
            top = point;
        }

        canvas.Capsule(World(grip - bowUp * 1.2f), World(grip + bowUp * 1.2f), 0.95f, 0.95f, CharacterMaterial.Grip, CharacterPart.Bow, minRadius: 0.62f);
        Vector3 rest = grip - aim * weapon.Brace;
        Vector3 nock = rest + (j.RightHand - rest) * drawn + aim * (1.4f * pose.Kick);
        canvas.Capsule(World(top), World(nock), 0.25f, 0.25f, CharacterMaterial.BowString, CharacterPart.Bow, 0.25f, 0.5f);
        canvas.Capsule(World(nock), World(bottom), 0.25f, 0.25f, CharacterMaterial.BowString, CharacterPart.Bow, 0.25f, 0.5f);
        if (pose.Arrow <= 0.5f)
        {
            return;
        }

        Vector3 toGrip = grip - nock;
        Vector3 along = toGrip.magnitude > 1f ? toGrip.normalized : aim;
        Vector3 head = nock + along * weapon.ArrowLength;
        canvas.Capsule(World(nock), World(head), 0.3f, 0.3f, CharacterMaterial.Shaft, CharacterPart.Arrow, 0.25f, 0.55f);
        canvas.Capsule(World(head - along * 0.4f), World(head + along * 1.2f), 0.75f, 0.2f, CharacterMaterial.Steel, CharacterPart.Arrow, minRadius: 0.55f);
        Vector3 side = Vector3.Cross(along, bowUp).normalized;
        for (int k = -1; k <= 1; k += 2)
        {
            canvas.Capsule(World(nock + along * 0.6f), World(nock + along * 2.6f + side * (0.9f * k)), 0.35f, 0.35f, CharacterMaterial.Fletching, CharacterPart.Arrow, minRadius: 0.5f);
        }
    }

    /// <summary>
    /// Draws an arrow's release at the bow: a burst of four short rays around the grip and a streak along the aim with
    /// two shorter ones beside it.
    /// </summary>
    private void DrawRelease(Vector3 grip, Vector3 aim, Vector3 bowUp)
    {
        Vector3 side = Vector3.Cross(aim, bowUp).normalized;
        DrawRay(grip + aim * 0.8f + bowUp * 1.6f, grip + aim * 1.6f + bowUp * 4.2f, CharacterMaterial.SmearNew, 0.3f, 0.55f);
        DrawRay(grip + aim * 0.8f - bowUp * 1.6f, grip + aim * 1.6f - bowUp * 4.2f, CharacterMaterial.SmearNew, 0.3f, 0.55f);
        DrawRay(grip + aim * 0.8f + side * 1.6f, grip + aim * 1.6f + side * 3.8f, CharacterMaterial.SmearNew, 0.3f, 0.55f);
        DrawRay(grip + aim * 0.8f - side * 1.6f, grip + aim * 1.6f - side * 3.8f, CharacterMaterial.SmearNew, 0.3f, 0.55f);
        DrawRay(grip + aim * 1.5f, grip + aim * 17f, CharacterMaterial.SmearNew, 0.3f, 0.55f);
        for (int k = -1; k <= 1; k += 2)
        {
            DrawRay(grip + aim * 4f + bowUp * (1.6f * k), grip + aim * 12f + bowUp * (1.6f * k), CharacterMaterial.SmearMiddle, 0.25f, 0.5f);
        }
    }

    /// <summary>
    /// Draws a thin line of an effect from <paramref name="a"/> to <paramref name="b"/>.
    /// </summary>
    private void DrawRay(Vector3 a, Vector3 b, CharacterMaterial colour, float radius, float minRadius)
    {
        canvas.Capsule(World(a), World(b), radius, radius, colour, CharacterPart.Effect, 0.25f, minRadius);
    }

    /// <summary>
    /// Draws the hooded head and its face. The hood is a sphere with a point behind it. The face is laid out on the
    /// hood's front by angle from the face's middle (yaw across, pitch up), with the head tipped toward the camera by
    /// the face tilt: the opening is an ellipse of those angles, its outer share and top in the hood's shadow; inside
    /// it the bangs hang in points from above, the forehead is shaded, the mask rises to a point at the nose, and the
    /// eyes are painted at their angles, so the face lines up in every direction and turns with the head.
    /// </summary>
    private void DrawHead(CharacterJoints j)
    {
        float radius = recipe.HeadRadius;
        Vector3 centre = World(j.Head);
        float turn = j.HeadTurn * Mathf.Deg2Rad;
        Vector3 faceForward = Turn(forward, turn);
        Vector3 faceRight = Turn(right, turn);
        float tilt = recipe.FaceTilt * Mathf.Deg2Rad;
        Vector3 tiltedForward = (faceForward * Mathf.Cos(tilt) + Up * Mathf.Sin(tilt)).normalized;
        Vector3 tiltedUp = (Up * Mathf.Cos(tilt) - faceForward * Mathf.Sin(tilt)).normalized;
        Vector3 hood = centre + Up * (0.08f * radius) - tiltedForward * (0.08f * radius);
        float hoodRadius = radius * recipe.HoodSize;
        canvas.Sphere(hood, hoodRadius, CharacterMaterial.Cloak, CharacterPart.Head);
        canvas.Capsule(centre + tiltedUp * (0.6f * radius) - tiltedForward * (0.4f * radius), centre + Up * (recipe.HoodTip.y * radius) - tiltedForward * (recipe.HoodTip.x * radius), 0.45f * radius, 0.1f * radius, CharacterMaterial.Cloak, CharacterPart.Head);

        Vector3 opening = recipe.Opening;
        Vector4 bangs = recipe.Bangs;
        Vector3 mask = recipe.Mask;
        for (int y = 0; y < canvas.Height; y++)
        {
            for (int x = 0; x < canvas.Width; x++)
            {
                if (!canvas.IsFilled(x, y) || canvas.PartAt(x, y) != CharacterPart.Head)
                {
                    continue;
                }

                Vector3 n = canvas.NormalAt(x, y);
                Vector3 world = n.x * CharacterCanvas.ViewRight + n.y * CharacterCanvas.ViewUp + n.z * CharacterCanvas.ViewForward;
                float yaw = Mathf.Atan2(Vector3.Dot(world, faceRight), Vector3.Dot(world, tiltedForward)) * Mathf.Rad2Deg;
                float pitch = Mathf.Asin(Mathf.Clamp(Vector3.Dot(world, tiltedUp), -1f, 1f)) * Mathf.Rad2Deg;
                float across = yaw / opening.x;
                float upward = (pitch - opening.y) / opening.z;
                float ellipse = across * across + upward * upward;
                if (ellipse >= 1f)
                {
                    canvas.SetMaterial(x, y, CharacterMaterial.Cloak);
                    continue;
                }

                if (ellipse > 1f - recipe.RimShare || pitch > recipe.RimTop)
                {
                    canvas.SetMaterial(x, y, CharacterMaterial.HoodInside);
                    continue;
                }

                CharacterMaterial skin = pitch > recipe.ForeheadShade ? CharacterMaterial.FaceShade : CharacterMaterial.Face;
                float zigzag = Mathf.Abs(Mathf.Repeat(yaw + 60f, bangs.z) / bangs.z - 0.5f) * 2f;
                float noseShare = Mathf.Clamp01(1f - Mathf.Abs(yaw) / mask.z);
                if (pitch < mask.x + mask.y * noseShare)
                {
                    skin = CharacterMaterial.Mask;
                }
                else if (pitch > bangs.x + bangs.y * zigzag - bangs.w * Mathf.Abs(yaw))
                {
                    skin = CharacterMaterial.Bangs;
                }

                canvas.SetMaterial(x, y, skin);
            }
        }

        for (int side = -1; side <= 1; side += 2)
        {
            float yaw = recipe.Eyes.x * side * Mathf.Deg2Rad;
            float pitch = recipe.Eyes.y * Mathf.Deg2Rad;
            Vector3 direction = (tiltedForward * (Mathf.Cos(pitch) * Mathf.Cos(yaw)) + faceRight * (Mathf.Cos(pitch) * Mathf.Sin(yaw)) + tiltedUp * Mathf.Sin(pitch)).normalized;
            Vector2Int eye = canvas.PixelOf(hood + direction * hoodRadius);
            for (int row = 0; row < recipe.EyeHeight; row++)
            {
                int ey = eye.y - row;
                if (!canvas.IsFilled(eye.x, ey) || canvas.PartAt(eye.x, ey) != CharacterPart.Head)
                {
                    continue;
                }

                CharacterMaterial under = canvas.MaterialAt(eye.x, ey);
                if (under == CharacterMaterial.Face || under == CharacterMaterial.FaceShade)
                {
                    canvas.SetMaterial(eye.x, ey, CharacterMaterial.Eye);
                }
            }
        }
    }

    /// <summary>
    /// <paramref name="direction"/> turned counterclockwise about the up axis by <paramref name="radians"/>.
    /// </summary>
    private static Vector3 Turn(Vector3 direction, float radians)
    {
        float c = Mathf.Cos(radians);
        float s = Mathf.Sin(radians);
        return new Vector3(direction.x * c - direction.y * s, direction.x * s + direction.y * c, direction.z);
    }
}
