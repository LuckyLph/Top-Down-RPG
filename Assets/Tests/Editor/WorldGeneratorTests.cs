using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tests for composing a world (Docs/WorldGeneration.md: Testing), on compositions of small worlds of the project's
/// generators, built in code from unsaved generators and worlds (<see cref="TestWorldGenerators"/>): the Overworld's Phase 0 settings (the six regions on
/// 5 x 4 chunks and their eight road links), a row of three regions on 6 x 2 chunks, and a Clearing or two on 3 x 2.
/// The same settings give the same composition, cell for cell and in order, and another seed another one; re-seeding a
/// region changes only the chunks its cells, road and gates touch and their neighbours, and re-rolling a chunk's wilds
/// only that chunk and its neighbours; each region is its generator's layout moved by its cell plus its plaza; a region
/// off the land, too close to another or to the beach, or on a missing chunk is warned about, and a sea region's ocean
/// meets the world's sea and its beach the world's beach; every land cell has ground, the sea ring is ocean and the
/// beach lies between them all round; every chunk lists its own and its neighbours' strips' materials in the style's
/// order, within 16 or warned about unless allowed wide, and the Overworld lays all eighteen; every waystone, gate and
/// mob is reachable from the default entry, every port is linked and the roads and gates lie on walkable wilds; the
/// wilds' patches meet only grass and the wilds' mobs keep their distances from the entries and every other mob; and the
/// derived ids are unique, in the format of the editor's random ids, and stay the same through re-seeding, moving,
/// re-rolling and composing again.
/// </summary>
public class WorldGeneratorTests
{
    private const string Snowfield = TestWorldGenerators.Snowfield;
    private const string Lakeside = TestWorldGenerators.Lakeside;
    private const string LavaFields = TestWorldGenerators.LavaFields;

    private static readonly Vector2Int[] Neighbours4 = { Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down };

    /// <summary>
    /// The raised ground of a composition as a source of cell kinds for the face rule.
    /// </summary>
    private readonly struct CompositionKinds : RaisedGround.IKinds
    {
        private readonly WorldComposition composition;

        /// <summary>
        /// Reads the raised ground of <paramref name="composition"/>.
        /// </summary>
        public CompositionKinds(WorldComposition composition)
        {
            this.composition = composition;
        }

        /// <summary>
        /// What <paramref name="cell"/> is.
        /// </summary>
        public byte KindAt(Vector3Int cell)
        {
            return composition.RaisedAt(new Vector2Int(cell.x, cell.y));
        }
    }

    private TestWorldGenerators generators;
    private WorldGenerator.GroundMaterials materials;
    private TerrainStyle style;
    private TerrainMovementProfile2D profile;
    private WorldGenerator overworldGenerator;
    private WorldComposition overworld;

