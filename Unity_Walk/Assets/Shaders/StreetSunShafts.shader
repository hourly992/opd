Shader "Hidden/Street/SunShafts"
{
    Properties { _MainTex ("Scene", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex, _CameraDepthNormalsTexture, _ShaftTex;
        float4 _CameraDepthNormalsTexture_TexelSize, _SunPosition, _SunColor;
        float _Strength, _RadialScale;
        float Depth(float2 uv)
        {
            #if UNITY_UV_STARTS_AT_TOP
            if (_CameraDepthNormalsTexture_TexelSize.y < 0) uv.y = 1 - uv.y;
            #endif
            float depth; float3 normal;
            DecodeDepthNormal(tex2D(_CameraDepthNormalsTexture, uv), depth, normal);
            return depth;
        }
        float SunDistance(float2 uv)
        {
            return length((uv - _SunPosition.xy) * float2(_SunPosition.z, 1));
        }
        ENDCG
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment mask
            #pragma target 3.0
            float4 mask(v2f_img i) : SV_Target
            {
                float sky = step(.995, Depth(i.uv));
                float d = SunDistance(i.uv);
                // Only the sky near the solar direction seeds the scattering integral.
                float angular = exp(-d*d*7);
                return float4((sky * angular).xxx, 1);
            }
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment radial
            #pragma target 3.0
            float4 radial(v2f_img i) : SV_Target
            {
                float2 delta = (_SunPosition.xy - i.uv) * (_RadialScale / 24);
                float2 uv = i.uv;
                float sum = 0, weight = 1, total = 0;
                [unroll] for (int n = 0; n < 24; n++)
                {
                    float valid = all(uv >= 0) && all(uv <= 1) ? 1 : 0;
                    sum += tex2D(_MainTex, saturate(uv)).r * weight * valid;
                    total += weight;
                    uv += delta; weight *= .975;
                }
                return float4((sum / total).xxx, 1);
            }
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment composite
            #pragma target 3.0
            float4 composite(v2f_img i) : SV_Target
            {
                float3 scene = tex2D(_MainTex, i.uv).rgb;
                float depth = Depth(i.uv), sky = step(.995, depth);
                float distanceFade = lerp(.18 + .52 * smoothstep(2, 80, depth * _ProjectionParams.z), 1, sky);
                float shaft = tex2D(_ShaftTex, i.uv).r * .32 * distanceFade;
                float d = SunDistance(i.uv);
                float disc = (1 - smoothstep(.004, .006, d)) * 5;
                float corona = exp(-d*d*1100) * .16;
                scene += _SunColor.rgb * _SunPosition.w * _Strength * (shaft + sky * (disc + corona));
                return float4(scene, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
