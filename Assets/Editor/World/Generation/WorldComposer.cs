using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEditor;
using UnityEngine;
using Random = System.Random;

/// <summary>
/// Composes a world from a <see cref="WorldGenerator"/>'s settings (Docs/WorldGeneration.md: Architecture:
/// Composition), in order: the regions laid from their seeds and moved into place cell for cell, with their outlines,
/// ports and spawn cells; the land, the sea and the world's beach, joined to a sea region's own coast; the wilds' grass;
/// the roads between the ports, each the cheapest route over the wilds sampled into a spline; the waystones with their
/// plazas, and the gates; the wilds' patches and mobs, per chunk on a stream of its own; each chunk's material list and
/// content hash; and the checks, each a warning. Every cell's materials are a bit per material of the style's layer
/// order in a dense array over the world, with the region owning each cell and its raised ground beside them, and
/// every step walks ordered lists or the arrays in row order, never a hash set, so the same settings always give the
/// same composition.
/// </summary>
internal sealed class WorldComposer
{
    private const int CoastHarmonics = 3;
    private const int OutlineHarmonics = 4;
    private const float CornerBlend = 8f;
    private const int HeldColumns = 3;
    private const int PatchMargin = 3;
    private const int MinPatchPiece = 4;
    private const float PatchSquash = 0.8f;
    private const float PatchWobble = 0.25f;
    private const int MobChunkMargin = 5;
    private const int StepCost = 10;
    private const int DiagonalCost = 14;
    private const int AvoidCost = 6;
    private const int CrossingCost = 80;
    private const int MaxPull = 48;
    private const float LineStep = 0.25f;
    private const float PaintAllowance = 0.05f;
    private const byte Far = byte.MaxValue;
    private const string MobKind = "mob";
    private const int ListedInWarning = 3;

    private static readonly Vector2Int[] Neighbours4 = { Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down };
    private static readonly Vector2Int[] Neighbours8 =
    {
        new(1, 0), new(0, 1), new(-1, 0), new(0, -1), new(1, 1), new(-1, 1), new(-1, -1), new(1, -1)
    };

    /// <summary>
    /// The cells waiting in an A* search, the cheapest estimate first, then the nearest to the goal, then the lowest
    /// index, so the order of a search is fixed.
    /// </summary>
    private sealed class OpenCells
    {
        private readonly List<long> scores = new();
        private readonly List<int> cells = new();

        public int Count => cells.Count;

        /// <summary>
        /// Clears the queue for a new search.
        /// </summary>
        public void Clear()
        {
            scores.Clear();
            cells.Clear();
        }

        /// <summary>
        /// Adds <paramref name="cell"/> with its estimate <paramref name="estimate"/> and its heuristic
        /// <paramref name="remaining"/>.
        /// </summary>
        public void Push(int cell, int estimate, int remaining)
        {
            long score = ((long)estimate << 42) | ((long)Mathf.Min(remaining, (1 << 21) - 1) << 21) | (uint)(cell & ((1 << 21) - 1));
            scores.Add(score);
            cells.Add(cell);
            int child = cells.Count - 1;
            while (child > 0)
            {
                int parent = (child - 1) / 2;
                if (scores[parent] <= scores[child])
                {
                    break;
                }

                Swap(parent, child);
                child = parent;
            }
        }

        /// <summary>
        /// Removes and returns the cell with the lowest score.
        /// </summary>
        public int Pop()
        {
            int top = cells[0];
            int last = cells.Count - 1;
            Swap(0, last);
            scores.RemoveAt(last);
            cells.RemoveAt(last);
            int parent = 0;
            while (true)
            {
                int left = parent * 2 + 1;
                int right = left + 1;
                int smallest = parent;
                if (left < cells.Count && scores[left] < scores[smallest])
                {
                    smallest = left;
                }

                if (right < cells.Count && scores[right] < scores[smallest])
                {
                    smallest = right;
                }

                if (smallest == parent)
                {
                    break;
                }

                Swap(parent, smallest);
                parent = smallest;
            }

            return top;
        }

        /// <summary>
        /// Swaps the entries at <paramref name="a"/> and <paramref name="b"/>.
        /// </summary>
        private void Swap(int a, int b)
        {
            (scores[a], scores[b]) = (scores[b], scores[a]);
            (cells[a], cells[b]) = (cells[b], cells[a]);
        }
    }

