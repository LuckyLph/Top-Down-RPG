using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

/// <summary>
/// Tests for scene generation. The project's generators place the generated props in every scatter layer and lay their
/// layouts without warnings; every other test runs copies of them with their props and with test built props
/// (<see cref="TestPropPrefabs"/>: a campfire and seats, a building and the village's furniture), which the assets
/// leave unset, so the campsites, the square and the buildings are laid too. The Lakeside and the Village Forest dry
/// the bushes that stand on their dry grass and tall grass. The Clearing generator gives playable layouts for many
/// seeds (paths on the grass, open ground around a spawn on the road, props clear of the paths and of each other, every
/// mob spread out where it can walk to the spawn, a campsite on a patch of dirt); the Village Forest generator does
/// too, with a village, a clearing and a forest joined by one connected path network, buildings beside the street with
/// reachable doors, a paved square, dirt along the trails near the village and most mobs in the forest; the Lakeside
/// generator lays a lake off the paths with a beach and a marsh, dry grass on the open grass, small shallow ponds on
/// plain grass apart from the lake with no third look at their corners, a dock from the trail's end across the beach
/// into the lake that adds no third look under a rounded corner, and keeps props off the water, the beach and the mud
/// with the beach and every mob within the spawn's reach; the Snowfield generator lays a frozen lake with a hole of
/// open water that meets only ice, a trail that stops short of the ice, and a campsite on dirt away from the ice and
/// the paths, with props off the ice and every mob and the ice within the spawn's reach; the Coast generator lays a
/// beach and the ocean out to the map's northern edge, the ocean meeting only the beach, a trail that ends on the beach
/// short of the sea, a campsite away from the coast and a plateau of raised grass that only its ramp leads up, with
/// props off the coast, the cliff's face and the ramp's approach and the beach and every mob within the spawn's reach;
/// the Lava Fields generator lays a lava river and pools that keep a cell of earth from the paths and stay off the
/// spawn's ground, the road crossing the river on earth, an unbroken trail that stops short of the lava and a campsite
/// away from it, with props off and beside the lava and every mob and the trail within the spawn's reach around it; the
/// regions' torches and braziers stand where their regions call for them (Docs/Lighting.md, Phase 6: either end of the
/// Lava Fields' causeway, the Coast's plateau, the Lakeside's dock's foot, the Snowfield's trail's end); the
/// terrain each generator lays from its detail stream leaves the rest of its layout where it was, and the Lakeside's
/// ponds leave its dry grass where it was and its dock everything else; the same seed gives the same layout; the
/// project's style lists every ground material in the shared draw order, the project's generators list only the layers
/// they lay (plain ground tiles and the project's materials) as a subsequence of it, and the default movement profile
/// blocks water and makes mud and the shallows cost more; and <see cref="AreaLayoutWriter"/> writes a layout into a
/// scene as plain ground layers drawn by a <see cref="TerrainBlendRenderer"/> in the shared order, with navigation
/// priorities from that order, keeping painted layers the generator does not list and removing empty ones, a renderer
/// that follows a layout written over another, a navigation grid that blocks exactly the props' cells, wades the
/// Lakeside's ponds at the shallows' cost and walks its dock over the lake at grass's cost, the water beside it
/// blocked, walks the Snowfield's snow at its cost and its ice like grass with its hole blocked, blocks the Coast's
/// ocean and its plateau's cliff but not its ramp and the Lava Fields' lava, an area of only some layers that still
/// draws and navigates, and writes up to 16 layers but refuses 17 and refuses layers out of the shared order.
/// </summary>
public class AreaGenerationTests
{
    private const string ClearingGeneratorPath = "Assets/Settings/Generation/Generator_Clearing.asset";
    private const string VillageForestGeneratorPath = "Assets/Settings/Generation/Generator_VillageForest.asset";
    private const string LakesideGeneratorPath = "Assets/Settings/Generation/Generator_Lakeside.asset";
    private const string SnowfieldGeneratorPath = "Assets/Settings/Generation/Generator_Snowfield.asset";
    private const string CoastGeneratorPath = "Assets/Settings/Generation/Generator_Coast.asset";
    private const string LavaFieldsGeneratorPath = "Assets/Settings/Generation/Generator_LavaFields.asset";
    private const string TerrainStylePath = "Assets/Settings/Terrain/TerrainStyle.asset";
    private const string ProfilePath = "Assets/Settings/AI/TerrainMovement_Default.asset";
    private const string PropsRootName = "Props";
    private const string PropsFolder = "Assets/Prefabs/Props/";
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
    private readonly Dictionary<string, AreaGenerator> projectCopies = new();
    private readonly Dictionary<AreaGenerator, Dictionary<int, AreaLayout>> layouts = new();
    private TestPropPrefabs testProps;
    private ClearingGenerator generator;
    private VillageForestGenerator villageForest;
    private LakesideGenerator lakeside;
    private SnowfieldGenerator snowfield;
    private CoastGenerator coast;
    private LavaFieldsGenerator lavaFields;
    private int undoGroup;

