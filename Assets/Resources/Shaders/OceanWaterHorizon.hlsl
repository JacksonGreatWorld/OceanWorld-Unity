#ifndef OCEAN_WATER_HORIZON_INCLUDED
#define OCEAN_WATER_HORIZON_INCLUDED
// Shared by the sky and the distant opaque floor. A distance-only fog colour
// cannot meet a directional sky below the horizon, especially at seated height.
float3 OceanWaterHorizon(float3 horizon, float3 bottom, float elevation)
{
    return lerp(horizon, bottom, smoothstep(0.0, 0.65, -elevation));
}
#endif
