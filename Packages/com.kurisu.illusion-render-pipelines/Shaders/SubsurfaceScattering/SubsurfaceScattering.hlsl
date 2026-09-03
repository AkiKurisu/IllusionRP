#ifndef SUBSURFACE_SCATTERING_INCLUDED
#define SUBSURFACE_SCATTERING_INCLUDED

//--------------------------------------------------------------------------------------------------
// Included headers
//--------------------------------------------------------------------------------------------------

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/ImageBasedLighting.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/Shaders/SubsurfaceScattering/ShaderVariablesSubsurface.hlsl"

//--------------------------------------------------------------------------------------------------
// Inputs & outputs
//--------------------------------------------------------------------------------------------------

#if !defined(SHADER_STAGE_COMPUTE) && defined(_ILLUSION_RENDER_PASS_ENABLED)
    #define SUBSURFACE_DIFFUSE  0
    #define SUBSURFACE_ALBEDO	1

    FRAMEBUFFER_INPUT_HALF(SUBSURFACE_DIFFUSE);
    FRAMEBUFFER_INPUT_HALF(SUBSURFACE_ALBEDO);
#else
    TEXTURE2D(_SubsurfaceDiffuse);
    SAMPLER(sampler_SubsurfaceDiffuse);
            
    TEXTURE2D(_SubsurfaceAlbedo);
    SAMPLER(sampler_SubsurfaceAlbedo);
#endif

int _SssSampleBudget;

//--------------------------------------------------------------------------------------------------
// Definitions
//--------------------------------------------------------------------------------------------------

// Do not modify these.
#define GROUP_SIZE_1D               16
#define GROUP_SIZE_2D               (GROUP_SIZE_1D * GROUP_SIZE_1D)
#define TEXTURE_CACHE_BORDER        2
#define TEXTURE_CACHE_SIZE_1D       (GROUP_SIZE_1D + 2 * TEXTURE_CACHE_BORDER)
#define TEXTURE_CACHE_SIZE_2D       (TEXTURE_CACHE_SIZE_1D * TEXTURE_CACHE_SIZE_1D)

#define SSS_PIXELS_PER_SAMPLE       4
#define SSS_BILATERAL_FILTER        1
#define SSS_CLAMP_ARTIFACT          0   // Reduces bleeding. Use with SSS_USE_TANGENT_PLANE.
#define SSS_RANDOM_ROTATION         1   // Hides undersampling artifacts with high-frequency noise. TAA blurs the noise.
#define SSS_USE_LDS_CACHE           1   // Use LDS as an L0 texture cache.

// 6656 bytes used. It appears that the reserved LDS space must be a multiple of 512 bytes.
#if SSS_USE_LDS_CACHE && defined(SHADER_STAGE_COMPUTE)
groupshared float2 textureCache0[TEXTURE_CACHE_SIZE_2D]; // {irradiance.rg}
groupshared float2 textureCache1[TEXTURE_CACHE_SIZE_2D]; // {irradiance.b, deviceDepth}
#endif

#if SSS_USE_LDS_CACHE && defined(SHADER_STAGE_COMPUTE)
void StoreSampleToCacheMemory(float4 value, int2 cacheCoord)
{
    int linearCoord = Mad24(TEXTURE_CACHE_SIZE_1D, cacheCoord.y, cacheCoord.x);

    textureCache0[linearCoord] = value.rg;
    textureCache1[linearCoord] = value.ba;
}

float4 LoadSampleFromCacheMemory(int2 cacheCoord)
{
    int linearCoord = Mad24(TEXTURE_CACHE_SIZE_1D, cacheCoord.y, cacheCoord.x);

    return float4(textureCache0[linearCoord],
                  textureCache1[linearCoord]);
}
#endif

// Note: The SSS buffer used here is sRGB
void DecodeFromSSSBuffer(uint2 positionSS, out SSSData sssData)
{
#if !defined(SHADER_STAGE_COMPUTE) && defined(_ILLUSION_RENDER_PASS_ENABLED)
    float4 sssBuffer = LOAD_FRAMEBUFFER_INPUT(SUBSURFACE_ALBEDO, positionSS);
#else
    float4 sssBuffer = LOAD_TEXTURE2D_X(_SubsurfaceAlbedo, positionSS);
#endif
    sssData.diffuseColor = sssBuffer.rgb;
    UnpackFloatInt8bit(sssBuffer.a, 16, sssData.subsurfaceMask, sssData.diffusionProfileIndex);
}

float4 LoadSampleFromVideoMemory(int2 pixelCoord)
{
#ifdef SHADER_STAGE_COMPUTE
    float3 irradiance = LOAD_TEXTURE2D_X(_SubsurfaceDiffuse, pixelCoord).rgb;
    float depth = LoadSceneDepth(pixelCoord);
#else
    float2 sampleUV = (float2)pixelCoord / _ScreenParams.xy;
    #ifdef _ILLUSION_RENDER_PASS_ENABLED
        float3 irradiance = LOAD_FRAMEBUFFER_INPUT(SUBSURFACE_DIFFUSE, pixelCoord.xy).rgb;
    #else
        float3 irradiance = SAMPLE_TEXTURE2D_X(_SubsurfaceDiffuse, sampler_SubsurfaceDiffuse, sampleUV).rgb;
    #endif
    float depth = SampleSceneDepth(sampleUV).r;
#endif

    return float4(irradiance, depth);
}

