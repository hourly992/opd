Shader "Street/PaintedBackdrop"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        CGPROGRAM
        #pragma surface surf StreetCloudStandard noforwardadd nolightmap nodynlightmap exclude_path:deferred
        #pragma target 3.0
        #include "StreetCloudLighting.cginc"

        struct Input { float4 color : COLOR; };

        void surf(Input input, inout SurfaceOutputStandard output)
        {
            output.Albedo = input.color.rgb;
            output.Smoothness = .08;
            output.Alpha = 1;
        }
        ENDCG
    }
    Fallback "Diffuse"
}
