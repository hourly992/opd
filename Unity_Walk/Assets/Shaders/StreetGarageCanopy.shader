Shader "Street/GarageCanopy"
{
    Properties
    {
        _Color ("Painted translucent polycarbonate", Color) = (.62,.69,.64,.24)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Cull Off
        ZWrite Off
        CGPROGRAM
        #pragma surface surf Standard alpha:fade keepalpha
        #pragma target 3.0
        #pragma multi_compile_instancing
        fixed4 _Color;
        struct Input { float3 worldPos; };
        void surf(Input input, inout SurfaceOutputStandard output)
        {
            output.Albedo = _Color.rgb;
            output.Metallic = .05;
            output.Smoothness = .34;
            output.Alpha = _Color.a;
        }
        ENDCG
    }
    Fallback Off
}
