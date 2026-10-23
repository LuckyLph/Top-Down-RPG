using UnityEngine;

/// <summary>
/// The Weasel's body plan: furred legs with wrapped shins and long paws, a tail from the small of the back that droops,
/// curls up, sways with the stride and lifts with the flow, a round torso whose forward-facing pixels are the pale
/// belly and throat, furred arms with wrapped forearms, and the head (<see cref="DrawHead"/>): a round head with a
/// snout and nose, two ears whose inside shows where they face forward, and a red band across the eyes knotted behind
/// the head, laid out by angle like the rogue's face so it lines up in every direction. Sizes and colours come from the
/// <see cref="WeaselRecipe"/>.
/// </summary>
public sealed class WeaselPainter : CharacterPainter
{
    private const int TailSegments = 12;
    private const float BandRingLength = 0.35f;
    private const float EyeDepthGap = 1.5f;

    private readonly WeaselRecipe recipe;

    /// <summary>
    /// Creates a painter of the Weasel of <paramref name="weasel"/> holding <paramref name="weaponArt"/> (or none) onto
    /// <paramref name="target"/>.
    /// </summary>
    public WeaselPainter(WeaselRecipe weasel, CharacterWeaponArt weaponArt, CharacterCanvas target) : base(weasel, weaponArt, target)
    {
        recipe = weasel;
    }

    /// <inheritdoc />
    protected override void DrawBody(CharacterPose pose, CharacterJoints j)
    {
        DrawLeg(j.LeftHip, j.LeftKnee, j.LeftAnkle, CharacterPart.LeftThigh, CharacterPart.LeftShin, CharacterPart.LeftFoot);
        DrawLeg(j.RightHip, j.RightKnee, j.RightAnkle, CharacterPart.RightThigh, CharacterPart.RightShin, CharacterPart.RightFoot);
        DrawTail(pose, j);

        CharacterCanvas canvas = Canvas;
        Vector3 sa = j.ShoulderAxis;
        Vector3 ha = j.HipAxis;
        Vector3 pelvis = j.Pelvis;
        Vector3 neck = j.Neck;
        Vector3 spine = j.Spine;
        float waist = recipe.WaistRadius;
        for (int side = -1; side <= 1; side += 2)
        {
            canvas.Capsule(World(pelvis + ha * (1.3f * side) + spine * 0.6f), World(neck - spine * 1.6f + sa * (1.6f * side)), waist, recipe.ChestRadius, CharacterMaterial.Fur, CharacterPart.Torso);
            canvas.Capsule(World(pelvis + ha * (1.4f * side) - spine * 0.2f), World(pelvis + ha * (1.6f * side) - spine * 1.2f), waist + 0.1f, waist, CharacterMaterial.Fur, CharacterPart.Torso);
        }

        canvas.Capsule(World(neck - spine * 0.8f), World(neck + (j.Head - neck) * 0.45f), recipe.ChestRadius * 0.75f, recipe.ChestRadius * 0.65f, CharacterMaterial.Fur, CharacterPart.Torso);
        DrawArm(j.LeftShoulder, j.LeftElbow, j.LeftHand, CharacterPart.LeftUpperArm, CharacterPart.LeftForearm, CharacterPart.LeftHand);
        DrawArm(j.RightShoulder, j.RightElbow, j.RightHand, CharacterPart.RightUpperArm, CharacterPart.RightForearm, CharacterPart.RightHand);

        Vector3 belly = World(j.TorsoForward);
        for (int y = 0; y < canvas.Height; y++)
        {
            for (int x = 0; x < canvas.Width; x++)
            {
                if (canvas.IsFilled(x, y) && canvas.PartAt(x, y) == CharacterPart.Torso && Vector3.Dot(WorldNormalAt(x, y), belly) > recipe.BellyFacing)
                {
                    canvas.SetMaterial(x, y, CharacterMaterial.FurLight);
                }
            }
        }
    }

