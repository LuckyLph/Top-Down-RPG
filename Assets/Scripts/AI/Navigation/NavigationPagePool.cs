using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Slots for <see cref="NavigationPage"/>s, shared by every map placed in it: a page's cells sit at
/// <c>slot * SlotSize + local index</c> in one flat index space, so the cliff sides, each movement profile's
/// <see cref="NavigationCostField"/> and the A* search arrays are flat arrays sized once to the pool and reused by every
/// search. The Gameplay scope holds one for every loaded world, with room for every chunk the streaming keeps loaded; an
/// area grid has a pool of its own with a single slot. A page placed when every slot is taken grows the pool, with a
/// warning.
/// </summary>
public sealed class NavigationPagePool
{
    public const int DefaultSlots = 24;

    private const int DefaultTraversalCost = 10;

    private readonly Dictionary<TerrainMovementProfile2D, NavigationCostField> fieldsByProfile = new();
    private readonly int[] layerCosts = new int[NavigationPage.MaxLayers];
    private NavigationPage[] slots;
    private byte[] cliffSides;
    private int[] regionQueue = Array.Empty<int>();
    private NavigationCostField defaultField;
    private int slotsVersion;
    private int nextStamp;

    /// <summary>
    /// A pool for world chunks: <see cref="DefaultSlots"/> slots of a chunk's cells each.
    /// </summary>
    public NavigationPagePool()
        : this(DefaultSlots, ChunkCoords.Size * ChunkCoords.Size)
    {
    }

    /// <summary>
    /// A pool of <paramref name="slotCount"/> slots of <paramref name="slotSize"/> cells each (at least one of each).
    /// </summary>
    internal NavigationPagePool(int slotCount, int slotSize)
    {
        SlotSize = Mathf.Max(1, slotSize);
        slots = new NavigationPage[Mathf.Max(1, slotCount)];
        cliffSides = new byte[slots.Length * SlotSize];
    }

    public int SlotSize { get; }
    public int SlotCount => slots.Length;
    public int CellCapacity => slots.Length * SlotSize;

    /// <summary>
    /// How many slots hold a page.
    /// </summary>
    public int UsedSlots { get; private set; }

    /// <summary>
    /// Changes whenever a page is placed, released or changed, so cost fields know to check their pages.
    /// </summary>
    internal int Version { get; private set; }

    internal NavigationPage[] Slots => slots;

    /// <summary>
    /// The sides of each cell the cliff of raised ground runs along, as <see cref="NavigationGrid2D.LeftSide"/>,
    /// <see cref="NavigationGrid2D.RightSide"/>, <see cref="NavigationGrid2D.DownSide"/> and
    /// <see cref="NavigationGrid2D.UpSide"/> bits.
    /// </summary>
    internal byte[] CliffSides => cliffSides;

    internal GridAStarPathfinder2D.SearchState Search { get; } = new();

    /// <summary>
    /// Puts <paramref name="page"/> in a free slot, growing the pool when none is free, and clears its cliff sides.
    /// </summary>
    internal void Acquire(NavigationPage page)
    {
        if (page.CellCount > SlotSize)
        {
            throw new ArgumentException($"A page of {page.CellCount} cells does not fit a slot of {SlotSize}.", nameof(page));
        }

        int slot = Array.IndexOf(slots, null);
        if (slot < 0)
        {
            slot = slots.Length;
            Debug.LogWarning($"{nameof(NavigationPagePool)} is full ({slots.Length} pages); growing it to {slots.Length * 2}.");
            Array.Resize(ref slots, slots.Length * 2);
            Array.Resize(ref cliffSides, slots.Length * SlotSize);
        }

        slots[slot] = page;
        page.Slot = slot;
        page.Base = slot * SlotSize;
        Array.Clear(cliffSides, page.Base, SlotSize);
        UsedSlots++;
        slotsVersion++;
        Touch(page);
    }

    /// <summary>
    /// Frees the slot of <paramref name="page"/>.
    /// </summary>
    internal void Release(NavigationPage page)
    {
        if (page.Slot < 0 || slots[page.Slot] != page)
        {
            return;
        }

        slots[page.Slot] = null;
        page.Slot = -1;
        UsedSlots--;
        slotsVersion++;
        Version++;
    }

