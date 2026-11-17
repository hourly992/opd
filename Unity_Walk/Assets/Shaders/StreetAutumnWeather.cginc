#ifndef STREET_AUTUMN_WEATHER_INCLUDED
#define STREET_AUTUMN_WEATHER_INCLUDED

// Runtime values are working-space HDR (Linear in Street). The same field
// drives a cached shadow tile, visible sky and analytic window reflections.
sampler2D _StreetWeatherCloudNoise;
sampler2D _StreetWeatherCloudCookie;
float _StreetWeatherEnabled;
float4 _StreetWeatherCookieOrigin;
float4 _StreetWeatherCookieRight;
float4 _StreetWeatherCookieUp;
float4 _StreetWeatherCloudParameters; // plane height, period, threshold, softness
float4 _StreetWeatherCloudOffset;     // world XZ drift, reserved
float4 _StreetWeatherSunDirection;
float4 _StreetWeatherSunColor;
float4 _StreetWeatherAmbientColor;
float4 _StreetWeatherZenith;
float4 _StreetWeatherHorizon;
float4 _StreetWeatherCloudLit;
float4 _StreetWeatherCloudShade;

float2 StreetWeatherCloudUV(float3 origin, float3 direction, float layer)
{
    float height = _StreetWeatherCloudParameters.x * layer;
    float distance = (height-origin.y) / max(.055,direction.y);
    return (origin.xz + direction.xz*distance - _StreetWeatherCloudOffset.xy) /
        (_StreetWeatherCloudParameters.y * layer) + (layer-1)*float2(.37,.19);
}

float StreetWeatherCloudShape(float density)
{
    float threshold = _StreetWeatherCloudParameters.z;
    float softness = _StreetWeatherCloudParameters.w;
    return smoothstep(threshold-softness,threshold+softness,density);
}

float StreetWeatherCloudShadow(float3 worldPosition)
{
    UNITY_BRANCH if (_StreetWeatherEnabled < .5) return 1;
    float3 relative = worldPosition-_StreetWeatherCookieOrigin.xyz;
    float2 uv = float2(dot(relative,_StreetWeatherCookieRight.xyz),
        dot(relative,_StreetWeatherCookieUp.xyz))+.5;
    return tex2D(_StreetWeatherCloudCookie,uv).a;
}

float3 StreetWeatherSkyColor(float3 worldPosition, float3 direction, float includeSun)
{
    direction = normalize(direction);
    float3 sun = normalize(_StreetWeatherSunDirection.xyz);
    float elevation = saturate(direction.y);
    float sunFacing = pow(saturate(dot(direction,sun)),6);
    float3 horizon = lerp(_StreetWeatherHorizon.rgb,
        _StreetWeatherHorizon.rgb*float3(1.10,1.025,.88),sunFacing*.30);
    float3 sky = lerp(horizon,_StreetWeatherZenith.rgb,smoothstep(0,.78,elevation));
    // The lower hemisphere gives reflections and SH a quiet, warm ground fill.
    sky = lerp(horizon*.27,sky,smoothstep(-.18,.015,direction.y));

    UNITY_BRANCH if (direction.y > .015)
    {
        float2 uv = StreetWeatherCloudUV(worldPosition,direction,1);
        float density = tex2D(_StreetWeatherCloudNoise,uv).r;
        float shape = StreetWeatherCloudShape(density);
        float nearSunDensity = tex2D(_StreetWeatherCloudNoise,
            uv + normalize(sun.xz+float2(.0001,.0001))*.009).r;
        float dimensional = saturate(.57+(density-nearSunDensity)*3.4);
        float silver = sunFacing*(1-shape)*.15;
        float3 cloud = lerp(_StreetWeatherCloudShade.rgb,_StreetWeatherCloudLit.rgb,dimensional)+silver;
        float farCloud = StreetWeatherCloudShape(tex2D(_StreetWeatherCloudNoise,
            StreetWeatherCloudUV(worldPosition,direction,1.8)).r);
        float horizonFade = smoothstep(.015,.16,direction.y);
        sky = lerp(sky,_StreetWeatherCloudLit.rgb*.83,farCloud*.20*horizonFade);
        sky = lerp(sky,cloud,shape*.82*horizonFade);
        float alignment = saturate(dot(direction,sun));
        // Angular radius ~0.30 degrees; the aureole is separate from the disk.
        float disk = smoothstep(.999974,.999990,alignment)*5.5;
        float halo = pow(alignment,1100)*.21 + pow(alignment,55)*.035;
        float cloudVisibility = 1-saturate(shape*.86+farCloud*.14);
        sky += _StreetWeatherSunColor.rgb*(disk+halo)*cloudVisibility*includeSun;
    }
    return max(0,sky);
}
#endif
