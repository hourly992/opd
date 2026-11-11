Shader "Hidden/Street/Presentation"
{
    Properties { _MainTex ("Scene", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex, _BloomTex, _BloomWideTex;
        float4 _MainTex_TexelSize;
        float _Exposure, _Saturation, _Sharpness, _BloomStrength, _AutumnGrade, _LeafGrade, _PaintedDistrictGrade;
        float3 Filmic(float3 color)
        {
            return saturate((color * (2.51 * color + .03)) / (color * (2.43 * color + .59) + .14));
        }
        float3 LeafGrade(float3 color, float luminance)
        {
            const float3 weights = float3(.2126,.7152,.0722);
            float lit = smoothstep(.06,.75,luminance);
            float3 graded = Filmic(color * lerp(float3(.965,1.005,1.085),
                float3(1.064,1.014,.923), lit));
            // Shape midtones in luminance so broad shaded planes keep their material hue.
            float level = dot(graded, weights);
            float contrast = smoothstep(.018,.20,level) * (1 - smoothstep(.62,.96,level));
            float displayLevel = level + (level - .38) * .18 * contrast;
            graded *= displayLevel / max(.001,level);
            graded = lerp(graded, displayLevel.xxx, .08 * smoothstep(.72,.99,displayLevel));
            return graded + (1 - smoothstep(.015,.20,displayLevel)) * float3(.003,.004,.008);
        }
        float3 PaintedGrade(float3 color, float luminance)
        {
            const float3 weights = float3(.2126,.7152,.0722);
            float shadow = 1 - smoothstep(.08,.68,luminance);
            float3 whiteBalance = lerp(float3(1.028,1.006,.970),float3(1.060,1.026,.949),shadow);
            float3 graded = Filmic(color * whiteBalance);
            float level = dot(graded,weights);
            float lift = (1 - smoothstep(.025,.49,level)) * .038;
            graded += lift * float3(1,.88,.67);
            return lerp(graded, dot(graded,weights).xxx, .10 * smoothstep(.78,1,level));
        }
        float3 Filter(float2 uv)
        {
            float2 px=abs(_MainTex_TexelSize.xy);
            float3 value=tex2D(_MainTex,uv).rgb*4;
            value+=tex2D(_MainTex,uv+px*float2(-1,-1)).rgb;
            value+=tex2D(_MainTex,uv+px*float2(1,-1)).rgb;
            value+=tex2D(_MainTex,uv+px*float2(-1,1)).rgb;
            value+=tex2D(_MainTex,uv+px*float2(1,1)).rgb;
            return value*.125;
        }
        float3 Antialiased(float2 uv)
        {
            float2 px=abs(_MainTex_TexelSize.xy);
            const float3 weights=float3(.299,.587,.114);
            float3 center=tex2D(_MainTex,uv).rgb;
            float3 cnw=tex2D(_MainTex,uv+px*float2(-1,-1)).rgb;
            float3 cne=tex2D(_MainTex,uv+px*float2(1,-1)).rgb;
            float3 csw=tex2D(_MainTex,uv+px*float2(-1,1)).rgb;
            float3 cse=tex2D(_MainTex,uv+px*float2(1,1)).rgb;
            float nw=dot(cnw,weights), ne=dot(cne,weights), sw=dot(csw,weights), se=dot(cse,weights);
            float middle=dot(center,weights);
            float low=min(middle,min(min(nw,ne),min(sw,se)));
            float high=max(middle,max(max(nw,ne),max(sw,se)));
            if(high-low<max(.008,high*.12))
            {
                float3 detail=center-(cnw+cne+csw+cse)*.25;
                return max(0,center+clamp(detail,-.08,.08)*_Sharpness);
            }
            float2 edge=float2(-((nw+ne)-(sw+se)),(nw+sw)-(ne+se));
            float reduce=max((nw+ne+sw+se)*.03125,.001);
            edge=clamp(edge/(min(abs(edge.x),abs(edge.y))+reduce),-4,4)*px;
            float3 a=(tex2D(_MainTex,uv-edge/6).rgb+tex2D(_MainTex,uv+edge/6).rgb)*.5;
            float3 b=a*.5+(tex2D(_MainTex,uv-edge*.5).rgb+tex2D(_MainTex,uv+edge*.5).rgb)*.25;
            float luma=dot(b,weights);
            return luma<low||luma>high?a:b;
        }
        ENDCG
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.0
            float4 frag(v2f_img i) : SV_Target
            {
                float3 c=Antialiased(i.uv);
                c+=(tex2D(_BloomTex,i.uv).rgb*.6+tex2D(_BloomWideTex,i.uv).rgb*.4)*_BloomStrength;
                c*=max(.01,_Exposure);
                float luminance=dot(c,float3(.2126,.7152,.0722));
                float3 display;
                UNITY_BRANCH if (_PaintedDistrictGrade > 0)
                    display = PaintedGrade(c, luminance);
                else
                {
                    float3 neutral = Filmic(c * lerp(float3(.975,.995,1.025),
                        float3(1.018,1.003,.982), smoothstep(.1,1.2,luminance)));
                    float3 warm = Filmic(c * lerp(float3(.99,1.012,1.035), float3(1.062,1.013,.944),
                        smoothstep(.04,.72,luminance)));
                    float warmLuma = dot(warm,float3(.2126,.7152,.0722));
                    warm = lerp(warm, warmLuma.xxx, .24 * smoothstep(.64,.98,warmLuma));
                    warm += (1 - smoothstep(.02,.35,warmLuma)) * float3(.018,.019,.022);
                    display = lerp(neutral, warm, _AutumnGrade);
                    UNITY_BRANCH if (_LeafGrade > 0)
                        display = lerp(display, LeafGrade(c, luminance), _LeafGrade);
                }
                c = display;
                float grey=dot(c,float3(.2126,.7152,.0722));
                float chroma=max(c.r,max(c.g,c.b))-min(c.r,min(c.g,c.b));
                c=lerp(grey.xxx,c,_Saturation+(1-saturate(chroma*3))*.04);
                return float4(saturate(c),1);
            }
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment prefilter
            #pragma target 3.0
            float4 prefilter(v2f_img i):SV_Target
            {
                float3 c=max(0,Filter(i.uv));
                float brightness=max(c.r,max(c.g,c.b));
                float knee=clamp(brightness-1.05,0,.6);
                float contribution=max(brightness-1.35,knee*knee/.6);
                return float4(c*contribution/max(.0001,brightness),1);
            }
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment downsample
            #pragma target 3.0
            float4 downsample(v2f_img i):SV_Target { return float4(Filter(i.uv),1); }
            ENDCG
        }
    }
    Fallback Off
}
