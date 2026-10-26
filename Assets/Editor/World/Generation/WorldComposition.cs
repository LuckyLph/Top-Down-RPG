using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A world composed in memory by a <see cref="WorldGenerator"/> (Docs/WorldGeneration.md: Terms), in world-local
/// cells: for every cell a bit per ground material of the terrain style's layer order, which region owns it (or the
/// wilds, or the sea) and its raised ground; the regions with their outlines, ports and spawn cells; the roads; the
/// mobs with their derived ids; the entries (the waystones, the first the default, then the gates); the base ground
/// materials; each chunk's material list and content hash; the warnings, and among them those that stop a write; and
/// how long each step took. Plain data, filled by <see cref="WorldComposer"/>.
/// </summary>
public sealed class WorldComposition
{
    public const int Wilds = -1;
    public const int Sea = -2;

    /// <summary>
    /// A region of the world: its placement's key, generator and seed, the cell its layout's origin went to, the
    /// bounds of its cells, its spawn cell, its outline (its cells with a cell outside it among their 8 neighbours, in
    /// row order), its ports, and whether it lays the ocean (a sea region, whose sea side meets the world's sea).
    /// </summary>
    public sealed class Region
    {
        /// <summary>
        /// The region at <paramref name="index"/> of the composition, laid for <paramref name="placement"/> from <paramref name="seed"/>.
        /// </summary>
        internal Region(int index, RegionPlacement placement, int seed)
        {
            Index = index;
            Key = placement.Key;
            Generator = placement.Generator;
            Seed = seed;
            Cell = placement.Cell;
        }

        public int Index { get; }
        public string Key { get; }
        public AreaGenerator Generator { get; }
        public int Seed { get; }
        public Vector2Int Cell { get; }
        public RectInt Bounds { get; internal set; }
        public Vector2Int SpawnCell { get; internal set; }
        public bool IsSea { get; internal set; }
        public List<Vector2Int> Outline { get; } = new();
        public List<Port> Ports { get; } = new();
    }

    /// <summary>
    /// A run of a region's path cells on its outline where one of its roads runs out, with the side it faces, the
    /// middle of the rows (or columns) it holds across that side, the line of its outer edge, and the gate named
    /// after it once a link uses it.
    /// </summary>
    public sealed class Port
    {
        /// <summary>
        /// A port of <paramref name="region"/> facing <paramref name="side"/>, made of <paramref name="cells"/>.
        /// </summary>
        internal Port(Region region, PortSide side, List<Vector2Int> cells, float middle, int outer, int inner)
        {
            Region = region;
            Side = side;
            Cells = cells;
            Middle = middle;
            Outer = outer;
            Inner = inner;
        }

        public Region Region { get; }
        public PortSide Side { get; }
        public IReadOnlyList<Vector2Int> Cells { get; }

        /// <summary>
        /// The middle, across the side it faces, of the rows (an east or west port) or columns (a north or south port)
        /// it holds, in cells from the world's origin.
        /// </summary>
        public float Middle { get; }

        /// <summary>
        /// Its outermost column (an east or west port) or row (north or south), the outline's there.
        /// </summary>
        public int Outer { get; }

        /// <summary>
        /// Its innermost column or row.
        /// </summary>
        public int Inner { get; }

        /// <summary>
        /// The name of its gate (its region's key and its side), or null while no link uses it.
        /// </summary>
        public string Gate { get; internal set; }

        public bool Linked => Gate != null;
    }

    /// <summary>
    /// A world road: the link it was laid for, its material and width, the course its spline runs through and the
    /// cells it paved, in the order it paved them.
    /// </summary>
    public sealed class Road
    {
        /// <summary>
        /// The road of link <paramref name="link"/>, of <paramref name="width"/> cells of <paramref name="material"/>.
        /// </summary>
        internal Road(int link, TerrainMaterial material, int width)
        {
            Link = link;
            Material = material;
            Width = width;
        }

        public int Link { get; }
        public TerrainMaterial Material { get; }
        public int Width { get; }
        public List<Vector2> Course { get; } = new();
        public List<Vector2Int> Cells { get; } = new();
    }

    /// <summary>
    /// A mob spawn point: its prefab, its cell, its derived persistent id, and the scope and ordinal the id is derived
    /// from (<see cref="GeneratedIds.For"/>): a region's key, or the wilds of a chunk.
    /// </summary>
    public readonly struct Mob
    {
        /// <summary>
        /// A spawn point of <paramref name="prefab"/> at <paramref name="cell"/> with the id derived from <paramref name="scope"/> and <paramref name="ordinal"/>.
        /// </summary>
        internal Mob(MobController prefab, Vector2Int cell, string id, string scope, int ordinal)
        {
            Prefab = prefab;
            Cell = cell;
            Id = id;
            Scope = scope;
            Ordinal = ordinal;
        }

        public MobController Prefab { get; }
        public Vector2Int Cell { get; }
        public string Id { get; }
        public string Scope { get; }
        public int Ordinal { get; }
    }

