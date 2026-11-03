using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Marks the grid of a chunk scene of a seamless world: which world and chunk it is, and the coast map baked for it
/// (Docs/WorldStreaming.md: Terrain). Runs in edit mode too. When enabled it makes the grid's
/// <see cref="TerrainBlendRenderer"/> draw only its own cells with a skirt around them, and its wall modules
/// (<see cref="DualGridTilemapModule"/>) its own render tiles, then links with each of its eight neighbouring chunks
/// whose scene is open (loaded by the streaming at runtime, or opened beside it in the editor): each gives the other
/// its border cells, the ground layers and raised ground of a strip <see cref="SkirtCells"/> wide for the terrain and
/// its wall data for the walls, and both follow each other's tile changes there. When disabled or unloaded it unlinks,
/// and the neighbours left draw their own edges there again. In play mode its terrain starts hidden:
/// <see cref="LocalWorldPresentation"/> shows the chunks of the local player's world near it.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(Grid))]
[DefaultExecutionOrder(-100)]
public class WorldChunk : MonoBehaviour
{
    public const int SkirtCells = 4;

    private static readonly Vector2Int[] Directions =
    {
        new(-1, -1), new(0, -1), new(1, -1),
        new(-1, 0), new(1, 0),
        new(-1, 1), new(0, 1), new(1, 1)
    };

    [Tooltip("The world this chunk belongs to.")]
    [SerializeField] private WorldDefinition world;
    [Tooltip("The chunk's coordinates in its world, in chunks from the world's origin.")]
    [SerializeField] private Vector2Int coords;
    [Tooltip("The coast map baked for this chunk from the whole world, for its waves; none when the chunk shows no sea with waves. Written by the world's coast bake.")]
    [SerializeField] private Texture2D coastMap;
    [Tooltip("What the coast map was baked from (a hash of the sea around the chunk, or none), written by the coast bake, so the World Editor bakes again only the chunks whose sea or shore changed.")]
    [SerializeField] private string coastSource;
    [Tooltip("The glow map baked for this chunk from the whole world, for its glowing ground's light (lava); none when nothing glows within its reach. Written by the world's glow bake.")]
    [SerializeField] private Texture2D glowMap;
    [Tooltip("The cells the baked glow map covers, inside the chunk.")]
    [SerializeField] private RectInt glowArea;
    [Tooltip("What the glow map was baked from (a hash of the glowing cells within reach of the chunk, or none), written by the glow bake, so the World Editor bakes again only the chunks whose glowing ground changed.")]
    [SerializeField] private string glowSource;

    [System.NonSerialized] private readonly List<WorldChunk> neighbours = new();
    [System.NonSerialized] private readonly List<TerrainSkirtSource> skirt = new();
    [System.NonSerialized] private readonly List<DualGridTilemapModule> neighbourWalls = new();
    [System.NonSerialized] private TerrainBlendRenderer terrain;
    [System.NonSerialized] private DualGridTilemapModule[] walls = new DualGridTilemapModule[0];

    public WorldDefinition World => world;
    public Vector2Int Coords => coords;
    public Texture2D CoastMap => coastMap;
    public string CoastSource => coastSource;
    public Texture2D GlowMap => glowMap;
    public RectInt GlowArea => glowArea;
    public string GlowSource => glowSource;

    /// <summary>
    /// The world-local cells this chunk owns.
    /// </summary>
    public RectInt Cells => ChunkCoords.Bounds(coords);
    public TerrainBlendRenderer Terrain => terrain;
    public IReadOnlyList<WorldChunk> Neighbours => neighbours;

    /// <summary>
    /// Whether its terrain renderer is enabled, so its ground is built and drawn.
    /// </summary>
    public bool IsDrawn => terrain != null && terrain.enabled;

    /// <summary>
    /// Sets which world and chunk it is and its baked coast map, in code (editor tools and tests). Takes effect the
    /// next time it is enabled.
    /// </summary>
    public void Configure(WorldDefinition chunkWorld, Vector2Int chunkCoords, Texture2D bakedCoastMap)
    {
        world = chunkWorld;
        coords = chunkCoords;
        coastMap = bakedCoastMap;
    }

    /// <summary>
    /// Enables or disables its terrain renderer, which builds its ground when enabled and destroys it when disabled.
    /// </summary>
    public void SetDrawn(bool drawn)
    {
        if (terrain != null && terrain.enabled != drawn)
        {
            terrain.enabled = drawn;
        }
    }

    /// <summary>
    /// Gives its neighbours its border cells again and takes theirs, after a change to the layers or wall modules of
    /// its grid or theirs (an editor tool adding a layer).
    /// </summary>
    public void RefreshLinks()
    {
        Cache();
        Refresh();
        for (int i = 0; i < neighbours.Count; i++)
        {
            if (neighbours[i] != null)
            {
                neighbours[i].Refresh();
            }
        }
    }

    /// <summary>
    /// Links with <paramref name="other"/> when it is one of its eight neighbouring chunks of the same world: each
    /// reads the other's border cells. Does nothing for another chunk or one already linked.
    /// </summary>
    internal void Link(WorldChunk other)
    {
        if (other == null || other == this || neighbours.Contains(other) || other.world != world || !IsNeighbour(other.coords))
        {
            return;
        }

        neighbours.Add(other);
        if (!other.neighbours.Contains(this))
        {
            other.neighbours.Add(this);
        }

        Refresh();
        other.Refresh();
    }

