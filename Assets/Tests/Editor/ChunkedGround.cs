using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// The same ground drawn two ways, for tests of terrain across chunk borders: one renderer over the whole 2 x 2 chunk
/// world, and a renderer per chunk with its <see cref="WorldChunk"/>, each over only its own cells, linked to the
/// others and with a coast map baked from the whole world (<see cref="WorldCoastBake"/>). The ground uses the project's
/// style and materials, which are only read: grass everywhere but the sea, an ocean coming in from the south-east
/// across a border behind a sandy beach, a stone road and a gravel trail across borders, a pond with a plank bridge
/// over the corner where the four chunks meet, tall grass across a border, and a plateau of raised ground with a ramp
/// across a border. Every grid lists every material of the world in the style's order, as the whole renderer does:
/// a bridge's end draws differently when a listed layer lies between it and the layer under it (Architecture.md's
/// Known gaps), so equal lists keep the comparison to the skirt and the coast map.
/// </summary>
public sealed class ChunkedGround
{
    public const string StylePath = "Assets/Settings/Terrain/TerrainStyle.asset";
    public static readonly Vector2Int Origin = new(2048, 0);
    public static readonly Vector2Int[] ChunkCoordinates = { new(0, 0), new(1, 0), new(0, 1), new(1, 1) };

    private readonly List<Object> created = new();
    private readonly Dictionary<TerrainMaterial, HashSet<Vector2Int>> ground = new();
    private readonly HashSet<Vector2Int> raised = new();
    private readonly HashSet<Vector2Int> ramps = new();
    private readonly Dictionary<Vector2Int, WorldChunk> chunks = new();
    private readonly List<TerrainBlendRenderer> renderers = new();

    /// <summary>
    /// Paints the layout and builds both ways of drawing it; with <paramref name="bake"/> false the chunks build their
    /// coast maps themselves instead. Records how long the coast bake took per chunk.
    /// </summary>
    public ChunkedGround(bool bake = true)
    {
        Style = AssetDatabase.LoadAssetAtPath<TerrainStyle>(StylePath);
        Assert.That(Style, Is.Not.Null);
        Tile = Track(ScriptableObject.CreateInstance<Tile>());
        RaisedTile = Track(RaisedGroundTile.Create(false));
        RampTile = Track(RaisedGroundTile.Create(true));
        World = Track(WorldDefinition.Create("ChunkedGround", Origin, new Vector2Int(2, 2), ChunkCoordinates));
        Lay();
        Whole = BuildWhole();
        Cells = new WorldCoastBake.WorldCells(World.LocalBounds);
        foreach (TerrainBlendRenderer.Layer layer in Whole.Layers)
        {
            Cells.Add(layer.Tilemap, layer.Material, World.LocalBounds);
        }

        foreach (Vector2Int coords in ChunkCoordinates)
        {
            Texture2D coast = null;
            if (bake)
            {
                System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
                ushort[] encoded = WorldCoastBake.BakeChunk(Cells, Style, MaterialsIn(World.LocalBounds), ChunkCoords.Bounds(coords), out int width, out int height);
                BakeMilliseconds[coords] = watch.Elapsed.TotalMilliseconds;
                coast = encoded != null ? Track(WorldCoastBake.ToTexture(encoded, width, height, $"Coast {coords}")) : null;
            }

            chunks[coords] = BuildChunk(coords, coast);
        }

        foreach (WorldChunk chunk in chunks.Values)
        {
            foreach (WorldChunk other in chunks.Values)
            {
                chunk.Link(other);
            }
        }

        renderers.Add(Whole);
        foreach (WorldChunk chunk in chunks.Values)
        {
            renderers.Add(chunk.Terrain);
        }
    }

