using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;

/// <summary>
/// Tests for scene generation: the project's Clearing generator gives playable layouts for many seeds (paths on the
/// grass, open ground around a spawn on the road, props clear of the paths and of each other, every mob spread out
/// where it can walk to the spawn, a campsite on a patch of dirt); the Village Forest generator does too, with a village,
/// a clearing and a forest joined by one connected path network, buildings beside the street with reachable doors, a
/// paved square, dirt along the trails near the village and most mobs in the forest; the Lakeside generator lays a lake
/// off the paths with a beach and a marsh, dry grass on the open grass, and keeps props off the water, the
/// beach and the mud with the beach and every mob within the spawn's reach; the terrain each generator lays from its
/// detail stream leaves the rest of its layout where it was; the same seed gives the same layout; the project's style
/// lists every ground material in the shared draw order, the project's generators list only the layers they lay (plain
/// ground tiles and the project's materials) as a subsequence of it, and the default movement profile blocks water and
/// makes mud cost more; and <see cref="AreaLayoutWriter"/> writes a layout into a scene as plain ground layers drawn by
/// a <see cref="TerrainBlendRenderer"/> in the shared order, with navigation priorities from that order, keeping painted
/// layers the generator does not list and removing empty ones, a renderer that follows a layout written over another,
/// a navigation grid that blocks exactly the props' cells, an area of only some layers that still draws and navigates,
/// and writes up to 16 layers but refuses 17 and refuses layers out of the shared order.
/// </summary>
public class AreaGenerationTests
{
    private const string ClearingGeneratorPath = "Assets/Settings/Generation/Generator_Clearing.asset";
    private const string VillageForestGeneratorPath = "Assets/Settings/Generation/Generator_VillageForest.asset";
    private const string LakesideGeneratorPath = "Assets/Settings/Generation/Generator_Lakeside.asset";
    private const string TerrainStylePath = "Assets/Settings/Terrain/TerrainStyle.asset";
    private const string ProfilePath = "Assets/Settings/AI/TerrainMovement_Default.asset";
    private const string PropsRootName = "Props";
    private const int SeedCount = 20;
    private const int MinRegionCells = 300;

    private static readonly Vector3Int[] Sides = { Vector3Int.right, Vector3Int.left, Vector3Int.up, Vector3Int.down };
    private static readonly VillageForestGenerator.Region[] Regions =
    {
        VillageForestGenerator.Region.Village,
        VillageForestGenerator.Region.Clearing,
        VillageForestGenerator.Region.Forest
    };

    private readonly List<Object> created = new();
    private readonly Dictionary<int, VillageForestGenerator.Generation> villageForestLayouts = new();
    private ClearingGenerator generator;
    private VillageForestGenerator villageForest;
    private LakesideGenerator lakeside;
    private int undoGroup;

