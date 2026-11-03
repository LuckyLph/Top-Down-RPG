using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Bakes each chunk's glow map from the whole world's ground (Docs/LightingGaps.md, Phase 5), so a chunk's renderer
/// draws its glowing ground's light (lava) without building the map as the chunk loads: the map the renderer would
/// build over its cells with its neighbours loaded (<see cref="TerrainGlowMap"/>), from the cells of the materials of
/// its layers that glow, within the reach of the first of them in its layers' order. A chunk's skirt reaches past the
/// glow's reach, so the renderer would read the same cells.
/// </summary>
public static class WorldGlowBake
{
    public const string NoGlow = "none";

    /// <summary>
    /// The first of <paramref name="chunkMaterials"/> that glows, whose reach and colour the chunk's glow takes; null
    /// when none glows.
    /// </summary>
    public static TerrainMaterial GlowingMaterialOf(IEnumerable<TerrainMaterial> chunkMaterials)
    {
        foreach (TerrainMaterial material in chunkMaterials)
        {
            if (material != null && material.Glows)
            {
                return material;
            }
        }

        return null;
    }

    /// <summary>
    /// The glow map of the chunk owning <paramref name="chunkCells"/>, whose renderer draws
    /// <paramref name="chunkMaterials"/>, as a readable linear RGBA32 texture named <paramref name="textureName"/> that
    /// the caller destroys, and the cells it covers in <paramref name="area"/>; null, with an empty area, when nothing
    /// glows within reach of the chunk.
    /// </summary>
    public static Texture2D BakeChunk(WorldCoastBake.WorldCells world, IEnumerable<TerrainMaterial> chunkMaterials, RectInt chunkCells, string textureName, out RectInt area)
    {
        area = new RectInt(0, 0, 0, 0);
        TerrainMaterial first = GlowingMaterialOf(chunkMaterials);
        if (first == null)
        {
            return null;
        }

        ulong glowing = GlowingBits(world, chunkMaterials);
        using TerrainGlowMap map = new(textureName);
        if (!map.Build((x, y) => (world.BitsAt(new Vector2Int(x, y)) & glowing) != 0ul, world.Area, chunkCells, first.GlowReach))
        {
            return null;
        }

        area = map.Area;
        Texture2D baked = new(map.Texture.width, map.Texture.height, TextureFormat.RGBA32, false, true)
        {
            name = textureName,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        baked.SetPixels32(map.Texture.GetPixels32());
        baked.Apply(false);
        return baked;
    }

    /// <summary>
    /// What the glow map of the chunk owning <paramref name="chunkCells"/> is baked from, as a short string: the first
    /// glowing material's name and reach, and which cells glow within that reach of the chunk, hashed; or
    /// <see cref="NoGlow"/> when none does. Equal strings bake equal maps, so the World Editor bakes again only the
    /// chunks whose string changed since their last bake.
    /// </summary>
    public static string SourceOf(WorldCoastBake.WorldCells world, IEnumerable<TerrainMaterial> chunkMaterials, RectInt chunkCells)
    {
        TerrainMaterial first = GlowingMaterialOf(chunkMaterials);
        if (first == null)
        {
            return NoGlow;
        }

        ulong glowing = GlowingBits(world, chunkMaterials);
        int grow = Mathf.CeilToInt(first.GlowReach);
        RectInt window = new(chunkCells.xMin - grow, chunkCells.yMin - grow, chunkCells.width + 2 * grow, chunkCells.height + 2 * grow);
        Hash128 hash = new();
        hash.Append(first.name);
        hash.Append(first.GlowReach);
        hash.Append(window.xMin);
        hash.Append(window.yMin);
        bool any = false;
        int word = 0;
        int count = 0;
        for (int y = window.yMin; y < window.yMax; y++)
        {
            for (int x = window.xMin; x < window.xMax; x++)
            {
                bool glows = (world.BitsAt(new Vector2Int(x, y)) & glowing) != 0ul;
                any |= glows;
                word = (word << 1) | (glows ? 1 : 0);
                if (++count % 32 == 0)
                {
                    hash.Append(word);
                    word = 0;
                }
            }
        }

        hash.Append(word);
        return any ? hash.ToString() : NoGlow;
    }

    /// <summary>
    /// The bits of <paramref name="world"/>'s materials that are among <paramref name="chunkMaterials"/> and glow.
    /// </summary>
    private static ulong GlowingBits(WorldCoastBake.WorldCells world, IEnumerable<TerrainMaterial> chunkMaterials)
    {
        HashSet<TerrainMaterial> listed = new(chunkMaterials);
        ulong bits = 0ul;
        for (int i = 0; i < world.MaterialCount; i++)
        {
            TerrainMaterial material = world.MaterialOf(i);
            if (material != null && material.Glows && listed.Contains(material))
            {
                bits |= 1ul << i;
            }
        }

        return bits;
    }
}
