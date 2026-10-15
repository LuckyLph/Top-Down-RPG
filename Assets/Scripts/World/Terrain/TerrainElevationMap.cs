using System;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Which cells of a renderer's area are raised ground, as a texture for the terrain blending shader: one point-filtered
/// R8 texel per cell over the control map's bounds holding what the cell of the renderer's raised tilemap is
/// (<see cref="RaisedGround.KindOf"/>, as a byte; the shader reads r * 255). 1 x 1 and level without a
/// raised tilemap. Built again whenever the control map's bounds or the raised tilemap change; owns its texture,
/// <see cref="Dispose"/> destroys it.
/// </summary>
public sealed class TerrainElevationMap : IDisposable
{
    private readonly string textureName;
    private Texture2D texture;
    private byte[] cells = new byte[0];
    private BoundsInt bounds;

    /// <summary>
    /// Creates an empty map whose texture will be named <paramref name="textureName"/>.
    /// </summary>
    public TerrainElevationMap(string textureName)
    {
        this.textureName = textureName;
    }

    public Texture2D Texture => texture;

    /// <summary>
    /// Reads <paramref name="raised"/> (null for none) over <paramref name="controlMap"/>'s bounds and uploads the map.
    /// </summary>
    public void Build(TerrainControlMap controlMap, Tilemap raised)
    {
        bool used = raised != null && !controlMap.IsEmpty;
        bounds = used ? controlMap.Bounds : new BoundsInt(0, 0, 0, 1, 1, 1);
        int width = bounds.size.x;
        int height = bounds.size.y;
        if (texture == null || texture.width != width || texture.height != height)
        {
            if (texture != null)
            {
                DestroyTexture();
            }

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
                cells[y * width + x] = used ? RaisedGround.KindAt(raised, new Vector3Int(bounds.xMin + x, bounds.yMin + y, 0)) : RaisedGround.Level;
            }
        }

        texture.SetPixelData(cells, 0);
        texture.Apply(false, false);
    }

    /// <summary>
    /// The value of <paramref name="cell"/> in the last build: <see cref="RaisedGround.Level"/> outside the map.
    /// </summary>
    public byte GetCell(Vector3Int cell)
    {
        int x = cell.x - bounds.xMin;
        int y = cell.y - bounds.yMin;
        return x < 0 || y < 0 || x >= bounds.size.x || y >= bounds.size.y || cells.Length == 0 ? RaisedGround.Level : cells[y * bounds.size.x + x];
    }

    /// <summary>
    /// Destroys the texture.
    /// </summary>
    public void Dispose()
    {
        DestroyTexture();
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
