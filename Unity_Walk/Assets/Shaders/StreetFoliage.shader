Shader "Street/Foliage"
{
    Properties
    {
        _MainTex ("CC0 foliage RGBA", 2D) = "white" {}
        [Normal] _BumpMap ("Normal", 2D) = "bump" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Cutoff ("Alpha cutoff", Range(0,1)) = .45
        _BumpStrength ("Normal strength", Range(0,1)) = .35
        _Translucency ("Thin leaf transmission", Range(0,1)) = .32
        _WindStrength ("Wind bend in metres", Range(0,.3)) = .065
        _WindStemHeight ("Stationary stem height", Float) = 1.5
        _WindHeightScale ("Canopy bend per metre", Float) = .23
        _Grass ("Rooted grass blades", Float) = 0
        _MeadowKeep ("Grass coverage threshold", Range(0,1)) = 1
        _AutumnAmount ("Autumn palette strength", Range(0,1)) = 0
        _AutumnGold ("Autumn gold", Color) = (.8,.53,.08,1)
        _AutumnCopper ("Autumn copper", Color) = (.68,.23,.06,1)
        _AutumnCrownVariation ("Autumn crown clusters", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "Queue"="AlphaTest" "RenderType"="TransparentCutout" "IgnoreProjector"="True" }
        Cull Off
        AlphaToMask On
        LOD 300
        CGPROGRAM
        #pragma surface surf Leaf fullforwardshadows alphatest:_Cutoff addshadow vertex:vert exclude_path:deferred
        #pragma target 3.0
        #pragma multi_compile_instancing
        #include "UnityPBSLighting.cginc"
        #include "StreetMeadow.cginc"
        sampler2D _MainTex, _BumpMap;
        fixed4 _Color;
        float _BumpStrength, _Translucency, _WindStrength, _Grass, _WindStemHeight, _WindHeightScale;
        float _MeadowKeep;
        float _AutumnAmount, _AutumnCrownVariation;
        fixed4 _AutumnGold, _AutumnCopper;
        struct Input { float2 uv_MainTex; float4 color : COLOR; float facing : VFACE; float3 worldPos; };
        void vert(inout appdata_full v)
        {
            float3 world=mul(unity_ObjectToWorld,v.vertex).xyz;
            float phase=dot(world.xz,float2(.45,.31));
            float wave=sin(_Time.y*1.45+phase)*.65+sin(_Time.y*2.7+phase*1.9)*.35;
            // The grass root stays fixed; tree motion increases towards the outer canopy.
            float weight=_Grass>.5?pow(saturate(v.texcoord.y),1.7):saturate((v.vertex.y-_WindStemHeight)*_WindHeightScale);
            float3 offset=float3(.82,0,.57)*wave*_WindStrength*weight;
            v.vertex.xyz+=mul((float3x3)unity_WorldToObject,offset);
        }
        half4 LightingLeaf(SurfaceOutputStandard s, float3 viewDir, UnityGI gi)
        {
            half4 result=LightingStandard(s,viewDir,gi);
            half back=saturate(-dot(s.Normal,gi.light.dir)*.65+.35);
            half forward=pow(saturate(dot(viewDir,-gi.light.dir)),3)*.6+.4;
            result.rgb+=s.Albedo*gi.light.color*(back*forward*_Translucency)*s.Occlusion;
            return result;
        }
        void LightingLeaf_GI(SurfaceOutputStandard s, UnityGIInput data, inout UnityGI gi)
        {
            LightingStandard_GI(s,data,gi);
        }
        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            if(_MeadowKeep < .999) clip(_MeadowKeep - MeadowDryness(IN.worldPos.xz));
            fixed4 texel=tex2D(_MainTex,IN.uv_MainTex);
            if(_Grass>.5)
            {
                float alpha=0;
                float2 uv=IN.uv_MainTex;
                [unroll] for(int blade=0;blade<6;blade++)
                {
                    float seed=frac(sin(blade*17.13+4.1)*43758.5453);
                    float height=.62+seed*.38;
                    float t=uv.y/height;
                    float root=(blade+.5)/6.0;
                    float curve=(seed-.5)*t*t*.22;
                    float width=.018*(1-saturate(t));
                    float aa=max(.001,fwidth(uv.x));
                    float coverage=saturate((width-abs(uv.x-root-curve))/aa+.5)*step(t,1);
                    alpha=max(alpha,coverage);
                }
                texel.a*=alpha;
            }
            o.Albedo=texel.rgb*_Color.rgb*IN.color.rgb;
            if(_AutumnAmount > .001)
            {
                float variation = MeadowNoise(IN.worldPos.xz * .052 + float2(17.3, 28.1));
                if (_AutumnCrownVariation > .001)
                {
                    // Local canopy clusters keep coherent patches of colour on each tree.
                    float2 crown = IN.worldPos.xz * .18 + IN.worldPos.y * float2(.073,.119);
                    float cluster = MeadowNoise(crown + float2(12.8,5.4));
                    variation = lerp(variation, cluster, _AutumnCrownVariation);
                }
                float leafTone = dot(texel.rgb, float3(.2126, .7152, .0722));
                float3 autumn = lerp(_AutumnGold.rgb, _AutumnCopper.rgb, smoothstep(.32, .76, variation));
                autumn *= lerp(.62, 1.18, saturate(leafTone * 2.2));
                o.Albedo = lerp(o.Albedo, autumn * lerp(1, IN.color.rgb, .26), _AutumnAmount);
            }
            if(_Grass>.5)o.Albedo*=lerp(float3(.51,.65,.39),float3(1.07,1.08,.91),IN.uv_MainTex.y);
            float3 normal=UnpackNormal(tex2D(_BumpMap,IN.uv_MainTex));
            normal.xy*=_BumpStrength;
            normal.z*=(_Grass>.5 || IN.facing>=0)?1:-1;
            o.Normal=normalize(normal);
            o.Smoothness=.13;
            o.Metallic=0;
            o.Occlusion=_Grass>.5?lerp(.58,1,IN.uv_MainTex.y):IN.color.a;
            o.Emission=0;
            o.Alpha=texel.a;
        }
        ENDCG
    }
    Fallback "Transparent/Cutout/Diffuse"
}
