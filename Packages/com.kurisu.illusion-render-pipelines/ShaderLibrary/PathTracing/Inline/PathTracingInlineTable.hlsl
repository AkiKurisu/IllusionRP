#ifndef ILLUSION_PATH_TRACING_INLINE_TABLE_INCLUDED
#define ILLUSION_PATH_TRACING_INLINE_TABLE_INCLUDED

// The scene resource table must be declared after every other shader resource of the compute shader.
Texture2D<float4> _PathTracingInlineTextures[PT_INLINE_TABLE_SIZE] : register(t31);
ByteAddressBuffer _PathTracingInlineBuffers[PT_INLINE_TABLE_SIZE] : register(t31, space1);

uint3 UnityRayTracingFetchTriangleIndices(uint primitiveIndex)
{
    PathTracingInlineGeometry g = g_PathTracingInlineGeometry;
    ByteAddressBuffer indexBuffer = _PathTracingInlineBuffers[NonUniformResourceIndex(g.indexBuffer)];
    uint3 indices;
    if (g.indexSize == 2)
    {
        uint offsetInBytes = (g.indexStart + primitiveIndex * 3) << 1;
        uint aligned = offsetInBytes & ~3u;
        uint2 four = indexBuffer.Load2(aligned);
        indices = aligned == offsetInBytes
            ? uint3(four.x & 0xffff, four.x >> 16, four.y & 0xffff)
            : uint3(four.x >> 16, four.y & 0xffff, four.y >> 16);
        indices += g.baseVertex;
    }
    else if (g.indexSize == 4)
    {
        indices = indexBuffer.Load3((g.indexStart + primitiveIndex * 3) << 2) + g.baseVertex;
    }
    else
    {
        indices = (primitiveIndex * 3 + g.vertexStart).xxx + uint3(0, 1, 2);
    }
    return indices;
}

float4 PathTracingInlineFetchAttribute(uint vertexIndex, uint attributeType)
{
    uint attribute = PathTracingInlineAttribute(attributeType);
    uint dimension = attribute & 7u;
    if (dimension == 0 || dimension > 4)
        return unity_DefaultVertexAttributes[attributeType];
    uint format = (attribute >> 3) & 15u;
    uint stream = (attribute >> 7) & 3u;
    uint address = vertexIndex * g_PathTracingInlineGeometry.vertexSize[stream] + (attribute >> 9);
    ByteAddressBuffer vertexBuffer = _PathTracingInlineBuffers[NonUniformResourceIndex(g_PathTracingInlineGeometry.vertexBuffers[stream])];

    float4 value = 0;
    if (format == kVertexFormatFloat)
    {
        if (dimension == 1) value.x = asfloat(vertexBuffer.Load(address));
        else if (dimension == 2) value.xy = asfloat(vertexBuffer.Load2(address));
        else if (dimension == 3) value.xyz = asfloat(vertexBuffer.Load3(address));
        else value = asfloat(vertexBuffer.Load4(address));
    }
    else if (format == kVertexFormatFloat16)
    {
        uint2 halfs = dimension > 2 ? vertexBuffer.Load2(address) : uint2(vertexBuffer.Load(address), 0);
        value = float4(f16tof32(halfs.x), f16tof32(halfs.x >> 16), f16tof32(halfs.y), f16tof32(halfs.y >> 16));
    }
    else if (format == kVertexFormatSNorm16)
    {
        uint2 shorts = dimension > 2 ? vertexBuffer.Load2(address) : uint2(vertexBuffer.Load(address), 0);
        value = float4(DecodeSNorm16(shorts.x & 0xffff), DecodeSNorm16(shorts.x >> 16), DecodeSNorm16(shorts.y & 0xffff), DecodeSNorm16(shorts.y >> 16));
    }
    else if (format == kVertexFormatUNorm16)
    {
        uint2 shorts = dimension > 2 ? vertexBuffer.Load2(address) : uint2(vertexBuffer.Load(address), 0);
        value = float4(shorts.x & 0xffff, shorts.x >> 16, shorts.y & 0xffff, shorts.y >> 16) / 65535.0;
    }
    else if (format == kVertexFormatUNorm8)
    {
        uint data = vertexBuffer.Load(address);
        value = float4(data & 0xff, (data >> 8) & 0xff, (data >> 16) & 0xff, data >> 24) / 255.0;
    }
    return unity_VertexChannelMask_RT[dimension] * value;
}

