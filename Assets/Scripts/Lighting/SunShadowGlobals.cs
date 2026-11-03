using UnityEngine;

/// <summary>
/// The global shader properties of the sun's shadows (Docs/Lighting.md, Phase 4), which the lit shaders, the caster
/// shader and <see cref="SunShadowFeature"/> read: the sun's light (<c>_SunColour</c>), its shadow vector and whether
/// it casts (<c>_SunShadow</c>), the mask the feature draws (<c>_SunShadowMask</c>, <c>_SunShadowRect</c>,
/// <c>_SunShadowMaskMatrix</c>), the ground beside it (<c>_SunShadowGround</c>) and the tallest caster's height, as far
/// as a shadow reaches from raised ground (<c>_SunShadowTallest</c>, Phase 11), the rendering layers of the casters,
/// which read their shadow at their feet (<c>_SunShadowFeetLayers</c>), and the clouds the feature draws into the mask
/// (<c>_SunClouds</c>, <c>_SunCloudShape</c>, Phase 8).
/// </summary>
public static class SunShadowGlobals
{
    public static readonly int SunColourId = Shader.PropertyToID("_SunColour");
    public static readonly int SunShadowId = Shader.PropertyToID("_SunShadow");
    public static readonly int MaskId = Shader.PropertyToID("_SunShadowMask");
    public static readonly int MaskRectId = Shader.PropertyToID("_SunShadowRect");
    public static readonly int MaskMatrixId = Shader.PropertyToID("_SunShadowMaskMatrix");
    public static readonly int GroundId = Shader.PropertyToID("_SunShadowGround");
    public static readonly int TallestId = Shader.PropertyToID("_SunShadowTallest");
    public static readonly int FeetLayersId = Shader.PropertyToID("_SunShadowFeetLayers");
    public static readonly int CloudsId = Shader.PropertyToID("_SunClouds");
    public static readonly int CloudShapeId = Shader.PropertyToID("_SunCloudShape");

    /// <summary>
    /// Sets the sun: its light <paramref name="sun"/>, which a sun shadow takes away, and the shadow it casts,
    /// <paramref name="shadow"/>, where a point one unit up lands on the ground, while <paramref name="casts"/>; with
    /// nothing cast, no light is taken away anywhere.
    /// </summary>
    public static void Set(Color sun, Vector2 shadow, bool casts)
    {
        Shader.SetGlobalVector(SunColourId, casts ? new Vector4(sun.r, sun.g, sun.b, 0f) : Vector4.zero);
        Shader.SetGlobalVector(SunShadowId, casts ? new Vector4(shadow.x, shadow.y, 1f, 0f) : Vector4.zero);
    }

    /// <summary>
    /// Sets the clouds: their field drifted by <paramref name="drift"/>, a cloud wherever it lies above
    /// <paramref name="level"/>, taking <paramref name="shade"/> of the sun away (none draws no cloud), their noise's
    /// cells <paramref name="size"/> units across, their edges fading over <paramref name="edge"/>
    /// (<see cref="SunClouds"/>).
    /// </summary>
    public static void SetClouds(Vector2 drift, float level, float shade, float size, float edge)
    {
        Shader.SetGlobalVector(CloudsId, new Vector4(drift.x, drift.y, level, shade));
        Shader.SetGlobalVector(CloudShapeId, new Vector4(size, edge, SunClouds.TileCells, 0f));
    }

    /// <summary>
    /// Whether any cloud takes the sun away, as last set.
    /// </summary>
    public static bool Cloudy()
    {
        return Shader.GetGlobalVector(CloudsId).w > 0f;
    }

    /// <summary>
    /// The sun as last set: its shadow vector, and whether it casts.
    /// </summary>
    public static Vector2 Shadow(out bool casts)
    {
        Vector4 shadow = Shader.GetGlobalVector(SunShadowId);
        casts = shadow.z > 0.5f;
        return new Vector2(shadow.x, shadow.y);
    }
}
