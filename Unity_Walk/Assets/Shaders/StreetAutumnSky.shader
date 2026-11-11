Shader "Skybox/StreetAutumn"
{
    Properties
    {
        [NoScaleOffset] _MainTex ("Cloud panorama", 2D) = "gray" {}
        _Exposure ("Sky exposure", Float) = 1
        _Rotation ("Panorama rotation", Float) = 110
        _SunDirection ("Sun direction", Vector) = (0,.5,1,0)
        _SoftDaylight ("Soft autumn daylight", Range(0,1)) = 0
        _LeafSky ("Painted autumn sky", Range(0,1)) = 0
        _PaintedDistrictSky ("Blue autumn sky", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "StreetSkyLighting.cginc"
            sampler2D _MainTex;
            half4 _MainTex_HDR;
            float _Exposure, _Rotation, _SoftDaylight, _LeafSky, _PaintedDistrictSky;
            float4 _SunDirection;
            struct Varyings { float4 position : SV_POSITION; float3 direction : TEXCOORD0; };
            Varyings vert(float4 vertex : POSITION)
            {
                Varyings output;
                output.position = UnityObjectToClipPos(vertex);
                output.direction = vertex.xyz;
                return output;
            }
            half4 frag(Varyings input) : SV_Target
            {
                float3 direction = normalize(input.direction);
                float longitude = atan2(direction.z, direction.x) - radians(_Rotation);
                float latitude = asin(clamp(direction.y, -1, 1));
                float2 uv = float2(frac(.5 - longitude / (2 * UNITY_PI)), .5 + latitude / UNITY_PI);
                float3 clouds = DecodeHDR(tex2D(_MainTex, uv), _MainTex_HDR);
                float cloudLight = dot(clouds, float3(.2126,.7152,.0722));
                float3 sky = StreetAutumnSkyColor(direction,_SunDirection.xyz,cloudLight,_SoftDaylight,_LeafSky,_PaintedDistrictSky);
                return half4(sky * _Exposure, 1);
            }
            ENDCG
        }
    }
}
