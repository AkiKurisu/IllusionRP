#ifndef ILLUSION_PATH_TRACING_HIT_INCLUDED
#define ILLUSION_PATH_TRACING_HIT_INCLUDED


static bool g_PathTracingClipped;
void PathTracingClip(float value) { g_PathTracingClipped = g_PathTracingClipped || value < 0.0; }
void PathTracingClip(float2 value) { g_PathTracingClipped = g_PathTracingClipped || any(value < 0.0); }
void PathTracingClip(float3 value) { g_PathTracingClipped = g_PathTracingClipped || any(value < 0.0); }
void PathTracingClip(float4 value) { g_PathTracingClipped = g_PathTracingClipped || any(value < 0.0); }
#define clip(value) PathTracingClip(value)

#define ddx(value) ((value) * 0)
#define ddy(value) ((value) * 0)
#define ddx_coarse(value) ((value) * 0)
#define ddy_coarse(value) ((value) * 0)
#define ddx_fine(value) ((value) * 0)
#define ddy_fine(value) ((value) * 0)
#define fwidth(value) ((value) * 0)

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "UnityRaytracingMeshUtils.cginc"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/PathTracing/PathTracingPayload.hlsl"

struct AttributeData
{
    float2 barycentrics;
};

struct PathTracingHitVertex
{
    float3 positionOS;
    float3 normalOS;
    float4 tangentOS;
    float4 texCoord0;
    float4 texCoord1;
    float4 texCoord2;
    float4 texCoord3;
    float4 color;
};

struct PathTracingHitContext
{
    PathTracingHitVertex vertex;
    float3 positionWS;
    float3 viewDirWS;
    float3 faceNormalWS;
    float3 vertexNormalWS;
    float4 tangentWS;
    bool frontFacing;
    uint3 indices;
    float3 barycentrics;
};

// @IllusionRP: instance ABI: x = previous-position base (or PT_NO_POSITION_HISTORY), y = packed sub-mesh culling range.
ByteAddressBuffer _PathTracingPreviousPositions;
StructuredBuffer<uint2> _PathTracingInstanceData;
StructuredBuffer<uint2> _PathTracingCulledSubMeshes;
StructuredBuffer<uint2> _PathTracingMaterialRanges;
StructuredBuffer<uint2> _PathTracingSubMeshMaterials;

uint PathTracingGetMaterialIndex()
{
    uint2 range = _PathTracingMaterialRanges[InstanceID()];
    uint indexStart = GetMeshInfo().indexStart;
    for (uint i = range.x; i < range.x + range.y; ++i)
    {
        uint2 subMesh = _PathTracingSubMeshMaterials[i];
        if (subMesh.x == indexStart)
            return subMesh.y;
    }
    return 0xFFFFFFFFu;
}

static PathTracingHitContext g_PathTracingHit;
static float4 g_PathTracingScreenPosition;

#define SHADERPASS_PATH_TRACING 100

static float g_PathTracingUVFootprint;

float2 PathTracingGradX(float2 uv) { return float2(g_PathTracingUVFootprint, 0.0); }
float2 PathTracingGradY(float2 uv) { return float2(0.0, g_PathTracingUVFootprint); }
float3 PathTracingGradX(float3 uv) { return float3(g_PathTracingUVFootprint, 0.0, 0.0); }
float3 PathTracingGradY(float3 uv) { return float3(0.0, g_PathTracingUVFootprint, 0.0); }

