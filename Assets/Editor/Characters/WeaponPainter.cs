using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Draws a <see cref="CharacterWeaponArt"/> onto a <see cref="CharacterCanvas"/>: a blade (grip, pommel, crossguard
/// and blade) with its smear and dust, or a bow (limbs, grip and string) with a nocked arrow and the flash of its
/// release, and an arrow on its own. Points and directions are given in a space of the caller's, which
/// <c>toWorld</c> turns into the canvas's world (a character's own space turned to its facing, or the world itself),
/// up being +z in both. <see cref="CharacterPainter"/> draws the weapon a character holds with it; the weapon sprite
/// writer draws icons and flying arrows.
/// </summary>
public sealed class WeaponPainter
{
    private static readonly Vector3 Up = new(0f, 0f, 1f);

    private readonly CharacterWeaponArt weapon;
    private readonly CharacterCanvas canvas;
    private readonly Func<Vector3, Vector3> world;

    /// <summary>
    /// Creates a painter of <paramref name="weaponArt"/> onto <paramref name="target"/>, mapping the caller's space to
    /// the canvas's world with <paramref name="toWorld"/>.
    /// </summary>
    public WeaponPainter(CharacterWeaponArt weaponArt, CharacterCanvas target, Func<Vector3, Vector3> toWorld)
    {
        weapon = weaponArt;
        canvas = target;
        world = toWorld;
    }

    /// <summary>
    /// How far an arrow's head reaches past its nock: the shaft and the tip beyond it.
    /// </summary>
    public float ArrowReach => weapon.ArrowLength + 1.2f;

    /// <summary>
    /// Draws a blade held at <paramref name="hand"/> along <paramref name="direction"/>: the grip down to a pommel,
    /// the crossguard across it (level when it can be, else along <paramref name="shoulderAxis"/>) and the tapering
    /// blade.
    /// </summary>
    public void DrawBlade(Vector3 hand, Vector3 direction, Vector3 shoulderAxis)
    {
        Vector3 across = Vector3.Cross(direction, Up);
        across = across.magnitude < 0.2f ? shoulderAxis.normalized : across.normalized;
        DrawBladeAcross(hand, direction, across, true);
    }

    /// <summary>
    /// Draws the blade held at <paramref name="hand"/> pointing along <paramref name="direction"/>, its crossguard along
    /// <paramref name="across"/>: the grip, the pommel, the crossguard and, with <paramref name="withBlade"/>, the blade
    /// (a sheathed blade's hilt alone).
    /// </summary>
    public void DrawBladeAcross(Vector3 hand, Vector3 direction, Vector3 across, bool withBlade)
    {
        Vector3 guard = hand + direction * weapon.GuardAt;
        canvas.Capsule(world(hand - direction * weapon.Pommel), world(guard), 0.6f, 0.6f, CharacterMaterial.Grip, CharacterPart.Weapon, minRadius: 0.6f);
        canvas.Sphere(world(hand - direction * (weapon.Pommel + 0.4f)), 0.8f, CharacterMaterial.Guard, CharacterPart.Weapon, 0.7f);
        canvas.Capsule(world(guard - across * weapon.GuardHalfWidth), world(guard + across * weapon.GuardHalfWidth), 0.65f, 0.65f, CharacterMaterial.Guard, CharacterPart.Weapon, minRadius: 0.6f);
        if (withBlade)
        {
            canvas.Capsule(world(guard + direction * 0.4f), world(hand + direction * weapon.Length), weapon.BladeRadius.x, weapon.BladeRadius.y, CharacterMaterial.Steel, CharacterPart.Weapon, minRadius: 0.62f);
        }
    }

    /// <summary>
    /// Draws the area the blade's outer part swept through its samples, oldest first: a band from an inner share of
    /// the blade to just past its tip, narrowing toward the oldest sample, in three colours from the newest part to the
    /// oldest.
    /// </summary>
    public void DrawSmear(IReadOnlyList<BladeSample> samples)
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
    /// Draws a puff of dust on the ground at <paramref name="point"/>, spreading with <paramref name="size"/>.
    /// </summary>
    public void DrawDust(Vector3 point, float size)
    {
        DustPuff(point, size, 0f, 0f, 1.9f);
        DustPuff(point, size, 2.2f, 0.9f, 1.5f);
        DustPuff(point, size, -2f, 1.2f, 1.4f);
        DustPuff(point, size, 0.8f, -1.8f, 1.3f);
        DustPuff(point, size, -1f, 2.4f, 1.1f);
        DustPuff(point, size, 2.8f, -1f, 1f);
    }

    /// <summary>
    /// Draws the bow held at <paramref name="grip"/> along <paramref name="bowUp"/> (unit length) and shooting along
    /// <paramref name="aim"/> (unit length, across it): its limbs tapering from the grip to the tips and curling back
    /// further as it is <paramref name="draw"/>n (their tips flicking forward a little), the grip, the string from the
    /// tips to the nock (pulled toward <paramref name="drawHand"/> by the draw, snapping past rest by the
    /// <paramref name="kick"/>), and, when <paramref name="nocked"/>, the arrow from the nock through the grip.
    /// </summary>
    public void DrawBow(Vector3 grip, Vector3 bowUp, Vector3 aim, float draw, float kick, bool nocked, Vector3 drawHand)
    {
        float drawn = Mathf.Clamp01(draw);
        float bend = Mathf.Lerp(weapon.Bend.x, weapon.Bend.y, drawn);
        const int segments = 12;
        Vector3 previous = Vector3.zero;
        Vector3 top = Vector3.zero;
        Vector3 bottom = Vector3.zero;
        for (int i = 0; i <= segments; i++)
        {
            float u = -1f + 2f * i / segments;
            float curl = bend * u * u - 0.9f * Mathf.Max(Mathf.Abs(u) - 0.8f, 0f) / 0.2f * (draw < 0.5f ? 1f : 0.4f);
            Vector3 point = grip + bowUp * (weapon.HalfLength * u) - aim * curl;
            if (i == 0)
            {
                bottom = point;
            }
            else
            {
                float radius = Mathf.Lerp(weapon.LimbRadius.x, weapon.LimbRadius.y, Mathf.Abs(-1f + 2f * (i - 0.5f) / segments));
                canvas.Capsule(world(previous), world(point), radius, radius, CharacterMaterial.Wood, CharacterPart.Bow, minRadius: 0.62f);
            }

            previous = point;
            top = point;
        }

        canvas.Capsule(world(grip - bowUp * 1.2f), world(grip + bowUp * 1.2f), 0.95f, 0.95f, CharacterMaterial.Grip, CharacterPart.Bow, minRadius: 0.62f);
        Vector3 rest = grip - aim * weapon.Brace;
        Vector3 nock = rest + (drawHand - rest) * drawn + aim * (1.4f * kick);
        canvas.Capsule(world(top), world(nock), 0.25f, 0.25f, CharacterMaterial.BowString, CharacterPart.Bow, 0.25f, 0.5f);
        canvas.Capsule(world(nock), world(bottom), 0.25f, 0.25f, CharacterMaterial.BowString, CharacterPart.Bow, 0.25f, 0.5f);
        if (!nocked)
        {
            return;
        }

        Vector3 toGrip = grip - nock;
        Vector3 along = toGrip.magnitude > 1f ? toGrip.normalized : aim;
        DrawArrow(nock, along, bowUp);
    }

    /// <summary>
    /// Draws an arrow from its nock at <paramref name="nock"/> along <paramref name="along"/> (unit length): the shaft,
    /// the head, and the fletching spread across <paramref name="up"/>.
    /// </summary>
    public void DrawArrow(Vector3 nock, Vector3 along, Vector3 up)
    {
        Vector3 head = nock + along * weapon.ArrowLength;
        canvas.Capsule(world(nock), world(head), 0.3f, 0.3f, CharacterMaterial.Shaft, CharacterPart.Arrow, 0.25f, 0.55f);
        canvas.Capsule(world(head - along * 0.4f), world(head + along * 1.2f), 0.75f, 0.2f, CharacterMaterial.Steel, CharacterPart.Arrow, minRadius: 0.55f);
        Vector3 side = Vector3.Cross(along, up).normalized;
        for (int k = -1; k <= 1; k += 2)
        {
            canvas.Capsule(world(nock + along * 0.6f), world(nock + along * 2.6f + side * (0.9f * k)), 0.35f, 0.35f, CharacterMaterial.Fletching, CharacterPart.Arrow, minRadius: 0.5f);
        }
    }

    /// <summary>
    /// Draws an arrow's release at the bow: a burst of four short rays around the grip and a streak along the aim with
    /// two shorter ones beside it.
    /// </summary>
    public void DrawRelease(Vector3 grip, Vector3 aim, Vector3 bowUp)
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
    /// The smear's inner and outer edge at sample <paramref name="index"/>, in the world.
    /// </summary>
    private void SmearEdge(IReadOnlyList<BladeSample> samples, int index, float start, out Vector3 inner, out Vector3 outer)
    {
        BladeSample sample = samples[index];
        float age = index / (float)(samples.Count - 1);
        float share = Mathf.Lerp(weapon.SmearInnerShare.x, weapon.SmearInnerShare.y, age);
        inner = world(sample.Hand + sample.Direction * (start + (weapon.Length - start) * share));
        outer = world(sample.Hand + sample.Direction * (weapon.Length + 0.3f));
    }

    /// <summary>
    /// One ball of a dust puff, offset from its middle by (<paramref name="x"/>, <paramref name="y"/>) times its size.
    /// </summary>
    private void DustPuff(Vector3 point, float size, float x, float y, float radius)
    {
        Vector3 p = point + new Vector3(x * size, y * size, 0.9f * size);
        canvas.Sphere(world(p), radius * (0.75f + 0.35f * size), CharacterMaterial.Dust, CharacterPart.Effect);
    }

    /// <summary>
    /// Draws a thin line of an effect from <paramref name="a"/> to <paramref name="b"/>.
    /// </summary>
    private void DrawRay(Vector3 a, Vector3 b, CharacterMaterial colour, float radius, float minRadius)
    {
        canvas.Capsule(world(a), world(b), radius, radius, colour, CharacterPart.Effect, 0.25f, minRadius);
    }
}