float4 PathTracingInlineSampleGrad(uint textureSlot, uint samplerSlot, float2 uv, float2 gradX, float2 gradY)
{
    uint entry = PathTracingInlineTextureEntry(textureSlot);
    Texture2D<float4> tex = _PathTracingInlineTextures[NonUniformResourceIndex(entry & PT_INLINE_INDEX_MASK)];
    switch (PathTracingInlineSamplerState(entry, samplerSlot) & 15u)
    {
        case 0u: return tex.SampleGrad(sampler_PathTracingInline_Point_Repeat, uv, gradX, gradY);
        case 1u: return tex.SampleGrad(sampler_PathTracingInline_Linear_Repeat, uv, gradX, gradY);
        case 4u: return tex.SampleGrad(sampler_PathTracingInline_Point_Clamp, uv, gradX, gradY);
        case 5u: return tex.SampleGrad(sampler_PathTracingInline_Linear_Clamp, uv, gradX, gradY);
        case 6u: return tex.SampleGrad(sampler_PathTracingInline_Trilinear_Clamp, uv, gradX, gradY);
        case 8u: return tex.SampleGrad(sampler_PathTracingInline_Point_Mirror, uv, gradX, gradY);
        case 9u: return tex.SampleGrad(sampler_PathTracingInline_Linear_Mirror, uv, gradX, gradY);
        case 10u: return tex.SampleGrad(sampler_PathTracingInline_Trilinear_Mirror, uv, gradX, gradY);
        case 3u: return tex.SampleGrad(sampler_PathTracingInline_Trilinear_Repeat_Aniso16, uv, gradX, gradY);
        case 7u: return tex.SampleGrad(sampler_PathTracingInline_Trilinear_Clamp_Aniso16, uv, gradX, gradY);
        case 11u: return tex.SampleGrad(sampler_PathTracingInline_Trilinear_Mirror_Aniso16, uv, gradX, gradY);
        default: return tex.SampleGrad(sampler_PathTracingInline_Trilinear_Repeat, uv, gradX, gradY);
    }
}

float4 PathTracingInlineSampleLevel(uint textureSlot, uint samplerSlot, float2 uv, float lod)
{
    uint entry = PathTracingInlineTextureEntry(textureSlot);
    Texture2D<float4> tex = _PathTracingInlineTextures[NonUniformResourceIndex(entry & PT_INLINE_INDEX_MASK)];
    switch (PathTracingInlineSamplerState(entry, samplerSlot) & 15u)
    {
        case 0u: return tex.SampleLevel(sampler_PathTracingInline_Point_Repeat, uv, lod);
        case 1u: return tex.SampleLevel(sampler_PathTracingInline_Linear_Repeat, uv, lod);
        case 4u: return tex.SampleLevel(sampler_PathTracingInline_Point_Clamp, uv, lod);
        case 5u: return tex.SampleLevel(sampler_PathTracingInline_Linear_Clamp, uv, lod);
        case 6u: return tex.SampleLevel(sampler_PathTracingInline_Trilinear_Clamp, uv, lod);
        case 8u: return tex.SampleLevel(sampler_PathTracingInline_Point_Mirror, uv, lod);
        case 9u: return tex.SampleLevel(sampler_PathTracingInline_Linear_Mirror, uv, lod);
        case 10u: return tex.SampleLevel(sampler_PathTracingInline_Trilinear_Mirror, uv, lod);
        case 3u: return tex.SampleLevel(sampler_PathTracingInline_Trilinear_Repeat_Aniso16, uv, lod);
        case 7u: return tex.SampleLevel(sampler_PathTracingInline_Trilinear_Clamp_Aniso16, uv, lod);
        case 11u: return tex.SampleLevel(sampler_PathTracingInline_Trilinear_Mirror_Aniso16, uv, lod);
        default: return tex.SampleLevel(sampler_PathTracingInline_Trilinear_Repeat, uv, lod);
    }
}

float4 PathTracingInlineLoad(uint textureSlot, int3 coord)
{
    uint entry = PathTracingInlineTextureEntry(textureSlot);
    return _PathTracingInlineTextures[NonUniformResourceIndex(entry & PT_INLINE_INDEX_MASK)].Load(coord);
}

float4 PathTracingInlineSampleGrad(uint textureSlot, SamplerState s, float2 uv, float2 gradX, float2 gradY)
{
    uint entry = PathTracingInlineTextureEntry(textureSlot);
    return _PathTracingInlineTextures[NonUniformResourceIndex(entry & PT_INLINE_INDEX_MASK)].SampleGrad(s, uv, gradX, gradY);
}

float4 PathTracingInlineSampleLevel(uint textureSlot, SamplerState s, float2 uv, float lod)
{
    uint entry = PathTracingInlineTextureEntry(textureSlot);
    return _PathTracingInlineTextures[NonUniformResourceIndex(entry & PT_INLINE_INDEX_MASK)].SampleLevel(s, uv, lod);
}

#endif
