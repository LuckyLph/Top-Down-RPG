using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Tilemaps;

/// <summary>
/// The ground layers' cells as a texture for the terrain blending shader: one texel per cell over the layers' bounds
/// plus a margin of empty cells, one bit per layer (bit i for layer i, bottom first). A narrow map's texel is R16, for
/// up to <see cref="NarrowLayers"/> layers; a wide map's is RGBA32, its four bytes the bits from red up, for up to
/// <see cref="WideLayers"/> (<see cref="FormatOf"/>); a renderer draws at most <see cref="MaxLayers"/>, the shader's cap
/// (TB_MAX_LAYERS). Built from the layers' tilemaps, then refreshed cell by cell after tile changes, growing when a
/// filled cell lands outside the bounds or in the margin. <see cref="Bounds"/> are the cells the texture covers
/// (z from 0 to 1; zero width while no layer has a filled cell). Changed cells are collected and written into the point-filtered texture in one batch by
/// <see cref="Apply"/>, which creates the texture the first time (1 x 1 while the map is empty) and counts its uploads
/// in <see cref="UploadCount"/> for tests. Preview bits, for a brush's hover, are kept apart from the real ones: they
/// show in the texture, never reach the tilemaps, and clear without touching the real bits. A layer may also read
/// <see cref="Source"/>s, extra tilemaps counted only inside their own cells (a chunk's skirt of its neighbours'
/// border cells), so a cell of a layer is filled when its own tilemap or one of its sources holds a tile there. Built
/// over a fixed <see cref="Area"/>, the bounds are that area whenever any cell is filled, and cells outside it are
/// never read. Owns its texture; <see cref="Dispose"/> destroys it.
/// </summary>
public sealed class TerrainControlMap : IDisposable
{
    public const int MaxLayers = 32;
    public const int NarrowLayers = 16;
    public const int WideLayers = 32;
    public const int Margin = 1;
    public const int GrowthSlack = 8;

    /// <summary>An extra tilemap a layer reads, counted only inside <see cref="Cells"/>.</summary>
    public readonly struct Source
    {
        /// <summary>
        /// A source of layer <paramref name="layer"/> reading <paramref name="tilemap"/> in <paramref name="cells"/>.
        /// </summary>
        public Source(Tilemap tilemap, int layer, RectInt cells)
        {
            Tilemap = tilemap;
            Layer = layer;
            Cells = cells;
        }

        public Tilemap Tilemap { get; }
        public int Layer { get; }
        public RectInt Cells { get; }

        /// <summary>
        /// Whether <paramref name="cell"/> lies inside <see cref="Cells"/>, ignoring z.
        /// </summary>
        public bool Contains(Vector3Int cell)
        {
            return cell.x >= Cells.xMin && cell.x < Cells.xMax && cell.y >= Cells.yMin && cell.y < Cells.yMax;
        }
    }

    /// <summary>The layer bits a preview replaces in one cell, and the values it shows instead.</summary>
    private struct PreviewBits
    {
        public uint Mask;
        public uint Value;
    }

    private readonly Tilemap[] layers;
    private readonly bool wide;
    private readonly string textureName;
    private readonly bool hasArea;
    private readonly RectInt area;
    private readonly List<int> pending = new();
    private readonly Dictionary<Vector3Int, PreviewBits> preview = new();
    private Source[] sources = new Source[0];
    private uint[] cells = new uint[0];
    private bool[] pendingFlags = new bool[0];
    private BoundsInt bounds;
    private bool fullUploadPending;
    private Texture2D texture;

    /// <summary>
    /// Builds the map from <paramref name="layers"/> (bottom first, null entries read as empty): narrow, at most
    /// <see cref="NarrowLayers"/>, or <paramref name="wide"/>, at most <see cref="WideLayers"/>. The texture holds
    /// nothing until the first <see cref="Apply"/>.
    /// </summary>
    public TerrainControlMap(IReadOnlyList<Tilemap> layers, bool wide = false, string textureName = "TerrainControlMap")
        : this(layers, wide, textureName, null, null)
    {
    }

