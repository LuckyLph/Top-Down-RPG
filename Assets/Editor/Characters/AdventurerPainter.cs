using UnityEngine;

/// <summary>
/// The adventurer's body plan, drawn a layer at a time (Docs/PlayerCharacter.md, Phases 6 and 7): its body
/// (<see cref="CharacterLayerKey.Base"/>: the bare head with its ears and face, the buckled belt, and the weapon in hand
/// and a cast's light), or one option of a part: the hair over the head, the headwear, the top (a tunic with its skirt
/// over the shirt, or the shirt alone, its sleeves and collar), the cape, the legs (trousers or shorts), the boots, the
/// hands (bare or gloved), the bag (a satchel's strap and its pouch) or a weapon's gear (the weapon stowed at the hip
/// or on the back with its scabbard, sling or quiver, drawn last). Every layer's painter takes the same
/// drawing steps in the same order, each step a layer's, drawing only its own (<see cref="ArtCanvas.BeginStep"/>), so
/// the character baker, merging the layers by depth, and the composer at runtime break a tie as one canvas drawn with
/// all of them would. Sizes come from the <see cref="AdventurerRecipe"/>.
/// </summary>
public sealed class AdventurerPainter : CharacterPainter
{
    private readonly AdventurerRecipe recipe;
    private readonly CharacterLayerKey layer;

    /// <summary>
    /// Creates a painter of <paramref name="drawn"/>, a layer of the adventurer of <paramref name="adventurer"/> holding
    /// <paramref name="weaponArt"/> (or none), onto <paramref name="target"/>.
    /// </summary>
    public AdventurerPainter(AdventurerRecipe adventurer, CharacterWeaponArt weaponArt, CharacterCanvas target, CharacterLayerKey drawn) : base(adventurer, weaponArt, target)
    {
        recipe = adventurer;
        layer = drawn;
    }

    /// <inheritdoc />
    protected override bool PaintsShared => layer.IsBase;

    /// <inheritdoc />
    protected override void DrawBody(CharacterPose pose, CharacterJoints j)
    {
        DrawLeg(j.LeftHip, j.LeftKnee, j.LeftAnkle, CharacterPart.LeftThigh, CharacterPart.LeftShin, CharacterPart.LeftFoot);
        DrawLeg(j.RightHip, j.RightKnee, j.RightAnkle, CharacterPart.RightThigh, CharacterPart.RightShin, CharacterPart.RightFoot);

        CharacterCanvas canvas = Canvas;
        float waist = recipe.WaistRadius;
        for (int side = -1; side <= 1; side += 2)
        {
            if (Step(CharacterSlot.Top))
            {
                AdventurerTop top = recipe.Tops[layer.Option];
                CharacterMaterial cloth = top.Tunic ? CharacterMaterial.Jerkin : CharacterMaterial.Shirt;
                canvas.Capsule(World(j.Pelvis + j.HipAxis * (1.3f * side) + j.Spine * 0.4f), World(j.Neck - j.Spine * 1.4f + j.ShoulderAxis * (1.8f * side)), waist, recipe.ChestRadius, cloth, CharacterPart.Torso);
                if (top.Tunic && top.SkirtLength > 0f)
                {
                    canvas.Capsule(World(j.Pelvis + j.HipAxis * (1.4f * side) - j.Spine * 0.2f), World(j.Pelvis + j.HipAxis * (1.7f * side) - j.Spine * top.SkirtLength), waist + 0.1f, waist + 0.2f, CharacterMaterial.Jerkin, CharacterPart.Torso);
                }
            }

            if (Step(null))
            {
                Vector3 beltStart = j.Pelvis + j.HipAxis * (1.4f * side) + j.Spine * 0.9f;
                canvas.Capsule(World(beltStart), World(beltStart + j.Spine * 1.1f), waist + 0.22f, waist + 0.22f, CharacterMaterial.Belt, CharacterPart.Belt);
            }
        }

        if (Step(null))
        {
            canvas.Sphere(World(j.Pelvis + j.Spine * 1.45f + j.HipForward * (waist + 0.6f)), 0.75f, CharacterMaterial.Fittings, CharacterPart.Belt);
        }

        DrawArm(j.LeftShoulder, j.LeftElbow, j.LeftHand, CharacterPart.LeftUpperArm, CharacterPart.LeftForearm, CharacterPart.LeftHand);
        DrawArm(j.RightShoulder, j.RightElbow, j.RightHand, CharacterPart.RightUpperArm, CharacterPart.RightForearm, CharacterPart.RightHand);
        if (Step(CharacterSlot.Bag))
        {
            DrawBag(j, recipe.Bags[layer.Option]);
        }

        if (Step(CharacterSlot.Cape))
        {
            DrawCape(pose, j, recipe.Capes[layer.Option]);
        }

        if (Step(CharacterSlot.Top))
        {
            for (int i = 0; i < 16; i++)
            {
                float angle = 2f * Mathf.PI * i / 16f;
                Vector3 p = j.Neck + j.Spine * 0.3f + j.ShoulderAxis * (Mathf.Cos(angle) * 1.9f) + j.TorsoForward * (Mathf.Sin(angle) * 1.7f);
                canvas.Sphere(World(p), 1.1f, CharacterMaterial.Shirt, CharacterPart.Strap);
            }
        }
    }

