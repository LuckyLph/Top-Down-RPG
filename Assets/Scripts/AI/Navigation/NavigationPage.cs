using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// One rectangle of navigation cells read from a <see cref="NavigationGrid2D"/>'s tilemaps: an area's walkable bounds,
/// or one chunk of a world. Per cell it holds, in flat arrays allocated once, the terrain layers on top (a bit per layer
/// of the highest priority there; 0 when nothing walkable is there or something blocks the cell) and the kind of raised
/// ground. Its cliff sides and per-profile costs live in its slot of a <see cref="NavigationPagePool"/>, at
/// <see cref="Base"/> plus its local index; its <see cref="NavigationMap2D"/> links it with its neighbouring pages.
/// </summary>
internal sealed class NavigationPage
{
    public const int MaxLayers = 64;

    private readonly List<Tilemap> blocking;
    private readonly HashSet<Vector3Int> obstacleCells;

    /// <summary>
    /// A page over <paramref name="rect"/> at <paramref name="z"/>, read from the terrain <paramref name="layers"/> (at
    /// most <see cref="MaxLayers"/>), with the cells of <paramref name="blockingTilemaps"/> and
    /// <paramref name="obstacles"/> blocked, raised ground from <paramref name="raised"/> (null for none) whose cliff
    /// face rises <paramref name="faceHeight"/> cells, and <paramref name="reference"/> for world/cell conversions. Reads
    /// the kinds of its raised ground now; its terrain is read once its map has placed it.
    /// </summary>
    public NavigationPage(
        NavigationGrid2D grid,
        RectInt rect,
        int z,
        List<NavigationTerrainSource2D> layers,
        List<Tilemap> blockingTilemaps,
        Tilemap raised,
        HashSet<Vector3Int> obstacles,
        float faceHeight,
        Tilemap reference)
    {
        Grid = grid;
        Rect = rect;
        Z = z;
        Width = Mathf.Max(0, rect.width);
        Height = Mathf.Max(0, rect.height);
        CellCount = Width * Height;
        Layers = layers.ToArray();
        LayerTypes = new TerrainType2D[Layers.Length];
        for (int i = 0; i < Layers.Length; i++)
        {
            LayerTypes[i] = Layers[i].TerrainType;
        }

        blocking = new List<Tilemap>(blockingTilemaps);
        Raised = raised;
        obstacleCells = new HashSet<Vector3Int>(obstacles);
        FaceHeight = raised != null ? faceHeight : 0f;
        FaceRows = RaisedGround.RowsCovered(FaceHeight);
        Reference = reference;
        Terrain = new ulong[CellCount];
        Around[4] = this;
        if (raised != null)
        {
            Kinds = new byte[CellCount];
            ReadKinds();
        }
    }

    public NavigationGrid2D Grid { get; }
    public RectInt Rect { get; }
    public int Z { get; }
    public int Width { get; }
    public int Height { get; }
    public int CellCount { get; }
    public NavigationTerrainSource2D[] Layers { get; }
    public TerrainType2D[] LayerTypes { get; }
    public Tilemap Raised { get; }
    public float FaceHeight { get; }
    public int FaceRows { get; }
    public Tilemap Reference { get; }

    /// <summary>
    /// The terrain layer bits of each cell, local index <c>y * Width + x</c>; 0 = blocked or no terrain.
    /// </summary>
    public ulong[] Terrain { get; }

    /// <summary>
    /// The kind of raised ground of each cell (<see cref="RaisedGround.Level"/>, <see cref="RaisedGround.Raised"/>,
    /// <see cref="RaisedGround.Ramp"/>); null without a raised tilemap.
    /// </summary>
    public byte[] Kinds { get; }

    /// <summary>
    /// The pages around this one in its map, indexed <c>(dy + 1) * 3 + dx + 1</c>; the middle one is this page, the
    /// others null where no page is loaded.
    /// </summary>
    public NavigationPage[] Around { get; } = new NavigationPage[9];

    public NavigationMap2D Map { get; set; }
    public int Slot { get; set; } = -1;
    public int Base { get; set; }

    /// <summary>
    /// A stamp unique within its pool, renewed whenever its cells or its neighbours change, which tells the cost fields
    /// to bake it again.
    /// </summary>
    public int Stamp { get; set; }

    /// <summary>
    /// How many times it changed since it was built.
    /// </summary>
    public int Changes { get; set; }

    /// <summary>
    /// Whether <paramref name="x"/>, <paramref name="y"/> is one of its cells.
    /// </summary>
    public bool Contains(int x, int y)
    {
        return x >= Rect.xMin && y >= Rect.yMin && x < Rect.xMin + Width && y < Rect.yMin + Height;
    }

    /// <summary>
    /// The cell at local index <paramref name="local"/>.
    /// </summary>
    public Vector3Int CellAt(int local)
    {
        return new Vector3Int(Rect.xMin + (local % Width), Rect.yMin + (local / Width), Z);
    }

    /// <summary>
    /// The pool index of the cell at local <paramref name="x"/>, <paramref name="y"/> (which may lie a cell beyond its
    /// edges, in a neighbouring page), or -1 when no page holds it.
    /// </summary>
    public int IndexAt(int x, int y)
    {
        if ((uint)x < (uint)Width && (uint)y < (uint)Height)
        {
            return Base + (y * Width) + x;
        }

        int px = x < 0 ? -1 : x >= Width ? 1 : 0;
        int py = y < 0 ? -1 : y >= Height ? 1 : 0;
        NavigationPage neighbour = Around[((py + 1) * 3) + px + 1];
        if (neighbour == null)
        {
            return -1;
        }

        int nx = px > 0 ? x - Width : px < 0 ? x + neighbour.Width : x;
        int ny = py > 0 ? y - Height : py < 0 ? y + neighbour.Height : y;
        if ((uint)nx >= (uint)neighbour.Width || (uint)ny >= (uint)neighbour.Height)
        {
            return -1;
        }

        return neighbour.Base + (ny * neighbour.Width) + nx;
    }