    /// <summary>
    /// Builds the map from <paramref name="layers"/> as the other constructor does, each layer also reading its
    /// <paramref name="layerSources"/> (null for none), over the fixed <paramref name="fixedArea"/> when it has one:
    /// then the bounds are that area while any cell in it is filled, and nothing outside it is read.
    /// </summary>
    public TerrainControlMap(IReadOnlyList<Tilemap> layers, bool wide, string textureName, RectInt? fixedArea, IReadOnlyList<Source> layerSources)
    {
        int most = wide ? WideLayers : NarrowLayers;
        if (layers.Count > most)
        {
            throw new ArgumentException($"A {(wide ? "wide" : "narrow")} control map holds at most {most} layers.", nameof(layers));
        }

        this.layers = new Tilemap[layers.Count];
        for (int i = 0; i < layers.Count; i++)
        {
            this.layers[i] = layers[i];
        }

        this.wide = wide;
        this.textureName = textureName;
        hasArea = fixedArea.HasValue;
        area = fixedArea ?? default;
        SetSources(layerSources);
        Rebuild();
    }

    /// <summary>
    /// The format of a narrow map's texture (R16) or a <paramref name="wide"/> one's (RGBA32, red the lowest byte).
    /// </summary>
    public static GraphicsFormat FormatOf(bool wide)
    {
        return wide ? GraphicsFormat.R8G8B8A8_UNorm : GraphicsFormat.R16_UNorm;
    }

    /// <summary>
    /// Whether the graphics device can sample a narrow or <paramref name="wide"/> map's texture.
    /// </summary>
    public static bool IsFormatSupported(bool wide)
    {
        return SystemInfo.IsFormatSupported(FormatOf(wide), GraphicsFormatUsage.Sample);
    }

    public BoundsInt Bounds => bounds;
    public Texture2D Texture => texture;
    public bool IsWide => wide;
    public int LayerCount => layers.Length;
    public bool IsEmpty => bounds.size.x <= 0 || bounds.size.y <= 0;
    public bool HasPendingChanges => fullUploadPending || pending.Count > 0;
    public bool HasPreview => preview.Count > 0;

    /// <summary>
    /// Whether the map is built over a fixed area (<see cref="Area"/>).
    /// </summary>
    public bool HasArea => hasArea;

    /// <summary>
    /// The fixed area the map covers when <see cref="HasArea"/>.
    /// </summary>
    public RectInt Area => area;
    public IReadOnlyList<Source> Sources => sources;
    internal int UploadCount { get; private set; }

    /// <summary>
    /// Replaces the extra tilemaps the layers read (null for none); they are read on the next <see cref="Rebuild"/>.
    /// Throws for a source of a layer the map does not have.
    /// </summary>
    public void SetSources(IReadOnlyList<Source> layerSources)
    {
        int count = layerSources != null ? layerSources.Count : 0;
        Source[] kept = new Source[count];
        for (int i = 0; i < count; i++)
        {
            if (layerSources[i].Layer < 0 || layerSources[i].Layer >= layers.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(layerSources), $"A source of layer {layerSources[i].Layer} on a map of {layers.Length} layers.");
            }

            kept[i] = layerSources[i];
        }

