#ifndef TOPDOWNRPG_TERRAIN_BLEND_LAYER_INCLUDED
#define TOPDOWNRPG_TERRAIN_BLEND_LAYER_INCLUDED

#ifdef _TERRAIN_SOFT
#include "Assets/Shaders/Terrain/TerrainBlendLayerSoft.hlsl"
#else
#include "Assets/Shaders/Terrain/TerrainBlendLayerCrisp.hlsl"
#endif

#endif
