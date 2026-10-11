#ifndef TOPDOWNRPG_ROUNDED_CORNERS_INCLUDED
#define TOPDOWNRPG_ROUNDED_CORNERS_INCLUDED

// Signed distance to the quarter plane x >= 0, y >= 0, positive outside.
float SdQuarterPlane(float x, float y)
{
    if (x >= 0.0 && y >= 0.0)
    {
        return -min(x, y);
    }
    return length(float2(min(x, 0.0), min(y, 0.0)));
}

// Signed distance to a quarter plane whose corner is rounded with radius r, with p measured from the unrounded corner.
float SdRoundedQuarter(float2 p, float r)
{
    return SdQuarterPlane(p.x - r, p.y - r) - r;
}

// The number of set bits among the low four.
uint CornerBitCount(uint m)
{
    return (m & 1u) + ((m >> 1) & 1u) + ((m >> 2) & 1u) + ((m >> 3) & 1u);
}

// The lowest set bit among the low four (3 when none is set).
uint CornerLowestBit(uint m)
{
    return (m & 1u) != 0 ? 0u : ((m & 2u) != 0 ? 1u : ((m & 4u) != 0 ? 2u : 3u));
}

// The direction from a corner to its cell k (0 down-left, 1 down-right, 2 up-left, 3 up-right).
float2 CornerCellDirection(uint k)
{
    return float2((k & 1u) != 0 ? 1.0 : -1.0, (k & 2u) != 0 ? 1.0 : -1.0);
}

// Exact signed distance, positive outside, from a point uv (measured from a corner of the grid, in the same unit as
// the radii) to the filled cells around that corner, given as a 4-bit mask in CornerCellDirection's order: outer
// corners rounded with radius rOut, inner corners filleted with radius rIn, and straight where a boundary crosses a
// cell border, so the patches of neighbouring corners agree. Returns far outside an empty mask and -far inside a full
// one. Both radii must stay under half a cell.
float CornerShapeDistance(uint mask, float2 uv, float rOut, float rIn, float far)
{
    if (mask == 0)
    {
        return far;
    }
    if (mask == 15)
    {
        return -far;
    }
    uint count = CornerBitCount(mask);
    if (count == 1)
    {
        return SdRoundedQuarter(CornerCellDirection(CornerLowestBit(mask)) * uv, rOut);
    }
    if (count == 3)
    {
        return -SdRoundedQuarter(CornerCellDirection(CornerLowestBit(~mask & 15u)) * uv, rIn);
    }
    if (mask == 3)
    {
        return uv.y;
    }
    if (mask == 12)
    {
        return -uv.y;
    }
    if (mask == 5)
    {
        return uv.x;
    }
    if (mask == 10)
    {
        return -uv.x;
    }
    uint first = CornerLowestBit(mask);
    uint second = CornerLowestBit(mask & ~(1u << first));
    return min(SdRoundedQuarter(CornerCellDirection(first) * uv, rOut), SdRoundedQuarter(CornerCellDirection(second) * uv, rOut));
}

#endif
