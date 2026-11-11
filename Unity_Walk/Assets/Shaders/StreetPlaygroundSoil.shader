Shader "Street/PlaygroundSoil"
{
    Properties
    {
        _MainTex ("Compacted soil colour", 2D) = "gray" {}
        [Normal] _BumpMap ("Compacted soil normal", 2D) = "bump" {}
        _MaskMap ("Occlusion and roughness", 2D) = "white" {}
        _Color ("Muted soil tint", Color) = (.75,.72,.65,1)
        _EdgeWidth ("Feathered edge in metres", Range(.05,1)) = .68
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows addshadow
        #pragma target 3.0
        #include "StreetMeadow.cginc"
        sampler2D _MainTex, _BumpMap, _MaskMap;
        fixed4 _Color;
        float _EdgeWidth;
        struct Input { float2 uv_MainTex; float3 worldPos; float4 color : COLOR; };

        void surf(Input input, inout SurfaceOutputStandard surface)
        {
            float2 ground = input.worldPos.xz;
            float distanceToEdge = input.color.r * 2;
            float edgeWidth = _EdgeWidth * lerp(.44, 1.16, MeadowNoise(ground * 2.7));
            float sandClearance = input.color.g * .2 - .1;
            float outerCover = smoothstep(.018, edgeWidth, distanceToEdge);
            float groundCover = smoothstep(.003, .031, sandClearance);
            float cover = min(outerCover, groundCover);
            clip(cover - lerp(.02, .98, MeadowNoise(ground * 47)));

            float3 soil = tex2D(_MainTex, input.uv_MainTex).rgb;
            float value = dot(soil, float3(.2126, .7152, .0722));
            float wear = MeadowNoise(ground * .45 + 23);
            float3 mask = tex2D(_MaskMap, input.uv_MainTex).rgb;
            surface.Albedo = lerp(value.xxx, soil, .58) * _Color.rgb * lerp(.78, 1.04, wear);
            surface.Normal = normalize(lerp(float3(0,0,1), UnpackNormal(tex2D(_BumpMap, input.uv_MainTex)), .32));
            surface.Occlusion = lerp(.76, 1, mask.r);
            surface.Smoothness = lerp(.07, .17, 1 - mask.g);
            surface.Metallic = 0;
            surface.Alpha = 1;
        }
        ENDCG
    }
    Fallback "Diffuse"
}
