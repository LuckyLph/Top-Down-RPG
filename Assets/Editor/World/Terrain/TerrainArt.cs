using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The maths the terrain material recipes share, on square textures that repeat every <c>size</c> pixels and are
/// indexed row by row from the bottom, y up, like <see cref="Texture2D.SetPixels32"/>: periodic noise made of whole
/// waves, jittered seeds on a grid, a hash per pixel, colour shading, wrapped offsets, lighting from the top left as in
/// the terrain shader, and the height texture's pixels.
/// </summary>
public static class TerrainArt
{
    public const double LightX = -0.6;
    public const double LightY = 0.8;

    /// <summary>
    /// Smooth noise that repeats every <paramref name="size"/> pixels: the sum of <paramref name="waves"/>, each a sine
    /// with a random phase drawn from <paramref name="random"/> in order. Indexed y * size + x.
    /// </summary>
    public static double[] PeriodicNoise(System.Random random, IReadOnlyList<TerrainMaterialRecipe.Wave> waves, int size)
    {
        double[] phases = new double[waves.Count];
        for (int i = 0; i < phases.Length; i++)
        {
            phases[i] = Uniform(random, 0.0, 2.0 * Math.PI);
        }

        double[] grid = new double[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                double sum = 0.0;
                for (int i = 0; i < phases.Length; i++)
                {
                    TerrainMaterialRecipe.Wave wave = waves[i];
                    sum += wave.Amplitude * Math.Sin(2.0 * Math.PI * (wave.X * x + wave.Y * y) / size + phases[i]);
                }

                grid[y * size + x] = sum;
            }
        }

