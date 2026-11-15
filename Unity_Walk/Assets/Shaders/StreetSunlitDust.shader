Shader "Street/SunlitDust"
{
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest LEqual
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            sampler2D _CameraDepthNormalsTexture;
            float4 _CameraDepthNormalsTexture_TexelSize;
            struct Vertex { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct Varying
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 screen : TEXCOORD1;
                float eyeDepth : TEXCOORD2;
                fixed4 color : COLOR;
            };
            Varying vert(Vertex input)
            {
                Varying output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.screen = ComputeScreenPos(output.position);
                output.eyeDepth = -UnityObjectToViewPos(input.vertex).z;
                output.uv = input.uv; output.color = input.color;
                return output;
            }
            fixed4 frag(Varying input) : SV_Target
            {
                float2 uv = input.screen.xy / input.screen.w;
                #if UNITY_UV_STARTS_AT_TOP
                if (_CameraDepthNormalsTexture_TexelSize.y < 0) uv.y = 1 - uv.y;
                #endif
                float depth; float3 normal;
                DecodeDepthNormal(tex2D(_CameraDepthNormalsTexture,uv),depth,normal);
                float soft = saturate((depth * _ProjectionParams.z - input.eyeDepth) / .18);
                float radial = saturate(1 - dot(input.uv,input.uv));
                float alpha = input.color.a * radial * radial * soft;
                return fixed4(input.color.rgb,alpha);
            }
            ENDCG
        }
    }
    Fallback Off
}
