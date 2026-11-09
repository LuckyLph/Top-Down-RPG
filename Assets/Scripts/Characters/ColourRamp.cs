using System;
using UnityEngine;

/// <summary>
/// The four colours a material is drawn in, darkest first: its outline, then its shadow, middle and light tones. The
/// editor's recipes draw with them, and a player's colours (<see cref="CharacterColours"/>) are made of them.
/// </summary>
[Serializable]
public struct ColourRamp : IEquatable<ColourRamp>
{
    public const int ToneCount = 4;

    [SerializeField] private Color32 outline;
    [SerializeField] private Color32 shadow;
    [SerializeField] private Color32 middle;
    [SerializeField] private Color32 light;

    /// <summary>
    /// Creates a ramp of the four colours, darkest first.
    /// </summary>
    public ColourRamp(Color32 outline, Color32 shadow, Color32 middle, Color32 light)
    {
        this.outline = outline;
        this.shadow = shadow;
        this.middle = middle;
        this.light = light;
    }

    public Color32 Outline => outline;
    public Color32 Shadow => shadow;
    public Color32 Middle => middle;
    public Color32 Light => light;

    /// <summary>
    /// The colour of <paramref name="tone"/>, darkest first: 0 the outline, 1 the shadow, 2 the middle and 3 the light.
    /// </summary>
    public Color32 Tone(int tone)
    {
        return tone switch
        {
            0 => outline,
            1 => shadow,
            2 => middle,
            _ => light
        };
    }

    /// <summary>
    /// Whether both ramps hold the same four colours.
    /// </summary>
    public bool Equals(ColourRamp other)
    {
        return outline.Equals(other.outline) && shadow.Equals(other.shadow) && middle.Equals(other.middle) && light.Equals(other.light);
    }

    /// <inheritdoc />
    public override bool Equals(object obj)
    {
        return obj is ColourRamp other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(outline, shadow, middle, light);
    }
}
