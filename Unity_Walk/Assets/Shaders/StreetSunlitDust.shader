Shader "Street/SunlitDust"
{
SubShader
    {
        Tags { "RenderPipeline"="HDRenderPipeline" "Queue"="Transparent" "RenderType"="Transparent" }

        Pass
        {
            Name "SunlitDust"
            Tags { "LightMode"="ForwardOnly" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float eyeDepth : TEXCOORD1;
                float4 color : COLOR;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.eyeDepth = -TransformWorldToView(positionWS).z;
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float deviceDepth = LoadCameraDepth(uint2(input.positionCS.xy));
                float opaqueDepth = LinearEyeDepth(deviceDepth, _ZBufferParams);
                float softEdge = saturate((opaqueDepth - input.eyeDepth) / 0.18);
                float radial = saturate(1.0 - dot(input.uv, input.uv));
                return float4(input.color.rgb * 1.35, input.color.a * radial * radial * softEdge);
            }
            ENDHLSL
        }
    }

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
