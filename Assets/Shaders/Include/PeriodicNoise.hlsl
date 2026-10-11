#ifndef TOPDOWNRPG_PERIODIC_NOISE_INCLUDED
#define TOPDOWNRPG_PERIODIC_NOISE_INCLUDED

#include "Assets/Shaders/Include/PixelSnap.hlsl"

#define PERIODIC_NOISE_PERIOD 128.0
#define PERIODIC_NOISE_MAX_SLOPE 0.33

// Smooth noise of three long waves that repeat every 128 px, about a pixel high at amplitude 1. Its slope stays under
// PERIODIC_NOISE_MAX_SLOPE (0.33) per pixel at amplitude 1, so a signed distance pushed by it keeps rising away from
// its shape up to amplitude 3.
float PeriodicEdgeNoise(float2 pixel)
{
    float2 q = (WrapPixel(pixel, PERIODIC_NOISE_PERIOD) + 0.5) * (6.28318530718 / PERIODIC_NOISE_PERIOD);
    return 0.45 * sin(dot(q, float2(4.0, 4.0)) + 1.3)
         + 0.25 * sin(dot(q, float2(8.0, -4.0)) + 4.1)
         + 0.2 * sin(dot(q, float2(4.0, 8.0)) + 2.6);
}

#endif