    /// <summary>
    /// Loads the project's generators, which the tests only read, and starts the undo group the test's scene changes
    /// are reverted to.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        generator = AssetDatabase.LoadAssetAtPath<ClearingGenerator>(ClearingGeneratorPath);
        Assert.That(generator, Is.Not.Null, $"{ClearingGeneratorPath} should exist.");
        villageForest = AssetDatabase.LoadAssetAtPath<VillageForestGenerator>(VillageForestGeneratorPath);
        Assert.That(villageForest, Is.Not.Null, $"{VillageForestGeneratorPath} should exist.");
        lakeside = AssetDatabase.LoadAssetAtPath<LakesideGenerator>(LakesideGeneratorPath);
        Assert.That(lakeside, Is.Not.Null, $"{LakesideGeneratorPath} should exist.");
        Undo.IncrementCurrentGroup();
        undoGroup = Undo.GetCurrentGroup();
    }

    /// <summary>
    /// Reverts the writer's undoable changes, so the Test Runner's own undo revert cannot bring back the objects it
    /// destroyed, then destroys every object the test created and the roots the writer added to the scene.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        Undo.RevertAllDownToGroup(undoGroup);
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
            HashSet<Vector3Int> grass = Cells(layout, "Grass");
            HashSet<Vector3Int> gravel = Cells(layout, "Gravel");
            HashSet<Vector3Int> stone = Cells(layout, "Stone");
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
            HashSet<Vector3Int> grass = Cells(layout, "Grass");
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
                Assert.That(Cells(layout, "Gravel").Contains(prop.Cell) || Cells(layout, "Stone").Contains(prop.Cell), Is.False, $"seed {seed}: {prop.Prefab.name} on a path");
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
            HashSet<Vector3Int> grass = Cells(layout, "Grass");
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
    /// For every Village Forest seed, with no warnings: each region is a sizeable stretch of grass; the stone street
    /// runs through the village and gravel trails reach the clearing and the forest; the paths lie on the grass, no
    /// footprint covers them, and they form one 4-connected network with the spawn on it; the forest is several times
    /// as dense with footprints as the clearing; and almost all the open ground of each region can be walked to from
    /// the spawn, so the trees seal off no more than a few stray cells.
    /// </summary>
    [Test]
    public void VillageForestLayouts_JoinAVillageAClearingAndAForest_WithOneConnectedPathNetwork()
    {
        for (int seed = 1; seed <= SeedCount; seed++)
        {
            VillageForestGenerator.Generation generation = VillageForestLayout(seed);
            AreaLayout layout = generation.Layout;
            HashSet<Vector3Int> grass = Cells(layout, "Grass");
            HashSet<Vector3Int> paths = PathCells(layout);
            HashSet<Vector3Int> footprints = Footprints(layout);
            HashSet<Vector3Int> reachable = Reachable(layout.SpawnCell, cell => grass.Contains(cell) && !footprints.Contains(cell));

            Assert.That(layout.Warnings, Is.Empty, $"seed {seed}");
            Assert.That(grass.IsSupersetOf(paths), Is.True, $"seed {seed}: paths only on grass");
            Assert.That(paths.Overlaps(footprints), Is.False, $"seed {seed}: a footprint on a path");
            Assert.That(Reachable(layout.SpawnCell, paths.Contains).SetEquals(paths), Is.True, $"seed {seed}: one path network through the spawn");

            Dictionary<VillageForestGenerator.Region, int> cells = new();
            Dictionary<VillageForestGenerator.Region, int> covered = new();
            Dictionary<VillageForestGenerator.Region, int> open = new();
            Dictionary<VillageForestGenerator.Region, int> reached = new();
            foreach (VillageForestGenerator.Region region in Regions)
            {
                cells[region] = covered[region] = open[region] = reached[region] = 0;
            }

            int villageStone = 0;
            int clearingGravel = 0;
            int forestGravel = 0;
            foreach (KeyValuePair<Vector3Int, VillageForestGenerator.Region> entry in generation.Regions)
            {
                VillageForestGenerator.Region region = entry.Value;
                cells[region]++;
                if (footprints.Contains(entry.Key))
                {
                    covered[region]++;
                }
                else
                {
                    open[region]++;
                    reached[region] += reachable.Contains(entry.Key) ? 1 : 0;
                }

                villageStone += region == VillageForestGenerator.Region.Village && Cells(layout, "Stone").Contains(entry.Key) ? 1 : 0;
                clearingGravel += region == VillageForestGenerator.Region.Clearing && Cells(layout, "Gravel").Contains(entry.Key) ? 1 : 0;
                forestGravel += region == VillageForestGenerator.Region.Forest && Cells(layout, "Gravel").Contains(entry.Key) ? 1 : 0;
            }

            Assert.That(grass.SetEquals(generation.Regions.Keys), Is.True, $"seed {seed}: every grass cell has a region");
            foreach (VillageForestGenerator.Region region in Regions)
            {
                Assert.That(cells[region], Is.GreaterThanOrEqualTo(MinRegionCells), $"seed {seed}: {region} cells");
                float share = reached[region] / (float)open[region];
                Assert.That(share, Is.GreaterThanOrEqualTo(region == VillageForestGenerator.Region.Forest ? 0.97f : 0.99f), $"seed {seed}: {region} open ground reachable");
            }

            Assert.That(villageStone, Is.GreaterThan(0), $"seed {seed}: the street through the village");
            Assert.That(clearingGravel, Is.GreaterThan(0), $"seed {seed}: a trail into the clearing");
            Assert.That(forestGravel, Is.GreaterThan(0), $"seed {seed}: trails into the forest");
            float forestDensity = covered[VillageForestGenerator.Region.Forest] / (float)cells[VillageForestGenerator.Region.Forest];
            float clearingDensity = covered[VillageForestGenerator.Region.Clearing] / (float)cells[VillageForestGenerator.Region.Clearing];
            Assert.That(forestDensity, Is.GreaterThan(3f * clearingDensity), $"seed {seed}: the forest is dense and the clearing open");
        }
    }

    /// <summary>
    /// For every Village Forest seed all the configured buildings stand in the village, off the paths, and the only
    /// path cell within the path clearance of each is its doorstep, the cell in front of its door: that cell is stone
    /// and can be walked to from the spawn. The fountain stands in its paved square, and lamp posts and fences line the
    /// street.
    /// </summary>
    [Test]
    public void VillageForestLayouts_FaceTheBuildingsOnTheStreet_WithReachableDoors()
    {
        int clearance = villageForest.PathClearance;
        for (int seed = 1; seed <= SeedCount; seed++)
        {
            VillageForestGenerator.Generation generation = VillageForestLayout(seed);
            AreaLayout layout = generation.Layout;
            HashSet<Vector3Int> grass = Cells(layout, "Grass");
            HashSet<Vector3Int> stone = Cells(layout, "Stone");
            HashSet<Vector3Int> paths = PathCells(layout);
            HashSet<Vector3Int> footprints = Footprints(layout);
            HashSet<Vector3Int> reachable = Reachable(layout.SpawnCell, cell => grass.Contains(cell) && !footprints.Contains(cell));
            int buildings = 0;
            int fountains = 0;
            int lamps = 0;
            int fences = 0;
            foreach (AreaLayout.Prop prop in layout.Props)
            {
                if (prop.Prefab == villageForest.FountainPrefab)
                {
                    fountains++;
                    Assert.That(Cells(layout, "Plaza").Contains(prop.Cell + new Vector3Int(-clearance - 1, 0, 0)), Is.True, $"seed {seed}: the fountain's square");
                }

                lamps += prop.Prefab == villageForest.LampPostPrefab ? 1 : 0;
                fences += prop.Prefab == villageForest.FencePrefab ? 1 : 0;
                if (prop.Prefab != villageForest.BuildingPrefab)
                {
                    continue;
                }

                buildings++;
                Vector3Int doorstep = prop.Cell + new Vector3Int(villageForest.DoorColumn, -1, 0);
                Assert.That(stone.Contains(doorstep), Is.True, $"seed {seed}: the path to the door at {doorstep}");
                Assert.That(reachable.Contains(doorstep), Is.True, $"seed {seed}: the door at {doorstep} reachable from the spawn");
                PropGridSnapping.TryGetFootprint(prop.Prefab, out Rect footprint);
                foreach (Vector3Int cell in CoveredCells(prop, footprint))
                {
                    Assert.That(generation.Regions[cell], Is.EqualTo(VillageForestGenerator.Region.Village), $"seed {seed}: building at {cell}");
                    foreach (Vector3Int near in CellsWithin(cell, clearance))
                    {
                        Assert.That(!paths.Contains(near) || near == doorstep, Is.True, $"seed {seed}: the building at {prop.Cell} beside the path at {near}");
                    }
                }
            }

            Assert.That(buildings, Is.EqualTo(villageForest.BuildingCount), $"seed {seed}: buildings");
            Assert.That(fountains, Is.EqualTo(1), $"seed {seed}: the fountain");
            Assert.That(lamps, Is.GreaterThanOrEqualTo(2), $"seed {seed}: lamp posts");
            Assert.That(fences, Is.GreaterThanOrEqualTo(villageForest.BuildingCount), $"seed {seed}: fences");
        }
    }

    /// <summary>
    /// For every Village Forest seed the spawn is on the street with grass all around it for the clear radius and no
    /// blocking prop near it; footprints cover grass cells no other footprint covers, and none but the buildings (whose
    /// doorsteps the previous test covers) comes within the clearance of a path; walk-through props stand on grass,
    /// off the paths and outside every footprint.
    /// </summary>
    [Test]
    public void VillageForestLayouts_KeepTheSpawnOpen_AndBlockingPropsOffThePaths()
    {
        int radius = villageForest.SpawnClearRadius;
        int clearance = villageForest.PathClearance;
        for (int seed = 1; seed <= SeedCount; seed++)
        {
            AreaLayout layout = VillageForestLayout(seed).Layout;
            HashSet<Vector3Int> grass = Cells(layout, "Grass");
            HashSet<Vector3Int> paths = PathCells(layout);
            HashSet<Vector3Int> footprints = new();
            foreach (AreaLayout.Prop prop in layout.Props)
            {
                if (!PropGridSnapping.TryGetFootprint(prop.Prefab, out Rect footprint))
                {
                    continue;
                }

                foreach (Vector3Int cell in CoveredCells(prop, footprint))
                {
                    Assert.That(grass.Contains(cell), Is.True, $"seed {seed}: {prop.Prefab.name} on grass at {cell}");
                    Assert.That(footprints.Add(cell), Is.True, $"seed {seed}: two footprints on {cell}");
                    if (prop.Prefab == villageForest.BuildingPrefab)
                    {
                        continue;
                    }

                    foreach (Vector3Int near in CellsWithin(cell, clearance))
                    {
                        Assert.That(paths.Contains(near), Is.False, $"seed {seed}: {prop.Prefab.name} at {cell} beside a path");
                    }
                }
            }

            Assert.That(Cells(layout, "Stone").Contains(layout.SpawnCell), Is.True, $"seed {seed}: the spawn is on the street");
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

            foreach (AreaLayout.Prop prop in layout.Props)
            {
                if (PropGridSnapping.TryGetFootprint(prop.Prefab, out _))
                {
                    continue;
                }

                Assert.That(grass.Contains(prop.Cell), Is.True, $"seed {seed}: {prop.Prefab.name} on grass");
                Assert.That(paths.Contains(prop.Cell), Is.False, $"seed {seed}: {prop.Prefab.name} on a path");
                Assert.That(footprints.Contains(prop.Cell), Is.False, $"seed {seed}: {prop.Prefab.name} under a footprint");
                Assert.That(prop.Offset.x, Is.InRange(0f, 1f));
                Assert.That(prop.Offset.y, Is.InRange(0f, 1f));
            }
        }
    }

    /// <summary>
    /// For every Village Forest seed each region has exactly its configured mobs, most of them in the forest, and every
    /// mob stands on open grass the spawn can walk to, far enough from the spawn and from every other mob.
    /// </summary>
    [Test]
    public void VillageForestLayouts_SpreadTheMobs_MostlyInTheForest_WhereTheSpawnCanWalkToThem()
    {
        for (int seed = 1; seed <= SeedCount; seed++)
        {
            VillageForestGenerator.Generation generation = VillageForestLayout(seed);
            AreaLayout layout = generation.Layout;
            HashSet<Vector3Int> grass = Cells(layout, "Grass");
            HashSet<Vector3Int> footprints = Footprints(layout);
            HashSet<Vector3Int> reachable = Reachable(layout.SpawnCell, cell => grass.Contains(cell) && !footprints.Contains(cell));
            Dictionary<VillageForestGenerator.Region, int> counts = new();
            foreach (VillageForestGenerator.Region region in Regions)
            {
                counts[region] = 0;
            }

            for (int i = 0; i < layout.Mobs.Count; i++)
            {
                AreaLayout.Mob mob = layout.Mobs[i];
                counts[generation.Regions[mob.Cell]]++;
                Assert.That(mob.Prefab, Is.SameAs(villageForest.MobPrefab));
                Assert.That(reachable.Contains(mob.Cell), Is.True, $"seed {seed}: mob at {mob.Cell} reachable from the spawn");
                Assert.That(Vector3Int.Distance(mob.Cell, layout.SpawnCell), Is.GreaterThanOrEqualTo(villageForest.MobDistanceFromSpawn), $"seed {seed}");
                for (int j = 0; j < i; j++)
                {
                    Assert.That(Vector3Int.Distance(mob.Cell, layout.Mobs[j].Cell), Is.GreaterThanOrEqualTo(villageForest.MobSpacing), $"seed {seed}");
                }
            }

            foreach (VillageForestGenerator.Region region in Regions)
            {
                Assert.That(counts[region], Is.EqualTo(villageForest.MobCount(region)), $"seed {seed}: mobs in the {region}");
            }

            Assert.That(2 * counts[VillageForestGenerator.Region.Forest], Is.GreaterThan(layout.Mobs.Count), $"seed {seed}: most mobs in the forest");
        }
    }

    /// <summary>
    /// For every Lakeside seed, with no warnings: every terrain layer has cells and lies on the grass; the dry grass lies
    /// on open grass, off the water and the beach and not on or beside mud or a path; the lake is water off every path and at least the
    /// road margin from the road, the beach and the marsh lie beside it and off
    /// it, and the gravel trail reaches the beach; the spawn is on the road with no footprint or water within its clear
    /// radius.
    /// </summary>
    [Test]
    public void LakesideLayouts_LayEveryTerrainType_WithTheLakeOffThePaths()
    {
        for (int seed = 1; seed <= SeedCount; seed++)
        {
            AreaLayout layout = lakeside.Generate(seed);
            HashSet<Vector3Int> grass = Cells(layout, "Grass");
            HashSet<Vector3Int> water = Cells(layout, "Water");
            HashSet<Vector3Int> sand = Cells(layout, "Sand");
            HashSet<Vector3Int> mud = Cells(layout, "Mud");
            HashSet<Vector3Int> stone = Cells(layout, "Stone");
            HashSet<Vector3Int> paths = PathCells(layout);

            Assert.That(layout.Warnings, Is.Empty, $"seed {seed}");
            Assert.That(layout.TerrainLayerCount, Is.EqualTo(7));
            for (int layer = 0; layer < layout.TerrainLayerCount; layer++)
            {
                Assert.That(layout.TerrainCells(layer), Is.Not.Empty, $"seed {seed}: layer {layer}");
                Assert.That(grass.IsSupersetOf(layout.TerrainCells(layer)), Is.True, $"seed {seed}: layer {layer} on grass");
            }

            Assert.That(water.Overlaps(paths) || water.Overlaps(sand) || water.Overlaps(mud), Is.False, $"seed {seed}: the lake covers nothing else");
            HashSet<Vector3Int> dryGrass = Cells(layout, "DryGrass");
            Assert.That(dryGrass.Overlaps(water) || dryGrass.Overlaps(sand) || dryGrass.Overlaps(mud) || dryGrass.Overlaps(paths), Is.False, $"seed {seed}: dry grass only on open grass");
            foreach (Vector3Int cell in dryGrass)
            {
                foreach (Vector3Int near in CellsWithin(cell, 1))
                {
                    Assert.That(paths.Contains(near) || mud.Contains(near), Is.False, $"seed {seed}: dry grass at {cell} beside mud or a path");
                }
            }
            foreach (Vector3Int cell in stone)
            {
                foreach (Vector3Int near in CellsWithin(cell, lakeside.RoadMargin))
                {
                    Assert.That(water.Contains(near), Is.False, $"seed {seed}: water at {near} near the road");
                }
            }

            int beside = 0;
            foreach (Vector3Int cell in sand)
            {
                foreach (Vector3Int near in CellsWithin(cell, 1))
                {
                    if (water.Contains(near))
                    {
                        beside++;
                        break;
                    }
                }
            }

            Assert.That(beside, Is.GreaterThan(sand.Count / 6), $"seed {seed}: the beach lies along the water");
            bool trailReachesBeach = false;
            foreach (Vector3Int cell in Cells(layout, "Gravel"))
            {
                foreach (Vector3Int side in Sides)
                {
                    trailReachesBeach |= sand.Contains(cell + side);
                }
            }

            Assert.That(trailReachesBeach, Is.True, $"seed {seed}: the trail reaches the beach");
            Assert.That(stone.Contains(layout.SpawnCell), Is.True, $"seed {seed}: the spawn is on the road");
            HashSet<Vector3Int> footprints = Footprints(layout);
            int radius = lakeside.SpawnClearRadius;
            foreach (Vector3Int cell in CellsWithin(layout.SpawnCell, radius))
            {
                Vector3Int offset = cell - layout.SpawnCell;
                if (offset.x * offset.x + offset.y * offset.y <= radius * radius)
                {
                    Assert.That(footprints.Contains(cell) || water.Contains(cell), Is.False, $"seed {seed}: {cell} near the spawn is open");
                }
            }
        }
    }

    /// <summary>
    /// For every Lakeside seed each blocking footprint covers dry grass that is neither beach nor mud, no other
    /// footprint covers it and no path lies within the clearance; each walk-through prop stands on such grass off the
    /// paths, not beside the water and outside every footprint.
    /// </summary>
    [Test]
    public void LakesideLayouts_KeepPropsOffTheWaterTheBeachAndTheMud()
    {
        for (int seed = 1; seed <= SeedCount; seed++)
        {
            AreaLayout layout = lakeside.Generate(seed);
            HashSet<Vector3Int> grass = Cells(layout, "Grass");
            HashSet<Vector3Int> water = Cells(layout, "Water");
            HashSet<Vector3Int> sand = Cells(layout, "Sand");
            HashSet<Vector3Int> mud = Cells(layout, "Mud");
            HashSet<Vector3Int> paths = PathCells(layout);
            HashSet<Vector3Int> footprints = new();
            foreach (AreaLayout.Prop prop in layout.Props)
            {
                if (!PropGridSnapping.TryGetFootprint(prop.Prefab, out Rect footprint))
                {
                    continue;
                }

                foreach (Vector3Int cell in CoveredCells(prop, footprint))
                {
                    Assert.That(grass.Contains(cell) && !water.Contains(cell) && !sand.Contains(cell) && !mud.Contains(cell), Is.True, $"seed {seed}: {prop.Prefab.name} on open grass at {cell}");
                    Assert.That(footprints.Add(cell), Is.True, $"seed {seed}: two footprints on {cell}");
                    foreach (Vector3Int near in CellsWithin(cell, lakeside.PathClearance))
                    {
                        Assert.That(paths.Contains(near), Is.False, $"seed {seed}: a prop at {cell} beside a path");
                    }
                }
            }

            Assert.That(footprints, Is.Not.Empty, $"seed {seed}: trees placed");
            foreach (AreaLayout.Prop prop in layout.Props)
            {
                if (PropGridSnapping.TryGetFootprint(prop.Prefab, out _))
                {
                    continue;
                }

                Assert.That(grass.Contains(prop.Cell) && !sand.Contains(prop.Cell) && !mud.Contains(prop.Cell) && !paths.Contains(prop.Cell), Is.True, $"seed {seed}: {prop.Prefab.name} at {prop.Cell}");
                Assert.That(footprints.Contains(prop.Cell), Is.False, $"seed {seed}: {prop.Prefab.name} under a footprint");
                foreach (Vector3Int near in CellsWithin(prop.Cell, 1))
                {
                    Assert.That(water.Contains(near), Is.False, $"seed {seed}: {prop.Prefab.name} at {prop.Cell} beside the water");
                }
            }
        }
    }

    /// <summary>
    /// For every Lakeside seed the spawn can walk (4-connected, around the footprints and the water) to every beach
    /// cell and to all the configured mobs, which stand on dry ground far enough from the spawn and from each other.
    /// </summary>
    [Test]
    public void LakesideLayouts_LetTheSpawnWalkToTheBeachAndEveryMob()
    {
        for (int seed = 1; seed <= SeedCount; seed++)
        {
            AreaLayout layout = lakeside.Generate(seed);
            HashSet<Vector3Int> grass = Cells(layout, "Grass");
            HashSet<Vector3Int> water = Cells(layout, "Water");
            HashSet<Vector3Int> footprints = Footprints(layout);
            HashSet<Vector3Int> reachable = Reachable(layout.SpawnCell, cell => grass.Contains(cell) && !water.Contains(cell) && !footprints.Contains(cell));

            Assert.That(reachable.IsSupersetOf(Cells(layout, "Sand")), Is.True, $"seed {seed}: the whole beach can be walked to");
            Assert.That(layout.Mobs.Count, Is.EqualTo(lakeside.MobCount), $"seed {seed}");
            for (int i = 0; i < layout.Mobs.Count; i++)
            {
                AreaLayout.Mob mob = layout.Mobs[i];
                Assert.That(mob.Prefab, Is.SameAs(lakeside.MobPrefab));
                Assert.That(reachable.Contains(mob.Cell), Is.True, $"seed {seed}: mob at {mob.Cell} reachable from the spawn");
                Assert.That(Vector3Int.Distance(mob.Cell, layout.SpawnCell), Is.GreaterThanOrEqualTo(lakeside.MobDistanceFromSpawn), $"seed {seed}");
                for (int j = 0; j < i; j++)
                {
                    Assert.That(Vector3Int.Distance(mob.Cell, layout.Mobs[j].Cell), Is.GreaterThanOrEqualTo(lakeside.MobSpacing), $"seed {seed}");
                }
            }
        }
    }

    /// <summary>
    /// For each generator, generating twice from one seed gives the same cells, props, mobs and spawn; another seed
    /// gives another layout.
    /// </summary>
    [TestCase(ClearingGeneratorPath)]
    [TestCase(VillageForestGeneratorPath)]
    [TestCase(LakesideGeneratorPath)]
    public void TheSameSeed_GivesTheSameLayout_AndAnotherSeedAnotherOne(string generatorPath)
    {
        AreaGenerator target = AssetDatabase.LoadAssetAtPath<AreaGenerator>(generatorPath);
        AreaLayout first = target.Generate(7);
        AreaLayout again = target.Generate(7);
        AreaLayout other = target.Generate(8);

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
            Assert.That(again.Mobs[i].Prefab, Is.SameAs(first.Mobs[i].Prefab));
            Assert.That(again.Mobs[i].Cell, Is.EqualTo(first.Mobs[i].Cell));
        }

        Assert.That(again.SpawnCell, Is.EqualTo(first.SpawnCell));
        Assert.That(Cells(other, "Grass").SetEquals(Cells(first, "Grass")) && Cells(other, "Stone").SetEquals(Cells(first, "Stone")), Is.False);
    }

    /// <summary>
    /// For each generator, writing a layout into a scene whose grass layer is still a dual-grid layer sets every layer
    /// up as a plain data tilemap at its place under the grid (no module, render tilemap or Tilemap Renderer left),
    /// holding the layer's tile in exactly the layout's cells, with a navigation source of its terrain type whose
    /// priority is its index; the grid's terrain renderer draws the layers in that order with their materials and the
    /// generator's style, and the area's terrain fills the grass layer with its tile. The old props and mob spawn
    /// points are replaced and the start spawn point moves; the navigation grid then blocks exactly the footprint
    /// cells and the water, and every mob can walk to the spawn.
    /// </summary>
    [TestCase(ClearingGeneratorPath)]
    [TestCase(VillageForestGeneratorPath)]
    [TestCase(LakesideGeneratorPath)]
    public void TheWriter_SetsUpPlainLayersDrawnByTheRenderer_ReplacesPropsAndMobSpawns_AndTheGridBlocksTheFootprints(string generatorPath)
    {
        AreaGenerator target = AssetDatabase.LoadAssetAtPath<AreaGenerator>(generatorPath);
        Grid grid = CreateAreaWithDualGridGrass(target);
        AreaTerrain terrain = grid.gameObject.AddComponent<AreaTerrain>();
        terrain.Configure(new AreaTerrain.Layer(grid.transform.Find(target.TerrainLayers[0].TilemapName).GetComponent<Tilemap>(), null));
        Transform props = Track(new GameObject(PropsRootName)).transform;
        GameObject stale = new("Stale");
        stale.transform.SetParent(props, false);
        GameObject oldMob = Track(new GameObject("OldMobSpawn"));
        oldMob.AddComponent<MobSpawnPoint>();
        SpawnPoint start = Track(new GameObject("SpawnPoint_start")).AddComponent<SpawnPoint>();
        AreaLayout layout = target.Generate(3);

        Assert.That(AreaLayoutWriter.Write(grid, target, layout), Is.True);

        IReadOnlyList<AreaGenerator.TerrainLayer> layers = target.TerrainLayers;
        TerrainBlendRenderer renderer = grid.GetComponent<TerrainBlendRenderer>();
        Assert.That(renderer, Is.Not.Null, "The writer adds the terrain renderer.");
        Assert.That(renderer.Style, Is.SameAs(target.TerrainStyle));
        Assert.That(renderer.Layers.Count, Is.EqualTo(layers.Count));
        for (int i = 0; i < layers.Count; i++)
        {
            Transform child = grid.transform.Find(layers[i].TilemapName);
            Assert.That(child, Is.Not.Null, layers[i].TilemapName);
            Assert.That(child.GetSiblingIndex(), Is.EqualTo(i), "Layers are listed bottom first.");
            Assert.That(child.GetComponent<DualGridTilemapModule>(), Is.Null, $"{layers[i].TilemapName} has no module");
            Assert.That(child.GetComponent<TilemapRenderer>(), Is.Null, $"{layers[i].TilemapName} has no Tilemap Renderer");
            Assert.That(child.childCount, Is.Zero, $"{layers[i].TilemapName} has no render tilemap");
            Tilemap tilemap = child.GetComponent<Tilemap>();
            Assert.That(FilledCells(tilemap).SetEquals(layout.TerrainCells(i)), Is.True, $"{layers[i].TilemapName} cells");
            foreach (Vector3Int cell in layout.TerrainCells(i))
            {
                Assert.That(tilemap.GetTile(cell), Is.SameAs(layers[i].Tile), $"{layers[i].TilemapName} {cell}");
            }

            SerializedProperty tileAssets = new SerializedObject(tilemap).FindProperty("m_TileAssetArray");
            Assert.That(tileAssets.arraySize, Is.EqualTo(layout.TerrainCells(i).Count > 0 ? 1 : 0), $"{layers[i].TilemapName} keeps no record of the data tiles it held");

            NavigationTerrainSource2D source = child.GetComponent<NavigationTerrainSource2D>();
            Assert.That(source.TerrainType, Is.SameAs(layers[i].TerrainType));
            Assert.That(source.Priority, Is.EqualTo(i), "A layer's navigation priority is its index, its draw order.");
            Assert.That(renderer.Layers[i].Tilemap, Is.SameAs(tilemap), $"layer {i}");
            Assert.That(renderer.Layers[i].Material, Is.SameAs(layers[i].Material), $"layer {i}");
        }

        Assert.That(renderer.HasNavigationPrioritiesInStep(), Is.True);
        SerializedObject terrainData = new(terrain);
        Assert.That(terrainData.FindProperty("layers").GetArrayElementAtIndex(0).FindPropertyRelative("fillTile").objectReferenceValue, Is.SameAs(layers[0].Tile), "The area's terrain fills the grass layer with its tile.");

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
        HashSet<Vector3Int> water = CellsOrNone(layout, "Water");
        foreach (Vector3Int cell in Cells(layout, "Grass"))
        {
            Assert.That(navigation.IsCellWalkable(cell, profile), Is.EqualTo(!footprints.Contains(cell) && !water.Contains(cell)), $"cell {cell}");
        }

        foreach (AreaLayout.Mob mob in layout.Mobs)
        {
            Assert.That(navigation.AreCellsConnected(layout.SpawnCell, mob.Cell, profile), Is.True, $"mob at {mob.Cell}");
        }
    }

    /// <summary>
    /// On the layers the writer sets up, water painted over the road takes the cell out of the navigation grid for the
    /// default movement profile, since water is drawn on top and so decides the cell, while mud painted beside it
    /// stays walkable.
    /// </summary>
    [Test]
    public void TheWriter_LetsWaterBlockACell_AndMudBeWalked()
    {
        Grid grid = CreateAreaWithDualGridGrass(lakeside);
        Track(new GameObject(PropsRootName));
        Track(new GameObject("SpawnPoint_start")).AddComponent<SpawnPoint>();
        AreaLayout layout = lakeside.Generate(3);
        Assert.That(AreaLayoutWriter.Write(grid, lakeside, layout), Is.True);
        AreaGenerator.TerrainLayer water = Layer(lakeside, "Water");
        AreaGenerator.TerrainLayer mud = Layer(lakeside, "Mud");
        Vector3Int road = layout.SpawnCell;
        Vector3Int beside = road + new Vector3Int(0, 2, 0);
        grid.transform.Find(water.TilemapName).GetComponent<Tilemap>().SetTile(road, water.Tile);
        grid.transform.Find(mud.TilemapName).GetComponent<Tilemap>().SetTile(beside, mud.Tile);
        NavigationGrid2D navigation = grid.GetComponent<NavigationGrid2D>();
        navigation.ConfigureObstacleLayers(LayerMask.GetMask("Obstacles"));
        TerrainMovementProfile2D profile = AssetDatabase.LoadAssetAtPath<TerrainMovementProfile2D>(ProfilePath);

        navigation.BuildGrid();

        Assert.That(Cells(layout, "Stone").Contains(road), Is.True, "The spawn is on the road.");
        Assert.That(navigation.IsCellWalkable(road, profile), Is.False, "Water over the road blocks it.");
        Assert.That(navigation.IsCellWalkable(beside, profile), Is.True, "Mud can be walked.");
    }

    /// <summary>
    /// The writer refuses a generator of 17 layers with an error, changing nothing in the scene, and writes one of
    /// <see cref="TerrainControlMap.MaxLayers"/> (16): a tilemap per layer, drawn by a renderer that builds them all,
    /// each layer's cell holding its own bit alone. The layers reuse the Clearing's grass tile and terrain type under
    /// their own tilemap names, each drawn with its own material made for the test, in a style whose layer order lists
    /// those materials.
    /// </summary>
    [Test]
    public void TheWriter_WritesSixteenLayers_AndRefusesSeventeen()
    {
        Grid grid = Track(new GameObject("Grid")).AddComponent<Grid>();
        Track(new GameObject(PropsRootName));
        Track(new GameObject("SpawnPoint_start")).AddComponent<SpawnPoint>();
        AreaGenerator.TerrainLayer source = generator.TerrainLayers[0];
        TerrainMaterial[] order = new TerrainMaterial[TerrainControlMap.MaxLayers + 1];
        List<AreaGenerator.TerrainLayer> layers = new();
        for (int i = 0; i < order.Length; i++)
        {
            order[i] = TestTerrainStyle.PlainMaterial($"Material{i}", TerrainMaterial.CellMode.None, created);
            layers.Add(new AreaGenerator.TerrainLayer($"Layer{i}Tilemap", source.Tile, source.TerrainType, order[i]));
        }

        TerrainStyle style = TestTerrainStyle.Create(created, order);
        TestAreaGenerator seventeen = Track(TestAreaGenerator.Create(style, layers));
        LogAssert.Expect(LogType.Error, "TestGenerator: 17 terrain layers, but the blended terrain draws at most 16.");
        Assert.That(AreaLayoutWriter.Write(grid, seventeen, seventeen.Generate(1)), Is.False);
        Assert.That(grid.transform.childCount, Is.EqualTo(0), "Nothing is written.");
        Assert.That(grid.GetComponent<TerrainBlendRenderer>(), Is.Null);

        layers.RemoveAt(TerrainControlMap.MaxLayers);
        TestAreaGenerator sixteen = Track(TestAreaGenerator.Create(style, layers));
        Assert.That(AreaLayoutWriter.Write(grid, sixteen, sixteen.Generate(1)), Is.True);

        TerrainBlendRenderer renderer = grid.GetComponent<TerrainBlendRenderer>();
        Assert.That(renderer.Layers.Count, Is.EqualTo(16));
        renderer.RunScheduledRebuild();
        Assert.That(renderer.ControlMap, Is.Not.Null, "The renderer builds 16 layers.");
        Assert.That(renderer.ControlMap.LayerCount, Is.EqualTo(16));
        for (int i = 0; i < layers.Count; i++)
        {
            Assert.That(renderer.Layers[i].Tilemap.name, Is.EqualTo(layers[i].TilemapName));
            Assert.That(renderer.ControlMap.GetShownCell(new Vector3Int(i, 0, 0)), Is.EqualTo(1 << i), layers[i].TilemapName);
        }
    }

    /// <summary>
    /// The writer refuses, with an error and without changing the scene, a generator whose layers are not a
    /// subsequence of its style's layer order: two layers listed against that order (stone under gravel), and a
    /// layer drawn with a material the order does not list.
    /// </summary>
    [Test]
    public void TheWriter_RefusesLayersOutOfTheStylesLayerOrder()
    {
        Grid grid = Track(new GameObject("Grid")).AddComponent<Grid>();
        AreaGenerator.TerrainLayer grass = Layer(generator, "Grass");
        TestAreaGenerator reversed = Track(TestAreaGenerator.Create(generator.TerrainStyle, new[] { grass, Layer(generator, "Stone"), Layer(generator, "Gravel") }));
        LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(@"^TestGenerator: terrain layer 2 \(Gravel\) comes before Stone in TerrainStyle's layer order"));
        Assert.That(AreaLayoutWriter.Write(grid, reversed, reversed.Generate(1)), Is.False);

        TerrainMaterial unlisted = TestTerrainStyle.PlainMaterial("Unlisted", TerrainMaterial.CellMode.None, created);
        TestAreaGenerator unknown = Track(TestAreaGenerator.Create(generator.TerrainStyle, new[] { grass, new AreaGenerator.TerrainLayer("UnlistedTilemap", grass.Tile, grass.TerrainType, unlisted) }));
        LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(@"^TestGenerator: terrain layer 1 is drawn with Unlisted, which TerrainStyle's layer order does not list"));
        Assert.That(AreaLayoutWriter.Write(grid, unknown, unknown.Generate(1)), Is.False);

        Assert.That(grid.transform.childCount, Is.EqualTo(0), "Nothing is written.");
        Assert.That(grid.GetComponent<TerrainBlendRenderer>(), Is.Null);
    }

    /// <summary>
    /// An area that lists only some of the project's ground layers (grass, mud and water, none of the paths, dry grass or
    /// sand) still draws and navigates: its renderer builds those three layers at their indices in the area, and the
    /// default movement profile still finds the water cell blocked and the mud cell walkable at a higher cost than
    /// grass, since navigation reads each layer's terrain type, not its place in the project's order.
    /// </summary>
    [Test]
    public void AnAreaMissingSomeLayers_StillDrawsAndNavigates()
    {
        AreaGenerator.TerrainLayer[] layers = { Layer(lakeside, "Grass"), Layer(lakeside, "Mud"), Layer(lakeside, "Water") };
        TestAreaGenerator partial = Track(TestAreaGenerator.Create(lakeside.TerrainStyle, layers));
        Grid grid = CreateAreaWithDualGridGrass(partial);
        Track(new GameObject(PropsRootName));
        Track(new GameObject("SpawnPoint_start")).AddComponent<SpawnPoint>();
        Assert.That(AreaLayoutWriter.Write(grid, partial, partial.Generate(1)), Is.True);
        Tilemap grass = grid.transform.Find(layers[0].TilemapName).GetComponent<Tilemap>();
        for (int y = 0; y < 5; y++)
        {
            for (int x = 0; x < 9; x++)
            {
                grass.SetTile(new Vector3Int(x, y, 0), layers[0].Tile);
            }
        }

        Vector3Int mud = new(2, 2, 0);
        Vector3Int water = new(7, 2, 0);
        grid.transform.Find(layers[1].TilemapName).GetComponent<Tilemap>().SetTile(mud, layers[1].Tile);
        grid.transform.Find(layers[2].TilemapName).GetComponent<Tilemap>().SetTile(water, layers[2].Tile);

        TerrainBlendRenderer renderer = grid.GetComponent<TerrainBlendRenderer>();
        renderer.RunScheduledRebuild();
        renderer.FlushPendingChanges();
        Assert.That(renderer.ControlMap, Is.Not.Null, "The renderer builds.");
        Assert.That(renderer.ControlMap.LayerCount, Is.EqualTo(3));
        Assert.That(renderer.ControlMap.GetShownCell(mud), Is.EqualTo(0b011), "Mud over grass");
        Assert.That(renderer.ControlMap.GetShownCell(water), Is.EqualTo(0b101), "Water over grass");
        Assert.That(renderer.HasNavigationPrioritiesInStep(), Is.True);

        NavigationGrid2D navigation = grid.GetComponent<NavigationGrid2D>();
        navigation.ConfigureObstacleLayers(LayerMask.GetMask("Obstacles"));
        navigation.BuildGrid();
        TerrainMovementProfile2D profile = AssetDatabase.LoadAssetAtPath<TerrainMovementProfile2D>(ProfilePath);
        Assert.That(navigation.IsCellWalkable(water, profile), Is.False, "Water still blocks.");
        Assert.That(navigation.IsCellWalkable(mud, profile), Is.True, "Mud can be walked.");
        Assert.That(navigation.MovementCost(mud + Vector3Int.left, mud, profile), Is.GreaterThan(navigation.MovementCost(new Vector3Int(3, 2, 0), new Vector3Int(4, 2, 0), profile)), "Mud still costs more than grass.");
    }

    /// <summary>
    /// The terrain renderer the writer adds stays in step with it: after one layout is written over another, its
    /// control map holds exactly the new layout's cells on every layer, the cells the old layout filled and the new one
    /// leaves empty included (the writer clears each layer before painting it).
    /// </summary>
    [Test]
    public void TheWriter_KeepsItsTerrainRendererInStep_WhenALayoutIsWrittenOverAnother()
    {
        Grid grid = CreateAreaWithDualGridGrass(villageForest);
        Track(new GameObject(PropsRootName));
        Track(new GameObject("SpawnPoint_start")).AddComponent<SpawnPoint>();
        AreaLayout first = villageForest.Generate(3);
        Assert.That(AreaLayoutWriter.Write(grid, villageForest, first), Is.True);
        IReadOnlyList<AreaGenerator.TerrainLayer> layers = villageForest.TerrainLayers;
        TerrainBlendRenderer renderer = grid.GetComponent<TerrainBlendRenderer>();
        renderer.RunScheduledRebuild();
        Assert.That(renderer.ControlMap, Is.Not.Null, "The renderer the writer added builds.");
        AreaLayout second = villageForest.Generate(4);

        Assert.That(AreaLayoutWriter.Write(grid, villageForest, second), Is.True);
        renderer.FlushPendingChanges();

        int emptied = 0;
        for (int i = 0; i < layers.Count; i++)
        {
            HashSet<Vector3Int> cells = new(first.TerrainCells(i));
            cells.UnionWith(second.TerrainCells(i));
            int bit = 1 << i;
            foreach (Vector3Int cell in cells)
            {
                bool filled = second.TerrainCells(i).Contains(cell);
                Assert.That((renderer.ControlMap.GetShownCell(cell) & bit) != 0, Is.EqualTo(filled), $"{layers[i].TilemapName} {cell}");
                emptied += filled ? 0 : 1;
            }
        }

        Assert.That(emptied, Is.GreaterThan(0), "The second layout leaves some of the first one's cells empty.");
    }

    /// <summary>
    /// In a Village Forest area whose renderer also draws, above its own layers and out of the shared order, a painted
    /// water layer, a painted sand layer and an empty dry grass layer (none of them the generator's), writing puts every
    /// layer drawn in the style's layer order: grass, dirt, sand, gravel, plaza, stone, water. The painted layers it
    /// does not list keep their cells, their materials and their place in that order, the empty one is removed, each
    /// layer comes at its place among the grid's children with its index as navigation priority, and the renderer then
    /// builds. The test's own changes go through undo, so the revert in <see cref="TearDown"/> only passes through
    /// states whose priorities match the draw order.
    /// </summary>
    [Test]
    public void TheWriter_PutsLayersInTheSharedOrder_KeepingPaintedLayersItDoesNotList_AndRemovingEmptyOnes()
    {
        Grid grid = CreateAreaWithDualGridGrass(villageForest);
        Track(new GameObject(PropsRootName));
        Track(new GameObject("SpawnPoint_start")).AddComponent<SpawnPoint>();
        Assert.That(AreaLayoutWriter.Write(grid, villageForest, villageForest.Generate(3)), Is.True);
        TerrainBlendRenderer renderer = grid.GetComponent<TerrainBlendRenderer>();
        Undo.IncrementCurrentGroup();
        int added = Undo.GetCurrentGroup();
        Tilemap water = AddDrawnLayer(grid, Layer(lakeside, "Water"), true);
        Tilemap sand = AddDrawnLayer(grid, Layer(lakeside, "Sand"), true);
        Tilemap dryGrass = AddDrawnLayer(grid, Layer(lakeside, "DryGrass"), false);
        TerrainBlendRendererEditor.SyncNavigationPriorities(renderer);
        Undo.CollapseUndoOperations(added);
        Assert.That(renderer.Layers.Count, Is.EqualTo(8));

        Assert.That(AreaLayoutWriter.Write(grid, villageForest, villageForest.Generate(4)), Is.True);

        string[] expected = { "Grass", "Dirt", "Sand", "Gravel", "Plaza", "Stone", "Water" };
        Assert.That(renderer.Layers.Count, Is.EqualTo(expected.Length));
        for (int i = 0; i < expected.Length; i++)
        {
            Tilemap tilemap = renderer.Layers[i].Tilemap;
            Assert.That(tilemap.name, Is.EqualTo($"{expected[i]}Tilemap"), $"layer {i}");
            Assert.That(renderer.Layers[i].Material, Is.SameAs(ProjectMaterial(expected[i])), $"layer {i}");
            Assert.That(tilemap.transform.GetSiblingIndex(), Is.EqualTo(i), $"{tilemap.name} at its place");
            Assert.That(tilemap.GetComponent<NavigationTerrainSource2D>().Priority, Is.EqualTo(TerrainBlendRenderer.NavigationPriorityOf(i)), tilemap.name);
        }

        Assert.That(renderer.Layers[2].Tilemap, Is.SameAs(sand));
        Assert.That(renderer.Layers[6].Tilemap, Is.SameAs(water));
        Assert.That(sand.HasTile(Vector3Int.zero) && water.HasTile(Vector3Int.zero), Is.True, "The painted layers keep their cells.");
        Assert.That(dryGrass == null, Is.True, "The empty dry grass layer is removed.");
        Assert.That(renderer.HasNavigationPrioritiesInStep(), Is.True);
        renderer.RunScheduledRebuild();
        Assert.That(renderer.ControlMap, Is.Not.Null);
    }

    /// <summary>
    /// The project's generators (only read) list the ground layers they use, bottom first: each one's tilemap, its
    /// plain ground tile from Assets/Tiles/Ground, its terrain type and the project's material of the name given, drawn
    /// with the project's terrain style; their materials are a subsequence of the style's layer order, at most 16, so
    /// the writer and Convert Ground take them: each lists only what it lays, the Clearing dirt for its campsite, the
    /// Village Forest dirt and the plaza, the Lakeside dry grass among its seven.
    /// </summary>
    [TestCase(ClearingGeneratorPath, "Grass", "Dirt", "Gravel", "Stone")]
    [TestCase(VillageForestGeneratorPath, "Grass", "Dirt", "Gravel", "Plaza", "Stone")]
    [TestCase(LakesideGeneratorPath, "Grass", "DryGrass", "Sand", "Mud", "Gravel", "Stone", "Water")]
    public void TheGenerators_ListTheirOwnLayers_AsASubsequenceOfTheSharedOrder(string generatorPath, params string[] names)
    {
        AreaGenerator target = AssetDatabase.LoadAssetAtPath<AreaGenerator>(generatorPath);
        IReadOnlyList<AreaGenerator.TerrainLayer> layers = target.TerrainLayers;
        TerrainStyle style = target.TerrainStyle;

        Assert.That(style, Is.Not.Null.And.SameAs(AssetDatabase.LoadAssetAtPath<TerrainStyle>(TerrainStylePath)));
        Assert.That(layers.Count, Is.EqualTo(names.Length));
        Assert.That(layers.Count, Is.LessThanOrEqualTo(TerrainControlMap.MaxLayers));
        int previous = -1;
        for (int i = 0; i < names.Length; i++)
        {
            Assert.That(layers[i].TilemapName, Is.EqualTo($"{names[i]}Tilemap"));
            Assert.That(layers[i].Tile, Is.Not.Null.And.SameAs(AssetDatabase.LoadAssetAtPath<Tile>($"{TerrainTileWriter.TileFolder}/Tile_{names[i]}.asset")), names[i]);
            Assert.That(layers[i].Tile.GetType(), Is.EqualTo(typeof(Tile)), $"{names[i]} is a plain tile");
            Assert.That(layers[i].Material, Is.Not.Null.And.SameAs(ProjectMaterial(names[i])), names[i]);
            Assert.That(layers[i].TerrainType, Is.Not.Null.And.SameAs(ProjectTerrainType(names[i])), names[i]);
            int place = style.LayerOrderIndexOf(layers[i].Material);
            Assert.That(place, Is.GreaterThan(previous), $"{names[i]} comes after the layer below it in the shared order");
            previous = place;
        }

        Assert.That(AreaGroundSetup.Problem(target.name, style, layers), Is.Null);
    }

    /// <summary>
    /// The project's terrain style lists every ground material of Assets/Settings/Terrain/Materials once, in the shared
    /// draw order of Docs/TerrainTypes.md: grass, dry grass, dirt, sand, mud, gravel, plaza, stone, water.
    /// </summary>
    [Test]
    public void TheStyle_ListsEveryGroundMaterial_InTheSharedOrder()
    {
        TerrainStyle style = AssetDatabase.LoadAssetAtPath<TerrainStyle>(TerrainStylePath);
        string[] order = { "Grass", "DryGrass", "Dirt", "Sand", "Mud", "Gravel", "Plaza", "Stone", "Water" };

        Assert.That(style.LayerOrder.Count, Is.EqualTo(order.Length));
        for (int i = 0; i < order.Length; i++)
        {
            Assert.That(style.LayerOrder[i], Is.SameAs(ProjectMaterial(order[i])), $"place {i}");
        }

        foreach (string guid in AssetDatabase.FindAssets($"t:{nameof(TerrainMaterial)}", new[] { TerrainTileWriter.MaterialFolder }))
        {
            TerrainMaterial material = AssetDatabase.LoadAssetAtPath<TerrainMaterial>(AssetDatabase.GUIDToAssetPath(guid));
            Assert.That(style.LayerOrderIndexOf(material), Is.GreaterThanOrEqualTo(0), $"{material.name} has a place in the layer order");
        }
    }

    /// <summary>
    /// The default movement profile lets sand, dirt, dry grass and the plaza be walked like grass, mud at a higher cost
    /// and water not at all.
    /// </summary>
    [Test]
    public void TheDefaultProfile_WalksSandLikeGrass_MudAtAHigherCost_AndNotWater()
    {
        TerrainMovementProfile2D profile = AssetDatabase.LoadAssetAtPath<TerrainMovementProfile2D>(ProfilePath);
        profile.TryGetTraversal(ProjectTerrainType("Grass"), out bool grassWalkable, out int grassCost);
        profile.TryGetTraversal(ProjectTerrainType("Mud"), out bool mudWalkable, out int mudCost);
        profile.TryGetTraversal(ProjectTerrainType("Sand"), out bool sandWalkable, out int sandCost);
        profile.TryGetTraversal(ProjectTerrainType("Water"), out bool waterWalkable, out _);
        Assert.That(grassWalkable && sandWalkable && mudWalkable, Is.True);
        Assert.That(sandCost, Is.EqualTo(grassCost), "Sand costs what grass costs.");
        foreach (string name in new[] { "Dirt", "DryGrass", "Plaza" })
        {
            TerrainType2D type = ProjectTerrainType(name);
            Assert.That(type, Is.Not.Null, name);
            profile.TryGetTraversal(type, out bool walkable, out int cost);
            Assert.That(walkable, Is.True, name);
            Assert.That(cost, Is.EqualTo(grassCost), $"{name} costs what grass costs.");
        }

        Assert.That(mudCost, Is.GreaterThan(grassCost), "Paths go around mud when they can.");
        Assert.That(waterWalkable, Is.False, "Water cannot be walked.");
    }

    /// <summary>
    /// For every Clearing seed the campsite lies on a patch of dirt on the grass, off the paths and outside the spawn's
    /// clear disc, deeper than the edge band: one campfire stands on the dirt with every seat, and no plant stands on it
    /// or beside it.
    /// </summary>
    [Test]
    public void ClearingLayouts_LayACampsite_OnAPatchOfDirt()
    {
        for (int seed = 1; seed <= SeedCount; seed++)
        {
            AreaLayout layout = generator.Generate(seed);
            HashSet<Vector3Int> grass = Cells(layout, "Grass");
            HashSet<Vector3Int> dirt = Cells(layout, "Dirt");
            Assert.That(dirt.Count, Is.GreaterThan(10), $"seed {seed}: the campsite's dirt");
            Assert.That(grass.IsSupersetOf(dirt), Is.True, $"seed {seed}: dirt on grass");
            Assert.That(dirt.Overlaps(PathCells(layout)), Is.False, $"seed {seed}: dirt off the paths");
            foreach (Vector3Int cell in dirt)
            {
                Assert.That(Vector3Int.Distance(cell, layout.SpawnCell), Is.GreaterThan(generator.SpawnClearRadius), $"seed {seed}: dirt at {cell} near the spawn");
                foreach (Vector3Int near in CellsWithin(cell, generator.BorderDepth))
                {
                    Assert.That(grass.Contains(near), Is.True, $"seed {seed}: dirt at {cell} in the edge band");
                }
            }

            int fires = 0;
            int seats = 0;
            foreach (AreaLayout.Prop prop in layout.Props)
            {
                bool blocking = PropGridSnapping.TryGetFootprint(prop.Prefab, out Rect footprint);
                if (prop.Prefab == generator.CampfirePrefab)
                {
                    fires++;
                    Assert.That(dirt.IsSupersetOf(CoveredCells(prop, footprint)), Is.True, $"seed {seed}: the campfire on the dirt");
                }
                else if (dirt.Contains(prop.Cell) && ListContains(generator.CampsiteSeats, prop.Prefab))
                {
                    seats++;
                }
                else if (!blocking)
                {
                    foreach (Vector3Int near in CellsWithin(prop.Cell, 1))
                    {
                        Assert.That(dirt.Contains(near), Is.False, $"seed {seed}: {prop.Prefab.name} on or beside the campsite");
                    }
                }
            }

            Assert.That(fires, Is.EqualTo(1), $"seed {seed}: one campfire");
            Assert.That(seats, Is.EqualTo(generator.SeatCount), $"seed {seed}: seats around the fire");
        }
    }

    /// <summary>
    /// For every Village Forest seed the square is plaza on the grass around the fountain, within its radius of the
    /// fountain, and opens onto the street's stone; dirt lies beside the gravel trails, on the grass and off the
    /// paths, within its width of a trail and its reach of the street, and under no building or fence.
    /// </summary>
    [Test]
    public void VillageForestLayouts_PaveTheSquare_AndWearDirtAlongTheTrailsNearTheVillage()
    {
        for (int seed = 1; seed <= SeedCount; seed++)
        {
            AreaLayout layout = VillageForestLayout(seed).Layout;
            HashSet<Vector3Int> grass = Cells(layout, "Grass");
            HashSet<Vector3Int> plaza = Cells(layout, "Plaza");
            HashSet<Vector3Int> stone = Cells(layout, "Stone");
            HashSet<Vector3Int> gravel = Cells(layout, "Gravel");
            HashSet<Vector3Int> dirt = Cells(layout, "Dirt");
            Vector2 fountain = Vector2.zero;
            HashSet<Vector3Int> builtOn = new();
            foreach (AreaLayout.Prop prop in layout.Props)
            {
                PropGridSnapping.TryGetFootprint(prop.Prefab, out Rect footprint);
                if (prop.Prefab == villageForest.FountainPrefab)
                {
                    Vector2Int covered = PropGridSnapping.CellsCovered(footprint, Vector2.one);
                    fountain = new Vector2(prop.Cell.x + covered.x * 0.5f, prop.Cell.y + covered.y * 0.5f);
                }
                else if (prop.Prefab == villageForest.BuildingPrefab || prop.Prefab == villageForest.FencePrefab)
                {
                    builtOn.UnionWith(CoveredCells(prop, footprint));
                }
            }

            Assert.That(plaza.Count, Is.GreaterThan(20), $"seed {seed}: the square");
            Assert.That(grass.IsSupersetOf(plaza), Is.True, $"seed {seed}: the square on grass");
            foreach (Vector3Int cell in plaza)
            {
                Assert.That(Vector2.Distance(CellCentre(cell), fountain), Is.LessThanOrEqualTo(villageForest.SquareRadius + 1f), $"seed {seed}: plaza at {cell} around the fountain");
            }

            bool opensOntoStreet = false;
            foreach (Vector3Int cell in plaza)
            {
                foreach (Vector3Int side in Sides)
                {
                    opensOntoStreet |= stone.Contains(cell + side) && !plaza.Contains(cell + side);
                }
            }

            Assert.That(opensOntoStreet, Is.True, $"seed {seed}: the square opens onto the street");

            Assert.That(dirt, Is.Not.Empty, $"seed {seed}: dirt along the trails");
            Assert.That(grass.IsSupersetOf(dirt), Is.True, $"seed {seed}: dirt on grass");
            Assert.That(dirt.Overlaps(PathCells(layout)), Is.False, $"seed {seed}: dirt off the paths");
            Assert.That(dirt.Overlaps(builtOn), Is.False, $"seed {seed}: dirt under a building or a fence");
            float width = villageForest.DirtWidth + villageForest.DirtWobble;
            float reach = villageForest.DirtReach + width + 1f;
            foreach (Vector3Int cell in dirt)
            {
                Assert.That(NearestDistance(cell, gravel), Is.LessThanOrEqualTo(width), $"seed {seed}: dirt at {cell} beside a trail");
                Assert.That(NearestDistance(cell, stone), Is.LessThanOrEqualTo(reach), $"seed {seed}: dirt at {cell} near the village");
            }
        }
    }

    /// <summary>
    /// For each generator, the terrain it lays from its detail stream at the end (the Clearing's campsite, the Village
    /// Forest's dirt, the Lakeside's dry grass) leaves the rest of the layout where it was: generated by a copy of the
    /// generator with that step turned off, every other layer, every mob and the spawn are the same, and so is every
    /// prop, but for the Clearing's campfire and seats and the props they replaced, all of them within the campsite's
    /// radius plus three cells of the campfire.
    /// </summary>
    [TestCase(ClearingGeneratorPath)]
    [TestCase(VillageForestGeneratorPath)]
    [TestCase(LakesideGeneratorPath)]
    public void TheDetailStream_LeavesTheRestOfTheLayoutWhereItWas(string generatorPath)
    {
        AreaGenerator target = AssetDatabase.LoadAssetAtPath<AreaGenerator>(generatorPath);
        AreaGenerator without = Track(Object.Instantiate(target));
        SerializedObject settings = new(without);
        string added = "Dirt";
        if (target is ClearingGenerator)
        {
            settings.FindProperty("campfirePrefab").objectReferenceValue = null;
        }
        else if (target is VillageForestGenerator)
        {
            settings.FindProperty("dirtWidth").floatValue = 0f;
        }
        else
        {
            settings.FindProperty("dryGrassPatchCount").intValue = 0;
            added = "DryGrass";
        }

        settings.ApplyModifiedPropertiesWithoutUndo();
        for (int seed = 1; seed <= 5; seed++)
        {
            AreaLayout full = target.Generate(seed);
            AreaLayout plain = without.Generate(seed);
            int addedIndex = full.IndexOf(ProjectMaterial(added));
            Assert.That(plain.TerrainCells(addedIndex), Is.Empty, $"seed {seed}: the step is off");
            for (int layer = 0; layer < full.TerrainLayerCount; layer++)
            {
                if (layer != addedIndex)
                {
                    Assert.That(full.TerrainCells(layer).SetEquals(plain.TerrainCells(layer)), Is.True, $"seed {seed}: layer {layer}");
                }
            }

            Assert.That(full.SpawnCell, Is.EqualTo(plain.SpawnCell), $"seed {seed}");
            Assert.That(full.Mobs.Count, Is.EqualTo(plain.Mobs.Count), $"seed {seed}");
            for (int i = 0; i < full.Mobs.Count; i++)
            {
                Assert.That(full.Mobs[i].Cell, Is.EqualTo(plain.Mobs[i].Cell), $"seed {seed}: mob {i}");
            }

            HashSet<Vector3Int> campsite = new();
            foreach (AreaLayout.Prop prop in full.Props)
            {
                if (target is ClearingGenerator clearing && prop.Prefab == clearing.CampfirePrefab)
                {
                    foreach (Vector3Int cell in CellsWithin(prop.Cell, Mathf.CeilToInt(clearing.CampsiteRadius) + 3))
                    {
                        campsite.Add(cell);
                    }
                }
            }

            int kept = 0;
            foreach (AreaLayout.Prop prop in plain.Props)
            {
                if (HasProp(full, prop))
                {
                    kept++;
                    continue;
                }

                Assert.That(campsite.Contains(prop.Cell), Is.True, $"seed {seed}: {prop.Prefab.name} at {prop.Cell} moved off the campsite");
            }

            foreach (AreaLayout.Prop prop in full.Props)
            {
                if (!HasProp(plain, prop))
                {
                    Assert.That(campsite.Contains(prop.Cell), Is.True, $"seed {seed}: {prop.Prefab.name} at {prop.Cell} added off the campsite");
                }
            }

            Assert.That(kept, Is.GreaterThan(plain.Props.Count - 8), $"seed {seed}: nearly every prop stays");
        }
    }

    /// <summary>
    /// Whether <paramref name="layout"/> holds a prop of the same prefab, cell and offset as <paramref name="prop"/>.
    /// </summary>
    private static bool HasProp(AreaLayout layout, AreaLayout.Prop prop)
    {
        foreach (AreaLayout.Prop other in layout.Props)
        {
            if (other.Prefab == prop.Prefab && other.Cell == prop.Cell && other.Offset == prop.Offset)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether <paramref name="prefabs"/> holds <paramref name="prefab"/>.
    /// </summary>
    private static bool ListContains(IReadOnlyList<GameObject> prefabs, GameObject prefab)
    {
        foreach (GameObject listed in prefabs)
        {
            if (listed == prefab)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The distance between the centres of <paramref name="cell"/> and the nearest of <paramref name="cells"/>.
    /// </summary>
    private static float NearestDistance(Vector3Int cell, HashSet<Vector3Int> cells)
    {
        float nearest = float.MaxValue;
        foreach (Vector3Int other in cells)
        {
            nearest = Mathf.Min(nearest, Vector3Int.Distance(cell, other));
        }

        return nearest;
    }

    /// <summary>
    /// Builds a grid of one-unit cells carrying a navigation grid, with a grass layer named like
    /// <paramref name="target"/>'s first layer that is still a dual-grid layer (data and render tilemaps of a rule tile
    /// made for the test, two cells painted far from any layout, a navigation source) but none of its path layers.
    /// </summary>
    private Grid CreateAreaWithDualGridGrass(AreaGenerator target)
    {
        Grid grid = Track(new GameObject("Grid")).AddComponent<Grid>();
        grid.gameObject.AddComponent<NavigationGrid2D>();
        DualGridRuleTile ruleTile = Track(ScriptableObject.CreateInstance<DualGridRuleTile>());
        Track(ruleTile.RefreshDataTile());
        DualGridTilemapModule grass = DualGridTilemapModuleEditor.CreateNewDualGridTilemap(grid);
        grass.name = target.TerrainLayers[0].TilemapName;
        grass.RenderTile = ruleTile;
        grass.SetDataTiles(new[] { new Vector3Int(500, 500, 0), new Vector3Int(501, 500, 0) });
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
    /// The Village Forest generation for <paramref name="seed"/>, generated once per fixture.
    /// </summary>
    private VillageForestGenerator.Generation VillageForestLayout(int seed)
    {
        if (!villageForestLayouts.TryGetValue(seed, out VillageForestGenerator.Generation generation))
        {
            generation = villageForest.GenerateWithRegions(seed);
            villageForestLayouts[seed] = generation;
        }

        return generation;
    }

    /// <summary>
    /// Every gravel, plaza and stone cell of <paramref name="layout"/>.
    /// </summary>
    private static HashSet<Vector3Int> PathCells(AreaLayout layout)
    {
        HashSet<Vector3Int> cells = new(Cells(layout, "Gravel"));
        cells.UnionWith(CellsOrNone(layout, "Plaza"));
        cells.UnionWith(Cells(layout, "Stone"));
        return cells;
    }

    /// <summary>
    /// <paramref name="cell"/> and every cell within <paramref name="steps"/> steps of it (8 neighbours).
    /// </summary>
    private static List<Vector3Int> CellsWithin(Vector3Int cell, int steps)
    {
        List<Vector3Int> cells = new();
        for (int dy = -steps; dy <= steps; dy++)
        {
            for (int dx = -steps; dx <= steps; dx++)
            {
                cells.Add(cell + new Vector3Int(dx, dy, 0));
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
    /// The cells of <paramref name="layout"/>'s layer drawn with the project's material named
    /// <paramref name="material"/>, which the layout must list.
    /// </summary>
    private static HashSet<Vector3Int> Cells(AreaLayout layout, string material)
    {
        int index = layout.IndexOf(ProjectMaterial(material));
        Assert.That(index, Is.GreaterThanOrEqualTo(0), $"The layout lists {material}.");
        return layout.TerrainCells(index);
    }

    /// <summary>
    /// The cells of <paramref name="layout"/>'s layer drawn with the project's material named
    /// <paramref name="material"/>, or none when the layout does not list it.
    /// </summary>
    private static HashSet<Vector3Int> CellsOrNone(AreaLayout layout, string material)
    {
        int index = layout.IndexOf(ProjectMaterial(material));
        return index >= 0 ? layout.TerrainCells(index) : new HashSet<Vector3Int>();
    }

    /// <summary>
    /// <paramref name="target"/>'s terrain layer drawn with the project's material named <paramref name="material"/>.
    /// </summary>
    private static AreaGenerator.TerrainLayer Layer(AreaGenerator target, string material)
    {
        foreach (AreaGenerator.TerrainLayer layer in target.TerrainLayers)
        {
            if (layer.Material == ProjectMaterial(material))
            {
                return layer;
            }
        }

        Assert.Fail($"{target.name} lists no {material} layer.");
        return null;
    }

    /// <summary>
    /// The project's ground material named <paramref name="name"/>.
    /// </summary>
    private static TerrainMaterial ProjectMaterial(string name)
    {
        return AssetDatabase.LoadAssetAtPath<TerrainMaterial>($"{TerrainTileWriter.MaterialFolder}/{name}.asset");
    }

    /// <summary>
    /// The project's terrain type of the ground material named <paramref name="name"/>.
    /// </summary>
    private static TerrainType2D ProjectTerrainType(string name)
    {
        return AssetDatabase.LoadAssetAtPath<TerrainType2D>($"Assets/Settings/AI/Terrain_{name}.asset");
    }

    /// <summary>
    /// Adds, with undo, a plain tilemap for <paramref name="layer"/> under <paramref name="grid"/> with its navigation
    /// source, holding the layer's tile in cell (0, 0) when <paramref name="painted"/>, and has the grid's renderer
    /// draw it above its other layers with the layer's material.
    /// </summary>
    private static Tilemap AddDrawnLayer(Grid grid, AreaGenerator.TerrainLayer layer, bool painted)
    {
        Tilemap tilemap = new GameObject(layer.TilemapName, typeof(Tilemap)).GetComponent<Tilemap>();
        Undo.RegisterCreatedObjectUndo(tilemap.gameObject, $"Create {layer.TilemapName}");
        tilemap.transform.SetParent(grid.transform, false);
        Undo.AddComponent<NavigationTerrainSource2D>(tilemap.gameObject).Configure(layer.TerrainType);
        if (painted)
        {
            tilemap.SetTile(Vector3Int.zero, layer.Tile);
        }

        SerializedObject rendererData = new(grid.GetComponent<TerrainBlendRenderer>());
        SerializedProperty listed = rendererData.FindProperty("layers");
        listed.arraySize++;
        SerializedProperty added = listed.GetArrayElementAtIndex(listed.arraySize - 1);
        added.FindPropertyRelative("tilemap").objectReferenceValue = tilemap;
        added.FindPropertyRelative("material").objectReferenceValue = layer.Material;
        rendererData.ApplyModifiedProperties();
        return tilemap;
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
