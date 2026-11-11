Shader "Street/Surface"
{
    Properties
    {
        _Color ("Color", Color) = (.6,.6,.6,1)
        _Kind ("0 plain / 1 brick / 2 asphalt / 3 paving / 4 ground / 5 wood / 6 concrete / 7 bark / 8 foliage / 9 glass", Float) = 0
        _Smoothness ("Smoothness", Range(0,1)) = .15
        _Metallic ("Metallic", Range(0,1)) = 0
        _VertexTint ("Use vertex tint", Range(0,1)) = 0
        _EmissionColor ("Emission", Color) = (0,0,0,1)
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Face culling", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull [_Cull]
        LOD 300
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0
        #pragma multi_compile_instancing
        fixed4 _Color, _EmissionColor;
        half _Kind, _Smoothness, _Metallic, _VertexTint;
        struct Input { float3 worldPos; float3 worldNormal; float4 color : COLOR; INTERNAL_DATA };
        float hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
        float noise(float2 p)
        {
            float2 i=floor(p), f=frac(p); f=f*f*(3-2*f);
            return lerp(lerp(hash(i),hash(i+float2(1,0)),f.x),lerp(hash(i+float2(0,1)),hash(i+1),f.x),f.y);
        }
        float detailNoise(float2 p)
        {
            return lerp(.5, noise(p), 1-saturate(length(fwidth(p))*.7));
        }
        float3 Perturb(float3 p, float3 n, float height)
        {
            float3 dx=ddx(p), dy=ddy(p);
            float3 r1=cross(dy,n), r2=cross(n,dx);
            float determinant=dot(dx,r1);
            float3 gradient=sign(determinant)*(ddx(height)*r1+ddy(height)*r2);
            return normalize(abs(determinant)*n-gradient);
        }
        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float3 p=IN.worldPos;
            float3 normal=normalize(WorldNormalVector(IN,float3(0,0,1)));
            float3 n=abs(normal);
            float2 uv=n.y>.65?p.xz:(n.x>n.z?p.zy:p.xy);
            float3 albedo=_Color.rgb;
            float height=0, occlusion=1, roughness=1-_Smoothness;
            float macro=noise(uv*.19)*.6+noise(uv*.71)*.4;
            if (_Kind>.5 && _Kind<1.5)
            {
                float row=floor(uv.y/.077);
                float2 q=float2(uv.x/.255+fmod(row,2)*.5,uv.y/.077);
                float2 f=frac(q), aa=max(fwidth(q),.004);
                float mortar=max(1-smoothstep(.028-aa.x,.028+aa.x,min(f.x,1-f.x)),
                    1-smoothstep(.045-aa.y,.045+aa.y,min(f.y,1-f.y)));
                float visible=1-saturate(max(aa.x,aa.y)*1.3);
                mortar*=visible;
                float brick=lerp(1,.83+.28*hash(floor(q)),visible);
                albedo*=brick*(.88+.20*macro);
                albedo=lerp(albedo,float3(.255,.247,.222),mortar*.72);
                height=(1-mortar)*.0025+(detailNoise(uv*95)-.5)*.0007;
                occlusion=1-mortar*.12;
                roughness=.89;
            }
            else if (_Kind>1.5 && _Kind<2.5)
            {
                float grain=detailNoise(uv*110);
                float patches=noise(uv*.31)*.65+noise(uv*1.7)*.35;
                albedo*=(.78+patches*.33)*(.88+grain*.23);
                height=(grain-.5)*.0016;
                roughness=.91;
            }
            else if (_Kind>2.5 && _Kind<3.5)
            {
                float2 q=uv/float2(.20,.10);
                q.x+=fmod(floor(q.y),2)*.5;
                float2 f=frac(q), aa=max(fwidth(q),.003);
                float edge=min(min(f.x,1-f.x),min(f.y,1-f.y));
                float mortar=(1-smoothstep(.027-max(aa.x,aa.y),.027+max(aa.x,aa.y),edge))*(1-saturate(max(aa.x,aa.y)*1.5));
                albedo*=lerp(1,.86+.25*hash(floor(q)),1-saturate(max(aa.x,aa.y)))*(.90+.15*macro);
                albedo=lerp(albedo,albedo*.58,mortar*.65);
                height=(1-mortar)*.003+detailNoise(uv*140)*.0008;
                roughness=.84;
            }
            else if (_Kind>3.5 && _Kind<4.5)
            {
                float ground=noise(uv*1.7)*.65+noise(uv*5.7)*.35;
                float dry=smoothstep(.38,.72,noise(uv*.8+17));
                albedo=lerp(_Color.rgb,_Color.rgb*float3(1.15,.92,.64),dry*.55)*(.86+ground*.23);
                height=noise(uv*9)*.012+detailNoise(uv*80)*.002;
                roughness=.98;
            }
            else if (_Kind>4.5 && _Kind<5.5)
            {
                float grain=noise(float2(uv.x*1.6,uv.y*135));
                albedo*=(.76+grain*.27)*(.87+.18*macro);
                height=grain*.0006; roughness=.74;
            }
            else if (_Kind>5.5 && _Kind<6.5)
            {
                float aggregate=detailNoise(uv*65);
                albedo*=(.83+.22*aggregate)*(.88+.18*macro);
                height=aggregate*.0014; roughness=.90;
            }
            else if (_Kind>6.5 && _Kind<7.5)
            {
                float marks=smoothstep(.63,.77,noise(float2(uv.x*19,uv.y*35)))*smoothstep(.46,.65,noise(uv*9));
                albedo*=lerp(.83+noise(uv*4)*.17,.21,marks);
                height=marks*.0009; roughness=.88;
            }
            else if (_Kind>7.5 && _Kind<8.5) { roughness=.82; }
            else if (_Kind>8.5) { albedo*=.82+.18*noise(float2(uv.x*2,.03*uv.y)); roughness=.12; }

            // Dirt and dampness where masonry and paving meet, not uniform clean toy surfaces.
            if (_Kind<7.5 && _Kind>.5)
                albedo*=1-(1-smoothstep(.08,1.1,p.y))*.15*noise(uv*3.1);
            float3 perturbed=Perturb(p,normal,height);
            float3 tangent=normalize(WorldNormalVector(IN,float3(1,0,0)));
            float3 bitangent=normalize(WorldNormalVector(IN,float3(0,1,0)));
            o.Normal=normalize(float3(dot(perturbed,tangent),dot(perturbed,bitangent),dot(perturbed,normal)));
            o.Albedo=albedo*lerp(float3(1,1,1),IN.color.rgb,_VertexTint);
            o.Metallic=_Metallic;
            o.Smoothness=1-roughness;
            o.Occlusion=occlusion;
            o.Emission=_EmissionColor.rgb;
            o.Alpha=1;
        }
        ENDCG
    }
    FallBack "Standard"
}