#undef PLATFORM_SAMPLE_TEXTURE2D
#define PLATFORM_SAMPLE_TEXTURE2D(textureName, samplerName, coord2) textureName.SampleGrad(samplerName, coord2, PathTracingGradX(coord2), PathTracingGradY(coord2))
#undef PLATFORM_SAMPLE_TEXTURE2D_BIAS
#define PLATFORM_SAMPLE_TEXTURE2D_BIAS(textureName, samplerName, coord2, bias) textureName.SampleGrad(samplerName, coord2, PathTracingGradX(coord2) * exp2(bias), PathTracingGradY(coord2) * exp2(bias))
#undef PLATFORM_SAMPLE_TEXTURE2D_GRAD
#define PLATFORM_SAMPLE_TEXTURE2D_GRAD(textureName, samplerName, coord2, dpdx, dpdy) textureName.SampleGrad(samplerName, coord2, PathTracingGradX(coord2), PathTracingGradY(coord2))
#undef PLATFORM_SAMPLE_TEXTURE2D_ARRAY
#define PLATFORM_SAMPLE_TEXTURE2D_ARRAY(textureName, samplerName, coord2, index) textureName.SampleGrad(samplerName, float3(coord2, index), PathTracingGradX(coord2), PathTracingGradY(coord2))
#undef PLATFORM_SAMPLE_TEXTURE2D_ARRAY_BIAS
#define PLATFORM_SAMPLE_TEXTURE2D_ARRAY_BIAS(textureName, samplerName, coord2, index, bias) textureName.SampleGrad(samplerName, float3(coord2, index), PathTracingGradX(coord2) * exp2(bias), PathTracingGradY(coord2) * exp2(bias))
#undef PLATFORM_SAMPLE_TEXTURE2D_ARRAY_GRAD
#define PLATFORM_SAMPLE_TEXTURE2D_ARRAY_GRAD(textureName, samplerName, coord2, index, dpdx, dpdy) textureName.SampleGrad(samplerName, float3(coord2, index), PathTracingGradX(coord2), PathTracingGradY(coord2))
#undef PLATFORM_SAMPLE_TEXTURECUBE
#define PLATFORM_SAMPLE_TEXTURECUBE(textureName, samplerName, coord3) textureName.SampleLevel(samplerName, coord3, 0)
#undef PLATFORM_SAMPLE_TEXTURECUBE_BIAS
#define PLATFORM_SAMPLE_TEXTURECUBE_BIAS(textureName, samplerName, coord3, bias) textureName.SampleLevel(samplerName, coord3, bias)
#undef PLATFORM_SAMPLE_TEXTURECUBE_ARRAY
#define PLATFORM_SAMPLE_TEXTURECUBE_ARRAY(textureName, samplerName, coord3, index) textureName.SampleLevel(samplerName, float4(coord3, index), 0)
#undef PLATFORM_SAMPLE_TEXTURECUBE_ARRAY_BIAS
#define PLATFORM_SAMPLE_TEXTURECUBE_ARRAY_BIAS(textureName, samplerName, coord3, index, bias) textureName.SampleLevel(samplerName, float4(coord3, index), bias)
#undef PLATFORM_SAMPLE_TEXTURE3D
#define PLATFORM_SAMPLE_TEXTURE3D(textureName, samplerName, coord3) textureName.SampleGrad(samplerName, coord3, PathTracingGradX(coord3), PathTracingGradY(coord3))

PathTracingHitVertex PathTracingFetchVertex(uint vertexIndex)
{
    PathTracingHitVertex v;
    v.positionOS = UnityRayTracingFetchVertexAttribute3(vertexIndex, kVertexAttributePosition);
    v.normalOS = UnityRayTracingFetchVertexAttribute3(vertexIndex, kVertexAttributeNormal);
    v.tangentOS = UnityRayTracingHasVertexAttribute(kVertexAttributeTangent)
        ? UnityRayTracingFetchVertexAttribute4(vertexIndex, kVertexAttributeTangent) : float4(0, 0, 0, 1);
    v.texCoord0 = UnityRayTracingHasVertexAttribute(kVertexAttributeTexCoord0)
        ? UnityRayTracingFetchVertexAttribute4(vertexIndex, kVertexAttributeTexCoord0) : 0;
    v.texCoord1 = UnityRayTracingHasVertexAttribute(kVertexAttributeTexCoord1)
        ? UnityRayTracingFetchVertexAttribute4(vertexIndex, kVertexAttributeTexCoord1) : 0;
    v.texCoord2 = UnityRayTracingHasVertexAttribute(kVertexAttributeTexCoord2)
        ? UnityRayTracingFetchVertexAttribute4(vertexIndex, kVertexAttributeTexCoord2) : 0;
    v.texCoord3 = UnityRayTracingHasVertexAttribute(kVertexAttributeTexCoord3)
        ? UnityRayTracingFetchVertexAttribute4(vertexIndex, kVertexAttributeTexCoord3) : 0;
    v.color = UnityRayTracingHasVertexAttribute(kVertexAttributeColor)
        ? UnityRayTracingFetchVertexAttribute4(vertexIndex, kVertexAttributeColor) : 1;
    return v;
}

uint PathTracingGetTriangleIndex()
{
    return GetMeshInfo().indexStart / 3 + PrimitiveIndex();
}

#define PATH_TRACING_INTERPOLATE(a, b) (v0.a * b.x + v1.a * b.y + v2.a * b.z)

