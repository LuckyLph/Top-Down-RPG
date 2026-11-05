#ifndef TOPDOWNRPG_TERRAIN_BLEND_RUNS_INCLUDED
#define TOPDOWNRPG_TERRAIN_BLEND_RUNS_INCLUDED

#include "Assets/Shaders/Terrain/TerrainBlendShape.hlsl"

#define TB_FACING_AUTOMATIC 0u
#define TB_FACING_EAST_WEST 1u
#define TB_FACING_NORTH_SOUTH 2u

TEXTURE2D(_TerrainTurns);

// Which way the layers that turn to their runs lie in a cell of the control map (TerrainTurnMap): TB_FACING_AUTOMATIC,
// where the runs decide, or fixed by the tile painted there, TB_FACING_EAST_WEST or TB_FACING_NORTH_SOUTH; automatic
// outside the map.
uint TBFacingCell(TBInputs ins, int2 c)
{
    if (c.x < 0 || c.y < 0 || c.x >= (int)ins.rect.z || c.y >= (int)ins.rect.w)
    {
        return TB_FACING_AUTOMATIC;
    }
    return (uint)(LOAD_TEXTURE2D(_TerrainTurns, c).r * 255.0 + 0.5);
}

// How many cells in a row, up to TB_RUN_REACH, layer m covers from a cell of the control map in one direction, the
// cell itself left out.
int TBRun(TBInputs ins, int2 cell, int2 direction, uint m)
{
    int run = 0;
    [loop]
    for (int i = 1; i <= TB_RUN_REACH; i++)
    {
        if ((TBCounted(TBCell(ins, cell + direction * i), ins.bridges, m) >> m) == 0u)
        {
            break;
        }
        run = i;
    }
    return run;
}

// Whether layer m's textures are turned a quarter at a map pixel: only for a layer that turns to its runs (its row's
// height column, second value). Where the tile painted in the pixel's cell fixes the way it lies (TBFacingCell), they
// are turned for north to south and not for east to west; elsewhere they are turned in a cell where the layer runs
// further across, left and right, than up and down, counting up to TB_RUN_REACH cells each way, a tie keeping the
// textures as they are. What runs along a texture's x (a board) then lies across the layer's run, whichever way the run
// goes.
bool TBTurned(TBInputs ins, uint m, float2 pixel)
{
    bool turned = false;
    [branch]
    if (TBLut(ins, TB_LAYER_HEIGHT_COLUMN, TBLayerRow(m)).y > 0.5)
    {
        int2 cell = (int2)floor((pixel + 0.5) / ART_PIXELS_PER_UNIT);
        uint facing = TBFacingCell(ins, cell);
        [branch]
        if (facing != TB_FACING_AUTOMATIC)
        {
            turned = facing == TB_FACING_NORTH_SOUTH;
        }
        else
        {
            int across = TBRun(ins, cell, int2(1, 0), m) + TBRun(ins, cell, int2(-1, 0), m);
            int along = TBRun(ins, cell, int2(0, 1), m) + TBRun(ins, cell, int2(0, -1), m);
            turned = across > along;
        }
    }
    return turned;
}

// The position a layer's textures are read at for a world pixel: the pixel itself, or turned a quarter, so the texture's
// x runs up the world and its top towards the left, which keeps the light's side on the top-left of what it shows.
float2 TBSampled(float2 worldPixel, bool turned)
{
    return turned ? float2(worldPixel.y, -worldPixel.x) : worldPixel;
}

// An offset read from a texture (a cell field's, a clump's rows) as an offset in the world: turned back a quarter
// where the texture was turned (TBSampled).
float2 TBUnturn(float2 offset, bool turned)
{
    return turned ? float2(-offset.y, offset.x) : offset;
}

#endif
