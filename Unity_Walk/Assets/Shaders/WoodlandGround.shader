Shader "Street/WoodlandGround"
{
    Properties
    {
        _MainTex ("Forest ground", 2D) = "white" {}
        _GrassTex ("Neighbouring grass", 2D) = "white" {}
        _GrassTint ("Neighbouring grass tint", Color) = (.6,.86,.48,1)
        [Normal] _BumpMap ("Ground normal", 2D) = "bump" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Offset -1, -1
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0
        #pragma multi_compile_instancing
        sampler2D _MainTex, _GrassTex, _BumpMap;
        fixed4 _GrassTint;
        struct Input { float3 worldPos; float4 color : COLOR; };
        float hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
        float noise(float2 p)
        {
            float2 i=floor(p),f=frac(p);f=f*f*(3-2*f);
            return lerp(lerp(hash(i),hash(i+float2(1,0)),f.x),lerp(hash(i+float2(0,1)),hash(i+1),f.x),f.y);
        }
        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float2 uv=IN.worldPos.xz;
            float variation=.5+.5*sin(uv.x*.37+sin(uv.y*.28))*sin(uv.y*.43);
            // Match the existing ground's world projection and cross-scale grass variation at the edge.
            float2 guv=float2(uv.x,-uv.y)/2;
            float2 guv2=float2(-guv.y,guv.x)*.731+float2(.317,.851);
            float3 grass=lerp(tex2D(_GrassTex,guv).rgb,tex2D(_GrassTex,guv2).rgb,smoothstep(.25,.75,noise(uv*.065))*.62);
            grass*=lerp(.78,1.10,noise(uv*.037)+noise(uv*.31)*.15);
            grass=lerp(dot(grass,float3(.2126,.7152,.0722)).xxx,grass,.9)*_GrassTint.rgb;
            float3 litter=tex2D(_MainTex,uv/2.7).rgb*float3(.38,.40,.29);
            o.Albedo=lerp(grass,litter,IN.color.a*(.16+variation*.29));
            float3 normal=UnpackNormal(tex2D(_BumpMap,uv/2.7)); normal.xy*=.3;
            o.Normal=normalize(normal); o.Smoothness=.08; o.Occlusion=.95;
            o.Alpha=1;
        }
        ENDCG
    }
    Fallback "Transparent/Diffuse"
}
