using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One frame of a character being drawn: an <see cref="ArtCanvas"/> in rig units whose material and part ids are the
/// characters' <see cref="CharacterMaterial"/> and <see cref="CharacterPart"/>, shaded with a ramp per material and the
/// table of <see cref="CharacterMaterials"/> saying which are flat and which draw no outline. Nothing under the ground
/// is hidden.
/// </summary>
public sealed class CharacterCanvas : ArtCanvas
{
    private Vector2 lightSum;
    private int lightsMarked;
    private Vector2 lanternCentre;

    /// <summary>
    /// Creates an empty frame of <paramref name="width"/> x <paramref name="height"/> pixels whose world origin falls on
    /// pixel <paramref name="pivot"/> (from the bottom left), drawn at <paramref name="scale"/> pixels per rig unit.
    /// </summary>
    public CharacterCanvas(int width, int height, Vector2Int pivot, float scale)
        : base(width, height, pivot, scale)
    {
    }

    /// <summary>
    /// How strongly a cast's light drawn on the frame shines on what is around it, 0 (none drawn) to 1: the strongest
    /// light marked.
    /// </summary>
    public float LightStrength { get; private set; }

    /// <summary>
    /// Where a cast's light drawn on the frame shines from, in pixels from the pivot on the screen: the middle of the
    /// lights marked (between the hands, for a light at both); zero when none is drawn.
    /// </summary>
    public Vector2 LightCentre => lightsMarked > 0 ? lightSum / lightsMarked : Vector2.zero;

    /// <summary>
    /// Whether a carried lantern is drawn on the frame.
    /// </summary>
    public bool HasLantern { get; private set; }

    /// <summary>
    /// Where the lantern drawn on the frame shines from, in pixels from the pivot on the screen: its glass's middle;
    /// zero when none is drawn.
    /// </summary>
    public Vector2 LanternCentre => lanternCentre;

    /// <summary>
    /// Marks the lantern's light shining from <paramref name="world"/>, whether or not the body hides it.
    /// </summary>
    public void MarkLantern(Vector3 world)
    {
        Vector3 view = Project(world);
        lanternCentre = new Vector2(view.x, view.y);
        HasLantern = true;
    }

    /// <summary>
    /// Marks a cast's light shining from <paramref name="world"/> at <paramref name="strength"/>, whether or not the
    /// body hides it, so a light at a hand behind the body still lights what is around it.
    /// </summary>
    public void MarkLight(Vector3 world, float strength)
    {
        Vector3 view = Project(world);
        lightSum += new Vector2(view.x, view.y);
        lightsMarked++;
        LightStrength = Mathf.Max(LightStrength, strength);
    }

    /// <summary>
    /// The material of a filled pixel.
    /// </summary>
    public CharacterMaterial MaterialAt(int x, int y)
    {
        return (CharacterMaterial)MaterialIdAt(x, y);
    }

    /// <summary>
    /// The part of a filled pixel.
    /// </summary>
    public CharacterPart PartAt(int x, int y)
    {
        return (CharacterPart)PartIdAt(x, y);
    }

    /// <summary>
    /// Gives a filled pixel another material, keeping its depth, part and normal.
    /// </summary>
    public void SetMaterial(int x, int y, CharacterMaterial value)
    {
        SetMaterialId(x, y, (int)value);
    }

    /// <summary>
    /// How many pixels hold <paramref name="value"/>.
    /// </summary>
    public int Count(CharacterMaterial value)
    {
        return CountId((int)value);
    }

    /// <summary>
    /// Draws a sphere of <paramref name="radius"/> rig units, at least <paramref name="minRadius"/> pixels, around
    /// <paramref name="world"/>, its depth moved by <paramref name="depthOffset"/> pixels (see
    /// <see cref="ArtCanvas.Sphere"/>).
    /// </summary>
    public void Sphere(Vector3 world, float radius, CharacterMaterial value, CharacterPart owner, float minRadius = 0f, float depthOffset = 0f)
    {
        Sphere(world, radius, (int)value, (int)owner, minRadius, null, 0f, depthOffset);
    }

    /// <summary>
    /// Draws the solid inside every plane of <paramref name="planes"/> within <paramref name="bounds"/> (see
    /// <see cref="ArtCanvas.Convex"/>).
    /// </summary>
    public void Convex(IReadOnlyList<Vector4> planes, Bounds bounds, CharacterMaterial value, CharacterPart owner)
    {
        Convex(planes, bounds, (int)value, (int)owner);
    }

    /// <summary>
    /// Draws a flat disc facing the camera, <paramref name="radius"/> pixels around where <paramref name="world"/>
    /// lands (see <see cref="ArtCanvas.Disc"/>).
    /// </summary>
    public void Disc(Vector3 world, float radius, CharacterMaterial value, CharacterPart owner)
    {
        Disc(world, radius, (int)value, (int)owner);
    }

    /// <summary>
    /// Draws the one pixel <paramref name="world"/> falls in (see <see cref="ArtCanvas.Dot"/>).
    /// </summary>
    public void Dot(Vector3 world, CharacterMaterial value, CharacterPart owner)
    {
        Dot(world, (int)value, (int)owner);
    }

    /// <summary>
    /// Draws a chain of spheres from <paramref name="a"/> to <paramref name="b"/> (see <see cref="ArtCanvas.Capsule"/>).
    /// </summary>
    public void Capsule(Vector3 a, Vector3 b, float radiusA, float radiusB, CharacterMaterial value, CharacterPart owner, float step = 0.3f, float minRadius = 0f)
    {
        Capsule(a, b, radiusA, radiusB, (int)value, (int)owner, step, minRadius);
    }

    /// <summary>
    /// Draws a flat triangle facing the camera (see <see cref="ArtCanvas.Triangle"/>).
    /// </summary>
    public void Triangle(Vector3 a, Vector3 b, Vector3 c, CharacterMaterial value, CharacterPart owner)
    {
        Triangle(a, b, c, (int)value, (int)owner);
    }

    /// <summary>
    /// The frame's colours, rows from the bottom, each material in <paramref name="ramps"/> (indexed by
    /// <see cref="CharacterMaterial"/>), flat and outlined as <see cref="CharacterMaterials"/> says, lit above
    /// <paramref name="lightThreshold"/> and shadowed at or below <paramref name="shadowThreshold"/>, with inner lines
    /// past <paramref name="innerGap"/> (see <see cref="ArtCanvas.Shade"/>).
    /// </summary>
    public Color32[] Shade(ColourRamp[] ramps, float lightThreshold, float shadowThreshold, float innerGap)
    {
        ArtMaterial[] table = new ArtMaterial[ramps.Length];
        for (int i = 0; i < table.Length; i++)
        {
            CharacterMaterial value = (CharacterMaterial)i;
            table[i] = new ArtMaterial(ramps[i], CharacterMaterials.IsFlat(value), CharacterMaterials.HasOutline(value));
        }

        return Shade(table, lightThreshold, shadowThreshold, innerGap);
    }
}
