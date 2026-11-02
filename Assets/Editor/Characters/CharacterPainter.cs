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
/// Draws a solved pose of a body plan onto a <see cref="CharacterCanvas"/>, facing a direction, in the order the depth
/// test needs no help with: the body below the head (<see cref="DrawBody"/>, the body plan's own), then the weapon with
/// its smear and effects (through a <see cref="WeaponPainter"/>), the head (<see cref="DrawHead"/>, the body plan's
/// own), and last a cast's light at the casting hands (<see cref="DrawLight"/>), just in front of each hand so the body
/// hides it when the hand is behind it. A carried lantern is drawn after all of it (<see cref="PaintLantern"/>), so the
/// baker can shade the frame without it first. <see cref="RoguePainter"/> and <see cref="WeaselPainter"/> are the body
/// plans.
/// </summary>
public abstract class CharacterPainter
{
    protected static readonly Vector3 Up = new(0f, 0f, 1f);
    private const float WickShowsThrough = 2.2f;
    private const float BailSpread = 0.55f;
    private const float FineStep = 0.2f;
    private const float FinePixels = 0.5f;

    private readonly CharacterRecipe recipe;
    private readonly CharacterWeaponArt weapon;
    private readonly CharacterCanvas canvas;

    /// <summary>
    /// Creates a painter of <paramref name="characterRecipe"/>'s character holding <paramref name="weaponArt"/> (or
    /// none) onto <paramref name="target"/>.
    /// </summary>
    protected CharacterPainter(CharacterRecipe characterRecipe, CharacterWeaponArt weaponArt, CharacterCanvas target)
    {
        recipe = characterRecipe;
        weapon = weaponArt;
        canvas = target;
    }

    protected CharacterCanvas Canvas => canvas;
    protected Vector3 Forward { get; private set; }
    protected Vector3 Right { get; private set; }

    /// <summary>
    /// Draws <paramref name="pose"/>, solved as <paramref name="joints"/>, facing <paramref name="facingDegrees"/>
    /// (counterclockwise from east), with the blade's <paramref name="smear"/> samples (none for no smear, oldest first)
    /// and <paramref name="effects"/>.
    /// </summary>
    public void Paint(float facingDegrees, CharacterPose pose, CharacterJoints joints, IReadOnlyList<BladeSample> smear, IReadOnlyList<MotionEffect> effects)
    {
        Face(facingDegrees);
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

        DrawHead(pose, joints);
        foreach (MotionEffect effect in effects)
        {
            if (effect.IsLight)
            {
                DrawLights(effect, joints);
            }
        }
    }

    /// <summary>
    /// Draws <paramref name="lantern"/> over a frame painted facing <paramref name="facingDegrees"/> with
    /// <paramref name="pose"/>, solved as <paramref name="joints"/>: hanging from the left hand, or from the hook at the
    /// left hip while the weapon takes that hand, swinging with the pose's stride; and marks where its light shines from,
    /// the glass's middle, whether or not the body hides it.
    /// </summary>
    public void PaintLantern(CharacterLanternArt lantern, float facingDegrees, CharacterPose pose, CharacterJoints joints)
    {
        Face(facingDegrees);
        float swing = pose.LanternSwing;
        Vector3 hook;
        Vector3 down;
        if (weapon != null && weapon.TakesLeftHand)
        {
            Vector3 at = lantern.HipHook;
            hook = joints.Pelvis + joints.Spine * at.z + joints.HipAxis * at.x + joints.HipForward * at.y;
            float tilt = (lantern.SwingDegrees * swing - lantern.HipTilt.x) * Mathf.Deg2Rad;
            down = (new Vector3(0f, Mathf.Sin(tilt), -Mathf.Cos(tilt)) - joints.HipAxis * lantern.HipTilt.y).normalized;
        }
        else
        {
            hook = joints.LeftHand - Up * (recipe.HandRadius * lantern.HandDrop);
            float tilt = lantern.SwingDegrees * lantern.HandSwingShare * swing * Mathf.Deg2Rad;
            down = new Vector3(0f, -Mathf.Sin(tilt), -Mathf.Cos(tilt));
        }

        canvas.MarkLantern(DrawLantern(lantern, World(hook), World(down)));
    }

    /// <summary>
    /// Draws everything below the head but the weapon.
    /// </summary>
    protected abstract void DrawBody(CharacterPose pose, CharacterJoints joints);

    /// <summary>
    /// Draws the head and its face.
    /// </summary>
    protected abstract void DrawHead(CharacterPose pose, CharacterJoints joints);

    /// <summary>
    /// A point of the character's space in the world, for the current facing.
    /// </summary>
    protected Vector3 World(Vector3 local)
    {
        return local.x * Right + local.y * Forward + local.z * Up;
    }

    /// <summary>
    /// The world offset that moves a point <paramref name="right"/> pixels right and <paramref name="up"/> pixels up on
    /// the screen, keeping its depth.
    /// </summary>
    protected Vector3 OnScreen(float right, float up)
    {
        return (CharacterCanvas.ViewRight * right + CharacterCanvas.ViewUp * up) / canvas.Scale;
    }

    /// <summary>
    /// The world direction a filled pixel's surface faces.
    /// </summary>
    protected Vector3 WorldNormalAt(int x, int y)
    {
        Vector3 n = canvas.NormalAt(x, y);
        return n.x * CharacterCanvas.ViewRight + n.y * CharacterCanvas.ViewUp + n.z * CharacterCanvas.ViewForward;
    }

