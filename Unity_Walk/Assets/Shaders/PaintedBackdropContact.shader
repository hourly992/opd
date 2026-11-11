Shader "Street/PaintedBackdropContact"
{
    SubShader
    {
        Tags { "Queue" = "Transparent-4" "RenderType" = "Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ColorMask RGB

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            struct Input { float4 vertex : POSITION; fixed4 color : COLOR; };
            struct Interpolators
            {
                float4 position : SV_POSITION;
                fixed4 color : COLOR;
                UNITY_FOG_COORDS(0)
            };

            Interpolators vert(Input input)
            {
                Interpolators output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.color = input.color;
                UNITY_TRANSFER_FOG(output, output.position);
                return output;
            }

            fixed4 frag(Interpolators input) : SV_Target
            {
                fixed4 color = input.color;
                UNITY_APPLY_FOG(input.fogCoord, color);
                return color;
            }
            ENDCG
        }
    }
}
