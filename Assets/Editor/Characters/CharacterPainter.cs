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
/// around the neck, then the weapon with its smear and effects (through a <see cref="WeaponPainter"/>), the hooded
/// head, whose face is placed by direction (<see cref="DrawHead"/>), and last a cast's light at the casting hands
/// (<see cref="DrawLight"/>), just in front of each hand so the body hides it when the hand is behind it. Sizes come
/// from the <see cref="CharacterRecipe"/> and the <see cref="CharacterWeaponArt"/>; the mantle, belt, dagger and scarf
/// are fixed parts of the rogue's outfit.
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
            WeaponPainter painter = new(weapon, canvas, World);
            if (smear != null && smear.Count > 1)
            {
                painter.DrawSmear(smear);
            }

            painter.DrawBlade(joints.RightHand, pose.Blade.normalized, joints.ShoulderAxis);
            foreach (MotionEffect effect in effects)
            {
                if (effect.Kind == MotionEffectKind.Dust)
                {
                    Vector3 tip = joints.RightHand + pose.Blade.normalized * weapon.Length;
                    painter.DrawDust(new Vector3(tip.x, tip.y, 0f), effect.Size);
                }
            }
        }
        else if (weapon != null)
        {
            WeaponPainter painter = new(weapon, canvas, World);
            Vector3 grip = joints.LeftHand;
            Vector3 bowUp = pose.BowUp.normalized;
            Vector3 aim = (pose.BowAim - bowUp * Vector3.Dot(pose.BowAim, bowUp)).normalized;
            painter.DrawBow(grip, bowUp, aim, pose.Draw, pose.Kick, pose.Arrow > 0.5f, joints.RightHand);
            foreach (MotionEffect effect in effects)
            {
                if (effect.Kind == MotionEffectKind.Release)
                {
                    painter.DrawRelease(grip, aim, bowUp);
                }
            }
        }

        DrawHead(joints);
        foreach (MotionEffect effect in effects)
        {
            if (effect.IsLight)
            {
                DrawLights(effect, joints);
            }
        }
    }

    /// <summary>
    /// A point of the character's space in the world, for the current facing.
    /// </summary>
    private Vector3 World(Vector3 local)
    {
        return local.x * right + local.y * forward + local.z * Up;
    }

    /// <summary>
    /// Draws a cast's light <paramref name="effect"/> at the hands it names.
    /// </summary>
    private void DrawLights(MotionEffect effect, CharacterJoints j)
    {
        switch (effect.Hands)
        {
            case CastHands.Left:
                DrawLight(effect, j.LeftHand);
                break;
            case CastHands.Both:
                DrawLight(effect, j.LeftHand);
                DrawLight(effect, j.RightHand);
                break;
            case CastHands.Between:
                DrawLight(effect, (j.LeftHand + j.RightHand) * 0.5f);
                break;
            default:
                DrawLight(effect, j.RightHand);
                break;
        }
    }

    /// <summary>
    /// Draws a cast's light just in front of <paramref name="hand"/>, flat to the camera, sized in pixels: a gathering
    /// glow (a rim, a glow and a core, growing with the effect's size, the share of the gather done), a star of eight
    /// rays on the release, a ring broken in four after it, or four sparks drifting out.
    /// </summary>
    private void DrawLight(MotionEffect effect, Vector3 hand)
    {
        Vector3 centre = World(hand) - CharacterCanvas.ViewForward * (recipe.HandRadius + 0.3f);
        Vector3 nearer = -CharacterCanvas.ViewForward * (0.01f / canvas.Scale);
        float size = effect.Size;
        switch (effect.Kind)
        {
            case MotionEffectKind.Gather:
                float radius = 0.6f + 1.6f * Mathf.Clamp01(size);
                canvas.Disc(centre, radius + 0.6f, CharacterMaterial.LightRim, CharacterPart.Effect);
                canvas.Disc(centre + nearer, radius, CharacterMaterial.LightGlow, CharacterPart.Effect);
                canvas.Disc(centre + nearer * 2f, Mathf.Max(0.5f, radius - 1f), CharacterMaterial.LightCore, CharacterPart.Effect);
                break;
            case MotionEffectKind.Burst:
                for (int ray = 0; ray < 8; ray++)
                {
                    float angle = ray * Mathf.PI / 4f;
                    float length = (ray % 2 == 0 ? 6f : 3.5f) * size;
                    for (float t = 0f; t <= length; t += 0.5f)
                    {
                        CharacterMaterial tone = t < length * 0.6f ? CharacterMaterial.LightGlow : CharacterMaterial.LightRim;
                        canvas.Dot(centre + OnScreen(Mathf.Cos(angle) * t, Mathf.Sin(angle) * t), tone, CharacterPart.Effect);
                    }
                }

                canvas.Disc(centre + nearer, 2.2f * size, CharacterMaterial.LightRim, CharacterPart.Effect);
                canvas.Disc(centre + nearer * 2f, 1.6f * size, CharacterMaterial.LightGlow, CharacterPart.Effect);
                canvas.Disc(centre + nearer * 3f, 0.9f * size, CharacterMaterial.LightCore, CharacterPart.Effect);
                break;
            case MotionEffectKind.Ring:
                for (int degrees = 0; degrees < 360; degrees += 5)
                {
                    if (degrees / 45 % 2 == 1)
                    {
                        continue;
                    }

                    float angle = degrees * Mathf.Deg2Rad;
                    foreach (float ring in new[] { 3.2f * size, 3.8f * size })
                    {
                        canvas.Dot(centre + OnScreen(Mathf.Cos(angle) * ring, Mathf.Sin(angle) * ring), CharacterMaterial.LightGlow, CharacterPart.Effect);
                    }
                }

                break;
            case MotionEffectKind.Sparks:
                foreach (float angle in new[] { 0.6f, 2.2f, 3.9f, 5.4f })
                {
                    canvas.Disc(centre + OnScreen(Mathf.Cos(angle) * 5f * size, Mathf.Sin(angle) * 5f * size), 0.8f, CharacterMaterial.LightRim, CharacterPart.Effect);
                }

                break;
        }
    }

    /// <summary>
    /// The world offset that moves a point <paramref name="right"/> pixels right and <paramref name="up"/> pixels up on
    /// the screen, keeping its depth.
    /// </summary>
    private Vector3 OnScreen(float right, float up)
    {
        return (CharacterCanvas.ViewRight * right + CharacterCanvas.ViewUp * up) / canvas.Scale;
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
