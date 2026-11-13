using UnityEngine;

/// <summary>
/// The logo a <see cref="TitleViewRecipe"/> draws: its size and pixels, rows from the bottom up.
/// </summary>
public sealed class LogoPicture
{
    /// <summary>
    /// A picture <paramref name="width"/> x <paramref name="height"/> px of <paramref name="pixels"/>.
    /// </summary>
    public LogoPicture(int width, int height, Color32[] pixels)
    {
        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }
    public int Height { get; }
    public Color32[] Pixels { get; }
}
