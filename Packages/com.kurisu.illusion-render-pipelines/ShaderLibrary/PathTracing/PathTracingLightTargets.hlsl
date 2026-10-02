#ifndef ILLUSION_PATH_TRACING_LIGHT_TARGETS_INCLUDED
#define ILLUSION_PATH_TRACING_LIGHT_TARGETS_INCLUDED

#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/PathTracing/PathTracingPayload.hlsl"

// Index 0 is the target of emissive surfaces and the environment: every receiver, shadows from every caster.
struct PathTracingLightTarget
{
    uint renderingLayers;
    uint shadowLayers;
};
StructuredBuffer<PathTracingLightTarget> _PathTracingLightTargets;

struct PathTracingDistantLight
{
    float3 forward;
    float angularDiameter;
    float3 right;
    uint targetIndex;
    float3 up;
    float padding0;
    float3 color;
    float padding1;
};
StructuredBuffer<PathTracingDistantLight> _PathTracingDistantLights;
uint _PathTracingDistantLightCount;

bool PathTracingLightAffects(uint targetIndex, uint receiverInstanceID)
{
    if (targetIndex == 0 || receiverInstanceID == PT_NO_INSTANCE)
        return true;
    return (_PathTracingLightTargets[targetIndex].renderingLayers & _PathTracingInstanceData[receiverInstanceID].renderingLayers) != 0;
}

#endif