    /// <summary>
    /// Draws a leg: a thick thigh and the shin in fur, wraps over the shin's lower share crossed by bands, and a long
    /// paw pointing forward.
    /// </summary>
    private void DrawLeg(Vector3 hipLocal, Vector3 kneeLocal, Vector3 ankleLocal, CharacterPart thigh, CharacterPart shin, CharacterPart foot)
    {
        CharacterCanvas canvas = Canvas;
        float limb = recipe.LimbThickness;
        float paw = recipe.PawSize;
        Vector3 hip = World(hipLocal);
        Vector3 knee = World(kneeLocal);
        Vector3 ankle = World(ankleLocal);
        canvas.Capsule(hip, knee, 2.3f * limb, 1.9f * limb, CharacterMaterial.Fur, thigh);
        canvas.Capsule(knee, ankle, 1.8f * limb, 1.4f * limb, CharacterMaterial.Fur, shin);
        DrawWraps(ankle, knee, recipe.ShinWraps, 1.4f * limb, 1.8f * limb, shin);
        canvas.Capsule(ankle, ankle + Forward * (2.6f * paw) - Up * 0.6f, 1.6f * paw, 1.3f * paw, CharacterMaterial.Fur, foot);
    }

    /// <summary>
    /// Draws an arm: the upper arm and forearm in fur, wraps over the forearm's share nearest the wrist crossed by bands,
    /// and the paw.
    /// </summary>
    private void DrawArm(Vector3 shoulderLocal, Vector3 elbowLocal, Vector3 handLocal, CharacterPart upper, CharacterPart fore, CharacterPart hand)
    {
        CharacterCanvas canvas = Canvas;
        float limb = recipe.LimbThickness;
        Vector3 shoulder = World(shoulderLocal);
        Vector3 elbow = World(elbowLocal);
        Vector3 wrist = World(handLocal);
        canvas.Capsule(shoulder, elbow, 1.7f * limb, 1.5f * limb, CharacterMaterial.Fur, upper);
        canvas.Capsule(elbow, wrist, 1.5f * limb, 1.3f * limb, CharacterMaterial.Fur, fore);
        DrawWraps(wrist, elbow, recipe.ForearmWraps, 1.3f * limb, 1.5f * limb, fore);
        canvas.Sphere(wrist, recipe.HandRadius, CharacterMaterial.Fur, hand);
    }

    /// <summary>
    /// Draws wraps over a limb from <paramref name="end"/> (radius <paramref name="endRadius"/>) toward
    /// <paramref name="start"/> (radius <paramref name="startRadius"/>), covering <paramref name="share"/> of it a little
    /// proud of the limb, with the recipe's bands evenly across them.
    /// </summary>
    private void DrawWraps(Vector3 end, Vector3 start, float share, float endRadius, float startRadius, CharacterPart part)
    {
        if (share <= 0f)
        {
            return;
        }

        CharacterCanvas canvas = Canvas;
        Vector3 top = end + (start - end) * share;
        float topRadius = Mathf.Lerp(endRadius, startRadius, share) + 0.12f;
        canvas.Capsule(end, top, endRadius + 0.12f, topRadius, CharacterMaterial.Wraps, part);
        Vector3 along = (top - end).normalized;
        int bands = recipe.WrapBands;
        for (int b = 1; b <= bands; b++)
        {
            float t = b / (float)(bands + 1);
            Vector3 middle = Vector3.Lerp(end, top, t);
            float radius = Mathf.Lerp(endRadius + 0.12f, topRadius, t) + 0.1f;
            canvas.Capsule(middle - along * BandRingLength, middle + along * BandRingLength, radius, radius, CharacterMaterial.WrapsBand, part);
        }
    }

