#ifndef ILLUSION_RAY_TRACED_GI_INITIAL_SAMPLES_INCLUDED
#define ILLUSION_RAY_TRACED_GI_INITIAL_SAMPLES_INCLUDED

// Generates the initial ReSTIR GI sample of every receiver pixel: a ray from the receiver's lobes reaches a secondary
// surface, which path tracing lights through its own material model and light sampling. With
// RAY_TRACED_GI_RADIANCE_CACHE, eligible hits take their radiance from the radiance cache and the others continue the
// path. With RAY_TRACED_GI_SKIP_TRANSMISSION, GI rays pass through every surface that transmits shadows.

#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/PathTracing/PathTracingRayGenConfig.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/PathTracing/RTXPT/PathTracer/Config.h"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/PathTracing/RTXPT/PathTracer/PathTracerTypes.hlsli"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/PathTracing/RTXPT/PathTracerBridgeUnity.hlsli"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/PathTracing/RTXPT/PathTracer/PathTracer.hlsli"

#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/RayTracedGlobalIllumination/RayTracedGIReceiver.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/RayTracedGlobalIllumination/RayTracedGISecondarySurface.hlsl"
#define RTXDI_GI_RESERVOIR_BUFFER u_GIReservoirs
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/RTXDI/Include/Rtxdi/GI/Reservoir.hlsli"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/RTXDI/Include/Rtxdi/Utils/RandomSamplerState.hlsli"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/RayTracedGlobalIllumination/RayTracedGIReceiverSampling.hlsl"
#if defined(RAY_TRACED_GI_RADIANCE_CACHE)
#define SHARC_QUERY 1
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/RayTracedGlobalIllumination/RayTracedGIRadianceCache.hlsl"
#endif

#define g_GIConst (t_RayTracedGIConstants[0])

struct SecondarySample
{
    float3 position;
    float3 normal;
    float3 radiance;
    bool environment;
};

void BeginSecondarySample(uint2 pixel, RayTracedGIReceiver receiver, float3 direction, out PathState path, out SecondarySample secondary)
{
    const float coneSpread = g_GIConst.view.pixelConeSpreadAngle;
    path = PathTracer::EmptyPathInitialize(pixel, coneSpread);
    path.setVertexIndex(1);
    path.setDeltaOnlyPath(false);
    path.setCounter(PackedCounters::DiffuseBounces, 1);
    path.SetOrigin(RayTracedGIRayOrigin(receiver.positionWS, receiver.normalWS, receiver.viewDepth, g_GIConst.view));
    path.SetDir(direction);
    path.rayCone = RayCone::make(g_GIConst.view.pixelConeWidth + receiver.viewDepth * coneSpread, coneSpread);

    secondary.position = path.GetOrigin() + direction * g_GIConst.view.environmentDistance;
    secondary.normal = -direction;
    secondary.radiance = 0.0;
    secondary.environment = true;
}

IllusionPathPayload CreateSecondaryPayload(PathState path)
{
    IllusionPathPayload payload = PathTracingCreatePayload(PT_RAY_SCATTER,
        Hash32Combine(path.GetId(), Bridge::getSampleIndex() * 0x9E3779B9u + path.getVertexIndex()),
        path.rayCone.getWidth(), path.rayCone.getSpreadAngle());
#if defined(RAY_TRACED_GI_SKIP_TRANSMISSION)
    PathTracingMarkSkipTransmission(payload);
#endif
    return payload;
}

RayDesc SecondaryRay(PathState path)
{
    RayDesc ray = path.getScatterRay().toRayDesc();
    ray.TMin = 0.0;
    ray.TMax = kMaxRayTravel;
    return ray;
}

// Raster lighting gives the receiver no environment term, so the lighting environment counts in full, without the
// directional discs that raster lights directly.
void HandleFirstBounceMiss(inout PathState path, float3 direction)
{
    EnvMap envMap = Bridge::CreateEnvMap();
    path.SetL(path.GetL() + float4(envMap.EvalLocal(envMap.ToLocal(direction), 0), 0));
    path.terminate();
}

