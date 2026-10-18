#ifndef ILLUSION_PATH_TRACING_INLINE_INCLUDED
#define ILLUSION_PATH_TRACING_INLINE_INCLUDED

#define SHADER_STAGE_RAY_TRACING 1
#define PATH_TRACING_INLINE 1

#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/PathTracing/PathTracingPayload.hlsl"

#define PT_INLINE_TABLE_SIZE 65536
#define PT_INLINE_NO_EVALUATOR 0xFFFFFFFFu
#define PT_INLINE_INDEX_MASK 0x00FFFFFFu

// Must match PathTracingInlineGeometry in PathTracingInlineScene.cs.
struct PathTracingInlineGeometry
{
    uint4 attributes[3];
    uint4 vertexSize;
    uint4 vertexBuffers;
    uint indexBuffer;
    uint indexSize;
    uint indexStart;
    uint baseVertex;
    uint vertexStart;
    uint materialData;
    uint evaluator;
    uint flags;
};

#define PT_INLINE_GEOMETRY_OPAQUE 1u

StructuredBuffer<PathTracingInlineGeometry> _PathTracingInlineGeometries;
StructuredBuffer<uint> _PathTracingInlineInstanceGeometries;
ByteAddressBuffer _PathTracingInlineMaterialData;

struct PathTracingInlineHitState
{
    uint instanceID;
    uint instanceIndex;
    uint primitiveIndex;
    uint geometryIndex;
    float3x4 objectToWorld;
    float3x4 worldToObject;
    float3 rayOrigin;
    float3 rayDirection;
    float t;
};

static PathTracingInlineHitState g_PathTracingInline;
static PathTracingInlineGeometry g_PathTracingInlineGeometry;
static bool g_PathTracingInlineIgnored;

void PathTracingInlineIgnoreHit() { g_PathTracingInlineIgnored = true; }

#define InstanceID() (g_PathTracingInline.instanceID)
#define InstanceIndex() (g_PathTracingInline.instanceIndex)
#define PrimitiveIndex() (g_PathTracingInline.primitiveIndex)
#define GeometryIndex() (g_PathTracingInline.geometryIndex)
#define ObjectToWorld3x4() (g_PathTracingInline.objectToWorld)
#define WorldToObject3x4() (g_PathTracingInline.worldToObject)
#define WorldRayOrigin() (g_PathTracingInline.rayOrigin)
#define WorldRayDirection() (g_PathTracingInline.rayDirection)
#define RayTCurrent() (g_PathTracingInline.t)
#define IgnoreHit() PathTracingInlineIgnoreHit()

#define UNITY_RAY_TRACING_MESH_UTILS_INCLUDED

struct MeshInfo
{
    uint vertexSize[4];
    uint baseVertex;
    uint vertexStart;
    uint indexSize;
    uint indexStart;
};

#define kVertexAttributePosition    0
#define kVertexAttributeNormal      1
#define kVertexAttributeTangent     2
#define kVertexAttributeColor       3
#define kVertexAttributeTexCoord0   4
#define kVertexAttributeTexCoord1   5
#define kVertexAttributeTexCoord2   6
#define kVertexAttributeTexCoord3   7
#define kVertexAttributeCount       12

#define kVertexFormatFloat          0
#define kVertexFormatFloat16        1
#define kVertexFormatUNorm8         2
#define kVertexFormatUNorm16        4
#define kVertexFormatSNorm16        5

static const float4 unity_DefaultVertexAttributes[kVertexAttributeCount] =
{
    float4(0, 0, 0, 0), float4(0, 0, 1, 0), float4(1, 0, 0, 1), float4(1, 1, 1, 1),
    float4(0, 0, 0, 0), float4(0, 0, 0, 0), float4(0, 0, 0, 0), float4(0, 0, 0, 0),
    float4(0, 0, 0, 0), float4(0, 0, 0, 0), float4(0, 0, 0, 0), float4(0, 0, 0, 0)
};

static const float4 unity_VertexChannelMask_RT[5] =
{
    float4(0, 0, 0, 0), float4(1, 0, 0, 0), float4(1, 1, 0, 0), float4(1, 1, 1, 0), float4(1, 1, 1, 1)
};

