using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Where the region skies reach in a composed world (Docs/Lighting.md, Phase 8): each region's sky is its generator's
/// (<see cref="AreaGenerator.Sky"/>), and a sky's weight at a point is full deeper than half the blend inside the cells
/// of the regions with that sky, none further than half the blend outside them, and eased between, from the point's
/// distance to the nearest cell centre in and out of them, so the sky changes over the blend's width across their
/// outline. <see cref="Plan"/> samples the weights of the skies reaching a chunk at every <see cref="ChunkSky.Spacing"/>
/// cells, as the chunk's <see cref="ChunkSky"/> holds them.
/// </summary>
internal sealed class RegionSkyField
{
    private readonly WorldComposition composition;
    private readonly int blend;
    private readonly int reach;
    private readonly List<RegionSky> skies = new();
    private readonly List<RectInt> skyBounds = new();
    private readonly short[] cellSkies;

    /// <summary>
    /// Reads which sky each cell of <paramref name="world"/> lies under, blending over <paramref name="blendCells"/>.
    /// </summary>
    public RegionSkyField(WorldComposition world, int blendCells)
    {
        composition = world;
        blend = Mathf.Max(1, blendCells);
        reach = blend / 2 + 2;
        Vector2Int size = composition.Size;
        cellSkies = new short[size.x * size.y];
        for (int i = 0; i < composition.Regions.Count; i++)
        {
            WorldComposition.Region region = composition.Regions[i];
            RegionSky sky = region.Generator != null ? region.Generator.Sky : null;
            if (sky == null)
            {
                continue;
            }

            int index = skies.IndexOf(sky);
            if (index < 0)
            {
                index = skies.Count;
                skies.Add(sky);
                skyBounds.Add(region.Bounds);
            }
            else
            {
                skyBounds[index] = Union(skyBounds[index], region.Bounds);
            }
        }

        for (int y = 0; y < size.y; y++)
        {
            for (int x = 0; x < size.x; x++)
            {
                int region = composition.RegionAt(new Vector2Int(x, y));
                AreaGenerator generator = region >= 0 ? composition.Regions[region].Generator : null;
                RegionSky sky = generator != null ? generator.Sky : null;
                cellSkies[y * size.x + x] = (short)(sky != null ? skies.IndexOf(sky) + 1 : 0);
            }
        }
    }

    /// <summary>
    /// Lists in <paramref name="chunk"/> the skies with any weight at its samples, in the order their regions were
    /// placed, and their weights, 0 to 255, sky by sky and row by row from its bottom left corner.
    /// </summary>
    public void Plan(WorldComposition.Chunk chunk)
    {
        chunk.Skies.Clear();
        RectInt bounds = ChunkCoords.Bounds(chunk.Coords);
        RectInt near = new(bounds.xMin - reach, bounds.yMin - reach, bounds.width + 2 * reach + 1, bounds.height + 2 * reach + 1);
        List<byte> weights = new();
        byte[] samples = new byte[ChunkSky.SamplesPerSky];
        for (int sky = 0; sky < skies.Count; sky++)
        {
            if (!skyBounds[sky].Overlaps(near))
            {
                continue;
            }

            bool any = false;
            for (int j = 0; j < ChunkSky.Samples; j++)
            {
                for (int i = 0; i < ChunkSky.Samples; i++)
                {
                    Vector2Int corner = new(bounds.xMin + i * ChunkSky.Spacing, bounds.yMin + j * ChunkSky.Spacing);
                    byte weight = (byte)Mathf.RoundToInt(Weight(sky, corner) * 255f);
                    samples[j * ChunkSky.Samples + i] = weight;
                    any |= weight > 0;
                }
            }

            if (any)
            {
                chunk.Skies.Add(skies[sky]);
                weights.AddRange(samples);
            }
        }

        chunk.SkyWeights = weights.ToArray();
    }

    /// <summary>
    /// The weight of <paramref name="sky"/> at <paramref name="corner"/>, a corner of the cells, from 0 to 1.
    /// </summary>
    public float Weight(RegionSky sky, Vector2Int corner)
    {
        int index = skies.IndexOf(sky);
        return index >= 0 ? Weight(index, corner) : 0f;
    }

    /// <summary>
    /// The weight of the sky at <paramref name="sky"/> in the list at <paramref name="corner"/>: its distance to the
    /// nearest cell centre outside the sky's cells less its distance to the nearest inside them, each at most the
    /// search's reach, eased from none at half the blend outside to full at half the blend inside.
    /// </summary>
    private float Weight(int sky, Vector2Int corner)
    {
        int width = composition.Size.x;
        int height = composition.Size.y;
        short own = (short)(sky + 1);
        float nearestIn = float.MaxValue;
        float nearestOut = float.MaxValue;
        for (int dy = -reach; dy < reach; dy++)
        {
            int y = corner.y + dy;
            float offsetY = dy + 0.5f;
            for (int dx = -reach; dx < reach; dx++)
            {
                int x = corner.x + dx;
                float offsetX = dx + 0.5f;
                float distance = offsetX * offsetX + offsetY * offsetY;
                bool inside = x >= 0 && y >= 0 && x < width && y < height && cellSkies[y * width + x] == own;
                if (inside)
                {
                    nearestIn = Mathf.Min(nearestIn, distance);
                }
                else
                {
                    nearestOut = Mathf.Min(nearestOut, distance);
                }
            }
        }

        float distanceIn = nearestIn == float.MaxValue ? reach : Mathf.Min(Mathf.Sqrt(nearestIn), reach);
        float distanceOut = nearestOut == float.MaxValue ? reach : Mathf.Min(Mathf.Sqrt(nearestOut), reach);
        float share = Mathf.Clamp01(0.5f - (distanceIn - distanceOut) / blend);
        return share * share * (3f - 2f * share);
    }

    /// <summary>
    /// The smallest rectangle holding <paramref name="a"/> and <paramref name="b"/>.
    /// </summary>
    private static RectInt Union(RectInt a, RectInt b)
    {
        int xMin = Mathf.Min(a.xMin, b.xMin);
        int yMin = Mathf.Min(a.yMin, b.yMin);
        return new RectInt(xMin, yMin, Mathf.Max(a.xMax, b.xMax) - xMin, Mathf.Max(a.yMax, b.yMax) - yMin);
    }
}