// Raster lights the receiver directly from the emitters of its lights.
void PrepareSecondaryHit(inout IllusionPathPayload payload, RayDesc ray, bool firstBounce, inout SecondarySample secondary)
{
    if (!firstBounce)
        return;
    if (PathTracingHasSurfaceFlag(payload, PT_SURFACE_LIGHT_EMITTER))
        payload.emissionMetallic = PathTracingPackHalf4(float4(0.0, 0.0, 0.0, PathTracingUnpackHalf4(payload.emissionMetallic).a));
    if (PathTracingGetFamily(payload) == PT_FAMILY_OVERLAY)
        return;
    const float3 faceNormal = PathTracingUnpackNormal(payload.normalFace);
    secondary.position = ray.Origin + ray.Direction * payload.hitT;
    secondary.normal = dot(faceNormal, ray.Direction) < 0.0 ? faceNormal : -faceNormal;
    secondary.environment = false;
}

bool TraceToSecondaryHit(inout PathState path, inout SecondarySample secondary, out RayDesc ray, out IllusionPathPayload payload,
    PathTracer::WorkingContext workingContext)
{
    ray = (RayDesc)0;
    payload = (IllusionPathPayload)0;
    while (path.isActive())
    {
        ray = SecondaryRay(path);
        const bool firstBounce = path.getVertexIndex() == 1;
        payload = CreateSecondaryPayload(path);
        TraceRay(SceneBVH, RAY_FLAG_NONE, PT_INSTANCE_SCENE, 0, 1, 0, ray, payload);

        if (payload.hitT == PT_NO_SURFACE_T)
        {
            PathTracer::HandleDiagnosticHit(path, ray.Origin, ray.Direction, workingContext);
        }
        else if (payload.hitT < 0.0)
        {
            if (firstBounce)
                HandleFirstBounceMiss(path, ray.Direction);
            else
                PathTracer::HandleMiss(path, ray.Origin, ray.Direction, kMaxRayTravel, workingContext);
        }
        else
        {
            PrepareSecondaryHit(payload, ray, firstBounce, secondary);
            if (PathTracingGetFamily(payload) == PT_FAMILY_OVERLAY)
            {
                PathTracer::HandleOverlayHit(path, ray.Origin, ray.Direction, payload.hitT, payload, workingContext);
                continue;
            }
            return true;
        }
    }
    return false;
}

// Clamps the luminance of the sample relative to the radiance that exposure maps to middle gray, so that resampling
// does not spread fireflies.
float3 SecondaryRadiance(PathState path, PathTracer::WorkingContext workingContext)
{
    float3 radiance = path.GetL().rgb;
    float maxRadiance = g_GIConst.radianceClamp * workingContext.PtConsts.preExposedGrayLuminance;
    float luminance = dot(radiance, float3(0.2126, 0.7152, 0.0722));
    if (maxRadiance > 0.0 && luminance > maxRadiance)
        radiance *= maxRadiance / luminance;
    return radiance;
}

#define RAY_TRACED_GI_SAMPLE_NONE 0u
#define RAY_TRACED_GI_SAMPLE_BELOW_SURFACE 1u
#define RAY_TRACED_GI_SAMPLE_TRACED 2u

void StoreInitialSample(uint2 reservoirPosition, uint kind, SecondarySample secondary, float pdf)
{
    const RTXDI_GIParameters restirGI = g_GIConst.restirGI;
    SecondaryGBufferData record = (SecondaryGBufferData)0;
    RTXDI_GIReservoir reservoir = RTXDI_EmptyGIReservoir();
    if (kind == RAY_TRACED_GI_SAMPLE_TRACED)
    {
        uint flags = secondary.environment ? kSecondaryGBuffer_IsEnvironmentMap : 0u;
        record.worldPos = secondary.position;
        record.normal = ndirToOctUnorm32(secondary.normal);
        record.throughputAndFlags = Pack_R16G16B16A16_FLOAT(float4(1.0, 1.0, 1.0, 0.0));
        record.throughputAndFlags.y |= flags << 16;
        record.emission = secondary.radiance;
        record.pdf = pdf;
        reservoir = RTXDI_MakeGIReservoir(secondary.position, secondary.normal, secondary.radiance, pdf);
    }
    else if (kind == RAY_TRACED_GI_SAMPLE_BELOW_SURFACE)
    {
        // A reflection below the surface contributes nothing, but it still counts as a sample, so that resampling
        // averages over every sample the lobes generate.
        reservoir = RTXDI_MakeGIReservoir(secondary.position, secondary.normal, 0.0, 0.0);
    }
    u_SecondaryGBuffer[RTXDI_ReservoirPositionToPointer(restirGI.reservoirBufferParams, reservoirPosition, 0)] = record;
    RTXDI_StoreGIReservoir(reservoir, restirGI.reservoirBufferParams, reservoirPosition, restirGI.bufferIndices.secondarySurfaceReSTIRDIOutputBufferIndex);
}

