using UnityEngine;

/// <summary>
/// The adventurer's body plan: legs in trousers and boots, a tunic with its skirt below a buckled belt, sleeves as the
/// recipe dresses them, bare or gloved hands, a satchel's strap and pouch when it has one, the shirt's collar, and a bare head of hair whose face is placed by direction
/// (<see cref="DrawHead"/>). Sizes come from the <see cref="AdventurerRecipe"/>.
/// </summary>
public sealed class AdventurerPainter : CharacterPainter
{
    private readonly AdventurerRecipe recipe;

    /// <summary>
    /// Creates a painter of the adventurer of <paramref name="adventurer"/> holding <paramref name="weaponArt"/> (or
    /// none) onto <paramref name="target"/>.
    /// </summary>
    public AdventurerPainter(AdventurerRecipe adventurer, CharacterWeaponArt weaponArt, CharacterCanvas target) : base(adventurer, weaponArt, target)
    {
        recipe = adventurer;
    }

    /// <inheritdoc />
    protected override void DrawBody(CharacterPose pose, CharacterJoints j)
    {
        DrawLeg(j.LeftHip, j.LeftKnee, j.LeftAnkle, CharacterPart.LeftThigh, CharacterPart.LeftShin, CharacterPart.LeftFoot);
        DrawLeg(j.RightHip, j.RightKnee, j.RightAnkle, CharacterPart.RightThigh, CharacterPart.RightShin, CharacterPart.RightFoot);

        CharacterCanvas canvas = Canvas;
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
            if (recipe.SkirtLength > 0f)
            {
                canvas.Capsule(World(pelvis + ha * (1.4f * side) - spine * 0.2f), World(pelvis + ha * (1.7f * side) - spine * recipe.SkirtLength), waist + 0.1f, waist + 0.2f, CharacterMaterial.Jerkin, CharacterPart.Torso);
            }

            Vector3 beltStart = pelvis + ha * (1.4f * side) + spine * 0.9f;
            canvas.Capsule(World(beltStart), World(beltStart + spine * 1.1f), waist + 0.22f, waist + 0.22f, CharacterMaterial.Belt, CharacterPart.Belt);
        }

        canvas.Sphere(World(pelvis + spine * 1.45f + hf * (waist + 0.6f)), 0.75f, CharacterMaterial.Fittings, CharacterPart.Belt);

        DrawArm(j.LeftShoulder, j.LeftElbow, j.LeftHand, CharacterPart.LeftUpperArm, CharacterPart.LeftForearm, CharacterPart.LeftHand);
        DrawArm(j.RightShoulder, j.RightElbow, j.RightHand, CharacterPart.RightUpperArm, CharacterPart.RightForearm, CharacterPart.RightHand);
        if (recipe.Strap)
        {
            DrawStrap(j);
        }

