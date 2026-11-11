Shader "Street/PaintedBackdrop"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        CGPROGRAM
        #pragma surface surf Standard noforwardadd nolightmap nodynlightmap
        #pragma target 3.0

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
