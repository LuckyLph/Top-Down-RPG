using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One variant of a prop being drawn: the <see cref="ArtCanvas"/> it is drawn on (in cells, the base at the world
/// origin, the ground hiding what lies under it), the random stream every value is drawn from, the table of materials
/// the recipe adds (each marked as touching the ground or not, for the footprint) and the parts its shapes take, each
/// new part lined where it overlaps another.
/// </summary>
public sealed class PropDrawing
{
    private readonly List<ArtMaterial> materials = new();
    private readonly List<bool> touchesGround = new();
    private int nextPart;

    /// <summary>
    /// Creates the drawing of a variant on <paramref name="canvas"/>, drawing from <paramref name="random"/>.
    /// </summary>
    public PropDrawing(ArtCanvas canvas, System.Random random)
    {
        Canvas = canvas;
        Random = random;
    }

    /// <summary>
    /// The direction from the world toward the camera.
    /// </summary>
    public static Vector3 ToCamera => -ArtCanvas.ViewForward;

    public ArtCanvas Canvas { get; }
    public System.Random Random { get; }
    public IReadOnlyList<ArtMaterial> Materials => materials;

    /// <summary>
    /// Adds <paramref name="material"/> to the table and returns its id; its pixels low enough count as touching the
    /// ground when <paramref name="contact"/>.
    /// </summary>
    public int AddMaterial(ArtMaterial material, bool contact)
    {
        materials.Add(material);
        touchesGround.Add(contact);
        return materials.Count - 1;
    }

    /// <summary>
    /// Whether the pixels of material <paramref name="id"/> count as touching the ground.
    /// </summary>
    public bool TouchesGround(int id)
    {
        return id >= 0 && id < touchesGround.Count && touchesGround[id];
    }

    /// <summary>
    /// A part id no shape has taken yet.
    /// </summary>
    public int NewPart()
    {
        return nextPart++;
    }

    /// <summary>
    /// A value between <paramref name="min"/> and <paramref name="max"/>.
    /// </summary>
    public float Range(float min, float max)
    {
        return min + (float)Random.NextDouble() * (max - min);
    }

    /// <summary>
    /// A value between the two values of <paramref name="range"/>.
    /// </summary>
    public float Range(Vector2 range)
    {
        return Range(range.x, range.y);
    }

    /// <summary>
    /// A direction picked evenly over the sphere.
    /// </summary>
    public Vector3 OnSphere()
    {
        while (true)
        {
            Vector3 v = new(Range(-1f, 1f), Range(-1f, 1f), Range(-1f, 1f));
            float lengthSquared = v.sqrMagnitude;
            if (lengthSquared > 0.01f && lengthSquared <= 1f)
            {
                return v / Mathf.Sqrt(lengthSquared);
            }
        }
    }
}