MeshInfo GetMeshInfo()
{
    MeshInfo info;
    info.vertexSize[0] = g_PathTracingInlineGeometry.vertexSize.x;
    info.vertexSize[1] = g_PathTracingInlineGeometry.vertexSize.y;
    info.vertexSize[2] = g_PathTracingInlineGeometry.vertexSize.z;
    info.vertexSize[3] = g_PathTracingInlineGeometry.vertexSize.w;
    info.baseVertex = g_PathTracingInlineGeometry.baseVertex;
    info.vertexStart = g_PathTracingInlineGeometry.vertexStart;
    info.indexSize = g_PathTracingInlineGeometry.indexSize;
    info.indexStart = g_PathTracingInlineGeometry.indexStart;
    return info;
}

// Attribute packing: dimension in bits 0-2, format in bits 3-6, stream in bits 7-8, byte offset from bit 9.
uint PathTracingInlineAttribute(uint attributeType)
{
    return attributeType < kVertexAttributeCount ? g_PathTracingInlineGeometry.attributes[attributeType >> 2][attributeType & 3] : 0u;
}

float DecodeSNorm16(uint data)
{
    int signedValue = (data & 0x8000) ? -1 * int((~data & 0x7fff) + 1) : int(data);
    return max(signedValue * (1.0 / 32767.0), -1.0);
}

uint3 UnityRayTracingFetchTriangleIndices(uint primitiveIndex);

bool UnityRayTracingHasVertexAttribute(uint attributeType)
{
    return (PathTracingInlineAttribute(attributeType) & 7u) != 0;
}

float4 PathTracingInlineFetchAttribute(uint vertexIndex, uint attributeType);

float2 UnityRayTracingFetchVertexAttribute2(uint vertexIndex, uint attributeType) { return PathTracingInlineFetchAttribute(vertexIndex, attributeType).xy; }
float3 UnityRayTracingFetchVertexAttribute3(uint vertexIndex, uint attributeType) { return PathTracingInlineFetchAttribute(vertexIndex, attributeType).xyz; }
float4 UnityRayTracingFetchVertexAttribute4(uint vertexIndex, uint attributeType) { return PathTracingInlineFetchAttribute(vertexIndex, attributeType); }

static uint g_PathTracingInlineMaterialBase;

typedef uint PathTracingInlineTexture2D;
typedef uint PathTracingInlineSampler;

// Distinct types keep overloads by texture dimension apart. These textures are not in the resource table.
struct PathTracingInlineTexture2DArray { uint slot; };
struct PathTracingInlineTextureCube { uint2 slot; };
struct PathTracingInlineTextureCubeArray { uint3 slot; };
struct PathTracingInlineTexture3D { uint4 slot; };
struct PathTracingInlineSamplerComparison { float2 slot; };

#define PT_INLINE_SAMPLER_STATE 0x80000000u
#define PT_INLINE_SAMPLER_DEFAULT 0u

SamplerState sampler_PathTracingInline_Point_Repeat;
SamplerState sampler_PathTracingInline_Point_Clamp;
SamplerState sampler_PathTracingInline_Point_Mirror;
SamplerState sampler_PathTracingInline_Linear_Repeat;
SamplerState sampler_PathTracingInline_Linear_Clamp;
SamplerState sampler_PathTracingInline_Linear_Mirror;
SamplerState sampler_PathTracingInline_Trilinear_Repeat;
SamplerState sampler_PathTracingInline_Trilinear_Clamp;
SamplerState sampler_PathTracingInline_Trilinear_Mirror;
SamplerState sampler_PathTracingInline_Trilinear_Repeat_Aniso16;
SamplerState sampler_PathTracingInline_Trilinear_Clamp_Aniso16;
SamplerState sampler_PathTracingInline_Trilinear_Mirror_Aniso16;

uint PathTracingInlineTextureEntry(uint slot)
{
    return _PathTracingInlineMaterialData.Load(g_PathTracingInlineMaterialBase + slot * 4);
}