        return grid;
    }

    /// <summary>
    /// The sum of the absolute amplitudes of <paramref name="waves"/>, the most their noise can reach either way.
    /// </summary>
    public static double TotalAmplitude(IReadOnlyList<TerrainMaterialRecipe.Wave> waves)
    {
        double total = 0.0;
        foreach (TerrainMaterialRecipe.Wave wave in waves)
        {
            total += Math.Abs(wave.Amplitude);
        }

        return total;
    }

    /// <summary>
    /// The steepest slope per pixel that <paramref name="waves"/> can reach together over a texture of
    /// <paramref name="size"/> pixels.
    /// </summary>
    public static double MaxSlope(IReadOnlyList<TerrainMaterialRecipe.Wave> waves, int size)
    {
        double slope = 0.0;
        foreach (TerrainMaterialRecipe.Wave wave in waves)
        {
            slope += Math.Abs(wave.Amplitude) * 2.0 * Math.PI * Math.Sqrt(wave.X * wave.X + wave.Y * wave.Y) / size;
        }

        return slope;
    }

    /// <summary>
    /// One seed per cell of a <paramref name="cells"/> x <paramref name="cells"/> grid over the texture, moved from the
    /// cell's centre by up to <paramref name="jitter"/> cells along each axis, in rows from the bottom, wrapped into the
    /// texture.
    /// </summary>
    public static (double X, double Y)[] JitteredSeeds(System.Random random, int cells, double jitter, int size)
    {
        double cellSize = (double)size / cells;
        (double X, double Y)[] seeds = new (double X, double Y)[cells * cells];
        for (int j = 0; j < cells; j++)
        {
            for (int i = 0; i < cells; i++)
            {
                double x = Wrap((i + 0.5 + Uniform(random, -jitter, jitter)) * cellSize, size);
                double y = Wrap((j + 0.5 + Uniform(random, -jitter, jitter)) * cellSize, size);
                seeds[j * cells + i] = (x, y);
            }
        }

        return seeds;
    }

    /// <summary>
    /// A value in [<paramref name="min"/>, <paramref name="max"/>) from <paramref name="random"/>.
    /// </summary>
    public static double Uniform(System.Random random, double min, double max)
    {
        return min + (max - min) * random.NextDouble();
    }

    /// <summary>
    /// A value in [0, 1] that depends only on the pixel and <paramref name="salt"/>, for grit that every seed shares.
    /// </summary>
    public static double Hash01(int x, int y, int salt)
    {
        unchecked
        {
            uint h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)salt * 1442695041u;
            h = (h ^ (h >> 13)) * 1274126177u;
            return ((h ^ (h >> 16)) & 0xffffu) / 65535.0;
        }
    }

    /// <summary>
    /// <paramref name="value"/> rounded to the nearest whole number, halves up.
    /// </summary>
    public static int Round(double value)
    {
        return (int)Math.Floor(value + 0.5);
    }

    /// <summary>
    /// <paramref name="value"/> between 0 and 1.
    /// </summary>
    public static double Clamp01(double value)
    {
        return value < 0.0 ? 0.0 : value > 1.0 ? 1.0 : value;
    }

    /// <summary>
    /// <paramref name="value"/> rounded and clamped to a byte.
    /// </summary>
    public static byte ToByte(double value)
    {
        return (byte)Mathf.Clamp(Round(value), 0, 255);
    }

    /// <summary>
    /// <paramref name="colour"/> with <paramref name="amount"/> (0-255 steps) added to each channel, opaque.
    /// </summary>
    public static Color32 Shade(Color32 colour, double amount)
    {
        return new Color32(ToByte(colour.r + amount), ToByte(colour.g + amount), ToByte(colour.b + amount), 255);
    }

    /// <summary>
    /// The colour <paramref name="t"/> of the way from <paramref name="a"/> to <paramref name="b"/>, opaque.
    /// </summary>
    public static Color32 Lerp(Color32 a, Color32 b, double t)
    {
        return new Color32(ToByte(a.r + (b.r - a.r) * t), ToByte(a.g + (b.g - a.g) * t), ToByte(a.b + (b.b - a.b) * t), 255);
    }

    /// <summary>
    /// How much a pixel offset by (<paramref name="dx"/>, <paramref name="dy"/>) from its chip's or stone's seed faces
    /// the light: 1 towards the top left, -1 away from it.
    /// </summary>
    public static double BevelLight(double dx, double dy)
    {
        double length = Math.Sqrt(dx * dx + dy * dy);
        if (length <= 0.0)
        {
            length = 1.0;
        }

        return (dx * LightX + dy * LightY) / length;
    }

    /// <summary>
    /// <paramref name="value"/> wrapped into [0, <paramref name="size"/>).
    /// </summary>
    public static double Wrap(double value, int size)
    {
        double wrapped = value - size * Math.Floor(value / size);
        return wrapped >= size ? 0.0 : wrapped;
    }

    /// <summary>
    /// <paramref name="value"/> wrapped into [0, <paramref name="size"/>).
    /// </summary>
    public static int Wrap(int value, int size)
    {
        int wrapped = value % size;
        return wrapped < 0 ? wrapped + size : wrapped;
    }

    /// <summary>
    /// The shortest offset equal to <paramref name="delta"/> on a texture that repeats every <paramref name="size"/>
    /// pixels, in [-size / 2, size / 2).
    /// </summary>
    public static double WrapDelta(double delta, int size)
    {
        return delta - size * Math.Floor(delta / size + 0.5);
    }

    /// <summary>
    /// The shortest whole offset equal to <paramref name="delta"/> on a texture that repeats every
    /// <paramref name="size"/> pixels, in [-size / 2, size / 2).
    /// </summary>
    public static int WrapDelta(int delta, int size)
    {
        return Wrap(delta + size / 2, size) - size / 2;
    }

    /// <summary>
    /// The height texture's pixels: R the smooth <paramref name="height"/> in [0, 1], G the value of the chip or stone
    /// that owns each pixel (<paramref name="owner"/>, an index into <paramref name="values"/>, negative for none), 0.5
    /// where none does or when there are no owners, B the pixel's glint or sparkle from <paramref name="glints"/> (0 for
    /// neither, else <see cref="GlintByte"/> or <see cref="SparkleByte"/>; 0 everywhere without glints) and A opaque.
    /// </summary>
    public static Color32[] HeightPixels(double[] height, int[] owner, double[] values, byte[] glints = null)
    {
        Color32[] pixels = new Color32[height.Length];
        for (int i = 0; i < pixels.Length; i++)
        {
            double value = owner != null && owner[i] >= 0 ? values[owner[i]] : 0.5;
            pixels[i] = new Color32(ToByte(height[i] * 255.0), ToByte(value * 255.0), glints != null ? glints[i] : (byte)0, 255);
        }

        return pixels;
    }

    /// <summary>
    /// The height texture's blue channel for a glint at <paramref name="phase"/> of its period (wrapped into [0, 1)):
    /// 1 plus 127 times the phase, from 1 to 127, as the terrain shader reads it; 0 stays free for neither.
    /// </summary>
    public static byte GlintByte(double phase)
    {
        return (byte)(1 + Math.Min(126, (int)Math.Floor(Wrap(phase, 1) * 127.0)));
    }

    /// <summary>
    /// The height texture's blue channel for a sparkle at <paramref name="phase"/> of its period (wrapped into [0, 1)):
    /// 128 plus 128 times the phase, from 128 to 255, as the terrain shader reads it.
    /// </summary>
    public static byte SparkleByte(double phase)
    {
        return (byte)(128 + Math.Min(127, (int)Math.Floor(Wrap(phase, 1) * 128.0)));
    }
}
