using System;
using UnityEngine;

/// <summary>
/// One key of the day's light (Docs/Lighting.md, Phase 3): at <see cref="Time"/> in the cycle (0 at sunrise, sunset at
/// the day's share of it), the sky's colour, the light that reaches everything, the sun's, the light a sun shadow takes
/// away, and how strongly the lights that burn at night shine, as a share of their full strength (Phase 5).
/// </summary>
[Serializable]
public struct DayLightKey
{
    [SerializeField, Range(0f, 1f), Tooltip("Where in the cycle the key lies: 0 at sunrise, the day's share at sunset.")]
    private float time;
    [SerializeField, Tooltip("The sky's light, which reaches everything, shadows included.")]
    private Color sky;
    [SerializeField, Tooltip("The sun's light, added where no sun shadow falls.")]
    private Color sun;
    [SerializeField, Range(0f, 1f), Tooltip("How strongly the lights that burn at night shine (campfires, lamps, the ground's glow), as a share of their full strength.")]
    private float nightLights;

    /// <summary>
    /// Creates the key of <paramref name="keyTime"/> with its <paramref name="skyColour"/> and
    /// <paramref name="sunColour"/>, the lights that burn at night shining at <paramref name="nightLightShare"/>.
    /// </summary>
    public DayLightKey(float keyTime, Color skyColour, Color sunColour, float nightLightShare = 0f)
    {
        time = keyTime;
        sky = skyColour;
        sun = sunColour;
        nightLights = nightLightShare;
    }

    public float Time => time;
    public Color Sky => sky;
    public Color Sun => sun;
    public float NightLights => nightLights;
}

/// <summary>
/// The day's light at a moment: the sky's and the sun's colours, the two together, the light where no sun shadow
/// falls, and how strongly the lights that burn at night shine.
/// </summary>
public readonly struct DayLight
{
    /// <summary>
    /// Creates the light of <paramref name="skyColour"/> and <paramref name="sunColour"/>, the lights that burn at night
    /// shining at <paramref name="nightLightShare"/>.
    /// </summary>
    public DayLight(Color skyColour, Color sunColour, float nightLightShare = 0f)
    {
        Sky = skyColour;
        Sun = sunColour;
        NightLights = nightLightShare;
    }

    public Color Sky { get; }
    public Color Sun { get; }
    public float NightLights { get; }

    /// <summary>
    /// The sky and the sun together, opaque.
    /// </summary>
    public Color Total => new(Sky.r + Sun.r, Sky.g + Sun.g, Sky.b + Sun.b, 1f);
}

/// <summary>
/// Where the sun is at a moment (Docs/Lighting.md, Phase 3): whether it is up, how high, from 0 on the horizon to 1 at
/// noon, and the shadow it casts: where a point one unit above the ground lands on the ground, as an offset on the
/// screen in units (none at night).
/// </summary>
public readonly struct SunState
{
    /// <summary>
    /// Creates the sun at <paramref name="sunElevation"/> casting <paramref name="sunShadow"/>, up or not.
    /// </summary>
    public SunState(bool up, float sunElevation, Vector2 sunShadow)
    {
        IsUp = up;
        Elevation = sunElevation;
        Shadow = sunShadow;
    }

    public bool IsUp { get; }
    public float Elevation { get; }
    public Vector2 Shadow { get; }
}

/// <summary>
/// How a part of the lighting is drawn on this machine (the lighting dev panel): following the time of day, forced on
/// or forced off.
/// </summary>
public enum LightingOverride
{
    Follow,
    On,
    Off
}

/// <summary>
/// The skies of the regions around a place blended into one (Docs/Lighting.md, Phase 8): the tint the day's sky and sun
/// take there, and how much of the day's cloud cover lies over it.
/// </summary>
public readonly struct RegionSkyBlend
{
    /// <summary>
    /// The day's own sky: no tint, the day's cloud cover.
    /// </summary>
    public static readonly RegionSkyBlend None = new(Color.white, 1f);

    /// <summary>
    /// Creates the blend of <paramref name="skyTint"/> and <paramref name="cover"/>.
    /// </summary>
    public RegionSkyBlend(Color skyTint, float cover)
    {
        Tint = skyTint;
        CloudCover = cover;
    }

    public Color Tint { get; }
    public float CloudCover { get; }
}
