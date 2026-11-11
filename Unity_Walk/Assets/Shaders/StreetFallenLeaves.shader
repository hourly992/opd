Shader "Street/FallenLeaves"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull Off
        CGPROGRAM
        #pragma surface surf Lambert fullforwardshadows vertex:vert exclude_path:deferred
        #pragma target 3.0
        struct Input { float4 color : COLOR; float2 leafUV; };
        void vert(inout appdata_full vertex, out Input output)
        {
            UNITY_INITIALIZE_OUTPUT(Input, output);
            output.color = vertex.color;
            output.leafUV = vertex.texcoord.xy;
        }
        void surf(Input input, inout SurfaceOutput surface)
        {
            float x = abs(input.leafUV.x);
            float stem = 1 - smoothstep(.008, .017 + fwidth(x), x);
            float phase = (input.leafUV.y - x * .73) * 5;
            float branches = 1 - smoothstep(.035, .055 + fwidth(phase), abs(frac(phase + .5) - .5));
            branches *= 1 - smoothstep(.21, .46, x);
            surface.Albedo = input.color.rgb * (.90 - .21 * stem - .12 * branches);
            surface.Alpha = 1;
        }
        ENDCG
    }
    Fallback "Diffuse"
}