    /// <summary>
    /// Draws the tail from the small of the back: it leaves the body the droop below level, curls up along its length,
    /// lifts with the pose's flow and sways sideways with its phase, swelling to its middle radius and tapering to the
    /// tip, the tip's share in the dark fur.
    /// </summary>
    private void DrawTail(CharacterPose pose, CharacterJoints j)
    {
        float length = recipe.TailLength;
        if (length <= 0f)
        {
            return;
        }

        Vector3 back = -j.HipForward;
        Vector3 root = j.Pelvis + back * (recipe.WaistRadius * 0.9f) - Up * 0.6f;
        float lift = recipe.TailLift * pose.CapeFlow;
        Vector3 radii = recipe.TailRadius;
        Vector3 previous = root;
        float previousRadius = radii.x;
        Vector3 path = root;
        for (int i = 1; i <= TailSegments; i++)
        {
            float s = i / (float)TailSegments;
            float angle = (-recipe.TailDroop + lift + recipe.TailCurl * s) * Mathf.Deg2Rad;
            path += (back * Mathf.Cos(angle) + Up * Mathf.Sin(angle)) * (length / TailSegments);
            float sway = recipe.TailSway * s * s * Mathf.Sin(2f * Mathf.PI * (pose.CapePhase - s * 0.3f));
            float radius = s < 0.5f ? Mathf.Lerp(radii.x, radii.y, s * 2f) : Mathf.Lerp(radii.y, radii.z, s * 2f - 1f);
            Vector3 point = path + j.HipAxis * sway;
            point.z = Mathf.Max(point.z, radius);
            CharacterMaterial fur = s > 1f - recipe.TailTip ? CharacterMaterial.FurDark : CharacterMaterial.Fur;
            Canvas.Capsule(World(previous), World(point), previousRadius, radius, fur, CharacterPart.Tail);
            previous = point;
            previousRadius = radius;
        }
    }

    /// <summary>
    /// Draws the head: a furred sphere, the snout pointing forward and down with the nose at its tip, the ears, and the
    /// band's knot and two tails behind the head, streaming back with the flow. Then the face is laid out on the head
    /// by angle from its middle (yaw across, pitch up), the head tipped toward the camera by the face tilt: the band
    /// between its two heights all round, the pale chin and cheeks below theirs, and the eyes at their angles in the
    /// band; an ear's pixels facing forward show its inside. Last, the ring at the right ear.
    /// </summary>
    protected override void DrawHead(CharacterPose pose, CharacterJoints j)
    {
        CharacterCanvas canvas = Canvas;
        float radius = recipe.HeadRadius;
        Vector3 centre = World(j.Head);
        float turn = j.HeadTurn * Mathf.Deg2Rad;
        Vector3 faceForward = Turn(Forward, turn);
        Vector3 faceRight = Turn(Right, turn);
        float tilt = recipe.FaceTilt * Mathf.Deg2Rad;
        Vector3 tiltedForward = (faceForward * Mathf.Cos(tilt) + Up * Mathf.Sin(tilt)).normalized;
        Vector3 tiltedUp = (Up * Mathf.Cos(tilt) - faceForward * Mathf.Sin(tilt)).normalized;
        canvas.Sphere(centre, radius, CharacterMaterial.Fur, CharacterPart.Head);

        float drop = recipe.SnoutDrop * Mathf.Deg2Rad;
        Vector3 snout = (faceForward * Mathf.Cos(drop) - Up * Mathf.Sin(drop)).normalized;
        Vector3 snoutBase = centre + faceForward * (0.35f * radius) - Up * (0.3f * radius);
        Vector3 snoutTip = centre + snout * (recipe.SnoutLength * radius) - Up * (0.15f * radius);
        canvas.Capsule(snoutBase, snoutTip, recipe.SnoutRadius.x * radius, recipe.SnoutRadius.y * radius, CharacterMaterial.FurLight, CharacterPart.Snout);
        canvas.Sphere(snoutTip + snout * (recipe.SnoutRadius.y * radius), recipe.NoseRadius, CharacterMaterial.Nose, CharacterPart.Snout, 0.6f);

        float place = recipe.EarPlace * Mathf.Deg2Rad;
        float spread = recipe.EarSpread * Mathf.Deg2Rad;
        Vector3 rightEarBase = centre;
        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 earBase = centre + (Up * Mathf.Cos(place) + faceRight * (side * Mathf.Sin(place))) * (0.8f * radius) - faceForward * (0.1f * radius);
            Vector3 earDirection = (Up * Mathf.Cos(spread) + faceRight * (side * Mathf.Sin(spread)) - faceForward * recipe.EarBack).normalized;
            CharacterPart ear = side < 0 ? CharacterPart.LeftEar : CharacterPart.RightEar;
            canvas.Capsule(earBase, earBase + earDirection * (recipe.EarLength * radius), recipe.EarRadius, 0.35f, CharacterMaterial.Fur, ear);
            if (side > 0)
            {
                rightEarBase = earBase;
            }
        }

        DrawBandTails(pose, centre, radius, faceForward, faceRight);