    /// <summary>
    /// Loads the project's style, profile, materials and mob, and composes the Overworld's Phase 0 settings once for
    /// the fixture; the tests only read it.
    /// </summary>
    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        generators = new TestWorldGenerators();
        style = generators.Style;
        profile = generators.Profile;
        materials = generators.Materials;
        overworldGenerator = generators.Overworld();
        overworld = overworldGenerator.Compose();
    }

    /// <summary>
    /// Destroys the unsaved worlds, scene definitions and generators the fixture made.
    /// </summary>
    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        generators.Destroy();
    }

    /// <summary>
    /// The same settings, in two generators, give the same composition cell for cell and in order (materials, owners,
    /// raised ground, mobs and their ids, entries, roads, chunk lists and hashes, warnings), over two world seeds and two
    /// region seeds; another world seed or another region seed gives another one.
    /// </summary>
    [Test]
    public void TheSameSettings_GiveTheSameComposition_AndAnotherSeedAnotherOne()
    {
        Dictionary<string, WorldComposition> compositions = new();
        foreach (int worldSeed in new[] { 1, 7 })
        {
            foreach (int regionSeed in new[] { 1, 4 })
            {
                WorldComposition first = Row(worldSeed, regionSeed, regionSeed, regionSeed, Vector2Int.zero).Compose();
                WorldComposition second = Row(worldSeed, regionSeed, regionSeed, regionSeed, Vector2Int.zero).Compose();
                ExpectSame(first, second, $"world seed {worldSeed}, region seed {regionSeed}");
                compositions[$"{worldSeed}/{regionSeed}"] = first;
            }
        }

        Assert.That(DiffersInCells(compositions["1/1"], compositions["7/1"]), Is.True, "another world seed gives another world");
        Assert.That(DiffersInCells(compositions["1/1"], compositions["1/4"]), Is.True, "another region seed gives another world");
    }

    /// <summary>
    /// Re-seeding one region changes only the chunks its cells (before and after), its road and its gates touch and
    /// their neighbours.
    /// </summary>
    [Test]
    public void ReseedingARegion_ChangesOnlyTheChunksItsCellsRoadsAndGatesTouch_AndTheirNeighbours()
    {
        WorldGenerator generator = Row(1, 1, 1, 1, Vector2Int.zero);
        WorldComposition before = generator.Compose();
        WorldComposition after = Row(1, 5, 1, 1, Vector2Int.zero).Compose();

        HashSet<Vector2Int> touched = new();
        foreach (WorldComposition composition in new[] { before, after })
        {
            WorldComposition.Region region = composition.FindRegion(Snowfield);
            foreach (Vector2Int cell in CellsOf(composition, region))
            {
                touched.Add(ChunkCoords.ChunkOf(cell));
            }

            foreach (WorldComposition.Road road in composition.Roads)
            {
                RoadLink link = generator.Links[road.Link];
                if (link.FromRegion == Snowfield || link.ToRegion == Snowfield)
                {
                    foreach (Vector2Int cell in road.Cells)
                    {
                        touched.Add(ChunkCoords.ChunkOf(cell));
                    }
                }
            }

            foreach (WorldDefinition.Entry entry in composition.Entries)
            {
                if (entry.Name.StartsWith(Snowfield, StringComparison.Ordinal))
                {
                    touched.Add(ChunkCoords.ChunkOf(Vector2Int.FloorToInt(entry.Position)));
                }
            }
        }

        int changed = ChangedChunks(before, after, Grown(touched), "re-seeding the Snowfield");
        Assert.That(changed, Is.GreaterThan(0), "re-seeding the Snowfield changes its chunks");
    }

    /// <summary>
    /// Re-rolling one chunk's wilds (its record's wilds seed) changes that chunk and at most its neighbours (whose
    /// lists read its border strip), and no cell outside it.
    /// </summary>
    [Test]
    public void RerollingAChunksWilds_ChangesOnlyThatChunk_AndItsNeighbours()
    {
        Vector2Int target = new(4, 0);
        WorldComposition before = Row(1, 1, 1, 1, Vector2Int.zero).Compose();
        WorldGenerator rerolled = Row(1, 1, 1, 1, Vector2Int.zero);
        rerolled.SetRecord(new ChunkRecord(target, false, false, 11));
        WorldComposition after = rerolled.Compose();

        ChangedChunks(before, after, Grown(new HashSet<Vector2Int> { target }), "re-rolling chunk (4, 0)");
        Assert.That(after.ChunkAt(target).ContentHash, Is.Not.EqualTo(before.ChunkAt(target).ContentHash), "the re-rolled chunk");
        RectInt bounds = ChunkCoords.Bounds(target);
        for (int y = 0; y < before.Size.y; y++)
        {
            for (int x = 0; x < before.Size.x; x++)
            {
                Vector2Int cell = new(x, y);
                Expect(bounds.Contains(cell) || before.MaskAt(cell) == after.MaskAt(cell), $"{cell} outside the re-rolled chunk changed");
            }
        }
    }

    /// <summary>
    /// Each region of the Overworld owns exactly its generator's seed layout's ground moved by its cell, and each of
    /// its cells holds exactly the layout's materials there, plus plaza within the plaza's radius of its spawn cell;
    /// its raised ground, its ramps, its mobs (in order) and its spawn cell are the layout's, moved.
    /// </summary>
    [Test]
    public void EachRegion_IsItsGeneratorsLayoutMovedByItsCell_PlusOnlyItsPlaza()
    {
        uint plaza = 1u << overworld.IndexOf(materials.Plaza);
        float radius = overworldGenerator.PlazaRadius;
        foreach (WorldComposition.Region region in overworld.Regions)
        {
            AreaLayout layout = region.Generator.Generate(region.Seed);
            Dictionary<Vector3Int, uint> masks = new(CellComparer.Instance);
            for (int layer = 0; layer < layout.TerrainLayerCount; layer++)
            {
                uint bit = 1u << overworld.IndexOf(layout.TerrainLayers[layer].Material);
                foreach (Vector3Int cell in layout.TerrainCells(layer))
                {
                    masks[cell] = (masks.TryGetValue(cell, out uint mask) ? mask : 0u) | bit;
                }
            }

            HashSet<Vector3Int> ground = layout.TerrainCells(0);
            List<Vector2Int> owned = CellsOf(overworld, region);
            Assert.That(owned.Count, Is.EqualTo(ground.Count), $"{region.Key}'s cells");
            Vector2 spawn = region.SpawnCell + new Vector2(0.5f, 0.5f);
            foreach (Vector2Int cell in owned)
            {
                Vector3Int local = new(cell.x - region.Cell.x, cell.y - region.Cell.y, 0);
                Expect(ground.Contains(local), $"{region.Key}: {cell} is not its layout's ground");
                uint expected = masks[local];
                bool inDisc = (cell + new Vector2(0.5f, 0.5f) - spawn).sqrMagnitude <= radius * radius;
                uint mask = overworld.MaskAt(cell);
                Expect(mask == expected || (inDisc && mask == (expected | plaza)), $"{region.Key}: {cell} holds {mask}, its layout {expected}");
                Expect(!inDisc || (mask & plaza) != 0, $"{region.Key}: no plaza at {cell}");
                byte kind = layout.Ramps.Contains(local) ? RaisedGround.Ramp : layout.Raised.Contains(local) ? RaisedGround.Raised : RaisedGround.Level;
                Expect(overworld.RaisedAt(cell) == kind, $"{region.Key}: raised ground at {cell}");
            }

            List<WorldComposition.Mob> mobs = overworld.Mobs.FindAll(mob => mob.Scope == region.Key);
            Assert.That(mobs.Count, Is.EqualTo(layout.Mobs.Count), $"{region.Key}'s mobs");
            for (int i = 0; i < mobs.Count; i++)
            {
                Assert.That(mobs[i].Cell, Is.EqualTo(new Vector2Int(layout.Mobs[i].Cell.x, layout.Mobs[i].Cell.y) + region.Cell), $"{region.Key}'s mob {i}");
                Assert.That(mobs[i].Prefab, Is.EqualTo(layout.Mobs[i].Prefab), $"{region.Key}'s mob {i}");
                Assert.That(mobs[i].Ordinal, Is.EqualTo(i), $"{region.Key}'s mob {i}");
            }

            Assert.That(region.SpawnCell, Is.EqualTo(new Vector2Int(layout.SpawnCell.x, layout.SpawnCell.y) + region.Cell), $"{region.Key}'s spawn");
        }
    }

    /// <summary>
    /// A Clearing well inside the land warns of no placement; one off the land, one within the shore gap of the beach,
    /// two within the region gap of each other and one on a chunk the world lacks are each warned about.
    /// </summary>
    [Test]
    public void APlacement_OffTheLand_NearTheBeach_NearAnother_OrOnAMissingChunk_IsWarnedAbout()
    {
        List<Vector2Int> all = new() { new(0, 0), new(1, 0), new(2, 0), new(0, 1), new(1, 1), new(2, 1) };
        List<Vector2Int> withoutMiddle = new(all);
        withoutMiddle.Remove(new Vector2Int(1, 1));
        AreaGenerator clearing = Generator("Clearing");

        List<string> control = Small(all, new RegionPlacement("clearing", clearing, new Vector2Int(96, 64))).Compose().Warnings;
        Assert.That(control.FindAll(IsPlacementWarning), Is.Empty, "a Clearing in the middle of the land");

        Assert.That(Small(all, new RegionPlacement("clearing", clearing, new Vector2Int(30, 64))).Compose().Warnings.Exists(warning => warning.Contains("off the land")), Is.True, "a Clearing off the land");
        Assert.That(Small(all, new RegionPlacement("clearing", clearing, new Vector2Int(51, 64))).Compose().Warnings.Exists(warning => warning.Contains("and the beach")), Is.True, "a Clearing beside the beach");
        Assert.That(Small(all, new RegionPlacement("clearing", clearing, new Vector2Int(60, 64)), new RegionPlacement("clearingTwo", clearing, new Vector2Int(116, 64))).Compose().Warnings.Exists(warning => warning.Contains("fewer than the region gap")), Is.True, "two Clearings 8 cells apart");
        Assert.That(Small(withoutMiddle, new RegionPlacement("clearing", clearing, new Vector2Int(96, 64))).Compose().Warnings.Exists(warning => warning.Contains("chunks the world does not have")), Is.True, "a Clearing on a missing chunk");
    }

    /// <summary>
    /// In the Overworld the Coast region's ocean meets the world's sea, its beach meets the world's beach at both its
    /// sides, and the ocean, the region's or the world's, meets only sand or ocean; a Coast placed inland is warned
    /// about.
    /// </summary>
    [Test]
    public void TheCoastsOcean_JoinsTheSea_AndItsBeachTheWorlds()
    {
        WorldComposition.Region coast = overworld.FindRegion("coast");
        int oceanBit = overworld.IndexOf(materials.Ocean);
        int sandBit = overworld.IndexOf(materials.Sand);
        uint ocean = 1u << oceanBit;
        uint sand = 1u << sandBit;
        bool meetsSea = false;
        bool westJoin = false;
        bool eastJoin = false;
        foreach (Vector2Int cell in CellsOf(overworld, coast))
        {
            uint mask = overworld.MaskAt(cell);
            foreach (Vector2Int step in Neighbours4)
            {
                Vector2Int next = cell + step;
                bool wildsSand = overworld.RegionAt(next) == WorldComposition.Wilds && (overworld.MaskAt(next) & sand) != 0;
                meetsSea |= (mask & ocean) != 0 && overworld.RegionAt(next) == WorldComposition.Sea;
                westJoin |= (mask & sand) != 0 && step == Vector2Int.left && wildsSand;
                eastJoin |= (mask & sand) != 0 && step == Vector2Int.right && wildsSand;
            }
        }

        Assert.That(meetsSea, Is.True, "the Coast's ocean meets the world's sea");
        Assert.That(westJoin && eastJoin, Is.True, "the Coast's beach meets the world's beach on its west and east sides");
        for (int y = 0; y < overworld.Size.y; y++)
        {
            for (int x = 0; x < overworld.Size.x; x++)
            {
                Vector2Int cell = new(x, y);
                if (WorldComposition.TopIndex(overworld.MaskAt(cell)) != oceanBit)
                {
                    continue;
                }

                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        Vector2Int next = cell + new Vector2Int(dx, dy);
                        int top = WorldComposition.TopIndex(overworld.MaskAt(next));
                        Expect(!overworld.Contains(next) || top == oceanBit || top == sandBit, $"the ocean at {cell} meets {overworld.TopAt(next)} at {next}");
                    }
                }
            }
        }

        Assert.That(overworld.Warnings.Exists(warning => warning.Contains("does not meet the world's sea")), Is.False, "no warning about the Coast's sea");
        WorldGenerator inland = Generator("Inland", new Vector2Int(5, 4), null);
        inland.SetRegions(new RegionPlacement("coast", Generator("Coast"), new Vector2Int(160, 128)));
        Assert.That(inland.Compose().Warnings.Exists(warning => warning.Contains("does not meet the world's sea")), Is.True, "a Coast inland");
    }

    /// <summary>
    /// Every cell of the Overworld holds ground, so it runs on across every chunk border; every wilds cell holds
    /// grass; every sea cell holds the ocean alone and the sea ring keeps 16 cells or more between any walkable cell
    /// and the world's edge; and the beach lies between the land and the sea all round: no wilds cell beside the sea
    /// lacks sand, and every wilds cell of sand lies within the beach's widest of the sea.
    /// </summary>
    [Test]
    public void TheGround_RunsOnEverywhere_WithTheBeachBetweenTheLandAndTheSeaAllRound()
    {
        uint ocean = 1u << overworld.IndexOf(materials.Ocean);
        uint sand = 1u << overworld.IndexOf(materials.Sand);
        uint grass = 1u << overworld.IndexOf(materials.Grass);
        bool[] walkable = WalkableMaterials(overworld);
        int widest = Mathf.CeilToInt(Mathf.Max(overworldGenerator.BeachWidth.x, overworldGenerator.BeachWidth.y));
        for (int y = 0; y < overworld.Size.y; y++)
        {
            for (int x = 0; x < overworld.Size.x; x++)
            {
                Vector2Int cell = new(x, y);
                uint mask = overworld.MaskAt(cell);
                int owner = overworld.RegionAt(cell);
                Expect(mask != 0, $"no ground at {cell}");
                Expect(owner != WorldComposition.Sea || mask == ocean, $"the sea at {cell} holds more than the ocean");
                Expect(owner != WorldComposition.Wilds || (mask & grass) != 0, $"no grass under the wilds at {cell}");
                int edge = Mathf.Min(Mathf.Min(x, overworld.Size.x - 1 - x), Mathf.Min(y, overworld.Size.y - 1 - y));
                Expect(!walkable[WorldComposition.TopIndex(mask)] || edge >= 16, $"walkable ground at {cell}, {edge} cells from the world's edge");
                if (owner != WorldComposition.Wilds)
                {
                    continue;
                }

                bool besideSea = false;
                bool nearSea = false;
                for (int dy = -widest - 1; dy <= widest + 1; dy++)
                {
                    for (int dx = -widest - 1; dx <= widest + 1; dx++)
                    {
                        bool sea = overworld.RegionAt(cell + new Vector2Int(dx, dy)) == WorldComposition.Sea;
                        nearSea |= sea;
                        besideSea |= sea && Mathf.Abs(dx) <= 1 && Mathf.Abs(dy) <= 1;
                    }
                }

                Expect(!besideSea || (mask & sand) != 0, $"the wilds at {cell} meet the sea without a beach");
                Expect((mask & sand) == 0 || nearSea, $"beach at {cell} more than {widest + 1} cells from the sea");
            }
        }
    }

    /// <summary>
    /// Each of the Overworld's chunks lists exactly the materials of its cells and of its neighbours' border strips,
    /// in the style's order and within 16, and the Overworld lays all eighteen of the style's materials.
    /// </summary>
    [Test]
    public void EachChunk_ListsItsAndItsNeighboursStripsMaterials_InTheStylesOrder_WithinSixteen_AndAllEighteenAreLaid()
    {
        uint laid = 0;
        foreach (WorldComposition.Chunk chunk in overworld.Chunks)
        {
            RectInt bounds = ChunkCoords.Bounds(chunk.Coords);
            uint own = 0;
            uint listed = 0;
            for (int y = bounds.yMin - WorldChunk.SkirtCells; y < bounds.yMax + WorldChunk.SkirtCells; y++)
            {
                for (int x = bounds.xMin - WorldChunk.SkirtCells; x < bounds.xMax + WorldChunk.SkirtCells; x++)
                {
                    uint mask = overworld.MaskAt(new Vector2Int(x, y));
                    listed |= mask;
                    own |= bounds.Contains(new Vector2Int(x, y)) ? mask : 0u;
                }
            }

            laid |= own;
            List<TerrainMaterial> expected = new();
            for (int i = 0; i < style.LayerOrder.Count; i++)
            {
                if ((listed & (1u << i)) != 0)
                {
                    expected.Add(style.LayerOrder[i]);
                }
            }

            Assert.That(chunk.Materials, Is.EqualTo(expected), $"chunk {chunk.Coords}'s list, in the style's order");
            Assert.That(chunk.Materials.Count, Is.LessThanOrEqualTo(TerrainControlMap.NarrowLayers), $"chunk {chunk.Coords}'s list");
        }

        Assert.That(WorldComposition.CountOf(laid), Is.EqualTo(18).And.EqualTo(style.LayerOrder.Count), "every material of the style is laid");
        Assert.That(overworld.Warnings, Is.Empty, "the Overworld's warnings");
    }

    /// <summary>
    /// A chunk listing more than 16 materials (the Lakeside, the Snowfield and the Lava Fields laid over one another in
    /// it) is warned about, unless its record allows it the wide variant.
    /// </summary>
    [Test]
    public void AChunkPastSixteenMaterials_IsWarnedAbout_UnlessAllowedWide()
    {
        Vector2Int middle = new(1, 1);
        WorldGenerator generator = Generator("Crowded", new Vector2Int(3, 3), null);
        generator.SetRegions(
            new RegionPlacement(Lakeside, Generator("Lakeside"), new Vector2Int(96, 96)),
            new RegionPlacement(Snowfield, Generator("Snowfield"), new Vector2Int(96, 96)),
            new RegionPlacement(LavaFields, Generator("LavaFields"), new Vector2Int(96, 96)));
        WorldComposition crowded = generator.Compose();
        Assert.That(crowded.ChunkAt(middle).Materials.Count, Is.GreaterThan(TerrainControlMap.NarrowLayers), "the middle chunk's list");
        Assert.That(crowded.Warnings.Exists(warning => warning.StartsWith($"Chunk {middle} lists", StringComparison.Ordinal)), Is.True, "the middle chunk is warned about");

        generator.SetRecord(new ChunkRecord(middle, false, true, 0));
        WorldComposition allowed = generator.Compose();
        Assert.That(allowed.ChunkAt(middle).AllowWide, Is.True, "the middle chunk's record");
        Assert.That(allowed.Warnings.Exists(warning => warning.StartsWith($"Chunk {middle} lists", StringComparison.Ordinal)), Is.False, "the middle chunk allowed wide");
    }

    /// <summary>
    /// In the Overworld every waystone, gate and mob can be walked to from the default entry (the Clearing's waystone)
    /// over 4-connected walkable cells, off the cliff faces and up the ramps only; every port is linked; every road cell
    /// lies on the wilds and is walkable, and every gate stands on its road.
    /// </summary>
    [Test]
    public void EveryWaystoneGateAndMob_IsReachable_EveryPortIsLinked_AndTheRoadsAndGatesAreWalkable()
    {
        Assert.That(overworld.DefaultEntry.Name, Is.EqualTo("clearing"), "the default entry");
        Assert.That(overworld.DefaultEntry.Waystone, Is.True, "the default entry is a waystone");
        bool[] walkable = WalkableMaterials(overworld);
        HashSet<Vector2Int> reachable = Reachable(overworld, walkable, Vector2Int.FloorToInt(overworld.DefaultEntry.Position));
        foreach (WorldDefinition.Entry entry in overworld.Entries)
        {
            Assert.That(reachable.Contains(Vector2Int.FloorToInt(entry.Position)), Is.True, $"{entry.Name} is reachable");
        }

        foreach (WorldComposition.Mob mob in overworld.Mobs)
        {
            Assert.That(reachable.Contains(mob.Cell), Is.True, $"the mob at {mob.Cell} ({mob.Scope}) is reachable");
        }

        foreach (WorldComposition.Region region in overworld.Regions)
        {
            foreach (WorldComposition.Port port in region.Ports)
            {
                Assert.That(port.Linked, Is.True, $"{region.Key}'s port facing {port.Side} is linked");
                Vector2Int gate = Vector2Int.FloorToInt(overworld.Entries.Find(entry => entry.Name == port.Gate).Position);
                TerrainMaterial top = overworld.TopAt(gate);
                Assert.That(top == materials.Stone || top == materials.Gravel, Is.True, $"{port.Gate} stands on its road, not {top}");
            }
        }

        foreach (WorldComposition.Road road in overworld.Roads)
        {
            Assert.That(road.Cells, Is.Not.Empty, $"road {road.Link + 1}");
            foreach (Vector2Int cell in road.Cells)
            {
                Expect(overworld.RegionAt(cell) == WorldComposition.Wilds, $"road {road.Link + 1} at {cell} off the wilds");
                Expect(walkable[WorldComposition.TopIndex(overworld.MaskAt(cell))], $"road {road.Link + 1} at {cell} not walkable");
            }
        }

        Assert.That(overworld.Roads.Count, Is.EqualTo(overworldGenerator.Links.Count), "a road per link");
    }

    /// <summary>
    /// The Overworld's wilds patches (tall grass, meadow and weeds) lie on the wilds' grass in pieces of four cells or
    /// more, two cells or more from every region, the beach and the roads, and a cell from every patch of another kind,
    /// so each meets only grass; and the wilds mobs stand on the wilds' grass, off the roads and the beach, at least
    /// the entry distance from every waystone and gate and the mob spacing from every other mob.
    /// </summary>
    [Test]
    public void TheWildsPatches_MeetOnlyGrass_AndTheWildsMobsKeepTheirDistances()
    {
        uint grass = 1u << overworld.IndexOf(materials.Grass);
        uint[] kinds = { 1u << overworld.IndexOf(materials.TallGrass), 1u << overworld.IndexOf(materials.Meadow), 1u << overworld.IndexOf(materials.Weeds) };
        uint patches = kinds[0] | kinds[1] | kinds[2];
        int laid = 0;
        for (int y = 0; y < overworld.Size.y; y++)
        {
            for (int x = 0; x < overworld.Size.x; x++)
            {
                Vector2Int cell = new(x, y);
                uint mask = overworld.MaskAt(cell);
                if (overworld.RegionAt(cell) != WorldComposition.Wilds || (mask & patches) == 0)
                {
                    continue;
                }

                laid++;
                Expect((mask & ~patches) == grass && WorldComposition.CountOf(mask & patches) == 1, $"the patch at {cell} holds {mask}");
                for (int dy = -2; dy <= 2; dy++)
                {
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        Vector2Int near = cell + new Vector2Int(dx, dy);
                        uint other = overworld.MaskAt(near);
                        Expect(overworld.RegionAt(near) == WorldComposition.Wilds && (other & ~(grass | patches)) == 0, $"the patch at {cell} within two cells of {overworld.TopAt(near)} at {near}");
                        Expect(Mathf.Abs(dx) > 1 || Mathf.Abs(dy) > 1 || (other & patches & ~(mask & patches)) == 0, $"the patch at {cell} beside another kind at {near}");
                    }
                }

                int piece = 0;
                HashSet<Vector2Int> seen = new() { cell };
                Queue<Vector2Int> queue = new();
                queue.Enqueue(cell);
                while (queue.Count > 0 && piece < 4)
                {
                    Vector2Int at = queue.Dequeue();
                    piece++;
                    foreach (Vector2Int step in Neighbours4)
                    {
                        Vector2Int next = at + step;
                        if (overworld.RegionAt(next) == WorldComposition.Wilds && (overworld.MaskAt(next) & mask & patches) != 0 && seen.Add(next))
                        {
                            queue.Enqueue(next);
                        }
                    }
                }

                Expect(piece >= 4, $"a scrap of patch at {cell}");
            }
        }

        Assert.That(laid, Is.GreaterThan(0), "the wilds lay patches");
        List<WorldComposition.Mob> wilds = overworld.Mobs.FindAll(mob => mob.Scope.StartsWith("wilds_", StringComparison.Ordinal));
        Assert.That(wilds, Is.Not.Empty, "the wilds lay mobs");
        foreach (WorldComposition.Mob mob in wilds)
        {
            Assert.That(overworld.RegionAt(mob.Cell), Is.EqualTo(WorldComposition.Wilds), $"the wilds mob at {mob.Cell} is on the wilds");
            Assert.That(overworld.MaskAt(mob.Cell) & ~(grass | patches), Is.Zero, $"the wilds mob at {mob.Cell} is on the wilds' grass");
            foreach (WorldDefinition.Entry entry in overworld.Entries)
            {
                Assert.That(Vector2.Distance(mob.Cell + new Vector2(0.5f, 0.5f), entry.Position), Is.GreaterThanOrEqualTo(overworldGenerator.MobDistanceFromEntries), $"the wilds mob at {mob.Cell} from {entry.Name}");
            }

            foreach (WorldComposition.Mob other in overworld.Mobs)
            {
                if (other.Id != mob.Id)
                {
                    Assert.That(Vector2Int.Distance(mob.Cell, other.Cell), Is.GreaterThanOrEqualTo(overworldGenerator.MobSpacing), $"the wilds mob at {mob.Cell} from the mob at {other.Cell}");
                }
            }
        }
    }

    /// <summary>
    /// The Overworld's mob ids are unique, 32 lower-case hex digits like the editor's random ids, and derived from the
    /// world's id, their scope and their ordinal; the derivation is fixed (saves name mobs by it); and a region's mob
    /// keeps its id when the region is re-seeded or moved, a chunk's wilds mob when its wilds are re-rolled, and every
    /// mob when the world is composed again.
    /// </summary>
    [Test]
    public void DerivedIds_AreUnique_AndStayTheSameThroughReseedingMovingRerollingAndComposingAgain()
    {
        HashSet<string> ids = new();
        string worldId = overworldGenerator.World.WorldId;
        foreach (WorldComposition.Mob mob in overworld.Mobs)
        {
            Assert.That(ids.Add(mob.Id), Is.True, $"the id of the mob at {mob.Cell} is unique");
            Assert.That(mob.Id, Does.Match("^[0-9a-f]{32}$"), "the id's format");
            Assert.That(mob.Id, Is.EqualTo(GeneratedIds.For(worldId, mob.Scope, "mob", mob.Ordinal)), "the id's derivation");
            Assert.That(mob.Id.Length, Is.EqualTo(PersistentIds.NewId().Length), "as long as a random id");
        }

        Assert.That(GeneratedIds.For("0123456789abcdef0123456789abcdef", "clearing", "mob", 2), Is.EqualTo("6e514702bdc46a083fea5efa93f95e78"), "the derivation is fixed");
        Assert.That(GeneratedIds.For("0123456789abcdef0123456789abcdef", "wilds_3_1", "mob", 0), Is.EqualTo("49852fe71b7b0244e90df08228512ae1"), "the derivation is fixed");

        WorldComposition before = Row(1, 1, 1, 1, Vector2Int.zero).Compose();
        WorldComposition reseededAndMoved = Row(1, 1, 1, 6, new Vector2Int(3, 2)).Compose();
        WorldGenerator rerolled = Row(1, 1, 1, 1, Vector2Int.zero);
        rerolled.SetRecord(new ChunkRecord(new Vector2Int(4, 0), false, false, 11));
        rerolled.SetRecord(new ChunkRecord(new Vector2Int(4, 1), false, false, 12));
        WorldComposition again = Row(1, 1, 1, 1, Vector2Int.zero).Compose();
        Assert.That(again.Mobs.ConvertAll(mob => mob.Id), Is.EqualTo(before.Mobs.ConvertAll(mob => mob.Id)), "composed again");
        int kept = ExpectSameIds(before, reseededAndMoved, LavaFields) + ExpectSameIds(before, rerolled.Compose(), "wilds_4_");
        Assert.That(kept, Is.GreaterThan(0), "mobs compared");
    }

    /// <summary>
    /// A row of the Snowfield, the Lakeside and the Lava Fields across 6 x 2 chunks from the given seeds, the Lava
    /// Fields moved by <paramref name="lavaOffset"/>, with roads from each to the next.
    /// </summary>
    private WorldGenerator Row(int worldSeed, int snowfieldSeed, int lakesideSeed, int lavaFieldsSeed, Vector2Int lavaOffset)
    {
        WorldGenerator generator = Generator("Row", new Vector2Int(6, 2), null);
        generator.SetSeed(worldSeed);
        generator.SetRegions(
            new RegionPlacement(Snowfield, Generator("Snowfield"), snowfieldSeed, new Vector2Int(68, 64)),
            new RegionPlacement(Lakeside, Generator("Lakeside"), lakesideSeed, new Vector2Int(146, 64)),
            new RegionPlacement(LavaFields, Generator("LavaFields"), lavaFieldsSeed, new Vector2Int(225, 64) + lavaOffset));
        generator.SetLinks(
            Link(Snowfield, PortSide.East, Lakeside, PortSide.West, false, RoadLink.Surface.Stone),
            Link(Lakeside, PortSide.East, LavaFields, PortSide.West, false, RoadLink.Surface.Stone));
        return generator;
    }

    /// <summary>
    /// A world of 3 x 2 chunks, of which it has <paramref name="chunks"/>, with <paramref name="placements"/>.
    /// </summary>
    private WorldGenerator Small(List<Vector2Int> chunks, params RegionPlacement[] placements)
    {
        WorldGenerator generator = Generator("Small", new Vector2Int(3, 2), chunks);
        generator.SetRegions(placements);
        return generator;
    }

    /// <summary>
    /// An unsaved world generator for an unsaved world <paramref name="name"/> of <paramref name="size"/> chunks, which
    /// has <paramref name="chunks"/> (all of them when null).
    /// </summary>
    private WorldGenerator Generator(string name, Vector2Int size, List<Vector2Int> chunks)
    {
        return generators.Generator(name, size, chunks);
    }

    /// <summary>
    /// The project's area generator <paramref name="name"/>, which the tests only read.
    /// </summary>
    private static AreaGenerator Generator(string name)
    {
        return TestWorldGenerators.Area(name);
    }

    /// <summary>
    /// A road link 3 cells wide for stone and 2 for gravel.
    /// </summary>
    private static RoadLink Link(string from, PortSide fromSide, string to, PortSide toSide, bool toRoad, RoadLink.Surface surface)
    {
        return TestWorldGenerators.Link(from, fromSide, to, toSide, toRoad, surface);
    }

    /// <summary>
    /// The cells <paramref name="region"/> owns in <paramref name="composition"/>, in row order.
    /// </summary>
    private static List<Vector2Int> CellsOf(WorldComposition composition, WorldComposition.Region region)
    {
        List<Vector2Int> cells = new();
        RectInt bounds = region.Bounds;
        for (int y = bounds.yMin; y < bounds.yMax; y++)
        {
            for (int x = bounds.xMin; x < bounds.xMax; x++)
            {
                if (composition.RegionAt(new Vector2Int(x, y)) == region.Index)
                {
                    cells.Add(new Vector2Int(x, y));
                }
            }
        }

        return cells;
    }

    /// <summary>
    /// <paramref name="chunks"/> and every chunk beside one of them.
    /// </summary>
    private static HashSet<Vector2Int> Grown(HashSet<Vector2Int> chunks)
    {
        HashSet<Vector2Int> grown = new();
        foreach (Vector2Int chunk in chunks)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    grown.Add(chunk + new Vector2Int(dx, dy));
                }
            }
        }

        return grown;
    }

    /// <summary>
    /// Asserts that every chunk whose content hash differs between <paramref name="before"/> and
    /// <paramref name="after"/> lies in <paramref name="allowed"/>, and returns how many differ.
    /// </summary>
    private static int ChangedChunks(WorldComposition before, WorldComposition after, HashSet<Vector2Int> allowed, string change)
    {
        int changed = 0;
        foreach (WorldComposition.Chunk chunk in before.Chunks)
        {
            if (chunk.ContentHash != after.ChunkAt(chunk.Coords).ContentHash)
            {
                changed++;
                Assert.That(allowed.Contains(chunk.Coords), Is.True, $"{change} changed chunk {chunk.Coords}");
            }
        }

        return changed;
    }

    /// <summary>
    /// Asserts that every mob whose scope starts with <paramref name="scope"/> and whose ordinal both compositions
    /// have keeps its id, and returns how many were compared.
    /// </summary>
    private static int ExpectSameIds(WorldComposition before, WorldComposition after, string scope)
    {
        int compared = 0;
        foreach (WorldComposition.Mob mob in before.Mobs)
        {
            if (!mob.Scope.StartsWith(scope, StringComparison.Ordinal))
            {
                continue;
            }

            WorldComposition.Mob match = after.Mobs.Find(other => other.Scope == mob.Scope && other.Ordinal == mob.Ordinal);
            if (match.Id != null)
            {
                Assert.That(match.Id, Is.EqualTo(mob.Id), $"{mob.Scope}'s mob {mob.Ordinal} keeps its id");
                compared++;
            }
        }

        return compared;
    }

    /// <summary>
    /// Expects <paramref name="a"/> and <paramref name="b"/> to be the same composition, cell for cell and in order.
    /// </summary>
    private static void ExpectSame(WorldComposition a, WorldComposition b, string label)
    {
        Assert.That(b.Size, Is.EqualTo(a.Size), $"{label}: size");
        for (int y = 0; y < a.Size.y; y++)
        {
            for (int x = 0; x < a.Size.x; x++)
            {
                Vector2Int cell = new(x, y);
                Expect(a.MaskAt(cell) == b.MaskAt(cell) && a.RegionAt(cell) == b.RegionAt(cell) && a.RaisedAt(cell) == b.RaisedAt(cell), $"{label}: {cell} differs");
            }
        }

        Assert.That(b.Mobs.Count, Is.EqualTo(a.Mobs.Count), $"{label}: mobs");
        for (int i = 0; i < a.Mobs.Count; i++)
        {
            Assert.That((b.Mobs[i].Prefab, b.Mobs[i].Cell, b.Mobs[i].Id, b.Mobs[i].Scope, b.Mobs[i].Ordinal), Is.EqualTo((a.Mobs[i].Prefab, a.Mobs[i].Cell, a.Mobs[i].Id, a.Mobs[i].Scope, a.Mobs[i].Ordinal)), $"{label}: mob {i}");
        }

        Assert.That(b.Entries.ConvertAll(entry => (entry.Name, entry.Position, entry.Waystone)), Is.EqualTo(a.Entries.ConvertAll(entry => (entry.Name, entry.Position, entry.Waystone))), $"{label}: entries");
        Assert.That(b.Roads.Count, Is.EqualTo(a.Roads.Count), $"{label}: roads");
        for (int i = 0; i < a.Roads.Count; i++)
        {
            Assert.That(b.Roads[i].Cells, Is.EqualTo(a.Roads[i].Cells), $"{label}: road {i}'s cells");
        }

        Assert.That(b.Chunks.ConvertAll(chunk => chunk.ContentHash), Is.EqualTo(a.Chunks.ConvertAll(chunk => chunk.ContentHash)), $"{label}: chunk hashes");
        for (int i = 0; i < a.Chunks.Count; i++)
        {
            Assert.That(b.Chunks[i].Materials, Is.EqualTo(a.Chunks[i].Materials), $"{label}: chunk {a.Chunks[i].Coords}'s list");
        }

        Assert.That(b.Warnings, Is.EqualTo(a.Warnings), $"{label}: warnings");
    }

    /// <summary>
    /// Whether any cell of <paramref name="a"/> and <paramref name="b"/> holds other materials.
    /// </summary>
    private static bool DiffersInCells(WorldComposition a, WorldComposition b)
    {
        for (int y = 0; y < a.Size.y; y++)
        {
            for (int x = 0; x < a.Size.x; x++)
            {
                if (a.MaskAt(new Vector2Int(x, y)) != b.MaskAt(new Vector2Int(x, y)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Whether <paramref name="warning"/> is one of the placement checks'.
    /// </summary>
    private static bool IsPlacementWarning(string warning)
    {
        return warning.Contains("off the land") || warning.Contains("and the beach") || warning.Contains("fewer than the region gap") || warning.Contains("chunks the world does not have");
    }

    /// <summary>
    /// Whether the default movement profile walks each material of <paramref name="composition"/>'s layer order, by
    /// its terrain type.
    /// </summary>
    private bool[] WalkableMaterials(WorldComposition composition)
    {
        bool[] walkable = new bool[composition.LayerOrder.Count];
        for (int i = 0; i < walkable.Length; i++)
        {
            TerrainType2D terrainType = AssetDatabase.LoadAssetAtPath<TerrainType2D>(GroundLayerMigration.TerrainTypePath(composition.LayerOrder[i]));
            Assert.That(terrainType, Is.Not.Null, $"{composition.LayerOrder[i].name}'s terrain type");
            profile.TryGetTraversal(terrainType, out walkable[i], out _);
        }

        return walkable;
    }

    /// <summary>
    /// The cells 4-connected to <paramref name="start"/> over cells whose top material is walkable, off the cliff
    /// faces the style's cliff height covers whole, stepping between raised and level ground only where
    /// <see cref="RaisedGround.CanCross"/> allows.
    /// </summary>
    private HashSet<Vector2Int> Reachable(WorldComposition composition, bool[] walkable, Vector2Int start)
    {
        int faceRows = RaisedGround.RowsCovered(style.CliffHeight / TerrainStyle.PixelsPerCell);
        CompositionKinds kinds = new(composition);
        bool Open(Vector2Int cell) => composition.Contains(cell) && walkable[WorldComposition.TopIndex(composition.MaskAt(cell))]
            && (composition.RaisedAt(cell) == RaisedGround.Level || !RaisedGround.IsOnFace(kinds, new Vector3Int(cell.x, cell.y, 0), faceRows));
        HashSet<Vector2Int> reached = new();
        Assert.That(Open(start), Is.True, $"the start {start} is walkable");
        Queue<Vector2Int> queue = new();
        reached.Add(start);
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            Vector2Int cell = queue.Dequeue();
            foreach (Vector2Int step in Neighbours4)
            {
                Vector2Int next = cell + step;
                if (!reached.Contains(next) && Open(next) && RaisedGround.CanCross(composition.RaisedAt(cell), composition.RaisedAt(next), new Vector3Int(step.x, step.y, 0)))
                {
                    reached.Add(next);
                    queue.Enqueue(next);
                }
            }
        }

        return reached;
    }

    /// <summary>
    /// Fails with <paramref name="message"/> when <paramref name="condition"/> is false, formatting the message only
    /// then, for checks run for every cell.
    /// </summary>
    private static void Expect(bool condition, FormattableString message)
    {
        if (!condition)
        {
            Assert.Fail(message.ToString());
        }
    }
}