// Returns {irradiance, linearDepth}.
float4 LoadSample(int2 pixelCoord, int2 cacheOffset)
{
    float4 value;

#if SSS_USE_LDS_CACHE && defined(SHADER_STAGE_COMPUTE)
    int2 cacheCoord = pixelCoord - cacheOffset;
    bool isInCache  = max((uint)cacheCoord.x, (uint)cacheCoord.y) < TEXTURE_CACHE_SIZE_1D;

    if (isInCache)
    {
        value = LoadSampleFromCacheMemory(cacheCoord);
    }
    else
#endif
    {
        // Always load both irradiance and depth.
        // Avoid dependent texture reads at the cost of extra bandwidth.
        value = LoadSampleFromVideoMemory(pixelCoord);
    }

    value.a = LinearEyeDepth(value.a, _ZBufferParams);

    return value;
}

void SampleBurleyDiffusionProfile(float u, float rcpS, out float r, out float rcpPdf)
{
    u = 1 - u; // Convert CDF to CCDF

    float g = 1 + 4 * u * (2 * u + sqrt(1 + 4 * u * u));
    float n = exp2(log2(g) * (-1.0 / 3.0));                    // g^(-1/3)
    float p = g * n * n;                                   // g^(+1/3)
    float c = 1 + p + n;                                     // 1 + g^(+1/3) + g^(-1/3)
    float d = 3 / LOG2_E * 2 + 3 / LOG2_E * log2(u);     // 3 * Log[4 * u]
    float x = (3 / LOG2_E) * log2(c) - d;                    // 3 * Log[c / (4 * u)]

    // x      = s * r
    // exp_13 = Exp[-x/3] = Exp[-1/3 * 3 * Log[c / (4 * u)]]
    // exp_13 = Exp[-Log[c / (4 * u)]] = (4 * u) / c
    // exp_1  = Exp[-x] = exp_13 * exp_13 * exp_13
    // expSum = exp_1 + exp_13 = exp_13 * (1 + exp_13 * exp_13)
    // rcpExp = rcp(expSum) = c^3 / ((4 * u) * (c^2 + 16 * u^2))
    float rcpExp = c * c * c * rcp(4 * u * (c * c + 4 * u * (4 * u)));

    r = x * rcpS;
    rcpPdf = 8 * PI * rcpS * rcpExp; // (8 * Pi) / s / (Exp[-s * r / 3] + Exp[-s * r])
}

// Performs sampling of the Normalized Burley diffusion profile in polar coordinates.
// The result must be multiplied by the albedo.
float3 EvalBurleyDiffusionProfile(float r, float3 S)
{
    float3 exp_13 = exp2(LOG2_E * (-1.0 / 3.0) * r * S); // Exp[-S * r / 3]
    float3 expSum = exp_13 * (1 + exp_13 * exp_13);        // Exp[-S * r / 3] + Exp[-S * r]

    return (S * rcp(8 * PI)) * expSum; // S / (8 * Pi) * (Exp[-S * r / 3] + Exp[-S * r])
}

// Computes f(r, s)/p(r, s), s.t. r = sqrt(xy^2 + z^2).
// Rescaling of the PDF is handled by 'totalWeight'.
float3 ComputeBilateralWeight(float xy2, float z, float mmPerUnit, float3 S, float rcpPdf)
{
#if !SSS_BILATERAL_FILTER
    z = 0;
#endif
    float r = sqrt(xy2 + (z * mmPerUnit) * (z * mmPerUnit));
    float area = rcpPdf;
#if SSS_CLAMP_ARTIFACT
    return saturate(EvalBurleyDiffusionProfile(r, S) * area);
#else
    return EvalBurleyDiffusionProfile(r, S) * area;
#endif
}

bool TestLightingForSSS(float3 subsurfaceLighting)
{
    return subsurfaceLighting.b > 0;
}

void EvaluateSample(uint i, uint n, int2 pixelCoord,
                    int2 cacheOffset, float3 S, float d,
                    float mmPerUnit, float pixelsPerMm, float phase, 
                    inout float3 totalIrradiance, inout float3 totalWeight, float linearDepth)
{
    // The sample count is loop-invariant.
    const float scale  = rcp(n);
    const float offset = rcp(n) * 0.5;

    // The phase angle is loop-invariant.
    float sinPhase, cosPhase;
    sincos(phase, sinPhase, cosPhase);

    float r, rcpPdf;
    SampleBurleyDiffusionProfile(i * scale + offset, d, r, rcpPdf);
    
    float phi = SampleDiskGolden(i, n).y;
    float sinPhi, cosPhi;
    sincos(phi, sinPhi, cosPhi);

    float sinPsi = cosPhase * sinPhi + sinPhase * cosPhi; // sin(phase + phi)
    float cosPsi = cosPhase * cosPhi - sinPhase * sinPhi; // cos(phase + phi)

    // floor((pixelCoord + 0.5) + vec * pixelsPerMm)
    // position = pixelCoord + floor(0.5 + vec * pixelsPerMm);
    // position = pixelCoord + round(vec * pixelsPerMm);
    // Note that (int) truncates towards 0, while floor() truncates towards -Inf!
    float2 position = pixelCoord + (int2)round(pixelsPerMm * r * float2(cosPsi, sinPsi));
    float xy2 = r * r;
    
    float4 textureSample = LoadSample(position, cacheOffset);
    float3 irradiance    = textureSample.rgb;

    // Check the results of the stencil test.
    if (TestLightingForSSS(irradiance))
    {
        // Apply bilateral weighting.
        float  viewZ  = textureSample.a;
        float  relZ   = viewZ - linearDepth;
        float3 weight = ComputeBilateralWeight(xy2, relZ, mmPerUnit, S, rcpPdf);

        // Note: if the texture sample if off-screen, (z = 0) -> (viewZ = far) -> (weight ≈ 0).
        totalIrradiance += weight * irradiance;
        totalWeight     += weight;
    }
}
#endif