    /// <summary>
    /// The raised ground of a composition as a source of cell kinds for <see cref="RaisedGround"/>'s face rule.
    /// </summary>
    private readonly struct RaisedKinds : RaisedGround.IKinds
    {
        private readonly WorldComposition composition;

        /// <summary>
        /// Reads the raised ground of <paramref name="composition"/>.
        /// </summary>
        public RaisedKinds(WorldComposition composition)
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

    private readonly WorldGenerator settings;
    private readonly WorldComposition composition;
    private readonly int width;
    private readonly int height;
    private readonly uint[] cells;
    private readonly short[] owners;
    private readonly byte[] raised;
    private readonly string worldId;
    private readonly List<AreaLayout> layouts = new();
    private readonly List<int> offLand = new();
    private readonly Dictionary<WorldComposition.Port, int> portWidths = new();
    private readonly Dictionary<WorldComposition.Port, List<WorldComposition.Road>> portRoads = new();
    private readonly Stopwatch watch = new();
    private uint grass;
    private uint sand;
    private uint ocean;
    private uint stone;
    private uint gravel;
    private uint plaza;
    private uint weeds;
    private uint meadow;
    private uint tallGrass;
    private uint paths;
    private byte[] regionSteps;
    private short[] nearestRegion;
    private byte[] beachSteps;
    private byte[] roadSteps;
    private int[,] regionDistances;
    private int[] shoreDistances;
    private int[] queue;
    private int[] costs;
    private int[] parents;
    private int[] searched;
    private int[] closed;
    private int[] goals;
    private bool[] chunkExists;
    private int searchId;
    private readonly OpenCells open = new();

    /// <summary>
    /// Prepares the composition of <paramref name="generator"/>'s world.
    /// </summary>
    private WorldComposer(WorldGenerator generator, Vector2Int sizeInChunks)
    {
        settings = generator;
        composition = new WorldComposition(sizeInChunks, generator.TerrainStyle);
        width = composition.Size.x;
        height = composition.Size.y;
        cells = composition.materialBits;
        owners = composition.owners;
        raised = composition.raised;
        worldId = !string.IsNullOrEmpty(generator.World.WorldId) ? generator.World.WorldId : generator.World.name;
        Array.Fill(owners, (short)WorldComposition.Sea);
    }

    /// <summary>
    /// The composition of <paramref name="generator"/>'s world, or one holding only a warning when its world, style or
    /// materials cannot be composed.
    /// </summary>
    public static WorldComposition Compose(WorldGenerator generator)
    {
        string problem = Problem(generator);
        if (problem != null)
        {
            WorldComposition failed = new(Vector2Int.zero, null);
            failed.Warnings.Add(problem);
            return failed;
        }

        return new WorldComposer(generator, generator.World.SizeInChunks).Run();
    }

    /// <summary>
    /// Why <paramref name="generator"/>'s world cannot be composed, or null when it can: it needs a world, a terrain
    /// style whose layer order fits in a cell's mask and lists every one of the world's own materials.
    /// </summary>
    private static string Problem(WorldGenerator generator)
    {
        if (generator == null || generator.World == null)
        {
            return "The world generator has no world.";
        }

        TerrainStyle style = generator.TerrainStyle;
        if (style == null)
        {
            return "The world generator has no terrain style.";
        }

        if (style.LayerOrder.Count > 32)
        {
            return $"{style.name}'s layer order lists {style.LayerOrder.Count} materials, more than a cell's mask holds (32).";
        }

        WorldGenerator.GroundMaterials own = generator.Materials;
        TerrainMaterial[] needed = { own.Grass, own.Sand, own.Ocean, own.Stone, own.Gravel, own.Plaza, own.Weeds, own.Meadow, own.TallGrass };
        string[] names = { "grass", "sand", "ocean", "stone", "gravel", "plaza", "weeds", "meadow", "tall grass" };
        for (int i = 0; i < needed.Length; i++)
        {
            if (needed[i] == null || style.LayerOrderIndexOf(needed[i]) < 0)
            {
                return $"The world generator's {names[i]} material is missing or not in {style.name}'s layer order.";
            }
        }

        return null;
    }

    /// <summary>
    /// Runs every step in order and returns the composition.
    /// </summary>
    private WorldComposition Run()
    {
        grass = Bit(settings.Materials.Grass);
        sand = Bit(settings.Materials.Sand);
        ocean = Bit(settings.Materials.Ocean);
        stone = Bit(settings.Materials.Stone);
        gravel = Bit(settings.Materials.Gravel);
        plaza = Bit(settings.Materials.Plaza);
        weeds = Bit(settings.Materials.Weeds);
        meadow = Bit(settings.Materials.Meadow);
        tallGrass = Bit(settings.Materials.TallGrass);
        paths = stone | gravel | plaza;
        int count = width * height;
        queue = new int[count];

        Timed("Regions", PlaceRegions);
        Timed("Land and sea", LayLandAndSea);
        Timed("Distances", MeasureDistances);
        Timed("Roads", LayRoads);
        Timed("Waystones and gates", PlaceEntries);
        Timed("Wilds", LayWilds);
        Timed("Chunks", ListChunks);
        Timed("Checks", Check);
        layouts.Clear();
        return composition;
    }

    /// <summary>
    /// Runs <paramref name="step"/> and records how long it took under <paramref name="name"/>.
    /// </summary>
    private void Timed(string name, Action step)
    {
        watch.Restart();
        step();
        watch.Stop();
        composition.StepTimes.Add(new WorldComposition.StepTime(name, watch.Elapsed.TotalMilliseconds));
    }

    /// <summary>
    /// The bit of <paramref name="material"/> in a cell's mask.
    /// </summary>
    private uint Bit(TerrainMaterial material)
    {
        int index = composition.IndexOf(material);
        return index >= 0 ? 1u << index : 0u;
    }

    /// <summary>
    /// Step 1. Lays each placement's layout from its seed, once, without its scatter layers' props, which the world
    /// does not write yet, and moves its cells, raised ground and mobs by the placement's cell; then reads each
    /// region's outline and ports from the moved cells.
    /// </summary>
    private void PlaceRegions()
    {
        List<string> keys = new();
        foreach (RegionPlacement placement in settings.Regions)
        {
            if (placement == null || placement.Generator == null)
            {
                composition.Warnings.Add("A region placement has no generator; it was skipped.");
                continue;
            }

            if (string.IsNullOrEmpty(placement.Key) || keys.Contains(placement.Key))
            {
                composition.Warnings.Add($"A region placement of {placement.Generator.name} has an empty or repeated key '{placement.Key}'; it was skipped.");
                continue;
            }

            keys.Add(placement.Key);
            AreaLayout layout = placement.Generator.Generate(placement.Seed, false);
            WorldComposition.Region region = new(composition.Regions.Count, placement, placement.Seed);
            composition.Regions.Add(region);
            layouts.Add(layout);
            offLand.Add(0);
            foreach (string warning in layout.Warnings)
            {
                composition.Warnings.Add($"{region.Key}: {warning}");
            }

            Lay(region, layout);
        }

        foreach (WorldComposition.Region region in composition.Regions)
        {
            ReadOutline(region);
            ReadPorts(region);
        }

        ListGrounds();
    }

    /// <summary>
    /// Lists the base ground materials in the style's order: the wilds' grass and each region's ground, its
    /// generator's first layer.
    /// </summary>
    private void ListGrounds()
    {
        uint grounds = grass;
        foreach (WorldComposition.Region region in composition.Regions)
        {
            IReadOnlyList<AreaGenerator.TerrainLayer> layers = region.Generator.TerrainLayers;
            grounds |= layers.Count > 0 && layers[0] != null ? Bit(layers[0].Material) : 0u;
        }

        for (int i = 0; i < composition.LayerOrder.Count; i++)
        {
            if ((grounds & (1u << i)) != 0)
            {
                composition.Grounds.Add(composition.LayerOrder[i]);
            }
        }
    }

    /// <summary>
    /// Moves <paramref name="layout"/>'s cells, raised ground and mobs into the world for <paramref name="region"/>,
    /// counting the cells that fall outside it or on another region.
    /// </summary>
    private void Lay(WorldComposition.Region region, AreaLayout layout)
    {
        Vector2Int offset = region.Cell;
        int outside = 0;
        int overlap = 0;
        int xMin = int.MaxValue;
        int yMin = int.MaxValue;
        int xMax = int.MinValue;
        int yMax = int.MinValue;
        foreach (Vector3Int cell in layout.TerrainCells(0))
        {
            int x = cell.x + offset.x;
            int y = cell.y + offset.y;
            if (x < 0 || y < 0 || x >= width || y >= height)
            {
                outside++;
                continue;
            }

            int index = y * width + x;
            if (owners[index] >= 0)
            {
                overlap++;
            }

            owners[index] = (short)region.Index;
            xMin = Mathf.Min(xMin, x);
            yMin = Mathf.Min(yMin, y);
            xMax = Mathf.Max(xMax, x);
            yMax = Mathf.Max(yMax, y);
        }

        region.Bounds = xMin <= xMax ? new RectInt(xMin, yMin, xMax - xMin + 1, yMax - yMin + 1) : new RectInt(offset.x, offset.y, 0, 0);
        for (int layer = 0; layer < layout.TerrainLayerCount; layer++)
        {
            TerrainMaterial material = layout.TerrainLayers[layer].Material;
            uint bit = Bit(material);
            if (bit == 0)
            {
                composition.Warnings.Add($"{region.Key}: {(material != null ? material.name : "a layer without a material")} is not in the style's layer order; its cells were skipped.");
                continue;
            }

            region.IsSea |= bit == ocean && layout.TerrainCells(layer).Count > 0;
            foreach (Vector3Int cell in layout.TerrainCells(layer))
            {
                int index = IndexOf(cell.x + offset.x, cell.y + offset.y);
                if (index >= 0)
                {
                    cells[index] |= bit;
                }
            }
        }

        foreach (Vector3Int cell in layout.Raised)
        {
            int index = IndexOf(cell.x + offset.x, cell.y + offset.y);
            if (index >= 0)
            {
                raised[index] = RaisedGround.Raised;
            }
        }

        foreach (Vector3Int cell in layout.Ramps)
        {
            int index = IndexOf(cell.x + offset.x, cell.y + offset.y);
            if (index >= 0)
            {
                raised[index] = RaisedGround.Ramp;
            }
        }

        for (int i = 0; i < layout.Mobs.Count; i++)
        {
            Vector2Int cell = new(layout.Mobs[i].Cell.x + offset.x, layout.Mobs[i].Cell.y + offset.y);
            if (IndexOf(cell.x, cell.y) >= 0)
            {
                composition.Mobs.Add(new WorldComposition.Mob(layout.Mobs[i].Prefab, cell, GeneratedIds.For(worldId, region.Key, MobKind, i), region.Key, i));
            }
        }

        region.SpawnCell = new Vector2Int(layout.SpawnCell.x + offset.x, layout.SpawnCell.y + offset.y);
        if (outside > 0)
        {
            composition.Warnings.Add($"{region.Key}: {outside} cells lie outside the world.");
        }

        if (overlap > 0)
        {
            composition.Warnings.Add($"{region.Key}: {overlap} cells lie on another region.");
        }
    }

    /// <summary>
    /// Reads <paramref name="region"/>'s outline: its cells with a cell outside it among their 8 neighbours, in row
    /// order.
    /// </summary>
    private void ReadOutline(WorldComposition.Region region)
    {
        RectInt bounds = region.Bounds;
        for (int y = bounds.yMin; y < bounds.yMax; y++)
        {
            for (int x = bounds.xMin; x < bounds.xMax; x++)
            {
                if (owners[y * width + x] != region.Index)
                {
                    continue;
                }

                foreach (Vector2Int step in Neighbours8)
                {
                    if (OwnerAt(x + step.x, y + step.y) != region.Index)
                    {
                        region.Outline.Add(new Vector2Int(x, y));
                        break;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Reads <paramref name="region"/>'s ports: each run (8-connected) of path cells on its outline, in row order of
    /// its first cell, facing the side most of its cells have outside them, with the middle of the rows or columns it
    /// holds across that side.
    /// </summary>
    private void ReadPorts(WorldComposition.Region region)
    {
        List<Vector2Int> candidates = new();
        foreach (Vector2Int cell in region.Outline)
        {
            if ((cells[cell.y * width + cell.x] & paths) != 0)
            {
                candidates.Add(cell);
            }
        }

        List<Vector2Int> seen = new();
        foreach (Vector2Int start in candidates)
        {
            if (seen.Contains(start))
            {
                continue;
            }

            List<Vector2Int> run = new() { start };
            seen.Add(start);
            for (int i = 0; i < run.Count; i++)
            {
                foreach (Vector2Int step in Neighbours8)
                {
                    Vector2Int next = run[i] + step;
                    if (candidates.Contains(next) && !seen.Contains(next))
                    {
                        seen.Add(next);
                        run.Add(next);
                    }
                }
            }

            run.Sort((a, b) => a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));
            region.Ports.Add(MakePort(region, run));
        }
    }

    /// <summary>
    /// The port of <paramref name="region"/> made of <paramref name="run"/>.
    /// </summary>
    private WorldComposition.Port MakePort(WorldComposition.Region region, List<Vector2Int> run)
    {
        int[] votes = new int[4];
        foreach (Vector2Int cell in run)
        {
            for (int side = 0; side < 4; side++)
            {
                Vector2Int step = PortSides.Step((PortSide)side);
                if (OwnerAt(cell.x + step.x, cell.y + step.y) != region.Index)
                {
                    votes[side]++;
                }
            }
        }

        int facing = 0;
        for (int side = 1; side < 4; side++)
        {
            if (votes[side] > votes[facing])
            {
                facing = side;
            }
        }

        PortSide portSide = (PortSide)facing;
        bool across = portSide == PortSide.East || portSide == PortSide.West;
        List<int> lines = new();
        int least = int.MaxValue;
        int most = int.MinValue;
        foreach (Vector2Int cell in run)
        {
            int line = across ? cell.y : cell.x;
            if (!lines.Contains(line))
            {
                lines.Add(line);
            }

            int along = across ? cell.x : cell.y;
            least = Mathf.Min(least, along);
            most = Mathf.Max(most, along);
        }

        float middle = 0f;
        foreach (int line in lines)
        {
            middle += line + 0.5f;
        }

        middle /= lines.Count;
        bool outward = portSide == PortSide.East || portSide == PortSide.North;
        return new WorldComposition.Port(region, portSide, run, middle, outward ? most : least, outward ? least : most);
    }

    /// <summary>
    /// Steps 2 and 3. Lays the sea and the land: the coast lies the sea depth in from each side of the world, give or
    /// take its wobble, along sums of sine waves, with rounded corners, and along the side a sea region lies on at that
    /// region's own rows, easing back beyond it; ocean outside, grass on every land cell outside the regions, and sand
    /// on the beach, the land within the beach's width of the coast, beside a sea region at that region's own beach
    /// width. Counts each region's cells that lie off the land but its ocean.
    /// </summary>
    private void LayLandAndSea()
    {
        Random random = new(settings.Seed);
        float[][] depth = new float[4][];
        float[][] beach = new float[4][];
        float low = Mathf.Max(1f, Mathf.Min(settings.BeachWidth.x, settings.BeachWidth.y));
        float high = Mathf.Max(low, Mathf.Max(settings.BeachWidth.x, settings.BeachWidth.y));
        for (int side = 0; side < 4; side++)
        {
            int length = side < 2 ? width : height;
            Func<float, float> coast = Noise(random, settings.CoastWavelength);
            Func<float, float> widths = Noise(random, settings.CoastWavelength);
            depth[side] = new float[length];
            beach[side] = new float[length];
            for (int a = 0; a < length; a++)
            {
                depth[side][a] = settings.SeaDepth + settings.CoastWobble * coast(a + 0.5f);
                beach[side][a] = Mathf.Lerp(low, high, 0.5f + 0.5f * widths(a + 0.5f));
            }
        }

        foreach (WorldComposition.Region region in composition.Regions)
        {
            if (region.IsSea)
            {
                Hold(region, depth, beach);
            }
        }

        float radius = settings.CoastCornerRadius;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float north = height - y - 0.5f - depth[0][x];
                float south = y + 0.5f - depth[1][x];
                float west = x + 0.5f - depth[2][y];
                float east = width - x - 0.5f - depth[3][y];
                float across = Mathf.Min(west, east);
                float down = Mathf.Min(south, north);
                float inside = InsideDepth(across, down, radius);
                int index = y * width + x;
                if (owners[index] >= 0)
                {
                    uint coast = composition.Regions[owners[index]].IsSea ? ocean | sand : ocean;
                    if (inside <= 0f && (cells[index] & coast) == 0)
                    {
                        offLand[owners[index]]++;
                    }

                    continue;
                }

                if (inside <= 0f)
                {
                    cells[index] = ocean;
                    continue;
                }

                owners[index] = WorldComposition.Wilds;
                cells[index] = grass;
                float acrossWidth = west < east ? beach[2][y] : beach[3][y];
                float downWidth = south < north ? beach[1][x] : beach[0][x];
                float blend = Smooth(0.5f + (down - across) / (2f * CornerBlend));
                if (inside < Mathf.Lerp(downWidth, acrossWidth, blend))
                {
                    cells[index] |= sand;
                }
            }
        }
    }

    /// <summary>
    /// How deep inside the land's rounded rectangle a cell lies, from its depths <paramref name="across"/> (from the
    /// nearer of the west and east coasts) and <paramref name="down"/> (from the nearer of the south and north coasts):
    /// the smaller one, but within <paramref name="radius"/> of both, its depth inside the corner's arc.
    /// </summary>
    private static float InsideDepth(float across, float down, float radius)
    {
        if (across <= 0f || down <= 0f || across >= radius || down >= radius)
        {
            return Mathf.Min(across, down);
        }

        float dx = radius - across;
        float dy = radius - down;
        return radius - Mathf.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// Holds the coast and the beach's width along the side of the world <paramref name="region"/>'s ocean faces at
    /// the rows the region's coast holds at its two ends, read from its outermost columns with ocean, across its span
    /// and easing back to their own values over the coast's ease beyond it, so the world's ocean meets the region's
    /// level and its beach the region's beach at the region's width. A region whose ocean lies further from that side's
    /// edge than the sea depth, its wobble and the ease together holds nothing, and the checks warn of it.
    /// </summary>
    private void Hold(WorldComposition.Region region, float[][] depth, float[][] beach)
    {
        RectInt bounds = region.Bounds;
        double[] meanDepth = new double[4];
        int[] leastDepth = { int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue };
        int oceanCells = 0;
        for (int y = bounds.yMin; y < bounds.yMax; y++)
        {
            for (int x = bounds.xMin; x < bounds.xMax; x++)
            {
                int index = y * width + x;
                if (owners[index] == region.Index && (cells[index] & ocean) != 0)
                {
                    int[] depths = { height - 1 - y, y, x, width - 1 - x };
                    for (int s = 0; s < 4; s++)
                    {
                        meanDepth[s] += depths[s];
                        leastDepth[s] = Mathf.Min(leastDepth[s], depths[s]);
                    }

                    oceanCells++;
                }
            }
        }

        if (oceanCells == 0)
        {
            return;
        }

        int side = 0;
        for (int other = 1; other < 4; other++)
        {
            if (meanDepth[other] < meanDepth[side])
            {
                side = other;
            }
        }

        if (leastDepth[side] > settings.SeaDepth + settings.CoastWobble + settings.CoastEase)
        {
            return;
        }

        bool alongX = side < 2;
        int spanMin = alongX ? bounds.xMin : bounds.yMin;
        int spanMax = (alongX ? bounds.xMax : bounds.yMax) - 1;
        int oceanMin = int.MaxValue;
        int oceanMax = int.MinValue;
        ForEachRegionCell(region, (a, d, mask) =>
        {
            if ((mask & ocean) != 0)
            {
                oceanMin = Mathf.Min(oceanMin, a);
                oceanMax = Mathf.Max(oceanMax, a);
            }
        }, side);

        HeldRows(region, side, oceanMin, oceanMin + HeldColumns - 1, out float lowDepth, out float lowBeach);
        HeldRows(region, side, oceanMax - HeldColumns + 1, oceanMax, out float highDepth, out float highBeach);
        float ease = settings.CoastEase;
        float[] sideDepth = depth[side];
        float[] sideBeach = beach[side];
        for (int a = 0; a < sideDepth.Length; a++)
        {
            if (a >= spanMin && a <= spanMax)
            {
                float t = spanMax > spanMin ? (a - spanMin) / (float)(spanMax - spanMin) : 0f;
                sideDepth[a] = Mathf.Lerp(lowDepth, highDepth, t);
                sideBeach[a] = Mathf.Lerp(lowBeach, highBeach, t);
                continue;
            }

            bool before = a < spanMin;
            float distance = before ? spanMin - a : a - spanMax;
            if (distance >= ease)
            {
                continue;
            }

            float hold = Smooth(1f - distance / ease);
            sideDepth[a] = Mathf.Lerp(sideDepth[a], before ? lowDepth : highDepth, hold);
            sideBeach[a] = Mathf.Lerp(sideBeach[a], before ? lowBeach : highBeach, hold);
        }
    }

    /// <summary>
    /// The coast's depth from the world's edge on <paramref name="side"/> and the beach's width that
    /// <paramref name="region"/> holds over its columns <paramref name="from"/> to <paramref name="to"/> along that
    /// side: one past its deepest ocean cell, and from there one past its deepest sand.
    /// </summary>
    private void HeldRows(WorldComposition.Region region, int side, int from, int to, out float coastDepth, out float beachWidth)
    {
        int deepestOcean = -1;
        ForEachRegionCell(region, (a, d, mask) =>
        {
            if (a >= from && a <= to && (mask & ocean) != 0)
            {
                deepestOcean = Mathf.Max(deepestOcean, d);
            }
        }, side);

        int deepestSand = deepestOcean;
        ForEachRegionCell(region, (a, d, mask) =>
        {
            if (a >= from && a <= to && (mask & ocean) == 0 && (mask & sand) != 0 && d > deepestOcean)
            {
                deepestSand = Mathf.Max(deepestSand, d);
            }
        }, side);

        coastDepth = deepestOcean + 1;
        beachWidth = deepestSand > deepestOcean ? deepestSand - deepestOcean : Mathf.Max(1f, settings.BeachWidth.x);
    }

    /// <summary>
    /// Calls <paramref name="visit"/> with each of <paramref name="region"/>'s cells in row order as its place along
    /// <paramref name="side"/> of the world (a column for the north and south sides, a row for the west and east), its
    /// depth in from that side's edge and its materials.
    /// </summary>
    private void ForEachRegionCell(WorldComposition.Region region, Action<int, int, uint> visit, int side)
    {
        RectInt bounds = region.Bounds;
        for (int y = bounds.yMin; y < bounds.yMax; y++)
        {
            for (int x = bounds.xMin; x < bounds.xMax; x++)
            {
                int index = y * width + x;
                if (owners[index] != region.Index)
                {
                    continue;
                }

                int along = side < 2 ? x : y;
                int depthIn = side switch
                {
                    0 => height - 1 - y,
                    1 => y,
                    2 => x,
                    _ => width - 1 - x
                };
                visit(along, depthIn, cells[index]);
            }
        }
    }

    /// <summary>
    /// A smooth noise along a line, from -1 to 1, as the Coast generator's coast lines swing: a few sine waves of
    /// random wavelengths between the two values of <paramref name="wavelengths"/> and random phases, weighted at
    /// random and scaled by their total weight.
    /// </summary>
    private static Func<float, float> Noise(Random random, Vector2 wavelengths)
    {
        float[] frequencies = new float[CoastHarmonics];
        float[] phases = new float[CoastHarmonics];
        float[] weights = new float[CoastHarmonics];
        float total = 0f;
        float shortest = Mathf.Max(2f, Mathf.Min(wavelengths.x, wavelengths.y));
        float longest = Mathf.Max(shortest, Mathf.Max(wavelengths.x, wavelengths.y));
        for (int i = 0; i < CoastHarmonics; i++)
        {
            frequencies[i] = 2f * Mathf.PI / Range(random, shortest, longest);
            phases[i] = Range(random, 0f, 2f * Mathf.PI);
            weights[i] = Range(random, 0.4f, 1f);
            total += weights[i];
        }

        return a =>
        {
            float sum = 0f;
            for (int i = 0; i < CoastHarmonics; i++)
            {
                sum += weights[i] * Mathf.Sin(frequencies[i] * a + phases[i]);
            }

            return sum / total;
        };
    }

    /// <summary>
    /// Measures, for the roads, the wilds and the checks, every cell's steps (8 neighbours) from the nearest region
    /// in one pass from every region's outline at once, noting the nearest region of each cell and the shortest
    /// distance between every two regions where their fronts meet, and every cell's steps from the beach; both only
    /// as far as the steps that use them reach.
    /// </summary>
    private void MeasureDistances()
    {
        int count = width * height;
        int regionCount = composition.Regions.Count;
        regionSteps = new byte[count];
        nearestRegion = new short[count];
        beachSteps = new byte[count];
        roadSteps = new byte[count];
        Array.Fill(regionSteps, Far);
        Array.Fill(nearestRegion, (short)-1);
        Array.Fill(roadSteps, Far);
        regionDistances = new int[regionCount, regionCount];
        shoreDistances = new int[regionCount];
        for (int a = 0; a < regionCount; a++)
        {
            shoreDistances[a] = int.MaxValue;
            for (int b = 0; b < regionCount; b++)
            {
                regionDistances[a, b] = int.MaxValue;
            }
        }

        for (int index = 0; index < count; index++)
        {
            if (owners[index] >= 0)
            {
                regionSteps[index] = 0;
                nearestRegion[index] = owners[index];
            }
        }

        int head = 0;
        int tail = 0;
        foreach (WorldComposition.Region region in composition.Regions)
        {
            foreach (Vector2Int cell in region.Outline)
            {
                queue[tail++] = cell.y * width + cell.x;
            }
        }

        int reach = Mathf.Clamp(Mathf.Max(Mathf.Max(settings.RegionGap, settings.RegionShoreGap), Mathf.Max(MaxClearance() + settings.RoadAvoidance + 1, PatchMargin + MaxPatchReach())), 1, Far - 1);
        while (head < tail)
        {
            int index = queue[head++];
            int steps = regionSteps[index];
            int label = nearestRegion[index];
            int x = index % width;
            int y = index / width;
            foreach (Vector2Int step in Neighbours8)
            {
                int next = IndexOf(x + step.x, y + step.y);
                if (next < 0)
                {
                    continue;
                }

                if (regionSteps[next] == Far)
                {
                    if (steps + 1 <= reach)
                    {
                        regionSteps[next] = (byte)(steps + 1);
                        nearestRegion[next] = (short)label;
                        if (steps + 1 < reach)
                        {
                            queue[tail++] = next;
                        }
                    }

                    continue;
                }

                int other = nearestRegion[next];
                if (other >= 0 && other != label)
                {
                    int distance = steps + regionSteps[next] + 1;
                    int a = Mathf.Min(label, other);
                    int b = Mathf.Max(label, other);
                    regionDistances[a, b] = Mathf.Min(regionDistances[a, b], distance);
                }
            }
        }

        Array.Fill(beachSteps, Far);
        head = 0;
        tail = 0;
        for (int index = 0; index < count; index++)
        {
            if (owners[index] == WorldComposition.Wilds && (cells[index] & sand) != 0)
            {
                beachSteps[index] = 0;
                queue[tail++] = index;
                int region = nearestRegion[index];
                if (region >= 0 && !composition.Regions[region].IsSea)
                {
                    shoreDistances[region] = Mathf.Min(shoreDistances[region], regionSteps[index]);
                }
            }
        }

        Spread(beachSteps, head, tail, Mathf.Clamp(Mathf.Max(MaxClearance() + settings.RoadAvoidance + 1, PatchMargin), 1, Far - 1));
    }

    /// <summary>
    /// Spreads <paramref name="steps"/> (8 neighbours) from the queued cells between <paramref name="head"/> and
    /// <paramref name="tail"/> as far as <paramref name="reach"/>, keeping each cell's least.
    /// </summary>
    private void Spread(byte[] steps, int head, int tail, int reach)
    {
        while (head < tail)
        {
            int index = queue[head++];
            int next = steps[index] + 1;
            if (next > reach)
            {
                continue;
            }

            int x = index % width;
            int y = index / width;
            foreach (Vector2Int step in Neighbours8)
            {
                int neighbour = IndexOf(x + step.x, y + step.y);
                if (neighbour >= 0 && steps[neighbour] > next)
                {
                    steps[neighbour] = (byte)next;
                    queue[tail++] = neighbour;
                }
            }
        }
    }

    /// <summary>
    /// The widest clearance any link's road keeps: half its width, rounded up, and the road margin.
    /// </summary>
    private int MaxClearance()
    {
        int widest = 1;
        foreach (RoadLink link in settings.Links)
        {
            if (link != null)
            {
                widest = Mathf.Max(widest, link.Width);
            }
        }

        return Clearance(widest);
    }

    /// <summary>
    /// The clearance a road of <paramref name="roadWidth"/> keeps from the outlines and the beach.
    /// </summary>
    private int Clearance(int roadWidth)
    {
        return Mathf.CeilToInt(roadWidth * 0.5f) + settings.RoadMargin;
    }

    /// <summary>
    /// The farthest reach of any edge patches past the patch margin.
    /// </summary>
    private int MaxPatchReach()
    {
        return Mathf.Max(settings.TallGrass.Reach, Mathf.Max(settings.Meadows.Reach, settings.Weeds.Reach));
    }

    /// <summary>
    /// Step 4. Lays each link's road in order: from its port's lead-in, the cheapest route over the wilds to the other
    /// port's lead-in or to a road already laid, pulled straight where the way is as clear, sampled into a spline
    /// through a point every spline spacing, and painted at the link's width on the wilds.
    /// </summary>
    private void LayRoads()
    {
        int count = width * height;
        costs = new int[count];
        parents = new int[count];
        searched = new int[count];
        closed = new int[count];
        goals = new int[count];
        for (int l = 0; l < settings.Links.Count; l++)
        {
            RoadLink link = settings.Links[l];
            if (link == null)
            {
                continue;
            }

            string name = LinkName(l, link);
            WorldComposition.Port from = FindPort(link.FromRegion, link.FromSide, name);
            WorldComposition.Port to = string.IsNullOrEmpty(link.ToRegion) ? null : FindPort(link.ToRegion, link.ToSide, name);
            if (from == null || (!string.IsNullOrEmpty(link.ToRegion) && to == null))
            {
                continue;
            }

            int roadWidth = link.Width;
            LeadIn(from, roadWidth, out Vector2 fromInner, out Vector2 fromLeave);
            Vector2 toInner = default;
            Vector2 toLeave = default;
            bool toPort = !link.EndsOnRoad;
            searchId++;
            int goal = -1;
            if (toPort)
            {
                LeadIn(to, roadWidth, out toInner, out toLeave);
                goal = IndexAt(toLeave);
            }
            else if (!MarkRoadGoals(to))
            {
                composition.Warnings.Add($"{name}: no road is laid {(to != null ? "to or from that port" : "yet")} for it to end on.");
                continue;
            }

            List<int> route = FindRoute(IndexAt(fromLeave), goal, Clearance(roadWidth));
            if (route == null)
            {
                composition.Warnings.Add($"{name}: no route over the wilds.");
                continue;
            }

            List<Vector2> corners = Pull(route, Clearance(roadWidth));
            corners[0] = fromLeave;
            corners[corners.Count - 1] = toPort ? toLeave : CentreOf(route[route.Count - 1]);
            List<Vector2> points = Resample(corners);
            Meander(points, l, Clearance(roadWidth));
            List<Vector2> course = new() { fromInner };
            course.AddRange(points);
            if (toPort)
            {
                course.Add(toInner);
            }

            TerrainMaterial material = link.RoadSurface == RoadLink.Surface.Gravel ? settings.Materials.Gravel : settings.Materials.Stone;
            WorldComposition.Road road = new(l, material, roadWidth);
            road.Course.AddRange(course);
            List<Vector2> samples = new();
            AreaLayoutBuilder.SampleSpline(course, samples);
            Pave(samples, roadWidth, Bit(material), road);
            composition.Roads.Add(road);
            UseRoadPort(from, roadWidth, road);
            if (toPort)
            {
                UseRoadPort(to, roadWidth, road);
            }
        }
    }

    /// <summary>
    /// How warnings name link <paramref name="index"/>: its number from 1, its port and where it leads.
    /// </summary>
    private static string LinkName(int index, RoadLink link)
    {
        string target = string.IsNullOrEmpty(link.ToRegion) ? "the nearest road" : link.ToRoad ? $"the road at {link.ToRegion} {link.ToSide}" : $"{link.ToRegion} {link.ToSide}";
        return $"Link {index + 1} ({link.FromRegion} {link.FromSide} to {target})";
    }

    /// <summary>
    /// The port of the region <paramref name="key"/> facing <paramref name="side"/> (its first, in row order), or null
    /// with a warning naming <paramref name="link"/>.
    /// </summary>
    private WorldComposition.Port FindPort(string key, PortSide side, string link)
    {
        WorldComposition.Region region = composition.FindRegion(key);
        if (region == null)
        {
            composition.Warnings.Add($"{link}: there is no region '{key}'.");
            return null;
        }

        foreach (WorldComposition.Port port in region.Ports)
        {
            if (port.Side == side)
            {
                return port;
            }
        }

        composition.Warnings.Add($"{link}: {key} has no port facing {side}.");
        return null;
    }

    /// <summary>
    /// Where a road of <paramref name="roadWidth"/> starts from <paramref name="port"/>: at the middle of its innermost
    /// line, and straight out on its region road's rows to the lead-in's end.
    /// </summary>
    private void LeadIn(WorldComposition.Port port, int roadWidth, out Vector2 inner, out Vector2 leave)
    {
        float middle = RoadMiddle(port, roadWidth);
        Vector2Int step = PortSides.Step(port.Side);
        bool across = port.Side == PortSide.East || port.Side == PortSide.West;
        float innerLine = port.Inner + 0.5f;
        float leaveLine = port.Outer + 0.5f + settings.PortLeadIn * (across ? step.x : step.y);
        inner = across ? new Vector2(innerLine, middle) : new Vector2(middle, innerLine);
        leave = across ? new Vector2(leaveLine, middle) : new Vector2(middle, leaveLine);
    }

    /// <summary>
    /// The middle of a road of <paramref name="roadWidth"/> across <paramref name="port"/>'s side, snapped so the road
    /// covers whole rows: on a cell's centre for an odd width, between two cells for an even one.
    /// </summary>
    private static float RoadMiddle(WorldComposition.Port port, int roadWidth)
    {
        return roadWidth % 2 == 1 ? Mathf.Floor(port.Middle) + 0.5f : Mathf.Round(port.Middle);
    }

    /// <summary>
    /// Records that <paramref name="road"/>, of <paramref name="roadWidth"/>, uses <paramref name="port"/>: the port
    /// gets its gate's name, and its gate stands on the first road that uses it.
    /// </summary>
    private void UseRoadPort(WorldComposition.Port port, int roadWidth, WorldComposition.Road road)
    {
        if (port.Gate == null)
        {
            port.Gate = $"{port.Region.Key}{port.Side}";
            portWidths[port] = roadWidth;
        }

        if (!portRoads.TryGetValue(port, out List<WorldComposition.Road> roads))
        {
            roads = new List<WorldComposition.Road>();
            portRoads[port] = roads;
        }

        roads.Add(road);
    }

    /// <summary>
    /// Marks as goals of the current search the cells of the roads laid to or from <paramref name="port"/>, or of
    /// every road laid when it is null. Returns whether any cell was marked.
    /// </summary>
    private bool MarkRoadGoals(WorldComposition.Port port)
    {
        bool any = false;
        if (port == null)
        {
            foreach (WorldComposition.Road road in composition.Roads)
            {
                any |= MarkGoals(road);
            }

            return any;
        }

        if (portRoads.TryGetValue(port, out List<WorldComposition.Road> roads))
        {
            foreach (WorldComposition.Road road in roads)
            {
                any |= MarkGoals(road);
            }
        }

        return any;
    }

    /// <summary>
    /// Marks <paramref name="road"/>'s cells as goals of the current search.
    /// </summary>
    private bool MarkGoals(WorldComposition.Road road)
    {
        foreach (Vector2Int cell in road.Cells)
        {
            goals[cell.y * width + cell.x] = searchId;
        }

        return road.Cells.Count > 0;
    }

    /// <summary>
    /// The cheapest route (8 neighbours, no corner cut) over the wilds from <paramref name="start"/> to
    /// <paramref name="goal"/>, or to any cell marked as a goal when it is -1, as cells in order; null when there is
    /// none. Cells within <paramref name="clearance"/> of a region or the beach are closed, and a step grows dearer
    /// near them and near another road (<see cref="Penalty"/>).
    /// </summary>
    private List<int> FindRoute(int start, int goal, int clearance)
    {
        if (start < 0)
        {
            return null;
        }

        open.Clear();
        int goalX = goal >= 0 ? goal % width : 0;
        int goalY = goal >= 0 ? goal / width : 0;
        costs[start] = 0;
        parents[start] = -1;
        searched[start] = searchId;
        open.Push(start, Heuristic(start, goal, goalX, goalY), Heuristic(start, goal, goalX, goalY));
        while (open.Count > 0)
        {
            int index = open.Pop();
            if (closed[index] == searchId)
            {
                continue;
            }

            closed[index] = searchId;
            if (index == goal || (goal < 0 && goals[index] == searchId))
            {
                List<int> route = new();
                for (int at = index; at >= 0; at = parents[at])
                {
                    route.Add(at);
                }

                route.Reverse();
                return route;
            }

            int x = index % width;
            int y = index / width;
            int cost = costs[index];
            for (int n = 0; n < Neighbours8.Length; n++)
            {
                Vector2Int step = Neighbours8[n];
                int next = IndexOf(x + step.x, y + step.y);
                if (next < 0 || !(next == goal || (goal < 0 && goals[next] == searchId) || Passable(next, clearance)))
                {
                    continue;
                }

                if (n >= 4 && (!Passable(IndexOf(x + step.x, y), clearance) || !Passable(IndexOf(x, y + step.y), clearance)))
                {
                    continue;
                }

                int nextCost = cost + (n >= 4 ? DiagonalCost : StepCost) + Penalty(next, clearance);
                if (closed[next] == searchId || (searched[next] == searchId && costs[next] <= nextCost))
                {
                    continue;
                }

                searched[next] = searchId;
                costs[next] = nextCost;
                parents[next] = index;
                int remaining = Heuristic(next, goal, goalX, goalY);
                open.Push(next, nextCost + remaining, remaining);
            }
        }

        return null;
    }

    /// <summary>
    /// The octile distance in step costs from <paramref name="index"/> to the goal cell, or 0 for a search to marked
    /// goals.
    /// </summary>
    private int Heuristic(int index, int goal, int goalX, int goalY)
    {
        if (goal < 0)
        {
            return 0;
        }

        int dx = Mathf.Abs(index % width - goalX);
        int dy = Mathf.Abs(index / width - goalY);
        return StepCost * Mathf.Max(dx, dy) + (DiagonalCost - StepCost) * Mathf.Min(dx, dy);
    }

    /// <summary>
    /// Whether a road's course may pass <paramref name="index"/>: a wilds cell off the beach, more than
    /// <paramref name="clearance"/> from every region and from the beach.
    /// </summary>
    private bool Passable(int index, int clearance)
    {
        return index >= 0 && owners[index] == WorldComposition.Wilds && (cells[index] & sand) == 0
            && regionSteps[index] > clearance && beachSteps[index] > clearance;
    }

    /// <summary>
    /// What a step onto <paramref name="index"/> costs over its base: more the closer it lies, within the road
    /// avoidance past <paramref name="clearance"/>, to a region or the beach, and to another road; most to cross a road.
    /// </summary>
    private int Penalty(int index, int clearance)
    {
        int avoidance = settings.RoadAvoidance;
        int penalty = 0;
        int near = Mathf.Min(regionSteps[index], beachSteps[index]) - clearance;
        if (near <= avoidance)
        {
            penalty += (avoidance + 1 - near) * AvoidCost;
        }

        int road = roadSteps[index];
        if (road == 0)
        {
            penalty += CrossingCost;
        }
        else if (road <= avoidance)
        {
            penalty += (avoidance + 1 - road) * AvoidCost;
        }

        return penalty;
    }

    /// <summary>
    /// <paramref name="route"/>'s corners: from each corner, the farthest cell of the route (within a reach) that a
    /// straight line reaches over cells a road of <paramref name="clearance"/> may pass, none of them dearer than the
    /// dearest cell of the route between, so a pulled line keeps as far from what the route kept from.
    /// </summary>
    private List<Vector2> Pull(List<int> route, int clearance)
    {
        List<Vector2> corners = new() { CentreOf(route[0]) };
        int at = 0;
        while (at < route.Count - 1)
        {
            int farthest = Mathf.Min(route.Count - 1, at + MaxPull);
            int next = at + 1;
            for (int candidate = farthest; candidate > at + 1; candidate--)
            {
                int limit = 0;
                for (int i = at; i <= candidate; i++)
                {
                    limit = Mathf.Max(limit, Penalty(route[i], clearance));
                }

                if (Clear(CentreOf(route[at]), CentreOf(route[candidate]), clearance, limit))
                {
                    next = candidate;
                    break;
                }
            }

            corners.Add(CentreOf(route[next]));
            at = next;
        }

        return corners;
    }

    /// <summary>
    /// Whether every cell a straight line from <paramref name="from"/> to <paramref name="to"/> crosses is one a road
    /// of <paramref name="clearance"/> may pass, at a penalty of no more than <paramref name="limit"/>.
    /// </summary>
    private bool Clear(Vector2 from, Vector2 to, int clearance, int limit)
    {
        float length = Vector2.Distance(from, to);
        int steps = Mathf.Max(1, Mathf.CeilToInt(length / LineStep));
        for (int s = 0; s <= steps; s++)
        {
            Vector2 point = Vector2.Lerp(from, to, s / (float)steps);
            int index = IndexOf(Mathf.FloorToInt(point.x), Mathf.FloorToInt(point.y));
            if (!Passable(index, clearance) || Penalty(index, clearance) > limit)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Points along the polyline <paramref name="corners"/> every spline spacing, its first and last points included.
    /// </summary>
    private List<Vector2> Resample(List<Vector2> corners)
    {
        List<Vector2> points = new() { corners[0] };
        float spacing = settings.SplineSpacing;
        float carried = 0f;
        for (int i = 1; i < corners.Count; i++)
        {
            Vector2 a = corners[i - 1];
            Vector2 b = corners[i];
            float length = Vector2.Distance(a, b);
            float along = spacing - carried;
            while (along < length)
            {
                points.Add(Vector2.Lerp(a, b, along / length));
                along += spacing;
            }

            carried = length - (along - spacing);
        }

        Vector2 last = corners[corners.Count - 1];
        if (Vector2.Distance(points[points.Count - 1], last) < spacing * 0.5f && points.Count > 1)
        {
            points[points.Count - 1] = last;
        }
        else
        {
            points.Add(last);
        }

        return points;
    }

    /// <summary>
    /// Moves each of <paramref name="points"/> but its first and last aside from the road's line by a smooth noise
    /// along it, up to the road meander, on a stream of link <paramref name="link"/>'s own from the world's seed,
    /// fading in over the first and last spline spacing, wherever the cell it lands on lets a road of
    /// <paramref name="clearance"/> pass.
    /// </summary>
    private void Meander(List<Vector2> points, int link, int clearance)
    {
        if (settings.RoadMeander <= 0f || points.Count < 3)
        {
            return;
        }

        Random random = new(unchecked(settings.Seed * 486187739 + (link + 1) * 1013904223));
        Func<float, float> wander = Noise(random, settings.RoadMeanderWavelength);
        float[] along = new float[points.Count];
        for (int i = 1; i < points.Count; i++)
        {
            along[i] = along[i - 1] + Vector2.Distance(points[i - 1], points[i]);
        }

        float length = along[points.Count - 1];
        Vector2[] moved = points.ToArray();
        for (int i = 1; i < points.Count - 1; i++)
        {
            Vector2 direction = (points[i + 1] - points[i - 1]).normalized;
            Vector2 side = new(-direction.y, direction.x);
            float fade = Smooth(Mathf.Min(along[i], length - along[i]) / settings.SplineSpacing);
            Vector2 point = points[i] + side * (settings.RoadMeander * wander(along[i]) * fade);
            if (Passable(IndexAt(point), clearance))
            {
                moved[i] = point;
            }
        }

        points.Clear();
        points.AddRange(moved);
    }

    /// <summary>
    /// Paves with <paramref name="bit"/> every wilds cell off the beach whose centre lies within half of
    /// <paramref name="roadWidth"/> of a sample, as the area generators paint their roads, and lists the cells in
    /// <paramref name="road"/>.
    /// </summary>
    private void Pave(List<Vector2> samples, int roadWidth, uint bit, WorldComposition.Road road)
    {
        float radius = roadWidth * 0.5f + PaintAllowance;
        int reach = Mathf.CeilToInt(radius);
        searchId++;
        int head = 0;
        int tail = 0;
        foreach (Vector2 sample in samples)
        {
            int cx = Mathf.FloorToInt(sample.x);
            int cy = Mathf.FloorToInt(sample.y);
            for (int dy = -reach; dy <= reach; dy++)
            {
                for (int dx = -reach; dx <= reach; dx++)
                {
                    int index = IndexOf(cx + dx, cy + dy);
                    if (index < 0 || searched[index] == searchId || owners[index] != WorldComposition.Wilds || (cells[index] & sand) != 0)
                    {
                        continue;
                    }

                    Vector2 centre = new(cx + dx + 0.5f, cy + dy + 0.5f);
                    if ((centre - sample).sqrMagnitude > radius * radius)
                    {
                        continue;
                    }

                    searched[index] = searchId;
                    cells[index] |= bit;
                    road.Cells.Add(new Vector2Int(cx + dx, cy + dy));
                    roadSteps[index] = 0;
                    queue[tail++] = index;
                }
            }
        }

        Spread(roadSteps, head, tail, Mathf.Clamp(Mathf.Max(settings.RoadAvoidance + 1, PatchMargin), 1, Far - 1));
    }

    /// <summary>
    /// Step 5. A waystone per region at its spawn cell, named by its key, the first region's first, with a disc of
    /// plaza of the plaza's radius under it on the region's own cells, under its road; then a gate per linked port, the
    /// gate distance outside its outline on its road.
    /// </summary>
    private void PlaceEntries()
    {
        float radius = settings.PlazaRadius;
        int reach = Mathf.CeilToInt(radius);
        foreach (WorldComposition.Region region in composition.Regions)
        {
            Vector2 middle = region.SpawnCell + new Vector2(0.5f, 0.5f);
            composition.Entries.Add(new WorldDefinition.Entry(region.Key, middle, true));
            for (int dy = -reach; dy <= reach; dy++)
            {
                for (int dx = -reach; dx <= reach; dx++)
                {
                    int index = IndexOf(region.SpawnCell.x + dx, region.SpawnCell.y + dy);
                    if (index >= 0 && owners[index] == region.Index && dx * dx + dy * dy <= radius * radius)
                    {
                        cells[index] |= plaza;
                    }
                }
            }
        }

        foreach (WorldComposition.Region region in composition.Regions)
        {
            foreach (WorldComposition.Port port in region.Ports)
            {
                if (port.Gate != null)
                {
                    composition.Entries.Add(new WorldDefinition.Entry(port.Gate, GatePosition(port), false));
                }
            }
        }
    }

    /// <summary>
    /// Where <paramref name="port"/>'s gate stands: on its road's middle line, at the centre of the cell the gate
    /// distance outside its outline.
    /// </summary>
    private Vector2 GatePosition(WorldComposition.Port port)
    {
        float middle = RoadMiddle(port, portWidths.TryGetValue(port, out int roadWidth) ? roadWidth : 1);
        Vector2Int step = PortSides.Step(port.Side);
        bool across = port.Side == PortSide.East || port.Side == PortSide.West;
        float line = port.Outer + 0.5f + settings.GateDistance * (across ? step.x : step.y);
        return across ? new Vector2(line, middle) : new Vector2(middle, line);
    }

    /// <summary>
    /// Steps 6 and 7, per chunk in row order on a stream of the chunk's own (the world's seed, its coordinates and its
    /// record's wilds seed): tall grass, meadow and weed patches on the wilds' plain grass, then its mobs.
    /// </summary>
    private void LayWilds()
    {
        Vector2Int size = composition.SizeInChunks;
        List<Vector2> entries = new();
        foreach (WorldDefinition.Entry entry in composition.Entries)
        {
            entries.Add(entry.Position);
        }

        List<Vector2Int> regionMobs = new();
        foreach (WorldComposition.Mob mob in composition.Mobs)
        {
            regionMobs.Add(mob.Cell);
        }

        for (int cy = 0; cy < size.y; cy++)
        {
            for (int cx = 0; cx < size.x; cx++)
            {
                Vector2Int coords = new(cx, cy);
                ChunkRecord record = settings.RecordOf(coords);
                Random random = new(ChunkSeed(coords, record != null ? record.WildsSeed : 0));
                RectInt bounds = ChunkCoords.Bounds(coords);
                LayPatches(random, bounds, tallGrass, 0u, settings.TallGrass);
                LayPatches(random, bounds, meadow, tallGrass, settings.Meadows);
                LayPatches(random, bounds, weeds, tallGrass | meadow, settings.Weeds);
                PlaceWildsMobs(random, coords, bounds, entries, regionMobs);
            }
        }
    }

    /// <summary>
    /// The seed of the chunk at <paramref name="coords"/>'s wilds stream, from the world's seed, its coordinates and
    /// <paramref name="wildsSeed"/>.
    /// </summary>
    private int ChunkSeed(Vector2Int coords, int wildsSeed)
    {
        unchecked
        {
            int hash = settings.Seed * 486187739;
            hash = (hash ^ coords.x * 73856093) * 16777619;
            hash = (hash ^ coords.y * 19349663) * 16777619;
            return (hash ^ wildsSeed * 83492791) * 16777619;
        }
    }

    /// <summary>
    /// Lays <paramref name="patches"/> of <paramref name="kind"/> in the chunk of <paramref name="bounds"/>: its edge
    /// patches centred on and kept to cells within their reach of a region, then its open ones anywhere, each a wobbly
    /// patch on the chunk's open wilds grass (<see cref="IsOpen"/>), a cell from any patch of <paramref name="others"/>;
    /// then drops the pieces under four cells.
    /// </summary>
    private void LayPatches(Random random, RectInt bounds, uint kind, uint others, WorldGenerator.Patches patches)
    {
        if (patches.EdgeCount + patches.OpenCount == 0)
        {
            return;
        }

        RectInt inside = new(bounds.xMin + 1, bounds.yMin + 1, bounds.width - 2, bounds.height - 2);
        int edge = PatchMargin + patches.Reach;
        bool Allowed(int x, int y) => inside.Contains(new Vector2Int(x, y)) && IsOpen(y * width + x) && !Touches(x, y, others);
        bool NearEdge(int x, int y) => Allowed(x, y) && regionSteps[y * width + x] <= edge;
        PaintPatches(random, inside, kind, patches.EdgeCount, patches.Radius, NearEdge);
        PaintPatches(random, inside, kind, patches.OpenCount, patches.Radius, Allowed);
        RemoveFragments(inside, kind);
    }

    /// <summary>
    /// Whether <paramref name="index"/> is open wilds grass: a wilds cell of grass alone, two cells or more from every
    /// region, the beach and the roads.
    /// </summary>
    private bool IsOpen(int index)
    {
        return owners[index] == WorldComposition.Wilds && cells[index] == grass && regionSteps[index] >= PatchMargin
            && beachSteps[index] >= PatchMargin && roadSteps[index] >= PatchMargin;
    }

    /// <summary>
    /// Whether <paramref name="x"/>, <paramref name="y"/> or one of its 8 neighbours holds a material of
    /// <paramref name="mask"/>.
    /// </summary>
    private bool Touches(int x, int y, uint mask)
    {
        if (mask == 0)
        {
            return false;
        }

        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int index = IndexOf(x + dx, y + dy);
                if (index >= 0 && (cells[index] & mask) != 0)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Paints up to <paramref name="count"/> wobbly patches of <paramref name="kind"/> of a radius between the two
    /// values of <paramref name="radii"/>, each centred on a random cell of <paramref name="area"/>, in row order, that
    /// <paramref name="allowed"/> allows and no patch covers yet, on the cells it allows.
    /// </summary>
    private void PaintPatches(Random random, RectInt area, uint kind, int count, Vector2 radii, Func<int, int, bool> allowed)
    {
        if (count == 0)
        {
            return;
        }

        List<Vector2Int> centres = new();
        for (int y = area.yMin; y < area.yMax; y++)
        {
            for (int x = area.xMin; x < area.xMax; x++)
            {
                if (allowed(x, y))
                {
                    centres.Add(new Vector2Int(x, y));
                }
            }
        }

        for (int i = 0; i < count && centres.Count > 0; i++)
        {
            Vector2Int centre = centres[random.Next(centres.Count)];
            float radius = Range(random, radii.x, Mathf.Max(radii.x, radii.y));
            Vector2 middle = centre + new Vector2(0.5f, 0.5f);
            Func<Vector2, bool> contains = Wobbly(random, middle, new Vector2(radius, radius * PatchSquash), 2f, PatchWobble);
            int reach = Mathf.CeilToInt(radius * 1.3f) + 1;
            for (int dy = -reach; dy <= reach; dy++)
            {
                for (int dx = -reach; dx <= reach; dx++)
                {
                    int x = centre.x + dx;
                    int y = centre.y + dy;
                    if (IndexOf(x, y) >= 0 && allowed(x, y) && contains(new Vector2(x + 0.5f, y + 0.5f)))
                    {
                        cells[y * width + x] |= kind;
                    }
                }
            }

            centres.RemoveAll(cell => (cells[cell.y * width + cell.x] & kind) != 0);
        }
    }

    /// <summary>
    /// A random superellipse of <paramref name="halfSize"/> around <paramref name="centre"/> whose radius wobbles by up
    /// to <paramref name="wobble"/> of itself with a few random harmonics, as the area generators' wobbly shapes, as a
    /// test of whether a point lies inside.
    /// </summary>
    private static Func<Vector2, bool> Wobbly(Random random, Vector2 centre, Vector2 halfSize, float squareness, float wobble)
    {
        float[] amplitudes = new float[OutlineHarmonics];
        float[] phases = new float[OutlineHarmonics];
        float total = 0f;
        for (int i = 0; i < OutlineHarmonics; i++)
        {
            amplitudes[i] = Range(random, 0.4f, 1f);
            phases[i] = Range(random, 0f, 2f * Mathf.PI);
            total += amplitudes[i];
        }

        return point =>
        {
            float nx = (point.x - centre.x) / halfSize.x;
            float ny = (point.y - centre.y) / halfSize.y;
            float radius = Mathf.Pow(Mathf.Pow(Mathf.Abs(nx), squareness) + Mathf.Pow(Mathf.Abs(ny), squareness), 1f / squareness);
            float angle = Mathf.Atan2(ny, nx);
            float offset = 0f;
            for (int i = 0; i < OutlineHarmonics; i++)
            {
                offset += amplitudes[i] * Mathf.Sin((i + 2) * angle + phases[i]);
            }

            return radius <= 1f + wobble * offset / total;
        };
    }

    /// <summary>
    /// Removes from <paramref name="area"/> every 4-connected piece of <paramref name="kind"/> of fewer than four cells.
    /// </summary>
    private void RemoveFragments(RectInt area, uint kind)
    {
        searchId++;
        List<int> piece = new();
        for (int y = area.yMin; y < area.yMax; y++)
        {
            for (int x = area.xMin; x < area.xMax; x++)
            {
                int start = y * width + x;
                if ((cells[start] & kind) == 0 || searched[start] == searchId || owners[start] != WorldComposition.Wilds)
                {
                    continue;
                }

                piece.Clear();
                piece.Add(start);
                searched[start] = searchId;
                for (int i = 0; i < piece.Count; i++)
                {
                    int px = piece[i] % width;
                    int py = piece[i] / width;
                    foreach (Vector2Int step in Neighbours4)
                    {
                        int next = IndexOf(px + step.x, py + step.y);
                        if (next >= 0 && searched[next] != searchId && (cells[next] & kind) != 0 && owners[next] == WorldComposition.Wilds && area.Contains(new Vector2Int(px + step.x, py + step.y)))
                        {
                            searched[next] = searchId;
                            piece.Add(next);
                        }
                    }
                }

                if (piece.Count < MinPatchPiece)
                {
                    foreach (int index in piece)
                    {
                        cells[index] &= ~kind;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Places up to the asset's count of wilds mobs in the chunk at <paramref name="coords"/>: on its wilds grass, off
    /// the roads and the beach, the chunk's mob margin inside its border (so two chunks' mobs keep their spacing without
    /// either reading the other), at least the entry distance from every waystone and gate and the mob spacing from
    /// every other mob, tried in random order.
    /// </summary>
    private void PlaceWildsMobs(Random random, Vector2Int coords, RectInt bounds, List<Vector2> entries, List<Vector2Int> regionMobs)
    {
        if (settings.MobsPerChunk == 0 || settings.WildsMob == null)
        {
            return;
        }

        uint grassy = grass | weeds | meadow | tallGrass;
        RectInt inside = new(bounds.xMin + MobChunkMargin, bounds.yMin + MobChunkMargin, bounds.width - 2 * MobChunkMargin, bounds.height - 2 * MobChunkMargin);
        List<Vector2Int> candidates = new();
        for (int y = inside.yMin; y < inside.yMax; y++)
        {
            for (int x = inside.xMin; x < inside.xMax; x++)
            {
                int index = y * width + x;
                if (owners[index] == WorldComposition.Wilds && (cells[index] & ~grassy) == 0 && regionSteps[index] >= PatchMargin)
                {
                    candidates.Add(new Vector2Int(x, y));
                }
            }
        }

        if (candidates.Count == 0)
        {
            return;
        }

        float entryDistance = settings.MobDistanceFromEntries;
        float spacing = settings.MobSpacing;
        int pad = Mathf.CeilToInt(Mathf.Max(entryDistance, spacing)) + 1;
        RectInt near = new(inside.xMin - pad, inside.yMin - pad, inside.width + 2 * pad, inside.height + 2 * pad);
        List<Vector2> nearEntries = new();
        foreach (Vector2 entry in entries)
        {
            if (near.Contains(Vector2Int.FloorToInt(entry)))
            {
                nearEntries.Add(entry);
            }
        }

        List<Vector2Int> nearMobs = new();
        foreach (Vector2Int mob in regionMobs)
        {
            if (near.Contains(mob))
            {
                nearMobs.Add(mob);
            }
        }

        for (int i = candidates.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
        }

        string scope = $"wilds_{coords.x}_{coords.y}";
        int placed = 0;
        foreach (Vector2Int cell in candidates)
        {
            if (placed >= settings.MobsPerChunk)
            {
                break;
            }

            Vector2 centre = cell + new Vector2(0.5f, 0.5f);
            bool fits = true;
            foreach (Vector2 entry in nearEntries)
            {
                fits &= Vector2.Distance(centre, entry) >= entryDistance;
            }

            foreach (Vector2Int mob in nearMobs)
            {
                fits &= Vector2Int.Distance(cell, mob) >= spacing;
            }

            if (!fits)
            {
                continue;
            }

            composition.Mobs.Add(new WorldComposition.Mob(settings.WildsMob, cell, GeneratedIds.For(worldId, scope, MobKind, placed), scope, placed));
            nearMobs.Add(cell);
            placed++;
        }
    }

    /// <summary>
    /// Lists each chunk's materials, in row order of the chunks: its own cells' and those of its neighbours' border
    /// strips (<see cref="WorldChunk.SkirtCells"/> wide), in the style's order, and hashes its content
    /// (<see cref="ChunkContent"/>).
    /// </summary>
    private void ListChunks()
    {
        Vector2Int size = composition.SizeInChunks;
        for (int cy = 0; cy < size.y; cy++)
        {
            for (int cx = 0; cx < size.x; cx++)
            {
                Vector2Int coords = new(cx, cy);
                ChunkRecord record = settings.RecordOf(coords);
                WorldComposition.Chunk chunk = new(coords, settings.World.HasChunk(coords), record != null && record.AllowWide);
                RectInt bounds = ChunkCoords.Bounds(coords);
                chunk.OwnMask = MaskOver(bounds);
                RectInt reach = new(bounds.xMin - WorldChunk.SkirtCells, bounds.yMin - WorldChunk.SkirtCells, bounds.width + 2 * WorldChunk.SkirtCells, bounds.height + 2 * WorldChunk.SkirtCells);
                chunk.ListMask = MaskOver(reach);
                for (int i = 0; i < composition.LayerOrder.Count; i++)
                {
                    if ((chunk.ListMask & (1u << i)) != 0)
                    {
                        chunk.Materials.Add(composition.LayerOrder[i]);
                    }
                }

                composition.Chunks.Add(chunk);
                chunk.ContentHash = ChunkContent.Of(composition, coords).Hash();
            }
        }
    }

    /// <summary>
    /// The materials of every cell of <paramref name="area"/>, clipped to the world, as one mask.
    /// </summary>
    private uint MaskOver(RectInt area)
    {
        uint mask = 0;
        int xMin = Mathf.Max(0, area.xMin);
        int yMin = Mathf.Max(0, area.yMin);
        int xMax = Mathf.Min(width, area.xMax);
        int yMax = Mathf.Min(height, area.yMax);
        for (int y = yMin; y < yMax; y++)
        {
            for (int x = xMin; x < xMax; x++)
            {
                mask |= cells[y * width + x];
            }
        }

        return mask;
    }

    /// <summary>
    /// Step 8. The checks, each a warning: the placement (every region on the land and on chunks the world has,
    /// <see cref="WorldGenerator.RegionGap"/> from every other and <see cref="WorldGenerator.RegionShoreGap"/> from the
    /// beach, a sea region's ocean joining the sea), the layer lists, reachability from the default entry, the ids, and
    /// every port linked.
    /// </summary>
    private void Check()
    {
        CheckPlacement();
        CheckLayerLists();
        CheckReachability();
        CheckIds();
        foreach (WorldComposition.Region region in composition.Regions)
        {
            foreach (WorldComposition.Port port in region.Ports)
            {
                if (!port.Linked)
                {
                    composition.Warnings.Add($"{region.Key}'s port facing {port.Side} at {port.Cells[0]} has no road link.");
                }
            }
        }
    }

    /// <summary>
    /// Warns of regions off the land or on missing chunks, too close to each other or to the beach, and of a sea
    /// region whose ocean does not meet the world's sea.
    /// </summary>
    private void CheckPlacement()
    {
        Vector2Int size = composition.SizeInChunks;
        chunkExists = new bool[size.x * size.y];
        for (int cy = 0; cy < size.y; cy++)
        {
            for (int cx = 0; cx < size.x; cx++)
            {
                chunkExists[cy * size.x + cx] = settings.World.HasChunk(new Vector2Int(cx, cy));
            }
        }

        int regionCount = composition.Regions.Count;
        for (int a = 0; a < regionCount; a++)
        {
            WorldComposition.Region region = composition.Regions[a];
            if (offLand[a] > 0)
            {
                composition.Warnings.Add($"{region.Key}: {offLand[a]} cells lie off the land, in the sea ring.");
            }

            for (int b = a + 1; b < regionCount; b++)
            {
                int distance = regionDistances[a, b];
                if (distance <= settings.RegionGap)
                {
                    composition.Warnings.Add($"{region.Key} and {composition.Regions[b].Key} have {distance - 1} cells of wilds between them, fewer than the region gap ({settings.RegionGap}).");
                }
            }

            if (!region.IsSea && shoreDistances[a] <= settings.RegionShoreGap)
            {
                composition.Warnings.Add($"{region.Key} has {shoreDistances[a] - 1} cells of wilds between it and the beach, fewer than the shore gap ({settings.RegionShoreGap}).");
            }

            int missing = 0;
            int touchesSea = 0;
            RectInt bounds = region.Bounds;
            for (int y = bounds.yMin; y < bounds.yMax; y++)
            {
                for (int x = bounds.xMin; x < bounds.xMax; x++)
                {
                    int index = y * width + x;
                    if (owners[index] != a)
                    {
                        continue;
                    }

                    if (!chunkExists[y / ChunkCoords.Size * size.x + x / ChunkCoords.Size])
                    {
                        missing++;
                    }

                    if (region.IsSea && (cells[index] & ocean) != 0)
                    {
                        foreach (Vector2Int step in Neighbours4)
                        {
                            touchesSea += OwnerAt(x + step.x, y + step.y) == WorldComposition.Sea ? 1 : 0;
                        }
                    }
                }
            }

            if (missing > 0)
            {
                composition.Warnings.Add($"{region.Key}: {missing} cells lie on chunks the world does not have.");
            }

            if (region.IsSea && touchesSea == 0)
            {
                composition.Warnings.Add($"{region.Key}'s ocean does not meet the world's sea: place it on the coast its sea faces.");
            }
        }
    }

    /// <summary>
    /// Warns of every chunk listing more than <see cref="TerrainControlMap.NarrowLayers"/> materials whose record does
    /// not allow it the wide variant.
    /// </summary>
    private void CheckLayerLists()
    {
        foreach (WorldComposition.Chunk chunk in composition.Chunks)
        {
            if (chunk.Materials.Count > TerrainControlMap.NarrowLayers && !chunk.AllowWide)
            {
                List<string> names = new();
                foreach (TerrainMaterial material in chunk.Materials)
                {
                    names.Add(material.name);
                }

                Block($"Chunk {chunk.Coords} lists {chunk.Materials.Count} materials, more than {TerrainControlMap.NarrowLayers}: {string.Join(", ", names)}.");
            }
        }
    }

    /// <summary>
    /// Warns of every waystone, gate and mob the default entry cannot walk to: over 4-connected cells whose top
    /// material the movement profile walks, off the cliff faces, stepping between raised and level ground only at a
    /// ramp's foot (<see cref="RaisedGround.CanCross"/>), as the navigation does.
    /// </summary>
    private void CheckReachability()
    {
        if (composition.DefaultEntry == null)
        {
            Block("The world has no region, so no default entry.");
            return;
        }

        if (settings.MovementProfile == null)
        {
            Block("The world generator has no movement profile, so reachability was not checked.");
            return;
        }

        bool[] reachable = Reachable(Vector2Int.FloorToInt(composition.DefaultEntry.Position));
        List<string> cut = new();
        foreach (WorldDefinition.Entry entry in composition.Entries)
        {
            int index = IndexAt(entry.Position);
            if (index < 0 || !reachable[index])
            {
                cut.Add($"{(entry.Waystone ? "waystone" : "gate")} {entry.Name}");
            }
        }

        foreach (WorldComposition.Mob mob in composition.Mobs)
        {
            if (!reachable[mob.Cell.y * width + mob.Cell.x])
            {
                cut.Add($"the mob at {mob.Cell} ({mob.Scope})");
            }
        }

        if (cut.Count > 0)
        {
            Block($"{cut.Count} entries and mobs cannot be walked to from {composition.DefaultEntry.Name}: {string.Join(", ", cut.GetRange(0, Mathf.Min(ListedInWarning, cut.Count)))}{(cut.Count > ListedInWarning ? ", ..." : string.Empty)}.");
        }
    }

    /// <summary>
    /// Adds <paramref name="warning"/> to the warnings and to those that stop a write.
    /// </summary>
    private void Block(string warning)
    {
        composition.Warnings.Add(warning);
        composition.BlockingWarnings.Add(warning);
    }

    /// <summary>
    /// The cells 4-connected to <paramref name="start"/> over walkable ground, as <see cref="CheckReachability"/>
    /// walks it.
    /// </summary>
    private bool[] Reachable(Vector2Int start)
    {
        bool[] walkable = WalkableMaterials();
        int faceRows = settings.TerrainStyle != null ? RaisedGround.RowsCovered(settings.TerrainStyle.CliffHeight / TerrainStyle.PixelsPerCell) : 0;
        RaisedKinds kinds = new(composition);
        bool[] reached = new bool[width * height];
        bool Walkable(int x, int y)
        {
            int top = WorldComposition.TopIndex(cells[y * width + x]);
            if (top < 0 || !walkable[top])
            {
                return false;
            }

            return faceRows == 0 || raised[y * width + x] == RaisedGround.Level || !RaisedGround.IsOnFace(kinds, new Vector3Int(x, y, 0), faceRows);
        }

        int first = IndexOf(start.x, start.y);
        if (first < 0 || !Walkable(start.x, start.y))
        {
            return reached;
        }

        int head = 0;
        int tail = 0;
        reached[first] = true;
        queue[tail++] = first;
        while (head < tail)
        {
            int index = queue[head++];
            int x = index % width;
            int y = index / width;
            foreach (Vector2Int step in Neighbours4)
            {
                int next = IndexOf(x + step.x, y + step.y);
                if (next < 0 || reached[next] || !RaisedGround.CanCross(raised[index], raised[next], new Vector3Int(step.x, step.y, 0)) || !Walkable(x + step.x, y + step.y))
                {
                    continue;
                }

                reached[next] = true;
                queue[tail++] = next;
            }
        }

        return reached;
    }

    /// <summary>
    /// Whether the movement profile walks each material of the layer order, by its terrain type
    /// (<see cref="GroundLayerMigration.TerrainTypePath"/>).
    /// </summary>
    private bool[] WalkableMaterials()
    {
        bool[] walkable = new bool[composition.LayerOrder.Count];
        for (int i = 0; i < walkable.Length; i++)
        {
            TerrainMaterial material = composition.LayerOrder[i];
            TerrainType2D terrainType = material != null ? AssetDatabase.LoadAssetAtPath<TerrainType2D>(GroundLayerMigration.TerrainTypePath(material)) : null;
            settings.MovementProfile.TryGetTraversal(terrainType, out walkable[i], out _);
        }

        return walkable;
    }

    /// <summary>
    /// Warns of any id two mobs share.
    /// </summary>
    private void CheckIds()
    {
        List<string> ids = new();
        foreach (WorldComposition.Mob mob in composition.Mobs)
        {
            ids.Add(mob.Id);
        }

        ids.Sort(StringComparer.Ordinal);
        for (int i = 1; i < ids.Count; i++)
        {
            if (ids[i] == ids[i - 1])
            {
                composition.Warnings.Add($"Two mobs share the id {ids[i]}.");
            }
        }
    }

    /// <summary>
    /// The owner of the cell at <paramref name="x"/>, <paramref name="y"/>: a region's index, the wilds or the sea
    /// (outside the world too).
    /// </summary>
    private int OwnerAt(int x, int y)
    {
        int index = IndexOf(x, y);
        return index >= 0 ? owners[index] : WorldComposition.Sea;
    }

    /// <summary>
    /// The index of the cell at <paramref name="x"/>, <paramref name="y"/> in the per-cell arrays, or -1 outside the
    /// world.
    /// </summary>
    private int IndexOf(int x, int y)
    {
        return x >= 0 && y >= 0 && x < width && y < height ? y * width + x : -1;
    }

    /// <summary>
    /// The index of the cell holding <paramref name="point"/>, or -1 outside the world.
    /// </summary>
    private int IndexAt(Vector2 point)
    {
        return IndexOf(Mathf.FloorToInt(point.x), Mathf.FloorToInt(point.y));
    }

    /// <summary>
    /// The centre of the cell at <paramref name="index"/>.
    /// </summary>
    private Vector2 CentreOf(int index)
    {
        return new Vector2(index % width + 0.5f, index / width + 0.5f);
    }

    /// <summary>
    /// A uniformly distributed value between <paramref name="min"/> and <paramref name="max"/> from
    /// <paramref name="random"/>.
    /// </summary>
    private static float Range(Random random, float min, float max)
    {
        return min + (float)random.NextDouble() * (max - min);
    }

    /// <summary>
    /// <paramref name="t"/> clamped to 0 to 1 and eased in and out.
    /// </summary>
    private static float Smooth(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }
}
