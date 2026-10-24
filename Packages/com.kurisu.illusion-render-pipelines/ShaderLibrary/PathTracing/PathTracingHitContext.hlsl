#ifndef ILLUSION_PATH_TRACING_HIT_CONTEXT_INCLUDED
#define ILLUSION_PATH_TRACING_HIT_CONTEXT_INCLUDED

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

#if defined(PATH_TRACING_INLINE)
static PathTracingHitContext g_PathTracingInlineCachedHit;
static float2 g_PathTracingInlineFootprintGeometry;
static bool g_PathTracingInlineHitContextReady;
#endif

#endif
