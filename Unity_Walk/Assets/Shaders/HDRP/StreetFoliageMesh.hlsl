// Note: positionWS can be either in camera relative space or not
float3 GetVertexDisplacement(float3 positionRWS, float3 normalWS, float2 texCoord0, float2 texCoord1, float2 texCoord2, float2 texCoord3, float4 vertexColor)
{
    // This call will work for both LayeredLit and Lit shader
    LayerTexCoord layerTexCoord;
    ZERO_INITIALIZE(LayerTexCoord, layerTexCoord);
    GetLayerTexCoord(texCoord0, texCoord1, texCoord2, texCoord3, positionRWS, normalWS, layerTexCoord);

    // TODO: do this algorithm for lod fetching as lod not available in vertex/domain shader
    // http://www.sebastiansylvan.com/post/the-problem-with-tessellation-in-directx-11/
    float lod = 0.0;
    return ComputePerVertexDisplacement(layerTexCoord, vertexColor, lod) * normalWS;
}

// Note: positionWS can be either in camera relative space or not
void ApplyVertexModification(AttributesMesh input, float3 normalWS, inout float3 positionRWS, float3 timeParameters)
{
#if defined(STREET_FOLIAGE_WIND)
    if (_Grass >= 2.5)
    {
        // Reduce the density of the instanced lawn before Terrain drops its detail patches.
        float3 rootRWS = TransformObjectToWorld(float3(0.0, 0.0, 0.0));
        float distanceToCamera = length(rootRWS.xz - GetPrimaryCameraPosition().xz);
        float density = 1.0 - smoothstep(15.0, 26.0, distanceToCamera);
        float2 cell = floor(GetAbsolutePositionWS(rootRWS).xz * 16.0);
        float variation = frac(dot(cell, float2(0.75487766, 0.5698403)));
        float growth = smoothstep(0.0, 0.23, density - variation * 0.77);
        positionRWS.y = lerp(rootRWS.y - 0.055, positionRWS.y, growth);
    }
    else if (_Grass >= 1.0)
    {
        // The baked mid-distance lawn keeps the higher silhouette as Terrain fades.
        // Moving only Y keeps the roots and the clearance at paving and curbs.
        float addedHeight = 0.053;
#if defined(ATTRIBUTES_NEED_COLOR)
        addedHeight = lerp(0.036, 0.078, saturate(3.5 * (input.color.r - input.color.b)));
#endif
        float tip = saturate(input.uv0.y);
        positionRWS.y += addedHeight * tip * tip;
    }
    float3 world = GetAbsolutePositionWS(positionRWS);
    float wave = StreetBreeze(world.xz, timeParameters.x);
    float weight = _Grass > .5 ? pow(saturate(input.uv0.y), 1.7)
                              : saturate((input.positionOS.y - _WindStemHeight) * _WindHeightScale);
    positionRWS += float3(.82, 0, .57) * wave * _WindStrength * weight * .82;
#endif
#if defined(_VERTEX_DISPLACEMENT)

    positionRWS += GetVertexDisplacement(positionRWS, normalWS,
    #ifdef ATTRIBUTES_NEED_TEXCOORD0
        input.uv0,
    #else
        float2(0.0, 0.0),
    #endif
    #ifdef ATTRIBUTES_NEED_TEXCOORD1
        input.uv1,
    #else
        float2(0.0, 0.0),
    #endif
    #ifdef ATTRIBUTES_NEED_TEXCOORD2
        input.uv2,
    #else
        float2(0.0, 0.0),
    #endif
    #ifdef ATTRIBUTES_NEED_TEXCOORD3
        input.uv3,
    #else
        float2(0.0, 0.0),
    #endif
    #ifdef ATTRIBUTES_NEED_COLOR
        input.color
    #else
        float4(0.0, 0.0, 0.0, 0.0)
    #endif
        );
#endif
}

#ifdef TESSELLATION_ON

float GetTessellationFactor(AttributesMesh input)
{
    return _TessellationFactor;
}

VaryingsMeshToDS ApplyTessellationModification(VaryingsMeshToDS input, float3 timeParameters)
{
#if defined(_TESSELLATION_DISPLACEMENT)

    input.positionRWS += GetVertexDisplacement(input.positionRWS, input.normalWS,
    #ifdef VARYINGS_DS_NEED_TEXCOORD0
        input.texCoord0.xy,
    #else
        float2(0.0, 0.0),
    #endif
    #ifdef VARYINGS_DS_NEED_TEXCOORD1
        input.texCoord1.xy,
    #else
        float2(0.0, 0.0),
    #endif
    #ifdef VARYINGS_DS_NEED_TEXCOORD2
        input.texCoord2.xy,
    #else
        float2(0.0, 0.0),
    #endif
    #ifdef VARYINGS_DS_NEED_TEXCOORD3
        input.texCoord3.xy,
    #else
        float2(0.0, 0.0),
    #endif
    #ifdef VARYINGS_DS_NEED_COLOR
        input.color
    #else
        float4(0.0, 0.0, 0.0, 0.0)
    #endif
        );

#endif // _TESSELLATION_DISPLACEMENT

    return input;
}

#endif // #ifdef TESSELLATION_ON