bool SampleInitialRay(uint2 pixel, out RayTracedGIReceiver receiver, out float3 direction, out float pdf, out float lobeRoughness)
{
    receiver = LoadRayTracedGIReceiver(pixel, g_GIConst.view);
    direction = 0.0;
    pdf = 0.0;
    lobeRoughness = 0.0;
    if (!receiver.valid)
        return false;
    RTXDI_RandomSamplerState rng = RTXDI_InitRandomSampler(pixel, g_GIConst.runtimeParams.frameIndex, RTXDI_GI_GENERATE_INITIAL_SAMPLES_RANDOM_SEED);
    direction = SampleReceiverLobes(rng, receiver, g_GIConst.specular != 0, pdf, lobeRoughness);
    return true;
}

SecondarySample BelowSurfaceSample(RayTracedGIReceiver receiver, float3 direction)
{
    SecondarySample secondary = (SecondarySample)0;
    secondary.position = receiver.positionWS + direction * g_GIConst.view.environmentDistance;
    secondary.normal = -direction;
    return secondary;
}

#if USE_RAY_QUERY

bool RayTracedGIInDispatch(uint2 reservoirPosition)
{
    const uint2 viewport = uint2(g_GIConst.view.viewportSize);
    const uint dispatchWidth = g_GIConst.runtimeParams.activeCheckerboardField != 0 ? (viewport.x + 1) / 2 : viewport.x;
    return reservoirPosition.x < dispatchWidth && reservoirPosition.y < viewport.y;
}

[numthreads(8, 4, 1)]
void RayTracedGIInitialSamples(uint2 reservoirPosition : SV_DispatchThreadID)
{
    if (!RayTracedGIInDispatch(reservoirPosition))
        return;
    const uint2 pixel = RTXDI_ReservoirPosToPixelPos(reservoirPosition, g_GIConst.runtimeParams.activeCheckerboardField);
    RayTracedGIReceiver receiver;
    float3 direction;
    float pdf;
    float lobeRoughness;
    if (!SampleInitialRay(pixel, receiver, direction, pdf, lobeRoughness))
    {
        StoreInitialSample(reservoirPosition, RAY_TRACED_GI_SAMPLE_NONE, (SecondarySample)0, 0.0);
        return;
    }
    if (!(pdf > 0.0))
    {
        StoreInitialSample(reservoirPosition, RAY_TRACED_GI_SAMPLE_BELOW_SURFACE, BelowSurfaceSample(receiver, direction), 0.0);
        return;
    }

    PathState path;
    SecondarySample secondary;
    BeginSecondarySample(pixel, receiver, direction, path, secondary);
    RayDesc ray = SecondaryRay(path);
    IllusionPathPayload payload = CreateSecondaryPayload(path);
    PathTracingInlineHit hit;
    PathTracer::WorkingContext workingContext = GetWorkingContext();
    if (!PathTracingInlineTraceHit(SceneBVH, RAY_FLAG_NONE, PT_INSTANCE_SCENE, ray, payload, hit))
        HandleFirstBounceMiss(path, ray.Direction);
    else
    {
        uint evaluator = PathTracingInlineLoadGeometry(hit.instanceID, hit.geometryIndex);
        if (evaluator == PT_INLINE_NO_EVALUATOR)
            PathTracer::HandleDiagnosticHit(path, ray.Origin, ray.Direction, workingContext);
        else
        {
            payload = CreateSecondaryPayload(path);
            AttributeData attributes;
            PathTracingInlineSetHit(hit, ray, attributes);
            PathTracingInlineEvaluateClosestHit(evaluator, payload, attributes);
            secondary = (SecondarySample)0;
            PrepareSecondaryHit(payload, ray, true, secondary);
            PathTracer::HandleHit(path, ray.Origin, ray.Direction, payload.hitT, payload, workingContext);
        }
    }
    secondary.radiance = SecondaryRadiance(path, workingContext);
    StoreInitialSample(reservoirPosition, RAY_TRACED_GI_SAMPLE_TRACED, secondary, pdf);
}