PathTracingHitContext PathTracingGetHitContext(AttributeData attributes, IllusionPathPayload payload)
{
    uint3 indices = UnityRayTracingFetchTriangleIndices(PrimitiveIndex());
    PathTracingHitVertex v0 = PathTracingFetchVertex(indices.x);
    PathTracingHitVertex v1 = PathTracingFetchVertex(indices.y);
    PathTracingHitVertex v2 = PathTracingFetchVertex(indices.z);
    float3 bary = float3(1.0 - attributes.barycentrics.x - attributes.barycentrics.y, attributes.barycentrics.x, attributes.barycentrics.y);

    PathTracingHitContext hit;
    hit.indices = indices;
    hit.barycentrics = bary;
    hit.vertex.positionOS = PATH_TRACING_INTERPOLATE(positionOS, bary);
    hit.vertex.normalOS = PATH_TRACING_INTERPOLATE(normalOS, bary);
    hit.vertex.tangentOS = PATH_TRACING_INTERPOLATE(tangentOS, bary);
    hit.vertex.texCoord0 = PATH_TRACING_INTERPOLATE(texCoord0, bary);
    hit.vertex.texCoord1 = PATH_TRACING_INTERPOLATE(texCoord1, bary);
    hit.vertex.texCoord2 = PATH_TRACING_INTERPOLATE(texCoord2, bary);
    hit.vertex.texCoord3 = PATH_TRACING_INTERPOLATE(texCoord3, bary);
    hit.vertex.color = PATH_TRACING_INTERPOLATE(color, bary);

    float3x3 objectToWorld = (float3x3)ObjectToWorld3x4();
    float3x3 worldToObject = (float3x3)WorldToObject3x4();
    float3 rayDir = WorldRayDirection();

    hit.positionWS = WorldRayOrigin() + rayDir * RayTCurrent();
    hit.viewDirWS = -rayDir;

    float3 edge1 = mul(objectToWorld, v1.positionOS - v0.positionOS);
    float3 edge2 = mul(objectToWorld, v2.positionOS - v0.positionOS);
    float3 faceCross = cross(edge1, edge2);
    float worldArea = length(faceCross);
    float3 faceNormalWS = normalize(mul(cross(v1.positionOS - v0.positionOS, v2.positionOS - v0.positionOS), worldToObject));
    hit.frontFacing = dot(faceNormalWS, rayDir) < 0.0;
    hit.faceNormalWS = faceNormalWS;
    hit.vertexNormalWS = SafeNormalize(mul(hit.vertex.normalOS, worldToObject));

    float handedness = determinant(objectToWorld) < 0.0 ? -1.0 : 1.0;
    hit.tangentWS = float4(SafeNormalize(mul(objectToWorld, hit.vertex.tangentOS.xyz)), (hit.vertex.tangentOS.w < 0.0 ? -1.0 : 1.0) * handedness);

    float2 uvEdge1 = v1.texCoord0.xy - v0.texCoord0.xy;
    float2 uvEdge2 = v2.texCoord0.xy - v0.texCoord0.xy;
    float uvArea = abs(uvEdge1.x * uvEdge2.y - uvEdge2.x * uvEdge1.y);
    float coneWidth = abs(payload.coneWidth + payload.coneSpread * RayTCurrent());
    float projection = max(abs(dot(rayDir, faceNormalWS)), 1e-4);
    g_PathTracingUVFootprint = sqrt(uvArea / max(worldArea, 1e-20)) * coneWidth / projection;

    return hit;
}

float3 PathTracingTangentToWorld(float3 normalTS, PathTracingHitContext hit)
{
    float3 n = hit.vertexNormalWS;
    float3 t = hit.tangentWS.xyz;
    float3 b = cross(n, t) * hit.tangentWS.w;
    return SafeNormalize(t * normalTS.x + b * normalTS.y + n * normalTS.z);
}

uint PathTracingHash(uint h)
{
    h ^= h >> 16; h *= 0x7FEB352Du; h ^= h >> 15; h *= 0x846CA68Bu; h ^= h >> 16;
    return h;
}

uint PathTracingHitHash(IllusionPathPayload payload, uint salt)
{
    return PathTracingHash((payload.rayKindAndSeed >> PT_RAY_SEED_SHIFT) ^ (PrimitiveIndex() * 0x9E3779B9u) ^ (InstanceIndex() * 0x85EBCA6Bu) ^ salt);
}

bool PathTracingAcceptCoverage(IllusionPathPayload payload, float alpha)
{
    return float(PathTracingHitHash(payload, 0u) & 0xFFFFFF) / 16777216.0 < alpha;
}

float PathTracingIorFromF0(float f0)
{
    float r = sqrt(saturate(f0));
    return (1.0 + r) / max(1.0 - r, 1e-4);
}