    /// <summary>
    /// Records that <paramref name="page"/>'s cells, or what its border cells read from its neighbours, changed: it gets
    /// a new stamp, so every cost field bakes it again on next use.
    /// </summary>
    internal void Touch(NavigationPage page)
    {
        page.Stamp = ++nextStamp;
        page.Changes++;
        Version++;
    }

    /// <summary>
    /// The cost field of <paramref name="movementProfile"/> (null = every terrain walkable at the default cost), with
    /// every page whose stamp or the profile's version changed baked again, and the regions joined across borders
    /// again after any change.
    /// </summary>
    internal NavigationCostField GetCostField(TerrainMovementProfile2D movementProfile)
    {
        NavigationCostField field;
        if (movementProfile == null)
        {
            defaultField ??= new NavigationCostField();
            field = defaultField;
        }
        else if (!fieldsByProfile.TryGetValue(movementProfile, out field))
        {
            field = new NavigationCostField();
            fieldsByProfile[movementProfile] = field;
        }

        int profileVersion = movementProfile != null ? movementProfile.Version : 0;
        if (field.IsBaked && field.PoolVersion == Version && field.ProfileVersion == profileVersion)
        {
            return field;
        }

        field.EnsureCapacity(CellCapacity, slots.Length);
        bool bakeAll = !field.IsBaked || field.ProfileVersion != profileVersion;
        field.MinimumCost = movementProfile != null ? movementProfile.GetMinimumTraversalCost() : DefaultTraversalCost;
        field.MinimumDiagonalCost = NavigationMap2D.ScaleDiagonalCost(field.MinimumCost);
        bool baked = false;
        for (int slot = 0; slot < slots.Length; slot++)
        {
            NavigationPage page = slots[slot];
            if (page == null || (!bakeAll && field.SlotStamps[slot] == page.Stamp))
            {
                continue;
            }

            BakePage(field, page, movementProfile);
            field.SlotStamps[slot] = page.Stamp;
            baked = true;
        }

        if (baked || field.SlotsVersion != slotsVersion)
        {
            JoinRegions(field);
        }

        field.IsBaked = true;
        field.ProfileVersion = profileVersion;
        field.PoolVersion = Version;
        field.SlotsVersion = slotsVersion;
        return field;
    }

    /// <summary>
    /// Whether the cost field of <paramref name="movementProfile"/> is baked and current, without baking it.
    /// </summary>
    internal bool IsCostFieldCurrent(TerrainMovementProfile2D movementProfile)
    {
        NavigationCostField field = defaultField;
        if (movementProfile != null)
        {
            fieldsByProfile.TryGetValue(movementProfile, out field);
        }

        int profileVersion = movementProfile != null ? movementProfile.Version : 0;
        return field != null && field.IsBaked && field.ProfileVersion == profileVersion && field.PoolVersion == Version;
    }

    /// <summary>
    /// The traversal cost of the cell at <paramref name="local"/> of <paramref name="page"/> under
    /// <paramref name="movementProfile"/>, read from its terrain rather than a baked field.
    /// </summary>
    internal static int TraversalCostOf(NavigationPage page, int local, TerrainMovementProfile2D movementProfile)
    {
        ulong bits = page.Terrain[local];
        if (bits == 0)
        {
            return 0;
        }

        if (movementProfile == null)
        {
            return DefaultTraversalCost;
        }

        int highest = 0;
        for (int layer = 0; bits != 0; layer++, bits >>= 1)
        {
            if ((bits & 1) == 0)
            {
                continue;
            }

            movementProfile.TryGetTraversal(page.LayerTypes[layer], out bool isWalkable, out int cost);
            if (!isWalkable)
            {
                return 0;
            }

            highest = Mathf.Max(highest, cost);
        }

        return highest > 0 ? highest : movementProfile.DefaultTraversalCost;
    }

