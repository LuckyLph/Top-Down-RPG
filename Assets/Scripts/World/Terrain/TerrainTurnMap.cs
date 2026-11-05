using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Which way the layers that turn to their runs (<see cref="TerrainMaterial.TurnsToRuns"/>, the planks) lie in each cell
/// of a renderer's area, as a texture for the terrain blending shader (Docs/KnownGaps.md, Phase 6): one point-filtered R8
/// texel per cell over the control map's bounds holding the <see cref="TerrainTile.Facing"/> of the tile there (as a
/// byte; the shader reads r * 255) on the highest such layer filled in the cell, read from the layer's own tilemap or,
/// past the chunk's cells, from the skirt source of the same layer covering it; automatic (0) where none is filled, so
/// the runs decide. 1 x 1 and automatic when no layer turns to its runs. Built again whenever the control map's bounds
/// or a turning layer's tiles change; owns its texture, <see cref="Dispose"/> destroys it.
/// </summary>
public sealed class TerrainTurnMap : IDisposable
{
    private readonly string textureName;
    private readonly List<int> turning = new();
    private Texture2D texture;
    private byte[] cells = new byte[0];
    private BoundsInt bounds;

    /// <summary>
    /// Creates an empty map whose texture will be named <paramref name="textureName"/>.
    /// </summary>
    public TerrainTurnMap(string textureName)
    {
        this.textureName = textureName;
    }

    public Texture2D Texture => texture;

    /// <summary>
    /// Reads the layers among <paramref name="layers"/> (null for a layer only a skirt source fills) whose bit is set in
    /// <paramref name="turnBits"/>, and the sources of those layers among <paramref name="sources"/> (null for none), over <paramref name="controlMap"/>'s bounds, and
    /// uploads the map.
    /// </summary>
    public void Build(TerrainControlMap controlMap, IReadOnlyList<Tilemap> layers, uint turnBits, IReadOnlyList<TerrainControlMap.Source> sources)
    {
        turning.Clear();
        for (int i = layers.Count - 1; i >= 0; i--)
        {
            if ((turnBits >> i & 1u) != 0u)
            {
                turning.Add(i);
            }
        }

        bool used = turning.Count > 0 && !controlMap.IsEmpty;
        bounds = used ? controlMap.Bounds : new BoundsInt(0, 0, 0, 1, 1, 1);
        int width = bounds.size.x;
        int height = bounds.size.y;
        if (texture == null || texture.width != width || texture.height != height)
        {
            DestroyTexture();
            texture = new Texture2D(width, height, TextureFormat.R8, false, true)
            {
                name = textureName,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };
            cells = new byte[width * height];
        }

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                cells[y * width + x] = used ? (byte)FacingAt(layers, sources, new Vector3Int(bounds.xMin + x, bounds.yMin + y, 0)) : (byte)TerrainTile.Facing.Automatic;
            }
        }

        texture.SetPixelData(cells, 0);
        texture.Apply(false, false);
    }

    /// <summary>
    /// The facing of <paramref name="cell"/> in the last build: automatic outside the map.
    /// </summary>
    public TerrainTile.Facing GetCell(Vector3Int cell)
    {
        int x = cell.x - bounds.xMin;
        int y = cell.y - bounds.yMin;
        return x < 0 || y < 0 || x >= bounds.size.x || y >= bounds.size.y || cells.Length == 0 ? TerrainTile.Facing.Automatic : (TerrainTile.Facing)cells[y * bounds.size.x + x];
    }

    /// <summary>
    /// Destroys the texture.
    /// </summary>
    public void Dispose()
    {
        DestroyTexture();
    }

    /// <summary>
    /// The facing of the tile on the highest turning layer filled at <paramref name="cell"/>, on its own tilemap or a
    /// source of it covering the cell; automatic when none is.
    /// </summary>
    private TerrainTile.Facing FacingAt(IReadOnlyList<Tilemap> layers, IReadOnlyList<TerrainControlMap.Source> sources, Vector3Int cell)
    {
        for (int t = 0; t < turning.Count; t++)
        {
            int layer = turning[t];
            TileBase tile = layers[layer] != null ? layers[layer].GetTile(cell) : null;
            for (int s = 0; tile == null && sources != null && s < sources.Count; s++)
            {
                if (sources[s].Layer == layer && sources[s].Tilemap != null && sources[s].Contains(cell))
                {
                    tile = sources[s].Tilemap.GetTile(cell);
                }
            }

            if (tile != null)
            {
                return TerrainTile.FacingOf(tile);
            }
        }

        return TerrainTile.Facing.Automatic;
    }

    /// <summary>
    /// Destroys the texture, immediately outside play mode.
    /// </summary>
    private void DestroyTexture()
    {
        if (texture == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            UnityEngine.Object.Destroy(texture);
        }
        else
        {
            UnityEngine.Object.DestroyImmediate(texture);
        }

        texture = null;
    }
}
