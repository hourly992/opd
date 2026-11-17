//-------------------------------------------------------------------------------------
// Defines
//-------------------------------------------------------------------------------------

// Use surface gradient normal mapping as it handle correctly triplanar normal mapping and multiple UVSet
// Not used in rtx as it requires derivatives
#ifndef SHADER_STAGE_RAY_TRACING
#define SURFACE_GRADIENT
#endif

//-------------------------------------------------------------------------------------
// Fill SurfaceData/Builtin data function
//-------------------------------------------------------------------------------------
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Sampling/SampleUVMapping.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/MaterialUtilities.hlsl"
#if !defined(SHADER_STAGE_RAY_TRACING) || defined (PATH_TRACING_CLUSTERED_DECALS)
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Decal/DecalUtilities.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/LitDecalData.hlsl"
#endif
#ifndef SURFACE_GRADIENT
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/NormalSurfaceGradient.hlsl"
#endif

//#define PROJECTED_SPACE_NDF_FILTERING

// Struct that gather UVMapping info of all layers + common calculation
// This is use to abstract the mapping that can differ on layers
struct LayerTexCoord
{
#ifndef LAYERED_LIT_SHADER
    UVMapping base;
    UVMapping details;
#else
    // Regular texcoord
    UVMapping base0;
    UVMapping base1;
    UVMapping base2;
    UVMapping base3;

    UVMapping details0;
    UVMapping details1;
    UVMapping details2;
    UVMapping details3;

    // Dedicated for blend mask
    UVMapping blendMask;
#endif

    // Store information that will be share by all UVMapping
    float3 vertexNormalWS; // TODO: store also object normal map for object triplanar
    float3 triplanarWeights;

#ifdef SURFACE_GRADIENT
    // tangent basis for each UVSet - up to 4 for now
    float3 vertexTangentWS0, vertexBitangentWS0;
    float3 vertexTangentWS1, vertexBitangentWS1;
    float3 vertexTangentWS2, vertexBitangentWS2;
    float3 vertexTangentWS3, vertexBitangentWS3;
#endif
};

#ifdef SURFACE_GRADIENT
void GenerateLayerTexCoordBasisTB(FragInputs input, inout LayerTexCoord layerTexCoord)
{
    float3 vertexNormalWS = input.tangentToWorld[2];

    layerTexCoord.vertexTangentWS0 = input.tangentToWorld[0];
    layerTexCoord.vertexBitangentWS0 = input.tangentToWorld[1];

    float3 dPdx = ddx_fine(input.positionRWS);
    float3 dPdy = ddy_fine(input.positionRWS);

    float3 sigmaX = dPdx - dot(dPdx, vertexNormalWS) * vertexNormalWS;
    float3 sigmaY = dPdy - dot(dPdy, vertexNormalWS) * vertexNormalWS;
    //float flipSign = dot(sigmaY, cross(vertexNormalWS, sigmaX) ) ? -1.0 : 1.0;
    float flipSign = dot(dPdy, cross(vertexNormalWS, dPdx)) < 0.0 ? -1.0 : 1.0; // gives same as the commented out line above

    // TODO: Optimize! The compiler will not be able to remove the tangent space that are not use because it can't know due to our UVMapping constant we use for both base and details
    // To solve this we should track which UVSet is use for normal mapping... Maybe not as simple as it sounds
    SurfaceGradientGenBasisTB(vertexNormalWS, sigmaX, sigmaY, flipSign, input.texCoord1.xy, layerTexCoord.vertexTangentWS1, layerTexCoord.vertexBitangentWS1);
    #if defined(_REQUIRE_UV2) || defined(_REQUIRE_UV3)
    SurfaceGradientGenBasisTB(vertexNormalWS, sigmaX, sigmaY, flipSign, input.texCoord2.xy, layerTexCoord.vertexTangentWS2, layerTexCoord.vertexBitangentWS2);
    #endif
    #if defined(_REQUIRE_UV3)
    SurfaceGradientGenBasisTB(vertexNormalWS, sigmaX, sigmaY, flipSign, input.texCoord3.xy, layerTexCoord.vertexTangentWS3, layerTexCoord.vertexBitangentWS3);
    #endif
}
#endif

