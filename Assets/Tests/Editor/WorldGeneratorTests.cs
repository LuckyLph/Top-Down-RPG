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
/// mob is reachable from the default entry around the props' footprints, every port is linked and the roads and gates
/// lie on walkable wilds; the wilds' patches meet only grass and the wilds' mobs keep their distances from the entries
/// and every other mob; each region's props are its layout's, moved, but those that would cross a chunk's border or
/// stand on or beside its plaza; a waystone lamp stands on each plaza beside its waystone, off the roads, moving nothing
/// else; a pair of torches stands at each gate and lamp posts along the stone roads near the gates, beside the roads on
/// the wilds' grass, moving nothing but the walk-through props on their cells; the wilds' props stand on the wilds' grass inside their chunks, clear of the paths, the
/// beach, the entries, the mobs and each other's footprints, and edge trees only beside regions of grass; and the
/// derived ids are unique, in the format of the editor's random ids, and stay the same through re-seeding, moving,
/// re-rolling and composing again; and the region skies are full inside their regions, none outside and blend across
/// their outline, the same along a border in both chunks (Docs/Lighting.md, Phase 8). The generators carry the wilds' prop settings of the project's Overworld generator.
/// </summary>
public class WorldGeneratorTests
{
    private const string Snowfield = TestWorldGenerators.Snowfield;
    private const string Lakeside = TestWorldGenerators.Lakeside;
    private const string LavaFields = TestWorldGenerators.LavaFields;

