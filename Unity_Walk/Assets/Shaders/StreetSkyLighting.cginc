#ifndef STREET_SKY_LIGHTING_INCLUDED
#define STREET_SKY_LIGHTING_INCLUDED

float3 StreetAutumnSkyColor(float3 direction, float3 sunDirection, float cloudLight,
    float softDaylight, float leafSky, float paintedDistrict)
{
    float height = saturate(direction.y);
    float warm = pow(saturate(dot(direction, normalize(sunDirection))), 6) * (1 - height);
    UNITY_BRANCH if (paintedDistrict > 0)
    {
        float3 blueHorizon = lerp(float3(.32,.54,.91),float3(.54,.67,.83),warm * .55);
        float3 blueSky = lerp(blueHorizon,float3(.060,.250,.74),smoothstep(-.08,.70,direction.y));
        float cloud = smoothstep(.67,2.35,cloudLight) * smoothstep(.02,.20,direction.y);
        return lerp(blueSky, float3(.86,.91,.99), cloud * .52);
    }

    float3 horizon = lerp(float3(.38,.50,.51), float3(.68,.52,.34), warm * .72);
    horizon = lerp(horizon, lerp(float3(.47,.55,.56), float3(.72,.60,.44), warm * .62), softDaylight);
    float3 zenith = lerp(float3(.115,.255,.39), float3(.145,.28,.405), softDaylight);
    float3 sky = lerp(horizon, zenith, smoothstep(-.10,.56,direction.y));
    float cloud = smoothstep(.55,1.8,cloudLight);
    float3 cloudTint = lerp(float3(.69,.71,.65), float3(.79,.77,.70), softDaylight);
    sky = lerp(sky,cloudTint,cloud * .34 * smoothstep(-.04,.14,direction.y));
    UNITY_BRANCH if (leafSky > 0)
    {
        float3 paintedHorizon = lerp(float3(.295,.411,.432), float3(.57,.45,.32), warm * .66);
        float3 painted = lerp(paintedHorizon,float3(.102,.213,.337),smoothstep(-.08,.67,direction.y));
        float veil = smoothstep(.58,2.1,cloudLight) * smoothstep(.025,.26,direction.y);
        float3 paintedCloud = lerp(float3(.62,.69,.69),float3(.83,.74,.57),warm);
        painted = lerp(painted,paintedCloud,veil * .33);
        sky = lerp(sky,painted,leafSky);
    }
    return sky;
}
#endif