    /// <summary>
    /// Saves the test built props and copies the project's generators with them once for the fixture
    /// (<see cref="WithTestProps{T}"/>), leaving the project's assets as they are. The tests only read the copies, so
    /// they share them, and the layouts they lay (<see cref="Layout"/>).
    /// </summary>
    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        testProps = TestPropPrefabs.Create();
        generator = ProjectCopy<ClearingGenerator>(ClearingGeneratorPath);
        villageForest = ProjectCopy<VillageForestGenerator>(VillageForestGeneratorPath);
        lakeside = ProjectCopy<LakesideGenerator>(LakesideGeneratorPath);
        snowfield = ProjectCopy<SnowfieldGenerator>(SnowfieldGeneratorPath);
        coast = ProjectCopy<CoastGenerator>(CoastGeneratorPath);
        lavaFields = ProjectCopy<LavaFieldsGenerator>(LavaFieldsGeneratorPath);
    }

    /// <summary>
    /// Destroys the generators' copies and removes the test built props.
    /// </summary>
    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        foreach (AreaGenerator copy in projectCopies.Values)
        {
            Object.DestroyImmediate(copy);
        }

        projectCopies.Clear();
        layouts.Clear();
        villageForestLayouts.Clear();
        TestPropPrefabs.Delete();
    }

    /// <summary>
    /// Starts the undo group the test's scene changes are reverted to.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
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
    /// The project's generators fill every scatter layer with the generated props (Docs/PropGeneration.md, Phase 5) and
    /// lay every layout without a warning, their mobs spread over it: each layer lists props of the props' folder, for
    /// every seed each places some, and every prop comes from a layer but the generator's own light props and the seats
    /// around them (Docs/Lighting.md, Phases 5 and 6): the campfire, laid at every seed, and its seats at the campsites,
    /// the Village Forest's lamp posts and the regions' torches and braziers; the assets set no building or other village
    /// furniture.
    /// </summary>
    [TestCase(ClearingGeneratorPath)]
    [TestCase(VillageForestGeneratorPath)]
    [TestCase(LakesideGeneratorPath)]
    [TestCase(SnowfieldGeneratorPath)]
    [TestCase(CoastGeneratorPath)]
    [TestCase(LavaFieldsGeneratorPath)]
    public void TheProjectsGenerators_FillEveryScatterLayer_AndWarnOfNothing(string generatorPath)
    {
        AreaGenerator target = AssetDatabase.LoadAssetAtPath<AreaGenerator>(generatorPath);
        Assert.That(target.Scatters, Is.Not.Empty, "scatter layers");
        foreach (AreaGenerator.PropScatter scatter in target.Scatters)
        {
            Assert.That(scatter.Prefabs, Is.Not.Empty, $"{scatter.Label}'s prefabs");
            foreach (GameObject prefab in scatter.Prefabs)
            {
                Assert.That(prefab != null && AssetDatabase.GetAssetPath(prefab).StartsWith(PropsFolder), Is.True, $"{scatter.Label} lists {prefab}");
            }
        }

        for (int seed = 1; seed <= SeedCount; seed++)
        {
            AreaLayout layout = Layout(target, seed);
            Assert.That(layout.Warnings, Is.Empty, $"seed {seed}: warnings");
            Assert.That(layout.Mobs, Is.Not.Empty, $"seed {seed}: mobs");
            foreach (AreaGenerator.PropScatter scatter in target.Scatters)
            {
                int placed = 0;
                foreach (AreaLayout.Prop prop in layout.Props)
                {
                    placed += prop.Scatter == scatter ? 1 : 0;
                }

                Expect(placed > 0, $"seed {seed}: {scatter.Label} places nothing");
            }

            HashSet<GameObject> own = OwnProps(target, out GameObject campfire);
            int fires = 0;
            foreach (AreaLayout.Prop prop in layout.Props)
            {
                Expect(prop.Scatter != null || own.Contains(prop.Prefab), $"seed {seed}: {prop.Prefab.name} at {prop.Cell} comes from no scatter layer");
                fires += campfire != null && prop.Prefab == campfire ? 1 : 0;
            }

            Expect(campfire == null || fires == 1, $"seed {seed}: {fires} campfires");
        }
    }

    /// <summary>
    /// The Lakeside and the Village Forest dry the bushes on the dry grass and the tall grass they lay after their
    /// props: the dry bushes are in no scatter layer, and over the seeds some stand in the layouts, each wholly on
    /// those patches, placed by a layer that lists a bush of the swap covering as many cells; no bush of the swap that
    /// a dry bush fits is left wholly on them.
    /// </summary>
    [TestCase(VillageForestGeneratorPath)]
    [TestCase(LakesideGeneratorPath)]
    public void TheGenerators_DryTheBushesOnTheirDryAndTallGrass(string generatorPath)
    {
        AreaGenerator target = ProjectCopy<AreaGenerator>(generatorPath);
        AreaGenerator.PropSwap swap = DryBushes(target);
        Assert.That(swap.From, Is.Not.Empty, "the bushes that dry");
        Assert.That(swap.To, Is.Not.Empty, "the dry bushes");
        HashSet<Vector2Int> drySizes = new();
        foreach (GameObject dry in swap.To)
        {
            Assert.That(PropGridSnapping.TryGetFootprint(dry, out Rect footprint), Is.True, $"{dry.name} blocks");
            drySizes.Add(PropGridSnapping.CellsCovered(footprint, Vector2.one));
            foreach (AreaGenerator.PropScatter scatter in target.Scatters)
            {
                Assert.That(ListContains(scatter.Prefabs, dry), Is.False, $"{scatter.Label} lists {dry.name}");
            }
        }

        int dried = 0;
        for (int seed = 1; seed <= SeedCount; seed++)
        {
            AreaLayout layout = Layout(target, seed);
            HashSet<Vector3Int> patches = new(CellsOrNone(layout, "DryGrass"), CellComparer.Instance);
            patches.UnionWith(Cells(layout, "TallGrass"));
            foreach (AreaLayout.Prop prop in layout.Props)
            {
                if (!PropGridSnapping.TryGetFootprint(prop.Prefab, out Rect footprint))
                {
                    continue;
                }

                Vector2Int size = PropGridSnapping.CellsCovered(footprint, Vector2.one);
                bool onPatches = CoveredCells(prop, footprint).TrueForAll(patches.Contains);
                if (ListContains(swap.To, prop.Prefab))
                {
                    dried++;
                    Expect(onPatches, $"seed {seed}: {prop.Prefab.name} at {prop.Cell} off the dry and tall grass");
                    Expect(prop.Scatter != null && ListsBushOfSize(prop.Scatter, swap, size), $"seed {seed}: {prop.Prefab.name} at {prop.Cell} where no bush of its size stood");
                }
                else if (ListContains(swap.From, prop.Prefab))
                {
                    Expect(!onPatches || !drySizes.Contains(size), $"seed {seed}: {prop.Prefab.name} at {prop.Cell} left green on the dry or tall grass");
                }
            }
        }

        Assert.That(dried, Is.GreaterThan(0), "dry bushes over the seeds");
    }

    /// <summary>
    /// The Clearing, the Village Forest, the Lakeside and the Coast lay weed patches on their plain grass (TerrainTypes.md,
    /// Phase 12): for every seed some, in pieces of four cells or more, every cell on the grass with grass all round it
    /// for two cells, so inside the map's edge, and no cell of another layer (a path, another look) within a step.
    /// </summary>
    [TestCase(ClearingGeneratorPath)]
    [TestCase(VillageForestGeneratorPath)]
    [TestCase(LakesideGeneratorPath)]
    [TestCase(CoastGeneratorPath)]
    public void TheGenerators_LayWeedsOnThePlainGrass(string generatorPath)
    {
        AreaGenerator target = ProjectCopy<AreaGenerator>(generatorPath);
        for (int seed = 1; seed <= SeedCount; seed++)
        {
            AreaLayout layout = Layout(target, seed);
            HashSet<Vector3Int> grass = Cells(layout, "Grass");
            HashSet<Vector3Int> weeds = Cells(layout, "Weeds");
            HashSet<Vector3Int> others = new(CellComparer.Instance);
            for (int layer = 0; layer < layout.TerrainLayerCount; layer++)
            {
                if (layout.TerrainCells(layer) != grass && layout.TerrainCells(layer) != weeds)
                {
                    others.UnionWith(layout.TerrainCells(layer));
                }
            }

            Assert.That(weeds, Is.Not.Empty, $"seed {seed}: weeds");
            HashSet<Vector3Int> seen = new(CellComparer.Instance);
            foreach (Vector3Int cell in weeds)
            {
                if (seen.Add(cell))
                {
                    HashSet<Vector3Int> piece = Reachable(cell, weeds.Contains);
                    seen.UnionWith(piece);
                    Expect(piece.Count >= 4, $"seed {seed}: a scrap of weeds at {cell}");
                }

                foreach (Vector3Int near in CellsWithin(cell, 2))
                {
                    Expect(grass.Contains(near), $"seed {seed}: weeds at {cell} within two cells of the map's edge");
                }

                foreach (Vector3Int near in CellsWithin(cell, 1))
                {
                    Expect(!others.Contains(near), $"seed {seed}: weeds at {cell} beside another look at {near}");
                }
            }
        }
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
            AreaLayout layout = Layout(generator, seed);
            HashSet<Vector3Int> grass = Cells(layout, "Grass");
            HashSet<Vector3Int> gravel = Cells(layout, "Gravel");
            HashSet<Vector3Int> stone = Cells(layout, "Stone");
            HashSet<Vector3Int> footprints = Footprints(layout);

            Assert.That(stone, Is.Not.Empty, $"seed {seed}: the road");
            Assert.That(gravel.Count > 0, Is.EqualTo(generator.GravelPathCount > 0), $"seed {seed}: the gravel paths");
            Expect(grass.IsSupersetOf(stone) && grass.IsSupersetOf(gravel), $"seed {seed}: paths only on grass");
            Expect(stone.Contains(layout.SpawnCell), $"seed {seed}: the spawn is on the road");

            int radius = generator.SpawnClearRadius;
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    Vector3Int cell = layout.SpawnCell + new Vector3Int(dx, dy, 0);
                    Expect(grass.Contains(cell), $"seed {seed}: grass at {cell} around the spawn");
                    if (dx * dx + dy * dy <= radius * radius)
                    {
                        Expect(!footprints.Contains(cell), $"seed {seed}: a prop at {cell} near the spawn");
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
                        Expect(!(stone.Contains(near) || gravel.Contains(near)), $"seed {seed}: a prop at {cell} beside a path");
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
            AreaLayout layout = Layout(generator, seed);
            HashSet<Vector3Int> grass = Cells(layout, "Grass");
            HashSet<Vector3Int> footprints = new(CellComparer.Instance);
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
                    Expect(grass.Contains(cell), $"seed {seed}: {prop.Prefab.name} on grass at {cell}");
                    Expect(footprints.Add(cell), $"seed {seed}: two footprints on {cell}");
                }
            }

            Assert.That(blocking, Is.GreaterThan(0), $"seed {seed}: trees placed");
            foreach (AreaLayout.Prop prop in layout.Props)
            {
                if (PropGridSnapping.TryGetFootprint(prop.Prefab, out _))
                {
                    continue;
                }

                Expect(grass.Contains(prop.Cell), $"seed {seed}: {prop.Prefab.name} on grass");
                Expect(!(Cells(layout, "Gravel").Contains(prop.Cell) || Cells(layout, "Stone").Contains(prop.Cell)), $"seed {seed}: {prop.Prefab.name} on a path");
                Expect(!footprints.Contains(prop.Cell), $"seed {seed}: {prop.Prefab.name} under a footprint");
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
            AreaLayout layout = Layout(generator, seed);
            HashSet<Vector3Int> grass = Cells(layout, "Grass");
            HashSet<Vector3Int> footprints = Footprints(layout);
            HashSet<Vector3Int> reachable = Reachable(layout.SpawnCell, cell => grass.Contains(cell) && !footprints.Contains(cell));

            Assert.That(layout.Warnings, Is.Empty, $"seed {seed}");
            Assert.That(layout.Mobs.Count, Is.EqualTo(generator.MobCount), $"seed {seed}");
            for (int i = 0; i < layout.Mobs.Count; i++)
            {
                AreaLayout.Mob mob = layout.Mobs[i];
                Assert.That(mob.Prefab, Is.SameAs(generator.MobPrefab));
                Expect(reachable.Contains(mob.Cell), $"seed {seed}: mob at {mob.Cell} reachable from the spawn");
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
            Expect(grass.IsSupersetOf(paths), $"seed {seed}: paths only on grass");
            Expect(!paths.Overlaps(footprints), $"seed {seed}: a footprint on a path");
            Expect(Reachable(layout.SpawnCell, paths.Contains).SetEquals(paths), $"seed {seed}: one path network through the spawn");

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

            Expect(grass.SetEquals(generation.Regions.Keys), $"seed {seed}: every grass cell has a region");
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
                    Expect(Cells(layout, "Plaza").Contains(prop.Cell + new Vector3Int(-clearance - 1, 0, 0)), $"seed {seed}: the fountain's square");
                }

                lamps += prop.Prefab == villageForest.LampPostPrefab ? 1 : 0;
                fences += prop.Prefab == villageForest.FencePrefab ? 1 : 0;
                if (prop.Prefab != villageForest.BuildingPrefab)
                {
                    continue;
                }

                buildings++;
                Vector3Int doorstep = prop.Cell + new Vector3Int(villageForest.DoorColumn, -1, 0);
                Expect(stone.Contains(doorstep), $"seed {seed}: the path to the door at {doorstep}");
                Expect(reachable.Contains(doorstep), $"seed {seed}: the door at {doorstep} reachable from the spawn");
                PropGridSnapping.TryGetFootprint(prop.Prefab, out Rect footprint);
                foreach (Vector3Int cell in CoveredCells(prop, footprint))
                {
                    Assert.That(generation.Regions[cell], Is.EqualTo(VillageForestGenerator.Region.Village), $"seed {seed}: building at {cell}");
                    foreach (Vector3Int near in CellsWithin(cell, clearance))
                    {
                        Expect(!paths.Contains(near) || near == doorstep, $"seed {seed}: the building at {prop.Cell} beside the path at {near}");
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
            HashSet<Vector3Int> footprints = new(CellComparer.Instance);
            foreach (AreaLayout.Prop prop in layout.Props)
            {
                if (!PropGridSnapping.TryGetFootprint(prop.Prefab, out Rect footprint))
                {
                    continue;
                }

                foreach (Vector3Int cell in CoveredCells(prop, footprint))
                {
                    Expect(grass.Contains(cell), $"seed {seed}: {prop.Prefab.name} on grass at {cell}");
                    Expect(footprints.Add(cell), $"seed {seed}: two footprints on {cell}");
                    if (prop.Prefab == villageForest.BuildingPrefab)
                    {
                        continue;
                    }

                    foreach (Vector3Int near in CellsWithin(cell, clearance))
                    {
                        Expect(!paths.Contains(near), $"seed {seed}: {prop.Prefab.name} at {cell} beside a path");
                    }
                }
            }

            Expect(Cells(layout, "Stone").Contains(layout.SpawnCell), $"seed {seed}: the spawn is on the street");
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    Vector3Int cell = layout.SpawnCell + new Vector3Int(dx, dy, 0);
                    Expect(grass.Contains(cell), $"seed {seed}: grass at {cell} around the spawn");
                    if (dx * dx + dy * dy <= radius * radius)
                    {
                        Expect(!footprints.Contains(cell), $"seed {seed}: a prop at {cell} near the spawn");
                    }
                }
            }

            foreach (AreaLayout.Prop prop in layout.Props)
            {
                if (PropGridSnapping.TryGetFootprint(prop.Prefab, out _))
                {
                    continue;
                }

                Expect(grass.Contains(prop.Cell), $"seed {seed}: {prop.Prefab.name} on grass");
                Expect(!paths.Contains(prop.Cell), $"seed {seed}: {prop.Prefab.name} on a path");
                Expect(!footprints.Contains(prop.Cell), $"seed {seed}: {prop.Prefab.name} under a footprint");
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
                Expect(reachable.Contains(mob.Cell), $"seed {seed}: mob at {mob.Cell} reachable from the spawn");
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
            AreaLayout layout = Layout(lakeside, seed);
            HashSet<Vector3Int> grass = Cells(layout, "Grass");
            HashSet<Vector3Int> water = Cells(layout, "Water");
            HashSet<Vector3Int> sand = Cells(layout, "Sand");
            HashSet<Vector3Int> mud = Cells(layout, "Mud");
            HashSet<Vector3Int> stone = Cells(layout, "Stone");
            HashSet<Vector3Int> paths = PathCells(layout);

            Assert.That(layout.Warnings, Is.Empty, $"seed {seed}");
            Assert.That(layout.TerrainLayerCount, Is.EqualTo(12));
            Expect(!water.Overlaps(Cells(layout, "Shallows")), $"seed {seed}: the ponds lie off the lake");
            for (int layer = 0; layer < layout.TerrainLayerCount; layer++)
            {
                Assert.That(layout.TerrainCells(layer), Is.Not.Empty, $"seed {seed}: layer {layer}");
                Expect(grass.IsSupersetOf(layout.TerrainCells(layer)), $"seed {seed}: layer {layer} on grass");
            }

            Expect(!(water.Overlaps(paths) || water.Overlaps(sand) || water.Overlaps(mud)), $"seed {seed}: the lake covers nothing else");
            HashSet<Vector3Int> dryGrass = Cells(layout, "DryGrass");
            Expect(!(dryGrass.Overlaps(water) || dryGrass.Overlaps(sand) || dryGrass.Overlaps(mud) || dryGrass.Overlaps(paths)), $"seed {seed}: dry grass only on open grass");
            foreach (Vector3Int cell in dryGrass)
            {
                Assert.That(Reachable(cell, dryGrass.Contains).Count, Is.GreaterThanOrEqualTo(4), $"seed {seed}: a scrap of dry grass at {cell}");
                foreach (Vector3Int near in CellsWithin(cell, 1))
                {
                    Expect(!(paths.Contains(near) || mud.Contains(near) || !grass.Contains(near)), $"seed {seed}: dry grass at {cell} beside mud, a path or the map's edge");
                }
            }
            foreach (Vector3Int cell in stone)
            {
                foreach (Vector3Int near in CellsWithin(cell, lakeside.RoadMargin))
                {
                    Expect(!water.Contains(near), $"seed {seed}: water at {near} near the road");
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

            Expect(trailReachesBeach, $"seed {seed}: the trail reaches the beach");
            Expect(stone.Contains(layout.SpawnCell), $"seed {seed}: the spawn is on the road");
            HashSet<Vector3Int> footprints = Footprints(layout);
            int radius = lakeside.SpawnClearRadius;
            foreach (Vector3Int cell in CellsWithin(layout.SpawnCell, radius))
            {
                Vector3Int offset = cell - layout.SpawnCell;
                if (offset.x * offset.x + offset.y * offset.y <= radius * radius)
                {
                    Expect(!(footprints.Contains(cell) || water.Contains(cell)), $"seed {seed}: {cell} near the spawn is open");
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
            AreaLayout layout = Layout(lakeside, seed);
            HashSet<Vector3Int> shallows = Cells(layout, "Shallows");
            HashSet<Vector3Int> grass = Cells(layout, "Grass");
            HashSet<Vector3Int> other = new(Cells(layout, "Water"), CellComparer.Instance);
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
            Expect(Cells(layout, "Water").SetEquals(Cells(plain, "Water")), $"seed {seed}: the lake is as it was");

            List<HashSet<Vector3Int>> ponds = new();
            HashSet<Vector3Int> seen = new(CellComparer.Instance);
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
                    Expect(grass.Contains(near) && !other.Contains(near), $"seed {seed}: a pond at {cell} beside {near}, not plain grass");
                    Expect(!props.Contains(near), $"seed {seed}: a prop or mob on or beside the pond at {cell}");
                }

                HashSet<Vector3Int> own = Reachable(cell, shallows.Contains);
                foreach (Vector3Int near in CellsWithin(cell, 2))
                {
                    Expect(!(shallows.Contains(near) && !own.Contains(near)), $"seed {seed}: two ponds near {cell}");
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
    /// sand, so it lies off the ponds, the mud and the grass; no prop stands on or beside it but its torch (Docs/Lighting.md,
    /// Phase 6); and it adds no third look
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
            AreaLayout layout = Layout(lakeside, seed);
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
                    Expect(sand.Contains(cell) || gravel.Contains(cell) || water.Contains(cell), $"seed {seed}: the dock at {cell} lies on the beach, the trail or the lake");
                    inWater &= water.Contains(cell);
                    coversTrail |= y == bounds.yMin && gravel.Contains(cell);
                }

                rowsInWater = inWater ? rowsInWater + 1 : 0;
                if (!inWater)
                {
                    for (int x = bounds.xMin - 1; x <= bounds.xMax; x++)
                    {
                        Vector3Int cell = new(x, y, 0);
                        Expect(dock.Contains(cell) || sand.Contains(cell) || water.Contains(cell) || gravel.Contains(cell), $"seed {seed}: beside the dock's foot at {cell}");
                    }
                }
            }

            Expect(coversTrail, $"seed {seed}: the dock starts on the trail's end");
            Assert.That(rowsInWater, Is.InRange(lakeside.DockReach.x, lakeside.DockReach.y), $"seed {seed}: rows over the water up to the far end");
            int shore = bounds.yMax - rowsInWater;
            int clearance = lakeside.DockClearance;
            for (int y = shore; y < bounds.yMax + clearance; y++)
            {
                for (int x = bounds.xMin - clearance; x < bounds.xMax + clearance; x++)
                {
                    Vector3Int cell = new(x, y, 0);
                    Expect(water.Contains(cell) || sand.Contains(cell), $"seed {seed}: {cell} near the dock's lake end is water or beach");
                }
            }

            HashSet<Vector3Int> props = Footprints(layout);
            foreach (AreaLayout.Prop prop in layout.Props)
            {
                props.Add(prop.Cell);
            }

            foreach (AreaLayout.Prop prop in layout.Props)
            {
                if (prop.Prefab == lakeside.DockTorchPrefab)
                {
                    props.Remove(prop.Cell);
                }
            }

            foreach (Vector3Int cell in dock)
            {
                foreach (Vector3Int near in CellsWithin(cell, 1))
                {
                    Expect(!props.Contains(near), $"seed {seed}: a prop on or beside the dock at {near}");
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
    /// reach of 0), every other layer, the spawn and every mob are the same, and so is every prop but the dock's torch
    /// and the walk-through props the dock removed, all of them on, beside or up to three rows below it.
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
            AreaLayout full = Layout(lakeside, seed);
            AreaLayout plain = without.Generate(seed);
            int planksIndex = full.IndexOf(ProjectMaterial("Planks"));
            Assert.That(plain.TerrainCells(planksIndex), Is.Empty, $"seed {seed}: no dock");
            HashSet<Vector3Int> near = new(CellComparer.Instance);
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
                    Expect(full.TerrainCells(layer).SetEquals(plain.TerrainCells(layer)), $"seed {seed}: layer {layer}");
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
                Expect(HasProp(plain, prop) || prop.Prefab == lakeside.DockTorchPrefab, $"seed {seed}: {prop.Prefab.name} at {prop.Cell} added");
            }

            foreach (AreaLayout.Prop prop in plain.Props)
            {
                if (!HasProp(full, prop))
                {
                    Expect(!PropGridSnapping.TryGetFootprint(prop.Prefab, out _), $"seed {seed}: blocking {prop.Prefab.name} removed");
                    Expect(near.Contains(prop.Cell) || full.Props.Exists(torch => torch.Prefab == lakeside.DockTorchPrefab && torch.Cell == prop.Cell), $"seed {seed}: {prop.Prefab.name} at {prop.Cell} removed away from the dock");
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
        HashSet<Vector3Int> corners = new(CellComparer.Instance);
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
        AreaLayout layout = Layout(lakeside, 1);
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
            Expect(navigation.IsCellWalkable(cell, profile), $"the dock at {cell}");
            Expect(navigation.AreCellsConnected(layout.SpawnCell, cell, profile), $"the dock at {cell} reachable");
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
                Expect(!navigation.IsCellWalkable(cell, profile), $"water at {cell}");
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
            AreaLayout layout = Layout(snowfield, seed);
            Assert.That(layout.Warnings, Is.Empty, $"seed {seed}: {string.Join("; ", layout.Warnings)}");
            HashSet<Vector3Int> snow = Cells(layout, "Snow");
            for (int layer = 0; layer < layout.TerrainLayerCount; layer++)
            {
                Assert.That(layout.TerrainCells(layer), Is.Not.Empty, $"seed {seed}: layer {layer}");
                Expect(layout.TerrainCells(layer).IsSubsetOf(snow), $"seed {seed}: layer {layer} lies on the snow");
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
                    Expect(!stone.Contains(near), $"seed {seed}: ice at {cell} within the road margin");
                }
            }

            Assert.That(Reachable(anyIce, ice.Contains).Count, Is.EqualTo(ice.Count), $"seed {seed}: the lake is one piece");
            HashSet<Vector3Int> seen = new(CellComparer.Instance);
            int holes = 0;
            foreach (Vector3Int cell in water)
            {
                foreach (Vector3Int near in CellsWithin(cell, snowfield.HoleMargin))
                {
                    Expect(ice.Contains(near), $"seed {seed}: a hole at {cell} without ice at {near}");
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
                    Expect(!ice.Contains(near), $"seed {seed}: the trail at {cell} touches the ice");
                }

                foreach (Vector3Int side in Sides)
                {
                    leavesTheRoad |= stone.Contains(cell + side);
                }
            }

            Expect(leavesTheRoad, $"seed {seed}: the trail leaves the road");
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
            AreaLayout layout = Layout(snowfield, seed);
            HashSet<Vector3Int> dirt = Cells(layout, "Dirt");
            HashSet<Vector3Int> ice = Cells(layout, "Ice");
            HashSet<Vector3Int> paths = PathCells(layout);
            foreach (Vector3Int cell in dirt)
            {
                foreach (Vector3Int near in CellsWithin(cell, 1))
                {
                    Expect(!(ice.Contains(near) || paths.Contains(near)), $"seed {seed}: dirt at {cell} beside the ice or a path");
                }
            }

            int campfires = 0;
            foreach (AreaLayout.Prop prop in layout.Props)
            {
                if (prop.Prefab == snowfield.CampfirePrefab)
                {
                    campfires++;
                    Expect(dirt.Contains(prop.Cell), $"seed {seed}: the campfire stands on the dirt");
                }
                else if (ListContains(snowfield.CampsiteSeats, prop.Prefab) && dirt.Contains(prop.Cell))
                {
                    continue;
                }
                else if (!PropGridSnapping.TryGetFootprint(prop.Prefab, out _))
                {
                    foreach (Vector3Int near in CellsWithin(prop.Cell, 1))
                    {
                        Expect(!dirt.Contains(near), $"seed {seed}: {prop.Prefab.name} at {prop.Cell} on or beside the dirt");
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
            AreaLayout layout = Layout(snowfield, seed);
            HashSet<Vector3Int> snow = Cells(layout, "Snow");
            HashSet<Vector3Int> ice = Cells(layout, "Ice");
            HashSet<Vector3Int> water = Cells(layout, "Water");
            HashSet<Vector3Int> footprints = new(CellComparer.Instance);
            foreach (AreaLayout.Prop prop in layout.Props)
            {
                if (!PropGridSnapping.TryGetFootprint(prop.Prefab, out Rect footprint))
                {
                    Expect(snow.Contains(prop.Cell) && !ice.Contains(prop.Cell), $"seed {seed}: {prop.Prefab.name} at {prop.Cell}");
                    continue;
                }

                foreach (Vector3Int cell in CoveredCells(prop, footprint))
                {
                    Expect(footprints.Add(cell), $"seed {seed}: footprints overlap at {cell}");
                    Expect(snow.Contains(cell), $"seed {seed}: {prop.Prefab.name} at {cell} off the snow");
                    foreach (Vector3Int near in CellsWithin(cell, 1))
                    {
                        Expect(!ice.Contains(near), $"seed {seed}: {prop.Prefab.name} at {cell} on or beside the ice");
                    }
                }
            }

            foreach (AreaLayout.Prop prop in layout.Props)
            {
                if (!PropGridSnapping.TryGetFootprint(prop.Prefab, out _))
                {
                    Expect(!footprints.Contains(prop.Cell), $"seed {seed}: {prop.Prefab.name} under a footprint");
                }
            }

            Expect(Cells(layout, "Stone").Contains(layout.SpawnCell), $"seed {seed}: the spawn is on the road");
            foreach (Vector3Int cell in footprints)
            {
                Assert.That(Vector3Int.Distance(cell, layout.SpawnCell), Is.GreaterThan(snowfield.SpawnClearRadius), $"seed {seed}: a footprint at {cell} near the spawn");
            }

            HashSet<Vector3Int> reachable = Reachable(layout.SpawnCell, cell => snow.Contains(cell) && !footprints.Contains(cell) && !water.Contains(cell));
            Assert.That(layout.Mobs.Count, Is.EqualTo(snowfield.MobCount), $"seed {seed}: mobs");
            for (int i = 0; i < layout.Mobs.Count; i++)
            {
                AreaLayout.Mob mob = layout.Mobs[i];
                Expect(!ice.Contains(mob.Cell), $"seed {seed}: a mob on the ice");
                Expect(reachable.Contains(mob.Cell), $"seed {seed}: mob at {mob.Cell} unreachable");
                Assert.That(Vector3Int.Distance(mob.Cell, layout.SpawnCell), Is.GreaterThanOrEqualTo(snowfield.MobDistanceFromSpawn), $"seed {seed}");
                for (int j = i + 1; j < layout.Mobs.Count; j++)
                {
                    Assert.That(Vector3Int.Distance(mob.Cell, layout.Mobs[j].Cell), Is.GreaterThanOrEqualTo(snowfield.MobSpacing), $"seed {seed}");
                }
            }

            foreach (Vector3Int cell in ice)
            {
                Expect(water.Contains(cell) || reachable.Contains(cell), $"seed {seed}: ice at {cell} unreachable");
            }
        }
    }

    /// <summary>
    /// Generating the Snowfield into a scratch scene's grid, set up as the Scene Generator's scratch scene sets it up (a
    /// grass layer its runtime terrain lists), points that entry at the generator's own ground layer, its lowest, filled
    /// with its tile, and the empty grass layer goes, with no warning that a layer the generator does not list was kept
    /// (Docs/KnownGaps.md, Phase 6).
    /// </summary>
    [Test]
    public void AScratchScenesGrassEntry_IsPointedAtTheGeneratorsGround()
    {
        Grid grid = Track(new GameObject("Grid")).AddComponent<Grid>();
        grid.gameObject.SetActive(false);
        WorldAuthoring.SetUpGround(grid);
        grid.gameObject.SetActive(true);
        AreaTerrain terrain = grid.GetComponent<AreaTerrain>();
        Assert.That(new SerializedObject(terrain).FindProperty("layers").GetArrayElementAtIndex(0).FindPropertyRelative("tilemap").objectReferenceValue.name, Is.EqualTo("GrassTilemap"));
        Track(new GameObject(PropsRootName));

        Assert.That(AreaLayoutWriter.Write(grid, snowfield, Layout(snowfield, 1)), Is.True);

        AreaGenerator.TerrainLayer ground = snowfield.TerrainLayers[0];
        SerializedProperty entry = new SerializedObject(terrain).FindProperty("layers").GetArrayElementAtIndex(0);
        Assert.That(entry.FindPropertyRelative("tilemap").objectReferenceValue, Is.SameAs(grid.transform.Find(ground.TilemapName).GetComponent<Tilemap>()));
        Assert.That(entry.FindPropertyRelative("fillTile").objectReferenceValue, Is.SameAs(ground.Tile));
        Assert.That(grid.transform.Find("GrassTilemap"), Is.Null, "the empty grass layer goes");
        Assert.That(grid.GetComponent<TerrainBlendRenderer>().Layers.Count, Is.EqualTo(snowfield.TerrainLayers.Count));
        LogAssert.NoUnexpectedReceived();
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
        AreaLayout layout = Layout(snowfield, 1);
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
        HashSet<Vector3Int> blocked = new(water, CellComparer.Instance);
        blocked.UnionWith(Footprints(layout));
        HashSet<Vector3Int> others = new(ice, CellComparer.Instance);
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
            Expect(!navigation.IsCellWalkable(cell, profile), $"the hole at {cell}");
        }

        foreach (Vector3Int cell in ice)
        {
            if (!water.Contains(cell))
            {
                Expect(navigation.AreCellsConnected(layout.SpawnCell, cell, profile), $"ice at {cell} reachable");
            }
        }
    }

    /// <summary>
    /// For every Coast seed, with no warnings: every layer has cells and lies on the grass; the ocean is one 4-connected
    /// piece and meets only the beach (8 neighbours), so no third look shows under a rounded corner around the coast
    /// (TerrainTypes.md, Phase 3's Corners note); and down every column of the middle three quarters of the area, the
    /// coast runs from the map's northern edge as ocean, then at least two rows of beach, with no ocean or beach further
    /// south. On the map's edge, where the beach and the ocean run out, the ocean meets the beach with a corner
    /// (Architecture.md's Known gaps); those are not counted.
    /// </summary>
    [Test]
    public void CoastLayouts_LayABeachAndTheOcean_OutToTheMapsNorthernEdge()
    {
        for (int seed = 1; seed <= SeedCount; seed++)
        {
            AreaLayout layout = Layout(coast, seed);
            Assert.That(layout.Warnings, Is.Empty, $"seed {seed}: {string.Join("; ", layout.Warnings)}");
            HashSet<Vector3Int> grass = Cells(layout, "Grass");
            for (int layer = 0; layer < layout.TerrainLayerCount; layer++)
            {
                Assert.That(layout.TerrainCells(layer), Is.Not.Empty, $"seed {seed}: layer {layer}");
                Expect(layout.TerrainCells(layer).IsSubsetOf(grass), $"seed {seed}: layer {layer} lies on the grass");
            }

            HashSet<Vector3Int> sand = Cells(layout, "Sand");
            HashSet<Vector3Int> ocean = Cells(layout, "Ocean");
            Vector3Int anyOcean = default;
            foreach (Vector3Int cell in ocean)
            {
                anyOcean = cell;
                foreach (Vector3Int near in CellsWithin(cell, 1))
                {
                    Expect(!grass.Contains(near) || ocean.Contains(near) || sand.Contains(near), $"seed {seed}: the ocean at {cell} meets {near}");
                }
            }

            Assert.That(Reachable(anyOcean, ocean.Contains).Count, Is.EqualTo(ocean.Count), $"seed {seed}: the ocean is one piece");
            BoundsInt bounds = BoundsOf(grass);
            int margin = bounds.size.x / 8;
            for (int x = bounds.xMin + margin; x < bounds.xMax - margin; x++)
            {
                int y = bounds.yMax - 1;
                while (!grass.Contains(new Vector3Int(x, y, 0)))
                {
                    y--;
                }

                int oceanRows = 0;
                while (ocean.Contains(new Vector3Int(x, y, 0)))
                {
                    oceanRows++;
                    y--;
                }

                int beachRows = 0;
                while (sand.Contains(new Vector3Int(x, y, 0)))
                {
                    beachRows++;
                    y--;
                }

                Assert.That(oceanRows, Is.GreaterThan(0), $"seed {seed}: column {x} reaches the map's edge as ocean");
                Assert.That(beachRows, Is.GreaterThan(1), $"seed {seed}: column {x} has a beach");
                for (; y >= bounds.yMin; y--)
                {
                    Vector3Int cell = new(x, y, 0);
                    Expect(!(ocean.Contains(cell) || sand.Contains(cell)), $"seed {seed}: coast at {cell} south of the beach");
                }
            }

            Assert.That(InlandCornerProblems(layout, "Sand", "Ocean"), Is.Empty, $"seed {seed}");
        }
    }

    /// <summary>
    /// For every Coast seed the trail leaves the road and ends on the beach; no path or dirt cell lies on or beside the
    /// ocean, and the road keeps the road margin from the beach and the ocean; the campsite has one
    /// campfire and every seat on its dirt, no dirt on or beside the beach or a path, and no walk-through prop on or
    /// beside the dirt; and no third look shows under a rounded corner around the trail or the dirt.
    /// </summary>
    [Test]
    public void CoastLayouts_EndTheTrailOnTheBeach_AndKeepThePathsAndTheCampsiteOffTheSea()
    {
        for (int seed = 1; seed <= SeedCount; seed++)
        {
            AreaLayout layout = Layout(coast, seed);
            HashSet<Vector3Int> sand = Cells(layout, "Sand");
            HashSet<Vector3Int> sea = Cells(layout, "Ocean");
            HashSet<Vector3Int> gravel = Cells(layout, "Gravel");
            HashSet<Vector3Int> stone = Cells(layout, "Stone");
            HashSet<Vector3Int> dirt = Cells(layout, "Dirt");
            HashSet<Vector3Int> paths = PathCells(layout);
            HashSet<Vector3Int> dry = new(paths, CellComparer.Instance);
            dry.UnionWith(dirt);
            foreach (Vector3Int cell in dry)
            {
                foreach (Vector3Int near in CellsWithin(cell, 1))
                {
                    Expect(!sea.Contains(near), $"seed {seed}: {cell} beside the sea");
                }
            }

            foreach (Vector3Int cell in stone)
            {
                foreach (Vector3Int near in CellsWithin(cell, coast.RoadMargin))
                {
                    Expect(!(sea.Contains(near) || sand.Contains(near)), $"seed {seed}: the road at {cell} within the road margin of the coast");
                }
            }

            bool leavesTheRoad = false;
            foreach (Vector3Int cell in gravel)
            {
                foreach (Vector3Int side in Sides)
                {
                    leavesTheRoad |= stone.Contains(cell + side);
                }
            }

            Expect(leavesTheRoad, $"seed {seed}: the trail leaves the road");
            Expect(gravel.Overlaps(sand), $"seed {seed}: the trail reaches the beach");
            foreach (Vector3Int cell in dirt)
            {
                foreach (Vector3Int near in CellsWithin(cell, 1))
                {
                    Expect(!(sand.Contains(near) || paths.Contains(near)), $"seed {seed}: dirt at {cell} beside the beach or a path");
                }
            }

            int campfires = 0;
            foreach (AreaLayout.Prop prop in layout.Props)
            {
                if (prop.Prefab == coast.CampfirePrefab)
                {
                    campfires++;
                    Expect(dirt.Contains(prop.Cell), $"seed {seed}: the campfire stands on the dirt");
                }
                else if (ListContains(coast.CampsiteSeats, prop.Prefab) && dirt.Contains(prop.Cell))
                {
                    continue;
                }
                else if (!PropGridSnapping.TryGetFootprint(prop.Prefab, out _))
                {
                    foreach (Vector3Int near in CellsWithin(prop.Cell, 1))
                    {
                        Expect(!dirt.Contains(near), $"seed {seed}: {prop.Prefab.name} at {prop.Cell} on or beside the dirt");
                    }
                }
            }

            Assert.That(campfires, Is.EqualTo(1), $"seed {seed}: campfires");
            Assert.That(CornerProblems(layout, "Dirt", "Gravel"), Is.Empty, $"seed {seed}");
        }
    }

    /// <summary>
    /// For every Coast seed blocking footprints cover grass off the coast, apart; walk-through props stand on grass off
    /// the coast and outside every footprint; the spawn is on the road with no footprint within its clear radius; every
    /// mob stands on land off the ocean, far enough from the spawn and from each other; and the spawn
    /// can walk to every mob, every beach cell and every trail cell, around the footprints, the ocean and the plateau's
    /// cliff.
    /// </summary>
    [Test]
    public void CoastLayouts_KeepPropsOffTheCoast_AndLetTheSpawnReachTheBeachAndEveryMob()
    {
        for (int seed = 1; seed <= SeedCount; seed++)
        {
            AreaLayout layout = Layout(coast, seed);
            HashSet<Vector3Int> grass = Cells(layout, "Grass");
            HashSet<Vector3Int> sand = Cells(layout, "Sand");
            HashSet<Vector3Int> ocean = Cells(layout, "Ocean");
            HashSet<Vector3Int> coastCells = new(sand, CellComparer.Instance);
            coastCells.UnionWith(ocean);
            HashSet<Vector3Int> footprints = new(CellComparer.Instance);
            foreach (AreaLayout.Prop prop in layout.Props)
            {
                if (!PropGridSnapping.TryGetFootprint(prop.Prefab, out Rect footprint))
                {
                    Expect(grass.Contains(prop.Cell) && !coastCells.Contains(prop.Cell), $"seed {seed}: {prop.Prefab.name} at {prop.Cell}");
                    continue;
                }

                foreach (Vector3Int cell in CoveredCells(prop, footprint))
                {
                    Expect(footprints.Add(cell), $"seed {seed}: footprints overlap at {cell}");
                    Expect(grass.Contains(cell) && !coastCells.Contains(cell), $"seed {seed}: {prop.Prefab.name} at {cell} off the grass or on the coast");
                }
            }

            foreach (AreaLayout.Prop prop in layout.Props)
            {
                if (!PropGridSnapping.TryGetFootprint(prop.Prefab, out _))
                {
                    Expect(!footprints.Contains(prop.Cell), $"seed {seed}: {prop.Prefab.name} under a footprint");
                }
            }

            Expect(Cells(layout, "Stone").Contains(layout.SpawnCell), $"seed {seed}: the spawn is on the road");
            foreach (Vector3Int cell in footprints)
            {
                Assert.That(Vector3Int.Distance(cell, layout.SpawnCell), Is.GreaterThan(coast.SpawnClearRadius), $"seed {seed}: a footprint at {cell} near the spawn");
            }

            HashSet<Vector3Int> reachable = Reachable(layout.SpawnCell, cell => grass.Contains(cell) && !footprints.Contains(cell) && !ocean.Contains(cell) && !RaisedGround.IsCliff(layout.Raised, layout.Ramps, cell));
            Assert.That(layout.Mobs.Count, Is.EqualTo(coast.MobCount), $"seed {seed}: mobs");
            for (int i = 0; i < layout.Mobs.Count; i++)
            {
                AreaLayout.Mob mob = layout.Mobs[i];
                Expect(!ocean.Contains(mob.Cell), $"seed {seed}: a mob in the sea");
                Expect(reachable.Contains(mob.Cell), $"seed {seed}: mob at {mob.Cell} unreachable");
                Assert.That(Vector3Int.Distance(mob.Cell, layout.SpawnCell), Is.GreaterThanOrEqualTo(coast.MobDistanceFromSpawn), $"seed {seed}");
                for (int j = i + 1; j < layout.Mobs.Count; j++)
                {
                    Assert.That(Vector3Int.Distance(mob.Cell, layout.Mobs[j].Cell), Is.GreaterThanOrEqualTo(coast.MobSpacing), $"seed {seed}");
                }
            }

            HashSet<Vector3Int> walked = new(sand, CellComparer.Instance);
            walked.UnionWith(Cells(layout, "Gravel"));
            foreach (Vector3Int cell in walked)
            {
                Expect(reachable.Contains(cell), $"seed {seed}: {cell} on the beach or the trail unreachable");
            }
        }
    }

    /// <summary>
    /// For every Coast seed the plateau of raised grass (TerrainTypes.md's Phase 10) lies on the grass, off the coast
    /// and the paths by more than the plateau margin and outside the spawn's disc and the margin beyond it, in one
    /// 4-connected piece every cell of which lies in a run of three of them across and in one down; its ramp, the ramp width wide, lies in its bottom row with cliff on either side and two rows of walkable
    /// ground below it; no prop stands on the cliff's face (the bottom cells and the rows above them its style's cliff
    /// height reaches into), the ramp, as many rows above it or the four rows in front of it a column wider, and no
    /// footprint lies both on and off the plateau; no mob stands on the cliff
    /// (RaisedGround.IsCliff); around the cliff, the footprints and the ocean the spawn reaches the ramp and the
    /// row above it, but no cell of the plateau that is not cliff from anywhere but the ramp; and from the ramp every
    /// cell of the plateau off the rows its cliff face covers whole is reached, so the face cuts no piece of the top off.
    /// </summary>
    [Test]
    public void CoastLayouts_LayAPlateau_ThatOnlyItsRampLeadsUp()
    {
        int faceRows = RaisedGround.RowsReached(coast.TerrainStyle.CliffHeight / TerrainStyle.PixelsPerCell);
        Assert.That(faceRows, Is.EqualTo(2), "The project's cliff face reaches two rows.");
        for (int seed = 1; seed <= SeedCount; seed++)
        {
            AreaLayout layout = Layout(coast, seed);
            HashSet<Vector3Int> raised = layout.Raised;
            HashSet<Vector3Int> ramps = layout.Ramps;
            HashSet<Vector3Int> grass = Cells(layout, "Grass");
            HashSet<Vector3Int> ocean = Cells(layout, "Ocean");
            HashSet<Vector3Int> coastCells = new(Cells(layout, "Sand"), CellComparer.Instance);
            coastCells.UnionWith(ocean);
            HashSet<Vector3Int> paths = PathCells(layout);
            Assert.That(raised, Is.Not.Empty, $"seed {seed}: a plateau");
            Assert.That(ramps.Count, Is.EqualTo(coast.RampWidth), $"seed {seed}: the ramp");
            Expect(raised.IsSupersetOf(ramps), $"seed {seed}: the ramp is raised ground");
            Assert.That(Reachable(new List<Vector3Int>(raised)[0], raised.Contains).Count, Is.EqualTo(raised.Count), $"seed {seed}: one piece");
            foreach (Vector3Int cell in raised)
            {
                Expect(grass.Contains(cell) && !coastCells.Contains(cell), $"seed {seed}: {cell} on the grass off the coast");
                Assert.That(NearestDistance(cell, coastCells), Is.GreaterThan(coast.PlateauMargin), $"seed {seed}: {cell} near the coast");
                Assert.That(NearestDistance(cell, paths), Is.GreaterThan(coast.PlateauMargin), $"seed {seed}: {cell} near a path");
                Assert.That(Vector3Int.Distance(cell, layout.SpawnCell), Is.GreaterThan(coast.SpawnClearRadius + coast.PlateauMargin), $"seed {seed}: {cell} near the spawn");
                Expect(InRun(raised, cell, Vector3Int.right) && InRun(raised, cell, Vector3Int.up), $"seed {seed}: {cell} three cells thick");
            }

            int left = int.MaxValue;
            int right = int.MinValue;
            HashSet<Vector3Int> clear = new(CellComparer.Instance);
            foreach (Vector3Int ramp in ramps)
            {
                Expect(!(raised.Contains(ramp + Vector3Int.down)), $"seed {seed}: the ramp at {ramp} on the bottom row");
                Assert.That(ramp.y, Is.EqualTo(new List<Vector3Int>(ramps)[0].y), $"seed {seed}: the ramp in one row");
                left = Mathf.Min(left, ramp.x);
                right = Mathf.Max(right, ramp.x);
                for (int up = 0; up <= faceRows; up++)
                {
                    clear.Add(ramp + (Vector3Int.up * up));
                }

                for (int below = 1; below <= 4; below++)
                {
                    for (int side = -1; side <= 1; side++)
                    {
                        clear.Add(ramp + new Vector3Int(side, -below, 0));
                    }
                }

                for (int below = 1; below <= 2; below++)
                {
                    Vector3Int approach = ramp + new Vector3Int(0, -below, 0);
                    Expect(grass.Contains(approach) && !ocean.Contains(approach) && !raised.Contains(approach), $"seed {seed}: the ground in front of the ramp at {approach}");
                }
            }

            Assert.That(right - left + 1, Is.EqualTo(coast.RampWidth), $"seed {seed}: the ramp is one run");
            int row = new List<Vector3Int>(ramps)[0].y;
            foreach (Vector3Int beside in new[] { new Vector3Int(left - 1, row, 0), new Vector3Int(right + 1, row, 0) })
            {
                Expect(RaisedGround.IsCliff(raised, ramps, beside), $"seed {seed}: cliff beside the ramp at {beside}");
            }

            foreach (Vector3Int cell in raised)
            {
                if (RaisedGround.IsOnFace(raised, ramps, cell, faceRows))
                {
                    clear.Add(cell);
                }
            }

            HashSet<Vector3Int> footprints = new(CellComparer.Instance);
            foreach (AreaLayout.Prop prop in layout.Props)
            {
                if (!PropGridSnapping.TryGetFootprint(prop.Prefab, out Rect footprint))
                {
                    Expect(!clear.Contains(prop.Cell), $"seed {seed}: {prop.Prefab.name} at {prop.Cell} on the face or by the ramp");
                    continue;
                }

                List<Vector3Int> covered = CoveredCells(prop, footprint);
                int on = 0;
                foreach (Vector3Int cell in covered)
                {
                    footprints.Add(cell);
                    on += raised.Contains(cell) ? 1 : 0;
                    Expect(!clear.Contains(cell), $"seed {seed}: {prop.Prefab.name} at {cell} on the face or by the ramp");
                }

                Expect(on == 0 || on == covered.Count, $"seed {seed}: {prop.Prefab.name} at {prop.Cell} across the plateau's edge");
            }

            foreach (AreaLayout.Mob mob in layout.Mobs)
            {
                Expect(!RaisedGround.IsCliff(raised, ramps, mob.Cell), $"seed {seed}: a mob on the cliff at {mob.Cell}");
            }

            System.Predicate<Vector3Int> open = cell => grass.Contains(cell) && !footprints.Contains(cell) && !ocean.Contains(cell) && !RaisedGround.IsCliff(raised, ramps, cell);
            HashSet<Vector3Int> reachable = Reachable(layout.SpawnCell, open);
            foreach (Vector3Int ramp in ramps)
            {
                Expect(reachable.Contains(ramp) && reachable.Contains(ramp + Vector3Int.up), $"seed {seed}: the ramp at {ramp} and the top above it reached");
            }

            HashSet<Vector3Int> withoutRamp = Reachable(layout.SpawnCell, cell => open(cell) && !ramps.Contains(cell));
            foreach (Vector3Int cell in raised)
            {
                Expect(!withoutRamp.Contains(cell), $"seed {seed}: the plateau at {cell} reached without the ramp");
            }

            int coveredRows = RaisedGround.RowsCovered(coast.TerrainStyle.CliffHeight / TerrainStyle.PixelsPerCell);
            System.Predicate<Vector3Int> offTheFace = cell => raised.Contains(cell) && !RaisedGround.IsOnFace(raised, ramps, cell, coveredRows);
            HashSet<Vector3Int> fromTheRamp = Reachable(new List<Vector3Int>(ramps)[0], offTheFace);
            foreach (Vector3Int cell in raised)
            {
                Expect(!offTheFace(cell) || fromTheRamp.Contains(cell), $"seed {seed}: the top at {cell} cut off from the ramp by the face");
            }
        }
    }

    /// <summary>
    /// On the layers the writer sets up for the Coast, the beach and the ocean reach navigation through their tilemaps'
    /// terrain sources at their draw order's priorities, and the default movement profile's rules: stepping onto a
    /// beach cell with no blocked cell beside it costs grass's 10, every cell of the ocean is blocked, so no unit walks
    /// onto the sea or off the map's northern edge (the top cell of every column of the middle three quarters is
    /// blocked), and every beach cell is joined to the spawn; the plateau's cells are walkable but for prop footprints
    /// and the two rows its 64 px cliff face covers whole (but above the ramp), its cliff stops every step up its face but
    /// the ramp's, and the slope above the ramp and the top above that are joined to the spawn.
    /// </summary>
    [Test]
    public void TheWriter_WalksTheCoastsBeach_AndBlocksItsOcean()
    {
        Grid grid = CreateAreaWithDualGridGrass(coast);
        Track(new GameObject(PropsRootName));
        AreaLayout layout = Layout(coast, 1);
        Assert.That(AreaLayoutWriter.Write(grid, coast, layout), Is.True);
        NavigationGrid2D navigation = grid.GetComponent<NavigationGrid2D>();
        navigation.ConfigureObstacleLayers(LayerMask.GetMask("Obstacles"));
        TerrainMovementProfile2D profile = AssetDatabase.LoadAssetAtPath<TerrainMovementProfile2D>(ProfilePath);
        navigation.BuildGrid();

        foreach (string name in new[] { "Sand", "Ocean" })
        {
            NavigationTerrainSource2D source = grid.transform.Find(Layer(coast, name).TilemapName).GetComponent<NavigationTerrainSource2D>();
            Assert.That(source.TerrainType, Is.SameAs(ProjectTerrainType(name)), name);
            Assert.That(source.Priority, Is.EqualTo(layout.IndexOf(ProjectMaterial(name))), name);
        }

        HashSet<Vector3Int> grass = Cells(layout, "Grass");
        HashSet<Vector3Int> sand = Cells(layout, "Sand");
        HashSet<Vector3Int> ocean = Cells(layout, "Ocean");
        HashSet<Vector3Int> paths = PathCells(layout);
        HashSet<Vector3Int> blocked = new(ocean, CellComparer.Instance);
        blocked.UnionWith(Footprints(layout));
        foreach (Vector3Int cell in ocean)
        {
            Expect(!navigation.IsCellWalkable(cell, profile), $"the ocean at {cell}");
        }

        int steps = 0;
        foreach (Vector3Int cell in sand)
        {
            bool open = !paths.Contains(cell);
            foreach (Vector3Int near in CellsWithin(cell, 1))
            {
                open &= !blocked.Contains(near) && grass.Contains(near);
            }

            if (open)
            {
                Assert.That(navigation.MovementCost(cell + Vector3Int.down, cell, profile), Is.EqualTo(10), $"stepping onto the beach at {cell}");
                steps++;
            }
        }

        Assert.That(steps, Is.GreaterThan(50));
        BoundsInt bounds = BoundsOf(grass);
        int margin = bounds.size.x / 8;
        for (int x = bounds.xMin + margin; x < bounds.xMax - margin; x++)
        {
            int y = bounds.yMax - 1;
            while (!grass.Contains(new Vector3Int(x, y, 0)))
            {
                y--;
            }

            Expect(!navigation.IsCellWalkable(new Vector3Int(x, y, 0), profile), $"the map's northern edge at column {x}");
        }

        foreach (Vector3Int cell in sand)
        {
            Expect(navigation.AreCellsConnected(layout.SpawnCell, cell, profile), $"the beach at {cell} reachable");
        }

        int faceRows = RaisedGround.RowsCovered(coast.TerrainStyle.CliffHeight / TerrainStyle.PixelsPerCell);
        Assert.That(faceRows, Is.EqualTo(2), "The project's cliff face covers two rows.");
        int faces = 0;
        int covered = 0;
        foreach (Vector3Int cell in layout.Raised)
        {
            bool onFace = RaisedGround.IsOnFace(layout.Raised, layout.Ramps, cell, faceRows);
            covered += onFace ? 1 : 0;
            Expect(navigation.IsCellWalkable(cell, profile) == !(blocked.Contains(cell) || onFace), $"raised ground at {cell}");
            if (!layout.Raised.Contains(cell + Vector3Int.down) && !layout.Ramps.Contains(cell))
            {
                Expect(navigation.CrossesCliff(cell + Vector3Int.down, cell), $"the face at {cell}");
                faces++;
            }
        }

        Assert.That(faces, Is.GreaterThan(3));
        Assert.That(covered, Is.EqualTo(faces * 2), "Every column of the face covers two cells.");
        foreach (Vector3Int ramp in layout.Ramps)
        {
            Expect(navigation.IsCellWalkable(ramp, profile), $"the ramp at {ramp}");
            Expect(!navigation.CrossesCliff(ramp + Vector3Int.down, ramp), $"up the ramp at {ramp}");
            Expect(navigation.AreCellsConnected(layout.SpawnCell, ramp + Vector3Int.up, profile), $"the slope above the ramp at {ramp} reachable");
            Expect(navigation.AreCellsConnected(layout.SpawnCell, ramp + (Vector3Int.up * 2), profile), $"the top above the ramp at {ramp} reachable");
        }
    }

    /// <summary>
    /// For every Lava Fields seed, with no warnings: every layer has cells and lies on the dirt; the lava keeps a cell of
    /// earth from every path (no path cell within a step of it, 8 neighbours) and stays out of the spawn's clear disc and
    /// the cell beyond it, in pieces of four cells or more, with no island of earth it encloses; it lies both north and
    /// south of the road, which crosses the river on earth; and no third look shows under a rounded corner around the
    /// lava or the trail away from the map's edge (TerrainTypes.md, Phase 3's Corners note).
    /// </summary>
    [Test]
    public void LavaFieldsLayouts_LayARiverAndPools_ClearOfThePathsAndTheSpawn()
    {
        for (int seed = 1; seed <= SeedCount; seed++)
        {
            AreaLayout layout = Layout(lavaFields, seed);
            Assert.That(layout.Warnings, Is.Empty, $"seed {seed}: {string.Join("; ", layout.Warnings)}");
            HashSet<Vector3Int> dirt = Cells(layout, "Dirt");
            for (int layer = 0; layer < layout.TerrainLayerCount; layer++)
            {
                Assert.That(layout.TerrainCells(layer), Is.Not.Empty, $"seed {seed}: layer {layer}");
                Expect(layout.TerrainCells(layer).IsSubsetOf(dirt), $"seed {seed}: layer {layer} lies on the dirt");
            }

            HashSet<Vector3Int> lava = Cells(layout, "Lava");
            HashSet<Vector3Int> paths = PathCells(layout);
            float roadMiddle = 0f;
            HashSet<Vector3Int> stone = Cells(layout, "Stone");
            foreach (Vector3Int cell in stone)
            {
                roadMiddle += cell.y;
            }

            roadMiddle /= stone.Count;
            bool north = false;
            bool south = false;
            foreach (Vector3Int cell in lava)
            {
                foreach (Vector3Int near in CellsWithin(cell, 1))
                {
                    Expect(!paths.Contains(near), $"seed {seed}: lava at {cell} beside the path at {near}");
                }

                Assert.That(Vector3Int.Distance(cell, layout.SpawnCell), Is.GreaterThan(lavaFields.SpawnClearRadius + 1f), $"seed {seed}: lava at {cell} near the spawn");
                north |= cell.y > roadMiddle;
                south |= cell.y < roadMiddle;
            }

            Expect(north && south, $"seed {seed}: the lava lies on both sides of the road");
            HashSet<Vector3Int> seen = new(CellComparer.Instance);
            foreach (Vector3Int cell in lava)
            {
                if (seen.Contains(cell))
                {
                    continue;
                }

                HashSet<Vector3Int> piece = Reachable(cell, lava.Contains);
                seen.UnionWith(piece);
                Assert.That(piece.Count, Is.GreaterThanOrEqualTo(4), $"seed {seed}: a lava piece at {cell}");
            }

            HashSet<Vector3Int> land = Reachable(layout.SpawnCell, cell => dirt.Contains(cell) && !lava.Contains(cell));
            foreach (Vector3Int cell in dirt)
            {
                if (!lava.Contains(cell) && !land.Contains(cell))
                {
                    HashSet<Vector3Int> island = Reachable(cell, other => dirt.Contains(other) && !lava.Contains(other));
                    Assert.That(island.Count, Is.GreaterThanOrEqualTo(12), $"seed {seed}: an island of earth at {cell}");
                }
            }

            Assert.That(InlandCornerProblems(layout, "Lava", "Gravel"), Is.Empty, $"seed {seed}");
        }
    }

    /// <summary>
    /// For every Lava Fields seed the trail is one 4-connected piece that leaves the road and ends a cell of earth from
    /// the lava (a trail cell two steps from it, none closer); the campsite has one campfire, further from the lava than
    /// its radius plus a cell, and no walk-through prop but its seats within its radius less a cell and a half of it.
    /// </summary>
    [Test]
    public void LavaFieldsLayouts_RunAnUnbrokenTrailToTheLava_AndKeepTheCampsiteOffIt()
    {
        for (int seed = 1; seed <= SeedCount; seed++)
        {
            AreaLayout layout = Layout(lavaFields, seed);
            HashSet<Vector3Int> lava = Cells(layout, "Lava");
            HashSet<Vector3Int> gravel = Cells(layout, "Gravel");
            HashSet<Vector3Int> stone = Cells(layout, "Stone");
            Vector3Int any = default;
            bool leavesTheRoad = false;
            bool reachesTheLava = false;
            foreach (Vector3Int cell in gravel)
            {
                any = cell;
                foreach (Vector3Int side in Sides)
                {
                    leavesTheRoad |= stone.Contains(cell + side);
                }

                foreach (Vector3Int near in CellsWithin(cell, 2))
                {
                    reachesTheLava |= lava.Contains(near);
                }
            }

            Expect(leavesTheRoad, $"seed {seed}: the trail leaves the road");
            Expect(reachesTheLava, $"seed {seed}: the trail ends near the lava");
            Assert.That(Reachable(any, gravel.Contains).Count, Is.EqualTo(gravel.Count), $"seed {seed}: the trail is one piece");
            int campfires = 0;
            foreach (AreaLayout.Prop prop in layout.Props)
            {
                if (prop.Prefab != lavaFields.CampfirePrefab)
                {
                    continue;
                }

                campfires++;
                Assert.That(NearestDistance(prop.Cell, lava), Is.GreaterThan(lavaFields.CampsiteRadius + 1f), $"seed {seed}: the campfire near the lava");
                foreach (AreaLayout.Prop other in layout.Props)
                {
                    if (other.Prefab != lavaFields.CampfirePrefab && !ListContains(lavaFields.CampsiteSeats, other.Prefab) && !PropGridSnapping.TryGetFootprint(other.Prefab, out _))
                    {
                        Assert.That(Vector3Int.Distance(other.Cell, prop.Cell), Is.GreaterThan(lavaFields.CampsiteRadius - 1.5f), $"seed {seed}: {other.Prefab.name} at {other.Cell} on the campsite");
                    }
                }
            }

            Assert.That(campfires, Is.EqualTo(1), $"seed {seed}: campfires");
        }
    }

    /// <summary>
    /// For every Lava Fields seed blocking footprints cover dirt neither on the lava nor beside it, apart; walk-through
    /// props stand on dirt off the lava and outside every footprint; the spawn is on the road with no footprint within
    /// its clear radius; every mob stands two steps or more from the lava, far enough from the spawn and from each
    /// other; and the spawn can walk to every mob and every trail cell around the footprints and the lava, so the road
    /// crosses the river.
    /// </summary>
    [Test]
    public void LavaFieldsLayouts_KeepPropsOffTheLava_AndLetTheSpawnReachTheTrailAndEveryMob()
    {
        for (int seed = 1; seed <= SeedCount; seed++)
        {
            AreaLayout layout = Layout(lavaFields, seed);
            HashSet<Vector3Int> dirt = Cells(layout, "Dirt");
            HashSet<Vector3Int> lava = Cells(layout, "Lava");
            HashSet<Vector3Int> nearLava = new(CellComparer.Instance);
            foreach (Vector3Int cell in lava)
            {
                nearLava.UnionWith(CellsWithin(cell, 1));
            }

            HashSet<Vector3Int> footprints = new(CellComparer.Instance);
            foreach (AreaLayout.Prop prop in layout.Props)
            {
                if (!PropGridSnapping.TryGetFootprint(prop.Prefab, out Rect footprint))
                {
                    Expect(dirt.Contains(prop.Cell) && !lava.Contains(prop.Cell), $"seed {seed}: {prop.Prefab.name} at {prop.Cell}");
                    continue;
                }

                foreach (Vector3Int cell in CoveredCells(prop, footprint))
                {
                    Expect(footprints.Add(cell), $"seed {seed}: footprints overlap at {cell}");
                    Expect(dirt.Contains(cell) && !nearLava.Contains(cell), $"seed {seed}: {prop.Prefab.name} at {cell} on or beside the lava");
                }
            }

            foreach (AreaLayout.Prop prop in layout.Props)
            {
                if (!PropGridSnapping.TryGetFootprint(prop.Prefab, out _))
                {
                    Expect(!footprints.Contains(prop.Cell), $"seed {seed}: {prop.Prefab.name} under a footprint");
                }
            }

            Expect(Cells(layout, "Stone").Contains(layout.SpawnCell), $"seed {seed}: the spawn is on the road");
            foreach (Vector3Int cell in footprints)
            {
                Assert.That(Vector3Int.Distance(cell, layout.SpawnCell), Is.GreaterThan(lavaFields.SpawnClearRadius), $"seed {seed}: a footprint at {cell} near the spawn");
            }

            HashSet<Vector3Int> reachable = Reachable(layout.SpawnCell, cell => dirt.Contains(cell) && !footprints.Contains(cell) && !lava.Contains(cell));
            Assert.That(layout.Mobs.Count, Is.EqualTo(lavaFields.MobCount), $"seed {seed}: mobs");
            for (int i = 0; i < layout.Mobs.Count; i++)
            {
                AreaLayout.Mob mob = layout.Mobs[i];
                Expect(!nearLava.Contains(mob.Cell), $"seed {seed}: a mob at {mob.Cell} on or beside the lava");
                Expect(reachable.Contains(mob.Cell), $"seed {seed}: mob at {mob.Cell} unreachable");
                Assert.That(Vector3Int.Distance(mob.Cell, layout.SpawnCell), Is.GreaterThanOrEqualTo(lavaFields.MobDistanceFromSpawn), $"seed {seed}");
                for (int j = i + 1; j < layout.Mobs.Count; j++)
                {
                    Assert.That(Vector3Int.Distance(mob.Cell, layout.Mobs[j].Cell), Is.GreaterThanOrEqualTo(lavaFields.MobSpacing), $"seed {seed}");
                }
            }

            foreach (Vector3Int cell in Cells(layout, "Gravel"))
            {
                Expect(reachable.Contains(cell), $"seed {seed}: the trail at {cell} unreachable");
            }

            foreach (Vector3Int cell in Cells(layout, "Stone"))
            {
                Expect(reachable.Contains(cell) || footprints.Contains(cell), $"seed {seed}: the road at {cell} unreachable");
            }
        }
    }

    /// <summary>
    /// On the layers the writer sets up for the Lava Fields, lava reaches navigation through its tilemap's terrain source
    /// at its draw order's priority, the last, and the default movement profile's rule: every lava cell is blocked,
    /// stepping onto open earth costs grass's 10, and every mob and every trail cell is joined to the spawn.
    /// </summary>
    [Test]
    public void TheWriter_BlocksTheLavaFieldsLava()
    {
        Grid grid = CreateAreaWithDualGridGrass(lavaFields);
        Track(new GameObject(PropsRootName));
        AreaLayout layout = Layout(lavaFields, 1);
        Assert.That(AreaLayoutWriter.Write(grid, lavaFields, layout), Is.True);
        NavigationGrid2D navigation = grid.GetComponent<NavigationGrid2D>();
        navigation.ConfigureObstacleLayers(LayerMask.GetMask("Obstacles"));
        TerrainMovementProfile2D profile = AssetDatabase.LoadAssetAtPath<TerrainMovementProfile2D>(ProfilePath);
        navigation.BuildGrid();

        NavigationTerrainSource2D source = grid.transform.Find(Layer(lavaFields, "Lava").TilemapName).GetComponent<NavigationTerrainSource2D>();
        Assert.That(source.TerrainType, Is.SameAs(ProjectTerrainType("Lava")));
        Assert.That(source.Priority, Is.EqualTo(layout.TerrainLayerCount - 1));
        HashSet<Vector3Int> dirt = Cells(layout, "Dirt");
        HashSet<Vector3Int> lava = Cells(layout, "Lava");
        HashSet<Vector3Int> paths = PathCells(layout);
        HashSet<Vector3Int> blocked = new(lava, CellComparer.Instance);
        blocked.UnionWith(Footprints(layout));
        foreach (Vector3Int cell in lava)
        {
            Expect(!navigation.IsCellWalkable(cell, profile), $"lava at {cell}");
        }

        int steps = 0;
        foreach (Vector3Int cell in dirt)
        {
            bool open = !paths.Contains(cell) && !lava.Contains(cell) && !paths.Contains(cell + Vector3Int.down);
            foreach (Vector3Int near in CellsWithin(cell, 1))
            {
                open &= !blocked.Contains(near) && dirt.Contains(near);
            }

            foreach (Vector3Int near in CellsWithin(cell + Vector3Int.down, 1))
            {
                open &= !blocked.Contains(near) && dirt.Contains(near);
            }

            if (open && steps < 200)
            {
                Assert.That(navigation.MovementCost(cell + Vector3Int.down, cell, profile), Is.EqualTo(10), $"stepping onto the earth at {cell}");
                steps++;
            }
        }

        Assert.That(steps, Is.GreaterThan(50));
        foreach (AreaLayout.Mob mob in layout.Mobs)
        {
            Expect(navigation.AreCellsConnected(layout.SpawnCell, mob.Cell, profile), $"mob at {mob.Cell}");
        }

        foreach (Vector3Int cell in Cells(layout, "Gravel"))
        {
            Expect(navigation.AreCellsConnected(layout.SpawnCell, cell, profile), $"the trail at {cell}");
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
        return CornerProblems(layout, true, materials);
    }

    /// <summary>
    /// The corners <see cref="CornerProblems(AreaLayout, string[])"/> finds, less those on the map's edge (a corner with
    /// a cell off the ground), where every band that runs out to the edge meets the next one with such a corner
    /// (Architecture.md's Known gaps).
    /// </summary>
    private static List<string> InlandCornerProblems(AreaLayout layout, params string[] materials)
    {
        return CornerProblems(layout, false, materials);
    }

    /// <summary>
    /// The corners of <paramref name="layout"/> around the cells of <paramref name="materials"/> where a third look
    /// shows under a rounded corner, those on the map's edge only when <paramref name="atMapEdge"/>.
    /// </summary>
    private static List<string> CornerProblems(AreaLayout layout, bool atMapEdge, string[] materials)
    {
        Dictionary<Vector3Int, int> top = new(CellComparer.Instance);
        HashSet<Vector3Int> corners = new(CellComparer.Instance);
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

            if (!atMapEdge && System.Array.IndexOf(tops, -1) >= 0)
            {
                continue;
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
    /// footprint covers it and no path lies within the clearance, but the dock's torch's (Docs/Lighting.md, Phase 6),
    /// which stands by the dock's foot, the beach included; each walk-through prop stands on such grass off the paths,
    /// not beside the water and outside every footprint.
    /// </summary>
    [Test]
    public void LakesideLayouts_KeepPropsOffTheWaterTheBeachAndTheMud()
    {
        for (int seed = 1; seed <= SeedCount; seed++)
        {
            AreaLayout layout = Layout(lakeside, seed);
            HashSet<Vector3Int> grass = Cells(layout, "Grass");
            HashSet<Vector3Int> water = Cells(layout, "Water");
            HashSet<Vector3Int> sand = Cells(layout, "Sand");
            HashSet<Vector3Int> mud = Cells(layout, "Mud");
            HashSet<Vector3Int> paths = PathCells(layout);
            HashSet<Vector3Int> footprints = new(CellComparer.Instance);
            foreach (AreaLayout.Prop prop in layout.Props)
            {
                if (!PropGridSnapping.TryGetFootprint(prop.Prefab, out Rect footprint))
                {
                    continue;
                }

                foreach (Vector3Int cell in CoveredCells(prop, footprint))
                {
                    Expect(footprints.Add(cell), $"seed {seed}: two footprints on {cell}");
                    if (prop.Prefab == lakeside.DockTorchPrefab)
                    {
                        continue;
                    }

                    Expect(grass.Contains(cell) && !water.Contains(cell) && !sand.Contains(cell) && !mud.Contains(cell), $"seed {seed}: {prop.Prefab.name} on open grass at {cell}");
                    foreach (Vector3Int near in CellsWithin(cell, lakeside.PathClearance))
                    {
                        Expect(!paths.Contains(near), $"seed {seed}: a prop at {cell} beside a path");
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

                Expect(grass.Contains(prop.Cell) && !sand.Contains(prop.Cell) && !mud.Contains(prop.Cell) && !paths.Contains(prop.Cell), $"seed {seed}: {prop.Prefab.name} at {prop.Cell}");
                Expect(!footprints.Contains(prop.Cell), $"seed {seed}: {prop.Prefab.name} under a footprint");
                foreach (Vector3Int near in CellsWithin(prop.Cell, 1))
                {
                    Expect(!water.Contains(near), $"seed {seed}: {prop.Prefab.name} at {prop.Cell} beside the water");
                }
            }
        }
    }

    /// <summary>
    /// For every Lakeside seed the spawn can walk (4-connected, around the footprints and the water) to every beach
    /// cell but the one the dock's torch stands on, and to all the configured mobs, which stand on dry ground far enough
    /// from the spawn and from each other.
    /// </summary>
    [Test]
    public void LakesideLayouts_LetTheSpawnWalkToTheBeachAndEveryMob()
    {
        for (int seed = 1; seed <= SeedCount; seed++)
        {
            AreaLayout layout = Layout(lakeside, seed);
            HashSet<Vector3Int> grass = Cells(layout, "Grass");
            HashSet<Vector3Int> water = Cells(layout, "Water");
            HashSet<Vector3Int> footprints = Footprints(layout);
            HashSet<Vector3Int> reachable = Reachable(layout.SpawnCell, cell => grass.Contains(cell) && !water.Contains(cell) && !footprints.Contains(cell));

            HashSet<Vector3Int> beach = new(Cells(layout, "Sand"), CellComparer.Instance);
            beach.ExceptWith(footprints);
            Expect(reachable.IsSupersetOf(beach), $"seed {seed}: the whole beach can be walked to");
            Assert.That(layout.Mobs.Count, Is.EqualTo(lakeside.MobCount), $"seed {seed}");
            for (int i = 0; i < layout.Mobs.Count; i++)
            {
                AreaLayout.Mob mob = layout.Mobs[i];
                Assert.That(mob.Prefab, Is.SameAs(lakeside.MobPrefab));
                Expect(reachable.Contains(mob.Cell), $"seed {seed}: mob at {mob.Cell} reachable from the spawn");
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
    [TestCase(CoastGeneratorPath)]
    [TestCase(LavaFieldsGeneratorPath)]
    public void TheSameSeed_GivesTheSameLayout_AndAnotherSeedAnotherOne(string generatorPath)
    {
        AreaGenerator target = ProjectCopy<AreaGenerator>(generatorPath);
        AreaLayout first = target.Generate(7);
        AreaLayout again = target.Generate(7);
        AreaLayout other = target.Generate(8);

        for (int layer = 0; layer < first.TerrainLayerCount; layer++)
        {
            Expect(first.TerrainCells(layer).SetEquals(again.TerrainCells(layer)), $"layer {layer}");
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
        Expect(again.Raised.SetEquals(first.Raised), $"raised ground");
        Expect(again.Ramps.SetEquals(first.Ramps), $"ramps");
        Assert.That(other.TerrainCells(0).SetEquals(first.TerrainCells(0)) && Cells(other, "Stone").SetEquals(Cells(first, "Stone")), Is.False);
    }

    /// <summary>
    /// For each generator, writing a layout into a scene whose ground layer (grass, the Snowfield's snow or the Lava
    /// Fields' dirt) is still a
    /// dual-grid layer sets every layer
    /// up as a plain data tilemap at its place under the grid (no module, render tilemap or Tilemap Renderer left),
    /// holding the layer's tile in exactly the layout's cells, with a navigation source of its terrain type whose
    /// priority is its index; the grid's terrain renderer draws the layers in that order with their materials and the
    /// generator's style, and the area's terrain fills the grass layer with its tile. The old props and mob spawn
    /// points are replaced; a generator with raised ground gets its raised tilemap,
    /// ramps included, drawn by the renderer and read by the navigation grid; the navigation grid then blocks exactly
    /// the footprint cells, the water, the ocean, lava and the cells the cliff face of raised ground covers whole at the
    /// style's cliff height, the cliff stops steps up its face, and every mob can walk to the spawn.
    /// </summary>
    [TestCase(ClearingGeneratorPath)]
    [TestCase(VillageForestGeneratorPath)]
    [TestCase(LakesideGeneratorPath)]
    [TestCase(SnowfieldGeneratorPath)]
    [TestCase(CoastGeneratorPath)]
    [TestCase(LavaFieldsGeneratorPath)]
    public void TheWriter_SetsUpPlainLayersDrawnByTheRenderer_ReplacesPropsAndMobSpawns_AndTheGridBlocksTheFootprints(string generatorPath)
    {
        AreaGenerator target = ProjectCopy<AreaGenerator>(generatorPath);
        Grid grid = CreateAreaWithDualGridGrass(target);
        AreaTerrain terrain = grid.gameObject.AddComponent<AreaTerrain>();
        terrain.Configure(new AreaTerrain.Layer(grid.transform.Find(target.TerrainLayers[0].TilemapName).GetComponent<Tilemap>(), null));
        Transform props = Track(new GameObject(PropsRootName)).transform;
        GameObject stale = new("Stale");
        stale.transform.SetParent(props, false);
        GameObject oldMob = Track(new GameObject("OldMobSpawn"));
        oldMob.AddComponent<MobSpawnPoint>();
        AreaLayout layout = Layout(target, 3);

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
            Expect(FilledCells(tilemap).SetEquals(layout.TerrainCells(i)), $"{layers[i].TilemapName} cells");
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

        Expect(stale == null, $"Old props are removed.");
        Expect(oldMob == null, $"Old mob spawn points are removed.");
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

        NavigationGrid2D navigation = grid.GetComponent<NavigationGrid2D>();
        navigation.ConfigureObstacleLayers(LayerMask.GetMask("Obstacles"));
        navigation.BuildGrid();
        TerrainMovementProfile2D profile = AssetDatabase.LoadAssetAtPath<TerrainMovementProfile2D>(ProfilePath);
        HashSet<Vector3Int> footprints = Footprints(layout);
        HashSet<Vector3Int> water = new(CellsOrNone(layout, "Water"), CellComparer.Instance);
        water.UnionWith(CellsOrNone(layout, "Ocean"));
        water.UnionWith(CellsOrNone(layout, "Lava"));
        water.ExceptWith(CellsOrNone(layout, "Planks"));
        Transform raisedChild = grid.transform.Find(AreaGenerator.RaisedTilemapName);
        if (target.RaisedTile == null)
        {
            Assert.That(layout.Raised, Is.Empty, "No raised ground without a raised tile.");
            Assert.That(raisedChild, Is.Null, "No raised tilemap without raised ground.");
        }
        else
        {
            Tilemap raisedMap = raisedChild.GetComponent<Tilemap>();
            Expect(FilledCells(raisedMap).SetEquals(layout.Raised), $"The raised tilemap holds the raised ground.");
            foreach (Vector3Int cell in layout.Raised)
            {
                Assert.That(raisedMap.GetTile(cell), Is.SameAs(layout.Ramps.Contains(cell) ? target.RampTile : target.RaisedTile), $"raised ground at {cell}");
            }

            Assert.That(renderer.RaisedTilemap, Is.SameAs(raisedMap), "The renderer draws the raised ground.");
            Assert.That(navigation.RaisedTilemap, Is.SameAs(raisedMap), "The navigation grid reads its cliff.");
            foreach (Vector3Int cell in layout.Raised)
            {
                Vector3Int below = cell + Vector3Int.down;
                if (!layout.Raised.Contains(below))
                {
                    Assert.That(navigation.CrossesCliff(below, cell), Is.EqualTo(!layout.Ramps.Contains(cell)), $"the step up to {cell}");
                }
            }
        }

        int faceRows = RaisedGround.RowsCovered(target.TerrainStyle.CliffHeight / TerrainStyle.PixelsPerCell);
        foreach (Vector3Int cell in layout.TerrainCells(0))
        {
            bool onFace = RaisedGround.IsOnFace(layout.Raised, layout.Ramps, cell, faceRows);
            Assert.That(navigation.IsCellWalkable(cell, profile), Is.EqualTo(!footprints.Contains(cell) && !water.Contains(cell) && !onFace), $"cell {cell}");
        }

        foreach (AreaLayout.Mob mob in layout.Mobs)
        {
            Expect(navigation.AreCellsConnected(layout.SpawnCell, mob.Cell, profile), $"mob at {mob.Cell}");
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
        AreaLayout layout = Layout(lakeside, 3);
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

        Expect(Cells(layout, "Stone").Contains(road), $"The spawn is on the road.");
        Expect(!navigation.IsCellWalkable(road, profile), $"Water over the road blocks it.");
        Expect(navigation.IsCellWalkable(beside, profile), $"Mud can be walked.");
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
        AreaLayout layout = Layout(lakeside, 1);
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
            Expect(navigation.IsCellWalkable(cell, profile), $"shallows at {cell}");
            Expect(navigation.AreCellsConnected(layout.SpawnCell, cell, profile), $"shallows at {cell} reachable");
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
    /// blocked cell around it costs grass's 10. Runs the project's generators, without props, since the test props
    /// leave no tall grass along the Village Forest's dense forest edge clear of a footprint.
    /// </summary>
    [TestCase(VillageForestGeneratorPath)]
    [TestCase(LakesideGeneratorPath)]
    public void TheWriter_LetsTheMeadowsAndTallGrassBeWalkedAtGrasssCost(string generatorPath)
    {
        AreaGenerator target = AssetDatabase.LoadAssetAtPath<AreaGenerator>(generatorPath);
        Grid grid = CreateAreaWithDualGridGrass(target);
        Track(new GameObject(PropsRootName));
        AreaLayout layout = Layout(target, 1);
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

                Expect(navigation.IsCellWalkable(cell, profile), $"{name} at {cell}");
                Expect(navigation.AreCellsConnected(layout.SpawnCell, cell, profile), $"{name} at {cell} reachable");
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
    /// The writer refuses a generator of 33 layers with an error, changing nothing in the scene, and writes one of
    /// <see cref="TerrainControlMap.MaxLayers"/> (32): a tilemap per layer, drawn by a renderer that builds them all
    /// (wide, past 16), each layer's cell holding its own bit alone. The layers reuse the Clearing's grass tile and terrain type under
    /// their own tilemap names, each drawn with its own material made for the test, in a style whose layer order lists
    /// those materials.
    /// </summary>
    [Test]
    public void TheWriter_WritesThirtyTwoLayers_AndRefusesThirtyThree()
    {
        Grid grid = Track(new GameObject("Grid")).AddComponent<Grid>();
        Track(new GameObject(PropsRootName));
        AreaGenerator.TerrainLayer source = generator.TerrainLayers[0];
        TerrainMaterial[] order = new TerrainMaterial[TerrainControlMap.MaxLayers + 1];
        List<AreaGenerator.TerrainLayer> layers = new();
        for (int i = 0; i < order.Length; i++)
        {
            order[i] = TestTerrainStyle.PlainMaterial($"Material{i}", TerrainMaterial.CellMode.None, created);
            layers.Add(new AreaGenerator.TerrainLayer($"Layer{i}Tilemap", source.Tile, source.TerrainType, order[i]));
        }

        TerrainStyle style = TestTerrainStyle.Create(created, order);
        TestAreaGenerator tooMany = Track(TestAreaGenerator.Create(style, layers));
        LogAssert.Expect(LogType.Error, "TestGenerator: 33 terrain layers, but the blended terrain draws at most 32.");
        Assert.That(AreaLayoutWriter.Write(grid, tooMany, tooMany.Generate(1)), Is.False);
        Assert.That(grid.transform.childCount, Is.EqualTo(0), "Nothing is written.");
        Assert.That(grid.GetComponent<TerrainBlendRenderer>(), Is.Null);

        layers.RemoveAt(TerrainControlMap.MaxLayers);
        TestAreaGenerator most = Track(TestAreaGenerator.Create(style, layers));
        Assert.That(AreaLayoutWriter.Write(grid, most, most.Generate(1)), Is.True);

        TerrainBlendRenderer renderer = grid.GetComponent<TerrainBlendRenderer>();
        Assert.That(renderer.Layers.Count, Is.EqualTo(32));
        renderer.RunScheduledRebuild();
        Assert.That(renderer.ControlMap, Is.Not.Null, "The renderer builds 32 layers.");
        Assert.That(renderer.ControlMap.LayerCount, Is.EqualTo(32));
        Assert.That(renderer.IsWide, Is.True, "Past 16 layers the renderer builds wide.");
        for (int i = 0; i < layers.Count; i++)
        {
            Assert.That(renderer.Layers[i].Tilemap.name, Is.EqualTo(layers[i].TilemapName));
            Assert.That(renderer.ControlMap.GetShownCell(new Vector3Int(i, 0, 0)), Is.EqualTo(1u << i), layers[i].TilemapName);
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
        Expect(!navigation.IsCellWalkable(water, profile), $"Water still blocks.");
        Expect(navigation.IsCellWalkable(mud, profile), $"Mud can be walked.");
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
        AreaLayout first = Layout(villageForest, 3);
        Assert.That(AreaLayoutWriter.Write(grid, villageForest, first), Is.True);
        IReadOnlyList<AreaGenerator.TerrainLayer> layers = villageForest.TerrainLayers;
        TerrainBlendRenderer renderer = grid.GetComponent<TerrainBlendRenderer>();
        renderer.RunScheduledRebuild();
        Assert.That(renderer.ControlMap, Is.Not.Null, "The renderer the writer added builds.");
        AreaLayout second = Layout(villageForest, 4);

        Assert.That(AreaLayoutWriter.Write(grid, villageForest, second), Is.True);
        renderer.FlushPendingChanges();

        int emptied = 0;
        for (int i = 0; i < layers.Count; i++)
        {
            HashSet<Vector3Int> cells = new(first.TerrainCells(i), CellComparer.Instance);
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
    /// layer drawn in the style's layer order: grass, weeds, meadow, tall grass, dirt, sand, gravel, plaza, stone, water. The
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
        Assert.That(AreaLayoutWriter.Write(grid, villageForest, Layout(villageForest, 3)), Is.True);
        TerrainBlendRenderer renderer = grid.GetComponent<TerrainBlendRenderer>();
        Undo.IncrementCurrentGroup();
        int added = Undo.GetCurrentGroup();
        Tilemap water = AddDrawnLayer(grid, Layer(lakeside, "Water"), true);
        Tilemap sand = AddDrawnLayer(grid, Layer(lakeside, "Sand"), true);
        Tilemap dryGrass = AddDrawnLayer(grid, Layer(lakeside, "DryGrass"), false);
        TerrainBlendRendererEditor.SyncNavigationPriorities(renderer);
        Undo.CollapseUndoOperations(added);
        Assert.That(renderer.Layers.Count, Is.EqualTo(11));

        Assert.That(AreaLayoutWriter.Write(grid, villageForest, Layout(villageForest, 4)), Is.True);

        string[] expected = { "Grass", "Weeds", "Meadow", "TallGrass", "Dirt", "Sand", "Gravel", "Plaza", "Stone", "Water" };
        Assert.That(renderer.Layers.Count, Is.EqualTo(expected.Length));
        for (int i = 0; i < expected.Length; i++)
        {
            Tilemap tilemap = renderer.Layers[i].Tilemap;
            Assert.That(tilemap.name, Is.EqualTo($"{expected[i]}Tilemap"), $"layer {i}");
            Assert.That(renderer.Layers[i].Material, Is.SameAs(ProjectMaterial(expected[i])), $"layer {i}");
            Assert.That(tilemap.transform.GetSiblingIndex(), Is.EqualTo(i), $"{tilemap.name} at its place");
            Assert.That(tilemap.GetComponent<NavigationTerrainSource2D>().Priority, Is.EqualTo(TerrainBlendRenderer.NavigationPriorityOf(i)), tilemap.name);
        }

        Assert.That(renderer.Layers[5].Tilemap, Is.SameAs(sand));
        Assert.That(renderer.Layers[9].Tilemap, Is.SameAs(water));
        Expect(sand.HasTile(Vector3Int.zero) && water.HasTile(Vector3Int.zero), $"The painted layers keep their cells.");
        Expect(dryGrass == null, $"The empty dry grass layer is removed.");
        Assert.That(renderer.HasNavigationPrioritiesInStep(), Is.True);
        renderer.RunScheduledRebuild();
        Assert.That(renderer.ControlMap, Is.Not.Null);
    }

    /// <summary>
    /// The project's generators (only read) list the ground layers they use, bottom first: each one's tilemap, its
    /// automatic ground tile from Assets/Tiles/Ground (a TerrainTile keeping that material), its terrain type and the project's material of the name given, drawn
    /// with the project's terrain style; their materials are a subsequence of the style's layer order, at most 32, so
    /// the writer and Convert Ground take them: each lists only what it lays, the Clearing dirt for its campsite, the
    /// Village Forest the meadow, tall grass, dirt and the plaza, the Lakeside dry grass, the meadow and tall grass among
    /// its eleven.
    /// </summary>
    [TestCase(ClearingGeneratorPath, "Grass", "Weeds", "Dirt", "Gravel", "Stone")]
    [TestCase(VillageForestGeneratorPath, "Grass", "Weeds", "Meadow", "TallGrass", "Dirt", "Gravel", "Plaza", "Stone")]
    [TestCase(LakesideGeneratorPath, "Grass", "DryGrass", "Weeds", "Meadow", "TallGrass", "Sand", "Mud", "Gravel", "Stone", "Shallows", "Water", "Planks")]
    [TestCase(SnowfieldGeneratorPath, "Snow", "Dirt", "Ice", "Gravel", "Stone", "Water")]
    [TestCase(CoastGeneratorPath, "Grass", "Weeds", "Dirt", "Sand", "Gravel", "Stone", "Ocean")]
    [TestCase(LavaFieldsGeneratorPath, "Dirt", "Gravel", "Stone", "Lava")]
    public void TheGenerators_ListTheirOwnLayers_AsASubsequenceOfTheSharedOrder(string generatorPath, params string[] names)
    {
        AreaGenerator target = ProjectCopy<AreaGenerator>(generatorPath);
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
            Assert.That(layers[i].Tile is TerrainTile ground && ground.Material == layers[i].Material && ground.Direction == TerrainTile.Facing.Automatic, Is.True, $"{names[i]}'s tile is its material's automatic ground tile");
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
    /// stone, shallows, water, ocean, lava, planks.
    /// </summary>
    [Test]
    public void TheStyle_ListsEveryGroundMaterial_InTheSharedOrder()
    {
        TerrainStyle style = AssetDatabase.LoadAssetAtPath<TerrainStyle>(TerrainStylePath);
        string[] order = { "Grass", "DryGrass", "Weeds", "Meadow", "TallGrass", "Snow", "Dirt", "Sand", "Mud", "Ice", "Gravel", "Plaza", "Stone", "Shallows", "Water", "Ocean", "Lava", "Planks" };

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
    /// walked like grass, snow at one and a half times grass's cost, mud and the shallows at twice it, and water, the
    /// ocean and lava not at all, so the cheapest step stays grass's.
    /// </summary>
    [Test]
    public void TheDefaultProfile_WalksSandLikeGrass_MudAtAHigherCost_AndNotWater()
    {
        TerrainMovementProfile2D profile = AssetDatabase.LoadAssetAtPath<TerrainMovementProfile2D>(ProfilePath);
        profile.TryGetTraversal(ProjectTerrainType("Grass"), out bool grassWalkable, out int grassCost);
        profile.TryGetTraversal(ProjectTerrainType("Shallows"), out bool shallowsWalkable, out int shallowsCost);
        Expect(shallowsWalkable, $"The shallows can be waded.");
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
        Expect(snowWalkable, $"Snow can be walked.");
        Assert.That(snowCost, Is.EqualTo(3 * grassCost / 2), "Snow costs half as much again as grass.");
        Assert.That(mudCost, Is.GreaterThan(grassCost), "Paths go around mud when they can.");
        Expect(!waterWalkable, $"Water cannot be walked.");
        Assert.That(ProjectTerrainType("Ocean"), Is.Not.Null);
        profile.TryGetTraversal(ProjectTerrainType("Ocean"), out bool oceanWalkable, out _);
        Expect(!oceanWalkable, $"The ocean cannot be walked.");
        Assert.That(ProjectTerrainType("Lava"), Is.Not.Null);
        profile.TryGetTraversal(ProjectTerrainType("Lava"), out bool lavaWalkable, out _);
        Expect(!lavaWalkable, $"Lava cannot be walked.");
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
            AreaLayout layout = Layout(generator, seed);
            HashSet<Vector3Int> grass = Cells(layout, "Grass");
            HashSet<Vector3Int> dirt = Cells(layout, "Dirt");
            Assert.That(dirt.Count, Is.GreaterThan(10), $"seed {seed}: the campsite's dirt");
            Expect(grass.IsSupersetOf(dirt), $"seed {seed}: dirt on grass");
            Expect(!dirt.Overlaps(PathCells(layout)), $"seed {seed}: dirt off the paths");
            foreach (Vector3Int cell in dirt)
            {
                Assert.That(Vector3Int.Distance(cell, layout.SpawnCell), Is.GreaterThan(generator.SpawnClearRadius), $"seed {seed}: dirt at {cell} near the spawn");
                foreach (Vector3Int near in CellsWithin(cell, generator.BorderDepth))
                {
                    Expect(grass.Contains(near), $"seed {seed}: dirt at {cell} in the edge band");
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
                    Expect(dirt.IsSupersetOf(CoveredCells(prop, footprint)), $"seed {seed}: the campfire on the dirt");
                }
                else if (dirt.Contains(prop.Cell) && ListContains(generator.CampsiteSeats, prop.Prefab))
                {
                    seats++;
                }
                else if (!blocking)
                {
                    foreach (Vector3Int near in CellsWithin(prop.Cell, 1))
                    {
                        Expect(!dirt.Contains(near), $"seed {seed}: {prop.Prefab.name} on or beside the campsite");
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
            HashSet<Vector3Int> builtOn = new(CellComparer.Instance);
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
            Expect(grass.IsSupersetOf(plaza), $"seed {seed}: the square on grass");
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

            Expect(opensOntoStreet, $"seed {seed}: the square opens onto the street");

            Assert.That(dirt, Is.Not.Empty, $"seed {seed}: dirt along the trails");
            Expect(grass.IsSupersetOf(dirt), $"seed {seed}: dirt on grass");
            Expect(!dirt.Overlaps(PathCells(layout)), $"seed {seed}: dirt off the paths");
            Expect(!dirt.Overlaps(builtOn), $"seed {seed}: dirt under a building or a fence");
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
    /// For every seed the regions' torches and braziers stand where their regions call for them (Docs/Lighting.md,
    /// Phase 6), each on a cell of its own that no mob holds: the Lava Fields' two braziers either side of the road where
    /// the river meets it, two to seven cells off the road and the lava; the Coast's beacon on the plateau's top, off its
    /// face and its ramp, with the plateau all round it; the Lakeside's torch within three cells of the dock's foot, off
    /// the paths, the dock and the water; and the Snowfield's torch within a few cells of the trail, off the paths and two
    /// cells or more from the ice.
    /// </summary>
    [Test]
    public void TheRegionsLights_StandWhereTheirRegionsCallForThem()
    {
        for (int seed = 1; seed <= SeedCount; seed++)
        {
            AreaLayout scorched = Layout(lavaFields, seed);
            HashSet<Vector3Int> lava = Cells(scorched, "Lava");
            HashSet<Vector3Int> road = Cells(scorched, "Stone");
            List<Vector3Int> braziers = RegionLights(scorched, lavaFields);
            Assert.That(braziers.Count, Is.EqualTo(2), $"seed {seed}: the causeway's braziers");
            int north = 0;
            foreach (Vector3Int cell in braziers)
            {
                float fromLava = NearestDistance(cell, lava);
                float fromRoad = NearestDistance(cell, road);
                Expect(fromLava >= 2f && fromLava <= 7f, $"seed {seed}: the brazier at {cell} lies {fromLava} from the lava");
                Expect(fromRoad >= 2f && fromRoad <= 7f, $"seed {seed}: the brazier at {cell} lies {fromRoad} from the road");
                bool roadBelow = false;
                foreach (Vector3Int stone in road)
                {
                    roadBelow |= stone.x == cell.x && stone.y < cell.y;
                }

                north += roadBelow ? 1 : 0;
            }

            Expect(north == 1, $"seed {seed}: one brazier either side of the road");

            AreaLayout coastal = Layout(coast, seed);
            List<Vector3Int> beacons = RegionLights(coastal, coast);
            Assert.That(beacons.Count, Is.EqualTo(1), $"seed {seed}: the beacon");
            Expect(!coastal.Ramps.Contains(beacons[0]), $"seed {seed}: the beacon stands off the ramp");
            foreach (Vector3Int cell in CellsWithin(beacons[0], 1))
            {
                Expect(coastal.Raised.Contains(cell), $"seed {seed}: the beacon at {beacons[0]} has the plateau all round it");
            }

            AreaLayout lakeLayout = Layout(lakeside, seed);
            HashSet<Vector3Int> planks = Cells(lakeLayout, "Planks");
            HashSet<Vector3Int> water = Cells(lakeLayout, "Water");
            HashSet<Vector3Int> lakePaths = new(Cells(lakeLayout, "Gravel"), CellComparer.Instance);
            lakePaths.UnionWith(Cells(lakeLayout, "Stone"));
            List<Vector3Int> dockTorches = RegionLights(lakeLayout, lakeside);
            Assert.That(dockTorches.Count, Is.EqualTo(1), $"seed {seed}: the dock's torch");
            int foot = int.MaxValue;
            foreach (Vector3Int cell in planks)
            {
                foot = Mathf.Min(foot, cell.y);
            }

            Vector3Int dockTorch = dockTorches[0];
            Expect(!planks.Contains(dockTorch) && !water.Contains(dockTorch) && !lakePaths.Contains(dockTorch), $"seed {seed}: the dock's torch at {dockTorch} stands off the dock, the water and the paths");
            Expect(NearestDistance(dockTorch, planks) <= 4f && Mathf.Abs(dockTorch.y - foot) <= 3, $"seed {seed}: the dock's torch at {dockTorch} stands by the dock's foot");

            AreaLayout snowy = Layout(snowfield, seed);
            HashSet<Vector3Int> trail = Cells(snowy, "Gravel");
            HashSet<Vector3Int> ice = Cells(snowy, "Ice");
            List<Vector3Int> trailTorches = RegionLights(snowy, snowfield);
            Assert.That(trailTorches.Count, Is.EqualTo(1), $"seed {seed}: the trail's torch");
            Vector3Int trailTorch = trailTorches[0];
            Expect(!trail.Contains(trailTorch) && !Cells(snowy, "Stone").Contains(trailTorch), $"seed {seed}: the trail's torch stands off the paths");
            Expect(NearestDistance(trailTorch, trail) <= 4f, $"seed {seed}: the trail's torch at {trailTorch} stands beside the trail");
            Expect(NearestDistance(trailTorch, ice) >= 2f, $"seed {seed}: the trail's torch at {trailTorch} keeps a cell of snow from the ice");

            foreach (AreaLayout layout in new[] { scorched, coastal, lakeLayout, snowy })
            {
                foreach (AreaLayout.Mob mob in layout.Mobs)
                {
                    Expect(!braziers.Contains(mob.Cell) && !beacons.Contains(mob.Cell) && mob.Cell != dockTorch && mob.Cell != trailTorch, $"seed {seed}: a light on the mob at {mob.Cell}");
                }
            }
        }
    }

    /// <summary>
    /// The cells of the torches and braziers <paramref name="target"/> stood in <paramref name="layout"/>.
    /// </summary>
    private static List<Vector3Int> RegionLights(AreaLayout layout, AreaGenerator target)
    {
        List<Vector3Int> cells = new();
        foreach (AreaLayout.Prop prop in layout.Props)
        {
            if (IsRegionLight(target, prop.Prefab))
            {
                cells.Add(prop.Cell);
            }
        }

        return cells;
    }

    /// <summary>
    /// Whether <paramref name="prefab"/> is one of the torches and braziers <paramref name="target"/> stands in its region
    /// (Docs/Lighting.md, Phase 6).
    /// </summary>
    private static bool IsRegionLight(AreaGenerator target, GameObject prefab)
    {
        return target switch
        {
            LavaFieldsGenerator scorched => ListContains(scorched.CausewayBraziers, prefab),
            CoastGenerator coastal => prefab == coastal.BeaconPrefab,
            LakesideGenerator lake => prefab == lake.DockTorchPrefab,
            SnowfieldGenerator snowy => prefab == snowy.TrailTorchPrefab,
            _ => false
        };
    }

    /// <summary>
    /// For each generator, the terrain it lays from its detail stream at the end (the Clearing's campsite, the Village
    /// Forest's dirt, tall grass, meadows and lamp posts, the Lakeside's dry grass, shallow ponds, tall grass and
    /// meadows, the Snowfield's holes and campsite) and the torches and braziers laid after it leave the rest of the
    /// layout where it was: generated by a copy of the generator with those steps turned off, every other layer, every
    /// mob and the spawn are the same, and so is every prop, but for the campfires and seats and the props they replaced,
    /// all of them within the campsite's radius plus three cells of the campfire, the lamp posts, torches and braziers and
    /// the plants they took off their cells,
    /// the plants the ponds removed, on, beside or just below them, the plants the tall grass removed, on it, and the
    /// bushes dried on the dry grass and the tall grass.
    /// </summary>
    [TestCase(ClearingGeneratorPath)]
    [TestCase(VillageForestGeneratorPath)]
    [TestCase(LakesideGeneratorPath)]
    [TestCase(SnowfieldGeneratorPath)]
    [TestCase(CoastGeneratorPath)]
    [TestCase(LavaFieldsGeneratorPath)]
    public void TheDetailStream_LeavesTheRestOfTheLayoutWhereItWas(string generatorPath)
    {
        AreaGenerator target = ProjectCopy<AreaGenerator>(generatorPath);
        AreaGenerator without = Track(Object.Instantiate(target));
        SerializedObject settings = new(without);
        string[] added = { "Dirt" };
        bool laysWeeds = TurnWeedsOff(settings);
        if (target is ClearingGenerator)
        {
            settings.FindProperty("campfirePrefab").objectReferenceValue = null;
        }
        else if (target is VillageForestGenerator)
        {
            settings.FindProperty("dirtWidth").floatValue = 0f;
            settings.FindProperty("tallGrassCount").intValue = 0;
            settings.FindProperty("meadowCount").intValue = 0;
            settings.FindProperty("lampPostPrefab").objectReferenceValue = null;
            added = new[] { "Dirt", "TallGrass", "Meadow" };
        }
        else if (target is SnowfieldGenerator)
        {
            settings.FindProperty("holeCount").intValue = 0;
            settings.FindProperty("campfirePrefab").objectReferenceValue = null;
            settings.FindProperty("trailTorchPrefab").objectReferenceValue = null;
            added = new[] { "Water", "Dirt" };
        }
        else if (target is CoastGenerator)
        {
            settings.FindProperty("campfirePrefab").objectReferenceValue = null;
            settings.FindProperty("plateauSize").vector2Value = Vector2.zero;
            settings.FindProperty("beaconPrefab").objectReferenceValue = null;
        }
        else if (target is LavaFieldsGenerator)
        {
            settings.FindProperty("campfirePrefab").objectReferenceValue = null;
            settings.FindProperty("causewayBraziers").arraySize = 0;
            added = new string[0];
        }
        else
        {
            settings.FindProperty("dryGrassPatchCount").intValue = 0;
            settings.FindProperty("shallowPondCount").intValue = 0;
            settings.FindProperty("tallGrassCount").intValue = 0;
            settings.FindProperty("meadowCount").intValue = 0;
            settings.FindProperty("dockTorchPrefab").objectReferenceValue = null;
            added = new[] { "DryGrass", "TallGrass", "Meadow" };
        }

        settings.ApplyModifiedPropertiesWithoutUndo();
        if (laysWeeds)
        {
            added = new List<string>(added) { "Weeds" }.ToArray();
        }

        for (int seed = 1; seed <= 5; seed++)
        {
            AreaLayout full = Layout(target, seed);
            AreaLayout plain = without.Generate(seed);
            List<int> addedIndices = new();
            foreach (string name in added)
            {
                addedIndices.Add(full.IndexOf(ProjectMaterial(name)));
                Assert.That(plain.TerrainCells(addedIndices[addedIndices.Count - 1]), Is.Empty, $"seed {seed}: the {name} step is off");
            }

            int shallowsIndex = target is LakesideGenerator ? full.IndexOf(ProjectMaterial("Shallows")) : -1;
            for (int layer = 0; layer < full.TerrainLayerCount; layer++)
            {
                if (!addedIndices.Contains(layer) && layer != shallowsIndex)
                {
                    Expect(full.TerrainCells(layer).SetEquals(plain.TerrainCells(layer)), $"seed {seed}: layer {layer}");
                }
            }

            Assert.That(full.SpawnCell, Is.EqualTo(plain.SpawnCell), $"seed {seed}");
            Assert.That(full.Mobs.Count, Is.EqualTo(plain.Mobs.Count), $"seed {seed}");
            for (int i = 0; i < full.Mobs.Count; i++)
            {
                Assert.That(full.Mobs[i].Cell, Is.EqualTo(plain.Mobs[i].Cell), $"seed {seed}: mob {i}");
            }

            HashSet<Vector3Int> campsite = new(CellComparer.Instance);
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
                else if (target is CoastGenerator coastal && prop.Prefab == coastal.CampfirePrefab)
                {
                    foreach (Vector3Int cell in CellsWithin(prop.Cell, Mathf.CeilToInt(coastal.CampsiteRadius) + 3))
                    {
                        campsite.Add(cell);
                    }
                }
                else if (target is LavaFieldsGenerator scorched && prop.Prefab == scorched.CampfirePrefab)
                {
                    foreach (Vector3Int cell in CellsWithin(prop.Cell, Mathf.CeilToInt(scorched.CampsiteRadius) + 3))
                    {
                        campsite.Add(cell);
                    }
                }
                else if ((target is VillageForestGenerator village && prop.Prefab == village.LampPostPrefab) || IsRegionLight(target, prop.Prefab))
                {
                    campsite.Add(prop.Cell);
                }
            }

            int tallGrassIndex = full.IndexOf(ProjectMaterial("TallGrass"));
            if (tallGrassIndex >= 0)
            {
                campsite.UnionWith(full.TerrainCells(tallGrassIndex));
            }

            int dryGrassIndex = full.IndexOf(ProjectMaterial("DryGrass"));
            if (dryGrassIndex >= 0)
            {
                campsite.UnionWith(full.TerrainCells(dryGrassIndex));
            }

            Assert.That(plain.Raised, Is.Empty, $"seed {seed}: the plateau is off");
            foreach (Vector3Int cell in full.Raised)
            {
                campsite.UnionWith(CellsWithin(cell, 2));
            }

            foreach (Vector3Int ramp in full.Ramps)
            {
                for (int below = 1; below <= 4; below++)
                {
                    campsite.UnionWith(CellsWithin(ramp + new Vector3Int(0, -below, 0), 2));
                }
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

                Expect(campsite.Contains(prop.Cell), $"seed {seed}: {prop.Prefab.name} at {prop.Cell} moved off the campsite");
            }

            foreach (AreaLayout.Prop prop in full.Props)
            {
                if (!HasProp(plain, prop))
                {
                    Expect(campsite.Contains(prop.Cell), $"seed {seed}: {prop.Prefab.name} at {prop.Cell} added off the campsite");
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
            HashSet<Vector3Int> builtOn = new(CellComparer.Instance);
            HashSet<Vector3Int> plants = new(CellComparer.Instance);
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
                        Expect(grass.Contains(near), $"seed {seed}: {name} at {cell} within two cells of the map's edge");
                    }

                    foreach (Vector3Int near in CellsWithin(cell, 1))
                    {
                        Expect(!(paths.Contains(near) || dirt.Contains(near) || other.Contains(near)), $"seed {seed}: {name} at {cell} beside {near}");
                    }
                }
            }

            foreach (Vector3Int cell in meadow)
            {
                Assert.That(generation.Regions[cell], Is.EqualTo(VillageForestGenerator.Region.Clearing), $"seed {seed}: meadow at {cell} outside the clearing");
            }

            foreach (Vector3Int cell in tall)
            {
                Expect(!builtOn.Contains(cell), $"seed {seed}: tall grass under a building or fence at {cell}");
                Expect(!plants.Contains(cell), $"seed {seed}: a plant on the tall grass at {cell}");
                bool forest = generation.Regions[cell] == VillageForestGenerator.Region.Forest;
                bool edge = false;
                foreach (Vector3Int near in CellsWithin(cell, reach))
                {
                    edge |= generation.Regions.TryGetValue(near, out VillageForestGenerator.Region region) && (region == VillageForestGenerator.Region.Forest) != forest;
                }

                Expect(edge, $"seed {seed}: tall grass at {cell} away from the forest's edge");
            }

            Assert.That(CornerProblems(layout, "Meadow", "TallGrass"), Is.Empty, $"seed {seed}");
        }
    }

    /// <summary>
    /// The Village Forest's tall grass and meadows, laid last on the detail stream with its lamp posts, leave everything
    /// laid before them where it was: with all three turned off, every other layer (the dirt included), the spawn and
    /// every mob are the same, and so is every prop but the walk-through props the tall grass removed and the bushes it
    /// dried, all of them on it, and the lamp posts and the walk-through props they took off their cells.
    /// </summary>
    [Test]
    public void TheVillageForestsMeadowsAndTallGrass_LeaveTheRestOfItsLayoutWhereItWas()
    {
        AreaGenerator.PropSwap swap = villageForest.DryBushes;
        AreaGenerator without = Track(Object.Instantiate(villageForest));
        SerializedObject settings = new(without);
        settings.FindProperty("tallGrassCount").intValue = 0;
        settings.FindProperty("meadowCount").intValue = 0;
        settings.FindProperty("lampPostPrefab").objectReferenceValue = null;
        TurnWeedsOff(settings);
        settings.ApplyModifiedPropertiesWithoutUndo();
        for (int seed = 1; seed <= 5; seed++)
        {
            AreaLayout full = VillageForestLayout(seed).Layout;
            AreaLayout plain = without.Generate(seed);
            int meadowIndex = full.IndexOf(ProjectMaterial("Meadow"));
            int weedsIndex = full.IndexOf(ProjectMaterial("Weeds"));
            int tallIndex = full.IndexOf(ProjectMaterial("TallGrass"));
            HashSet<Vector3Int> tall = full.TerrainCells(tallIndex);
            Assert.That(plain.TerrainCells(meadowIndex), Is.Empty, $"seed {seed}");
            Assert.That(plain.TerrainCells(tallIndex), Is.Empty, $"seed {seed}");
            for (int layer = 0; layer < full.TerrainLayerCount; layer++)
            {
                if (layer != meadowIndex && layer != tallIndex && layer != weedsIndex)
                {
                    Expect(full.TerrainCells(layer).SetEquals(plain.TerrainCells(layer)), $"seed {seed}: layer {layer}");
                }
            }

            Assert.That(full.SpawnCell, Is.EqualTo(plain.SpawnCell));
            Assert.That(full.Mobs.Count, Is.EqualTo(plain.Mobs.Count));
            for (int i = 0; i < full.Mobs.Count; i++)
            {
                Assert.That(full.Mobs[i].Cell, Is.EqualTo(plain.Mobs[i].Cell), $"seed {seed}: mob {i}");
            }

            HashSet<Vector3Int> lamps = new(CellComparer.Instance);
            foreach (AreaLayout.Prop prop in full.Props)
            {
                if (prop.Prefab == villageForest.LampPostPrefab)
                {
                    lamps.Add(prop.Cell);
                    continue;
                }

                Expect(HasProp(plain, prop) || (IsSwapped(plain, prop, swap) && tall.Contains(prop.Cell)), $"seed {seed}: {prop.Prefab.name} at {prop.Cell} added");
            }

            foreach (AreaLayout.Prop prop in plain.Props)
            {
                if (HasProp(full, prop))
                {
                    continue;
                }

                if (IsSwapped(full, prop, swap))
                {
                    Expect(tall.Contains(prop.Cell), $"seed {seed}: {prop.Prefab.name} at {prop.Cell} dried off the tall grass");
                    continue;
                }

                Expect(!PropGridSnapping.TryGetFootprint(prop.Prefab, out _), $"seed {seed}: blocking {prop.Prefab.name} removed");
                Expect(tall.Contains(prop.Cell) || lamps.Contains(prop.Cell), $"seed {seed}: {prop.Prefab.name} at {prop.Cell} removed off the tall grass and the lamp posts");
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
            AreaLayout layout = Layout(lakeside, seed);
            HashSet<Vector3Int> grass = Cells(layout, "Grass");
            HashSet<Vector3Int> meadow = Cells(layout, "Meadow");
            HashSet<Vector3Int> tall = Cells(layout, "TallGrass");
            HashSet<Vector3Int> mud = Cells(layout, "Mud");
            HashSet<Vector3Int> others = new(CellComparer.Instance);
            foreach (string name in new[] { "DryGrass", "Sand", "Mud", "Gravel", "Stone", "Shallows", "Water", "Planks" })
            {
                others.UnionWith(Cells(layout, name));
            }

            HashSet<Vector3Int> plants = new(CellComparer.Instance);
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
                        Expect(grass.Contains(near), $"seed {seed}: {name} at {cell} within two cells of the map's edge");
                    }

                    foreach (Vector3Int near in CellsWithin(cell, 1))
                    {
                        Expect(!(others.Contains(near) || other.Contains(near)), $"seed {seed}: {name} at {cell} beside {near}");
                    }
                }
            }

            foreach (Vector3Int cell in meadow)
            {
                foreach (Vector3Int near in CellsWithin(cell, lakeside.BorderDepth))
                {
                    Expect(grass.Contains(near), $"seed {seed}: meadow at {cell} in the band of trees");
                }
            }

            foreach (Vector3Int cell in tall)
            {
                Expect(!plants.Contains(cell), $"seed {seed}: a plant on the tall grass at {cell}");
                bool nearTreesOrMud = false;
                foreach (Vector3Int near in CellsWithin(cell, lakeside.BorderDepth + reach))
                {
                    nearTreesOrMud |= !grass.Contains(near);
                }

                foreach (Vector3Int near in CellsWithin(cell, reach))
                {
                    nearTreesOrMud |= mud.Contains(near);
                }

                Expect(nearTreesOrMud, $"seed {seed}: tall grass at {cell} away from the trees and the marsh");
            }

            Assert.That(CornerProblems(layout, "Meadow", "TallGrass"), Is.Empty, $"seed {seed}");
        }
    }

    /// <summary>
    /// The Lakeside's tall grass and meadows, laid last, leave everything laid before them where it was: with both
    /// turned off, every other layer, the spawn and every mob are the same, and so is every prop but the walk-through
    /// props the tall grass removed and the bushes it dried, all of them on it.
    /// </summary>
    [Test]
    public void TheLakesidesMeadowsAndTallGrass_LeaveTheRestOfItsLayoutWhereItWas()
    {
        AreaGenerator.PropSwap swap = lakeside.DryBushes;
        AreaGenerator without = Track(Object.Instantiate(lakeside));
        SerializedObject settings = new(without);
        settings.FindProperty("tallGrassCount").intValue = 0;
        settings.FindProperty("meadowCount").intValue = 0;
        TurnWeedsOff(settings);
        settings.ApplyModifiedPropertiesWithoutUndo();
        for (int seed = 1; seed <= 5; seed++)
        {
            AreaLayout full = Layout(lakeside, seed);
            AreaLayout plain = without.Generate(seed);
            int meadowIndex = full.IndexOf(ProjectMaterial("Meadow"));
            int weedsIndex = full.IndexOf(ProjectMaterial("Weeds"));
            int tallIndex = full.IndexOf(ProjectMaterial("TallGrass"));
            HashSet<Vector3Int> tall = full.TerrainCells(tallIndex);
            Assert.That(plain.TerrainCells(meadowIndex), Is.Empty, $"seed {seed}");
            Assert.That(plain.TerrainCells(tallIndex), Is.Empty, $"seed {seed}");
            for (int layer = 0; layer < full.TerrainLayerCount; layer++)
            {
                if (layer != meadowIndex && layer != tallIndex && layer != weedsIndex)
                {
                    Expect(full.TerrainCells(layer).SetEquals(plain.TerrainCells(layer)), $"seed {seed}: layer {layer}");
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
                Expect(HasProp(plain, prop) || (IsSwapped(plain, prop, swap) && tall.Contains(prop.Cell)), $"seed {seed}: {prop.Prefab.name} at {prop.Cell} added");
            }

            foreach (AreaLayout.Prop prop in plain.Props)
            {
                if (HasProp(full, prop))
                {
                    continue;
                }

                if (IsSwapped(full, prop, swap))
                {
                    Expect(tall.Contains(prop.Cell), $"seed {seed}: {prop.Prefab.name} at {prop.Cell} dried off the tall grass");
                    continue;
                }

                Expect(!PropGridSnapping.TryGetFootprint(prop.Prefab, out _), $"seed {seed}: blocking {prop.Prefab.name} removed");
                Expect(tall.Contains(prop.Cell), $"seed {seed}: {prop.Prefab.name} at {prop.Cell} removed off the tall grass");
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
        TurnWeedsOff(pondSettings);
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
            HashSet<Vector3Int> wet = new(CellComparer.Instance);
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
                    Expect(full.TerrainCells(layer).SetEquals(plain.TerrainCells(layer)), $"seed {seed}: layer {layer}");
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
                Expect(HasProp(plain, prop), $"seed {seed}: {prop.Prefab.name} at {prop.Cell} added");
            }

            foreach (AreaLayout.Prop prop in plain.Props)
            {
                if (!HasProp(full, prop))
                {
                    Expect(!PropGridSnapping.TryGetFootprint(prop.Prefab, out _), $"seed {seed}: blocking {prop.Prefab.name} removed");
                    Expect(wet.Contains(prop.Cell), $"seed {seed}: {prop.Prefab.name} at {prop.Cell} removed away from the ponds");
                }
            }
        }
    }

    /// <summary>
    /// The bushes <paramref name="target"/>, the Lakeside or the Village Forest, dries on its dry grass and tall grass.
    /// </summary>
    private static AreaGenerator.PropSwap DryBushes(AreaGenerator target)
    {
        return target is LakesideGenerator lakesideTarget ? lakesideTarget.DryBushes : ((VillageForestGenerator)target).DryBushes;
    }

    /// <summary>
    /// Whether <paramref name="scatter"/> lists a prop of <paramref name="swap"/>'s From covering
    /// <paramref name="size"/> cells.
    /// </summary>
    private static bool ListsBushOfSize(AreaGenerator.PropScatter scatter, AreaGenerator.PropSwap swap, Vector2Int size)
    {
        foreach (GameObject prefab in scatter.Prefabs)
        {
            if (ListContains(swap.From, prefab) && PropGridSnapping.TryGetFootprint(prefab, out Rect footprint) && PropGridSnapping.CellsCovered(footprint, Vector2.one) == size)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether <paramref name="layout"/> holds, on <paramref name="prop"/>'s cell, the prop <paramref name="swap"/>
    /// turned it into or the one it was before: one of the swap's From and the other of its To.
    /// </summary>
    private static bool IsSwapped(AreaLayout layout, AreaLayout.Prop prop, AreaGenerator.PropSwap swap)
    {
        bool from = ListContains(swap.From, prop.Prefab);
        bool to = ListContains(swap.To, prop.Prefab);
        foreach (AreaLayout.Prop other in layout.Props)
        {
            if (other.Cell == prop.Cell && ((from && ListContains(swap.To, other.Prefab)) || (to && ListContains(swap.From, other.Prefab))))
            {
                return true;
            }
        }

        return false;
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
    /// Whether <paramref name="cell"/> lies in a run of three of <paramref name="cells"/> along <paramref name="step"/>.
    /// </summary>
    private static bool InRun(HashSet<Vector3Int> cells, Vector3Int cell, Vector3Int step)
    {
        bool before = cells.Contains(cell - step);
        bool after = cells.Contains(cell + step);
        return (before && after) || (before && cells.Contains(cell - step * 2)) || (after && cells.Contains(cell + step * 2));
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
        HashSet<Vector3Int> cells = new(CellComparer.Instance);
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
    /// Turns the weed patches of the generator <paramref name="settings"/> edits off, the last step of its detail
    /// stream, so a comparison of its layouts with and without an earlier step is not moved by them. Returns whether
    /// the generator lays weeds. The caller applies the change.
    /// </summary>
    private static bool TurnWeedsOff(SerializedObject settings)
    {
        SerializedProperty patches = settings.FindProperty("weedPatches");
        if (patches == null)
        {
            return false;
        }

        patches.FindPropertyRelative("edgeCount").intValue = 0;
        patches.FindPropertyRelative("openCount").intValue = 0;
        return true;
    }

    /// <summary>
    /// The fixture's copy, with the test built props, of the project's generator at <paramref name="path"/>, made once.
    /// </summary>
    private T ProjectCopy<T>(string path) where T : AreaGenerator
    {
        if (!projectCopies.TryGetValue(path, out AreaGenerator copy))
        {
            copy = WithTestProps(AssetDatabase.LoadAssetAtPath<AreaGenerator>(path));
            projectCopies[path] = copy;
        }

        return (T)copy;
    }

    /// <summary>
    /// <paramref name="target"/>'s layout for <paramref name="seed"/>, laid once per fixture (the Village Forest's
    /// shared with <see cref="VillageForestLayout"/>): the tests only read layouts, and a generator gives the same one
    /// for a seed every time (which <see cref="TheSameSeed_GivesTheSameLayout_AndAnotherSeedAnotherOne"/> checks
    /// without it).
    /// </summary>
    private AreaLayout Layout(AreaGenerator target, int seed)
    {
        if (!layouts.TryGetValue(target, out Dictionary<int, AreaLayout> bySeed))
        {
            bySeed = new Dictionary<int, AreaLayout>();
            layouts[target] = bySeed;
        }

        if (!bySeed.TryGetValue(seed, out AreaLayout layout))
        {
            layout = target == villageForest ? VillageForestLayout(seed).Layout : target.Generate(seed);
            bySeed[seed] = layout;
        }

        return layout;
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
        HashSet<Vector3Int> cells = new(Cells(layout, "Gravel"), CellComparer.Instance);
        cells.UnionWith(CellsOrNone(layout, "Plaza"));
        cells.UnionWith(Cells(layout, "Stone"));
        return cells;
    }

    /// <summary>
    /// The props <paramref name="target"/> places itself rather than through a scatter layer: a campsite's campfire,
    /// given in <paramref name="campfire"/> (null for a generator without one), and its seats, and the Village Forest's
    /// lamp posts.
    /// </summary>
    private static HashSet<GameObject> OwnProps(AreaGenerator target, out GameObject campfire)
    {
        HashSet<GameObject> own = new();
        IReadOnlyList<GameObject> seats = null;
        campfire = null;
        switch (target)
        {
            case ClearingGenerator clearing:
                campfire = clearing.CampfirePrefab;
                seats = clearing.CampsiteSeats;
                break;
            case SnowfieldGenerator snowfield:
                campfire = snowfield.CampfirePrefab;
                seats = snowfield.CampsiteSeats;
                own.Add(snowfield.TrailTorchPrefab);
                break;
            case CoastGenerator coast:
                campfire = coast.CampfirePrefab;
                seats = coast.CampsiteSeats;
                own.Add(coast.BeaconPrefab);
                break;
            case LavaFieldsGenerator lavaFields:
                campfire = lavaFields.CampfirePrefab;
                seats = lavaFields.CampsiteSeats;
                own.UnionWith(lavaFields.CausewayBraziers);
                break;
            case LakesideGenerator lakesideGenerator:
                own.Add(lakesideGenerator.DockTorchPrefab);
                break;
            case VillageForestGenerator village:
                own.Add(village.LampPostPrefab);
                break;
        }

        if (campfire != null)
        {
            own.Add(campfire);
            own.UnionWith(seats);
        }

        own.Remove(null);
        return own;
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
        HashSet<Vector3Int> cells = new(CellComparer.Instance);
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
        HashSet<Vector3Int> region = new(CellComparer.Instance) { start };
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
        return index >= 0 ? layout.TerrainCells(index) : new HashSet<Vector3Int>(CellComparer.Instance);
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
    /// A copy of the project's generator <paramref name="asset"/>, which the caller destroys, keeping its scatter
    /// layers' props and given the test built props the asset leaves unset: the test campfire and seats, and for the
    /// Village Forest the test building, fence, lamp post, fountain and town props.
    /// </summary>
    private T WithTestProps<T>(T asset) where T : AreaGenerator
    {
        Assert.That(asset, Is.Not.Null, "The project's generator should exist.");
        T copy = Object.Instantiate(asset);
        SerializedObject settings = new(copy);
        SetIfPresent(settings, "campfirePrefab", testProps.Campfire);
        SetListIfPresent(settings, "campsiteSeats", testProps.Seats);
        SetIfPresent(settings, "buildingPrefab", testProps.Building);
        SetIfPresent(settings, "fencePrefab", testProps.Fence);
        SetIfPresent(settings, "lampPostPrefab", testProps.LampPost);
        SetIfPresent(settings, "fountainPrefab", testProps.Fountain);
        SetListIfPresent(settings, "townProps", testProps.TownProps);
        settings.ApplyModifiedPropertiesWithoutUndo();
        return copy;
    }

    /// <summary>
    /// Sets the object field <paramref name="name"/> of <paramref name="settings"/> to <paramref name="prefab"/> when
    /// the generator has one.
    /// </summary>
    private static void SetIfPresent(SerializedObject settings, string name, GameObject prefab)
    {
        SerializedProperty property = settings.FindProperty(name);
        if (property != null)
        {
            property.objectReferenceValue = prefab;
        }
    }

    /// <summary>
    /// Sets the list field <paramref name="name"/> of <paramref name="settings"/> to <paramref name="prefabs"/> when
    /// the generator has one.
    /// </summary>
    private static void SetListIfPresent(SerializedObject settings, string name, IReadOnlyList<GameObject> prefabs)
    {
        SerializedProperty property = settings.FindProperty(name);
        if (property != null)
        {
            SetList(property, prefabs);
        }
    }

    /// <summary>
    /// Fills the serialized list <paramref name="list"/> with <paramref name="prefabs"/>.
    /// </summary>
    private static void SetList(SerializedProperty list, IReadOnlyList<GameObject> prefabs)
    {
        list.arraySize = prefabs.Count;
        for (int i = 0; i < prefabs.Count; i++)
        {
            list.GetArrayElementAtIndex(i).objectReferenceValue = prefabs[i];
        }
    }

    /// <summary>
    /// Fails the test with <paramref name="message"/> unless <paramref name="condition"/> holds. The checks here run
    /// for every cell of twenty layouts, and <c>Assert.That</c> costs a few microseconds a call, its message formatted
    /// and a constraint result built even when it passes; this costs next to nothing until it fails.
    /// </summary>
    private static void Expect(bool condition, FormattableString message)
    {
        if (!condition)
        {
            Assert.Fail(message.ToString());
        }
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
