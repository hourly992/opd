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
            sampler2D _MainTex;
            half4 _MainTex_HDR;
            float _Exposure, _Rotation, _SoftDaylight, _LeafSky;
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
                float height = saturate(direction.y);
                float warm = pow(saturate(dot(direction, normalize(_SunDirection.xyz))), 6) * (1 - height);
                float3 horizon = lerp(float3(.38,.50,.51), float3(.68,.52,.34), warm * .72);
                horizon = lerp(horizon, lerp(float3(.47,.55,.56), float3(.72,.60,.44), warm * .62), _SoftDaylight);
                float3 zenith = lerp(float3(.115,.255,.39), float3(.145,.28,.405), _SoftDaylight);
                float3 sky = lerp(horizon, zenith, smoothstep(-.10, .56, direction.y));

                float longitude = atan2(direction.z, direction.x) - radians(_Rotation);
                float latitude = asin(clamp(direction.y, -1, 1));
                float2 uv = float2(frac(.5 - longitude / (2 * UNITY_PI)), .5 + latitude / UNITY_PI);
                float3 clouds = DecodeHDR(tex2D(_MainTex, uv), _MainTex_HDR);
                float cloudLight = dot(clouds, float3(.2126,.7152,.0722));
                float cloud = smoothstep(.55, 1.8, cloudLight);
                float3 cloudTint = lerp(float3(.69,.71,.65), float3(.79,.77,.70), _SoftDaylight);
                sky = lerp(sky, cloudTint, cloud * .34 * smoothstep(-.04, .14, direction.y));

                UNITY_BRANCH if (_LeafSky > 0)
                {
                    float3 paintedHorizon = lerp(float3(.295,.411,.432), float3(.57,.45,.32), warm * .66);
                    float3 paintedZenith = float3(.102,.213,.337);
                    float3 painted = lerp(paintedHorizon, paintedZenith, smoothstep(-.08,.67,direction.y));
                    float veil = smoothstep(.58,2.1,cloudLight) * smoothstep(.025,.26,direction.y);
                    float3 paintedCloud = lerp(float3(.62,.69,.69), float3(.83,.74,.57), warm);
                    painted = lerp(painted, paintedCloud, veil * .33);
                    sky = lerp(sky, painted, _LeafSky);
                }
                return half4(sky * _Exposure, 1);
            }
            ENDCG
        }
    }
}