    /// <summary>
    /// <paramref name="direction"/> turned counterclockwise about the up axis by <paramref name="radians"/>.
    /// </summary>
    protected static Vector3 Turn(Vector3 direction, float radians)
    {
        float c = Mathf.Cos(radians);
        float s = Mathf.Sin(radians);
        return new Vector3(direction.x * c - direction.y * s, direction.x * s + direction.y * c, direction.z);
    }

    /// <summary>
    /// Faces the character <paramref name="facingDegrees"/> counterclockwise from east.
    /// </summary>
    private void Face(float facingDegrees)
    {
        float facing = facingDegrees * Mathf.Deg2Rad;
        Forward = new Vector3(Mathf.Cos(facing), Mathf.Sin(facing), 0f);
        Right = new Vector3(Mathf.Sin(facing), -Mathf.Cos(facing), 0f);
    }

    /// <summary>
    /// Draws <paramref name="lantern"/> hanging from <paramref name="hook"/> along <paramref name="down"/>, both in the
    /// world, its sides square to the character's right: a bail, a pyramid roof with a knob, a box of glass between four
    /// corner bars with the wick nearer than the glass so it shows through, and a base. Returns the glass's middle.
    /// </summary>
    private Vector3 DrawLantern(CharacterLanternArt lantern, Vector3 hook, Vector3 down)
    {
        float s = lantern.Size;
        Vector3 up = -down;
        Vector3 ax = (Right - up * Vector3.Dot(Right, up)).normalized;
        Vector3 ay = Vector3.Cross(up, ax).normalized;
        float half = lantern.GlassHalfWidth * s;
        Vector3 apex = hook + down * (lantern.BailDrop * s);
        Vector3 ceiling = apex + down * (lantern.RoofHeight * s);
        Vector3 floor = ceiling + down * (lantern.GlassHeight * s);
        Vector3 bottom = floor + down * (lantern.BaseHeight * s);
        Vector3 middle = (ceiling + floor) * 0.5f;

        float bail = lantern.BailRadius;
        Vector3 spread = ax * (half * BailSpread);
        canvas.Capsule(hook + spread, apex, bail, bail, CharacterMaterial.LanternBrass, CharacterPart.LanternBail, FineStep, FinePixels);
        canvas.Capsule(hook - spread, apex, bail, bail, CharacterMaterial.LanternBrass, CharacterPart.LanternBail, FineStep, FinePixels);
        canvas.Capsule(hook + spread, hook - spread, bail, bail, CharacterMaterial.LanternBrass, CharacterPart.LanternBail, FineStep, FinePixels);

        canvas.Convex(new[]
        {
            Plane(ax, middle + ax * half), Plane(-ax, middle - ax * half), Plane(ay, middle + ay * half), Plane(-ay, middle - ay * half),
            Plane(up, ceiling), Plane(-up, floor)
        }, BoxBounds(ceiling, floor, half * 1.5f), CharacterMaterial.LanternGlass, CharacterPart.LanternGlass);

        float eave = half + lantern.Eave * s;
        Vector3[] outward = { ax, -ax, ay, -ay };
        Vector4[] roof = new Vector4[outward.Length + 1];
        for (int k = 0; k < outward.Length; k++)
        {
            roof[k] = Plane((outward[k] * (lantern.RoofHeight * s) + up * eave).normalized, apex);
        }

        roof[outward.Length] = Plane(-up, ceiling);
        canvas.Convex(roof, BoxBounds(apex, ceiling, eave * 1.3f), CharacterMaterial.LanternIron, CharacterPart.Lantern);
        canvas.Sphere(apex, lantern.KnobRadius * s, CharacterMaterial.LanternIron, CharacterPart.Lantern);

        float baseHalf = half + lantern.BaseOverhang * s;
        canvas.Convex(new[]
        {
            Plane(ax, floor + ax * baseHalf), Plane(-ax, floor - ax * baseHalf), Plane(ay, floor + ay * baseHalf), Plane(-ay, floor - ay * baseHalf),
            Plane(up, floor), Plane(down, bottom)
        }, BoxBounds(floor, bottom, baseHalf * 1.5f), CharacterMaterial.LanternIron, CharacterPart.Lantern);

        float bar = lantern.BarRadius * s;
        for (int x = -1; x <= 1; x += 2)
        {
            for (int y = -1; y <= 1; y += 2)
            {
                Vector3 corner = ax * (x * half) + ay * (y * half);
                canvas.Capsule(ceiling + corner, floor + corner, bar, bar, CharacterMaterial.LanternIron, CharacterPart.Lantern, FineStep, FinePixels);
            }
        }

        canvas.Sphere(middle - up * (lantern.WickDrop * s), lantern.WickRadius * s, CharacterMaterial.LanternWick, CharacterPart.LanternWick, FinePixels, -half * WickShowsThrough * canvas.Scale);
        return middle;
    }

    /// <summary>
    /// The plane through <paramref name="point"/> facing <paramref name="normal"/>, as <see cref="ArtCanvas.Convex"/>
    /// takes it.
    /// </summary>
    private static Vector4 Plane(Vector3 normal, Vector3 point)
    {
        return new Vector4(normal.x, normal.y, normal.z, Vector3.Dot(normal, point));
    }

    /// <summary>
    /// A box around the segment from <paramref name="a"/> to <paramref name="b"/>, <paramref name="margin"/> wider every
    /// way.
    /// </summary>
    private static Bounds BoxBounds(Vector3 a, Vector3 b, float margin)
    {
        Vector3 size = new(Mathf.Abs(a.x - b.x) + 2f * margin, Mathf.Abs(a.y - b.y) + 2f * margin, Mathf.Abs(a.z - b.z) + 2f * margin);
        return new Bounds((a + b) * 0.5f, size);
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
        canvas.MarkLight(centre, effect.LightStrength);
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
}