    /// <summary>
    /// Fills <paramref name="page"/>'s part of <paramref name="field"/>: traversal costs plus the profile's edge cost,
    /// diagonal costs and regions.
    /// </summary>
    private void BakePage(NavigationCostField field, NavigationPage page, TerrainMovementProfile2D movementProfile)
    {
        int[] costs = field.Costs;
        int start = page.Base;
        int count = page.CellCount;
        FillLayerCosts(page, movementProfile);
        for (int i = 0; i < count; i++)
        {
            costs[start + i] = TraversalCost(page.Terrain[i], movementProfile);
        }

        int edgeCost = movementProfile != null ? movementProfile.EdgeCost : 0;
        if (edgeCost > 0)
        {
            for (int i = 0; i < count; i++)
            {
                if (costs[start + i] > 0 && (cliffSides[start + i] != 0 || TouchesBlockedCell(page, i, costs, movementProfile)))
                {
                    costs[start + i] += edgeCost;
                }
            }
        }

        int[] diagonalCosts = field.DiagonalCosts;
        for (int i = 0; i < count; i++)
        {
            diagonalCosts[start + i] = costs[start + i] > 0 ? NavigationMap2D.ScaleDiagonalCost(costs[start + i]) : 0;
        }

        field.RegionCounts[page.Slot] = LabelRegions(field, page);
    }

    /// <summary>
    /// The cost of each terrain layer of <paramref name="page"/> under <paramref name="movementProfile"/>, 0 for one it
    /// cannot walk, into the scratch array the bake reads.
    /// </summary>
    private void FillLayerCosts(NavigationPage page, TerrainMovementProfile2D movementProfile)
    {
        for (int i = 0; i < page.LayerTypes.Length; i++)
        {
            if (movementProfile == null)
            {
                layerCosts[i] = DefaultTraversalCost;
                continue;
            }

            movementProfile.TryGetTraversal(page.LayerTypes[i], out bool isWalkable, out int cost);
            layerCosts[i] = isWalkable ? cost : 0;
        }
    }

    /// <summary>
    /// A cell's traversal cost from its terrain <paramref name="bits"/> and the layer costs filled for its page: 0
    /// without terrain or when any of its layers is unwalkable, else the highest cost among them. Without a profile
    /// every terrain costs the default.
    /// </summary>
    private int TraversalCost(ulong bits, TerrainMovementProfile2D movementProfile)
    {
        if (bits == 0)
        {
            return 0;
        }

        if (movementProfile == null)
        {
            return DefaultTraversalCost;
        }

        int highest = 0;
        for (int layer = 0; bits != 0; layer++, bits >>= 1)
        {
            if ((bits & 1) == 0)
            {
                continue;
            }

            int cost = layerCosts[layer];
            if (cost == 0)
            {
                return 0;
            }

            highest = Mathf.Max(highest, cost);
        }

        return highest > 0 ? highest : movementProfile.DefaultTraversalCost;
    }

