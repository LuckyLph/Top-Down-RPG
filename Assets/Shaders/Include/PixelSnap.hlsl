#ifndef TOPDOWNRPG_PIXEL_SNAP_INCLUDED
#define TOPDOWNRPG_PIXEL_SNAP_INCLUDED

#define ART_PIXELS_PER_UNIT 32.0

// The art pixel of the 32 px-per-unit grid that holds a world position, as whole pixel coordinates, so every edge
// drawn from it lands on whole art pixels.
float2 SnapToArtPixel(float2 worldPosition)
{
    return floor(worldPosition * ART_PIXELS_PER_UNIT);
}

// A pixel coordinate wrapped into [0, period).
float2 WrapPixel(float2 pixel, float period)
{
    return pixel - period * floor(pixel / period);
}

#endif