uint PathTracingInlineSamplerState(uint textureEntry, uint samplerSlot)
{
    if ((samplerSlot & PT_INLINE_SAMPLER_STATE) != 0)
        return samplerSlot & 0xFFu;
    if (samplerSlot != PT_INLINE_SAMPLER_DEFAULT)
        return PathTracingInlineTextureEntry(samplerSlot - 1) >> 24;
    return textureEntry >> 24;
}

float4 PathTracingInlineSampleGrad(uint textureSlot, uint samplerSlot, float2 uv, float2 gradX, float2 gradY);

float4 PathTracingInlineSampleLevel(uint textureSlot, uint samplerSlot, float2 uv, float lod);

float4 PathTracingInlineLoad(uint textureSlot, int3 coord);

float4 PathTracingInlineSampleGrad(uint textureSlot, SamplerState s, float2 uv, float2 gradX, float2 gradY);

float4 PathTracingInlineSampleLevel(uint textureSlot, SamplerState s, float2 uv, float lod);

float4 PathTracingInlineSampleGrad(Texture2D tex, SamplerState s, float2 uv, float2 gradX, float2 gradY) { return tex.SampleGrad(s, uv, gradX, gradY); }
float4 PathTracingInlineSampleLevel(Texture2D tex, SamplerState s, float2 uv, float lod) { return tex.SampleLevel(s, uv, lod); }
float4 PathTracingInlineLoad(Texture2D tex, int3 coord) { return tex.Load(coord); }

float4 PathTracingInlineSampleOther(TextureCube tex, SamplerState s, float3 coord, float lod) { return tex.SampleLevel(s, coord, lod); }
float4 PathTracingInlineSampleOther(TextureCubeArray tex, SamplerState s, float4 coord, float lod) { return tex.SampleLevel(s, coord, lod); }
float4 PathTracingInlineSampleOther(Texture2DArray tex, SamplerState s, float3 coord, float lod) { return tex.SampleLevel(s, coord, lod); }
float4 PathTracingInlineSampleOther(Texture3D tex, SamplerState s, float3 coord, float lod) { return tex.SampleLevel(s, coord, lod); }
float4 PathTracingInlineSampleOther(PathTracingInlineTexture2DArray tex, uint s, float3 coord, float lod) { return 0; }
float4 PathTracingInlineSampleOther(PathTracingInlineTexture2DArray tex, SamplerState s, float3 coord, float lod) { return 0; }
float4 PathTracingInlineSampleOther(PathTracingInlineTextureCube tex, uint s, float3 coord, float lod) { return 0; }
float4 PathTracingInlineSampleOther(PathTracingInlineTextureCube tex, SamplerState s, float3 coord, float lod) { return 0; }
float4 PathTracingInlineSampleOther(PathTracingInlineTextureCubeArray tex, uint s, float4 coord, float lod) { return 0; }
float4 PathTracingInlineSampleOther(PathTracingInlineTextureCubeArray tex, SamplerState s, float4 coord, float lod) { return 0; }
float4 PathTracingInlineSampleOther(PathTracingInlineTexture3D tex, uint s, float3 coord, float lod) { return 0; }
float4 PathTracingInlineSampleOther(PathTracingInlineTexture3D tex, SamplerState s, float3 coord, float lod) { return 0; }
float PathTracingInlineSampleShadow(Texture2D tex, SamplerComparisonState s, float3 coord) { return tex.SampleCmpLevelZero(s, coord.xy, coord.z); }
float PathTracingInlineSampleShadow(uint tex, PathTracingInlineSamplerComparison s, float3 coord) { return 1; }
float4 PathTracingInlineGather(Texture2D tex, SamplerState s, float2 coord) { return tex.Gather(s, coord); }
float4 PathTracingInlineGather(uint tex, uint s, float2 coord) { return 0; }
float4 PathTracingInlineGather(uint tex, SamplerState s, float2 coord) { return 0; }
float PathTracingInlineLod(Texture2D tex, SamplerState s, float2 coord) { return tex.CalculateLevelOfDetail(s, coord); }
float PathTracingInlineLod(uint tex, uint s, float2 coord) { return 0; }

#endif
