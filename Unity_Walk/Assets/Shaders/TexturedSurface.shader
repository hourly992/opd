Shader "Street/TexturedSurface"
{
    Properties
    {
        _MainTex ("Albedo", 2D) = "white" {}
        [Normal] _BumpMap ("Normal", 2D) = "bump" {}
        _MaskMap ("AO / roughness / height", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _TileMeters ("Metres per tile XY", Vector) = (3,3,0,0)
        _Saturation ("Albedo saturation", Range(0,1)) = 1
        _AlbedoContrast ("Photographic colour variation", Range(0,1)) = 1
        _AlbedoMean ("Linear photographic albedo average", Vector) = (.3,.3,.3,0)
        _OcclusionStrength ("Material AO strength", Range(0,1)) = .65
        _BumpStrength ("Normal strength", Range(0,2)) = .7
        _Parallax ("Relief in metres", Range(0,.04)) = .008
        _Metallic ("Metallic", Range(0,1)) = 0
        _RoughnessScale ("Roughness scale", Range(0,2)) = 1
        _RoughnessBias ("Roughness bias", Range(-1,1)) = 0
        _Weathering ("Weathering", Range(0,1)) = .25
        _Colorize ("Recolour using tint", Range(0,1)) = 0
        _DryGroundTex ("Exposed soil and aggregate", 2D) = "gray" {}
        _MeadowDrying ("Patchy exposed soil", Range(0,1)) = 0
        _MeadowSoilRange ("Soil brightness and exposure range", Vector) = (.078,.20,.36,.66)
        _PlinthPanels ("Panel width, height, joint width and amount", Vector) = (0,0,0,0)
        _PaverPattern ("Pavers: 0 basket / 1 running bond", Float) = 0
        _PaverGreyFraction ("Grey paver fraction", Range(0,1)) = .23
        _PaverJointMeters ("Paver joint width in metres", Float) = .005
        _PaverRedTone ("Paver red in linear colour", Vector) = (.14,.052,.036,0)
        _PaverGreyTone ("Paver grey in linear colour", Vector) = (.12,.123,.11,0)
        _PaverFarTone ("Paver distant tone in linear colour", Vector) = (.13,.072,.052,0)
        _PaverFinish ("Paver relief, variation, joint darkening", Vector) = (.0025,1,.75,0)
        _PaintedVariation ("Broad painted variation", Range(0,1)) = 0
        _SurfaceClass ("0 regular / 1 ground / 2 masonry / 3 roof / 4 road / 5 road paint", Float) = 0
        _UseUV ("Use source UVs", Float) = 0
        _UVRotation ("Rotate grain 90 degrees", Float) = 0
        _EmissionColor ("Emission", Color) = (0,0,0,1)
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Face culling", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull [_Cull]
        LOD 300
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows addshadow
        #pragma target 3.0
        #pragma multi_compile_instancing
        #include "StreetMeadow.cginc"
        sampler2D _MainTex, _BumpMap, _MaskMap, _DryGroundTex;
        fixed4 _Color, _EmissionColor;
        float4 _TileMeters, _AlbedoMean;
        float _BumpStrength, _Saturation, _Parallax, _Metallic, _RoughnessScale, _RoughnessBias;
        float _Weathering, _Colorize, _SurfaceClass, _UseUV, _UVRotation;
        float _AlbedoContrast, _OcclusionStrength;
        float _PaverPattern, _PaverGreyFraction, _PaverJointMeters;
        float4 _PaverRedTone, _PaverGreyTone, _PaverFarTone, _PaverFinish;
        float _PaintedVariation;
        float _MeadowDrying;
        float4 _MeadowSoilRange, _PlinthPanels;
        struct Input { float3 worldPos; float3 worldNormal; float2 uv_MainTex; INTERNAL_DATA };
        float hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
        float noise(float2 p)
        {
            float2 i=floor(p), f=frac(p); f=f*f*(3-2*f);
            return lerp(lerp(hash(i),hash(i+float2(1,0)),f.x),lerp(hash(i+float2(0,1)),hash(i+1),f.x),f.y);
        }
        float3 ReliefNormal(float3 p, float3 n, float height)
        {
            float3 dx=ddx(p), dy=ddy(p), r1=cross(dy,n), r2=cross(n,dx);
            float det=dot(dx,r1);
            return normalize(abs(det)*n-sign(det)*(ddx(height)*r1+ddy(height)*r2));
        }
        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float3 n = normalize(WorldNormalVector(IN,float3(0,0,1)));
            float3 p = IN.worldPos;
            // Continuous facade coordinates also on rotated walls and sloping roofs.
            // Tangent/bitangent are orthonormal; the former dominant-axis projection stretched bays.
            float3 t = abs(n.y)>.95 ? float3(1,0,0) : normalize(cross(float3(0,1,0),n));
            float3 b = normalize(cross(n,t));
            float2 metres=max(float2(.05,.05),_TileMeters.xy);
            float2 uv=float2(dot(p,t),dot(p,b))/metres;
            float3 view=normalize(_WorldSpaceCameraPos-p);
            float2 viewUV=float2(dot(view,t),dot(view,b));
            if(_UVRotation>.5) { uv=float2(uv.y,-uv.x); viewUV=float2(viewUV.y,-viewUV.x); }
            float height=tex2D(_MaskMap,uv).b-.5;
            uv-=viewUV/max(.3,abs(dot(view,n)))*height*_Parallax/metres;
            if(_UseUV>.5) uv=IN.uv_MainTex;
            float3 map = UnpackNormal(tex2D(_BumpMap,uv));
            // Old high-contrast normals retained a low Z after scaling XY, keeping brick edges puffy.
            // A linear blend from the flat normal gives masonry the requested shallow relief.
            if(_SurfaceClass>1.5&&_SurfaceClass<2.5)map=normalize(lerp(float3(0,0,1),map,_BumpStrength));
            else map.xy *= _BumpStrength;
            if(_UVRotation>.5) map.xy=float2(-map.y,map.x);
            float3 world = normalize(t*map.x+b*map.y+n*map.z);
            float3 tangent=normalize(WorldNormalVector(IN,float3(1,0,0)));
            float3 bitangent=normalize(WorldNormalVector(IN,float3(0,1,0)));
            o.Normal=_UseUV>.5?normalize(map):normalize(float3(dot(world,tangent),dot(world,bitangent),dot(world,n)));
            float3 albedo=tex2D(_MainTex,uv).rgb;
            albedo=max(0,lerp(_AlbedoMean.rgb,albedo,_AlbedoContrast));
            float3 mask=tex2D(_MaskMap,uv).rgb;
            if(_SurfaceClass>.5&&_SurfaceClass<1.5)
            {
                // Cross-scale, rotated sample breaks the lawn's repeated 2m stamp.
                float2 uv2=float2(-uv.y,uv.x)*.731+float2(.317,.851);
                float blend=smoothstep(.25,.75,noise(p.xz*.065));
                albedo=lerp(albedo,tex2D(_MainTex,uv2).rgb,blend*.62);
                albedo*=lerp(.78,1.10,noise(p.xz*.037)+noise(p.xz*.31)*.15);
                mask=lerp(mask,tex2D(_MaskMap,uv2).rgb,blend*.62);
            }
            if(_SurfaceClass>3.5&&_SurfaceClass<4.5)
            {
                float2 uv2=float2(-uv.y,uv.x)*.83+float2(.73,.21);
                float blend=smoothstep(.22,.78,noise(p.xz*.19+3));
                albedo=lerp(albedo,tex2D(_MainTex,uv2).rgb,blend*.72);
                mask=lerp(mask,tex2D(_MaskMap,uv2).rgb,blend*.72);
                albedo*=lerp(.9,1.05,noise(p.xz*.061));
            }
            if(_SurfaceClass>4.5&&_SurfaceClass<5.5)
            {
                // A worn marking still follows the original lane geometry and road level.
                float wear=noise(p.xz*125)*.65+noise(p.xz*39)*.35;
                clip(wear-.17);
                albedo*=lerp(.61,1,smoothstep(.22,.65,wear));
            }
            if(_SurfaceClass>5.5)
            {
                // 200 x 100 mm concrete pavers. The photographed path uses rectangular red/grey blocks,
                // not the former repeated hexagonal paving texture. All dimensions are in metres.
                float2 q=float2(dot(p.xz,float2(.881,.474)),dot(p.xz,float2(.474,-.881)))/.1;
                float2 cell=floor(q), local=frac(q), origin=cell;
                float k=fmod(fmod(cell.x+cell.y,4)+4,4);
                float2 dimensions;
                if(k<2) { local.x+=k; origin.x-=k; dimensions=float2(2,1); }
                else { local.y+=k-2; origin.y-=k-2; dimensions=float2(1,2); }
                if(_PaverPattern>.5)
                {
                    float row=floor(q.y);
                    float2 staggered=float2(q.x+fmod(fmod(row,2)+2,2),q.y);
                    origin=float2(floor(staggered.x*.5)*2,row);
                    local=float2(frac(staggered.x*.5)*2,frac(staggered.y));
                    dimensions=float2(2,1);
                }
                float edge=min(min(local.x,dimensions.x-local.x),min(local.y,dimensions.y-local.y));
                float aa=max(.003,max(fwidth(q).x,fwidth(q).y));
                float visible=1-saturate(aa*.9);
                float jointHalf=max(.001,_PaverJointMeters)*5;
                float joint=(1-smoothstep(jointHalf-aa,jointHalf+aa,edge))*visible;
                float seed=hash(origin);
                float3 brick=lerp(_PaverRedTone.rgb,_PaverGreyTone.rgb,step(1-_PaverGreyFraction,seed));
                brick*=1+lerp(-.14,.13,hash(origin+31))*_PaverFinish.y;
                float grain=lerp(.86,1.12,saturate(dot(albedo,float3(.2126,.7152,.0722))*3));
                albedo=lerp(_PaverFarTone.rgb,brick,visible)*grain;
                albedo=lerp(albedo,float3(.05,.045,.035),joint*_PaverFinish.z);
                float3 raised=ReliefNormal(p,n,(1-joint)*_PaverFinish.x);
                float3 delta=raised-n;
                o.Normal=normalize(o.Normal+float3(dot(delta,tangent),dot(delta,bitangent),dot(delta,n)));
                mask.r=lerp(.96,.70,joint); mask.g=.86;
            }
            float grey=dot(albedo,float3(.2126,.7152,.0722));
            albedo=lerp(grey.xxx,albedo,_Saturation)*_Color.rgb;
            albedo=lerp(albedo,_Color.rgb*lerp(.74,1.16,saturate(grey*3)),_Colorize);
            float stains=noise(float2(dot(p,t)*3.4,p.y*.19));
            float baseDirt=(1-smoothstep(.08,1.25,p.y))*stains;
            albedo*=1-_Weathering*(baseDirt*.34+(1-mask.r)*.12);
            if(_PaintedVariation>.001)
            {
                float2 brush = abs(n.y) > .72 ? p.xz : float2(dot(p,t),p.y);
                float broad = noise(brush * float2(.61,.29));
                float stroke = noise(brush * float2(2.4,.78) + 4.71);
                albedo *= 1 + (broad * .73 + stroke * .27 - .5) * _PaintedVariation;
            }
            if(_MeadowDrying > .001)
            {
                float dry = smoothstep(_MeadowSoilRange.z, _MeadowSoilRange.w, MeadowDryness(p.xz)) * _MeadowDrying;
                float grain = dot(tex2D(_DryGroundTex, p.xz * .91).rgb, float3(.2126, .7152, .0722));
                float3 soil = lerp(_MeadowSoilRange.x, _MeadowSoilRange.y, saturate(grain * 1.8)) * float3(1, .90, .73);
                albedo = lerp(albedo, soil, dry);
                o.Normal = normalize(lerp(o.Normal, float3(0, 0, 1), dry * .6));
                mask.g = lerp(mask.g, .94, dry);
            }
            if(_PlinthPanels.w > .001 && abs(n.y) < .35)
            {
                float2 panelSize = max(_PlinthPanels.xy, .05);
                float2 panelUV = float2(dot(p, t), p.y) / panelSize;
                float2 within = frac(panelUV);
                float2 distanceToJoint = min(within, 1 - within) * panelSize;
                float2 pixelWidth = max(fwidth(panelUV) * panelSize, .0005);
                float2 joint = 1 - smoothstep(_PlinthPanels.z * .5 - pixelWidth,
                                               _PlinthPanels.z * .5 + pixelWidth, distanceToJoint);
                float visibility = 1 - saturate(max(fwidth(panelUV.x), fwidth(panelUV.y)));
                float seam = max(joint.x, joint.y) * visibility * _PlinthPanels.w;
                albedo *= lerp(.96, 1.04, hash(floor(panelUV))) * lerp(1, .39, seam);
                mask.g = lerp(mask.g, .96, seam);
            }
            o.Albedo=albedo;
            o.Smoothness=1-clamp(mask.g*_RoughnessScale+_RoughnessBias,.08,.98);
            if(_SurfaceClass>4.5&&_SurfaceClass<5.5)o.Smoothness=.12;
            o.Occlusion=lerp(1,mask.r,_OcclusionStrength);
            o.Metallic=_Metallic;
            o.Emission=_EmissionColor.rgb;
            o.Alpha=1;
        }
        ENDCG
    }
    Fallback "Standard"
}