    /// <summary>
    /// A prop: its prefab, its cell (the bottom-left cell of a blocking prop's footprint, or the cell a walk-through prop
    /// stands in), where in that cell a walk-through prop stands (a fraction of a cell), how many cells a blocking prop's
    /// footprint covers (none for a walk-through prop), and the scope that placed it: a region's key, or the wilds of a
    /// chunk. Every cell it covers lies in one chunk, <see cref="Chunk"/>.
    /// </summary>
    public readonly struct Prop
    {
        /// <summary>
        /// A placement of <paramref name="prefab"/> at <paramref name="cell"/>, offset by <paramref name="offset"/> when it
        /// covers no cells, covering <paramref name="covered"/> cells when it blocks, placed by <paramref name="scope"/>.
        /// </summary>
        internal Prop(GameObject prefab, Vector2Int cell, Vector2 offset, Vector2Int covered, string scope)
        {
            Prefab = prefab;
            Cell = cell;
            Offset = offset;
            Covered = covered;
            Scope = scope;
        }

        public GameObject Prefab { get; }
        public Vector2Int Cell { get; }
        public Vector2 Offset { get; }
        public Vector2Int Covered { get; }
        public string Scope { get; }
        public bool Blocks => Covered != Vector2Int.zero;
        public Vector2Int Chunk => ChunkCoords.ChunkOf(Cell);
    }

    /// <summary>
    /// A chunk of the world: its coordinates, whether the world has its scene, the materials it lists (its own cells'
    /// and its neighbours' border strips', <see cref="WorldChunk.SkirtCells"/> wide, in the style's order), the
    /// materials of its own cells, whether it may be wide (its record), and a hash of its content.
    /// </summary>
    public sealed class Chunk
    {
        /// <summary>
        /// The chunk at <paramref name="coords"/>, whose scene the world has when <paramref name="exists"/>.
        /// </summary>
        internal Chunk(Vector2Int coords, bool exists, bool allowWide)
        {
            Coords = coords;
            Exists = exists;
            AllowWide = allowWide;
        }

        public Vector2Int Coords { get; }
        public bool Exists { get; }
        public bool AllowWide { get; }
        public List<TerrainMaterial> Materials { get; } = new();
        public uint ListMask { get; internal set; }
        public uint OwnMask { get; internal set; }

        /// <summary>
        /// A hash of what the chunk holds: each material's cells, its raised ground and its mobs
        /// (<see cref="ChunkContent"/>), which a chunk scene the writer wrote there hashes to as well.
        /// </summary>
        public string ContentHash { get; internal set; }
    }

    /// <summary>
    /// How long one step of the composition took.
    /// </summary>
    public readonly struct StepTime
    {
        /// <summary>
        /// That <paramref name="step"/> took <paramref name="milliseconds"/>.
        /// </summary>
        internal StepTime(string step, double milliseconds)
        {
            Step = step;
            Milliseconds = milliseconds;
        }

        public string Step { get; }
        public double Milliseconds { get; }
    }

    internal readonly uint[] materialBits;
    internal readonly short[] owners;
    internal readonly byte[] raised;

    /// <summary>
    /// An empty composition of a world of <paramref name="sizeInChunks"/> chunks drawn with <paramref name="style"/>
    /// (none for a composition that failed), every cell the sea's.
    /// </summary>
    internal WorldComposition(Vector2Int sizeInChunks, TerrainStyle style)
    {
        SizeInChunks = sizeInChunks;
        Size = sizeInChunks * ChunkCoords.Size;
        Style = style;
        LayerOrder = style != null ? style.LayerOrder : System.Array.Empty<TerrainMaterial>();
        int count = Size.x * Size.y;
        materialBits = new uint[count];
        owners = new short[count];
        raised = new byte[count];
    }

    public Vector2Int SizeInChunks { get; }
    public Vector2Int Size { get; }
    public RectInt Bounds => new(0, 0, Size.x, Size.y);

    /// <summary>
    /// The terrain style the world's chunks draw with.
    /// </summary>
    public TerrainStyle Style { get; }

    /// <summary>
    /// The materials of the terrain style's layer order, bottom first: bit i of a cell's mask is material i.
    /// </summary>
    public IReadOnlyList<TerrainMaterial> LayerOrder { get; }

    public List<Region> Regions { get; } = new();
    public List<Road> Roads { get; } = new();
    public List<Mob> Mobs { get; } = new();

    /// <summary>
    /// The props, the regions' first, in region order and each region's own order, then the wilds', chunk by chunk in
    /// row order.
    /// </summary>
    public List<Prop> Props { get; } = new();

    /// <summary>
    /// The entries the composition makes, in order: a waystone per region, the first region's first, so it is the
    /// world's default entry, then a gate per linked port.
    /// </summary>
    public List<WorldDefinition.Entry> Entries { get; } = new();

    /// <summary>
    /// The base ground materials, in the style's order: the wilds' grass and each region's ground (its generator's
    /// first layer), which the writer lists in a chunk's <see cref="AreaTerrain"/> where it lays them.
    /// </summary>
    public List<TerrainMaterial> Grounds { get; } = new();

    public List<Chunk> Chunks { get; } = new();
    public List<string> Warnings { get; } = new();