    /// <summary>
    /// Starts the canvas's next drawing step, drawn only by the painter of <paramref name="owner"/>'s layer (the body's
    /// for none), so every layer's painter takes the same steps in the same order; returns whether this one draws it.
    /// </summary>
    private bool Step(CharacterSlot? owner)
    {
        bool draws = owner.HasValue ? layer.Is(owner.Value) : layer.IsBase;
        Canvas.BeginStep(draws);
        return draws;
    }

    /// <summary>
    /// Draws an arm: its sleeves as the top dresses it (<see cref="DrawSleeves"/>), then the hand, bare or gloved, the
    /// body's.
    /// </summary>
    private void DrawArm(Vector3 shoulderLocal, Vector3 elbowLocal, Vector3 handLocal, CharacterPart upper, CharacterPart fore, CharacterPart hand)
    {
        if (Step(CharacterSlot.Top))
        {
            DrawSleeves(recipe.Tops[layer.Option], shoulderLocal, elbowLocal, handLocal, upper, fore);
        }

        if (Step(CharacterSlot.Hands))
        {
            Canvas.Sphere(World(handLocal), recipe.HandRadius, recipe.Hands[layer.Option].Gloves ? CharacterMaterial.Gloves : CharacterMaterial.Skin, hand);
        }
    }

    /// <summary>
    /// Draws a leg: the legs' thigh and shin in trousers (the shin bare in shorts), then the boots' boot over the shin
    /// from its top down and the foot pointing forward.
    /// </summary>
    private void DrawLeg(Vector3 hipLocal, Vector3 kneeLocal, Vector3 ankleLocal, CharacterPart thigh, CharacterPart shin, CharacterPart foot)
    {
        CharacterCanvas canvas = Canvas;
        float limb = recipe.LimbThickness;
        Vector3 hip = World(hipLocal);
        Vector3 knee = World(kneeLocal);
        Vector3 ankle = World(ankleLocal);
        if (Step(CharacterSlot.Legs))
        {
            AdventurerLegs legs = recipe.Legs[layer.Option];
            canvas.Capsule(hip, knee, 2.1f * limb, 1.8f * limb, CharacterMaterial.Trousers, thigh);
            canvas.Capsule(knee, ankle, 1.7f * limb, 1.45f * limb, legs.Shorts ? CharacterMaterial.Skin : CharacterMaterial.Trousers, shin);
        }

        if (Step(CharacterSlot.Boots))
        {
            AdventurerBoots boots = recipe.Boots[layer.Option];
            float boot = boots.Size;
            canvas.Capsule(knee + (ankle - knee) * boots.Top, ankle, 1.95f * boot, 1.65f * boot, CharacterMaterial.Boots, shin);
            canvas.Capsule(ankle, ankle + Forward * (2.4f * boot) - Up * 0.5f, 1.7f * boot, 1.4f * boot, CharacterMaterial.Boots, foot);
        }
    }

