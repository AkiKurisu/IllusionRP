// Directional lights are sampled apart from the RTXPT light list, as HDRP samples its distant lights: one light is
// picked uniformly per NEE sample, its cone is sampled when it has an angular size, and BSDF rays that miss the
// scene add the cone they reach with the matching MIS weight.

bool PathTracingIsDeltaDirectional(PathTracingDistantLight light)
{
    return cos(light.angularDiameter * 0.5) >= 1.0;
}

void SampleDirectionalNEE(inout NEEResult result, const PathState path, const ShadingData shadingData,
    const ActiveBSDF bsdf, uint fullSamples, inout UniformSampleSequenceGenerator generator, const WorkingContext context)
{
    if (_PathTracingDistantLightCount == 0)
        return;
    float3 random = float3(sampleNext2D(generator), sampleNext1D(generator));
    uint index = min((uint)(random.z * _PathTracingDistantLightCount), _PathTracingDistantLightCount - 1);
    PathTracingDistantLight light = _PathTracingDistantLights[index];
    if (!PathTracingLightAffects(light.targetIndex, shadingData.instanceID))
        return;

    float selection = rcp((float)_PathTracingDistantLightCount);
    float3 direction;
    float pdf;
    if (PathTracingIsDeltaDirectional(light))
    {
        direction = -light.forward;
        pdf = selection;
    }
    else
    {
        float rcpPdf;
        Illusion::SampleCone(random.xy, cos(light.angularDiameter * 0.5), direction, rcpPdf);
        direction = normalize(direction.x * normalize(light.right) + direction.y * normalize(light.up) - direction.z * light.forward);
        pdf = selection / rcpPdf;
    }

    float scatterPdf = bsdf.evalPdf(shadingData, direction, kUseBSDFSampling);
    float mis = PathTracingIsDeltaDirectional(light) ? 1.0 : EvalMIS(RTXPT_NEE_MIS_HEURISTIC, fullSamples, pdf, 1, scatterPdf);

    LightSample sample = (LightSample)0;
    sample.Direction = direction;
    sample.Distance = FLT_MAX;
    RayDesc ray = ComputeVisibilityRay(sample, shadingData, bsdf);
    float3 transmission = Bridge::traceVisibilityRay(ray, path.rayCone, path.getVertexIndex(), context.Debug, path.GetPixelPos(), light.targetIndex);
    if (all(transmission <= 0.0))
        return;

    float fade = shadingData.shadowNoLFadeout > 0
        ? ComputeLowGrazingAngleFalloff(direction, shadingData.vertexN, shadingData.shadowNoLFadeout, 2.0 * shadingData.shadowNoLFadeout) : 1.0;
    float3 Li = transmission * light.color * (mis * fade / (selection * fullSamples));
    float4 response = bsdf.eval(shadingData, direction);
    float3 radiance = response.rgb * Li;
#if RTXPT_FIREFLY_FILTER && PATH_TRACER_MODE!=PATH_TRACER_MODE_BUILD_STABLE_PLANES
    if (context.PtConsts.fireflyFilterThreshold != 0)
    {
        float filterK = ComputeNewScatterFireflyFilterK(path.GetFireflyFilterK(), pdf, 1);
        radiance *= FireflyFilterShort(Average(radiance), context.PtConsts.fireflyFilterThreshold, filterK);
    }
#endif
    result.AccumulateRadiance(radiance * path.GetThp(), response.w * Average(Li) * Average(path.GetThp()));
}

float3 EvaluateDirectionalMiss(float3 rayDirection, float bsdfPdf, NEEBSDFMISInfo misInfo, uint receiverInstanceID)
{
    float3 value = 0;
    for (uint i = 0; i < _PathTracingDistantLightCount; i++)
    {
        PathTracingDistantLight light = _PathTracingDistantLights[i];
        if (PathTracingIsDeltaDirectional(light) || !PathTracingLightAffects(light.targetIndex, receiverInstanceID))
            continue;
        float cosHalfAngle = cos(light.angularDiameter * 0.5);
        if (-dot(rayDirection, light.forward) < cosHalfAngle)
            continue;
        float rcpPdf = TWO_PI * (1.0 - cosHalfAngle);
        float pdf = rcp((float)_PathTracingDistantLightCount) / rcpPdf;
        float mis = misInfo.LightSamplingEnabled && bsdfPdf > 0 ? EvalMIS(RTXPT_NEE_MIS_HEURISTIC, 1, bsdfPdf, misInfo.FullSamples, pdf) : 1.0;
        value += light.color * (mis / rcpPdf);
    }
    return value;
}
