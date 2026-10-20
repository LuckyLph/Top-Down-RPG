using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The global layout of seamless worlds (Docs/WorldStreaming.md: World origins). Every loaded world shares one physics
/// world and one camera, so no two worlds' rectangles may come within <see cref="MinimumGap"/> units of each other, and
/// the layout stays within <see cref="Extent"/> units of the origin, where float precision is a sixteenth of an art
/// pixel. New worlds take the first free slot of a grid <see cref="SlotSize"/> units apart, twice the largest world, so
/// worlds in different slots are always far enough apart.
/// </summary>
public static class WorldLayout
{
    public const int SlotSize = 2048;
    public const int MinimumGap = 1024;
    public const int Extent = 16384;

    /// <summary>
    /// The rectangle, in world units, of a world at <paramref name="origin"/> <paramref name="sizeInChunks"/> chunks
    /// across.
    /// </summary>
    public static RectInt RectOf(Vector2Int origin, Vector2Int sizeInChunks)
    {
        return new RectInt(origin, sizeInChunks * ChunkCoords.Size);
    }

    /// <summary>
    /// The rectangle of <paramref name="world"/> in world units.
    /// </summary>
    public static RectInt RectOf(WorldDefinition world)
    {
        return RectOf(world.Origin, world.SizeInChunks);
    }

    /// <summary>
    /// The distance between the nearest points of <paramref name="a"/> and <paramref name="b"/>; 0 when they touch or
    /// overlap.
    /// </summary>
    public static float Gap(RectInt a, RectInt b)
    {
        int dx = Mathf.Max(0, Mathf.Max(a.xMin - b.xMax, b.xMin - a.xMax));
        int dy = Mathf.Max(0, Mathf.Max(a.yMin - b.yMax, b.yMin - a.yMax));
        return Mathf.Sqrt((float)dx * dx + (float)dy * dy);
    }

    /// <summary>
    /// Why a world at <paramref name="origin"/>, <paramref name="sizeInChunks"/> chunks across, does not fit the layout
    /// beside <paramref name="others"/> (<paramref name="self"/> and null entries ignored), or null when it fits.
    /// </summary>
    public static string Problem(Vector2Int origin, Vector2Int sizeInChunks, IEnumerable<WorldDefinition> others, WorldDefinition self = null)
    {
        RectInt rect = RectOf(origin, sizeInChunks);
        if (rect.xMin < -Extent || rect.yMin < -Extent || rect.xMax > Extent || rect.yMax > Extent)
        {
            return $"its rectangle {Describe(rect)} leaves the layout, which stays within {Extent} units of the origin";
        }

        foreach (WorldDefinition other in others)
        {
            if (other == null || other == self)
            {
                continue;
            }

            float gap = Gap(rect, RectOf(other));
            if (gap < MinimumGap)
            {
                return $"its rectangle {Describe(rect)} comes within {gap:0} units of {other.name}'s {Describe(RectOf(other))}; worlds stay at least {MinimumGap} units apart";
            }
        }

        return null;
    }

    /// <summary>
    /// A line for every world of <paramref name="worlds"/> that leaves the layout, and for every pair that comes too
    /// close, naming them; empty when the layout holds.
    /// </summary>
    public static List<string> Validate(IReadOnlyList<WorldDefinition> worlds)
    {
        List<string> problems = new();
        WorldDefinition[] none = new WorldDefinition[0];
        for (int i = 0; i < worlds.Count; i++)
        {
            WorldDefinition world = worlds[i];
            if (world == null)
            {
                continue;
            }

            string alone = Problem(world.Origin, world.SizeInChunks, none);
            if (alone != null)
            {
                problems.Add($"World {world.name} (origin {world.Origin}): {alone}.");
            }

            for (int j = i + 1; j < worlds.Count; j++)
            {
                if (worlds[j] == null || worlds[j] == world)
                {
                    continue;
                }

                string close = Problem(world.Origin, world.SizeInChunks, new[] { worlds[j] });
                if (close != null && alone == null)
                {
                    problems.Add($"World {world.name} (origin {world.Origin}): {close}.");
                }
            }
        }

        return problems;
    }

    /// <summary>
    /// The first slot origin, row by row from the origin outwards, where a world <paramref name="sizeInChunks"/> chunks
    /// across fits beside <paramref name="worlds"/> (<see cref="Problem"/>); false when the layout is full.
    /// </summary>
    public static bool TryFindFreeOrigin(Vector2Int sizeInChunks, IEnumerable<WorldDefinition> worlds, out Vector2Int origin)
    {
        List<WorldDefinition> placed = new(worlds);
        int slots = Extent / SlotSize;
        for (int y = 0; y < slots; y++)
        {
            for (int x = 0; x < slots; x++)
            {
                origin = new Vector2Int(x * SlotSize, y * SlotSize);
                if (Problem(origin, sizeInChunks, placed) == null)
                {
                    return true;
                }
            }
        }

        origin = default;
        return false;
    }

    /// <summary>
    /// Every <see cref="WorldDefinition"/> asset in the project, by path.
    /// </summary>
    public static List<WorldDefinition> AllWorlds()
    {
        List<WorldDefinition> worlds = new();
        foreach (string guid in AssetDatabase.FindAssets($"t:{nameof(WorldDefinition)}"))
        {
            WorldDefinition world = AssetDatabase.LoadAssetAtPath<WorldDefinition>(AssetDatabase.GUIDToAssetPath(guid));
            if (world != null)
            {
                worlds.Add(world);
            }
        }

        return worlds;
    }

    /// <summary>
    /// <paramref name="rect"/> as "(xMin, yMin) to (xMax, yMax)".
    /// </summary>
    private static string Describe(RectInt rect)
    {
        return $"({rect.xMin}, {rect.yMin}) to ({rect.xMax}, {rect.yMax})";
    }
}
