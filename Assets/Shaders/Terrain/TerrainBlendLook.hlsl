#ifndef TOPDOWNRPG_TERRAIN_BLEND_LOOK_INCLUDED
#define TOPDOWNRPG_TERRAIN_BLEND_LOOK_INCLUDED

#ifdef _TERRAIN_SOFT
#include "Assets/Shaders/Terrain/TerrainBlendSoft.hlsl"
#else
#include "Assets/Shaders/Terrain/TerrainBlendCrisp.hlsl"
#endif

#endif