    /// <summary>
    /// The warnings, also in <see cref="Warnings"/>, that stop the writer unless it is asked to write anyway: a chunk
    /// past 16 materials not allowed wide, and an entry or mob the default entry cannot walk to.
    /// </summary>
    public List<string> BlockingWarnings { get; } = new();
    public List<StepTime> StepTimes { get; } = new();

    public WorldDefinition.Entry DefaultEntry => Entries.Count > 0 ? Entries[0] : null;

    /// <summary>
    /// Whether <paramref name="cell"/> lies inside the world.
    /// </summary>
    public bool Contains(Vector2Int cell)
    {
        return cell.x >= 0 && cell.y >= 0 && cell.x < Size.x && cell.y < Size.y;
    }

    /// <summary>
    /// The materials of <paramref name="cell"/> as a mask over <see cref="LayerOrder"/>; 0 outside the world.
    /// </summary>
    public uint MaskAt(Vector2Int cell)
    {
        return Contains(cell) ? materialBits[cell.y * Size.x + cell.x] : 0u;
    }

    /// <summary>
    /// Whether <paramref name="cell"/> holds <paramref name="material"/>.
    /// </summary>
    public bool Has(Vector2Int cell, TerrainMaterial material)
    {
        int index = IndexOf(material);
        return index >= 0 && (MaskAt(cell) & (1u << index)) != 0;
    }

    /// <summary>
    /// The material drawn on top of <paramref name="cell"/>, the last of its materials in the layer order, which
    /// decides it; null for a cell with none.
    /// </summary>
    public TerrainMaterial TopAt(Vector2Int cell)
    {
        int top = TopIndex(MaskAt(cell));
        return top >= 0 ? LayerOrder[top] : null;
    }

    /// <summary>
    /// The index in <see cref="Regions"/> of the region owning <paramref name="cell"/>, or <see cref="Wilds"/> for a
    /// land cell outside every region, or <see cref="Sea"/> for the sea and outside the world.
    /// </summary>
    public int RegionAt(Vector2Int cell)
    {
        return Contains(cell) ? owners[cell.y * Size.x + cell.x] : Sea;
    }

    /// <summary>
    /// What <paramref name="cell"/>'s raised ground is: <see cref="RaisedGround.Level"/>,
    /// <see cref="RaisedGround.Raised"/> or <see cref="RaisedGround.Ramp"/>.
    /// </summary>
    public byte RaisedAt(Vector2Int cell)
    {
        return Contains(cell) ? raised[cell.y * Size.x + cell.x] : RaisedGround.Level;
    }

    /// <summary>
    /// The place of <paramref name="material"/> in <see cref="LayerOrder"/>, or -1.
    /// </summary>
    public int IndexOf(TerrainMaterial material)
    {
        for (int i = 0; i < LayerOrder.Count; i++)
        {
            if (material != null && LayerOrder[i] == material)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// The cells of <paramref name="area"/> (clipped to the world) holding <paramref name="material"/>, in row order.
    /// </summary>
    public List<Vector2Int> CellsOf(TerrainMaterial material, RectInt area)
    {
        List<Vector2Int> found = new();
        int index = IndexOf(material);
        if (index < 0)
        {
            return found;
        }

        uint bit = 1u << index;
        int xMin = Mathf.Max(0, area.xMin);
        int yMin = Mathf.Max(0, area.yMin);
        int xMax = Mathf.Min(Size.x, area.xMax);
        int yMax = Mathf.Min(Size.y, area.yMax);
        for (int y = yMin; y < yMax; y++)
        {
            for (int x = xMin; x < xMax; x++)
            {
                if ((materialBits[y * Size.x + x] & bit) != 0)
                {
                    found.Add(new Vector2Int(x, y));
                }
            }
        }

        return found;
    }

    /// <summary>
    /// The chunk at <paramref name="coords"/>, or null outside the world.
    /// </summary>
    public Chunk ChunkAt(Vector2Int coords)
    {
        if (coords.x < 0 || coords.y < 0 || coords.x >= SizeInChunks.x || coords.y >= SizeInChunks.y)
        {
            return null;
        }

        return Chunks[coords.y * SizeInChunks.x + coords.x];
    }

    /// <summary>
    /// The region named <paramref name="key"/>, or null.
    /// </summary>
    public Region FindRegion(string key)
    {
        foreach (Region region in Regions)
        {
            if (region.Key == key)
            {
                return region;
            }
        }

        return null;
    }

    /// <summary>
    /// The index of the highest bit of <paramref name="mask"/>, the top material of a cell, or -1 for none.
    /// </summary>
    public static int TopIndex(uint mask)
    {
        int top = -1;
        while (mask != 0)
        {
            mask >>= 1;
            top++;
        }

        return top;
    }

    /// <summary>
    /// How many bits of <paramref name="mask"/> are set: how many materials it holds.
    /// </summary>
    public static int CountOf(uint mask)
    {
        int count = 0;
        while (mask != 0)
        {
            mask &= mask - 1;
            count++;
        }

        return count;
    }
}
