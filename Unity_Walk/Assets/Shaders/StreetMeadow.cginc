#ifndef STREET_MEADOW_INCLUDED
#define STREET_MEADOW_INCLUDED

float MeadowHash(float2 position)
{
    return frac(sin(dot(position, float2(127.1, 311.7))) * 43758.5453);
}

float MeadowNoise(float2 position)
{
    float2 cell = floor(position);
    float2 blend = frac(position);
    blend = blend * blend * (3 - 2 * blend);
    return lerp(lerp(MeadowHash(cell), MeadowHash(cell + float2(1, 0)), blend.x),
                lerp(MeadowHash(cell + float2(0, 1)), MeadowHash(cell + 1), blend.x), blend.y);
}

float MeadowDryness(float2 worldXZ)
{
    return MeadowNoise(worldXZ * .19 + float2(13.7, 43.2)) * .72
         + MeadowNoise(worldXZ * .68 + float2(7.1, 29.6)) * .28;
}
#endif
