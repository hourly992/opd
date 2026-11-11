Shader "Street/Glass"
{
    Properties
    {
        _MainTex ("Subdued room variation atlas", 2D) = "white" {}
        _DirtTex ("Fine glass dirt", 2D) = "white" {}
        _Color ("Transmission tint", Color) = (.66,.77,.81,1)
        _Interior ("Interior visibility", Range(0,1)) = .65
        _InteriorDetail ("Restrained room detail", Range(0,1)) = .20
        _ReflectionStrength ("Probe reflection strength", Range(0,2)) = 1
        [NoScaleOffset] _SkyTex ("Scene panoramic HDR sky fallback", 2D) = "black" {}
        [HideInInspector] _SkyTex_HDR ("HDR texture decode", Vector) = (1,1,0,0)
        _SkyTint ("Scene sky tint", Color) = (.5,.5,.5,1)
        [Gamma] _SkyExposure ("Scene sky exposure", Float) = .82
        _SkyRotation ("Scene sky rotation", Float) = 110
        _AutumnSky ("Use scene autumn sky", Range(0,1)) = 0
        _SkySoftDaylight ("Scene soft daylight", Range(0,1)) = 0
        _SkyLeaf ("Scene painted daylight", Range(0,1)) = 0
        _SkySunDirection ("Scene sun direction", Vector) = (0,.5,1,0)
        _Variant ("Variant offset", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Face culling", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull [_Cull]
        CGPROGRAM
        #pragma surface surf Window fullforwardshadows addshadow vertex:vert exclude_path:deferred
        #pragma target 3.0
        #pragma multi_compile_instancing
        #include "UnityPBSLighting.cginc"
        #include "StreetSkyLighting.cginc"
        sampler2D _MainTex, _DirtTex, _SkyTex;
        fixed4 _Color, _SkyTint;
        half4 _SkyTex_HDR;
        float _Interior, _Variant, _InteriorDetail, _ReflectionStrength, _SkyExposure, _SkyRotation;
        float _AutumnSky, _SkySoftDaylight, _SkyLeaf;
        float4 _SkySunDirection;
        struct Input
        {
            float2 paneUV;
            float variant;
            float3 worldPos;
            float3 worldNormal;
            float3 viewDir;
            float facing : VFACE;
            INTERNAL_DATA
        };
        float random(float n) { return frac(sin(n * 127.13 + 7.17) * 43758.5453); }
        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input,o);
            o.paneUV=v.texcoord.xy;
            o.variant=v.texcoord.z;
        }

        half3 PanoramicSky(float3 worldDirection)
        {
            float3 direction = Unity_SafeNormalize(worldDirection);
            // Skybox/Panoramic rotates the cube geometry +Rotation while retaining the original
            // lookup direction. An arbitrary reflected world ray therefore needs the INVERSE rotation.
            float sine, cosine;
            sincos(-_SkyRotation * UNITY_PI / 180, sine, cosine);
            direction = float3(cosine * direction.x - sine * direction.z, direction.y,
                sine * direction.x + cosine * direction.z);
            // Same latitude/longitude convention as Unity's built-in Skybox/Panoramic shader.
            float longitude = atan2(direction.z, direction.x);
            float latitude = acos(clamp(direction.y, -1, 1));
            float2 uv = float2(.5 - longitude / (2 * UNITY_PI), 1 - latitude / UNITY_PI);
            half3 radiance = DecodeHDR(tex2D(_SkyTex, uv), _SkyTex_HDR);
            UNITY_BRANCH if (_AutumnSky > 0)
                return StreetAutumnSkyColor(Unity_SafeNormalize(worldDirection),_SkySunDirection.xyz,
                    dot(radiance,float3(.2126,.7152,.0722)),_SkySoftDaylight,_SkyLeaf) * _SkyExposure;
            return max(0, radiance * _SkyTint.rgb * unity_ColorSpaceDouble.rgb * _SkyExposure);
        }

        half4 LightingWindow(SurfaceOutputStandard s, float3 viewDir, UnityGI gi)
        {
            float3 normal = Unity_SafeNormalize(s.Normal);
            float3 view = Unity_SafeNormalize(viewDir);
            half facing = saturate(abs(dot(normal, view)));
            // IOR 1.52 -> 4.26% at each air/glass interface. Approximate a two-pane sealed unit
            // with four incoherent interfaces; R = n*r/(1+(n-1)*r) stays energy-conserving.
            half front = .0426h + .9574h * pow(1 - facing, 5);
            half fresnel = 4 * front / (1 + 3 * front);
            // Retain nonblack scene probes, including box projection and blending. Some built-in
            // captures return a black probe while realtime cubemaps are unavailable; in that case
            // sample the same sky model and panorama as the visible environment.
            half3 probe = max(0, gi.indirect.specular);
            half3 sky = PanoramicSky(reflect(-view, normal));
            half signal = max(probe.r, max(probe.g, probe.b));
            half available = smoothstep(.0005h, .005h, signal);
            half3 reflection = lerp(sky, probe, available) * fresnel * _ReflectionStrength;
            // The photographed room is a transmitted interior, not diffuse paint on the facade.
            // Soft indirect fill lights it; a small amount of direct daylight can enter the room.
            half daylight = saturate(dot(normal, gi.light.dir)) * .035h;
            half3 transmission = s.Albedo * (max(0, gi.indirect.diffuse) + gi.light.color * daylight) * (1 - fresnel);
            UnityIndirect empty;
            empty.diffuse = half3(0,0,0);
            empty.specular = half3(0,0,0);
            // Direct solar highlights still use Unity's BRDF and the real shadow attenuation.
            half3 sun = UNITY_BRDF_PBS(half3(0,0,0), half3(.0426h,.0426h,.0426h), .9574h,
                s.Smoothness, normal, view, gi.light, empty).rgb;
            return half4(max(0, reflection + transmission + sun), 1);
        }
        void LightingWindow_GI(SurfaceOutputStandard s, UnityGIInput data, inout UnityGI gi)
        {
            // Keep UnityGlossyEnvironmentSetup / UnityGlobalIllumination for actual local probes.
            // The direct panorama fallback above also works when these GI channels return black.
            LightingStandard_GI(s, data, gi);
        }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            // Interpolating a constant UV seed can still introduce floating-point roundoff.
            // Quantize before the high-gain sine hash, otherwise curtain selection flickers per pixel.
            float seed = frac(floor(frac(IN.variant + _Variant * .173) * 256 + .37) / 256);
            float variant = floor(seed * 15.99);
            float2 cell = float2(fmod(variant, 4), floor(variant / 4));
            float2 uv = IN.paneUV;
            float3 normalWorld = normalize(WorldNormalVector(IN, float3(0,0,1)));
            float3 across = abs(normalWorld.y) > .95 ? float3(1,0,0) : normalize(cross(float3(0,1,0), normalWorld));
            float2 dirtUV = float2(dot(IN.worldPos, across), IN.worldPos.y) * .73;
            float dirt = saturate(tex2D(_DirtTex, dirtUV).r);

            // Recover metres per full UV rectangle from the geometry derivatives. A single room
            // texture must not be stretched across a several-metre arch or balcony glazing.
            float2 dxUV = ddx(uv), dyUV = ddy(uv);
            float3 dxP = ddx(IN.worldPos), dyP = ddy(IN.worldPos);
            float determinant = dxUV.x * dyUV.y - dxUV.y * dyUV.x;
            float denominator = (determinant < 0 ? -1 : 1) * max(abs(determinant), .00000001);
            float width = length((dxP * dyUV.y - dyP * dxUV.y) / denominator);
            float height = length((dyP * dxUV.x - dxP * dyUV.x) / denominator);
            float architectural = smoothstep(1.9, 2.7, max(width, height));

            float2 parallax = IN.viewDir.xy / max(.45, abs(IN.viewDir.z)) * .018;
            float2 roomUV = (cell + clamp(uv - parallax, .018, .982)) / 4;
            float3 room = tex2D(_MainTex, roomUV).rgb;
            // Only some windows contain a pale curtain. Most show a subdued dark interior;
            // the old identical high-contrast striped atlas no longer dominates every pane.
            float curtain = step(.68, random(seed * 31 + 2));
            float3 baseRoom = lerp(float3(.045,.052,.058), float3(.29,.285,.26), curtain);
            float3 temperature = lerp(float3(.88,.94,1), float3(1.02,.98,.89), random(seed * 19 + 3));
            float3 detail = lerp(float3(1,1,1), .86 + room * .28, _InteriorDetail);
            float visibility = _Interior * lerp(.65, 1, random(seed * 43 + 5)) * lerp(1, .055, architectural);
            float3 transmissionTint = lerp(float3(1,1,1), _Color.rgb, .18);
            o.Albedo = baseRoom * temperature * detail * visibility * transmissionTint;
            o.Normal = float3(0,0,IN.facing >= 0 ? 1 : -1);
            o.Smoothness = lerp(.89, .955, dirt);
            o.Metallic = 0;
            o.Occlusion = 1;
            o.Emission = 0;
            o.Alpha = 1;
        }
        ENDCG
    }
    Fallback "Standard"
}
