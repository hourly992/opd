Shader "Street/OvercastSky"
{
    Properties
    {
        _Zenith ("Zenith", Color) = (.36,.40,.44,1)
        _Horizon ("Horizon", Color) = (.65,.67,.68,1)
        _Cloud ("Cloud", Color) = (.66,.67,.67,1)
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
            #include "UnityCG.cginc"
            float4 _Zenith, _Horizon, _Cloud;
            struct Output { float4 position : SV_POSITION; float3 direction : TEXCOORD0; };
            Output vert(float4 vertex : POSITION)
            {
                Output o; o.position = UnityObjectToClipPos(vertex); o.direction = vertex.xyz; return o;
            }
            float hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            float noise(float2 p)
            {
                float2 i=floor(p), f=frac(p); f=f*f*(3-2*f);
                return lerp(lerp(hash(i),hash(i+float2(1,0)),f.x),lerp(hash(i+float2(0,1)),hash(i+1),f.x),f.y);
            }
            float4 frag(Output i) : SV_Target
            {
                float3 d=normalize(i.direction);
                float h=saturate(d.y);
                float2 p=d.xz/(max(.12,d.y)+.35)*2.4;
                float clouds=noise(p)*.55+noise(p*2.1+5)*.28+noise(p*4.3)*.12+noise(p*9)*.05;
                float3 sky=lerp(_Horizon.rgb,_Zenith.rgb,pow(h,.45));
                sky=lerp(sky,_Cloud.rgb,smoothstep(.22,.76,clouds)*smoothstep(0,.35,h));
                return float4(sky,1);
            }
            ENDCG
        }
    }
    Fallback Off
}