#else

SecondarySample TraceSecondarySample(uint2 pixel, RayTracedGIReceiver receiver, float3 direction, float lobeRoughness)
{
    PathTracer::WorkingContext workingContext = GetWorkingContext();
    PathState path;
    SecondarySample secondary;
    BeginSecondarySample(pixel, receiver, direction, path, secondary);
#if defined(RAY_TRACED_GI_RADIANCE_CACHE)
    // The spread estimate: the perceptual roughness of every specular lobe on the path, plus one for every diffuse lobe.
    float pathRoughness = lobeRoughness;
#endif
    RayDesc ray;
    IllusionPathPayload payload;
    while (TraceToSecondaryHit(path, secondary, ray, payload, workingContext))
    {
#if defined(RAY_TRACED_GI_RADIANCE_CACHE)
        // Emissive hits stay path tracing vertices, so that their emission keeps its weight against light sampling.
        const float3 faceNormal = PathTracingUnpackNormal(payload.normalFace);
        const float3 hitPosition = ray.Origin + ray.Direction * payload.hitT;
        const float3 hitNormal = dot(faceNormal, ray.Direction) < 0.0 ? faceNormal : -faceNormal;
        float3 cachedRadiance;
        if (!any(PathTracingUnpackHalf4(payload.emissionMetallic).rgb > 0.0)
            && !PathTracingHasSurfaceFlag(payload, PT_SURFACE_LIGHT_EMITTER)
            && RayTracedGIQueryCache(hitPosition, hitNormal, payload.hitT, pathRoughness, cachedRadiance))
        {
            path.SetL(path.GetL() + float4(path.GetThp() * cachedRadiance, 0.0));
            path.terminate();
            break;
        }
        const uint diffuseBounces = path.getCounter(PackedCounters::DiffuseBounces);
#endif
        PathTracer::HandleHit(path, ray.Origin, ray.Direction, payload.hitT, payload, workingContext);
#if defined(RAY_TRACED_GI_RADIANCE_CACHE)
        pathRoughness += path.getCounter(PackedCounters::DiffuseBounces) != diffuseBounces
            ? 1.0 : PathTracingUnpackHalf4(payload.specularRoughness).a;
#endif
    }
    secondary.radiance = SecondaryRadiance(path, workingContext);
    return secondary;
}

[shader("raygeneration")]
void RayGen_InitialSamples()
{
    const uint2 reservoirPosition = DispatchRaysIndex().xy;
    const uint2 pixel = RTXDI_ReservoirPosToPixelPos(reservoirPosition, g_GIConst.runtimeParams.activeCheckerboardField);
    RayTracedGIReceiver receiver;
    float3 direction;
    float pdf;
    float lobeRoughness;
    if (!SampleInitialRay(pixel, receiver, direction, pdf, lobeRoughness))
        StoreInitialSample(reservoirPosition, RAY_TRACED_GI_SAMPLE_NONE, (SecondarySample)0, 0.0);
    else if (pdf > 0.0)
        StoreInitialSample(reservoirPosition, RAY_TRACED_GI_SAMPLE_TRACED, TraceSecondarySample(pixel, receiver, direction, lobeRoughness), pdf);
    else
        StoreInitialSample(reservoirPosition, RAY_TRACED_GI_SAMPLE_BELOW_SURFACE, BelowSurfaceSample(receiver, direction), 0.0);
}

[shader("miss")]
void Miss_InitialSamples(inout IllusionPathPayload payload : SV_RayPayload)
{
    payload.hitT = PT_MISS_T;
}

#endif

#endif
