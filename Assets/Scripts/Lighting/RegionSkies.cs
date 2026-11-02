using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The region skies of the loaded chunks (Docs/Lighting.md, Phase 8), which each chunk's scope hands over as it loads
/// (<see cref="ChunkRegionSky"/>) and takes back as it unloads. <see cref="At"/> blends the skies reaching a place of a
/// world into one tint and cloud cover, the day's own where none does. Presentation each machine works out on its own.
/// </summary>
public sealed class RegionSkies
{
    /// <summary>
    /// A loaded chunk of a world.
    /// </summary>
    private readonly struct Key : IEquatable<Key>
    {
        public Key(WorldDefinition world, Vector2Int coords)
        {
            World = world;
            Coords = coords;
        }

        public WorldDefinition World { get; }
        public Vector2Int Coords { get; }

        public bool Equals(Key other)
        {
            return ReferenceEquals(World, other.World) && Coords == other.Coords;
        }

        public override bool Equals(object other)
        {
            return other is Key key && Equals(key);
        }

        public override int GetHashCode()
        {
            return (World != null ? World.GetHashCode() : 0) * 397 ^ Coords.GetHashCode();
        }
    }

    private readonly Dictionary<Key, ChunkSky> chunks = new();

    /// <summary>
    /// How many chunks' skies are held.
    /// </summary>
    public int Count => chunks.Count;

    /// <summary>
    /// Holds <paramref name="sky"/>, the region skies of the chunk at <paramref name="coords"/> of
    /// <paramref name="world"/>.
    /// </summary>
    public void Add(WorldDefinition world, Vector2Int coords, ChunkSky sky)
    {
        if (world != null && sky != null)
        {
            chunks[new Key(world, coords)] = sky;
        }
    }

    /// <summary>
    /// Lets <paramref name="sky"/>, the chunk at <paramref name="coords"/> of <paramref name="world"/>, go, when it is
    /// the one held there.
    /// </summary>
    public void Remove(WorldDefinition world, Vector2Int coords, ChunkSky sky)
    {
        Key key = new(world, coords);
        if (chunks.TryGetValue(key, out ChunkSky held) && held == sky)
        {
            chunks.Remove(key);
        }
    }

    /// <summary>
    /// The sky at <paramref name="localPosition"/> of <paramref name="world"/>: each region sky's tint and cloud cover
    /// by its weight there, the day's own (white, 1) by what weight is left, the weights scaled down where they add up
    /// to more than one. The day's own where the chunk there is not loaded or has no region sky.
    /// </summary>
    public RegionSkyBlend At(WorldDefinition world, Vector2 localPosition)
    {
        if (world == null)
        {
            return RegionSkyBlend.None;
        }

        Vector2Int coords = ChunkCoords.ChunkOf(ChunkCoords.CellOf(localPosition));
        if (!chunks.TryGetValue(new Key(world, coords), out ChunkSky sky) || sky == null)
        {
            return RegionSkyBlend.None;
        }

        Vector2 cell = localPosition - (Vector2)(coords * ChunkCoords.Size);
        IReadOnlyList<RegionSky> skies = sky.Skies;
        float total = 0f;
        for (int i = 0; i < skies.Count; i++)
        {
            total += skies[i] != null ? sky.WeightAt(i, cell) : 0f;
        }

        if (total <= 0f)
        {
            return RegionSkyBlend.None;
        }

        float scale = total > 1f ? 1f / total : 1f;
        float rest = 1f - Mathf.Min(total, 1f);
        Color tint = new(rest, rest, rest, 1f);
        float cover = rest;
        for (int i = 0; i < skies.Count; i++)
        {
            if (skies[i] == null)
            {
                continue;
            }

            float weight = sky.WeightAt(i, cell) * scale;
            Color skyTint = skies[i].Tint;
            tint.r += skyTint.r * weight;
            tint.g += skyTint.g * weight;
            tint.b += skyTint.b * weight;
            cover += skies[i].CloudCover * weight;
        }

        return new RegionSkyBlend(tint, cover);
    }
}