    /// <summary>
    /// Draws an arm's sleeves as <paramref name="top"/> dresses it: the shirt's long sleeve, the tunic's short sleeve
    /// over a bare forearm, or both; the shirt alone always has its long sleeves.
    /// </summary>
    private void DrawSleeves(AdventurerTop top, Vector3 shoulderLocal, Vector3 elbowLocal, Vector3 handLocal, CharacterPart upper, CharacterPart fore)
    {
        CharacterCanvas canvas = Canvas;
        float limb = recipe.LimbThickness;
        Vector3 shoulder = World(shoulderLocal);
        Vector3 elbow = World(elbowLocal);
        Vector3 wrist = World(handLocal);
        AdventurerSleeves sleeves = top.Tunic ? top.Sleeves : AdventurerSleeves.Long;
        bool shirtSleeve = sleeves == AdventurerSleeves.Long;
        CharacterMaterial upperSleeve = shirtSleeve ? CharacterMaterial.Shirt : CharacterMaterial.Jerkin;
        float upperRadius = shirtSleeve ? 1.7f : 1.85f;
        canvas.Capsule(shoulder, elbow, upperRadius * limb, 1.5f * limb, upperSleeve, upper);
        if (sleeves == AdventurerSleeves.Short)
        {
            canvas.Capsule(elbow, wrist, 1.3f * limb, 1.15f * limb, CharacterMaterial.Skin, fore);
        }
        else
        {
            canvas.Capsule(elbow, wrist, 1.5f * limb, 1.35f * limb, CharacterMaterial.Shirt, fore);
        }
    }

    /// <summary>
    /// Draws <paramref name="cape"/>, if it hangs: a mantle over the shoulders, then strips from behind them to a hem that
    /// rises and streams back with the pose's flow and ripples with its phase; with a lining, each strip is an outer
    /// layer and a lining a little inside it whose hem hangs lower.
    /// </summary>
    private void DrawCape(CharacterPose pose, CharacterJoints j, AdventurerCape cape)
    {
        if (!cape.Cape)
        {
            return;
        }

        CharacterCanvas canvas = Canvas;
        Vector3 mantle = j.Neck - j.Spine * 0.9f;
        for (int i = 0; i < 24; i++)
        {
            float angle = 2f * Mathf.PI * i / 24f;
            float drop = 0.2f + 0.5f * Mathf.Max(Mathf.Sin(angle), 0f);
            Vector3 p = mantle + j.ShoulderAxis * (Mathf.Cos(angle) * recipe.ShoulderHalfWidth * 0.95f) + j.TorsoForward * (Mathf.Sin(angle) * (recipe.ChestRadius - 0.3f)) - j.Spine * drop;
            canvas.Sphere(World(p), 1.5f, CharacterMaterial.Cloak, CharacterPart.Cape);
        }

        float flow = pose.CapeFlow;
        Vector3 back = -j.HipForward;
        int strips = cape.Strips;
        float hemHeight = recipe.Ankle + (pose.PelvisHeight - recipe.Ankle) * cape.Hem;
        for (int i = 0; i < strips; i++)
        {
            float k = -1f + 2f * i / (strips - 1);
            Vector3 top = j.Neck - j.Spine * 1.6f + j.ShoulderAxis * (3.2f * k) - j.TorsoForward * (recipe.ChestRadius - 0.3f);
            float ripple = 0.9f * Mathf.Sin(2f * Mathf.PI * pose.CapePhase * 2f + i * 1.1f) * (0.3f + flow);
            float hem = hemHeight + flow * 4f + 0.6f * ripple;
            Vector3 bottom = new Vector3(top.x, top.y, hem) + back * (1.2f + flow * 6f + ripple) + j.HipAxis * (0.6f * k);
            Vector3 middle = (top + bottom) * 0.5f + back * (0.5f + 0.7f * flow) + Up * (0.4f * flow);
            if (!cape.Lining)
            {
                canvas.Capsule(World(top), World(middle), 1.6f, 2f, CharacterMaterial.Cloak, CharacterPart.Cape);
                canvas.Capsule(World(middle), World(bottom), 2f, 2f, CharacterMaterial.Cloak, CharacterPart.Cape);
                continue;
            }

            Vector3 outward = (back + j.ShoulderAxis * (0.6f * k)).normalized * 0.55f;
            Vector3 hemDrop = new(0f, 0f, -1.4f);
            canvas.Capsule(World(top + outward), World(middle + outward), 1.6f, 2f, CharacterMaterial.Cloak, CharacterPart.Cape);
            canvas.Capsule(World(middle + outward), World(bottom + outward), 2f, 2f, CharacterMaterial.Cloak, CharacterPart.Cape);
            canvas.Capsule(World(top - outward), World(middle - outward), 1.5f, 1.85f, CharacterMaterial.Lining, CharacterPart.Cape);
            canvas.Capsule(World(middle - outward), World(bottom - outward + hemDrop), 1.85f, 1.85f, CharacterMaterial.Lining, CharacterPart.Cape);
        }
    }

