Shader "Hidden/Street/Occlusion"
{
    Properties { _MainTex ("Scene", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex, _CameraDepthNormalsTexture, _AOTex;
        float4 _MainTex_TexelSize, _CameraDepthNormalsTexture_TexelSize, _AOTex_TexelSize;
        float4 _AOSettings, _BlurDirection;
        float2 _ProjectionScale;
        float2 DepthUV(float2 uv)
        {
            #if UNITY_UV_STARTS_AT_TOP
            if(_CameraDepthNormalsTexture_TexelSize.y<0) uv.y=1-uv.y;
            #endif
            return uv;
        }
        float DepthNormal(float2 uv, out float3 normal)
        {
            float depth;
            DecodeDepthNormal(tex2D(_CameraDepthNormalsTexture,DepthUV(uv)),depth,normal);
            return depth*_ProjectionParams.z;
        }
        float3 Position(float2 uv, float depth) { return float3((uv*2-1)*_ProjectionScale*depth,-depth); }
        ENDCG
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.0
            float4 frag(v2f_img i):SV_Target
            {
                float3 n; float depth=DepthNormal(i.uv,n);
                if(depth>_ProjectionParams.z*.98) return 1;
                float3 p=Position(i.uv,depth);
                float radius=_AOSettings.x;
                float2 projected=min(float2(.065,.065),radius*.5/max(.1,depth)/_ProjectionScale);
                float sum=0;
                [unroll] for(int k=0;k<16;k++)
                {
                    float angle=k*2.399963, distance=sqrt((k+.5)/16.0);
                    float2 uv=i.uv+float2(cos(angle),sin(angle))*projected*distance;
                    float3 sampleN; float sampleDepth=DepthNormal(uv,sampleN);
                    float3 delta=Position(uv,sampleDepth)-p;
                    float lengthSquared=dot(delta,delta);
                    float horizon=saturate(dot(n,delta)*rsqrt(max(.00001,lengthSquared))-.075);
                    float range=saturate(1-lengthSquared/(radius*radius));
                    float valid=all(uv>=0)&&all(uv<=1)?1:0;
                    sum+=horizon*range*valid;
                }
                float ao=1-min(.78,sum/16.0*_AOSettings.y*3.6)*(1-smoothstep(55,100,depth));
                return float4(ao,ao,ao,1);
            }
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment fragBlur
            #pragma target 3.0
            float4 fragBlur(v2f_img i):SV_Target
            {
                float3 normal; float center=DepthNormal(i.uv,normal);
                float total=0,weights=0;
                [unroll] for(int k=-2;k<=2;k++)
                {
                    float2 uv=i.uv+_BlurDirection.xy*_MainTex_TexelSize.xy*k;
                    float3 n;float depth=DepthNormal(uv,n);
                    float weight=exp(-abs(depth-center)*8)*exp(-k*k*.45)*saturate(dot(n,normal));
                    total+=tex2D(_MainTex,uv).r*weight;weights+=weight;
                }
                return total/max(.0001,weights);
            }
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment fragComposite
            #pragma target 3.0
            float4 fragComposite(v2f_img i):SV_Target
            {
                float4 c=tex2D(_MainTex,i.uv);
                float3 normal;float center=DepthNormal(i.uv,normal);
                float total=0, weights=0;
                // Bilateral upsampling keeps half-resolution AO off foreground silhouettes and the sky.
                [unroll] for(int k=0;k<5;k++)
                {
                    float2 offset=k==0?float2(0,0):k==1?float2(1,0):k==2?float2(-1,0):k==3?float2(0,1):float2(0,-1);
                    float2 uv=i.uv+offset*abs(_AOTex_TexelSize.xy);
                    float3 n;float depth=DepthNormal(uv,n);
                    float weight=exp(-abs(depth-center)*12)*pow(saturate(dot(normal,n)),4)+.00001;
                    total+=tex2D(_AOTex,uv).r*weight;weights+=weight;
                }
                float ao=lerp(1,total/max(.0001,weights),_AOSettings.z);
                c.rgb*=lerp(float3(.94,.97,1),float3(1,1,1),ao)*ao;
                return c;
            }
            ENDCG
        }
        // Copy depth-normals to a native half-resolution target for the DXR compute pass.
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment fragPack
            #pragma target 3.0
            float4 fragPack(v2f_img i) : SV_Target
            {
                return tex2D(_CameraDepthNormalsTexture, DepthUV(i.uv));
            }
            ENDCG
        }
    }
    Fallback Off
}
