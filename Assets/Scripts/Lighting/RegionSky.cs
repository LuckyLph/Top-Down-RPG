using UnityEngine;

/// <summary>
/// The sky of a kind of region (Docs/Lighting.md, Phase 8): the tint its sky and sun take, and how much more or less
/// cloud it has than the day's. A region's area generator names its sky, the world generator writes where each sky
/// reaches into the chunks (<see cref="ChunkSky"/>), and the day and night blends the skies around the local player
/// into the light. Runtime code only reads it.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/Lighting/Region Sky", fileName = "Sky")]
public sealed class RegionSky : ScriptableObject
{
    [SerializeField, Tooltip("The tint the sky and the sun take in the region: white keeps the day's light.")]
    private Color tint = Color.white;
    [SerializeField, Min(0f), Tooltip("How much of the day's cloud cover the region has: 1 the day's, more for a cloudier or smokier sky, less for a clearer one.")]
    private float cloudCover = 1f;

    public Color Tint => tint;
    public float CloudCover => cloudCover;

    /// <summary>
    /// Creates an unsaved sky for tests.
    /// </summary>
    public static RegionSky Create(Color skyTint, float cover = 1f)
    {
        RegionSky sky = CreateInstance<RegionSky>();
        sky.tint = skyTint;
        sky.cloudCover = cover;
        return sky;
    }
}
