#ifndef ILLUSION_RAY_TRACED_GI_INITIAL_SAMPLES_INCLUDED
#define ILLUSION_RAY_TRACED_GI_INITIAL_SAMPLES_INCLUDED

// Generates the initial ReSTIR GI sample of every receiver pixel, as the BrdfRayTracing and ShadeSecondarySurfaces passes of
// the RTXDI sample do together: a ray from the receiver's lobes reaches a secondary surface, which path tracing lights
// through its own material model and light sampling. With RAY_TRACED_GI_RADIANCE_CACHE, as SHaRC's render pass does,
// eligible hits take their radiance from the radiance cache and the others continue the path. With
// RAY_TRACED_GI_SKIP_TRANSMISSION, GI rays pass through every surface that transmits shadows.

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

SecondarySample TraceSecondarySample(uint2 pixel, RayTracedGIReceiver receiver, float3 direction, float lobeRoughness)
{
    const float coneSpread = g_Const.ptConsts.camera.PixelConeSpreadAngle;
    PathTracer::WorkingContext workingContext = GetWorkingContext();
    PathState path = PathTracer::EmptyPathInitialize(pixel, coneSpread);
    path.setVertexIndex(1);
    path.setDeltaOnlyPath(false);
    path.setCounter(PackedCounters::DiffuseBounces, 1);
    path.SetOrigin(RayTracedGIRayOrigin(receiver.positionWS, receiver.normalWS, receiver.viewDepth, g_GIConst.view));
    path.SetDir(direction);
    path.rayCone = RayCone::make(receiver.viewDepth * coneSpread, coneSpread);

    SecondarySample secondary;
    secondary.position = path.GetOrigin() + direction * g_GIConst.view.environmentDistance;
    secondary.normal = -direction;
    secondary.environment = true;
#if defined(RAY_TRACED_GI_RADIANCE_CACHE)
    // SHaRC's spread estimate: the perceptual roughness of every specular lobe on the path, plus one for every diffuse lobe.
    float pathRoughness = lobeRoughness;
#endif

    while (path.isActive())
    {
        RayDesc ray = path.getScatterRay().toRayDesc();
        ray.TMin = 0.0;
        ray.TMax = kMaxRayTravel;
        const bool firstBounce = path.getVertexIndex() == 1;
        IllusionPathPayload payload = PathTracingCreatePayload(PT_RAY_SCATTER,
            Hash32Combine(path.GetId(), Bridge::getSampleIndex() * 0x9E3779B9u + path.getVertexIndex()),
            path.rayCone.getWidth(), path.rayCone.getSpreadAngle());
#if defined(RAY_TRACED_GI_SKIP_TRANSMISSION)
        PathTracingMarkSkipTransmission(payload);
#endif
        TraceRay(SceneBVH, RAY_FLAG_NONE, PT_INSTANCE_SCENE, 0, 1, 0, ray, payload);

        if (payload.hitT == PT_NO_SURFACE_T)
        {
            PathTracer::HandleDiagnosticHit(path, ray.Origin, ray.Direction, workingContext);
        }
        else if (payload.hitT < 0.0)
        {
            if (firstBounce)
            {
                // Raster lighting gives the receiver no environment term, so the lighting environment counts in full,
                // without the directional discs that raster lights directly.
                EnvMap envMap = Bridge::CreateEnvMap();
                path.SetL(path.GetL() + float4(envMap.EvalLocal(envMap.ToLocal(ray.Direction), 0), 0));
                path.terminate();
            }
            else
            {
                PathTracer::HandleMiss(path, ray.Origin, ray.Direction, kMaxRayTravel, workingContext);
            }
        }
        else
        {
            // Raster lights the receiver directly from the emitters of its lights.
            if (firstBounce && PathTracingHasSurfaceFlag(payload, PT_SURFACE_LIGHT_EMITTER))
                payload.emissionMetallic = PathTracingPackHalf4(float4(0.0, 0.0, 0.0, PathTracingUnpackHalf4(payload.emissionMetallic).a));
            const float3 faceNormal = PathTracingUnpackNormal(payload.normalFace);
            const float3 hitPosition = ray.Origin + ray.Direction * payload.hitT;
            const float3 hitNormal = dot(faceNormal, ray.Direction) < 0.0 ? faceNormal : -faceNormal;
            const bool overlay = PathTracingGetFamily(payload) == PT_FAMILY_OVERLAY;
            if (firstBounce && !overlay)
            {
                secondary.position = hitPosition;
                secondary.normal = hitNormal;
                secondary.environment = false;
            }
#if defined(RAY_TRACED_GI_RADIANCE_CACHE)
            // Emissive hits stay path tracing vertices, so that their emission keeps its weight against light sampling.
            float3 cachedRadiance;
            if (!overlay && !any(PathTracingUnpackHalf4(payload.emissionMetallic).rgb > 0.0)
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
    }
    secondary.radiance = path.GetL().rgb;
    // As the RTXDI sample's secondary shading does, clamp the luminance of the sample, here relative to the radiance that
    // exposure maps to middle gray, so that resampling does not spread fireflies.
    float maxRadiance = g_GIConst.radianceClamp * workingContext.PtConsts.preExposedGrayLuminance;
    float luminance = dot(secondary.radiance, float3(0.2126, 0.7152, 0.0722));
    if (maxRadiance > 0.0 && luminance > maxRadiance)
        secondary.radiance *= maxRadiance / luminance;
    return secondary;
}

[shader("raygeneration")]
void RayGen_InitialSamples()
{
    const uint2 reservoirPosition = DispatchRaysIndex().xy;
    const uint2 pixel = RTXDI_ReservoirPosToPixelPos(reservoirPosition, g_GIConst.runtimeParams.activeCheckerboardField);
    const RTXDI_GIParameters restirGI = g_GIConst.restirGI;
    const uint pointer = RTXDI_ReservoirPositionToPointer(restirGI.reservoirBufferParams, reservoirPosition, 0);

    SecondaryGBufferData record = (SecondaryGBufferData)0;
    RTXDI_GIReservoir reservoir = RTXDI_EmptyGIReservoir();

    RayTracedGIReceiver receiver = LoadRayTracedGIReceiver(pixel, g_GIConst.view);
    if (receiver.valid)
    {
        RTXDI_RandomSamplerState rng = RTXDI_InitRandomSampler(pixel, g_GIConst.runtimeParams.frameIndex, RTXDI_GI_GENERATE_INITIAL_SAMPLES_RANDOM_SEED);
        float pdf;
        float lobeRoughness;
        float3 direction = SampleReceiverLobes(rng, receiver, g_GIConst.specular != 0, pdf, lobeRoughness);
        if (pdf > 0.0)
        {
            SecondarySample secondary = TraceSecondarySample(pixel, receiver, direction, lobeRoughness);
            uint flags = secondary.environment ? kSecondaryGBuffer_IsEnvironmentMap : 0u;
            record.worldPos = secondary.position;
            record.normal = ndirToOctUnorm32(secondary.normal);
            record.throughputAndFlags = Pack_R16G16B16A16_FLOAT(float4(1.0, 1.0, 1.0, 0.0));
            record.throughputAndFlags.y |= flags << 16;
            record.emission = secondary.radiance;
            record.pdf = pdf;
            reservoir = RTXDI_MakeGIReservoir(secondary.position, secondary.normal, secondary.radiance, pdf);
        }
        else
        {
            // A reflection below the surface contributes nothing, but it still counts as a sample, so that resampling
            // averages over every sample the lobes generate, as the RTXDI sample's reservoirs do.
            reservoir = RTXDI_MakeGIReservoir(receiver.positionWS + direction * g_GIConst.view.environmentDistance, -direction, 0.0, 0.0);
        }
    }

    u_SecondaryGBuffer[pointer] = record;
    RTXDI_StoreGIReservoir(reservoir, restirGI.reservoirBufferParams, reservoirPosition, restirGI.bufferIndices.secondarySurfaceReSTIRDIOutputBufferIndex);
}

[shader("miss")]
void Miss_InitialSamples(inout IllusionPathPayload payload : SV_RayPayload)
{
    payload.hitT = PT_MISS_T;
}

#endif