#ifndef LAYERED_LIT_SHADER

// Want to use only one sampler for normalmap/bentnormalmap either we use OS or TS. And either we have normal map or bent normal or both.
#ifdef _NORMALMAP_TANGENT_SPACE
    #if defined(_NORMALMAP)
    #define SAMPLER_NORMALMAP_IDX sampler_NormalMap
    #elif defined(_BENTNORMALMAP)
    #define SAMPLER_NORMALMAP_IDX sampler_BentNormalMap
    #endif
#else
    #if defined(_NORMALMAP)
    #define SAMPLER_NORMALMAP_IDX sampler_NormalMapOS
    #elif defined(_BENTNORMALMAP)
    #define SAMPLER_NORMALMAP_IDX sampler_BentNormalMapOS
    #endif
#endif

#define SAMPLER_DETAILMAP_IDX sampler_DetailMap
#define SAMPLER_MASKMAP_IDX sampler_MaskMap
#define SAMPLER_HEIGHTMAP_IDX sampler_HeightMap

#define SAMPLER_SUBSURFACE_MASK_MAP_IDX sampler_SubsurfaceMaskMap
#define SAMPLER_TRANSMISSION_MASK_MAP_IDX sampler_TransmissionMaskMap
#define SAMPLER_THICKNESSMAP_IDX sampler_ThicknessMap

// include LitDataIndividualLayer to define GetSurfaceData
#define LAYER_INDEX 0
#define ADD_IDX(Name) Name
#define ADD_ZERO_IDX(Name) Name
#ifdef _NORMALMAP
#define _NORMALMAP_IDX
#endif
#ifdef _NORMALMAP_TANGENT_SPACE
#define _NORMALMAP_TANGENT_SPACE_IDX
#endif
#ifdef _DETAIL_MAP
#define _DETAIL_MAP_IDX
#endif
#ifdef _SUBSURFACE_MASK_MAP
#define _SUBSURFACE_MASK_MAP_IDX
#endif
#ifdef _TRANSMISSION_MASK_MAP
#define _TRANSMISSION_MASK_MAP_IDX
#endif
#ifdef _THICKNESSMAP
#define _THICKNESSMAP_IDX
#endif
#ifdef _MASKMAP
#define _MASKMAP_IDX
#endif
#ifdef _BENTNORMALMAP
#define _BENTNORMALMAP_IDX
#endif
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/LitDataIndividualLayer.hlsl"

// This maybe call directly by tessellation (domain) shader, thus all part regarding surface gradient must be done
// in function with FragInputs input as parameters
// layerTexCoord must have been initialize to 0 outside of this function
void GetLayerTexCoord(float2 texCoord0, float2 texCoord1, float2 texCoord2, float2 texCoord3,
                      float3 positionRWS, float3 vertexNormalWS, inout LayerTexCoord layerTexCoord)
{
    layerTexCoord.vertexNormalWS = vertexNormalWS;
    float objectSpaceMapping = false;
    float3 normalToComputeWeights = layerTexCoord.vertexNormalWS;
#ifndef LAYERED_LIT_SHADER
    objectSpaceMapping = _ObjectSpaceUVMapping;
    if (objectSpaceMapping)
    {
        normalToComputeWeights = TransformWorldToObjectNormal(normalToComputeWeights);
    }
#endif

    layerTexCoord.triplanarWeights = ComputeTriplanarWeights(normalToComputeWeights);

    int mappingType = UV_MAPPING_UVSET;
#if defined(_MAPPING_PLANAR)
    mappingType = UV_MAPPING_PLANAR;
#elif defined(_MAPPING_TRIPLANAR)
    mappingType = UV_MAPPING_TRIPLANAR;
#endif


    // Be sure that the compiler is aware that we don't use UV1 to UV3 for main layer so it can optimize code
    ComputeLayerTexCoord(   texCoord0, texCoord1, texCoord2, texCoord3, _UVMappingMask, _UVDetailsMappingMask,
                            _BaseColorMap_ST.xy, _BaseColorMap_ST.zw, _DetailMap_ST.xy, _DetailMap_ST.zw, 1.0, _LinkDetailsWithBase,
                            positionRWS, _TexWorldScale,
                            mappingType, objectSpaceMapping, layerTexCoord);
}