        for (int i = 0; i < 16; i++)
        {
            float angle = 2f * Mathf.PI * i / 16f;
            Vector3 p = neck + spine * 0.3f + sa * (Mathf.Cos(angle) * 1.9f) + tf * (Mathf.Sin(angle) * 1.7f);
            canvas.Sphere(World(p), 1.1f, CharacterMaterial.Shirt, CharacterPart.Strap);
        }
    }

    /// <summary>
    /// Draws a leg: thigh and shin in trousers, a tall boot over the shin, and the foot pointing forward.
    /// </summary>
    private void DrawLeg(Vector3 hipLocal, Vector3 kneeLocal, Vector3 ankleLocal, CharacterPart thigh, CharacterPart shin, CharacterPart foot)
    {
        CharacterCanvas canvas = Canvas;
        float limb = recipe.LimbThickness;
        float boot = recipe.BootSize;
        Vector3 hip = World(hipLocal);
        Vector3 knee = World(kneeLocal);
        Vector3 ankle = World(ankleLocal);
        canvas.Capsule(hip, knee, 2.1f * limb, 1.8f * limb, CharacterMaterial.Trousers, thigh);
        canvas.Capsule(knee, ankle, 1.7f * limb, 1.45f * limb, CharacterMaterial.Trousers, shin);
        canvas.Capsule(knee + (ankle - knee) * recipe.BootTop, ankle, 1.95f * boot, 1.65f * boot, CharacterMaterial.Boots, shin);
        canvas.Capsule(ankle, ankle + Forward * (2.4f * boot) - Up * 0.5f, 1.7f * boot, 1.4f * boot, CharacterMaterial.Boots, foot);
    }

    /// <summary>
    /// Draws an arm as the recipe dresses it: the shirt's long sleeve, the tunic's short sleeve over a bare forearm, or
    /// both; then the hand, bare or gloved.
    /// </summary>
    private void DrawArm(Vector3 shoulderLocal, Vector3 elbowLocal, Vector3 handLocal, CharacterPart upper, CharacterPart fore, CharacterPart hand)
    {
        CharacterCanvas canvas = Canvas;
        float limb = recipe.LimbThickness;
        Vector3 shoulder = World(shoulderLocal);
        Vector3 elbow = World(elbowLocal);
        Vector3 wrist = World(handLocal);
        AdventurerSleeves sleeves = recipe.Sleeves;
        CharacterMaterial upperSleeve = sleeves == AdventurerSleeves.Long ? CharacterMaterial.Shirt : CharacterMaterial.Jerkin;
        float upperRadius = sleeves == AdventurerSleeves.Long ? 1.7f : 1.85f;
        canvas.Capsule(shoulder, elbow, upperRadius * limb, 1.5f * limb, upperSleeve, upper);
        if (sleeves == AdventurerSleeves.Short)
        {
            canvas.Capsule(elbow, wrist, 1.3f * limb, 1.15f * limb, CharacterMaterial.Skin, fore);
        }
        else
        {
            canvas.Capsule(elbow, wrist, 1.5f * limb, 1.35f * limb, CharacterMaterial.Shirt, fore);
        }

        canvas.Sphere(wrist, recipe.HandRadius, recipe.Gloves ? CharacterMaterial.Gloves : CharacterMaterial.Skin, hand);
    }

    /// <summary>
    /// Draws a satchel's strap, a band around the torso from the left shoulder across the chest to the right hip and
    /// back, and its pouch at the right hip.
    /// </summary>
    private void DrawStrap(CharacterJoints j)
    {
        CharacterCanvas canvas = Canvas;
        Vector3 middle = (j.Pelvis + j.Neck) * 0.5f;
        Vector3 along = (j.Spine * 0.8f - j.ShoulderAxis * 0.6f).normalized;
        float reach = Vector3.Distance(j.Pelvis, j.Neck) * 0.62f;
        float depth = recipe.ChestRadius + 0.35f;
        for (int i = 0; i < 48; i++)
        {
            float angle = 2f * Mathf.PI * i / 48f;
            Vector3 p = middle + along * (Mathf.Cos(angle) * reach) + j.TorsoForward * (Mathf.Sin(angle) * depth);
            canvas.Sphere(World(p), 0.55f, CharacterMaterial.Strap, CharacterPart.Strap);
        }

        canvas.Sphere(World(j.Pelvis + j.HipAxis * 2.9f + j.Spine * 0.2f + j.HipForward * 0.4f), 1.25f, CharacterMaterial.Pouch, CharacterPart.Belt);
    }

    /// <summary>
    /// Draws the head: the ears, then a ball of hair whose front holds the face, laid out by angle from the face's
    /// middle (yaw across, pitch up) with the head tipped toward the camera by the face tilt. Inside the face's ellipse
    /// the fringe hangs in points from above and the rest is skin; the eyes, brows, mouth and blush are painted at their
    /// angles, so the face lines up in every direction and turns with the head; then the locks grow over it all.
    /// </summary>
    protected override void DrawHead(CharacterPose pose, CharacterJoints j)
    {
        CharacterCanvas canvas = Canvas;
        float radius = recipe.HeadRadius;
        Vector3 centre = World(j.Head);
        float turn = j.HeadTurn * Mathf.Deg2Rad;
        Vector3 faceForward = Turn(Forward, turn);
        Vector3 faceRight = Turn(Right, turn);
        float tilt = (recipe.FaceTilt + pose.HeadTilt) * Mathf.Deg2Rad;
        Vector3 tiltedForward = (faceForward * Mathf.Cos(tilt) + Up * Mathf.Sin(tilt)).normalized;
        Vector3 tiltedUp = (Up * Mathf.Cos(tilt) - faceForward * Mathf.Sin(tilt)).normalized;
        Vector3 head = centre + Up * (0.08f * radius) - tiltedForward * (0.06f * radius);
        float headRadius = radius * recipe.HairSize;
        Vector3 ears = recipe.Ears;
        if (ears.z > 0f)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 ear = head + Direction(tiltedForward, faceRight, tiltedUp, ears.x * side, ears.y) * (headRadius * 0.92f);
                canvas.Sphere(ear, ears.z * headRadius, CharacterMaterial.Skin, side < 0 ? CharacterPart.LeftEar : CharacterPart.RightEar);
            }
        }

        canvas.Sphere(head, headRadius, CharacterMaterial.Hair, CharacterPart.Head);

        Vector3 face = recipe.Face;
        Vector4 fringe = recipe.Fringe;
        for (int y = 0; y < canvas.Height; y++)
        {
            for (int x = 0; x < canvas.Width; x++)
            {
                if (!canvas.IsFilled(x, y) || canvas.PartAt(x, y) != CharacterPart.Head)
                {
                    continue;
                }

                Vector3 world = WorldNormalAt(x, y);
                float yaw = Mathf.Atan2(Vector3.Dot(world, faceRight), Vector3.Dot(world, tiltedForward)) * Mathf.Rad2Deg;
                float pitch = Mathf.Asin(Mathf.Clamp(Vector3.Dot(world, tiltedUp), -1f, 1f)) * Mathf.Rad2Deg;
                float across = yaw / face.x;
                float upward = (pitch - face.y) / face.z;
                if (across * across + upward * upward >= 1f)
                {
                    continue;
                }

                float zigzag = Mathf.Abs(Mathf.Repeat(yaw + 60f, fringe.z) / fringe.z - 0.5f) * 2f;
                if (pitch > fringe.x + fringe.y * zigzag - fringe.w * Mathf.Abs(yaw))
                {
                    continue;
                }

                canvas.SetMaterial(x, y, CharacterMaterial.Skin);
            }
        }

        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 eyeDirection = Direction(tiltedForward, faceRight, tiltedUp, recipe.Eyes.x * side, recipe.Eyes.y);
            if (!FacesCamera(eyeDirection))
            {
                continue;
            }

            Vector2Int eye = canvas.PixelOf(head + eyeDirection * headRadius);
            bool shut = pose.EyesClosed > 0.5f;
            for (int column = 0; column < recipe.EyeWidth; column++)
            {
                int ex = eye.x - side * column;
                for (int row = shut ? recipe.EyeHeight - 1 : 0; row < recipe.EyeHeight; row++)
                {
                    PaintSkin(ex, eye.y - row, CharacterMaterial.Eye);
                }
            }

            if (recipe.EyeGlint && recipe.EyeHeight > 1 && !shut)
            {
                PaintOver(Mathf.Min(eye.x, eye.x - side * (recipe.EyeWidth - 1)), eye.y, CharacterMaterial.Eye, CharacterMaterial.EyeGlint);
            }

            if (recipe.BrowGap > 0)
            {
                Vector2Int middle = canvas.PixelOf(head + Direction(tiltedForward, faceRight, tiltedUp, 0f, recipe.Eyes.y) * headRadius);
                int left = Mathf.Min(eye.x, eye.x - side * (recipe.EyeWidth - 1));
                int right = Mathf.Max(eye.x, eye.x - side * (recipe.EyeWidth - 1));
                int outward = middle.x * 2 >= left + right ? -1 : 1;
                int inner = outward < 0 ? right : left;
                int width = recipe.BrowWidth;
                for (int k = 0; k < width; k++)
                {
                    int lift = k == width - 1 && width > 1 ? recipe.BrowTilt : 0;
                    PaintSkin(inner + outward * k, eye.y + recipe.BrowGap + lift, CharacterMaterial.Brow);
                }
            }

            if (recipe.Blush.x > 0f)
            {
                Vector2Int cheek = canvas.PixelOf(head + Direction(tiltedForward, faceRight, tiltedUp, recipe.Blush.x * side, recipe.Blush.y) * headRadius);
                PaintSkin(cheek.x, cheek.y, CharacterMaterial.Blush);
            }
        }

        int mouthWidth = Mathf.RoundToInt(recipe.Mouth.y);
        Vector3 mouthDirection = Direction(tiltedForward, faceRight, tiltedUp, 0f, recipe.Mouth.x);
        if (mouthWidth > 0 && FacesCamera(mouthDirection))
        {
            Vector2Int mouth = canvas.PixelOf(head + mouthDirection * headRadius);
            int start = mouth.x - mouthWidth / 2;
            int open = Mathf.RoundToInt(pose.MouthOpen);
            for (int column = 0; column < mouthWidth; column++)
            {
                for (int row = 0; row <= open; row++)
                {
                    PaintSkin(start + column, mouth.y - row, row == 0 ? CharacterMaterial.Mouth : CharacterMaterial.MouthInside);
                }
            }

            if (recipe.MouthCurve != 0)
            {
                PaintSkin(start - 1, mouth.y + recipe.MouthCurve, CharacterMaterial.Mouth);
                PaintSkin(start + mouthWidth, mouth.y + recipe.MouthCurve, CharacterMaterial.Mouth);
            }
        }

        foreach (HairLock hairLock in recipe.Locks)
        {
            Vector3 root = head + Direction(tiltedForward, faceRight, tiltedUp, hairLock.Root.x, hairLock.Root.y) * (headRadius * 0.85f);
            Vector3 tip = root + (faceRight * hairLock.Tip.x + tiltedUp * hairLock.Tip.y + tiltedForward * hairLock.Tip.z) * headRadius + Up * (pose.HairLift * hairLock.Tip.magnitude);
            canvas.Capsule(root, tip, hairLock.Radius.x * headRadius, hairLock.Radius.y * headRadius, CharacterMaterial.Hair, CharacterPart.Head);
        }
    }

    /// <summary>
    /// The direction <paramref name="yawDegrees"/> across and <paramref name="pitchDegrees"/> up from the face's middle,
    /// in the face's frame.
    /// </summary>
    private static Vector3 Direction(Vector3 forward, Vector3 right, Vector3 up, float yawDegrees, float pitchDegrees)
    {
        float yaw = yawDegrees * Mathf.Deg2Rad;
        float pitch = pitchDegrees * Mathf.Deg2Rad;
        return (forward * (Mathf.Cos(pitch) * Mathf.Cos(yaw)) + right * (Mathf.Cos(pitch) * Mathf.Sin(yaw)) + up * Mathf.Sin(pitch)).normalized;
    }

    /// <summary>
    /// Whether a face feature pointing along <paramref name="direction"/> faces the camera enough to be painted, so a
    /// feature turned away never lands on the head's rim.
    /// </summary>
    private static bool FacesCamera(Vector3 direction)
    {
        return ArtCanvas.ToView(direction).z < -0.15f;
    }

    /// <summary>
    /// Paints pixel (<paramref name="x"/>, <paramref name="y"/>) in <paramref name="value"/> where it shows the face's
    /// skin.
    /// </summary>
    private void PaintSkin(int x, int y, CharacterMaterial value)
    {
        PaintOver(x, y, CharacterMaterial.Skin, value);
    }

    /// <summary>
    /// Paints pixel (<paramref name="x"/>, <paramref name="y"/>) of the head in <paramref name="value"/> where it shows
    /// <paramref name="under"/>.
    /// </summary>
    private void PaintOver(int x, int y, CharacterMaterial under, CharacterMaterial value)
    {
        CharacterCanvas canvas = Canvas;
        if (canvas.IsFilled(x, y) && canvas.PartAt(x, y) == CharacterPart.Head && canvas.MaterialAt(x, y) == under)
        {
            canvas.SetMaterial(x, y, value);
        }
    }
}
