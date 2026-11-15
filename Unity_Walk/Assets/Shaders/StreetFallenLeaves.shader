Shader "Street/FallenLeaves"
{
    Properties
    {
        _LeafAtlas ("Original autumn leaf silhouettes", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "Queue"="AlphaTest" "RenderType"="TransparentCutout" }
        Cull Off
        AlphaToMask On
        CGPROGRAM
        #pragma surface surf CloudLambert fullforwardshadows vertex:vert exclude_path:deferred
        #pragma target 3.0
        #include "Lighting.cginc"
        #include "StreetAutumnWeather.cginc"
        sampler2D _LeafAtlas;
        struct Input { float4 color : COLOR; float2 leafUV; };
        half4 LightingCloudLambert(SurfaceOutput surface, UnityGI gi)
        {
            return LightingLambert(surface,gi);
        }
        void LightingCloudLambert_GI(SurfaceOutput surface, UnityGIInput data, inout UnityGI gi)
        {
            LightingLambert_GI(surface,data,gi);
            #if defined(UNITY_PASS_FORWARDBASE) && defined(DIRECTIONAL)
            UNITY_BRANCH if (_StreetWeatherEnabled > .5)
                gi.light.color *= StreetWeatherCloudShadow(data.worldPos);
            #endif
        }
        void vert(inout appdata_full vertex, out Input output)
        {
            UNITY_INITIALIZE_OUTPUT(Input, output);
            output.color = vertex.color;
            output.leafUV = vertex.texcoord.xy;
        }
        void surf(Input input, inout SurfaceOutput surface)
        {
            clip(tex2D(_LeafAtlas, input.leafUV).a - .45);
            float2 leaf = float2(frac(input.leafUV.x * 3) - .5, input.leafUV.y - .5);
            float x = abs(leaf.x);
            float stem = 1 - smoothstep(.008, .017 + fwidth(x), x);
            float phase = (leaf.y - x * .73) * 5;
            float branches = 1 - smoothstep(.035, .055 + fwidth(phase), abs(frac(phase + .5) - .5));
            branches *= 1 - smoothstep(.21, .46, x);
            surface.Albedo = input.color.rgb * (.90 - .21 * stem - .12 * branches);
            surface.Alpha = 1;
        }
        ENDCG
    }
    Fallback "Diffuse"
}