    /// <summary>
    /// The page holding the cell at local <paramref name="x"/>, <paramref name="y"/>, this one or a neighbour, with its
    /// local index there; false when no page holds it.
    /// </summary>
    public bool TryLocateAround(int x, int y, out NavigationPage page, out int local)
    {
        int index = IndexAt(x, y);
        if (index < 0)
        {
            page = null;
            local = -1;
            return false;
        }

        int px = x < 0 ? -1 : x >= Width ? 1 : 0;
        int py = y < 0 ? -1 : y >= Height ? 1 : 0;
        page = Around[((py + 1) * 3) + px + 1];
        local = index - page.Base;
        return true;
    }

    /// <summary>
    /// The terrain layer bits of <paramref name="cell"/> read from the layers' tilemaps, ignoring what blocks it: a
    /// higher priority replaces what is there, an equal one joins it.
    /// </summary>
    public ulong ReadTerrain(Vector3Int cell)
    {
        ulong bits = 0;
        int priority = int.MinValue;
        for (int i = 0; i < Layers.Length; i++)
        {
            NavigationTerrainSource2D source = Layers[i];
            if (source == null || !source.DataTilemap.HasTile(cell))
            {
                continue;
            }

            if (source.Priority > priority)
            {
                bits = 0;
                priority = source.Priority;
            }

            if (source.Priority == priority)
            {
                bits |= 1UL << i;
            }
        }

        return bits;
    }

    /// <summary>
    /// Reads the terrain of every cell from the layers' tilemaps in one block each, ignoring what blocks it, then clears
    /// the cells with a tile on a blocking tilemap or under an obstacle collider.
    /// </summary>
    public void ReadTerrainBlock()
    {
        System.Array.Clear(Terrain, 0, Terrain.Length);
        if (CellCount == 0)
        {
            return;
        }

        BoundsInt block = new(Rect.xMin, Rect.yMin, Z, Width, Height, 1);
        TileBase[] tiles = new TileBase[CellCount];
        int[] priorities = new int[CellCount];
        System.Array.Fill(priorities, int.MinValue);
        for (int i = 0; i < Layers.Length; i++)
        {
            NavigationTerrainSource2D source = Layers[i];
            if (source == null)
            {
                continue;
            }

            source.DataTilemap.GetTilesBlockNonAlloc(block, tiles);
            ulong bit = 1UL << i;
            int priority = source.Priority;
            for (int local = 0; local < CellCount; local++)
            {
                if (tiles[local] == null)
                {
                    continue;
                }

                if (priority > priorities[local])
                {
                    Terrain[local] = 0;
                    priorities[local] = priority;
                }

                if (priority == priorities[local])
                {
                    Terrain[local] |= bit;
                }
            }
        }

        for (int i = 0; i < blocking.Count; i++)
        {
            blocking[i].GetTilesBlockNonAlloc(block, tiles);
            for (int local = 0; local < CellCount; local++)
            {
                if (tiles[local] != null)
                {
                    Terrain[local] = 0;
                }
            }
        }

        foreach (Vector3Int cell in obstacleCells)
        {
            if (cell.z == Z && Contains(cell.x, cell.y))
            {
                Terrain[((cell.y - Rect.yMin) * Width) + cell.x - Rect.xMin] = 0;
            }
        }
    }

    /// <summary>
    /// What blocks <paramref name="cell"/> for every terrain layer in this page's tilemaps: an obstacle collider, then a
    /// tile on a blocking tilemap; <see cref="NavigationGrid2D.Blocker.None"/> otherwise. The cliff face is the map's to
    /// tell, since it reads raised ground across page borders.
    /// </summary>
    public NavigationGrid2D.Blocker TileBlockerOf(Vector3Int cell)
    {
        if (obstacleCells.Contains(cell))
        {
            return NavigationGrid2D.Blocker.Obstacle;
        }

        for (int i = 0; i < blocking.Count; i++)
        {
            if (blocking[i] != null && blocking[i].HasTile(cell))
            {
                return NavigationGrid2D.Blocker.Wall;
            }
        }

        return NavigationGrid2D.Blocker.None;
    }

    /// <summary>
    /// Re-reads the kind of raised ground of <paramref name="cell"/>, one of its cells, from the raised tilemap.
    /// </summary>
    public void RefreshKind(Vector3Int cell)
    {
        if (Kinds != null && Contains(cell.x, cell.y))
        {
            Kinds[((cell.y - Rect.yMin) * Width) + cell.x - Rect.xMin] = RaisedGround.KindAt(Raised, cell);
        }
    }

    /// <summary>
    /// Reads the kind of raised ground of every cell in one block.
    /// </summary>
    private void ReadKinds()
    {
        if (CellCount == 0)
        {
            return;
        }

        TileBase[] tiles = new TileBase[CellCount];
        Raised.GetTilesBlockNonAlloc(new BoundsInt(Rect.xMin, Rect.yMin, 0, Width, Height, 1), tiles);
        for (int local = 0; local < CellCount; local++)
        {
            Kinds[local] = RaisedGround.KindOf(tiles[local]);
        }
    }
}