// This is call only in this file
// layerTexCoord must have been initialize to 0 outside of this function
void GetLayerTexCoord(FragInputs input, inout LayerTexCoord layerTexCoord)
{
#ifdef SURFACE_GRADIENT
    GenerateLayerTexCoordBasisTB(input, layerTexCoord);
#endif

    GetLayerTexCoord(   input.texCoord0.xy, input.texCoord1.xy, input.texCoord2.xy, input.texCoord3.xy,
                        input.positionRWS, input.tangentToWorld[2].xyz, layerTexCoord);
}

#if !defined(SHADER_STAGE_RAY_TRACING)
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/LitDataDisplacement.hlsl"
#endif

#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/LitBuiltinData.hlsl"

#include "Assets/Shaders/StreetMeadow.cginc"

#if defined(SHADER_STAGE_RAY_TRACING)
#define STREET_SAMPLE(tex, uv) SAMPLE_TEXTURE2D_LOD(tex, sampler##tex, uv, 0)
#else
#define STREET_SAMPLE(tex, uv) SAMPLE_TEXTURE2D(tex, sampler##tex, uv)
#endif

struct StreetInput
{
    float3 worldPos;
    float3 worldNormal;
    float3 tangent;
    float3 bitangent;
    float3 viewDir;
    float2 uv_MainTex;
    float2 paneUV;
    float2 leafUV;
    float variant;
    float4 color;
    float facing;
};

struct StreetSurfaceSample
{
    float3 Albedo;
    float3 Normal;
    float3 Emission;
    float Smoothness;
    float Occlusion;
    float Metallic;
    float Alpha;
    float Visibility;
};

float3 StreetWorldNormal(StreetInput input, float3 normal)
{
    return normal.x * input.tangent + normal.y * input.bitangent + normal.z * input.worldNormal;
}

float3 StreetNormalMap(float4 packedNormal)
{
    return UnpackNormal(packedNormal);
}

StreetInput StreetFragment(FragInputs input, float3 view)
{
    StreetInput street;
    street.worldPos = GetAbsolutePositionWS(input.positionRWS);
    street.worldNormal = SafeNormalize(input.tangentToWorld[2]);
    float3 reference = abs(street.worldNormal.y) > .9 ? float3(0, 0, 1) : float3(0, 1, 0);
    float3 fallbackTangent = SafeNormalize(cross(reference, street.worldNormal));
    float3 tangent = input.tangentToWorld[0];
    float3 bitangent = input.tangentToWorld[1];
    street.tangent = dot(tangent, tangent) > .01 ? SafeNormalize(tangent) : fallbackTangent;
    street.bitangent = dot(bitangent, bitangent) > .01 ? SafeNormalize(bitangent)
        : SafeNormalize(cross(street.worldNormal, street.tangent));
    street.viewDir = float3(dot(view, street.tangent), dot(view, street.bitangent), dot(view, street.worldNormal));
    street.uv_MainTex = input.texCoord0.xy * _MainTex_ST.xy + _MainTex_ST.zw;
    street.paneUV = input.texCoord0.xy;
    street.leafUV = input.texCoord0.xy;
    // Adjacent panes retain different atlas selections even on meshes with two UV channels.
    float3 approximateOrigin = street.worldPos - street.tangent * street.paneUV.x
                                                 - street.bitangent * street.paneUV.y;
    street.variant = frac(dot(floor(approximateOrigin * .46), float3(.173, .317, .427)));
    street.color = input.color;
    street.facing = 1;
    return street;
}

