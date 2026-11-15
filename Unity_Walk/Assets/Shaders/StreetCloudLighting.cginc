#ifndef STREET_CLOUD_LIGHTING_INCLUDED
#define STREET_CLOUD_LIGHTING_INCLUDED

#include "UnityPBSLighting.cginc"
#include "StreetAutumnWeather.cginc"

// Preserve the material's Standard BRDF and sky/ambient GI. The shared cloud
// tile attenuates only the primary directional sun in the existing base pass.
// Local lights, emission, reflection probes and shadow-caster passes are intact.
inline half4 LightingStreetCloudStandard(SurfaceOutputStandard surface, half3 viewDir, UnityGI gi)
{
    return LightingStandard(surface, viewDir, gi);
}

inline void LightingStreetCloudStandard_GI(SurfaceOutputStandard surface, UnityGIInput data, inout UnityGI gi)
{
    LightingStandard_GI(surface, data, gi);
    #if defined(UNITY_PASS_FORWARDBASE) && defined(DIRECTIONAL)
    UNITY_BRANCH if (_StreetWeatherEnabled > .5)
        gi.light.color *= StreetWeatherCloudShadow(data.worldPos);
    #endif
}

#endif