float4 PathTracingPseudoScreenPosition(IllusionPathPayload payload)
{
    uint h = PathTracingHitHash(payload, 0x68E31DA4u);
    return float4(float(h & 0xFFFF) / 65536.0, float(h >> 16) / 65536.0, 0.5, 1.0);
}

float3 PathTracingPreviousPositionWS(PathTracingHitContext hit)
{
    float3 positionOS = hit.vertex.positionOS;
    uint historyBase = _PathTracingInstanceData[InstanceID()].x;
    if (historyBase != PT_NO_POSITION_HISTORY)
    {
        float3 p0 = asfloat(_PathTracingPreviousPositions.Load3((historyBase + hit.indices.x) * 12));
        float3 p1 = asfloat(_PathTracingPreviousPositions.Load3((historyBase + hit.indices.y) * 12));
        float3 p2 = asfloat(_PathTracingPreviousPositions.Load3((historyBase + hit.indices.z) * 12));
        positionOS = p0 * hit.barycentrics.x + p1 * hit.barycentrics.y + p2 * hit.barycentrics.z;
    }
    return mul(unity_MatrixPreviousM, float4(positionOS, 1.0)).xyz;
}

void PathTracingWriteGeometry(inout IllusionPathPayload payload, PathTracingHitContext hit, uint family, uint flags,
    float3 shadingNormalWS, float4 shadingTangentWS)
{
    float3 outsideFace = hit.faceNormalWS;
    payload.hitT = RayTCurrent();
    payload.familyAndFlags = family | flags
        | (hit.frontFacing ? PT_SURFACE_FRONT_FACING : 0u)
        | (shadingTangentWS.w < 0.0 ? PT_SURFACE_BITANGENT_FLIP : 0u);
    payload.normalShading = PathTracingPackNormal(shadingNormalWS);
    payload.tangentShading = PathTracingPackNormal(shadingTangentWS.xyz);
    payload.normalVertex = PathTracingPackNormal(hit.vertexNormalWS);
    payload.normalFace = PathTracingPackNormal(outsideFace);
    payload.instanceID = InstanceID();
    payload.triangleIndex = PathTracingGetTriangleIndex();
    payload.familyParameters.w = PathTracingGetMaterialIndex();
    float3 currentWS = mul(ObjectToWorld3x4(), float4(hit.vertex.positionOS, 1.0));
    payload.motion = PathTracingPackHalf4(float4(PathTracingPreviousPositionWS(hit) - currentWS, 0.0));
}

#define PT_CULL_BACK    1u
#define PT_CULL_FRONT   2u

bool PathTracingIsCulledFace()
{
    uint range = _PathTracingInstanceData[InstanceID()].y;
    uint first = range >> 8u;
    uint end = first + (range & 0xFFu);
    uint indexStart = GetMeshInfo().indexStart;
    uint culled = 0u;
    for (uint i = first; i < end; i++)
    {
        uint2 subMesh = _PathTracingCulledSubMeshes[i];
        if (subMesh.x == indexStart)
        {
            culled = subMesh.y;
            break;
        }
    }
    if (culled == 0u)
        return false;
    uint3 indices = UnityRayTracingFetchTriangleIndices(PrimitiveIndex());
    float3 p0 = UnityRayTracingFetchVertexAttribute3(indices.x, kVertexAttributePosition);
    float3 p1 = UnityRayTracingFetchVertexAttribute3(indices.y, kVertexAttributePosition);
    float3 p2 = UnityRayTracingFetchVertexAttribute3(indices.z, kVertexAttributePosition);
    float3 faceNormalWS = mul(cross(p1 - p0, p2 - p0), (float3x3)WorldToObject3x4());
    bool frontFacing = dot(faceNormalWS, WorldRayDirection()) < 0.0;
    return (culled & (frontFacing ? PT_CULL_FRONT : PT_CULL_BACK)) != 0u;
}

// @IllusionRP: all Unity material passes contribute the nearest exit normal to HDRP random walks.
void PathTracingRecordRandomWalk(inout IllusionPathPayload payload, AttributeData attributes)
{
    if (PathTracingGetRayKind(payload) == PT_RAY_RANDOM_WALK && RayTCurrent() < payload.hitT)
    {
        PathTracingHitContext hit = PathTracingGetHitContext(attributes, payload);
        payload.hitT = RayTCurrent();
        payload.normalVertex = PathTracingPackNormal(hit.vertexNormalWS);
    }
}

#endif