    /// <summary>
    /// Whether the cell at <paramref name="local"/> of <paramref name="page"/> has a blocked cell among its 8
    /// neighbours. A neighbour in another loaded page is read from that page's terrain; one where no page is loaded (the
    /// edge of an area, an unloaded chunk) counts as blocked.
    /// </summary>
    private static bool TouchesBlockedCell(NavigationPage page, int local, int[] costs, TerrainMovementProfile2D movementProfile)
    {
        int width = page.Width;
        int height = page.Height;
        int x = local % width;
        int y = local / width;
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx;
                int ny = y + dy;
                if ((uint)nx < (uint)width && (uint)ny < (uint)height)
                {
                    if (costs[page.Base + (ny * width) + nx] == 0)
                    {
                        return true;
                    }

                    continue;
                }

                if (!page.TryLocateAround(nx, ny, out NavigationPage neighbour, out int neighbourLocal)
                    || TraversalCostOf(neighbour, neighbourLocal, movementProfile) == 0)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Labels the 4-connected regions of <paramref name="page"/>'s walkable cells with a flood fill that stays in the
    /// page and does not cross the cliff; a region's label is the page's base plus its number, blocked cells get -1.
    /// Returns how many regions it found.
    /// </summary>
    private int LabelRegions(NavigationCostField field, NavigationPage page)
    {
        int[] costs = field.Costs;
        int[] regions = field.Regions;
        int start = page.Base;
        int count = page.CellCount;
        int width = page.Width;
        int height = page.Height;
        Array.Fill(regions, -1, start, count);
        if (regionQueue.Length < count)
        {
            regionQueue = new int[count];
        }

        int next = 0;
        for (int seed = 0; seed < count; seed++)
        {
            if (costs[start + seed] == 0 || regions[start + seed] >= 0)
            {
                continue;
            }

            int label = start + next;
            int head = 0;
            int tail = 0;
            regions[start + seed] = label;
            regionQueue[tail++] = seed;
            while (head < tail)
            {
                int current = regionQueue[head++];
                int x = current % width;
                int y = current / width;
                byte sides = cliffSides[start + current];
                if (x > 0 && (sides & NavigationGrid2D.LeftSide) == 0)
                {
                    tail = EnqueueRegionCell(current - 1, label, start, costs, regions, tail);
                }

                if (x < width - 1 && (sides & NavigationGrid2D.RightSide) == 0)
                {
                    tail = EnqueueRegionCell(current + 1, label, start, costs, regions, tail);
                }

                if (y > 0 && (sides & NavigationGrid2D.DownSide) == 0)
                {
                    tail = EnqueueRegionCell(current - width, label, start, costs, regions, tail);
                }

                if (y < height - 1 && (sides & NavigationGrid2D.UpSide) == 0)
                {
                    tail = EnqueueRegionCell(current + width, label, start, costs, regions, tail);
                }
            }

            next++;
        }

        return next;
    }

    /// <summary>
    /// Labels the cell at local <paramref name="local"/> with <paramref name="label"/> and queues it when it is
    /// walkable and not labeled yet. Returns the new queue tail.
    /// </summary>
    private int EnqueueRegionCell(int local, int label, int start, int[] costs, int[] regions, int tail)
    {
        if (costs[start + local] == 0 || regions[start + local] >= 0)
        {
            return tail;
        }

        regions[start + local] = label;
        regionQueue[tail] = local;
        return tail + 1;
    }

    /// <summary>
    /// Rebuilds the roots of every loaded page's regions: each its own, then joined with the regions they meet along
    /// the right and upper borders of loaded neighbouring pages where no cliff runs between them, then flattened so
    /// every lookup is a single read.
    /// </summary>
    private void JoinRegions(NavigationCostField field)
    {
        int[] roots = field.Roots;
        int[] regions = field.Regions;
        for (int slot = 0; slot < slots.Length; slot++)
        {
            NavigationPage page = slots[slot];
            if (page == null)
            {
                continue;
            }

            for (int region = 0; region < field.RegionCounts[slot]; region++)
            {
                roots[page.Base + region] = page.Base + region;
            }
        }

        for (int slot = 0; slot < slots.Length; slot++)
        {
            NavigationPage page = slots[slot];
            if (page == null)
            {
                continue;
            }

            int width = page.Width;
            int height = page.Height;
            NavigationPage right = page.Around[5];
            if (right != null && right.Height == height)
            {
                for (int y = 0; y < height; y++)
                {
                    int from = page.Base + (y * width) + width - 1;
                    int to = right.Base + (y * right.Width);
                    if ((cliffSides[from] & NavigationGrid2D.RightSide) == 0)
                    {
                        Union(roots, regions[from], regions[to]);
                    }
                }
            }

            NavigationPage up = page.Around[7];
            if (up != null && up.Width == width)
            {
                for (int x = 0; x < width; x++)
                {
                    int from = page.Base + ((height - 1) * width) + x;
                    int to = up.Base + x;
                    if ((cliffSides[from] & NavigationGrid2D.UpSide) == 0)
                    {
                        Union(roots, regions[from], regions[to]);
                    }
                }
            }
        }

        for (int slot = 0; slot < slots.Length; slot++)
        {
            NavigationPage page = slots[slot];
            if (page == null)
            {
                continue;
            }

            for (int region = 0; region < field.RegionCounts[slot]; region++)
            {
                roots[page.Base + region] = Find(roots, page.Base + region);
            }
        }
    }

    /// <summary>
    /// Joins the regions <paramref name="a"/> and <paramref name="b"/> under the lower root; nothing when either is
    /// blocked (-1).
    /// </summary>
    private static void Union(int[] roots, int a, int b)
    {
        if (a < 0 || b < 0)
        {
            return;
        }

        int rootA = Find(roots, a);
        int rootB = Find(roots, b);
        if (rootA < rootB)
        {
            roots[rootB] = rootA;
        }
        else if (rootB < rootA)
        {
            roots[rootA] = rootB;
        }
    }

    /// <summary>
    /// The root of <paramref name="region"/>, halving the path to it on the way.
    /// </summary>
    private static int Find(int[] roots, int region)
    {
        while (roots[region] != region)
        {
            roots[region] = roots[roots[region]];
            region = roots[region];
        }

        return region;
    }
}