    /// <summary>
    /// Draws <paramref name="bag"/>, if it is a satchel: its strap from the left shoulder across the chest to the right
    /// hip and round the back, and its pouch hanging at the right hip, over the skirt.
    /// </summary>
    private void DrawBag(CharacterJoints j, AdventurerBag bag)
    {
        if (!bag.Satchel)
        {
            return;
        }

        DrawSling(j, -1f, CharacterMaterial.Strap, CharacterPart.Strap);
        Vector3 at = bag.Pouch;
        Vector3 size = bag.PouchSize;
        Vector3 centre = World(j.Pelvis + j.HipAxis * at.x + j.HipForward * at.y + j.Spine * at.z);
        Canvas.Ellipsoid(centre, World(j.HipAxis) * size.x, World(j.HipForward) * size.y, World(j.Spine) * size.z, (int)CharacterMaterial.Pouch, (int)CharacterPart.Belt);
    }

    /// <summary>
    /// Draws a strap round the torso from the shoulder on <paramref name="side"/> (-1 left, 1 right) across the chest
    /// to the other hip and back over the back.
    /// </summary>
    private void DrawSling(CharacterJoints j, float side, CharacterMaterial material, CharacterPart part)
    {
        CharacterCanvas canvas = Canvas;
        Vector3 middle = (j.Pelvis + j.Neck) * 0.5f;
        Vector3 along = (j.Spine * 0.8f + j.ShoulderAxis * (0.6f * side)).normalized;
        float reach = Vector3.Distance(j.Pelvis, j.Neck) * 0.62f;
        float depth = recipe.ChestRadius + 0.35f;
        for (int i = 0; i < 48; i++)
        {
            float angle = 2f * Mathf.PI * i / 48f;
            Vector3 p = middle + along * (Mathf.Cos(angle) * reach) + j.TorsoForward * (Mathf.Sin(angle) * depth);
            canvas.Sphere(World(p), 0.55f, material, part);
        }
    }

    /// <inheritdoc />
    protected override void DrawCarried(CharacterPose pose, CharacterJoints joints)
    {
        if (Step(CharacterSlot.Weapon))
        {
            DrawGear(joints, recipe.Weapons[layer.Option]);
        }
    }

