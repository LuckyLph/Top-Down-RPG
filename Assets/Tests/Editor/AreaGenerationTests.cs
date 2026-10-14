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
/// off the paths with a beach and a marsh, dry grass on the open grass, small shallow ponds on plain grass apart from
/// the lake with no third look at their corners, a dock from the trail's end across the beach into the lake that adds
/// no third look under a rounded corner, and keeps props off the water, the beach and the mud with the beach and every
/// mob within the spawn's reach; the Snowfield generator lays a frozen lake with a hole of open water that meets only
/// ice, a trail that stops short of the ice, and a campsite on dirt away from the ice and the paths, with props off the
/// ice and every mob and the ice within the spawn's reach; the terrain each generator lays from its detail stream leaves the rest of its layout
/// where it was, and the Lakeside's ponds leave its dry grass where it was and its dock everything else; the same
/// seed gives the same layout; the project's style lists every ground material in the shared draw order, the
/// project's generators list only the layers they lay (plain ground tiles and the project's materials) as a
/// subsequence of it, and the default movement profile blocks water and makes mud and the shallows cost more; and
/// <see cref="AreaLayoutWriter"/> writes a layout into a scene as plain ground layers drawn by
/// a <see cref="TerrainBlendRenderer"/> in the shared order, with navigation priorities from that order, keeping painted
/// layers the generator does not list and removing empty ones, a renderer that follows a layout written over another,
/// a navigation grid that blocks exactly the props' cells, wades the Lakeside's ponds at the shallows' cost and walks
/// its dock over the lake at grass's cost, the water beside it blocked, walks the Snowfield's snow at its cost and its
/// ice like grass with its hole blocked, an area
/// of only some layers that still draws and navigates, and writes up to 16 layers but refuses 17 and refuses layers out of the shared order.
/// </summary>
public class AreaGenerationTests
{
    private const string ClearingGeneratorPath = "Assets/Settings/Generation/Generator_Clearing.asset";
    private const string VillageForestGeneratorPath = "Assets/Settings/Generation/Generator_VillageForest.asset";
    private const string LakesideGeneratorPath = "Assets/Settings/Generation/Generator_Lakeside.asset";
    private const string SnowfieldGeneratorPath = "Assets/Settings/Generation/Generator_Snowfield.asset";
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
    private SnowfieldGenerator snowfield;
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
        snowfield = AssetDatabase.LoadAssetAtPath<SnowfieldGenerator>(SnowfieldGeneratorPath);
        Assert.That(snowfield, Is.Not.Null, $"{SnowfieldGeneratorPath} should exist.");
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
    /// on open grass, off the water and the beach and not on or beside mud, a path or the map's edge, in pieces of four
    /// cells or more; the lake lies off every path and the shallow ponds and at least the road margin from the road, the
    /// beach and the marsh lie beside it and off it, and the gravel trail reaches the beach; the spawn is on the road
    /// with no footprint or water within its clear radius.
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
            Assert.That(layout.TerrainLayerCount, Is.EqualTo(11));
            Assert.That(water.Overlaps(Cells(layout, "Shallows")), Is.False, $"seed {seed}: the ponds lie off the lake");
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
                Assert.That(Reachable(cell, dryGrass.Contains).Count, Is.GreaterThanOrEqualTo(4), $"seed {seed}: a scrap of dry grass at {cell}");
                foreach (Vector3Int near in CellsWithin(cell, 1))
                {
                    Assert.That(paths.Contains(near) || mud.Contains(near) || !grass.Contains(near), Is.False, $"seed {seed}: dry grass at {cell} beside mud, a path or the map's edge");
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
    /// For every Lakeside seed the shallows lie in separate small ponds on the open grass, apart from the lake: at least
    /// one pond and at most the configured count, each of four cells or more, two cells or more from the others; every
    /// pond cell and its 8 neighbours are plain grass or pond (no water, dry grass, beach, mud, path or map's edge), with
    /// no blocking prop, walk-through prop or mob on them and none in the spawn's clear disc; the lake is the same as
    /// with no ponds. Wherever the ponds meet another look at a corner, no third look shows under a rounded corner
    /// (TerrainTypes.md, Phase 3's Corners note).
    /// </summary>
    [Test]
    public void LakesideLayouts_LayShallowPonds_ApartFromTheLake_OnPlainGrass()
    {
        AreaGenerator withoutPonds = Track(Object.Instantiate(lakeside));
        SerializedObject settings = new(withoutPonds);
        settings.FindProperty("shallowPondCount").intValue = 0;
        settings.ApplyModifiedPropertiesWithoutUndo();
        for (int seed = 1; seed <= SeedCount; seed++)
        {
            AreaLayout layout = lakeside.Generate(seed);
            HashSet<Vector3Int> shallows = Cells(layout, "Shallows");
            HashSet<Vector3Int> grass = Cells(layout, "Grass");
            HashSet<Vector3Int> other = new(Cells(layout, "Water"));
            foreach (string name in new[] { "DryGrass", "Sand", "Mud", "Gravel", "Stone", "Planks" })
            {
                other.UnionWith(Cells(layout, name));
            }

            HashSet<Vector3Int> props = Footprints(layout);
            foreach (AreaLayout.Prop prop in layout.Props)
            {
                props.Add(prop.Cell);
            }

            foreach (AreaLayout.Mob mob in layout.Mobs)
            {
                props.Add(mob.Cell);
            }

            AreaLayout plain = withoutPonds.Generate(seed);
            Assert.That(Cells(plain, "Shallows"), Is.Empty, $"seed {seed}");
            Assert.That(Cells(layout, "Water").SetEquals(Cells(plain, "Water")), Is.True, $"seed {seed}: the lake is as it was");

            List<HashSet<Vector3Int>> ponds = new();
            HashSet<Vector3Int> seen = new();
            foreach (Vector3Int cell in shallows)
            {
                if (seen.Add(cell))
                {
                    HashSet<Vector3Int> pond = Reachable(cell, shallows.Contains);
                    seen.UnionWith(pond);
                    ponds.Add(pond);
                    Assert.That(pond.Count, Is.GreaterThanOrEqualTo(4), $"seed {seed}: a scrap of shallows at {cell}");
                }

                Vector3Int offset = cell - layout.SpawnCell;
                Assert.That(offset.x * offset.x + offset.y * offset.y, Is.GreaterThan(lakeside.SpawnClearRadius * lakeside.SpawnClearRadius), $"seed {seed}: a pond at {cell} near the spawn");
                foreach (Vector3Int near in CellsWithin(cell, 1))
                {
                    Assert.That(grass.Contains(near) && !other.Contains(near), Is.True, $"seed {seed}: a pond at {cell} beside {near}, not plain grass");
                    Assert.That(props.Contains(near), Is.False, $"seed {seed}: a prop or mob on or beside the pond at {cell}");
                }

                HashSet<Vector3Int> own = Reachable(cell, shallows.Contains);
                foreach (Vector3Int near in CellsWithin(cell, 2))
                {
                    Assert.That(shallows.Contains(near) && !own.Contains(near), Is.False, $"seed {seed}: two ponds near {cell}");
                }
            }

            Assert.That(ponds.Count, Is.InRange(1, lakeside.ShallowPondCount), $"seed {seed}: ponds");
            Assert.That(CornerProblems(layout, "Shallows"), Is.Empty, $"seed {seed}");
        }
    }

    /// <summary>
    /// For every Lakeside seed a dock runs from the trail's end across the beach into the lake: a block of planks the
    /// dock width wide, whose bottom row covers the trail's last cell, whose cells are sand, the trail's gravel or water,
    /// with at least the fewest rows of the dock reach over open water up to its far end; the cells beside its rows on the
    /// beach are sand, water or gravel, and every cell within the dock clearance of its rows over the water is water or
    /// sand, so it lies off the ponds, the mud and the grass; no prop stands on or beside it; and it adds no third look
    /// under a rounded corner: at every corner of its cells, whatever shows under a rounded corner there is painted in
    /// that cell or showed there without the dock too.
    /// </summary>
    [Test]
    public void LakesideLayouts_LayADock_FromTheTrailsEndIntoTheLake()
    {
        AreaGenerator withoutDock = Track(Object.Instantiate(lakeside));
        SerializedObject settings = new(withoutDock);
        settings.FindProperty("dockReach").vector2IntValue = Vector2Int.zero;
        settings.ApplyModifiedPropertiesWithoutUndo();
        for (int seed = 1; seed <= SeedCount; seed++)
        {
            AreaLayout layout = lakeside.Generate(seed);
            HashSet<Vector3Int> dock = Cells(layout, "Planks");
            HashSet<Vector3Int> water = Cells(layout, "Water");
            HashSet<Vector3Int> sand = Cells(layout, "Sand");
            HashSet<Vector3Int> gravel = Cells(layout, "Gravel");
            Assert.That(dock, Is.Not.Empty, $"seed {seed}: a dock");
            Assert.That(layout.Warnings, Is.Empty, $"seed {seed}");
            BoundsInt bounds = BoundsOf(dock);
            Assert.That(bounds.size.x, Is.EqualTo(lakeside.DockWidth), $"seed {seed}: the dock's width");
            Assert.That(dock.Count, Is.EqualTo(bounds.size.x * bounds.size.y), $"seed {seed}: a block of planks");
            bool coversTrail = false;
            int rowsInWater = 0;
            for (int y = bounds.yMin; y < bounds.yMax; y++)
            {
                bool inWater = true;
                for (int x = bounds.xMin; x < bounds.xMax; x++)
                {
                    Vector3Int cell = new(x, y, 0);
                    Assert.That(sand.Contains(cell) || gravel.Contains(cell) || water.Contains(cell), Is.True, $"seed {seed}: the dock at {cell} lies on the beach, the trail or the lake");
                    inWater &= water.Contains(cell);
                    coversTrail |= y == bounds.yMin && gravel.Contains(cell);
                }

                rowsInWater = inWater ? rowsInWater + 1 : 0;
                if (!inWater)
                {
                    for (int x = bounds.xMin - 1; x <= bounds.xMax; x++)
                    {
                        Vector3Int cell = new(x, y, 0);
                        Assert.That(dock.Contains(cell) || sand.Contains(cell) || water.Contains(cell) || gravel.Contains(cell), Is.True, $"seed {seed}: beside the dock's foot at {cell}");
                    }
                }
            }

            Assert.That(coversTrail, Is.True, $"seed {seed}: the dock starts on the trail's end");
            Assert.That(rowsInWater, Is.InRange(lakeside.DockReach.x, lakeside.DockReach.y), $"seed {seed}: rows over the water up to the far end");
            int shore = bounds.yMax - rowsInWater;
            int clearance = lakeside.DockClearance;
            for (int y = shore; y < bounds.yMax + clearance; y++)
            {
                for (int x = bounds.xMin - clearance; x < bounds.xMax + clearance; x++)
                {
                    Vector3Int cell = new(x, y, 0);
                    Assert.That(water.Contains(cell) || sand.Contains(cell), Is.True, $"seed {seed}: {cell} near the dock's lake end is water or beach");
                }
            }

            HashSet<Vector3Int> props = Footprints(layout);
            foreach (AreaLayout.Prop prop in layout.Props)
            {
                props.Add(prop.Cell);
            }

            foreach (Vector3Int cell in dock)
            {
                foreach (Vector3Int near in CellsWithin(cell, 1))
                {
                    Assert.That(props.Contains(near), Is.False, $"seed {seed}: a prop on or beside the dock at {near}");
                }
            }

            HashSet<string> withDock = new(BridgeCornerProblems(layout, dock));
            HashSet<string> plain = new(BridgeCornerProblems(withoutDock.Generate(seed), dock));
            withDock.ExceptWith(plain);
            Assert.That(withDock, Is.Empty, $"seed {seed}");
        }
    }

    /// <summary>
    /// The Lakeside's dock, laid last, leaves everything laid before it where it was: with the dock turned off (a dock
    /// reach of 0), every other layer, the spawn and every mob are the same, and so is every prop but the walk-through
    /// props the dock removed, all of them on, beside or up to three rows below it.
    /// </summary>
    [Test]
    public void TheLakesidesDock_LeavesTheRestOfItsLayoutWhereItWas()
    {
        AreaGenerator without = Track(Object.Instantiate(lakeside));
        SerializedObject settings = new(without);
        settings.FindProperty("dockReach").vector2IntValue = Vector2Int.zero;
        settings.ApplyModifiedPropertiesWithoutUndo();
        for (int seed = 1; seed <= 5; seed++)
        {
            AreaLayout full = lakeside.Generate(seed);
            AreaLayout plain = without.Generate(seed);
            int planksIndex = full.IndexOf(ProjectMaterial("Planks"));
            Assert.That(plain.TerrainCells(planksIndex), Is.Empty, $"seed {seed}: no dock");
            HashSet<Vector3Int> near = new();
            foreach (Vector3Int cell in full.TerrainCells(planksIndex))
            {
                near.UnionWith(CellsWithin(cell, 1));
                near.UnionWith(CellsWithin(cell + new Vector3Int(0, -2, 0), 1));
                near.UnionWith(CellsWithin(cell + new Vector3Int(0, -3, 0), 1));
            }

            for (int layer = 0; layer < full.TerrainLayerCount; layer++)
            {
                if (layer != planksIndex)
                {
                    Assert.That(full.TerrainCells(layer).SetEquals(plain.TerrainCells(layer)), Is.True, $"seed {seed}: layer {layer}");
                }
            }

            Assert.That(full.SpawnCell, Is.EqualTo(plain.SpawnCell));
            Assert.That(full.Mobs.Count, Is.EqualTo(plain.Mobs.Count));
            for (int i = 0; i < full.Mobs.Count; i++)
            {
                Assert.That(full.Mobs[i].Cell, Is.EqualTo(plain.Mobs[i].Cell), $"seed {seed}: mob {i}");
            }

            foreach (AreaLayout.Prop prop in full.Props)
            {
                Assert.That(HasProp(plain, prop), Is.True, $"seed {seed}: {prop.Prefab.name} at {prop.Cell} added");
            }

            foreach (AreaLayout.Prop prop in plain.Props)
            {
                if (!HasProp(full, prop))
                {
                    Assert.That(PropGridSnapping.TryGetFootprint(prop.Prefab, out _), Is.False, $"seed {seed}: blocking {prop.Prefab.name} removed");
                    Assert.That(near.Contains(prop.Cell), Is.True, $"seed {seed}: {prop.Prefab.name} at {prop.Cell} removed away from the dock");
                }
            }
        }
    }

    /// <summary>
    /// The corners of <paramref name="layout"/> around <paramref name="cells"/> where a look shows under a layer's
    /// rounded corner in a cell it is not painted in, counting each layer's cells as the renderer does with bridges: a
    /// cell counts for a layer when that layer or a layer above it is painted there, but a bridge layer (the planks)
    /// only for itself. At each corner of the cells, a layer filling one cell of the four is rounded off there; under it
    /// shows the highest layer below whose cells there differ from it and that fills that cell. That is a problem when
    /// that layer is not painted in the cell and fills just one more cell, beside it, so its straight edge runs on where
    /// the upper layer turns away (Phase 3's Corners note).
    /// </summary>
    private static List<string> BridgeCornerProblems(AreaLayout layout, HashSet<Vector3Int> cells)
    {
        int layers = layout.TerrainLayerCount;
        HashSet<Vector3Int> corners = new();
        foreach (Vector3Int cell in cells)
        {
            corners.Add(cell);
            corners.Add(cell + Vector3Int.right);
            corners.Add(cell + Vector3Int.up);
            corners.Add(cell + new Vector3Int(1, 1, 0));
        }

        List<string> problems = new();
        foreach (Vector3Int corner in corners)
        {
            Vector3Int[] around = { corner + new Vector3Int(-1, -1, 0), corner + Vector3Int.down, corner + Vector3Int.left, corner };
            int[] masks = new int[layers];
            for (int layer = 0; layer < layers; layer++)
            {
                for (int k = 0; k < 4; k++)
                {
                    if (Counts(layout, layer, around[k]))
                    {
                        masks[layer] |= 1 << k;
                    }
                }
            }

            for (int upper = 1; upper < layers; upper++)
            {
                int c = SingleCell(masks[upper]);
                if (c < 0)
                {
                    continue;
                }

                for (int lower = upper - 1; lower >= 0; lower--)
                {
                    if (masks[lower] == masks[upper] || (masks[lower] & (1 << c)) == 0)
                    {
                        continue;
                    }

                    int others = masks[lower] & ~(1 << c);
                    int other = SingleCell(others);
                    if (!layout.TerrainCells(lower).Contains(around[c]) && other >= 0 && (c ^ other) != 3)
                    {
                        problems.Add($"{corner}: layer {lower} under layer {upper}'s corner");
                    }

                    break;
                }
            }
        }

        return problems;
    }

    /// <summary>
    /// Whether <paramref name="cell"/> counts for <paramref name="layer"/>'s shape as the renderer counts it: the layer
    /// or one above it that is not a bridge is painted there.
    /// </summary>
    private static bool Counts(AreaLayout layout, int layer, Vector3Int cell)
    {
        for (int above = layer; above < layout.TerrainLayerCount; above++)
        {
            bool bridge = above != layer && layout.TerrainLayers[above].Material.Bridge;
            if (!bridge && layout.TerrainCells(above).Contains(cell))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The index of the only cell of a corner's four set in <paramref name="mask"/>, or -1 when not exactly one is.
    /// </summary>
    private static int SingleCell(int mask)
    {
        for (int k = 0; k < 4; k++)
        {
            if (mask == 1 << k)
            {
                return k;
            }
        }

        return -1;
    }

    /// <summary>
    /// The bounds of <paramref name="cells"/>.
    /// </summary>
    private static BoundsInt BoundsOf(HashSet<Vector3Int> cells)
    {
        Vector3Int min = new(int.MaxValue, int.MaxValue, 0);
        Vector3Int max = new(int.MinValue, int.MinValue, 0);
        foreach (Vector3Int cell in cells)
        {
            min = Vector3Int.Min(min, cell);
            max = Vector3Int.Max(max, cell);
        }

        return new BoundsInt(min.x, min.y, 0, max.x - min.x + 1, max.y - min.y + 1, 1);
    }

    /// <summary>
    /// On the layers the writer sets up for the Lakeside, the planks reach navigation through their tilemap's terrain
    /// source, at their draw order's priority, the last, above the water's: every dock cell, over the lake too, can be
    /// walked and is joined to the spawn, at grass's cost where no blocked cell lies beside it (the middle of the dock)
    /// and at grass's cost plus the profile's edge cost along its sides over the water, while the water beside the dock
    /// stays blocked.
    /// </summary>
    [Test]
    public void TheWriter_LetsTheDockBeWalkedAtGrasssCost_AndKeepsTheWaterBesideItBlocked()
    {
        Grid grid = CreateAreaWithDualGridGrass(lakeside);
        Track(new GameObject(PropsRootName));
        Track(new GameObject("SpawnPoint_start")).AddComponent<SpawnPoint>();
        AreaLayout layout = lakeside.Generate(1);
        Assert.That(AreaLayoutWriter.Write(grid, lakeside, layout), Is.True);
        NavigationGrid2D navigation = grid.GetComponent<NavigationGrid2D>();
        navigation.ConfigureObstacleLayers(LayerMask.GetMask("Obstacles"));
        TerrainMovementProfile2D profile = AssetDatabase.LoadAssetAtPath<TerrainMovementProfile2D>(ProfilePath);
        NavigationTerrainSource2D source = grid.transform.Find(Layer(lakeside, "Planks").TilemapName).GetComponent<NavigationTerrainSource2D>();
        HashSet<Vector3Int> dock = Cells(layout, "Planks");
        HashSet<Vector3Int> water = Cells(layout, "Water");

        navigation.BuildGrid();

        Assert.That(source.TerrainType, Is.SameAs(ProjectTerrainType("Planks")));
        Assert.That(source.Priority, Is.EqualTo(layout.TerrainLayerCount - 1));
        Assert.That(source.Priority, Is.GreaterThan(layout.IndexOf(ProjectMaterial("Water"))));
        profile.TryGetTraversal(ProjectTerrainType("Grass"), out _, out int grassCost);
        int middle = 0;
        int sides = 0;
        foreach (Vector3Int cell in dock)
        {
            Assert.That(navigation.IsCellWalkable(cell, profile), Is.True, $"the dock at {cell}");
            Assert.That(navigation.AreCellsConnected(layout.SpawnCell, cell, profile), Is.True, $"the dock at {cell} reachable");
            if (!water.Contains(cell))
            {
                continue;
            }

            bool besideWater = false;
            foreach (Vector3Int near in CellsWithin(cell, 1))
            {
                besideWater |= water.Contains(near) && !dock.Contains(near);
            }

            Vector3Int from = dock.Contains(cell + Vector3Int.down) ? cell + Vector3Int.down : cell + Vector3Int.up;
            int expected = besideWater ? grassCost + profile.EdgeCost : grassCost;
            Assert.That(navigation.MovementCost(from, cell, profile), Is.EqualTo(expected), $"stepping onto the dock at {cell} over the water");
            middle += besideWater ? 0 : 1;
            sides += besideWater ? 1 : 0;
        }

        Assert.That(middle, Is.GreaterThan(0), "Plank cells over the water away from its edge.");
        Assert.That(sides, Is.GreaterThan(0), "Plank cells along the dock's sides over the water.");
        foreach (Vector3Int cell in water)
        {
            if (!dock.Contains(cell))
            {
                Assert.That(navigation.IsCellWalkable(cell, profile), Is.False, $"water at {cell}");
            }
        }
    }

    /// <summary>
    /// For every Snowfield seed, with no warnings: every layer has cells and lies on the snow; the frozen lake is one
    /// 4-connected piece of ice, no cell of it within the road margin of the road; the holes of open water lie in the
    /// ice with ice all round each of their cells for the hole margin, in one to the hole count pieces of four cells or
    /// more; the trail leaves the road and stops short of the lake, no trail cell on or beside the ice; and no third look
    /// shows under a rounded corner around the ice or the holes (TerrainTypes.md, Phase 3's Corners note).
    /// </summary>
    [Test]
    public void SnowfieldLayouts_LayAFrozenLakeWithAHole_AndATrailThatStopsShortOfIt()
    {
        for (int seed = 1; seed <= SeedCount; seed++)
        {
            AreaLayout layout = snowfield.Generate(seed);
            Assert.That(layout.Warnings, Is.Empty, $"seed {seed}: {string.Join("; ", layout.Warnings)}");
            HashSet<Vector3Int> snow = Cells(layout, "Snow");
            for (int layer = 0; layer < layout.TerrainLayerCount; layer++)
            {
                Assert.That(layout.TerrainCells(layer), Is.Not.Empty, $"seed {seed}: layer {layer}");
                Assert.That(layout.TerrainCells(layer).IsSubsetOf(snow), Is.True, $"seed {seed}: layer {layer} lies on the snow");
            }

            HashSet<Vector3Int> ice = Cells(layout, "Ice");
            HashSet<Vector3Int> water = Cells(layout, "Water");
            HashSet<Vector3Int> stone = Cells(layout, "Stone");
            HashSet<Vector3Int> gravel = Cells(layout, "Gravel");
            Vector3Int anyIce = default;
            foreach (Vector3Int cell in ice)
            {
                anyIce = cell;
                foreach (Vector3Int near in CellsWithin(cell, snowfield.RoadMargin))
                {
                    Assert.That(stone.Contains(near), Is.False, $"seed {seed}: ice at {cell} within the road margin");
                }
            }

            Assert.That(Reachable(anyIce, ice.Contains).Count, Is.EqualTo(ice.Count), $"seed {seed}: the lake is one piece");
            HashSet<Vector3Int> seen = new();
            int holes = 0;
            foreach (Vector3Int cell in water)
            {
                foreach (Vector3Int near in CellsWithin(cell, snowfield.HoleMargin))
                {
                    Assert.That(ice.Contains(near), Is.True, $"seed {seed}: a hole at {cell} without ice at {near}");
                }

                if (seen.Add(cell))
                {
                    HashSet<Vector3Int> hole = Reachable(cell, water.Contains);
                    seen.UnionWith(hole);
                    holes++;
                    Assert.That(hole.Count, Is.GreaterThanOrEqualTo(4), $"seed {seed}: a scrap of water at {cell}");
                }
            }

            Assert.That(holes, Is.InRange(1, snowfield.HoleCount), $"seed {seed}: holes");
            bool leavesTheRoad = false;
            foreach (Vector3Int cell in gravel)
            {
                foreach (Vector3Int near in CellsWithin(cell, 1))
                {
                    Assert.That(ice.Contains(near), Is.False, $"seed {seed}: the trail at {cell} touches the ice");
                }

                foreach (Vector3Int side in Sides)
                {
                    leavesTheRoad |= stone.Contains(cell + side);
                }
            }

            Assert.That(leavesTheRoad, Is.True, $"seed {seed}: the trail leaves the road");
            Assert.That(CornerProblems(layout, "Ice", "Water"), Is.Empty, $"seed {seed}");
        }
    }

    /// <summary>
    /// For every Snowfield seed a campsite stands on a patch of dirt: one campfire, on the dirt, and every seat on the
    /// dirt; no dirt cell on or beside the ice or a path; no walk-through prop on or beside the dirt; and no third look
    /// under a rounded corner around it.
    /// </summary>
    [Test]
    public void SnowfieldLayouts_LayACampsite_OnDirtAwayFromTheIceAndThePaths()
    {
        for (int seed = 1; seed <= SeedCount; seed++)
        {
            AreaLayout layout = snowfield.Generate(seed);
            HashSet<Vector3Int> dirt = Cells(layout, "Dirt");
            HashSet<Vector3Int> ice = Cells(layout, "Ice");
            HashSet<Vector3Int> paths = PathCells(layout);
            foreach (Vector3Int cell in dirt)
            {
                foreach (Vector3Int near in CellsWithin(cell, 1))
                {
                    Assert.That(ice.Contains(near) || paths.Contains(near), Is.False, $"seed {seed}: dirt at {cell} beside the ice or a path");
                }
            }

            int campfires = 0;
            foreach (AreaLayout.Prop prop in layout.Props)
            {
                if (prop.Prefab == snowfield.CampfirePrefab)
                {
                    campfires++;
                    Assert.That(dirt.Contains(prop.Cell), Is.True, $"seed {seed}: the campfire stands on the dirt");
                }
                else if (ListContains(snowfield.CampsiteSeats, prop.Prefab) && dirt.Contains(prop.Cell))
                {
                    continue;
                }
                else if (!PropGridSnapping.TryGetFootprint(prop.Prefab, out _))
                {
                    foreach (Vector3Int near in CellsWithin(prop.Cell, 1))
                    {
                        Assert.That(dirt.Contains(near), Is.False, $"seed {seed}: {prop.Prefab.name} at {prop.Cell} on or beside the dirt");
                    }
                }
            }

            Assert.That(campfires, Is.EqualTo(1), $"seed {seed}: campfires");
            Assert.That(CornerProblems(layout, "Dirt"), Is.Empty, $"seed {seed}");
        }
    }

    /// <summary>
    /// For every Snowfield seed blocking footprints cover snow off the ice and the snow beside it, apart; walk-through
    /// props stand on snow off the ice and outside every footprint; the spawn is on the road with no footprint within its
    /// clear radius; every mob stands on snow off the ice, far enough from the spawn and from each other; and the spawn
    /// can walk to every mob and every ice cell, around the footprints and the holes.
    /// </summary>
    [Test]
    public void SnowfieldLayouts_KeepPropsOffTheIce_AndLetTheSpawnReachEveryMobAndTheLake()
    {
        for (int seed = 1; seed <= SeedCount; seed++)
        {
            AreaLayout layout = snowfield.Generate(seed);
            HashSet<Vector3Int> snow = Cells(layout, "Snow");
            HashSet<Vector3Int> ice = Cells(layout, "Ice");
            HashSet<Vector3Int> water = Cells(layout, "Water");
            HashSet<Vector3Int> footprints = new();
            foreach (AreaLayout.Prop prop in layout.Props)
            {
                if (!PropGridSnapping.TryGetFootprint(prop.Prefab, out Rect footprint))
                {
                    Assert.That(snow.Contains(prop.Cell) && !ice.Contains(prop.Cell), Is.True, $"seed {seed}: {prop.Prefab.name} at {prop.Cell}");
                    continue;
                }

                foreach (Vector3Int cell in CoveredCells(prop, footprint))
                {
                    Assert.That(footprints.Add(cell), Is.True, $"seed {seed}: footprints overlap at {cell}");
                    Assert.That(snow.Contains(cell), Is.True, $"seed {seed}: {prop.Prefab.name} at {cell} off the snow");
                    foreach (Vector3Int near in CellsWithin(cell, 1))
                    {
                        Assert.That(ice.Contains(near), Is.False, $"seed {seed}: {prop.Prefab.name} at {cell} on or beside the ice");
                    }
                }
            }

            foreach (AreaLayout.Prop prop in layout.Props)
            {
                if (!PropGridSnapping.TryGetFootprint(prop.Prefab, out _))
                {
                    Assert.That(footprints.Contains(prop.Cell), Is.False, $"seed {seed}: {prop.Prefab.name} under a footprint");
                }
            }

            Assert.That(Cells(layout, "Stone").Contains(layout.SpawnCell), Is.True, $"seed {seed}: the spawn is on the road");
            foreach (Vector3Int cell in footprints)
            {
                Assert.That(Vector3Int.Distance(cell, layout.SpawnCell), Is.GreaterThan(snowfield.SpawnClearRadius), $"seed {seed}: a footprint at {cell} near the spawn");
            }

            HashSet<Vector3Int> reachable = Reachable(layout.SpawnCell, cell => snow.Contains(cell) && !footprints.Contains(cell) && !water.Contains(cell));
            Assert.That(layout.Mobs.Count, Is.EqualTo(snowfield.MobCount), $"seed {seed}: mobs");
            for (int i = 0; i < layout.Mobs.Count; i++)
            {
                AreaLayout.Mob mob = layout.Mobs[i];
                Assert.That(ice.Contains(mob.Cell), Is.False, $"seed {seed}: a mob on the ice");
                Assert.That(reachable.Contains(mob.Cell), Is.True, $"seed {seed}: mob at {mob.Cell} unreachable");
                Assert.That(Vector3Int.Distance(mob.Cell, layout.SpawnCell), Is.GreaterThanOrEqualTo(snowfield.MobDistanceFromSpawn), $"seed {seed}");
                for (int j = i + 1; j < layout.Mobs.Count; j++)
                {
                    Assert.That(Vector3Int.Distance(mob.Cell, layout.Mobs[j].Cell), Is.GreaterThanOrEqualTo(snowfield.MobSpacing), $"seed {seed}");
                }
            }

            foreach (Vector3Int cell in ice)
            {
                Assert.That(water.Contains(cell) || reachable.Contains(cell), Is.True, $"seed {seed}: ice at {cell} unreachable");
            }
        }
    }

    /// <summary>
    /// On the layers the writer sets up for the Snowfield, snow, ice and water reach navigation through their tilemaps'
    /// terrain sources at their draw order's priorities, and the default movement profile's rules: stepping into a snow
    /// cell with no blocked cell around it costs snow's 15 and into such an ice cell grass's 10, every ice cell is joined
    /// to the spawn, and every cell of the holes is blocked.
    /// </summary>
    [Test]
    public void TheWriter_WalksTheSnowfieldsSnowAtItsCost_AndItsIceLikeGrass_AndBlocksTheHoles()
    {
        Grid grid = CreateAreaWithDualGridGrass(snowfield);
        Track(new GameObject(PropsRootName));
        Track(new GameObject("SpawnPoint_start")).AddComponent<SpawnPoint>();
        AreaLayout layout = snowfield.Generate(1);
        Assert.That(AreaLayoutWriter.Write(grid, snowfield, layout), Is.True);
        NavigationGrid2D navigation = grid.GetComponent<NavigationGrid2D>();
        navigation.ConfigureObstacleLayers(LayerMask.GetMask("Obstacles"));
        TerrainMovementProfile2D profile = AssetDatabase.LoadAssetAtPath<TerrainMovementProfile2D>(ProfilePath);
        navigation.BuildGrid();

        foreach (string name in new[] { "Snow", "Ice", "Water" })
        {
            NavigationTerrainSource2D source = grid.transform.Find(Layer(snowfield, name).TilemapName).GetComponent<NavigationTerrainSource2D>();
            Assert.That(source.TerrainType, Is.SameAs(ProjectTerrainType(name)), name);
            Assert.That(source.Priority, Is.EqualTo(layout.IndexOf(ProjectMaterial(name))), name);
        }

        HashSet<Vector3Int> ice = Cells(layout, "Ice");
        HashSet<Vector3Int> water = Cells(layout, "Water");
        HashSet<Vector3Int> blocked = new(water);
        blocked.UnionWith(Footprints(layout));
        HashSet<Vector3Int> others = new(ice);
        foreach (string name in new[] { "Dirt", "Gravel", "Stone" })
        {
            others.UnionWith(Cells(layout, name));
        }

        int snowSteps = 0;
        int iceSteps = 0;
        foreach (Vector3Int cell in Cells(layout, "Snow"))
        {
            bool open = true;
            foreach (Vector3Int near in CellsWithin(cell, 2))
            {
                open &= !blocked.Contains(near) && Cells(layout, "Snow").Contains(near);
            }

            if (!open)
            {
                continue;
            }

            if (ice.Contains(cell) && !water.Contains(cell))
            {
                Assert.That(navigation.MovementCost(cell + Vector3Int.right, cell, profile), Is.EqualTo(10), $"stepping onto the ice at {cell}");
                iceSteps++;
            }
            else if (!others.Contains(cell))
            {
                Assert.That(navigation.MovementCost(cell + Vector3Int.right, cell, profile), Is.EqualTo(15), $"stepping into the snow at {cell}");
                snowSteps++;
            }
        }

        Assert.That(snowSteps, Is.GreaterThan(100));
        Assert.That(iceSteps, Is.GreaterThan(20));
        foreach (Vector3Int cell in water)
        {
            Assert.That(navigation.IsCellWalkable(cell, profile), Is.False, $"the hole at {cell}");
        }

        foreach (Vector3Int cell in ice)
        {
            if (!water.Contains(cell))
            {
                Assert.That(navigation.AreCellsConnected(layout.SpawnCell, cell, profile), Is.True, $"ice at {cell} reachable");
            }
        }
    }

    /// <summary>
    /// The corners of <paramref name="layout"/> around the cells of <paramref name="materials"/> where a third look shows
    /// under a rounded corner: at each cell corner with such a cell among its four cells, a layer filling one of the four
    /// (with the layers above it, as the renderer counts them) is rounded off there, and the next layer down fills that
    /// cell's quarter of the corner. That is a problem when the next layer fills just one more cell, beside it, so its
    /// straight edge runs on where the upper layer turns away.
    /// </summary>
    private static List<string> CornerProblems(AreaLayout layout, params string[] materials)
    {
        Dictionary<Vector3Int, int> top = new();
        HashSet<Vector3Int> corners = new();
        for (int layer = 0; layer < layout.TerrainLayerCount; layer++)
        {
            foreach (Vector3Int cell in layout.TerrainCells(layer))
            {
                top[cell] = top.TryGetValue(cell, out int below) ? Mathf.Max(below, layer) : layer;
            }
        }

        foreach (string material in materials)
        {
            foreach (Vector3Int cell in layout.TerrainCells(layout.IndexOf(ProjectMaterial(material))))
            {
                corners.Add(cell);
                corners.Add(cell + Vector3Int.right);
                corners.Add(cell + Vector3Int.up);
                corners.Add(cell + new Vector3Int(1, 1, 0));
            }
        }

        List<string> problems = new();
        foreach (Vector3Int corner in corners)
        {
            Vector3Int[] cells = { corner + new Vector3Int(-1, -1, 0), corner + Vector3Int.down, corner + Vector3Int.left, corner };
            int[] tops = new int[4];
            for (int k = 0; k < 4; k++)
            {
                tops[k] = top.TryGetValue(cells[k], out int layer) ? layer : -1;
            }

            for (int c = 0; c < 4; c++)
            {
                int next = -1;
                int filled = 0;
                int other = -1;
                for (int k = 0; k < 4; k++)
                {
                    if (k != c)
                    {
                        next = Mathf.Max(next, tops[k]);
                    }
                }

                for (int k = 0; k < 4; k++)
                {
                    if (k != c && tops[k] >= 0 && tops[k] == next)
                    {
                        filled++;
                        other = k;
                    }
                }

                if (tops[c] > next && next >= 0 && filled == 1 && (c ^ other) != 3)
                {
                    problems.Add($"{corner}: layer {next} under layer {tops[c]}'s corner");
                }
            }
        }

        return problems;
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
    [TestCase(SnowfieldGeneratorPath)]
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
        Assert.That(other.TerrainCells(0).SetEquals(first.TerrainCells(0)) && Cells(other, "Stone").SetEquals(Cells(first, "Stone")), Is.False);
    }

    /// <summary>
    /// For each generator, writing a layout into a scene whose ground layer (grass, or the Snowfield's snow) is still a
    /// dual-grid layer sets every layer
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
    [TestCase(SnowfieldGeneratorPath)]
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
        Assert.That(terrainData.FindProperty("layers").GetArrayElementAtIndex(0).FindPropertyRelative("fillTile").objectReferenceValue, Is.SameAs(layers[0].Tile), "The area's terrain fills the ground layer with its tile.");

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
        HashSet<Vector3Int> water = new(CellsOrNone(layout, "Water"));
        water.ExceptWith(CellsOrNone(layout, "Planks"));
        foreach (Vector3Int cell in layout.TerrainCells(0))
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
    /// On the layers the writer sets up for the Lakeside, the shallows reach navigation through their tilemap's terrain
    /// source, at their draw order's priority, and the default movement profile's rule: every pond cell can be walked at
    /// the shallows' cost of 20 (no blocked cell lies beside a pond, so no edge cost), joined to the spawn, while every
    /// water cell is blocked.
    /// </summary>
    [Test]
    public void TheWriter_LetsThePondsBeWadedAtTheShallowsCost_AndKeepsTheWaterBlocked()
    {
        Grid grid = CreateAreaWithDualGridGrass(lakeside);
        Track(new GameObject(PropsRootName));
        Track(new GameObject("SpawnPoint_start")).AddComponent<SpawnPoint>();
        AreaLayout layout = lakeside.Generate(1);
        Assert.That(AreaLayoutWriter.Write(grid, lakeside, layout), Is.True);
        NavigationGrid2D navigation = grid.GetComponent<NavigationGrid2D>();
        navigation.ConfigureObstacleLayers(LayerMask.GetMask("Obstacles"));
        TerrainMovementProfile2D profile = AssetDatabase.LoadAssetAtPath<TerrainMovementProfile2D>(ProfilePath);
        AreaGenerator.TerrainLayer shallowsLayer = Layer(lakeside, "Shallows");
        NavigationTerrainSource2D source = grid.transform.Find(shallowsLayer.TilemapName).GetComponent<NavigationTerrainSource2D>();
        HashSet<Vector3Int> shallows = Cells(layout, "Shallows");

        navigation.BuildGrid();

        Assert.That(source.TerrainType, Is.SameAs(ProjectTerrainType("Shallows")));
        Assert.That(source.Priority, Is.EqualTo(layout.IndexOf(ProjectMaterial("Shallows"))));
        profile.TryGetTraversal(ProjectTerrainType("Shallows"), out bool walkable, out int cost);
        Assert.That(walkable, Is.True);
        Assert.That(cost, Is.EqualTo(20));
        Assert.That(shallows, Is.Not.Empty);
        HashSet<Vector3Int> dock = Cells(layout, "Planks");
        foreach (Vector3Int cell in Cells(layout, "Water"))
        {
            Assert.That(navigation.IsCellWalkable(cell, profile), Is.EqualTo(dock.Contains(cell)), $"water at {cell}, walkable only under the dock");
        }

        foreach (Vector3Int cell in shallows)
        {
            Assert.That(navigation.IsCellWalkable(cell, profile), Is.True, $"shallows at {cell}");
            Assert.That(navigation.AreCellsConnected(layout.SpawnCell, cell, profile), Is.True, $"shallows at {cell} reachable");
            foreach (Vector3Int side in Sides)
            {
                Assert.That(navigation.MovementCost(cell + side, cell, profile), Is.EqualTo(cost), $"stepping into {cell}");
            }
        }
    }

    /// <summary>
    /// On the layers the writer sets up for the Village Forest and the Lakeside, the meadow and tall grass reach
    /// navigation through their tilemaps' terrain sources, at their draw order's priorities, and are walked like grass:
    /// every cell of them not under a footprint is walkable and joined to the spawn, and stepping into one with no
    /// blocked cell around it costs grass's 10.
    /// </summary>
    [TestCase(VillageForestGeneratorPath)]
    [TestCase(LakesideGeneratorPath)]
    public void TheWriter_LetsTheMeadowsAndTallGrassBeWalkedAtGrasssCost(string generatorPath)
    {
        AreaGenerator target = AssetDatabase.LoadAssetAtPath<AreaGenerator>(generatorPath);
        Grid grid = CreateAreaWithDualGridGrass(target);
        Track(new GameObject(PropsRootName));
        Track(new GameObject("SpawnPoint_start")).AddComponent<SpawnPoint>();
        AreaLayout layout = target.Generate(1);
        Assert.That(AreaLayoutWriter.Write(grid, target, layout), Is.True);
        NavigationGrid2D navigation = grid.GetComponent<NavigationGrid2D>();
        navigation.ConfigureObstacleLayers(LayerMask.GetMask("Obstacles"));
        TerrainMovementProfile2D profile = AssetDatabase.LoadAssetAtPath<TerrainMovementProfile2D>(ProfilePath);
        profile.TryGetTraversal(ProjectTerrainType("Grass"), out _, out int grassCost);
        HashSet<Vector3Int> footprints = Footprints(layout);

        navigation.BuildGrid();

        foreach (string name in new[] { "Meadow", "TallGrass" })
        {
            NavigationTerrainSource2D source = grid.transform.Find(Layer(target, name).TilemapName).GetComponent<NavigationTerrainSource2D>();
            Assert.That(source.TerrainType, Is.SameAs(ProjectTerrainType(name)), name);
            Assert.That(source.Priority, Is.EqualTo(layout.IndexOf(ProjectMaterial(name))), name);
            int open = 0;
            foreach (Vector3Int cell in Cells(layout, name))
            {
                if (footprints.Contains(cell))
                {
                    continue;
                }

                Assert.That(navigation.IsCellWalkable(cell, profile), Is.True, $"{name} at {cell}");
                Assert.That(navigation.AreCellsConnected(layout.SpawnCell, cell, profile), Is.True, $"{name} at {cell} reachable");
                bool clear = true;
                foreach (Vector3Int near in CellsWithin(cell, 2))
                {
                    clear &= !footprints.Contains(near);
                }

                if (!clear)
                {
                    continue;
                }

                open++;
                foreach (Vector3Int side in Sides)
                {
                    Assert.That(navigation.MovementCost(cell + side, cell, profile), Is.EqualTo(grassCost), $"stepping into {name} at {cell}");
                }
            }

            Assert.That(open, Is.GreaterThan(0), $"{name} cells with nothing blocked around them");
        }
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
    /// layer drawn in the style's layer order: grass, meadow, tall grass, dirt, sand, gravel, plaza, stone, water. The
    /// painted layers it does not list keep their cells, their materials and their place in that order, the empty one is
    /// removed, each layer comes at its place among the grid's children with its index as navigation priority, and the
    /// renderer then builds. The test's own changes go through undo, so the revert in <see cref="TearDown"/> only passes through
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
        Assert.That(renderer.Layers.Count, Is.EqualTo(10));

        Assert.That(AreaLayoutWriter.Write(grid, villageForest, villageForest.Generate(4)), Is.True);

        string[] expected = { "Grass", "Meadow", "TallGrass", "Dirt", "Sand", "Gravel", "Plaza", "Stone", "Water" };
        Assert.That(renderer.Layers.Count, Is.EqualTo(expected.Length));
        for (int i = 0; i < expected.Length; i++)
        {
            Tilemap tilemap = renderer.Layers[i].Tilemap;
            Assert.That(tilemap.name, Is.EqualTo($"{expected[i]}Tilemap"), $"layer {i}");
            Assert.That(renderer.Layers[i].Material, Is.SameAs(ProjectMaterial(expected[i])), $"layer {i}");
            Assert.That(tilemap.transform.GetSiblingIndex(), Is.EqualTo(i), $"{tilemap.name} at its place");
            Assert.That(tilemap.GetComponent<NavigationTerrainSource2D>().Priority, Is.EqualTo(TerrainBlendRenderer.NavigationPriorityOf(i)), tilemap.name);
        }

        Assert.That(renderer.Layers[4].Tilemap, Is.SameAs(sand));
        Assert.That(renderer.Layers[8].Tilemap, Is.SameAs(water));
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
    /// Village Forest the meadow, tall grass, dirt and the plaza, the Lakeside dry grass, the meadow and tall grass among
    /// its eleven.
    /// </summary>
    [TestCase(ClearingGeneratorPath, "Grass", "Dirt", "Gravel", "Stone")]
    [TestCase(VillageForestGeneratorPath, "Grass", "Meadow", "TallGrass", "Dirt", "Gravel", "Plaza", "Stone")]
    [TestCase(LakesideGeneratorPath, "Grass", "DryGrass", "Meadow", "TallGrass", "Sand", "Mud", "Gravel", "Stone", "Shallows", "Water", "Planks")]
    [TestCase(SnowfieldGeneratorPath, "Snow", "Dirt", "Ice", "Gravel", "Stone", "Water")]
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
    /// draw order of Docs/TerrainTypes.md: grass, dry grass, meadow, tall grass, snow, dirt, sand, mud, ice, gravel, plaza,
    /// stone, shallows, water, planks.
    /// </summary>
    [Test]
    public void TheStyle_ListsEveryGroundMaterial_InTheSharedOrder()
    {
        TerrainStyle style = AssetDatabase.LoadAssetAtPath<TerrainStyle>(TerrainStylePath);
        string[] order = { "Grass", "DryGrass", "Meadow", "TallGrass", "Snow", "Dirt", "Sand", "Mud", "Ice", "Gravel", "Plaza", "Stone", "Shallows", "Water", "Planks" };

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
    /// The default movement profile lets sand, dirt, dry grass, the meadow, tall grass, ice, the plaza and the planks be
    /// walked like grass, snow at one and a half times grass's cost, mud and the shallows at twice it, and water not at
    /// all, so the cheapest step stays grass's.
    /// </summary>
    [Test]
    public void TheDefaultProfile_WalksSandLikeGrass_MudAtAHigherCost_AndNotWater()
    {
        TerrainMovementProfile2D profile = AssetDatabase.LoadAssetAtPath<TerrainMovementProfile2D>(ProfilePath);
        profile.TryGetTraversal(ProjectTerrainType("Grass"), out bool grassWalkable, out int grassCost);
        profile.TryGetTraversal(ProjectTerrainType("Shallows"), out bool shallowsWalkable, out int shallowsCost);
        Assert.That(shallowsWalkable, Is.True, "The shallows can be waded.");
        Assert.That(shallowsCost, Is.EqualTo(2 * grassCost), "Wading costs twice as much as walking.");
        Assert.That(profile.GetMinimumTraversalCost(), Is.EqualTo(grassCost));
        profile.TryGetTraversal(ProjectTerrainType("Mud"), out bool mudWalkable, out int mudCost);
        profile.TryGetTraversal(ProjectTerrainType("Sand"), out bool sandWalkable, out int sandCost);
        profile.TryGetTraversal(ProjectTerrainType("Water"), out bool waterWalkable, out _);
        Assert.That(grassWalkable && sandWalkable && mudWalkable, Is.True);
        Assert.That(sandCost, Is.EqualTo(grassCost), "Sand costs what grass costs.");
        foreach (string name in new[] { "Dirt", "DryGrass", "Meadow", "TallGrass", "Ice", "Plaza", "Planks" })
        {
            TerrainType2D type = ProjectTerrainType(name);
            Assert.That(type, Is.Not.Null, name);
            profile.TryGetTraversal(type, out bool walkable, out int cost);
            Assert.That(walkable, Is.True, name);
            Assert.That(cost, Is.EqualTo(grassCost), $"{name} costs what grass costs.");
        }

        profile.TryGetTraversal(ProjectTerrainType("Snow"), out bool snowWalkable, out int snowCost);
        Assert.That(snowWalkable, Is.True, "Snow can be walked.");
        Assert.That(snowCost, Is.EqualTo(3 * grassCost / 2), "Snow costs half as much again as grass.");
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
    /// Forest's dirt, tall grass and meadows, the Lakeside's dry grass, shallow ponds, tall grass and meadows, the
    /// Snowfield's holes and campsite) leaves the rest of the layout where it was: generated by a copy of the generator
    /// with those steps turned off, every other layer, every mob and the spawn are the same, and so is every prop, but
    /// for the campfires and seats and the props they replaced, all of them within the campsite's radius plus three
    /// cells of the campfire,
    /// the plants the ponds removed, on, beside or just below them, and the plants the tall grass removed, on it.
    /// </summary>
    [TestCase(ClearingGeneratorPath)]
    [TestCase(VillageForestGeneratorPath)]
    [TestCase(LakesideGeneratorPath)]
    [TestCase(SnowfieldGeneratorPath)]
    public void TheDetailStream_LeavesTheRestOfTheLayoutWhereItWas(string generatorPath)
    {
        AreaGenerator target = AssetDatabase.LoadAssetAtPath<AreaGenerator>(generatorPath);
        AreaGenerator without = Track(Object.Instantiate(target));
        SerializedObject settings = new(without);
        string[] added = { "Dirt" };
        if (target is ClearingGenerator)
        {
            settings.FindProperty("campfirePrefab").objectReferenceValue = null;
        }
        else if (target is VillageForestGenerator)
        {
            settings.FindProperty("dirtWidth").floatValue = 0f;
            settings.FindProperty("tallGrassCount").intValue = 0;
            settings.FindProperty("meadowCount").intValue = 0;
            added = new[] { "Dirt", "TallGrass", "Meadow" };
        }
        else if (target is SnowfieldGenerator)
        {
            settings.FindProperty("holeCount").intValue = 0;
            settings.FindProperty("campfirePrefab").objectReferenceValue = null;
            added = new[] { "Water", "Dirt" };
        }
        else
        {
            settings.FindProperty("dryGrassPatchCount").intValue = 0;
            settings.FindProperty("shallowPondCount").intValue = 0;
            settings.FindProperty("tallGrassCount").intValue = 0;
            settings.FindProperty("meadowCount").intValue = 0;
            added = new[] { "DryGrass", "TallGrass", "Meadow" };
        }

        settings.ApplyModifiedPropertiesWithoutUndo();
        for (int seed = 1; seed <= 5; seed++)
        {
            AreaLayout full = target.Generate(seed);
            AreaLayout plain = without.Generate(seed);
            List<int> addedIndices = new();
            foreach (string name in added)
            {
                addedIndices.Add(full.IndexOf(ProjectMaterial(name)));
                Assert.That(plain.TerrainCells(addedIndices[addedIndices.Count - 1]), Is.Empty, $"seed {seed}: the {name} step is off");
            }

            int shallowsIndex = full.IndexOf(ProjectMaterial("Shallows"));
            for (int layer = 0; layer < full.TerrainLayerCount; layer++)
            {
                if (!addedIndices.Contains(layer) && layer != shallowsIndex)
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
                else if (target is SnowfieldGenerator snowy && prop.Prefab == snowy.CampfirePrefab)
                {
                    foreach (Vector3Int cell in CellsWithin(prop.Cell, Mathf.CeilToInt(snowy.CampsiteRadius) + 3))
                    {
                        campsite.Add(cell);
                    }
                }
            }

            int tallGrassIndex = full.IndexOf(ProjectMaterial("TallGrass"));
            if (tallGrassIndex >= 0)
            {
                campsite.UnionWith(full.TerrainCells(tallGrassIndex));
            }

            if (shallowsIndex >= 0)
            {
                foreach (Vector3Int cell in full.TerrainCells(shallowsIndex))
                {
                    campsite.UnionWith(CellsWithin(cell, 1));
                    campsite.UnionWith(CellsWithin(cell + new Vector3Int(0, -2, 0), 1));
                    campsite.UnionWith(CellsWithin(cell + new Vector3Int(0, -3, 0), 1));
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

            Assert.That(kept, Is.GreaterThan(plain.Props.Count - 24), $"seed {seed}: nearly every prop stays");
        }
    }

    /// <summary>
    /// For every Village Forest seed the meadows lie in the clearing and the tall grass across the forest's edge: both
    /// on grass, in pieces of four cells or more, two cells or more inside the map's edge, a cell or more from every path
    /// and every dirt cell and from each other, the tall grass off every building and fence and within its reach of the
    /// other side of the forest's edge (a clearing or village cell for a forest cell, a forest cell for the others), with
    /// no walk-through prop on it; and around both, no third look shows under a rounded corner (TerrainTypes.md, Phase
    /// 3's Corners note).
    /// </summary>
    [Test]
    public void VillageForestLayouts_LayMeadowsInTheClearing_AndTallGrassAcrossTheForestsEdge()
    {
        int reach = villageForest.TallGrassReach;
        for (int seed = 1; seed <= SeedCount; seed++)
        {
            VillageForestGenerator.Generation generation = VillageForestLayout(seed);
            AreaLayout layout = generation.Layout;
            HashSet<Vector3Int> grass = Cells(layout, "Grass");
            HashSet<Vector3Int> meadow = Cells(layout, "Meadow");
            HashSet<Vector3Int> tall = Cells(layout, "TallGrass");
            HashSet<Vector3Int> paths = PathCells(layout);
            HashSet<Vector3Int> dirt = Cells(layout, "Dirt");
            HashSet<Vector3Int> builtOn = new();
            HashSet<Vector3Int> plants = new();
            foreach (AreaLayout.Prop prop in layout.Props)
            {
                if (!PropGridSnapping.TryGetFootprint(prop.Prefab, out Rect footprint))
                {
                    plants.Add(prop.Cell);
                }
                else if (prop.Prefab == villageForest.BuildingPrefab || prop.Prefab == villageForest.FencePrefab)
                {
                    builtOn.UnionWith(CoveredCells(prop, footprint));
                }
            }

            Assert.That(meadow, Is.Not.Empty, $"seed {seed}: meadows");
            Assert.That(tall, Is.Not.Empty, $"seed {seed}: tall grass");
            foreach ((HashSet<Vector3Int> cells, HashSet<Vector3Int> other, string name) in new[] { (meadow, tall, "meadow"), (tall, meadow, "tall grass") })
            {
                foreach (Vector3Int cell in cells)
                {
                    Assert.That(Reachable(cell, cells.Contains).Count, Is.GreaterThanOrEqualTo(4), $"seed {seed}: a scrap of {name} at {cell}");
                    foreach (Vector3Int near in CellsWithin(cell, 2))
                    {
                        Assert.That(grass.Contains(near), Is.True, $"seed {seed}: {name} at {cell} within two cells of the map's edge");
                    }

                    foreach (Vector3Int near in CellsWithin(cell, 1))
                    {
                        Assert.That(paths.Contains(near) || dirt.Contains(near) || other.Contains(near), Is.False, $"seed {seed}: {name} at {cell} beside {near}");
                    }
                }
            }

            foreach (Vector3Int cell in meadow)
            {
                Assert.That(generation.Regions[cell], Is.EqualTo(VillageForestGenerator.Region.Clearing), $"seed {seed}: meadow at {cell} outside the clearing");
            }

            foreach (Vector3Int cell in tall)
            {
                Assert.That(builtOn.Contains(cell), Is.False, $"seed {seed}: tall grass under a building or fence at {cell}");
                Assert.That(plants.Contains(cell), Is.False, $"seed {seed}: a plant on the tall grass at {cell}");
                bool forest = generation.Regions[cell] == VillageForestGenerator.Region.Forest;
                bool edge = false;
                foreach (Vector3Int near in CellsWithin(cell, reach))
                {
                    edge |= generation.Regions.TryGetValue(near, out VillageForestGenerator.Region region) && (region == VillageForestGenerator.Region.Forest) != forest;
                }

                Assert.That(edge, Is.True, $"seed {seed}: tall grass at {cell} away from the forest's edge");
            }

            Assert.That(CornerProblems(layout, "Meadow", "TallGrass"), Is.Empty, $"seed {seed}");
        }
    }

    /// <summary>
    /// The Village Forest's tall grass and meadows, laid last on the detail stream, leave everything laid before them
    /// where it was: with both turned off, every other layer (the dirt included), the spawn and every mob are the same,
    /// and so is every prop but the walk-through props the tall grass removed, all of them on it.
    /// </summary>
    [Test]
    public void TheVillageForestsMeadowsAndTallGrass_LeaveTheRestOfItsLayoutWhereItWas()
    {
        AreaGenerator without = Track(Object.Instantiate(villageForest));
        SerializedObject settings = new(without);
        settings.FindProperty("tallGrassCount").intValue = 0;
        settings.FindProperty("meadowCount").intValue = 0;
        settings.ApplyModifiedPropertiesWithoutUndo();
        for (int seed = 1; seed <= 5; seed++)
        {
            AreaLayout full = VillageForestLayout(seed).Layout;
            AreaLayout plain = without.Generate(seed);
            int meadowIndex = full.IndexOf(ProjectMaterial("Meadow"));
            int tallIndex = full.IndexOf(ProjectMaterial("TallGrass"));
            HashSet<Vector3Int> tall = full.TerrainCells(tallIndex);
            Assert.That(plain.TerrainCells(meadowIndex), Is.Empty, $"seed {seed}");
            Assert.That(plain.TerrainCells(tallIndex), Is.Empty, $"seed {seed}");
            for (int layer = 0; layer < full.TerrainLayerCount; layer++)
            {
                if (layer != meadowIndex && layer != tallIndex)
                {
                    Assert.That(full.TerrainCells(layer).SetEquals(plain.TerrainCells(layer)), Is.True, $"seed {seed}: layer {layer}");
                }
            }

            Assert.That(full.SpawnCell, Is.EqualTo(plain.SpawnCell));
            Assert.That(full.Mobs.Count, Is.EqualTo(plain.Mobs.Count));
            for (int i = 0; i < full.Mobs.Count; i++)
            {
                Assert.That(full.Mobs[i].Cell, Is.EqualTo(plain.Mobs[i].Cell), $"seed {seed}: mob {i}");
            }

            foreach (AreaLayout.Prop prop in full.Props)
            {
                Assert.That(HasProp(plain, prop), Is.True, $"seed {seed}: {prop.Prefab.name} at {prop.Cell} added");
            }

            foreach (AreaLayout.Prop prop in plain.Props)
            {
                if (!HasProp(full, prop))
                {
                    Assert.That(PropGridSnapping.TryGetFootprint(prop.Prefab, out _), Is.False, $"seed {seed}: blocking {prop.Prefab.name} removed");
                    Assert.That(tall.Contains(prop.Cell), Is.True, $"seed {seed}: {prop.Prefab.name} at {prop.Cell} removed off the tall grass");
                }
            }
        }
    }

    /// <summary>
    /// For every Lakeside seed the meadows lie on the open grass inside the band of trees and the tall grass along the
    /// trees or the marsh: both on grass, in pieces of four cells or more, two cells or more inside the map's edge, a
    /// cell or more from every other look (water, sand, mud, the paths, dry grass, the ponds and the dock) and from each
    /// other, the meadows deeper than the band of trees, the tall grass within its reach of that band or of the mud, with
    /// no walk-through prop on it; and around both, no third look shows under a rounded corner.
    /// </summary>
    [Test]
    public void LakesideLayouts_LayMeadowsOnTheOpenGrass_AndTallGrassAlongTheTreesAndTheMarsh()
    {
        int reach = lakeside.TallGrassReach;
        for (int seed = 1; seed <= SeedCount; seed++)
        {
            AreaLayout layout = lakeside.Generate(seed);
            HashSet<Vector3Int> grass = Cells(layout, "Grass");
            HashSet<Vector3Int> meadow = Cells(layout, "Meadow");
            HashSet<Vector3Int> tall = Cells(layout, "TallGrass");
            HashSet<Vector3Int> mud = Cells(layout, "Mud");
            HashSet<Vector3Int> others = new();
            foreach (string name in new[] { "DryGrass", "Sand", "Mud", "Gravel", "Stone", "Shallows", "Water", "Planks" })
            {
                others.UnionWith(Cells(layout, name));
            }

            HashSet<Vector3Int> plants = new();
            foreach (AreaLayout.Prop prop in layout.Props)
            {
                if (!PropGridSnapping.TryGetFootprint(prop.Prefab, out _))
                {
                    plants.Add(prop.Cell);
                }
            }

            Assert.That(meadow, Is.Not.Empty, $"seed {seed}: meadows");
            Assert.That(tall, Is.Not.Empty, $"seed {seed}: tall grass");
            foreach ((HashSet<Vector3Int> cells, HashSet<Vector3Int> other, string name) in new[] { (meadow, tall, "meadow"), (tall, meadow, "tall grass") })
            {
                foreach (Vector3Int cell in cells)
                {
                    Assert.That(Reachable(cell, cells.Contains).Count, Is.GreaterThanOrEqualTo(4), $"seed {seed}: a scrap of {name} at {cell}");
                    foreach (Vector3Int near in CellsWithin(cell, 2))
                    {
                        Assert.That(grass.Contains(near), Is.True, $"seed {seed}: {name} at {cell} within two cells of the map's edge");
                    }

                    foreach (Vector3Int near in CellsWithin(cell, 1))
                    {
                        Assert.That(others.Contains(near) || other.Contains(near), Is.False, $"seed {seed}: {name} at {cell} beside {near}");
                    }
                }
            }

            foreach (Vector3Int cell in meadow)
            {
                foreach (Vector3Int near in CellsWithin(cell, lakeside.BorderDepth))
                {
                    Assert.That(grass.Contains(near), Is.True, $"seed {seed}: meadow at {cell} in the band of trees");
                }
            }

            foreach (Vector3Int cell in tall)
            {
                Assert.That(plants.Contains(cell), Is.False, $"seed {seed}: a plant on the tall grass at {cell}");
                bool nearTreesOrMud = false;
                foreach (Vector3Int near in CellsWithin(cell, lakeside.BorderDepth + reach))
                {
                    nearTreesOrMud |= !grass.Contains(near);
                }

                foreach (Vector3Int near in CellsWithin(cell, reach))
                {
                    nearTreesOrMud |= mud.Contains(near);
                }

                Assert.That(nearTreesOrMud, Is.True, $"seed {seed}: tall grass at {cell} away from the trees and the marsh");
            }

            Assert.That(CornerProblems(layout, "Meadow", "TallGrass"), Is.Empty, $"seed {seed}");
        }
    }

    /// <summary>
    /// The Lakeside's tall grass and meadows, laid last, leave everything laid before them where it was: with both
    /// turned off, every other layer, the spawn and every mob are the same, and so is every prop but the walk-through
    /// props the tall grass removed, all of them on it.
    /// </summary>
    [Test]
    public void TheLakesidesMeadowsAndTallGrass_LeaveTheRestOfItsLayoutWhereItWas()
    {
        AreaGenerator without = Track(Object.Instantiate(lakeside));
        SerializedObject settings = new(without);
        settings.FindProperty("tallGrassCount").intValue = 0;
        settings.FindProperty("meadowCount").intValue = 0;
        settings.ApplyModifiedPropertiesWithoutUndo();
        for (int seed = 1; seed <= 5; seed++)
        {
            AreaLayout full = lakeside.Generate(seed);
            AreaLayout plain = without.Generate(seed);
            int meadowIndex = full.IndexOf(ProjectMaterial("Meadow"));
            int tallIndex = full.IndexOf(ProjectMaterial("TallGrass"));
            HashSet<Vector3Int> tall = full.TerrainCells(tallIndex);
            Assert.That(plain.TerrainCells(meadowIndex), Is.Empty, $"seed {seed}");
            Assert.That(plain.TerrainCells(tallIndex), Is.Empty, $"seed {seed}");
            for (int layer = 0; layer < full.TerrainLayerCount; layer++)
            {
                if (layer != meadowIndex && layer != tallIndex)
                {
                    Assert.That(full.TerrainCells(layer).SetEquals(plain.TerrainCells(layer)), Is.True, $"seed {seed}: layer {layer}");
                }
            }

            Assert.That(full.SpawnCell, Is.EqualTo(plain.SpawnCell));
            Assert.That(full.Mobs.Count, Is.EqualTo(plain.Mobs.Count));
            for (int i = 0; i < full.Mobs.Count; i++)
            {
                Assert.That(full.Mobs[i].Cell, Is.EqualTo(plain.Mobs[i].Cell), $"seed {seed}: mob {i}");
            }

            foreach (AreaLayout.Prop prop in full.Props)
            {
                Assert.That(HasProp(plain, prop), Is.True, $"seed {seed}: {prop.Prefab.name} at {prop.Cell} added");
            }

            foreach (AreaLayout.Prop prop in plain.Props)
            {
                if (!HasProp(full, prop))
                {
                    Assert.That(PropGridSnapping.TryGetFootprint(prop.Prefab, out _), Is.False, $"seed {seed}: blocking {prop.Prefab.name} removed");
                    Assert.That(tall.Contains(prop.Cell), Is.True, $"seed {seed}: {prop.Prefab.name} at {prop.Cell} removed off the tall grass");
                }
            }
        }
    }

    /// <summary>
    /// The Lakeside's shallow ponds leave everything laid before them where it was: with the ponds turned off (and the
    /// tall grass and meadows laid after them on the same stream turned off on both sides), every other layer (the dry
    /// grass included), the spawn and every mob are the same, and so is every prop but the walk-through props the ponds
    /// removed, all of them on, beside or up to three rows below a pond.
    /// </summary>
    [Test]
    public void TheLakesidesPonds_LeaveTheRestOfItsLayoutWhereItWas()
    {
        AreaGenerator withPonds = Track(Object.Instantiate(lakeside));
        SerializedObject pondSettings = new(withPonds);
        pondSettings.FindProperty("tallGrassCount").intValue = 0;
        pondSettings.FindProperty("meadowCount").intValue = 0;
        pondSettings.ApplyModifiedPropertiesWithoutUndo();
        AreaGenerator without = Track(Object.Instantiate(withPonds));
        SerializedObject settings = new(without);
        settings.FindProperty("shallowPondCount").intValue = 0;
        settings.ApplyModifiedPropertiesWithoutUndo();
        int shallowsIndex = lakeside.TerrainLayers.Count - 3;
        for (int seed = 1; seed <= 5; seed++)
        {
            AreaLayout full = withPonds.Generate(seed);
            AreaLayout plain = without.Generate(seed);
            HashSet<Vector3Int> wet = new();
            foreach (Vector3Int cell in Cells(full, "Shallows"))
            {
                wet.UnionWith(CellsWithin(cell, 1));
                wet.UnionWith(CellsWithin(cell + new Vector3Int(0, -2, 0), 1));
                wet.UnionWith(CellsWithin(cell + new Vector3Int(0, -3, 0), 1));
            }

            Assert.That(full.IndexOf(ProjectMaterial("Shallows")), Is.EqualTo(shallowsIndex));
            for (int layer = 0; layer < full.TerrainLayerCount; layer++)
            {
                if (layer != shallowsIndex)
                {
                    Assert.That(full.TerrainCells(layer).SetEquals(plain.TerrainCells(layer)), Is.True, $"seed {seed}: layer {layer}");
                }
            }

            Assert.That(full.SpawnCell, Is.EqualTo(plain.SpawnCell));
            Assert.That(full.Mobs.Count, Is.EqualTo(plain.Mobs.Count));
            for (int i = 0; i < full.Mobs.Count; i++)
            {
                Assert.That(full.Mobs[i].Cell, Is.EqualTo(plain.Mobs[i].Cell), $"seed {seed}: mob {i}");
            }

            foreach (AreaLayout.Prop prop in full.Props)
            {
                Assert.That(HasProp(plain, prop), Is.True, $"seed {seed}: {prop.Prefab.name} at {prop.Cell} added");
            }

            foreach (AreaLayout.Prop prop in plain.Props)
            {
                if (!HasProp(full, prop))
                {
                    Assert.That(PropGridSnapping.TryGetFootprint(prop.Prefab, out _), Is.False, $"seed {seed}: blocking {prop.Prefab.name} removed");
                    Assert.That(wet.Contains(prop.Cell), Is.True, $"seed {seed}: {prop.Prefab.name} at {prop.Cell} removed away from the ponds");
                }
            }
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
