#define PATH_TRACER_MODE PATH_TRACER_MODE_REFERENCE
#define RTXPT_PIPELINE_PERMUTATION_NAME GI
#define PT_BOUNCE_COUNT 9
#define PT_DIFFUSE_BOUNCE_COUNT 9
#define PT_STANDARD_LIT
// Avoid caching sharp specular highlights.
#define PT_MIN_PERCEPTUAL_ROUGHNESS 0.4
// SHaRC needs unmodified segment throughput.
#define PT_ENABLE_RUSSIAN_ROULETTE 0
#define PT_SEPARATE_SURFACE_EMISSION
#define SHARC_UPDATE 1

#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/PathTracing/PathTracingRayGenConfig.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/PathTracing/RTXPT/PathTracer/Config.h"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/PathTracing/RTXPT/PathTracer/PathTracerTypes.hlsli"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/PathTracing/RTXPT/PathTracerBridgeUnity.hlsli"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/PathTracing/RTXPT/PathTracer/PathTracer.hlsli"

#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/RayTracedGlobalIllumination/RayTracedGIReceiverSampling.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/RayTracedGlobalIllumination/RayTracedGIRadianceCache.hlsl"

#define g_GIConst (t_RayTracedGIConstants[0])

static const uint kRayTracedGICacheUpdateBlockSize = 5;
static const uint kRayTracedGICacheUpdateRandomSeed = 31;

void RayGen_RadianceCacheUpdate(uint2 block)
{
    if (any(block >= (uint2(g_GIConst.view.viewportSize) + kRayTracedGICacheUpdateBlockSize - 1) / kRayTracedGICacheUpdateBlockSize))
        return;

    RTXDI_RandomSamplerState rng = RTXDI_InitRandomSampler(block, g_GIConst.runtimeParams.frameIndex, kRayTracedGICacheUpdateRandomSeed);
    const uint2 offset = min(uint2(float2(RTXDI_GetNextRandom(rng), RTXDI_GetNextRandom(rng)) * kRayTracedGICacheUpdateBlockSize),
        kRayTracedGICacheUpdateBlockSize - 1);
    const uint2 pixel = block * kRayTracedGICacheUpdateBlockSize + offset;

    RayTracedGIReceiver receiver = LoadRayTracedGIReceiver(pixel, g_GIConst.view);
    if (!receiver.valid)
        return;
    float pdf;
    float lobeRoughness;
    float3 direction = SampleReceiverLobes(rng, receiver, g_GIConst.specular != 0, pdf, lobeRoughness);
    if (!(pdf > 0.0))
        return;

    const SharcParameters sharcParameters = RayTracedGICacheParameters();
    SharcState sharcState;
    SharcInit(sharcState);

    const float coneSpread = g_GIConst.view.pixelConeSpreadAngle;
    PathTracer::WorkingContext workingContext = GetWorkingContext();
    PathState path = PathTracer::EmptyPathInitialize(pixel, coneSpread);
    path.setVertexIndex(1);
    path.setDeltaOnlyPath(false);
    path.setCounter(PackedCounters::DiffuseBounces, 1);
    path.SetOrigin(RayTracedGIRayOrigin(receiver.positionWS, receiver.normalWS, receiver.viewDepth, g_GIConst.view));
    path.SetDir(direction);
    path.rayCone = RayCone::make(g_GIConst.view.pixelConeWidth + receiver.viewDepth * coneSpread, coneSpread);

    while (path.isActive())
    {
        RayDesc ray = path.getScatterRay().toRayDesc();
        ray.TMin = 0.0;
        ray.TMax = kMaxRayTravel;
        const bool firstBounce = path.getVertexIndex() == 1;
        IllusionPathPayload payload = PathTracingCreatePayload(PT_RAY_SCATTER,
            Hash32Combine(path.GetId(), Bridge::getSampleIndex() * 0x9E3779B9u + path.getVertexIndex()),
            path.rayCone.getWidth(), path.rayCone.getSpreadAngle());
        TraceRay(SceneBVH, RAY_FLAG_NONE, PT_INSTANCE_SCENE, 0, 1, 0, ray, payload);

        path.SetThp(1.0);
        path.SetL(0.0);
        if (payload.hitT == PT_NO_SURFACE_T)
            break;
        if (payload.hitT < 0.0)
        {
            // The receiver is not cached, so a miss from it updates nothing.
            if (!firstBounce)
            {
                PathTracer::HandleMiss(path, ray.Origin, ray.Direction, kMaxRayTravel, workingContext);
                SharcUpdateMiss(sharcParameters, sharcState, path.GetL().rgb);
            }
            break;
        }
        // Skip emission-only terminal vertices.
        if (path.isTerminatingAtNextBounce())
            break;

        // Raster lights the receiver directly from the emitters of its lights.
        if (firstBounce && PathTracingHasSurfaceFlag(payload, PT_SURFACE_LIGHT_EMITTER))
            payload.emissionMetallic = PathTracingPackHalf4(float4(0.0, 0.0, 0.0, PathTracingUnpackHalf4(payload.emissionMetallic).a));
        const uint vertexIndex = path.getVertexIndex();
        const float3 faceNormal = PathTracingUnpackNormal(payload.normalFace);
        SharcHitData sharcHitData;
        sharcHitData.positionWorld = ray.Origin + ray.Direction * payload.hitT;
        sharcHitData.normalWorld = dot(faceNormal, ray.Direction) < 0.0 ? faceNormal : -faceNormal;
        const bool cacheable = PathTracingGetFamily(payload) != PT_FAMILY_OVERLAY && path.interiorList.isEmpty();
        const float sharcRandom = cacheable ? RTXDI_GetNextRandom(rng) : 0.0;
        if (cacheable && RayTracedGICacheEndsPath(sharcParameters, sharcState, sharcHitData, sharcRandom))
            path.setTerminateAtNextBounce();
        g_PathSurfaceEmission = 0.0;
        PathTracer::HandleHit(path, ray.Origin, ray.Direction, payload.hitT, payload, workingContext);
        if (path.getVertexIndex() == vertexIndex)
            continue;

        sharcHitData.emissive = g_PathSurfaceEmission;
        if (!SharcUpdateHit(sharcParameters, sharcState, sharcHitData, path.GetL().rgb, cacheable ? sharcRandom : RTXDI_GetNextRandom(rng)))
            break;
        SharcSetThroughput(sharcState, path.GetThp());
    }
}
