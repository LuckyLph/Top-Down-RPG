using System;

/// <summary>
/// One movement profile's view of every page in a <see cref="NavigationPagePool"/>, indexed like the pool: traversal
/// cost (0 = blocked), the cost of entering a cell diagonally, and the cell's region, flooded per page (-1 = blocked).
/// <see cref="Roots"/> joins the regions that meet along the borders of loaded neighbouring pages, so two cells are
/// connected when their regions share a root. Each page is baked again when its stamp changes.
/// </summary>
internal sealed class NavigationCostField
{
    public int[] Costs = Array.Empty<int>();
    public int[] DiagonalCosts = Array.Empty<int>();
    public int[] Regions = Array.Empty<int>();
    public int[] Roots = Array.Empty<int>();
    public int[] SlotStamps = Array.Empty<int>();
    public int[] RegionCounts = Array.Empty<int>();
    public int MinimumCost;
    public int MinimumDiagonalCost;
    public int ProfileVersion;
    public int PoolVersion = -1;
    public int SlotsVersion = -1;
    public bool IsBaked;

    /// <summary>
    /// Grows the arrays to <paramref name="cellCapacity"/> cells and <paramref name="slotCount"/> slots, keeping what
    /// they hold.
    /// </summary>
    public void EnsureCapacity(int cellCapacity, int slotCount)
    {
        if (Costs.Length < cellCapacity)
        {
            Array.Resize(ref Costs, cellCapacity);
            Array.Resize(ref DiagonalCosts, cellCapacity);
            Array.Resize(ref Regions, cellCapacity);
            Array.Resize(ref Roots, cellCapacity);
        }

        if (SlotStamps.Length < slotCount)
        {
            Array.Resize(ref SlotStamps, slotCount);
            Array.Resize(ref RegionCounts, slotCount);
        }
    }

    /// <summary>
    /// Whether the cells at pool indices <paramref name="from"/> and <paramref name="to"/> are both walkable and in
    /// joined regions.
    /// </summary>
    public bool AreConnected(int from, int to)
    {
        int fromRegion = Regions[from];
        int toRegion = Regions[to];
        return fromRegion >= 0 && toRegion >= 0 && Roots[fromRegion] == Roots[toRegion];
    }
}
