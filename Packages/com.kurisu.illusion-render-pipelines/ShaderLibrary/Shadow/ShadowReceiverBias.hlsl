#ifndef ILLUSION_SHADOW_RECEIVER_BIAS_INCLUDED
#define ILLUSION_SHADOW_RECEIVER_BIAS_INCLUDED

float3 EvalShadow_NormalBiasOrtho(float worldTexelSize, float normalBiasFactor, float3 normalWS)
{
    return normalWS * normalBiasFactor * worldTexelSize;
}

float3 EvalShadow_NormalBiasProj(float worldTexelSize, float normalBiasFactor, float3 normalWS, float L_dist)
{
    return EvalShadow_NormalBiasOrtho(worldTexelSize, normalBiasFactor, normalWS) * L_dist;
}

#endif