    /// <summary>
    /// Draws <paramref name="carry"/>'s gear: in a clip without a weapon, its weapon stowed where it is carried with
    /// what holds it; in the weapon's own clips, where the weapon is in hand, only what holds it (an empty scabbard, the
    /// sling, the quiver); in another weapon's clips nothing.
    /// </summary>
    private void DrawGear(CharacterJoints j, AdventurerCarry carry)
    {
        CharacterWeaponArt carried = carry.Weapon;
        if (carried == null || (Weapon != null && Weapon != carried))
        {
            return;
        }

        bool stowed = Weapon == null;
        if (carry.Place == AdventurerCarryPlace.Hip)
        {
            DrawAtHip(j, carry, stowed);
            return;
        }

        if (carry.Sling)
        {
            DrawSling(j, carry.Quiver ? -carry.Side : carry.Side, CharacterMaterial.Strap, CharacterPart.Sling);
        }

        if (carry.Quiver)
        {
            DrawQuiver(j, carry);
        }

        if (!stowed)
        {
            return;
        }

        float side = carry.Side;
        Vector3 offset = carry.Offset;
        float lean = carry.Angles.x * Mathf.Deg2Rad;
        Vector3 middle = j.Neck + j.ShoulderAxis * (side * offset.x) - j.TorsoForward * (recipe.ChestRadius + offset.y + CapeDepth(carry)) - j.Spine * offset.z;
        Vector3 up = (j.Spine * Mathf.Cos(lean) + j.ShoulderAxis * (side * Mathf.Sin(lean))).normalized;
        WeaponPainter painter = new(carried, Canvas, World);
        if (carried.Kind == WeaponKind.Bow)
        {
            painter.DrawBow(middle, up, -j.TorsoForward, 0f, 0f, false, middle);
            return;
        }

        Vector3 down = -up;
        Vector3 hand = middle - down * ((carried.Length - carried.Pommel) * 0.5f);
        painter.DrawBladeAcross(hand, down, Vector3.Cross(down, j.TorsoForward).normalized, true);
    }

    /// <summary>
    /// Draws a blade's scabbard hanging from the belt at a hip, tipped back and out, with the hilt in it when
    /// <paramref name="stowed"/>.
    /// </summary>
    private void DrawAtHip(CharacterJoints j, AdventurerCarry carry, bool stowed)
    {
        CharacterWeaponArt carried = carry.Weapon;
        float side = carry.Side;
        Vector3 offset = carry.Offset;
        float back = carry.Angles.x * Mathf.Deg2Rad;
        float outward = carry.Angles.y * Mathf.Deg2Rad;
        Vector3 mouth = j.Pelvis + j.HipAxis * (side * offset.x) + j.HipForward * offset.y + j.Spine * offset.z;
        Vector3 hang = (-Up * Mathf.Cos(back) - j.HipForward * Mathf.Sin(back)).normalized;
        Vector3 down = (hang * Mathf.Cos(outward) + j.HipAxis * (side * Mathf.Sin(outward))).normalized;
        float sheath = carried.Length - carried.GuardAt;
        if (carry.Scabbard)
        {
            Vector2 blade = carried.BladeRadius;
            Canvas.Capsule(World(mouth), World(mouth + down * sheath), blade.x + 0.5f, blade.y + 0.45f, CharacterMaterial.Belt, CharacterPart.Scabbard);
            Canvas.Sphere(World(mouth + down * sheath), blade.y + 0.6f, CharacterMaterial.Fittings, CharacterPart.Scabbard);
            Canvas.Sphere(World(mouth + down * 0.3f), blade.x + 0.6f, CharacterMaterial.Fittings, CharacterPart.Scabbard);
        }

        if (stowed)
        {
            Vector3 hand = mouth - down * carried.GuardAt;
            new WeaponPainter(carried, Canvas, World).DrawBladeAcross(hand, down, Vector3.Cross(down, j.HipAxis).normalized, !carry.Scabbard);
        }
    }

    /// <summary>
    /// How much further out <paramref name="carry"/>'s gear sits on the back over the cape the layer is drawn under, so
    /// a weapon slung on the back shows over a cape.
    /// </summary>
    private float CapeDepth(AdventurerCarry carry)
    {
        return layer.Variant < recipe.Capes.Count && recipe.Capes[layer.Variant].Cape ? carry.CapeDepth : 0f;
    }