    private const int JoinReach = 6;
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
    /// raised ground, mobs and their ids, entries, roads, props, chunk lists and hashes, warnings), over two world seeds and two
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
    /// Each region of the Overworld owns exactly its generator's seed layout's ground moved by its cell, and each of its
    /// cells holds exactly the layout's materials there, plus plaza within the plaza's radius of its spawn cell, but the
    /// sand of a nub where a sea region's beach meets the world's, cut back to ocean (Docs/KnownGaps.md, Phase 5); its
    /// raised ground, its ramps, its mobs (in order) and its spawn cell are the layout's, moved; and its props are the
    /// layout's props moved, in order, but each blocking prop whose footprint would cross a chunk's border and each prop
    /// on its plaza (a walk-through prop beside it too), which are left out.
    /// </summary>
    [Test]
    public void EachRegion_IsItsGeneratorsLayoutMovedByItsCell_PlusOnlyItsPlaza()
    {
        uint plaza = 1u << overworld.IndexOf(materials.Plaza);
        uint ocean = 1u << overworld.IndexOf(materials.Ocean);
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
                bool cutBack = region.IsSea && (expected & ocean) == 0 && (mask & ocean) != 0 && NearAJoin(region, cell);
                Expect(mask == expected || cutBack || (inDisc && mask == (expected | plaza)), $"{region.Key}: {cell} holds {mask}, its layout {expected}");
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
            List<WorldComposition.Prop> props = overworld.Props.FindAll(prop => prop.Scope == region.Key);
            int next = 0;
            int left = 0;
            foreach (AreaLayout.Prop prop in layout.Props)
            {
                Vector2Int cell = new Vector2Int(prop.Cell.x, prop.Cell.y) + region.Cell;
                Vector2Int covered = PropGridSnapping.TryGetFootprint(prop.Prefab, out Rect footprint) ? PropGridSnapping.CellsCovered(footprint, Vector2.one) : Vector2Int.zero;
                if (next < props.Count && props[next].Prefab == prop.Prefab && props[next].Cell == cell && props[next].Offset == prop.Offset)
                {
                    Assert.That(props[next].Covered, Is.EqualTo(covered), $"{region.Key}'s prop at {cell} covers its footprint's cells");
                    next++;
                    continue;
                }

                left++;
                Vector2Int far = cell + Vector2Int.Max(covered, Vector2Int.one) - Vector2Int.one;
                bool crosses = ChunkCoords.ChunkOf(cell) != ChunkCoords.ChunkOf(far);
                int reach = covered == Vector2Int.zero ? 1 : 0;
                bool onPlaza = false;
                for (int dy = -reach; dy <= far.y - cell.y + reach; dy++)
                {
                    for (int dx = -reach; dx <= far.x - cell.x + reach; dx++)
                    {
                        Vector2Int near = cell + new Vector2Int(dx, dy);
                        onPlaza |= overworld.RegionAt(near) == region.Index && overworld.Has(near, materials.Plaza);
                    }
                }

                Assert.That(crosses || onPlaza, Is.True, $"{region.Key}'s {prop.Prefab.name} at {cell} is left out, neither across a chunk's border nor on its plaza");
            }

            Assert.That(next, Is.EqualTo(props.Count), $"{region.Key}'s props are its layout's, in order");
            Assert.That(left, Is.LessThan(layout.Props.Count / 20 + 2), $"{region.Key} leaves out only a few props");
        }
    }

    /// <summary>
    /// With a waystone lamp (Docs/Lighting.md, Phase 5), one more prop stands on each region's plaza than without it: the
    /// lamp, within the plaza's radius of its waystone and more than a cell and a half from it, clear of the players
    /// arriving there, every cell it covers the region's plaza off its roads and in one chunk; every other prop is the
    /// composition's without it, in order.
    /// </summary>
    [Test]
    public void AWaystoneLamp_StandsOnEachPlaza_BesideItsWaystone_OffTheRoads()
    {
        GameObject lamp = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Props/Lights/LampPost_0.prefab");
        Assert.That(lamp, Is.Not.Null, "the lamp post");
        WorldGenerator lit = generators.Overworld("LitOverworld");
        lit.SetWaystoneLamp(lamp);
        WorldComposition composed = lit.Compose();
        List<WorldComposition.Prop> others = new();
        Dictionary<string, WorldComposition.Prop> added = new();
        foreach (WorldComposition.Prop prop in composed.Props)
        {
            if (prop.Prefab == lamp && !overworld.Props.Exists(other => other.Prefab == lamp && other.Cell == prop.Cell))
            {
                Assert.That(added.ContainsKey(prop.Scope), Is.False, $"{prop.Scope} has one waystone lamp");
                added[prop.Scope] = prop;
                continue;
            }

            others.Add(prop);
        }

        Assert.That(others.Count, Is.EqualTo(overworld.Props.Count), "nothing else is placed or left out");
        for (int i = 0; i < others.Count; i++)
        {
            Assert.That((others[i].Prefab, others[i].Cell), Is.EqualTo((overworld.Props[i].Prefab, overworld.Props[i].Cell)), $"prop {i}");
        }

        foreach (WorldComposition.Region region in composed.Regions)
        {
            Assert.That(added.TryGetValue(region.Key, out WorldComposition.Prop placed), Is.True, $"{region.Key}'s waystone lamp");
            float distance = Vector2.Distance(placed.Cell + new Vector2(0.5f, 0.5f), region.SpawnCell + new Vector2(0.5f, 0.5f));
            Assert.That(distance, Is.InRange(1.5f, lit.PlazaRadius), $"{region.Key}'s lamp stands beside its waystone");
            Assert.That(ChunkCoords.ChunkOf(placed.Cell), Is.EqualTo(ChunkCoords.ChunkOf(placed.Cell + placed.Covered - Vector2Int.one)), $"{region.Key}'s lamp lies in one chunk");
            for (int dy = 0; dy < placed.Covered.y; dy++)
            {
                for (int dx = 0; dx < placed.Covered.x; dx++)
                {
                    Vector2Int cell = placed.Cell + new Vector2Int(dx, dy);
                    Assert.That(composed.RegionAt(cell), Is.EqualTo(region.Index), $"{region.Key}'s lamp at {cell}");
                    Assert.That(composed.Has(cell, materials.Plaza), Is.True, $"{region.Key}'s lamp at {cell} stands on plaza");
                    Assert.That(composed.Has(cell, materials.Stone) || composed.Has(cell, materials.Gravel), Is.False, $"{region.Key}'s lamp at {cell} stands off the roads");
                }
            }
        }
    }

    /// <summary>
    /// With gate torches and a road lamp (Docs/Lighting.md, Phase 6): two torches stand at each gate, the first on its
    /// west or south side and the second on the other, each on the first cell off the road across it from the gate; lamp
    /// posts stand along the stone roads, none along the gravel ones, each on the wilds' grass beside a stone road, within
    /// the road lamp reach of a gate and no nearer a waystone or a gate than the entry clearance, half the spacing or more
    /// from each other; no light stands on or beside a mob; every other prop is the composition's without them, in order,
    /// but the walk-through props on the lights' cells, and the mobs are the same, from the same scopes and ordinals,
    /// which their ids derive from.
    /// </summary>
    [Test]
    public void GateTorchesAndRoadLamps_StandBesideTheRoads_MovingNothingElse()
    {
        GameObject[] torches =
        {
            AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Props/Lights/Torch_0.prefab"),
            AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Props/Lights/Torch_1.prefab")
        };
        GameObject lamp = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Props/Lights/LampPost_0.prefab");
        WorldGenerator lit = generators.Overworld("RoadLitOverworld");
        lit.SetLights(torches, lamp);
        WorldComposition composed = lit.Compose();
        List<WorldComposition.Prop> gateTorches = composed.Props.FindAll(prop => prop.Scope == "gates");
        List<WorldComposition.Prop> roadLamps = composed.Props.FindAll(prop => prop.Scope == "roads");
        List<WorldComposition.Prop> others = composed.Props.FindAll(prop => prop.Scope != "gates" && prop.Scope != "roads");
        HashSet<Vector2Int> lightCells = new();
        foreach (WorldComposition.Prop light in composed.Props)
        {
            if (light.Scope == "gates" || light.Scope == "roads")
            {
                lightCells.Add(light.Cell);
            }
        }

        int next = 0;
        foreach (WorldComposition.Prop prop in overworld.Props)
        {
            if (next < others.Count && (others[next].Prefab, others[next].Cell, others[next].Offset) == (prop.Prefab, prop.Cell, prop.Offset))
            {
                next++;
                continue;
            }

            Assert.That(prop.Blocks, Is.False, $"{prop.Prefab.name} at {prop.Cell} left out");
            Assert.That(lightCells.Contains(prop.Cell), Is.True, $"{prop.Prefab.name} at {prop.Cell} left out off the lights");
        }

        Assert.That(next, Is.EqualTo(others.Count), "nothing else is placed");
        Assert.That(composed.Mobs.Count, Is.EqualTo(overworld.Mobs.Count));
        for (int i = 0; i < composed.Mobs.Count; i++)
        {
            Assert.That((composed.Mobs[i].Cell, composed.Mobs[i].Scope, composed.Mobs[i].Ordinal), Is.EqualTo((overworld.Mobs[i].Cell, overworld.Mobs[i].Scope, overworld.Mobs[i].Ordinal)), $"mob {i}");
        }

        List<Vector2> gates = new();
        List<Vector2> entries = new();
        foreach (WorldDefinition.Entry entry in composed.Entries)
        {
            entries.Add(entry.Position);
            if (!entry.Waystone)
            {
                gates.Add(entry.Position);
            }
        }

        Assert.That(gateTorches.Count, Is.EqualTo(gates.Count * 2), "two torches at each gate");
        foreach (Vector2 gate in gates)
        {
            List<WorldComposition.Prop> pair = gateTorches.FindAll(torch => Vector2.Distance(torch.Cell + new Vector2(0.5f, 0.5f), gate) <= 3.5f);
            Assert.That(pair.Count, Is.EqualTo(2), $"the gate at {gate}'s torches");
            pair.Sort((a, b) => (a.Cell.x + a.Cell.y).CompareTo(b.Cell.x + b.Cell.y));
            Assert.That((pair[0].Prefab, pair[1].Prefab), Is.EqualTo((torches[0], torches[1])), $"the gate at {gate}'s torches in order");
            Vector2Int gateCell = Vector2Int.FloorToInt(gate);
            Assert.That(pair[0].Cell.x == gateCell.x || pair[0].Cell.y == gateCell.y, Is.True, $"the gate at {gate}'s torches stand across the road from it");
            foreach (WorldComposition.Prop torch in pair)
            {
                AssertBesideTheRoad(composed, torch.Cell, $"the gate at {gate}'s torch at {torch.Cell}");
            }
        }

        Assert.That(roadLamps.Count, Is.GreaterThan(10), "lamps along the roads");
        foreach (WorldComposition.Prop roadLamp in roadLamps)
        {
            Vector2 at = roadLamp.Cell + new Vector2(0.5f, 0.5f);
            Assert.That(roadLamp.Prefab, Is.EqualTo(lamp));
            bool besideStone = false;
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    besideStone |= composed.Has(roadLamp.Cell + new Vector2Int(dx, dy), materials.Stone);
                }
            }

            Assert.That(besideStone, Is.True, $"the lamp at {roadLamp.Cell} stands beside a stone road");
            AssertBesideTheRoad(composed, roadLamp.Cell, $"the lamp at {roadLamp.Cell}");
            Assert.That(gates.Exists(gate => Vector2.Distance(gate, at) <= lit.RoadLampReach + 3f), Is.True, $"the lamp at {roadLamp.Cell} stands near a gate");
            Assert.That(entries.TrueForAll(entry => Vector2.Distance(entry, at) >= lit.RoadLampEntryClearance - 2f), Is.True, $"the lamp at {roadLamp.Cell} keeps from the entries");
            foreach (WorldComposition.Prop other in roadLamps)
            {
                if (other.Cell != roadLamp.Cell)
                {
                    Assert.That(Vector2.Distance(other.Cell, roadLamp.Cell), Is.GreaterThanOrEqualTo(lit.RoadLampSpacing * 0.5f), $"the lamps at {roadLamp.Cell} and {other.Cell}");
                }
            }
        }

        foreach (Vector2Int cell in lightCells)
        {
            foreach (WorldComposition.Mob mob in composed.Mobs)
            {
                Assert.That(Mathf.Abs(mob.Cell.x - cell.x) > 1 || Mathf.Abs(mob.Cell.y - cell.y) > 1, Is.True, $"the light at {cell} beside the mob at {mob.Cell}");
            }
        }
    }

    /// <summary>
    /// Asserts <paramref name="cell"/> is wilds ground of grass and its patches, off the roads and the beach, beside a
    /// road cell.
    /// </summary>
    private void AssertBesideTheRoad(WorldComposition composed, Vector2Int cell, string what)
    {
        Assert.That(composed.RegionAt(cell), Is.EqualTo(WorldComposition.Wilds), $"{what} stands in the wilds");
        TerrainMaterial top = composed.TopAt(cell);
        Assert.That(top == materials.Grass || top == materials.Weeds || top == materials.Meadow || top == materials.TallGrass, Is.True, $"{what} stands on the wilds' grass, not {top}");
        bool beside = false;
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                Vector2Int near = cell + new Vector2Int(dx, dy);
                beside |= composed.Has(near, materials.Stone) || composed.Has(near, materials.Gravel);
            }
        }

        Assert.That(beside, Is.True, $"{what} stands beside the road");
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
    /// sides with no nub of sand a cell or two wide poking into the sea at either join (Docs/KnownGaps.md, Phase 5), and
    /// the ocean, the region's or the world's, meets only sand or ocean; a Coast placed inland is warned about.
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
        foreach (int end in new[] { coast.Bounds.xMin, coast.Bounds.xMax - 1 })
        {
            int[] coastRow = new int[2 * JoinReach + 1];
            for (int i = 0; i < coastRow.Length; i++)
            {
                int x = end - JoinReach + i;
                int y = overworld.Size.y - 1;
                while (y >= 0 && (overworld.MaskAt(new Vector2Int(x, y)) & ocean) != 0)
                {
                    y--;
                }

                coastRow[i] = y;
            }

            for (int i = 1; i < coastRow.Length - 1; i++)
            {
                for (int width = 1; width <= 2 && i + width < coastRow.Length; width++)
                {
                    bool run = true;
                    for (int j = i; j < i + width; j++)
                    {
                        run &= coastRow[j] == coastRow[i];
                    }

                    Assert.That(run && coastRow[i] > coastRow[i - 1] && coastRow[i] > coastRow[i + width], Is.False, $"a nub of sand {width} wide pokes into the sea at x {end - JoinReach + i}, the Coast's join at {end}");
                }
            }
        }
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
    /// over 4-connected walkable cells, off the cliff faces and the props' footprints and up the ramps only; every port
    /// is linked; every road cell lies on the wilds and is walkable, and every gate stands on its road.
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
    /// The Overworld's wilds lay props, each of a chunk's own scope and inside it, a cell from its border: on the wilds'
    /// grass and its patches, beside a region at the nearest; a blocking prop more than the props' path clearance from
    /// every road and beach cell, on cells no other prop takes and with no other footprint beside it; a walk-through prop
    /// off the tall grass and more than a step from every road and beach cell, in a cell no other prop takes; and every
    /// prop at least the entry clearance from every waystone and gate and more than a cell from every mob.
    /// </summary>
    [Test]
    public void TheWildsProps_StandOnTheWildsGrassInsideTheirChunks_ClearOfThePathsTheEntriesTheMobsAndEachOther()
    {
        uint grassy = (1u << overworld.IndexOf(materials.Grass)) | (1u << overworld.IndexOf(materials.Weeds)) | (1u << overworld.IndexOf(materials.Meadow)) | (1u << overworld.IndexOf(materials.TallGrass));
        uint tall = 1u << overworld.IndexOf(materials.TallGrass);
        uint paths = (1u << overworld.IndexOf(materials.Stone)) | (1u << overworld.IndexOf(materials.Gravel)) | (1u << overworld.IndexOf(materials.Plaza)) | (1u << overworld.IndexOf(materials.Sand));
        Dictionary<Vector2Int, int> taken = new();
        for (int i = 0; i < overworld.Props.Count; i++)
        {
            foreach (Vector2Int cell in CellsOf(overworld.Props[i]))
            {
                Assert.That(taken.TryAdd(cell, i), Is.True, $"{cell} holds two props");
            }
        }

        List<WorldComposition.Prop> wilds = overworld.Props.FindAll(prop => prop.Scope.StartsWith("wilds_", StringComparison.Ordinal));
        Assert.That(wilds.FindAll(prop => prop.Blocks).Count, Is.GreaterThan(100), "the wilds lay blocking props");
        Assert.That(wilds.FindAll(prop => !prop.Blocks).Count, Is.GreaterThan(20), "the wilds lay walk-through props");
        int clearance = overworldGenerator.PropPathClearance;
        foreach (WorldComposition.Prop prop in wilds)
        {
            Vector2Int chunk = prop.Chunk;
            Assert.That(prop.Scope, Is.EqualTo($"wilds_{chunk.x}_{chunk.y}"), $"the prop at {prop.Cell} is its chunk's");
            RectInt inside = ChunkCoords.Bounds(chunk);
            inside = new RectInt(inside.xMin + 1, inside.yMin + 1, inside.width - 2, inside.height - 2);
            List<Vector2Int> cells = CellsOf(prop);
            int reach = prop.Blocks ? clearance : 1;
            foreach (Vector2Int cell in cells)
            {
                Expect(inside.Contains(cell), $"the wilds prop at {prop.Cell} covers {cell}, outside its chunk or on its border");
                Expect(overworld.RegionAt(cell) == WorldComposition.Wilds && (overworld.MaskAt(cell) & ~grassy) == 0, $"the wilds prop at {prop.Cell} covers {cell}, off the wilds' grass");
                Expect(prop.Blocks || (overworld.MaskAt(cell) & tall) == 0, $"the wilds plant at {prop.Cell} stands on tall grass");
                for (int dy = -reach; dy <= reach; dy++)
                {
                    for (int dx = -reach; dx <= reach; dx++)
                    {
                        Vector2Int near = cell + new Vector2Int(dx, dy);
                        Expect((overworld.MaskAt(near) & paths) == 0 || overworld.RegionAt(near) >= 0, $"the wilds prop at {prop.Cell} within {reach} of a path or the beach at {near}");
                        Expect(Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) > 1 || !overworld.Mobs.Exists(mob => mob.Cell == near), $"the wilds prop at {prop.Cell} beside the mob at {near}");
                    }
                }

                foreach (WorldDefinition.Entry entry in overworld.Entries)
                {
                    Expect(Vector2.Distance(cell + new Vector2(0.5f, 0.5f), entry.Position) >= overworldGenerator.PropEntryClearance, $"the wilds prop at {prop.Cell} near {entry.Name}");
                }

                if (prop.Blocks)
                {
                    foreach (Vector2Int step in Neighbours4)
                    {
                        Vector2Int beside = cell + step;
                        Expect(cells.Contains(beside) || !taken.TryGetValue(beside, out int other) || !overworld.Props[other].Blocks, $"the wilds prop at {prop.Cell} beside the footprint at {beside}");
                    }
                }
            }
        }
    }

    /// <summary>
    /// With only the wilds' Edge layer, a row of the Snowfield, the Lakeside and the Lava Fields gets edge trees beside
    /// the Lakeside, whose ground is grass, and none within the edge reach of the snow or the scorched earth.
    /// </summary>
    [Test]
    public void EdgeTrees_GrowOnlyBesideTheRegionsOfGrass()
    {
        WorldGenerator generator = Row(1, 1, 1, 1, Vector2Int.zero);
        SerializedObject settings = new(generator);
        SerializedProperty layers = settings.FindProperty("wildsProps");
        for (int i = layers.arraySize - 1; i >= 0; i--)
        {
            if (layers.GetArrayElementAtIndex(i).FindPropertyRelative("zone").enumValueIndex != (int)WorldGenerator.WildsZone.Edge)
            {
                layers.DeleteArrayElementAtIndex(i);
            }
        }

        Assert.That(layers.arraySize, Is.GreaterThan(0), "the Overworld's generator has an Edge layer");
        settings.ApplyModifiedPropertiesWithoutUndo();
        WorldComposition row = generator.Compose();
        int reach = generator.EdgeReach;
        int besideLakeside = 0;
        foreach (WorldComposition.Prop prop in row.Props.FindAll(prop => prop.Scope.StartsWith("wilds_", StringComparison.Ordinal)))
        {
            string nearest = null;
            for (int dy = -reach; dy <= reach && nearest == null; dy++)
            {
                for (int dx = -reach; dx <= reach && nearest == null; dx++)
                {
                    int owner = row.RegionAt(prop.Cell + new Vector2Int(dx, dy));
                    nearest = owner >= 0 ? row.Regions[owner].Key : null;
                }
            }

            Assert.That(nearest, Is.EqualTo(Lakeside), $"the edge prop at {prop.Cell} grows beside {nearest ?? "no region"}");
            besideLakeside++;
        }

        Assert.That(besideLakeside, Is.GreaterThan(20), "edge trees beside the Lakeside");
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
    /// The region skies (Docs/Lighting.md, Phase 8): the chunks near a region whose generator names a sky list it, and
    /// list only those; a sky weighs full at the samples more than half the blend inside its regions' cells, nothing at
    /// those more than half the blend outside them and half on their outline, from the nearest cell centres in and out;
    /// and the samples two chunks share along their border are the same in both.
    /// </summary>
    [Test]
    public void TheRegionSkies_AreFullInsideTheirRegions_NoneOutside_AndBlendAcrossTheirOutline()
    {
        float half = overworldGenerator.SkyBlend * 0.5f;
        int reach = Mathf.CeilToInt(half) + 3;
        HashSet<RegionSky> named = new();
        foreach (WorldComposition.Region region in overworld.Regions)
        {
            if (region.Generator.Sky != null)
            {
                named.Add(region.Generator.Sky);
            }
        }

        Assert.That(named.Count, Is.EqualTo(5), "Every region but the Clearing names a sky.");
        int full = 0;
        int none = 0;
        int outline = 0;
        foreach (WorldComposition.Chunk chunk in overworld.Chunks)
        {
            RectInt bounds = ChunkCoords.Bounds(chunk.Coords);
            Assert.That(chunk.SkyWeights.Length, Is.EqualTo(chunk.Skies.Count * ChunkSky.SamplesPerSky), $"chunk {chunk.Coords}'s samples");
            for (int s = 0; s < chunk.Skies.Count; s++)
            {
                RegionSky sky = chunk.Skies[s];
                Assert.That(named.Contains(sky), Is.True, $"chunk {chunk.Coords}: {sky} is a region's");
                for (int j = 0; j < ChunkSky.Samples; j++)
                {
                    for (int i = 0; i < ChunkSky.Samples; i++)
                    {
                        Vector2Int corner = new(bounds.xMin + i * ChunkSky.Spacing, bounds.yMin + j * ChunkSky.Spacing);
                        byte weight = chunk.SkyWeights[s * ChunkSky.SamplesPerSky + j * ChunkSky.Samples + i];
                        SkyDistances(overworld, sky, corner, reach, out float toSky, out float toOther);
                        if (toOther > half + 0.75f)
                        {
                            Expect(weight == 255, $"chunk {chunk.Coords}: {sky.name} full at {corner}, {toOther} cells from the next other cell");
                            full++;
                        }
                        else if (toSky > half + 0.75f)
                        {
                            Expect(weight == 0, $"chunk {chunk.Coords}: {sky.name} none at {corner}, {toSky} cells from its nearest");
                            none++;
                        }
                        else if (Mathf.Abs(toSky - toOther) < 1e-3f)
                        {
                            Expect(Mathf.Abs(weight - 128) <= 1, $"chunk {chunk.Coords}: {sky.name} half at {corner} on the outline, {weight}");
                            outline++;
                        }
                    }
                }
            }

            WorldComposition.Chunk right = overworld.ChunkAt(chunk.Coords + Vector2Int.right);
            WorldComposition.Chunk up = overworld.ChunkAt(chunk.Coords + Vector2Int.up);
            foreach (RegionSky sky in named)
            {
                for (int k = 0; k < ChunkSky.Samples; k++)
                {
                    if (right != null)
                    {
                        Expect(SkyWeight(chunk, sky, ChunkSky.Samples - 1, k) == SkyWeight(right, sky, 0, k), $"chunk {chunk.Coords}'s east border, {sky.name} sample {k}");
                    }

                    if (up != null)
                    {
                        Expect(SkyWeight(chunk, sky, k, ChunkSky.Samples - 1) == SkyWeight(up, sky, k, 0), $"chunk {chunk.Coords}'s north border, {sky.name} sample {k}");
                    }
                }
            }
        }

        Assert.That(full, Is.GreaterThan(0), "Samples deep inside a region.");
        Assert.That(none, Is.GreaterThan(0), "Samples far outside.");
        Assert.That(outline, Is.GreaterThan(0), "Samples on an outline.");
    }

    /// <summary>
    /// The distances from <paramref name="corner"/> to the nearest centre of a cell under <paramref name="sky"/> and of
    /// one not, searched within <paramref name="reach"/> cells, each that reach when none lies within it.
    /// </summary>
    private static void SkyDistances(WorldComposition composition, RegionSky sky, Vector2Int corner, int reach, out float toSky, out float toOther)
    {
        toSky = reach;
        toOther = reach;
        for (int dy = -reach; dy < reach; dy++)
        {
            for (int dx = -reach; dx < reach; dx++)
            {
                Vector2Int cell = corner + new Vector2Int(dx, dy);
                int region = composition.RegionAt(cell);
                bool under = region >= 0 && composition.Regions[region].Generator.Sky == sky;
                float distance = new Vector2(dx + 0.5f, dy + 0.5f).magnitude;
                if (under)
                {
                    toSky = Mathf.Min(toSky, distance);
                }
                else
                {
                    toOther = Mathf.Min(toOther, distance);
                }
            }
        }
    }

    /// <summary>
    /// <paramref name="sky"/>'s weight at sample (<paramref name="i"/>, <paramref name="j"/>) of
    /// <paramref name="chunk"/>, 0 when the chunk does not list it.
    /// </summary>
    private static int SkyWeight(WorldComposition.Chunk chunk, RegionSky sky, int i, int j)
    {
        int index = chunk.Skies.IndexOf(sky);
        return index < 0 ? 0 : chunk.SkyWeights[index * ChunkSky.SamplesPerSky + j * ChunkSky.Samples + i];
    }

    /// <summary>
    /// A row of the Snowfield, the Lakeside and the Lava Fields across 6 x 2 chunks from the given seeds, the Lava
    /// Fields moved by <paramref name="lavaOffset"/>, with roads from each to the next and the wilds' props.
    /// </summary>
    private WorldGenerator Row(int worldSeed, int snowfieldSeed, int lakesideSeed, int lavaFieldsSeed, Vector2Int lavaOffset)
    {
        WorldGenerator generator = TestWorldGenerators.WithWildsProps(Generator("Row", new Vector2Int(6, 2), null));
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
    /// Whether <paramref name="cell"/> lies within the join smoothing's reach of either end of
    /// <paramref name="region"/>'s span along the north or south side its ocean faces in the Overworld.
    /// </summary>
    private static bool NearAJoin(WorldComposition.Region region, Vector2Int cell)
    {
        return Mathf.Abs(cell.x - region.Bounds.xMin) <= JoinReach || Mathf.Abs(cell.x - (region.Bounds.xMax - 1)) <= JoinReach;
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
    /// The cells <paramref name="prop"/>'s footprint covers, or the cell it stands in.
    /// </summary>
    private static List<Vector2Int> CellsOf(WorldComposition.Prop prop)
    {
        List<Vector2Int> cells = new();
        Vector2Int size = prop.Blocks ? prop.Covered : Vector2Int.one;
        for (int dy = 0; dy < size.y; dy++)
        {
            for (int dx = 0; dx < size.x; dx++)
            {
                cells.Add(prop.Cell + new Vector2Int(dx, dy));
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

        Assert.That(b.Props.Count, Is.EqualTo(a.Props.Count), $"{label}: props");
        for (int i = 0; i < a.Props.Count; i++)
        {
            Assert.That((b.Props[i].Prefab, b.Props[i].Cell, b.Props[i].Offset, b.Props[i].Covered, b.Props[i].Scope), Is.EqualTo((a.Props[i].Prefab, a.Props[i].Cell, a.Props[i].Offset, a.Props[i].Covered, a.Props[i].Scope)), $"{label}: prop {i}");
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
    /// faces the style's cliff height covers whole and the props' footprints, stepping between raised and level ground
    /// only where <see cref="RaisedGround.CanCross"/> allows.
    /// </summary>
    private HashSet<Vector2Int> Reachable(WorldComposition composition, bool[] walkable, Vector2Int start)
    {
        int faceRows = RaisedGround.RowsCovered(style.CliffHeight / TerrainStyle.PixelsPerCell);
        CompositionKinds kinds = new(composition);
        HashSet<Vector2Int> footprints = new();
        foreach (WorldComposition.Prop prop in composition.Props)
        {
            if (prop.Blocks)
            {
                footprints.UnionWith(CellsOf(prop));
            }
        }

        bool Open(Vector2Int cell) => composition.Contains(cell) && !footprints.Contains(cell) && walkable[WorldComposition.TopIndex(composition.MaskAt(cell))]
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
