using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

/// <summary>
/// Tests for scene generation: the project's Clearing generator gives playable layouts for many seeds (paths on the
/// grass, open ground around a spawn on the road, props clear of the paths and of each other, every mob spread out
/// where it can walk to the spawn), the same seed gives the same layout, and <see cref="AreaLayoutWriter"/> writes a
/// layout into a scene so that the navigation grid blocks exactly the props' cells.
/// </summary>
public class AreaGenerationTests
{
    private const string GeneratorPath = "Assets/Settings/Generation/Generator_Clearing.asset";
    private const string GrassTilePath = "Assets/Tiles/tilesets/grass_DualGridRuleTile.asset";
    private const string ProfilePath = "Assets/Settings/AI/TerrainMovement_Default.asset";
    private const string PropsRootName = "Props";
    private const int SeedCount = 20;

    private static readonly Vector3Int[] Sides = { Vector3Int.right, Vector3Int.left, Vector3Int.up, Vector3Int.down };

    private readonly List<Object> created = new();
    private ClearingGenerator generator;

    /// <summary>
    /// Loads the project's Clearing generator, which the tests only read.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        generator = AssetDatabase.LoadAssetAtPath<ClearingGenerator>(GeneratorPath);
        Assert.That(generator, Is.Not.Null, $"{GeneratorPath} should exist.");
    }

    /// <summary>
    /// Destroys every object the test created, and the roots the writer added to the scene.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (root.name == AreaLayoutWriter.MobSpawnsRootName || root.name == PropsRootName)
            {
                created.Add(root);
            }
        }

        foreach (Object instance in created)
        {
            if (instance != null)
            {
                Object.DestroyImmediate(instance);
            }
        }

        created.Clear();
    }

    /// <summary>
    /// For every seed the road and the gravel paths lie on the grass, the spawn is on the road with grass all around
    /// it for the clear radius, and no blocking prop stands near the spawn or within the clearance of a path.
    /// </summary>
    [Test]
    public void ClearingLayouts_KeepThePathsOnTheGrass_AndTheGroundAroundTheSpawnOpen()
    {
        for (int seed = 1; seed <= SeedCount; seed++)
        {
            AreaLayout layout = generator.Generate(seed);
            HashSet<Vector3Int> grass = layout.TerrainCells(0);
            HashSet<Vector3Int> gravel = layout.TerrainCells(1);
            HashSet<Vector3Int> stone = layout.TerrainCells(2);
            HashSet<Vector3Int> footprints = Footprints(layout);

            Assert.That(stone, Is.Not.Empty, $"seed {seed}: the road");
            Assert.That(gravel.Count > 0, Is.EqualTo(generator.GravelPathCount > 0), $"seed {seed}: the gravel paths");
            Assert.That(grass.IsSupersetOf(stone) && grass.IsSupersetOf(gravel), Is.True, $"seed {seed}: paths only on grass");
            Assert.That(stone.Contains(layout.SpawnCell), Is.True, $"seed {seed}: the spawn is on the road");

            int radius = generator.SpawnClearRadius;
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    Vector3Int cell = layout.SpawnCell + new Vector3Int(dx, dy, 0);
                    Assert.That(grass.Contains(cell), Is.True, $"seed {seed}: grass at {cell} around the spawn");
                    if (dx * dx + dy * dy <= radius * radius)
                    {
                        Assert.That(footprints.Contains(cell), Is.False, $"seed {seed}: a prop at {cell} near the spawn");
                    }
                }
            }

            int clearance = generator.PathClearance;
            foreach (Vector3Int cell in footprints)
            {
                for (int dy = -clearance; dy <= clearance; dy++)
                {
                    for (int dx = -clearance; dx <= clearance; dx++)
                    {
                        Vector3Int near = cell + new Vector3Int(dx, dy, 0);
                        Assert.That(stone.Contains(near) || gravel.Contains(near), Is.False, $"seed {seed}: a prop at {cell} beside a path");
                    }
                }
            }
        }
    }

    /// <summary>
    /// For every seed each blocking footprint covers grass cells no other footprint covers, and each walk-through prop
    /// stands on grass, off the paths and outside every footprint.
    /// </summary>
    [Test]
    public void ClearingLayouts_PutPropsOnGrass_WithoutOverlappingFootprints()
    {
        for (int seed = 1; seed <= SeedCount; seed++)
        {
            AreaLayout layout = generator.Generate(seed);
            HashSet<Vector3Int> grass = layout.TerrainCells(0);
            HashSet<Vector3Int> footprints = new();
            int blocking = 0;
            foreach (AreaLayout.Prop prop in layout.Props)
            {
                if (!PropGridSnapping.TryGetFootprint(prop.Prefab, out Rect footprint))
                {
                    continue;
                }

                blocking++;
                foreach (Vector3Int cell in CoveredCells(prop, footprint))
                {
                    Assert.That(grass.Contains(cell), Is.True, $"seed {seed}: {prop.Prefab.name} on grass at {cell}");
                    Assert.That(footprints.Add(cell), Is.True, $"seed {seed}: two footprints on {cell}");
                }
            }

            Assert.That(blocking, Is.GreaterThan(0), $"seed {seed}: trees placed");
            foreach (AreaLayout.Prop prop in layout.Props)
            {
                if (PropGridSnapping.TryGetFootprint(prop.Prefab, out _))
                {
                    continue;
                }

                Assert.That(grass.Contains(prop.Cell), Is.True, $"seed {seed}: {prop.Prefab.name} on grass");
                Assert.That(layout.TerrainCells(1).Contains(prop.Cell) || layout.TerrainCells(2).Contains(prop.Cell), Is.False, $"seed {seed}: {prop.Prefab.name} on a path");
                Assert.That(footprints.Contains(prop.Cell), Is.False, $"seed {seed}: {prop.Prefab.name} under a footprint");
                Assert.That(prop.Offset.x, Is.InRange(0f, 1f));
                Assert.That(prop.Offset.y, Is.InRange(0f, 1f));
            }
        }
    }

    /// <summary>
    /// For every seed all the configured mobs are placed, on open grass the spawn can walk to (4-connected, around
    /// the footprints, like the navigation grid's regions), far enough from the spawn and from each other.
    /// </summary>
    [Test]
    public void ClearingLayouts_SpreadEveryMob_WhereItCanWalkToTheSpawn()
    {
        for (int seed = 1; seed <= SeedCount; seed++)
        {
            AreaLayout layout = generator.Generate(seed);
            HashSet<Vector3Int> grass = layout.TerrainCells(0);
            HashSet<Vector3Int> footprints = Footprints(layout);
            HashSet<Vector3Int> reachable = Reachable(layout.SpawnCell, cell => grass.Contains(cell) && !footprints.Contains(cell));

            Assert.That(layout.Warnings, Is.Empty, $"seed {seed}");
            Assert.That(layout.Mobs.Count, Is.EqualTo(generator.MobCount), $"seed {seed}");
            for (int i = 0; i < layout.Mobs.Count; i++)
            {
                AreaLayout.Mob mob = layout.Mobs[i];
                Assert.That(mob.Prefab, Is.SameAs(generator.MobPrefab));
                Assert.That(reachable.Contains(mob.Cell), Is.True, $"seed {seed}: mob at {mob.Cell} reachable from the spawn");
                Assert.That(Vector3Int.Distance(mob.Cell, layout.SpawnCell), Is.GreaterThanOrEqualTo(generator.MobDistanceFromSpawn), $"seed {seed}");
                for (int j = 0; j < i; j++)
                {
                    Assert.That(Vector3Int.Distance(mob.Cell, layout.Mobs[j].Cell), Is.GreaterThanOrEqualTo(generator.MobSpacing), $"seed {seed}");
                }
            }
        }
    }

    /// <summary>
    /// Generating twice from one seed gives the same cells, props, mobs and spawn; another seed gives another layout.
    /// </summary>
    [Test]
    public void TheSameSeed_GivesTheSameLayout_AndAnotherSeedAnotherOne()
    {
        AreaLayout first = generator.Generate(7);
        AreaLayout again = generator.Generate(7);
        AreaLayout other = generator.Generate(8);

        for (int layer = 0; layer < first.TerrainLayerCount; layer++)
        {
            Assert.That(first.TerrainCells(layer).SetEquals(again.TerrainCells(layer)), Is.True, $"layer {layer}");
        }

        Assert.That(again.Props.Count, Is.EqualTo(first.Props.Count));
        for (int i = 0; i < first.Props.Count; i++)
        {
            Assert.That(again.Props[i].Prefab, Is.SameAs(first.Props[i].Prefab));
            Assert.That(again.Props[i].Cell, Is.EqualTo(first.Props[i].Cell));
            Assert.That(again.Props[i].Offset, Is.EqualTo(first.Props[i].Offset));
        }

        Assert.That(again.Mobs.Count, Is.EqualTo(first.Mobs.Count));
        for (int i = 0; i < first.Mobs.Count; i++)
        {
            Assert.That(again.Mobs[i].Cell, Is.EqualTo(first.Mobs[i].Cell));
        }

        Assert.That(again.SpawnCell, Is.EqualTo(first.SpawnCell));
        Assert.That(other.TerrainCells(0).SetEquals(first.TerrainCells(0)) && other.TerrainCells(2).SetEquals(first.TerrainCells(2)), Is.False);
    }

    /// <summary>
    /// Writing a layout into a scene with only a grass layer creates the missing path layers in order, paints every
    /// layer's data tilemap with exactly its cells, sets up each one's rule tile, navigation source and draw order,
    /// replaces the old props and mob spawn points and moves the start spawn point; the navigation grid then blocks
    /// exactly the footprint cells, and every mob can walk to the spawn.
    /// </summary>
    [Test]
    public void TheWriter_PaintsEveryLayer_ReplacesPropsAndMobSpawns_AndTheGridBlocksTheFootprints()
    {
        Grid grid = CreateAreaWithGrass();
        Transform props = Track(new GameObject(PropsRootName)).transform;
        GameObject stale = new("Stale");
        stale.transform.SetParent(props, false);
        GameObject oldMob = Track(new GameObject("OldMobSpawn"));
        oldMob.AddComponent<MobSpawnPoint>();
        SpawnPoint start = Track(new GameObject("SpawnPoint_start")).AddComponent<SpawnPoint>();
        AreaLayout layout = generator.Generate(3);

        Assert.That(AreaLayoutWriter.Write(grid, generator, layout), Is.True);

        IReadOnlyList<AreaGenerator.TerrainLayer> layers = generator.TerrainLayers;
        for (int i = 0; i < layers.Count; i++)
        {
            Transform child = grid.transform.Find(layers[i].TilemapName);
            Assert.That(child, Is.Not.Null, layers[i].TilemapName);
            Assert.That(child.GetSiblingIndex(), Is.EqualTo(i), "Layers are listed bottom first.");
            DualGridTilemapModule module = child.GetComponent<DualGridTilemapModule>();
            Assert.That(module.RenderTile, Is.SameAs(layers[i].RuleTile));
            NavigationTerrainSource2D source = child.GetComponent<NavigationTerrainSource2D>();
            Assert.That(source.TerrainType, Is.SameAs(layers[i].TerrainType));
            Assert.That(source.Priority, Is.EqualTo(layers[i].NavigationPriority));
            TilemapRenderer renderer = module.RenderTilemap.GetComponent<TilemapRenderer>();
            Assert.That(renderer.sortingLayerName, Is.EqualTo("Terrain"));
            Assert.That(renderer.sortingOrder, Is.EqualTo(layers[i].SortingOrder));
            Assert.That(FilledCells(module.DataTilemap).SetEquals(layout.TerrainCells(i)), Is.True, $"{layers[i].TilemapName} cells");
            Assert.That(FilledCells(module.RenderTilemap), Is.Not.Empty, $"{layers[i].TilemapName} is drawn");
        }

        Assert.That(stale == null, Is.True, "Old props are removed.");
        Assert.That(oldMob == null, Is.True, "Old mob spawn points are removed.");
        Assert.That(props.childCount, Is.EqualTo(layout.Props.Count));
        for (int i = 0; i < layout.Props.Count; i++)
        {
            AreaLayout.Prop prop = layout.Props[i];
            Transform instance = props.GetChild(i);
            Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(instance.gameObject), Is.SameAs(prop.Prefab));
            Vector3 expected = PropGridSnapping.TryGetFootprint(prop.Prefab, out Rect footprint)
                ? PropGridSnapping.RootOnCells(grid, prop.Cell, footprint)
                : PropGridSnapping.SnapToPixels(new Vector2(prop.Cell.x, prop.Cell.y) + prop.Offset);
            Assert.That(Vector3.Distance(instance.position, expected), Is.LessThan(0.0001f), prop.Prefab.name);
        }

        MobSpawnPoint[] spawns = SceneQuery.FindAll<MobSpawnPoint>(SceneManager.GetActiveScene());
        Assert.That(spawns.Length, Is.EqualTo(layout.Mobs.Count));
        for (int i = 0; i < spawns.Length; i++)
        {
            Assert.That(spawns[i].MobPrefab, Is.SameAs(layout.Mobs[i].Prefab));
            Assert.That((Vector2)spawns[i].transform.position, Is.EqualTo(CellCentre(layout.Mobs[i].Cell)));
            Assert.That(spawns[i].transform.parent.name, Is.EqualTo(AreaLayoutWriter.MobSpawnsRootName));
        }

        Assert.That((Vector2)start.transform.position, Is.EqualTo(CellCentre(layout.SpawnCell)));

        NavigationGrid2D navigation = grid.GetComponent<NavigationGrid2D>();
        navigation.ConfigureObstacleLayers(LayerMask.GetMask("Obstacles"));
        navigation.BuildGrid();
        TerrainMovementProfile2D profile = AssetDatabase.LoadAssetAtPath<TerrainMovementProfile2D>(ProfilePath);
        HashSet<Vector3Int> footprints = Footprints(layout);
        foreach (Vector3Int cell in layout.TerrainCells(0))
        {
            Assert.That(navigation.IsCellWalkable(cell, profile), Is.EqualTo(!footprints.Contains(cell)), $"cell {cell}");
        }

        foreach (AreaLayout.Mob mob in layout.Mobs)
        {
            Assert.That(navigation.AreCellsConnected(layout.SpawnCell, mob.Cell, profile), Is.True, $"mob at {mob.Cell}");
        }
    }

    /// <summary>
    /// Builds a grid of one-unit cells carrying a navigation grid, with a dual-grid grass tilemap named like the
    /// generator's first layer but none of its path layers.
    /// </summary>
    private Grid CreateAreaWithGrass()
    {
        Grid grid = Track(new GameObject("Grid")).AddComponent<Grid>();
        grid.gameObject.AddComponent<NavigationGrid2D>();
        DualGridTilemapModule grass = DualGridTilemapModuleEditor.CreateNewDualGridTilemap(grid);
        grass.name = generator.TerrainLayers[0].TilemapName;
        grass.RenderTile = AssetDatabase.LoadAssetAtPath<DualGridRuleTile>(GrassTilePath);
        grass.gameObject.AddComponent<NavigationTerrainSource2D>();
        return grid;
    }

    /// <summary>
    /// Every cell of <paramref name="tilemap"/> that holds a tile.
    /// </summary>
    private static HashSet<Vector3Int> FilledCells(Tilemap tilemap)
    {
        HashSet<Vector3Int> cells = new();
        foreach (Vector3Int cell in tilemap.cellBounds.allPositionsWithin)
        {
            if (tilemap.HasTile(cell))
            {
                cells.Add(cell);
            }
        }

        return cells;
    }

    /// <summary>
    /// Every cell a blocking prop of <paramref name="layout"/> covers.
    /// </summary>
    private static HashSet<Vector3Int> Footprints(AreaLayout layout)
    {
        HashSet<Vector3Int> cells = new();
        foreach (AreaLayout.Prop prop in layout.Props)
        {
            if (PropGridSnapping.TryGetFootprint(prop.Prefab, out Rect footprint))
            {
                cells.UnionWith(CoveredCells(prop, footprint));
            }
        }

        return cells;
    }

    /// <summary>
    /// The cells <paramref name="prop"/>'s <paramref name="footprint"/> covers from its bottom-left cell.
    /// </summary>
    private static List<Vector3Int> CoveredCells(AreaLayout.Prop prop, Rect footprint)
    {
        Vector2Int covered = PropGridSnapping.CellsCovered(footprint, Vector2.one);
        List<Vector3Int> cells = new();
        for (int dy = 0; dy < covered.y; dy++)
        {
            for (int dx = 0; dx < covered.x; dx++)
            {
                cells.Add(prop.Cell + new Vector3Int(dx, dy, 0));
            }
        }

        return cells;
    }

    /// <summary>
    /// The cells 4-connected to <paramref name="start"/> through cells that pass <paramref name="isOpen"/>.
    /// </summary>
    private static HashSet<Vector3Int> Reachable(Vector3Int start, System.Predicate<Vector3Int> isOpen)
    {
        HashSet<Vector3Int> region = new() { start };
        Queue<Vector3Int> queue = new();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            Vector3Int cell = queue.Dequeue();
            foreach (Vector3Int side in Sides)
            {
                Vector3Int next = cell + side;
                if (isOpen(next) && region.Add(next))
                {
                    queue.Enqueue(next);
                }
            }
        }

        return region;
    }

    /// <summary>
    /// The centre of <paramref name="cell"/> on a grid of one-unit cells at the origin.
    /// </summary>
    private static Vector2 CellCentre(Vector3Int cell)
    {
        return new Vector2(cell.x + 0.5f, cell.y + 0.5f);
    }

    /// <summary>
    /// Records <paramref name="instance"/> for destruction in <see cref="TearDown"/> and returns it.
    /// </summary>
    private T Track<T>(T instance) where T : Object
    {
        created.Add(instance);
        return instance;
    }
}