    /// <summary>
    /// Unlinks from <paramref name="other"/>: each draws its own edge along their border again.
    /// </summary>
    internal void Unlink(WorldChunk other)
    {
        if (other == null || !neighbours.Remove(other))
        {
            return;
        }

        other.neighbours.Remove(this);
        Refresh();
        other.Refresh();
    }

    /// <summary>
    /// Caches its terrain and walls, and in play mode hides its terrain until the presentation shows it.
    /// </summary>
    private void Awake()
    {
        Cache();
        if (Application.isPlaying)
        {
            SetDrawn(false);
        }
    }

    /// <summary>
    /// Makes its terrain and walls draw this chunk, then links with the neighbours whose scenes are open.
    /// </summary>
    private void OnEnable()
    {
        Cache();
        if (terrain != null)
        {
            terrain.SetChunk(Cells, SkirtCells, coastMap, glowMap, glowArea);
        }

        foreach (DualGridTilemapModule wall in walls)
        {
            if (wall != null)
            {
                wall.SetChunk(Cells, null);
            }
        }

        ForgetNeighbours();
        LinkOpenNeighbours();
        Refresh();
    }

    /// <summary>
    /// Unlinks from every neighbour, so each draws its own edge along their border again.
    /// </summary>
    private void OnDisable()
    {
        ForgetNeighbours();
    }

    /// <summary>
    /// Takes itself out of each neighbour's links, refreshing the neighbour, and forgets them all.
    /// </summary>
    private void ForgetNeighbours()
    {
        for (int i = neighbours.Count - 1; i >= 0; i--)
        {
            WorldChunk neighbour = neighbours[i];
            if (neighbour != null && neighbour.neighbours.Remove(this))
            {
                neighbour.Refresh();
            }
        }

        neighbours.Clear();
    }

    /// <summary>
    /// Finds the terrain renderer on its grid and the wall modules under it.
    /// </summary>
    private void Cache()
    {
        terrain = GetComponent<TerrainBlendRenderer>();
        walls = GetComponentsInChildren<DualGridTilemapModule>(true);
    }

    /// <summary>
    /// Links with each enabled chunk of its world whose scene is open beside its own at one of its eight neighbouring
    /// coordinates, refreshing each of them; the caller refreshes this one once after.
    /// </summary>
    private void LinkOpenNeighbours()
    {
        if (world == null)
        {
            return;
        }

        foreach (Vector2Int direction in Directions)
        {
            Vector2Int at = coords + direction;
            if (!world.TryGetChunkScene(at, out SceneDefinition definition) || !definition.IsValid)
            {
                continue;
            }

            Scene scene = SceneManager.GetSceneByPath(definition.ScenePath);
            if (!scene.IsValid() || !scene.isLoaded || scene == gameObject.scene)
            {
                continue;
            }

            WorldChunk neighbour = SceneQuery.FindFirst<WorldChunk>(scene);
            if (neighbour != null && neighbour != this && neighbour.isActiveAndEnabled && neighbour.world == world && neighbour.coords == at && !neighbours.Contains(neighbour))
            {
                neighbours.Add(neighbour);
                if (!neighbour.neighbours.Contains(this))
                {
                    neighbour.neighbours.Add(this);
                }

                neighbour.Refresh();
            }
        }
    }

    /// <summary>
    /// Whether <paramref name="other"/> is one of the eight chunks around its own.
    /// </summary>
    private bool IsNeighbour(Vector2Int other)
    {
        Vector2Int offset = other - coords;
        return offset != Vector2Int.zero && Mathf.Abs(offset.x) <= 1 && Mathf.Abs(offset.y) <= 1;
    }

    /// <summary>
    /// Gives its terrain the skirt of its linked neighbours' border cells, and its wall modules their wall modules
    /// drawing the same walls.
    /// </summary>
    private void Refresh()
    {
        if (!isActiveAndEnabled)
        {
            return;
        }

        RectInt cells = Cells;
        RectInt reach = new(cells.xMin - SkirtCells, cells.yMin - SkirtCells, cells.width + 2 * SkirtCells, cells.height + 2 * SkirtCells);
        skirt.Clear();
        foreach (WorldChunk neighbour in neighbours)
        {
            if (neighbour == null || neighbour.terrain == null)
            {
                continue;
            }

            RectInt strip = Intersect(neighbour.Cells, reach);
            if (strip.width <= 0 || strip.height <= 0)
            {
                continue;
            }

            foreach (TerrainBlendRenderer.Layer layer in neighbour.terrain.Layers)
            {
                if (layer != null && layer.Tilemap != null && layer.Material != null)
                {
                    skirt.Add(new TerrainSkirtSource(layer.Tilemap, layer.Material, strip));
                }
            }

            if (neighbour.terrain.RaisedTilemap != null)
            {
                skirt.Add(TerrainSkirtSource.Raised(neighbour.terrain.RaisedTilemap, strip));
            }
        }

        if (terrain != null)
        {
            terrain.SetSkirt(skirt);
        }

        foreach (DualGridTilemapModule wall in walls)
        {
            if (wall == null)
            {
                continue;
            }

            neighbourWalls.Clear();
            foreach (WorldChunk neighbour in neighbours)
            {
                if (neighbour == null)
                {
                    continue;
                }

                foreach (DualGridTilemapModule other in neighbour.walls)
                {
                    if (other != null && other.RenderTile == wall.RenderTile)
                    {
                        neighbourWalls.Add(other);
                    }
                }
            }

            wall.SetChunk(Cells, neighbourWalls);
        }
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
}