        Vector2 band = recipe.Band;
        Vector2 chin = recipe.Chin;
        for (int y = 0; y < canvas.Height; y++)
        {
            for (int x = 0; x < canvas.Width; x++)
            {
                if (!canvas.IsFilled(x, y))
                {
                    continue;
                }

                CharacterPart part = canvas.PartAt(x, y);
                if (part == CharacterPart.LeftEar || part == CharacterPart.RightEar)
                {
                    if (Vector3.Dot(WorldNormalAt(x, y), faceForward) > recipe.EarInsideFacing)
                    {
                        canvas.SetMaterial(x, y, CharacterMaterial.EarInside);
                    }

                    continue;
                }

                if (part != CharacterPart.Head)
                {
                    continue;
                }

                Vector3 world = WorldNormalAt(x, y);
                float yaw = Mathf.Atan2(Vector3.Dot(world, faceRight), Vector3.Dot(world, tiltedForward)) * Mathf.Rad2Deg;
                float pitch = Mathf.Asin(Mathf.Clamp(Vector3.Dot(world, tiltedUp), -1f, 1f)) * Mathf.Rad2Deg;
                if (pitch >= band.x && pitch <= band.y)
                {
                    canvas.SetMaterial(x, y, CharacterMaterial.Sash);
                }
                else if (pitch < chin.x && Mathf.Abs(yaw) < chin.y)
                {
                    canvas.SetMaterial(x, y, CharacterMaterial.FurLight);
                }
            }
        }

        for (int side = -1; side <= 1; side += 2)
        {
            float yaw = recipe.Eyes.x * side * Mathf.Deg2Rad;
            float pitch = recipe.Eyes.y * Mathf.Deg2Rad;
            Vector3 direction = (tiltedForward * (Mathf.Cos(pitch) * Mathf.Cos(yaw)) + faceRight * (Mathf.Cos(pitch) * Mathf.Sin(yaw)) + tiltedUp * Mathf.Sin(pitch)).normalized;
            Vector3 surface = centre + direction * radius;
            Vector2Int eye = canvas.PixelOf(surface);
            float depth = canvas.Project(surface).z;
            for (int row = 0; row < recipe.EyeHeight; row++)
            {
                int ey = eye.y - row;
                if (canvas.IsFilled(eye.x, ey) && canvas.PartAt(eye.x, ey) == CharacterPart.Head && canvas.DepthAt(eye.x, ey) > depth - EyeDepthGap)
                {
                    canvas.SetMaterial(eye.x, ey, CharacterMaterial.Eye);
                }
            }
        }

        if (recipe.Earring)
        {
            canvas.Sphere(rightEarBase + faceRight * (0.45f * radius) - Up * (0.35f * radius), 0.55f, CharacterMaterial.Fittings, CharacterPart.Strap, 0.6f);
        }
    }

    /// <summary>
    /// Draws the band's knot on the back of the head at the band's middle height and its two tails hanging from it,
    /// streaming back and rising with the pose's flow and rippling with its phase.
    /// </summary>
    private void DrawBandTails(CharacterPose pose, Vector3 centre, float radius, Vector3 faceForward, Vector3 faceRight)
    {
        float length = recipe.BandTails;
        if (length <= 0f)
        {
            return;
        }

        CharacterCanvas canvas = Canvas;
        float middle = (recipe.Band.x + recipe.Band.y) * 0.5f * Mathf.Deg2Rad;
        Vector3 knot = centre + (-faceForward * Mathf.Cos(middle) + Up * Mathf.Sin(middle)) * (radius * 0.95f);
        canvas.Sphere(knot, 1.1f, CharacterMaterial.Sash, CharacterPart.Strap);
        float flow = pose.CapeFlow;
        for (int side = -1; side <= 1; side += 2)
        {
            float ripple = 0.5f * Mathf.Sin(2f * Mathf.PI * pose.CapePhase * 2f + side);
            Vector3 end = knot - faceForward * (length * 0.6f * (1f + flow)) - Up * (length * (0.9f - 0.6f * flow)) + faceRight * (side * (1f + ripple));
            canvas.Capsule(knot, end, 0.8f, 0.5f, CharacterMaterial.Sash, CharacterPart.Strap);
        }
    }
}
