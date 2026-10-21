using UnityEngine;

/// <summary>
/// The eight directions a character is drawn in, counterclockwise from east, 45 degrees apart: a sheet's rows from the
/// top, the direction part of its slices' names, and the rows of a <see cref="CharacterAnimationClip"/>'s sprites. A
/// facing shows as the nearest direction, kept until the facing turns a margin past the boundary with the next one.
/// </summary>
public static class CharacterDirections
{
    public const int Count = 8;
    public const int East = 0;
    public const int North = 2;
    public const int West = 4;
    public const int South = 6;

    private const float HalfStep = 22.5f;

    private static readonly string[] Names = { "E", "NE", "N", "NW", "W", "SW", "S", "SE" };

    /// <summary>
    /// The facing of direction <paramref name="index"/> in degrees counterclockwise from east.
    /// </summary>
    public static float Angle(int index)
    {
        return index * 45f;
    }

    /// <summary>
    /// The short name of direction <paramref name="index"/> (E, NE, N, ...).
    /// </summary>
    public static string Name(int index)
    {
        return Names[index];
    }

    /// <summary>
    /// The direction nearest <paramref name="facing"/>; south for a zero facing.
    /// </summary>
    public static int Nearest(Vector2 facing)
    {
        if (facing.sqrMagnitude < 1e-8f)
        {
            return South;
        }

        float degrees = Mathf.Atan2(facing.y, facing.x) * Mathf.Rad2Deg;
        return Mathf.RoundToInt(Mathf.Repeat(degrees, 360f) / 45f) % Count;
    }

    /// <summary>
    /// <paramref name="current"/> while <paramref name="facing"/> lies within half a step plus
    /// <paramref name="marginDegrees"/> of it, else the direction nearest the facing; a zero facing keeps it.
    /// </summary>
    public static int Keep(int current, Vector2 facing, float marginDegrees)
    {
        if (facing.sqrMagnitude < 1e-8f)
        {
            return current;
        }

        float degrees = Mathf.Atan2(facing.y, facing.x) * Mathf.Rad2Deg;
        if (Mathf.Abs(Mathf.DeltaAngle(degrees, Angle(current))) <= HalfStep + marginDegrees)
        {
            return current;
        }

        return Nearest(facing);
    }
}
