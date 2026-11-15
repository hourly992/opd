Shader "Street/Asphalt020S"
{
    Properties
    {
        _MainTex ("ambientCG Asphalt020S color", 2D) = "white" {}
        [Normal] _BumpMap ("ambientCG Asphalt020S NormalGL", 2D) = "bump" {}
        _MaskMap ("ambientCG AO (R) and roughness (G)", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _ToneGain ("Working color-space albedo gain", Vector) = (.76364610,.73200228,.71368004,1)
        _TileMeters ("Physical tile size in metres", Vector) = (1.85,1.85,0,0)
        _BumpStrength ("Normal strength", Range(0,1)) = 1
        _OcclusionStrength ("AO strength", Range(0,1)) = 1
        [HideInInspector] _SurfaceClass ("Asphalt class", Float) = 4
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Face culling", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull [_Cull]
        LOD 200
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert
        #pragma target 3.0
        #pragma multi_compile_instancing
        sampler2D _MainTex, _BumpMap, _MaskMap;
        float4 _TileMeters;
        float4 _ToneGain;
        fixed4 _Color;
        half _BumpStrength, _OcclusionStrength;
        struct Input { float3 worldPos; float3 worldNormal; INTERNAL_DATA };
        void vert(inout appdata_full v)
        {
            // Saved offline meshes can lack tangents. Supply an orthogonal basis
            // without changing geometry, normals, collision or any shared assets.
            float3 n = normalize(v.normal);
            float3 t = abs(n.y) > .8 ? float3(1,0,0) : normalize(cross(float3(0,1,0),n));
            t = normalize(t - n * dot(n,t));
            v.tangent = float4(t,1);
        }
        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float3 n = normalize(WorldNormalVector(IN,float3(0,0,1)));
            float3 t = abs(n.y) > .8 ? normalize(float3(1,0,0) - n*n.x) : normalize(cross(float3(0,1,0),n));
            float3 b = normalize(cross(n,t));
            float2 uv = float2(dot(IN.worldPos,t),dot(IN.worldPos,b)) / max(_TileMeters.xy,.05);
            float3 map = UnpackNormal(tex2D(_BumpMap,uv));
            map = normalize(lerp(float3(0,0,1),map,_BumpStrength));
            float3 world = normalize(t*map.x + b*map.y + n*map.z);
            float3 tangent = normalize(WorldNormalVector(IN,float3(1,0,0)));
            float3 bitangent = normalize(WorldNormalVector(IN,float3(0,1,0)));
            o.Normal = normalize(float3(dot(world,tangent),dot(world,bitangent),dot(world,n)));
            float2 mask = tex2D(_MaskMap,uv).rg;
            o.Albedo = tex2D(_MainTex,uv).rgb * _Color.rgb * _ToneGain.rgb;
            o.Occlusion = lerp(1,mask.r,_OcclusionStrength);
            o.Smoothness = 1-saturate(mask.g);
            o.Metallic = 0;
            o.Alpha = 1;
        }
        ENDCG
    }
    Fallback "Standard"
}
