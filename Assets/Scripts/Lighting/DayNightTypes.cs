using System;
using UnityEngine;

/// <summary>
/// One key of the day's light (Docs/Lighting.md, Phase 3): at <see cref="Time"/> in the cycle (0 at sunrise, sunset at
/// the day's share of it), the sky's colour, the light that reaches everything, and the sun's, the light a sun shadow
/// takes away.
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

    /// <summary>
    /// Creates the key of <paramref name="keyTime"/> with its <paramref name="skyColour"/> and
    /// <paramref name="sunColour"/>.
    /// </summary>
    public DayLightKey(float keyTime, Color skyColour, Color sunColour)
    {
        time = keyTime;
        sky = skyColour;
        sun = sunColour;
    }

    public float Time => time;
    public Color Sky => sky;
    public Color Sun => sun;
}

/// <summary>
/// The day's light at a moment: the sky's and the sun's colours, and the two together, the light where no sun shadow
/// falls.
/// </summary>
public readonly struct DayLight
{
    /// <summary>
    /// Creates the light of <paramref name="skyColour"/> and <paramref name="sunColour"/>.
    /// </summary>
    public DayLight(Color skyColour, Color sunColour)
    {
        Sky = skyColour;
        Sun = sunColour;
    }

    public Color Sky { get; }
    public Color Sun { get; }

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