    public TerrainStyle Style { get; }
    public Tile Tile { get; }
    public RaisedGroundTile RaisedTile { get; }
    public RaisedGroundTile RampTile { get; }
    public WorldDefinition World { get; }
    public WorldCoastBake.WorldCells Cells { get; }
    public TerrainBlendRenderer Whole { get; }
    public IReadOnlyDictionary<Vector2Int, WorldChunk> Chunks => chunks;
    public IReadOnlyList<TerrainBlendRenderer> Renderers => renderers;
    public Dictionary<Vector2Int, double> BakeMilliseconds { get; } = new();

    /// <summary>
    /// The project's material named <paramref name="materialName"/>.
    /// </summary>
    public TerrainMaterial Material(string materialName)
    {
        TerrainMaterial material = AssetDatabase.LoadAssetAtPath<TerrainMaterial>($"Assets/Settings/Terrain/Materials/{materialName}.asset");
        Assert.That(material, Is.Not.Null, materialName);
        return material;
    }

    /// <summary>
    /// The world rectangle, in units, of the chunk at <paramref name="coords"/>.
    /// </summary>
    public Rect WorldRectOf(Vector2Int coords)
    {
        RectInt cells = ChunkCoords.Bounds(coords);
        return new Rect(Origin.x + cells.xMin, Origin.y + cells.yMin, cells.width, cells.height);
    }

    /// <summary>
    /// Destroys every grid, texture and object it made.
    /// </summary>
    public void Destroy()
    {
        for (int i = created.Count - 1; i >= 0; i--)
        {
            if (created[i] != null)
            {
                Object.DestroyImmediate(created[i]);
            }
        }

        created.Clear();
    }

    /// <summary>
    /// The cells of each material and of the raised ground.
    /// </summary>
    private void Lay()
    {
        TerrainMaterial grass = Material("Grass");
        TerrainMaterial tallGrass = Material("TallGrass");
        TerrainMaterial sand = Material("Sand");
        TerrainMaterial gravel = Material("Gravel");
        TerrainMaterial stone = Material("Stone");
        TerrainMaterial water = Material("Water");
        TerrainMaterial ocean = Material("Ocean");
        TerrainMaterial planks = Material("Planks");
        foreach (Vector2Int cell in World.LocalBounds.allPositionsWithin)
        {
            int x = cell.x;
            int y = cell.y;
            bool sea = x >= 40 && y < 12 + x / 8;
            Add(sea ? ocean : grass, cell);
            if (!sea && x >= 38 && y < 15 + x / 8)
            {
                Add(sand, cell);
            }

            if (y >= 40 && y <= 43)
            {
                Add(stone, cell);
            }

            if (x >= 90 && x <= 91 && y >= 44)
            {
                Add(gravel, cell);
            }

            if ((x - 64) * (x - 64) + (y - 64) * (y - 64) <= 36)
            {
                Add(water, cell);
            }

            if (x >= 56 && x <= 72 && y >= 63 && y <= 64)
            {
                Add(planks, cell);
            }

            if ((x - 64) * (x - 64) / 196f + (y - 100) * (y - 100) / 81f <= 1f)
            {
                Add(tallGrass, cell);
            }

            if (x >= 10 && x <= 30 && y >= 56 && y <= 72)
            {
                if (y == 56 && x >= 18 && x <= 21)
                {
                    ramps.Add(cell);
                }
                else
                {
                    raised.Add(cell);
                }
            }
        }
    }

    /// <summary>
    /// Adds <paramref name="cell"/> to <paramref name="material"/>'s cells.
    /// </summary>
    private void Add(TerrainMaterial material, Vector2Int cell)
    {
        if (!ground.TryGetValue(material, out HashSet<Vector2Int> cells))
        {
            ground[material] = cells = new HashSet<Vector2Int>();
        }

        cells.Add(cell);
    }

    /// <summary>
    /// The materials with cells inside <paramref name="cells"/>, in the style's order.
    /// </summary>
    private List<TerrainMaterial> MaterialsIn(RectInt cells)
    {
        List<TerrainMaterial> found = new();
        foreach (TerrainMaterial material in Style.LayerOrder)
        {
            if (material != null && ground.TryGetValue(material, out HashSet<Vector2Int> painted) && AnyIn(painted, cells))
            {
                found.Add(material);
            }
        }

        return found;
    }

