using UnityEngine;

/// <summary>
/// A surface pattern an <see cref="ArtMaterial"/> draws over its shading, each pixel one tone darker or lighter by
/// where it lies in the world: bark's vertical grooves, stone's speckles, and the rings of a cut end around the world's
/// up axis.
/// </summary>
public enum ArtPattern
{
    None,
    Bark,
    Speckle,
    Rings
}

/// <summary>
/// How an <see cref="ArtCanvas"/> draws one material id: its <see cref="ColourRamp"/>, whether it is flat (always its
/// middle tone) and outlined, a surface pattern, a tone offset (a crown's core one tone darker), a height over which
/// its tone rises from shadow to light (thin blades, lit by how high they reach rather than where they face), and a
/// cover: another material taken where the surface faces up past a threshold, broken up by value noise.
/// </summary>
public sealed class ArtMaterial
{
    /// <summary>
    /// Creates a material drawn in <paramref name="ramp"/>. <paramref name="patternScale"/> is the bark's grooves per
    /// unit across, the share of speckled pixels, or the rings per unit; <paramref name="cover"/> is the id of the
    /// material taken where the world normal's up component plus noise of <paramref name="coverNoise"/> (its full range)
    /// at <paramref name="coverNoiseScale"/> cycles per unit passes <paramref name="coverThreshold"/>, or -1 for none.
    /// </summary>
    public ArtMaterial(ColourRamp ramp, bool flat = false, bool outlined = true, ArtPattern pattern = ArtPattern.None, float patternScale = 1f,
        int toneOffset = 0, float toneHeight = 0f, int cover = -1, float coverThreshold = 1f, float coverNoise = 0f, float coverNoiseScale = 1f)
    {
        Ramp = ramp;
        Flat = flat;
        Outlined = outlined;
        Pattern = pattern;
        PatternScale = patternScale;
        ToneOffset = toneOffset;
        ToneHeight = toneHeight;
        Cover = cover;
        CoverThreshold = coverThreshold;
        CoverNoise = coverNoise;
        CoverNoiseScale = coverNoiseScale;
    }

    public ColourRamp Ramp { get; }
    public bool Flat { get; }
    public bool Outlined { get; }
    public ArtPattern Pattern { get; }
    public float PatternScale { get; }
    public int ToneOffset { get; }
    public float ToneHeight { get; }
    public int Cover { get; }
    public float CoverThreshold { get; }
    public float CoverNoise { get; }
    public float CoverNoiseScale { get; }
}

/// <summary>
/// An ellipsoid a shape's normals can lean toward, so clumps of a crown each catch the light while the crown is lit as
/// one mass.
/// </summary>
public sealed class ArtEnvelope
{
    private readonly Vector3 centre;
    private readonly Vector3 inverse;

    /// <summary>
    /// Creates the envelope of <paramref name="radii"/> around <paramref name="middle"/>, in world units.
    /// </summary>
    public ArtEnvelope(Vector3 middle, Vector3 radii)
    {
        centre = middle;
        inverse = new Vector3(1f / (radii.x * radii.x), 1f / (radii.y * radii.y), 1f / (radii.z * radii.z));
    }

    /// <summary>
    /// The envelope's outward normal at <paramref name="point"/>, in world space.
    /// </summary>
    public Vector3 NormalAt(Vector3 point)
    {
        Vector3 q = point - centre;
        return new Vector3(q.x * inverse.x, q.y * inverse.y, q.z * inverse.z).normalized;
    }
}

/// <summary>
/// Deterministic noise for patterns and covers: a hash of a lattice point, and value noise smoothly interpolated
/// between lattice points.
/// </summary>
public static class ArtNoise
{
    /// <summary>
    /// A value in [0, 1) for the lattice point (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>).
    /// </summary>
    public static float Hash(int x, int y, int z)
    {
        unchecked
        {
            uint h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)z * 1440662683u;
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777216f;
        }
    }

    /// <summary>
    /// Value noise in [0, 1] at <paramref name="point"/>, one lattice cell per unit.
    /// </summary>
    public static float Value(Vector3 point)
    {
        int x0 = Mathf.FloorToInt(point.x);
        int y0 = Mathf.FloorToInt(point.y);
        int z0 = Mathf.FloorToInt(point.z);
        float fx = Smooth(point.x - x0);
        float fy = Smooth(point.y - y0);
        float fz = Smooth(point.z - z0);
        float near = Mathf.Lerp(Mathf.Lerp(Hash(x0, y0, z0), Hash(x0 + 1, y0, z0), fx), Mathf.Lerp(Hash(x0, y0 + 1, z0), Hash(x0 + 1, y0 + 1, z0), fx), fy);
        float far = Mathf.Lerp(Mathf.Lerp(Hash(x0, y0, z0 + 1), Hash(x0 + 1, y0, z0 + 1), fx), Mathf.Lerp(Hash(x0, y0 + 1, z0 + 1), Hash(x0 + 1, y0 + 1, z0 + 1), fx), fy);
        return Mathf.Lerp(near, far, fz);
    }

    /// <summary>
    /// Smoothstep of <paramref name="t"/> in [0, 1].
    /// </summary>
    private static float Smooth(float t)
    {
        return t * t * (3f - 2f * t);
    }
}