    /// <summary>
    /// Draws a quiver on the back, its mouth over the shoulder opposite <paramref name="carry"/>'s side, with the
    /// arrows' fletching standing out of it.
    /// </summary>
    private void DrawQuiver(CharacterJoints j, AdventurerCarry carry)
    {
        CharacterCanvas canvas = Canvas;
        float side = -carry.Side;
        Vector4 place = carry.QuiverPlace;
        float lean = place.w * Mathf.Deg2Rad;
        Vector3 middle = j.Neck + j.ShoulderAxis * (side * place.x) - j.TorsoForward * (recipe.ChestRadius + place.y + CapeDepth(carry)) - j.Spine * place.z;
        Vector3 up = (j.Spine * Mathf.Cos(lean) + j.ShoulderAxis * (side * Mathf.Sin(lean))).normalized;
        Vector3 mouth = middle + up * 4.6f;
        canvas.Capsule(World(middle - up * 4.6f), World(mouth), 1.35f, 1.55f, CharacterMaterial.Belt, CharacterPart.Quiver);
        canvas.Capsule(World(mouth - up * 0.6f), World(mouth), 1.75f, 1.75f, CharacterMaterial.Fittings, CharacterPart.Quiver);
        Vector3 across = Vector3.Cross(up, j.TorsoForward).normalized;
        for (int k = -1; k <= 1; k++)
        {
            Vector3 nock = mouth + up * (2.4f + 0.5f * (1 - Mathf.Abs(k))) + across * (0.7f * k);
            canvas.Capsule(World(mouth - up * 0.5f + across * (0.7f * k)), World(nock), 0.3f, 0.3f, CharacterMaterial.Shaft, CharacterPart.Arrow, 0.25f, 0.55f);
            canvas.Capsule(World(nock - up * 0.4f), World(nock - up * 2.2f), 0.55f, 0.45f, CharacterMaterial.Fletching, CharacterPart.Arrow, minRadius: 0.5f);
        }
    }