    /// <summary>
    /// One renderer over every cell of the world.
    /// </summary>
    private TerrainBlendRenderer BuildWhole()
    {
        return BuildGrid("Whole", World.LocalBounds, null);
    }

    /// <summary>
    /// The chunk at <paramref name="coords"/>, drawing only its own cells, with <paramref name="coast"/> as its baked
    /// coast map.
    /// </summary>
    private WorldChunk BuildChunk(Vector2Int coords, Texture2D coast)
    {
        WorldChunk chunk = null;
        BuildGrid($"Chunk {coords.x},{coords.y}", ChunkCoords.Bounds(coords), gridObject =>
        {
            chunk = gridObject.AddComponent<WorldChunk>();
            chunk.Configure(World, coords, coast);
        });
        return chunk;
    }

    /// <summary>
    /// An enabled grid at the world's origin with a layer per material of the world, each holding only its cells in
    /// <paramref name="cells"/>, a raised tilemap of the raised cells in them when there are any, and a terrain renderer
    /// over them in the style's order; <paramref name="beforeRenderer"/> runs on the inactive grid first.
    /// </summary>
    private TerrainBlendRenderer BuildGrid(string gridName, RectInt cells, System.Action<GameObject> beforeRenderer)
    {
        GameObject gridObject = Track(new GameObject(gridName, typeof(Grid)));
        gridObject.transform.position = new Vector3(Origin.x, Origin.y, 0f);
        gridObject.SetActive(false);
        List<TerrainBlendRenderer.Layer> layers = new();
        foreach (TerrainMaterial material in MaterialsIn(World.LocalBounds))
        {
            Tilemap tilemap = new GameObject(material.name, typeof(Tilemap)).GetComponent<Tilemap>();
            tilemap.transform.SetParent(gridObject.transform, false);
            Write(tilemap, ground[material], cells, Tile);
            layers.Add(new TerrainBlendRenderer.Layer(tilemap, material));
        }

        Tilemap raisedMap = null;
        if (AnyIn(raised, cells) || AnyIn(ramps, cells))
        {
            raisedMap = new GameObject("Raised", typeof(Tilemap)).GetComponent<Tilemap>();
            raisedMap.transform.SetParent(gridObject.transform, false);
            Write(raisedMap, raised, cells, RaisedTile);
            Write(raisedMap, ramps, cells, RampTile);
        }

        beforeRenderer?.Invoke(gridObject);
        TerrainBlendRenderer renderer = gridObject.AddComponent<TerrainBlendRenderer>();
        renderer.Configure(Style, raisedMap, layers.ToArray());
        gridObject.SetActive(true);
        return renderer;
    }

    /// <summary>
    /// Writes <paramref name="tile"/> into <paramref name="tilemap"/> at those of <paramref name="painted"/> inside
    /// <paramref name="cells"/>.
    /// </summary>
    private static void Write(Tilemap tilemap, HashSet<Vector2Int> painted, RectInt cells, TileBase tile)
    {
        List<Vector3Int> positions = new();
        foreach (Vector2Int cell in painted)
        {
            if (cells.Contains(cell))
            {
                positions.Add(new Vector3Int(cell.x, cell.y, 0));
            }
        }

        TileBase[] tiles = new TileBase[positions.Count];
        System.Array.Fill(tiles, tile);
        tilemap.SetTiles(positions.ToArray(), tiles);
    }

    /// <summary>
    /// Whether any of <paramref name="painted"/> lies inside <paramref name="cells"/>.
    /// </summary>
    private static bool AnyIn(HashSet<Vector2Int> painted, RectInt cells)
    {
        foreach (Vector2Int cell in painted)
        {
            if (cells.Contains(cell))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Records <paramref name="target"/> for <see cref="Destroy"/> and returns it.
    /// </summary>
    private T Track<T>(T target) where T : Object
    {
        created.Add(target);
        return target;
    }
}
