#ifndef TOPDOWNRPG_TERRAIN_BLEND_RUNS_INCLUDED
#define TOPDOWNRPG_TERRAIN_BLEND_RUNS_INCLUDED

#include "Assets/Shaders/Terrain/TerrainBlendShape.hlsl"

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
// height column, second value), in a cell where the layer runs further across, left and right, than up and down,
// counting up to TB_RUN_REACH cells each way; a tie keeps the textures as they are. What runs along a texture's x
// (a board) then lies across the layer's run, whichever way the run goes.
bool TBTurned(TBInputs ins, uint m, float2 pixel)
{
    bool turned = false;
    [branch]
    if (TBLut(ins, 3, 1 + (int)m).y > 0.5)
    {
        int2 cell = (int2)floor((pixel + 0.5) / ART_PIXELS_PER_UNIT);
        int across = TBRun(ins, cell, int2(1, 0), m) + TBRun(ins, cell, int2(-1, 0), m);
        int along = TBRun(ins, cell, int2(0, 1), m) + TBRun(ins, cell, int2(0, -1), m);
        turned = across > along;
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