        sources = kept;
    }

    /// <summary>
    /// Whether <paramref name="tilemap"/> is one of the layers' own tilemaps or sources.
    /// </summary>
    public bool Reads(Tilemap tilemap)
    {
        if (tilemap == null)
        {
            return false;
        }

        for (int i = 0; i < layers.Length; i++)
        {
            if (layers[i] == tilemap)
            {
                return true;
            }
        }

        for (int i = 0; i < sources.Length; i++)
        {
            if (sources[i].Tilemap == tilemap)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Reads every layer again: the bounds become the filled cells' bounds (and any filled preview cell's) plus the
    /// margin, or the fixed area when the map has one and any cell in it is filled, and the whole texture is written on
    /// the next <see cref="Apply"/>.
    /// </summary>
    public void Rebuild()
    {
        bool any = false;
        Vector2Int min = Vector2Int.zero;
        Vector2Int max = Vector2Int.zero;
        int blockCount = layers.Length + sources.Length;
        TileBase[][] blocks = new TileBase[blockCount][];
        BoundsInt[] blockBounds = new BoundsInt[blockCount];
        for (int block = 0; block < blockCount; block++)
        {
            Tilemap tilemap = block < layers.Length ? layers[block] : sources[block - layers.Length].Tilemap;
            if (tilemap == null)
            {
                continue;
            }

            BoundsInt cellBounds = tilemap.cellBounds;
            RectInt read = new(cellBounds.xMin, cellBounds.yMin, cellBounds.size.x, cellBounds.size.y);
            if (block >= layers.Length)
            {
                read = Intersect(read, sources[block - layers.Length].Cells);
            }

            if (hasArea)
            {
                read = Intersect(read, area);
            }

            if (read.width <= 0 || read.height <= 0)
            {
                continue;
            }

            BoundsInt readBounds = new(read.xMin, read.yMin, 0, read.width, read.height, 1);
            TileBase[] tiles = tilemap.GetTilesBlock(readBounds);
            blocks[block] = tiles;
            blockBounds[block] = readBounds;
            for (int index = 0; index < tiles.Length; index++)
            {
                if (tiles[index] != null)
                {
                    Include(new Vector2Int(read.xMin + index % read.width, read.yMin + index / read.width), ref any, ref min, ref max);
                }
            }
        }

        foreach (KeyValuePair<Vector3Int, PreviewBits> entry in preview)
        {
            if ((entry.Value.Mask & entry.Value.Value) != 0 && InArea(entry.Key))
            {
                Include(new Vector2Int(entry.Key.x, entry.Key.y), ref any, ref min, ref max);
            }
        }

        bounds = !any
            ? new BoundsInt(0, 0, 0, 0, 0, 1)
            : hasArea
                ? new BoundsInt(area.xMin, area.yMin, 0, area.width, area.height, 1)
                : new BoundsInt(min.x - Margin, min.y - Margin, 0, max.x - min.x + 1 + 2 * Margin, max.y - min.y + 1 + 2 * Margin, 1);
        cells = new uint[bounds.size.x * bounds.size.y];
        pendingFlags = new bool[cells.Length];
        pending.Clear();
        fullUploadPending = true;

        for (int block = 0; block < blockCount; block++)
        {
            TileBase[] tiles = blocks[block];
            if (tiles == null)
            {
                continue;
            }

            uint bit = 1u << (block < layers.Length ? block : sources[block - layers.Length].Layer);
            BoundsInt cellBounds = blockBounds[block];
            for (int index = 0; index < tiles.Length; index++)
            {
                if (tiles[index] != null)
                {
                    cells[IndexOf(cellBounds.xMin + index % cellBounds.size.x, cellBounds.yMin + index / cellBounds.size.x)] |= bit;
                }
            }
        }
    }

    /// <summary>
    /// Reads <paramref name="positions"/> again after <paramref name="tilemap"/>'s tiles changed, for every layer that
    /// has it as its own tilemap or as a source, like <see cref="Refresh(int, NativeArray{Vector3Int})"/>. Returns
    /// whether any layer reads it. Allocates nothing unless the bounds grow.
    /// </summary>
    public bool Refresh(Tilemap tilemap, NativeArray<Vector3Int> positions)
    {
        bool read = false;
        for (int layer = 0; tilemap != null && layer < layers.Length; layer++)
        {
            if (layers[layer] == tilemap)
            {
                Refresh(layer, positions);
                read = true;
            }
        }

        for (int i = 0; tilemap != null && i < sources.Length; i++)
        {
            if (sources[i].Tilemap == tilemap)
            {
                Refresh(sources[i].Layer, positions);
                read = true;
            }
        }

        return read;
    }

    /// <summary>
    /// Reads <paramref name="positions"/> of layer <paramref name="layer"/> again after its tiles changed (on its own
    /// tilemap or one of its sources), growing the bounds first when a filled cell lies outside them or in the margin,
    /// and records the cells whose bits changed for the next <see cref="Apply"/>. Like <see cref="Rebuild"/>, it reads
    /// only z = 0 and ignores other positions, and with a fixed area those outside it. Allocates nothing unless the
    /// bounds grow.
    /// </summary>
    public void Refresh(int layer, NativeArray<Vector3Int> positions)
    {
        bool grow = false;
        Vector2Int min = Vector2Int.zero;
        Vector2Int max = Vector2Int.zero;
        for (int i = 0; i < positions.Length; i++)
        {
            Vector3Int position = positions[i];
            if (position.z == 0 && NeedsGrowth(position) && IsFilled(layer, position))
            {
                Include(new Vector2Int(position.x, position.y), ref grow, ref min, ref max);
            }
        }

        if (grow)
        {
            Grow(min, max);
        }

        uint bit = 1u << layer;
        for (int i = 0; i < positions.Length; i++)
        {
            Vector3Int position = positions[i];
            if (position.z != 0 || !Contains(position))
            {
                continue;
            }

            int index = IndexOf(position.x, position.y);
            uint value = IsFilled(layer, position) ? cells[index] | bit : cells[index] & ~bit;
            if (value != cells[index])
            {
                cells[index] = value;
                MarkPending(index);
            }
        }
    }

    /// <summary>
    /// The real layer bits of <paramref name="cell"/>, without any preview; 0 outside the bounds.
    /// </summary>
    public uint GetCell(Vector3Int cell)
    {
        return Contains(cell) ? cells[IndexOf(cell.x, cell.y)] : 0u;
    }

    /// <summary>
    /// The layer bits <paramref name="cell"/> shows: the real bits with any preview applied; 0 outside the bounds.
    /// </summary>
    public uint GetShownCell(Vector3Int cell)
    {
        return Contains(cell) ? Shown(IndexOf(cell.x, cell.y)) : 0u;
    }

    /// <summary>
    /// Shows layer <paramref name="layer"/> as filled or empty in <paramref name="previewCells"/> without changing the
    /// real bits or the tilemaps, growing the bounds when a filled preview cell lies outside them or in the margin.
    /// Stays until <see cref="ClearPreview"/>.
    /// </summary>
    public void SetPreview(int layer, IReadOnlyList<Vector3Int> previewCells, bool filled)
    {
        uint bit = 1u << layer;
        if (filled)
        {
            bool grow = false;
            Vector2Int min = Vector2Int.zero;
            Vector2Int max = Vector2Int.zero;
            for (int i = 0; i < previewCells.Count; i++)
            {
                if (NeedsGrowth(previewCells[i]))
                {
                    Include(new Vector2Int(previewCells[i].x, previewCells[i].y), ref grow, ref min, ref max);
                }
            }

            if (grow)
            {
                Grow(min, max);
            }
        }

        for (int i = 0; i < previewCells.Count; i++)
        {
            Vector3Int cell = new(previewCells[i].x, previewCells[i].y, 0);
            preview.TryGetValue(cell, out PreviewBits bits);
            bits.Mask |= bit;
            bits.Value = filled ? bits.Value | bit : bits.Value & ~bit;
            preview[cell] = bits;
            if (Contains(cell))
            {
                MarkPending(IndexOf(cell.x, cell.y));
            }
        }
    }

    /// <summary>
    /// Removes every preview, so the cells show their real bits again on the next <see cref="Apply"/>.
    /// </summary>
    public void ClearPreview()
    {
        foreach (KeyValuePair<Vector3Int, PreviewBits> entry in preview)
        {
            if (Contains(entry.Key))
            {
                MarkPending(IndexOf(entry.Key.x, entry.Key.y));
            }
        }

        preview.Clear();
    }

    /// <summary>
    /// Writes the changed cells (every cell after a rebuild or growth) into the texture and uploads it once. Returns
    /// whether anything was uploaded. Allocates nothing unless the texture has to be created.
    /// </summary>
    public bool Apply()
    {
        if (!HasPendingChanges)
        {
            return false;
        }

        int width = Mathf.Max(1, bounds.size.x);
        int height = Mathf.Max(1, bounds.size.y);
        if (texture == null)
        {
            texture = new Texture2D(width, height, wide ? TextureFormat.RGBA32 : TextureFormat.R16, false, true)
            {
                name = textureName,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };
            fullUploadPending = true;
        }
        else if (texture.width != width || texture.height != height)
        {
            texture.Reinitialize(width, height);
            fullUploadPending = true;
        }

        NativeArray<byte> data = texture.GetPixelData<byte>(0);
        if (fullUploadPending)
        {
            if (cells.Length == 0)
            {
                WriteTexel(data, 0, 0u);
            }
            else
            {
                for (int i = 0; i < cells.Length; i++)
                {
                    WriteTexel(data, i, cells[i]);
                }

                foreach (KeyValuePair<Vector3Int, PreviewBits> entry in preview)
                {
                    if (Contains(entry.Key))
                    {
                        int index = IndexOf(entry.Key.x, entry.Key.y);
                        WriteTexel(data, index, Shown(index));
                    }
                }
            }
        }
        else
        {
            for (int i = 0; i < pending.Count; i++)
            {
                WriteTexel(data, pending[i], Shown(pending[i]));
            }
        }

        for (int i = 0; i < pending.Count; i++)
        {
            pendingFlags[pending[i]] = false;
        }

        pending.Clear();
        fullUploadPending = false;
        texture.Apply(false, false);
        UploadCount++;
        return true;
    }

    /// <summary>
    /// Destroys the texture.
    /// </summary>
    public void Dispose()
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

    /// <summary>
    /// Widens the running bounds <paramref name="min"/> to <paramref name="max"/> to hold <paramref name="cell"/>,
    /// starting them at the cell when <paramref name="any"/> is still false.
    /// </summary>
    private static void Include(Vector2Int cell, ref bool any, ref Vector2Int min, ref Vector2Int max)
    {
        min = any ? Vector2Int.Min(min, cell) : cell;
        max = any ? Vector2Int.Max(max, cell) : cell;
        any = true;
    }

    /// <summary>
    /// Grows the bounds to hold the cells from <paramref name="min"/> to <paramref name="max"/> with the margin around
    /// them, plus <see cref="GrowthSlack"/> more cells on each side that grows, so painting outward does not regrow
    /// every cell. Keeps every cell's bits and schedules a full upload.
    /// </summary>
    private void Grow(Vector2Int min, Vector2Int max)
    {
        Vector2Int needMin = min - new Vector2Int(Margin, Margin);
        Vector2Int needMax = max + new Vector2Int(Margin, Margin);
        int xMin;
        int yMin;
        int xMax;
        int yMax;
        if (hasArea)
        {
            xMin = area.xMin;
            yMin = area.yMin;
            xMax = area.xMax - 1;
            yMax = area.yMax - 1;
        }
        else if (IsEmpty)
        {
            xMin = needMin.x;
            yMin = needMin.y;
            xMax = needMax.x;
            yMax = needMax.y;
        }
        else
        {
            xMin = needMin.x < bounds.xMin ? needMin.x - GrowthSlack : bounds.xMin;
            yMin = needMin.y < bounds.yMin ? needMin.y - GrowthSlack : bounds.yMin;
            xMax = needMax.x > bounds.xMax - 1 ? needMax.x + GrowthSlack : bounds.xMax - 1;
            yMax = needMax.y > bounds.yMax - 1 ? needMax.y + GrowthSlack : bounds.yMax - 1;
        }

        BoundsInt grown = new(xMin, yMin, 0, xMax - xMin + 1, yMax - yMin + 1, 1);
        uint[] grownCells = new uint[grown.size.x * grown.size.y];
        for (int y = bounds.yMin; y < bounds.yMax; y++)
        {
            Array.Copy(cells, (y - bounds.yMin) * bounds.size.x, grownCells, (y - grown.yMin) * grown.size.x + bounds.xMin - grown.xMin, bounds.size.x);
        }

        bounds = grown;
        cells = grownCells;
        pendingFlags = new bool[cells.Length];
        pending.Clear();
        fullUploadPending = true;
    }

    /// <summary>
    /// Whether filling <paramref name="cell"/> grows the bounds: with a fixed area, when the map is empty and the cell
    /// lies inside the area; otherwise when it lies outside the bounds or in the margin ring.
    /// </summary>
    private bool NeedsGrowth(Vector3Int cell)
    {
        if (hasArea)
        {
            return IsEmpty && InArea(cell);
        }

        return IsEmpty
            || cell.x < bounds.xMin + Margin || cell.x >= bounds.xMax - Margin
            || cell.y < bounds.yMin + Margin || cell.y >= bounds.yMax - Margin;
    }

    /// <summary>
    /// Whether <paramref name="cell"/> lies inside the fixed area, or anywhere without one, ignoring z.
    /// </summary>
    private bool InArea(Vector3Int cell)
    {
        return !hasArea || (cell.x >= area.xMin && cell.x < area.xMax && cell.y >= area.yMin && cell.y < area.yMax);
    }

    /// <summary>
    /// Whether layer <paramref name="layer"/> is filled at <paramref name="cell"/>: its own tilemap or one of its
    /// sources covering the cell holds a tile there.
    /// </summary>
    private bool IsFilled(int layer, Vector3Int cell)
    {
        Tilemap own = layers[layer];
        if (own != null && own.HasTile(cell))
        {
            return true;
        }

        for (int i = 0; i < sources.Length; i++)
        {
            if (sources[i].Layer == layer && sources[i].Tilemap != null && sources[i].Contains(cell) && sources[i].Tilemap.HasTile(cell))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The cells <paramref name="a"/> and <paramref name="b"/> share; zero size when none.
    /// </summary>
    private static RectInt Intersect(RectInt a, RectInt b)
    {
        int xMin = Mathf.Max(a.xMin, b.xMin);
        int yMin = Mathf.Max(a.yMin, b.yMin);
        int xMax = Mathf.Min(a.xMax, b.xMax);
        int yMax = Mathf.Min(a.yMax, b.yMax);
        return new RectInt(xMin, yMin, Mathf.Max(0, xMax - xMin), Mathf.Max(0, yMax - yMin));
    }

    /// <summary>
    /// Whether <paramref name="cell"/> lies inside the bounds, ignoring z.
    /// </summary>
    private bool Contains(Vector3Int cell)
    {
        return cell.x >= bounds.xMin && cell.x < bounds.xMax && cell.y >= bounds.yMin && cell.y < bounds.yMax;
    }

    /// <summary>
    /// The index in <see cref="cells"/> and in the texture's data of the cell at (<paramref name="x"/>,
    /// <paramref name="y"/>), which must lie inside the bounds.
    /// </summary>
    private int IndexOf(int x, int y)
    {
        return (y - bounds.yMin) * bounds.size.x + x - bounds.xMin;
    }

    /// <summary>
    /// The bits cell <paramref name="index"/> shows: its real bits with any preview applied.
    /// </summary>
    private uint Shown(int index)
    {
        uint value = cells[index];
        if (preview.Count == 0)
        {
            return value;
        }

        Vector3Int cell = new(bounds.xMin + index % bounds.size.x, bounds.yMin + index / bounds.size.x, 0);
        return preview.TryGetValue(cell, out PreviewBits bits) ? (value & ~bits.Mask) | (bits.Value & bits.Mask) : value;
    }

    /// <summary>
    /// Writes <paramref name="bits"/> into texel <paramref name="index"/> of the texture's <paramref name="data"/>, lowest
    /// byte first: the two bytes of a narrow map's R16 texel, or the four of a wide map's RGBA32 texel, red first.
    /// </summary>
    private void WriteTexel(NativeArray<byte> data, int index, uint bits)
    {
        int size = wide ? 4 : 2;
        int start = index * size;
        for (int b = 0; b < size; b++)
        {
            data[start + b] = (byte)(bits >> (8 * b));
        }
    }

    /// <summary>
    /// Records cell <paramref name="index"/> for the next <see cref="Apply"/>, once.
    /// </summary>
    private void MarkPending(int index)
    {
        if (fullUploadPending || pendingFlags[index])
        {
            return;
        }

        pendingFlags[index] = true;
        pending.Add(index);
    }
}