#if defined(SHADER_STAGE_RAY_TRACING)
float StreetWidth(float v) { return .003; }
float2 StreetWidth(float2 v) { return .003; }
#else
float StreetWidth(float v) { return fwidth(v); }
float2 StreetWidth(float2 v) { return fwidth(v); }
#endif
float StreetHash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
        float StreetNoise(float2 p)
        {
            float2 i=floor(p), f=frac(p); f=f*f*(3-2*f);
            return lerp(lerp(StreetHash(i),StreetHash(i+float2(1,0)),f.x),lerp(StreetHash(i+float2(0,1)),StreetHash(i+1),f.x),f.y);
        }
        float3 ReliefNormal(float3 p, float3 n, float height)
        {
#if defined(SHADER_STAGE_RAY_TRACING)
    return n;
#else
            float3 dx=ddx(p), dy=ddy(p), r1=cross(dy,n), r2=cross(n,dx);
            float det=dot(dx,r1);
            return normalize(abs(det)*n-sign(det)*(ddx(height)*r1+ddy(height)*r2));
        
#endif
}
        void StreetSample(StreetInput IN, inout StreetSurfaceSample o)
        {
            float3 n = normalize(StreetWorldNormal(IN,float3(0,0,1)));
            float3 p = IN.worldPos;
            // Continuous facade coordinates also on rotated walls and sloping roofs.
            // Tangent/bitangent are orthonormal; the former dominant-axis projection stretched bays.
            float3 t = abs(n.y)>.95 ? float3(1,0,0) : normalize(cross(float3(0,1,0),n));
            float3 b = normalize(cross(n,t));
            float2 metres=max(float2(.05,.05),_TileMeters.xy);
            float2 uv=float2(dot(p,t),dot(p,b))/metres;
            float3 view=normalize(StreetWorldNormal(IN, IN.viewDir));
            float2 viewUV=float2(dot(view,t),dot(view,b));
            if(_UVRotation>.5) { uv=float2(uv.y,-uv.x); viewUV=float2(viewUV.y,-viewUV.x); }
            float height=STREET_SAMPLE(_MaskMap,uv).b-.5;
            uv-=viewUV/max(.3,abs(dot(view,n)))*height*_Parallax/metres;
            if(_UseUV>.5) uv=IN.uv_MainTex;
            float3 map = StreetNormalMap(STREET_SAMPLE(_BumpMap,uv));
            // Old high-contrast normals retained a low Z after scaling XY, keeping brick edges puffy.
            // A linear blend from the flat normal gives masonry the requested shallow relief.
            if(_SurfaceClass>1.5&&_SurfaceClass<2.5)map=normalize(lerp(float3(0,0,1),map,_BumpStrength));
            else map.xy *= _BumpStrength;
            if(_UVRotation>.5) map.xy=float2(-map.y,map.x);
            float3 world = normalize(t*map.x+b*map.y+n*map.z);
            float3 tangent=normalize(StreetWorldNormal(IN,float3(1,0,0)));
            float3 bitangent=normalize(StreetWorldNormal(IN,float3(0,1,0)));
            o.Normal=_UseUV>.5?normalize(map):normalize(float3(dot(world,tangent),dot(world,bitangent),dot(world,n)));
            float3 albedo=STREET_SAMPLE(_MainTex,uv).rgb;
            albedo=max(0,lerp(_AlbedoMean.rgb,albedo,_AlbedoContrast));
            float3 mask=STREET_SAMPLE(_MaskMap,uv).rgb;
            if(_SurfaceClass>.5&&_SurfaceClass<1.5)
            {
                // Cross-scale, rotated sample breaks the lawn's repeated 2m stamp.
                float2 uv2=float2(-uv.y,uv.x)*.731+float2(.317,.851);
                float blend=smoothstep(.25,.75,StreetNoise(p.xz*.065));
                albedo=lerp(albedo,STREET_SAMPLE(_MainTex,uv2).rgb,blend*.62);
                albedo*=lerp(.78,1.10,StreetNoise(p.xz*.037)+StreetNoise(p.xz*.31)*.15);
                mask=lerp(mask,STREET_SAMPLE(_MaskMap,uv2).rgb,blend*.62);
            }
            if(_SurfaceClass>3.5&&_SurfaceClass<4.5)
            {
                float2 uv2=float2(-uv.y,uv.x)*.83+float2(.73,.21);
                float blend=smoothstep(.22,.78,StreetNoise(p.xz*.19+3));
                albedo=lerp(albedo,STREET_SAMPLE(_MainTex,uv2).rgb,blend*.72);
                mask=lerp(mask,STREET_SAMPLE(_MaskMap,uv2).rgb,blend*.72);
                albedo*=lerp(.9,1.05,StreetNoise(p.xz*.061));
            }
            if(_SurfaceClass>4.5&&_SurfaceClass<5.5)
            {
                // A worn marking still follows the original lane geometry and road level.
                float wear=StreetNoise(p.xz*125)*.65+StreetNoise(p.xz*39)*.35;
                o.Visibility = step(0, wear-.17);
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
                float aa=max(.003,max(StreetWidth(q).x,StreetWidth(q).y));
                float visible=1-saturate(aa*.9);
                float jointHalf=max(.001,_PaverJointMeters)*5;
                float joint=(1-smoothstep(jointHalf-aa,jointHalf+aa,edge))*visible;
                float seed=StreetHash(origin);
                float3 brick=lerp(_PaverRedTone.rgb,_PaverGreyTone.rgb,step(1-_PaverGreyFraction,seed));
                brick*=1+lerp(-.14,.13,StreetHash(origin+31))*_PaverFinish.y;
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
            float stains=StreetNoise(float2(dot(p,t)*3.4,p.y*.19));
            float baseDirt=(1-smoothstep(.08,1.25,p.y))*stains;
            albedo*=1-_Weathering*(baseDirt*.34+(1-mask.r)*.12);
            if(_PaintedVariation>.001)
            {
                float2 brush = abs(n.y) > .72 ? p.xz : float2(dot(p,t),p.y);
                float broad = StreetNoise(brush * float2(.61,.29));
                float stroke = StreetNoise(brush * float2(2.4,.78) + 4.71);
                albedo *= 1 + (broad * .73 + stroke * .27 - .5) * _PaintedVariation;
            }
            if(_MeadowDrying > .001)
            {
                float dry = smoothstep(_MeadowSoilRange.z, _MeadowSoilRange.w, MeadowDryness(p.xz)) * _MeadowDrying;
                float grain = dot(STREET_SAMPLE(_DryGroundTex, p.xz * .91).rgb, float3(.2126, .7152, .0722));
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
                float2 pixelWidth = max(StreetWidth(panelUV) * panelSize, .0005);
                float2 joint = 1 - smoothstep(_PlinthPanels.z * .5 - pixelWidth,
                                               _PlinthPanels.z * .5 + pixelWidth, distanceToJoint);
                float visibility = 1 - saturate(max(StreetWidth(panelUV.x), StreetWidth(panelUV.y)));
                float seam = max(joint.x, joint.y) * visibility * _PlinthPanels.w;
                albedo *= lerp(.96, 1.04, StreetHash(floor(panelUV))) * lerp(1, .39, seam);
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

void GetSurfaceAndBuiltinData(FragInputs input, float3 V, inout PositionInputs posInput, out SurfaceData surfaceData, out BuiltinData builtinData RAY_TRACING_OPTIONAL_PARAMETERS)
{
    // Fix case 1210058. With Lit.shader / LayeredLit.shader we always have UV1. But in the case of some SpeedTree mesh, there is no stream sent
    // and UV1 is corrupt when we use surface gradient. In case UV1 aren't required we set them to 0, so we ensure there is no garbage.
    // When using lightmaps, the uv1 is always valid but we don't update _UVMappingMask.y to 1
    // So when we are using them, we just need to keep the UVs as is.
#if !defined(LIGHTMAP_ON) && defined(SURFACE_GRADIENT)
    input.texCoord1 = (_UVMappingMask.y + _UVDetailsMappingMask.y + _UVMappingMaskEmissive.y) > 0 ? input.texCoord1 : 0;
#endif

// Don't dither if displaced tessellation (we're fading out the displacement instead to match the next LOD)
#if !defined(SHADER_STAGE_RAY_TRACING) && !defined(_TESSELLATION_DISPLACEMENT)
#ifdef LOD_FADE_CROSSFADE // enable dithering LOD transition if user select CrossFade transition in LOD group
    LODDitheringTransition(ComputeFadeMaskSeed(V, posInput.positionSS), unity_LODFade.x);
#endif
#endif

    float3 doubleSidedConstants = GetDoubleSidedConstants();

    ApplyDoubleSidedFlipOrMirror(input, doubleSidedConstants); // Apply double sided flip on the vertex normal

    LayerTexCoord layerTexCoord;
    ZERO_INITIALIZE(LayerTexCoord, layerTexCoord);
    GetLayerTexCoord(input, layerTexCoord);

#if !defined(SHADER_STAGE_RAY_TRACING)
    float depthOffset = ApplyPerPixelDisplacement(input, V, layerTexCoord);
    #ifdef _DEPTHOFFSET_ON
    ApplyDepthOffsetPositionInput(V, depthOffset, GetViewForwardDir(), GetWorldToHClipMatrix(), posInput);
    #endif
#else
    float depthOffset = 0.0;
#endif

    StreetInput streetInput = StreetFragment(input, V);
    StreetSurfaceSample street = (StreetSurfaceSample)0;
    street.Normal = float3(0, 0, 1);
    street.Occlusion = 1;
    street.Alpha = 1;
    street.Visibility = 1;
    StreetSample(streetInput, street);

#if defined(_ALPHATEST_ON)
    float alphaTex = street.Alpha * street.Visibility;
    alphaTex = lerp(_AlphaRemapMin, _AlphaRemapMax, alphaTex);
    float alphaValue = alphaTex * _BaseColor.a;

    // Perform alha test very early to save performance (a killed pixel will not sample textures)
    #if SHADERPASS == SHADERPASS_TRANSPARENT_DEPTH_PREPASS
    float alphaCutoff = _AlphaCutoffPrepass;
    #elif SHADERPASS == SHADERPASS_TRANSPARENT_DEPTH_POSTPASS
    float alphaCutoff = _AlphaCutoffPostpass;
    #elif (SHADERPASS == SHADERPASS_SHADOWS) || (SHADERPASS == SHADERPASS_RAYTRACING_VISIBILITY)
    float alphaCutoff = _UseShadowThreshold ? _AlphaCutoffShadow : _AlphaCutoff;
    #else
    float alphaCutoff = _AlphaCutoff;
    #endif

    GENERIC_ALPHA_TEST(alphaValue, alphaCutoff);
#endif

    // We perform the conversion to world of the normalTS outside of the GetSurfaceData
    // so it allow us to correctly deal with detail normal map and optimize the code for the layered shaders
    float3 normalTS;
    float3 bentNormalTS;
    float3 bentNormalWS;
    float alpha = GetSurfaceData(input, layerTexCoord, surfaceData, normalTS, bentNormalTS);

    // This need to be init here to quiet the compiler in case of decal, but can be override later.
    surfaceData.geomNormalWS = input.tangentToWorld[2];
    surfaceData.specularOcclusion = 1.0;

#ifdef DECAL_NORMAL_BLENDING
    if (_EnableDecals)
    {
        #ifndef SURFACE_GRADIENT
        normalTS = SurfaceGradientFromTangentSpaceNormalAndFromTBN(normalTS,
                input.tangentToWorld[0], input.tangentToWorld[1]);
        #endif

        DecalSurfaceData decalSurfaceData = GetDecalSurfaceData(posInput, input, alpha);
        ApplyDecalToSurfaceData(decalSurfaceData, input.tangentToWorld[2], surfaceData, normalTS);
    }

    GetNormalWS_SG(input, normalTS, surfaceData.normalWS, doubleSidedConstants);
#else
    GetNormalWS(input, normalTS, surfaceData.normalWS, doubleSidedConstants);

    #if HAVE_DECALS
    if (_EnableDecals)
    {
        // Both uses and modifies 'surfaceData.normalWS'.
        DecalSurfaceData decalSurfaceData = GetDecalSurfaceData(posInput, input, alpha);
        ApplyDecalToSurfaceData(decalSurfaceData, input.tangentToWorld[2], surfaceData);
    }
    #endif
#endif

    alpha = street.Alpha;
    surfaceData.baseColor = street.Albedo;
    surfaceData.normalWS = SafeNormalize(StreetWorldNormal(streetInput, street.Normal));
    surfaceData.perceptualSmoothness = street.Smoothness;
    surfaceData.ambientOcclusion = street.Occlusion;
    surfaceData.metallic = street.Metallic;

    // Use bent normal to sample GI if available
#ifdef _BENTNORMALMAP
    GetNormalWS(input, bentNormalTS, bentNormalWS, doubleSidedConstants);
#else
    bentNormalWS = surfaceData.normalWS;
#endif

#if defined(DEBUG_DISPLAY)
#if !defined(SHADER_STAGE_RAY_TRACING)
    // Mipmap mode debugging isn't supported with ray tracing as it relies on derivatives
    if (_DebugMipMapMode != DEBUGMIPMAPMODE_NONE)
    {
        surfaceData.baseColor = GET_TEXTURE_STREAMING_DEBUG(posInput.positionSS, input.texCoord0);
        surfaceData.metallic = 0;
    }
#endif

    // We need to call ApplyDebugToSurfaceData after filling the surfaceData and before filling builtinData
    // as it can modify attribute use for static lighting
    ApplyDebugToSurfaceData(input.tangentToWorld, surfaceData);
#endif

    // By default we use the ambient occlusion with Tri-ace trick (apply outside) for specular occlusion.
    // If user provide bent normal then we process a better term
#if defined(_SPECULAR_OCCLUSION_FROM_BENT_NORMAL_MAP)
    // If we have bent normal and ambient occlusion, process a specular occlusion
    surfaceData.specularOcclusion = GetSpecularOcclusionFromBentAO(V, bentNormalWS, surfaceData.normalWS, surfaceData.ambientOcclusion, PerceptualSmoothnessToRoughness(surfaceData.perceptualSmoothness));
    // Don't do spec occ from Ambient if there is no mask mask
#elif defined(_MASKMAP) && !defined(_SPECULAR_OCCLUSION_NONE)
    surfaceData.specularOcclusion = GetSpecularOcclusionFromAmbientOcclusion(ClampNdotV(dot(surfaceData.normalWS, V)), surfaceData.ambientOcclusion, PerceptualSmoothnessToRoughness(surfaceData.perceptualSmoothness));
#endif

    // This is use with anisotropic material
    surfaceData.tangentWS = Orthonormalize(surfaceData.tangentWS, surfaceData.normalWS);

#if defined(_ENABLE_GEOMETRIC_SPECULAR_AA) && !defined(SHADER_STAGE_RAY_TRACING)
    // Specular AA
    #ifdef PROJECTED_SPACE_NDF_FILTERING
    surfaceData.perceptualSmoothness = ProjectedSpaceGeometricNormalFiltering(surfaceData.perceptualSmoothness, input.tangentToWorld[2], _SpecularAAScreenSpaceVariance, _SpecularAAThreshold);
    #else
    surfaceData.perceptualSmoothness = GeometricNormalFiltering(surfaceData.perceptualSmoothness, input.tangentToWorld[2], _SpecularAAScreenSpaceVariance, _SpecularAAThreshold);
    #endif
#endif

    // Caution: surfaceData must be fully initialize before calling GetBuiltinData
    GetBuiltinData(input, V, posInput, surfaceData, alpha, bentNormalWS, depthOffset, layerTexCoord.base, builtinData);
    builtinData.emissiveColor += street.Emission;

#ifdef _ALPHATEST_ON
    // Used for sharpening by alpha to mask
    builtinData.alphaClipTreshold = alphaCutoff;
#endif

    RAY_TRACING_OPTIONAL_ALPHA_TEST_PASS
}
#if !defined(SHADER_STAGE_RAY_TRACING)
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/LitDataMeshModification.hlsl"
#endif
#endif // #ifndef LAYERED_LIT_SHADER
