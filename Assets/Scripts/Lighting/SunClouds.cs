using System;
using UnityEngine;

/// <summary>
/// The clouds whose shadows drift over the ground by day (Docs/Lighting.md, Phase 8), worked out from the world clock,
/// so every machine of a session sees the same: a field of octaves of gradient noise repeating over a tile of
/// <see cref="TileCells"/> cells of its first octave (a power of two, so the shader wraps its cells with a mask), each
/// the settings' cloud size across, drifting with the wind, with a cloud wherever it lies above the level that leaves
/// the cover asked for under cloud. The cover comes and goes:
/// each spell of the settings' length has a level of its own, hashed from its number, and the cover eases from one to
/// the next. The cloud shader (<c>SunShadowClouds.shader</c>) draws the same field into the sun shadow mask.
/// </summary>
public sealed class SunClouds
{
    public const int TileCells = 16;
    private const int Octaves = 4;
    private const int LevelSamples = 96;

    private readonly DayNightSettings settings;
    private readonly float[] levels = new float[LevelSamples * LevelSamples];

    /// <summary>
    /// Creates the clouds of <paramref name="daySettings"/>, sampling the field over its tile to find the level that
    /// leaves any share of it under cloud.
    /// </summary>
    public SunClouds(DayNightSettings daySettings)
    {
        settings = daySettings;
        int index = 0;
        for (int y = 0; y < LevelSamples; y++)
        {
            for (int x = 0; x < LevelSamples; x++)
            {
                levels[index++] = Field(new Vector2((x + 0.5f) * TileCells / LevelSamples, (y + 0.5f) * TileCells / LevelSamples));
            }
        }

        Array.Sort(levels);
    }

    /// <summary>
    /// The width of the tile the field repeats over, in units.
    /// </summary>
    public float TileUnits => TileCells * settings.CloudSize;

    /// <summary>
    /// How far the field has moved against the wind at <paramref name="worldSeconds"/> of the world clock, wrapped to
    /// its tile: the field the clouds show at a point is the one at that point plus this, so they move with the wind.
    /// </summary>
    public Vector2 DriftAt(double worldSeconds)
    {
        double tile = TileUnits;
        if (tile <= 0d)
        {
            return Vector2.zero;
        }

        Vector2 wind = settings.Wind;
        return new Vector2(Wrap(-wind.x * worldSeconds, tile), Wrap(-wind.y * worldSeconds, tile));
    }

    /// <summary>
    /// The day's cloud cover at <paramref name="worldSeconds"/> of the world clock: the settings' most cover times a
    /// level eased between those of the spells either side, 0 to 1 each.
    /// </summary>
    public float CoverAt(double worldSeconds)
    {
        double spells = worldSeconds / Math.Max(settings.CloudSpellSeconds, 1f);
        double first = Math.Floor(spells);
        long spell = (long)first;
        float into = (float)(spells - first);
        float eased = into * into * (3f - 2f * into);
        return settings.CloudCover * Mathf.Lerp(SpellLevel(spell), SpellLevel(spell + 1), eased);
    }

    /// <summary>
    /// The level of the field above which <paramref name="cover"/> of the ground lies under cloud; above the field's
    /// highest for none.
    /// </summary>
    public float LevelFor(float cover)
    {
        if (cover <= 0f)
        {
            return levels[^1] + 1f;
        }

        if (cover >= 1f)
        {
            return levels[0] - 1f;
        }

        return levels[Mathf.Clamp(Mathf.RoundToInt((1f - cover) * levels.Length), 0, levels.Length - 1)];
    }

    /// <summary>
    /// The field at <paramref name="world"/>, a point of the world in units, with the clouds drifted by
    /// <paramref name="drift"/>, as the cloud shader works it out.
    /// </summary>
    public float FieldAt(Vector2 world, Vector2 drift)
    {
        return Field((world + drift) / Mathf.Max(settings.CloudSize, 1e-3f));
    }

    /// <summary>
    /// The field at <paramref name="point"/>, in cells of its first octave: octaves of gradient noise, each twice as
    /// fine and half as strong, repeating over <see cref="TileCells"/> cells; about -1 to 1, most of it near 0.
    /// </summary>
    public static float Field(Vector2 point)
    {
        float field = 0f;
        float amplitude = 0.5f;
        int period = TileCells;
        for (int octave = 0; octave < Octaves; octave++)
        {
            field += amplitude * Noise(point, period);
            point *= 2f;
            period *= 2;
            amplitude *= 0.5f;
        }

        return field * 2f;
    }

    /// <summary>
    /// Gradient noise at <paramref name="point"/>, repeating every <paramref name="period"/> cells.
    /// </summary>
    private static float Noise(Vector2 point, int period)
    {
        int x = Mathf.FloorToInt(point.x);
        int y = Mathf.FloorToInt(point.y);
        float fx = point.x - x;
        float fy = point.y - y;
        float ux = fx * fx * fx * (fx * (fx * 6f - 15f) + 10f);
        float uy = fy * fy * fy * (fy * (fy * 6f - 15f) + 10f);
        float a = Dot(Gradient(x, y, period), fx, fy);
        float b = Dot(Gradient(x + 1, y, period), fx - 1f, fy);
        float c = Dot(Gradient(x, y + 1, period), fx, fy - 1f);
        float d = Dot(Gradient(x + 1, y + 1, period), fx - 1f, fy - 1f);
        return Mathf.Lerp(Mathf.Lerp(a, b, ux), Mathf.Lerp(c, d, ux), uy);
    }

    /// <summary>
    /// The gradient of the lattice cell (<paramref name="x"/>, <paramref name="y"/>), the cells wrapped to a tile of
    /// <paramref name="period"/>, a power of two.
    /// </summary>
    private static Vector2 Gradient(int x, int y, int period)
    {
        x &= period - 1;
        y &= period - 1;
        unchecked
        {
            uint hash = (uint)x * 374761393u + (uint)y * 668265263u + 2246822519u;
            hash = (hash ^ (hash >> 13)) * 1274126177u;
            hash ^= hash >> 16;
            float angle = hash * (2f * Mathf.PI / 4294967296f);
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        }
    }

    /// <summary>
    /// The dot product of <paramref name="gradient"/> and (<paramref name="x"/>, <paramref name="y"/>).
    /// </summary>
    private static float Dot(Vector2 gradient, float x, float y)
    {
        return gradient.x * x + gradient.y * y;
    }

    /// <summary>
    /// The cover level of spell <paramref name="spell"/>, hashed from its number, 0 to 1.
    /// </summary>
    private static float SpellLevel(long spell)
    {
        unchecked
        {
            ulong hash = (ulong)spell + 0x9E3779B97F4A7C15UL;
            hash = (hash ^ (hash >> 30)) * 0xBF58476D1CE4E5B9UL;
            hash = (hash ^ (hash >> 27)) * 0x94D049BB133111EBUL;
            hash ^= hash >> 31;
            return (hash >> 40) / 16777216f;
        }
    }

    /// <summary>
    /// <paramref name="value"/> wrapped into [0, <paramref name="period"/>).
    /// </summary>
    private static float Wrap(double value, double period)
    {
        double wrapped = value - period * Math.Floor(value / period);
        return (float)(wrapped >= period ? 0d : wrapped);
    }
}
