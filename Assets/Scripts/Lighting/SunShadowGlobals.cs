using UnityEngine;

/// <summary>
/// The global shader properties of the sun's shadows (Docs/Lighting.md, Phase 4), which the lit shaders, the caster
/// shader and <see cref="SunShadowFeature"/> read: the sun's light (<c>_SunColour</c>), its shadow vector and whether
/// it casts (<c>_SunShadow</c>), the mask the feature draws (<c>_SunShadowMask</c>, <c>_SunShadowRect</c>,
/// <c>_SunShadowMaskMatrix</c>), and the rendering layers of the casters, which read their shadow at their feet
/// (<c>_SunShadowFeetLayers</c>).
/// </summary>
public static class SunShadowGlobals
{
    public static readonly int SunColourId = Shader.PropertyToID("_SunColour");
    public static readonly int SunShadowId = Shader.PropertyToID("_SunShadow");
    public static readonly int MaskId = Shader.PropertyToID("_SunShadowMask");
    public static readonly int MaskRectId = Shader.PropertyToID("_SunShadowRect");
    public static readonly int MaskMatrixId = Shader.PropertyToID("_SunShadowMaskMatrix");
    public static readonly int FeetLayersId = Shader.PropertyToID("_SunShadowFeetLayers");

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
    /// The sun as last set: its shadow vector, and whether it casts.
    /// </summary>
    public static Vector2 Shadow(out bool casts)
    {
        Vector4 shadow = Shader.GetGlobalVector(SunShadowId);
        casts = shadow.z > 0.5f;
        return new Vector2(shadow.x, shadow.y);
    }
}