    /// <summary>
    /// Draws the head's layer, laid out by angle from the face's middle (yaw across, pitch up) with the head tipped toward
    /// the camera by the face tilt, so it lines up in every direction and turns with the head. The body's: the ears and a
    /// bare ball of skin, the hair's size, with the eyes, brows, mouth and blush painted at their angles
    /// (<see cref="DrawFace"/>). The hair's: the same ball in hair but for the face, drawn just before the body's so it
    /// covers the scalp (<see cref="DrawHairBall"/>), then the locks (<see cref="DrawLocks"/>). The headwear's
    /// (<see cref="DrawHood"/>, <see cref="DrawCap"/>). Each in a drawing step of its own.
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
        if (Step(null) && ears.z > 0f)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 ear = head + Direction(tiltedForward, faceRight, tiltedUp, ears.x * side, ears.y) * (headRadius * 0.92f);
                canvas.Sphere(ear, ears.z * headRadius, CharacterMaterial.Skin, side < 0 ? CharacterPart.LeftEar : CharacterPart.RightEar);
            }
        }

        if (Step(CharacterSlot.Hair))
        {
            DrawHairBall(recipe.Hairs[layer.Option], head, headRadius, tiltedForward, faceRight, tiltedUp);
        }

        if (Step(null))
        {
            canvas.Sphere(head, headRadius, CharacterMaterial.Skin, CharacterPart.Head);
            DrawFace(pose, head, headRadius, tiltedForward, faceRight, tiltedUp);
        }

        if (Step(CharacterSlot.Hair))
        {
            DrawLocks(pose, recipe.Hairs[layer.Option], recipe.Headwear[layer.Variant], head, headRadius, tiltedForward, faceRight, tiltedUp);
        }

        if (Step(CharacterSlot.Headwear))
        {
            AdventurerHeadwearPart headwear = recipe.Headwear[layer.Option];
            if (headwear.Kind == AdventurerHeadwear.Hood)
            {
                DrawHood(headwear, head, headRadius, tiltedForward, faceRight, tiltedUp);
            }
            else if (headwear.Kind == AdventurerHeadwear.Cap)
            {
                DrawCap(headwear, head, headRadius, tiltedForward, faceRight, tiltedUp);
            }
        }
    }

    /// <summary>
    /// Paints the face's features on the bare head, <paramref name="head"/> its middle and <paramref name="headRadius"/>
    /// its radius: the eyes, their glints, the brows, the blush and the mouth at their angles, where the face faces the
    /// camera.
    /// </summary>
    private void DrawFace(CharacterPose pose, Vector3 head, float headRadius, Vector3 tiltedForward, Vector3 faceRight, Vector3 tiltedUp)
    {
        CharacterCanvas canvas = Canvas;
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
    }

    /// <summary>
    /// Draws <paramref name="hair"/>'s ball, the head's, <paramref name="head"/> its middle and
    /// <paramref name="hairRadius"/> its radius, in hair but where the face shows (inside the face's ellipse, below the
    /// fringe hanging in points from above), so the body's face shows there.
    /// </summary>
    private void DrawHairBall(AdventurerHair hair, Vector3 head, float hairRadius, Vector3 forward, Vector3 right, Vector3 up)
    {
        CharacterCanvas canvas = Canvas;
        canvas.Sphere(head, hairRadius, CharacterMaterial.Hair, CharacterPart.Head);
        Vector3 face = hair.Face;
        Vector4 fringe = hair.Fringe;
        for (int y = 0; y < canvas.Height; y++)
        {
            for (int x = 0; x < canvas.Width; x++)
            {
                if (!canvas.IsFilled(x, y))
                {
                    continue;
                }

                Vector3 world = WorldNormalAt(x, y);
                float yaw = Mathf.Atan2(Vector3.Dot(world, right), Vector3.Dot(world, forward)) * Mathf.Rad2Deg;
                float pitch = Mathf.Asin(Mathf.Clamp(Vector3.Dot(world, up), -1f, 1f)) * Mathf.Rad2Deg;
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

                canvas.ClearPixel(x, y);
            }
        }
    }

    /// <summary>
    /// Draws <paramref name="hair"/>'s locks that <paramref name="headwear"/> does not cover, growing from the head's
    /// ball (<paramref name="head"/> its middle, <paramref name="hairRadius"/> its radius) in the face's frame, their tips
    /// lifted with the pose.
    /// </summary>
    private void DrawLocks(CharacterPose pose, AdventurerHair hair, AdventurerHeadwearPart headwear, Vector3 head, float hairRadius, Vector3 forward, Vector3 right, Vector3 up)
    {
        CharacterCanvas canvas = Canvas;
        foreach (HairLock hairLock in hair.Locks)
        {
            if (Covers(headwear, hairLock.Root))
            {
                continue;
            }

            Vector3 root = head + Direction(forward, right, up, hairLock.Root.x, hairLock.Root.y) * (hairRadius * 0.85f);
            Vector3 tip = root + (right * hairLock.Tip.x + up * hairLock.Tip.y + forward * hairLock.Tip.z) * hairRadius + Up * (pose.HairLift * hairLock.Tip.magnitude);
            canvas.Capsule(root, tip, hairLock.Radius.x * hairRadius, hairLock.Radius.y * hairRadius, CharacterMaterial.Hair, CharacterPart.Head);
        }
    }

    /// <summary>
    /// Whether <paramref name="headwear"/> covers a lock growing from <paramref name="root"/> (degrees across and up from
    /// the face's middle), which is then not drawn: a hood every lock outside its opening, a cap every lock above its
    /// rim.
    /// </summary>
    private static bool Covers(AdventurerHeadwearPart headwear, Vector2 root)
    {
        switch (headwear.Kind)
        {
            case AdventurerHeadwear.Hood:
            {
                Vector3 opening = headwear.HoodOpening;
                float across = Mathf.DeltaAngle(0f, root.x) / opening.x;
                float upward = (root.y - opening.y) / opening.z;
                return across * across + upward * upward >= 1f;
            }
            case AdventurerHeadwear.Cap:
                return root.y > headwear.Cap.y;
            default:
                return false;
        }
    }

    /// <summary>
    /// Draws a hood around the hair, <paramref name="head"/> its middle and <paramref name="hairRadius"/> the hair's
    /// radius: a shell whose opening, an ellipse of angles from the face's middle, shows the face, the shell's inside in
    /// shadow around it, and a point hanging behind.
    /// </summary>
    private void DrawHood(AdventurerHeadwearPart headwear, Vector3 head, float hairRadius, Vector3 forward, Vector3 right, Vector3 up)
    {
        CharacterCanvas canvas = Canvas;
        Vector3 hood = headwear.Hood;
        Vector3 opening = headwear.HoodOpening;
        float radius = hairRadius * hood.x;
        float Field(Vector3 p)
        {
            Vector3 d = p - head;
            float r = d.magnitude;
            if (r < 1e-4f)
            {
                return -radius;
            }

            float yaw = Mathf.Atan2(Vector3.Dot(d, right), Vector3.Dot(d, forward)) * Mathf.Rad2Deg;
            float pitch = Mathf.Asin(Mathf.Clamp(Vector3.Dot(d, up) / r, -1f, 1f)) * Mathf.Rad2Deg;
            float across = yaw / opening.x;
            float upward = (pitch - opening.y) / opening.z;
            float outside = (Mathf.Sqrt(across * across + upward * upward) - 1f) * r * 0.9f;
            return Mathf.Max(r - radius, -outside);
        }

        int Material(Vector3 p, Vector3 n)
        {
            return (int)((p - head).magnitude > radius - 0.2f ? CharacterMaterial.Headwear : CharacterMaterial.HoodInside);
        }

        canvas.Field(Field, new Bounds(head, Vector3.one * (radius * 2f + 1f)), Material, (int)CharacterPart.Headwear);
        canvas.Capsule(head + up * (0.55f * radius) - forward * (0.45f * radius), head + Up * (hood.z * radius) - forward * (hood.y * radius), 0.4f * radius, 0.1f * radius, CharacterMaterial.Headwear, CharacterPart.Headwear);
    }

    /// <summary>
    /// Draws a cap over the hair, <paramref name="head"/> its middle and <paramref name="hairRadius"/> the hair's radius:
    /// a crown, the part of a sphere above a rim tilted with the face, and a flat brim reaching forward from its front.
    /// </summary>
    private void DrawCap(AdventurerHeadwearPart headwear, Vector3 head, float hairRadius, Vector3 forward, Vector3 right, Vector3 up)
    {
        CharacterCanvas canvas = Canvas;
        Vector3 cap = headwear.Cap;
        float radius = hairRadius * cap.x;
        float rim = radius * Mathf.Sin(cap.y * Mathf.Deg2Rad);
        float Field(Vector3 p)
        {
            Vector3 d = p - head;
            return Mathf.Max(d.magnitude - radius, rim - Vector3.Dot(d, up));
        }

        canvas.Field(Field, new Bounds(head, Vector3.one * (radius * 2f + 1f)), (p, n) => (int)CharacterMaterial.Headwear, (int)CharacterPart.Headwear);
        Vector3 brimUp = (up * 0.94f + forward * 0.34f).normalized;
        Vector3 brimForward = Vector3.Cross(brimUp, right).normalized;
        Vector3 centre = head + up * rim + forward * (radius * 0.72f);
        canvas.Ellipsoid(centre, right * (radius * 0.72f), brimForward * (radius * cap.z), brimUp * (radius * 0.08f), (int)CharacterMaterial.Headwear, (int)CharacterPart.Headwear);
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
