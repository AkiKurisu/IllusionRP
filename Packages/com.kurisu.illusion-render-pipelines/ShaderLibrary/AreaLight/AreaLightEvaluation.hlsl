#ifndef ILLUSION_AREA_LIGHT_EVALUATION_INCLUDED
#define ILLUSION_AREA_LIGHT_EVALUATION_INCLUDED

// @IllusionRP: per-family BRDF inputs are carried by AreaBSDFData / AreaPreLightData so one light loop body serves every family.

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonLighting.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/AreaLighting.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/AreaLight/ShaderVariablesAreaLights.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/AreaLight/CookieSampling.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/AreaLight/HDShadowContext.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/ShaderLibrary/AreaLight/LTCAreaLight.hlsl"
#include "Packages/com.kurisu.illusion-render-pipelines/Shaders/PreIntegratedFGD/PreIntegratedFGD.hlsl"

#define SHADOW_TYPE float

// Material feature flags consumed by the area light loop.
#define MATERIALFEATUREFLAGS_LIT_TRANSMISSION (2)
#define MATERIALFEATUREFLAGS_LIT_CLEAR_COAT (16)
#define MATERIALFEATUREFLAGS_SSS_DUAL_LOBE (256)

struct DirectLighting
{
    float3 diffuse;
    float3 specular;
};

// BRDF inputs read by the area light loop.
struct AreaBSDFData
{
    uint materialFeatures;
    float3 normalWS;
    float perceptualRoughness;
    float perceptualRoughnessB;      // Second GGX lobe (MATERIALFEATUREFLAGS_SSS_DUAL_LOBE)
    float3 fresnel0;
    float3 transmittance;
    float coatRoughness;
    float coatMask;
};

// Per-pixel area light precomputation.
struct AreaPreLightData
{
    float3 specularFGD;              // Store preintegrated BSDF for both specular and diffuse
    float  diffuseFGD;

    // Area lights
    // TODO: 'orthoBasisViewNormal' is just a rotation around the normal and should thus be just 1x VGPR.
    float3x3 orthoBasisViewNormal;   // Right-handed view-dependent orthogonal basis around the normal (6x VGPRs)
    // Warning: these matrices are transposed! They are designed to transform row vectors via mul(V, M).
    float3x3 ltcTransformDiffuse;    // Inverse transformation for Lambertian or Disney Diffuse        (4x VGPRs)
    float3x3 ltcTransformSpecular[2];// Inverse transformation for GGX - 2 specular lobes              (4x VGPRs * 2)
    float    ltcLobeMix;             // We store it only for area lights to save the vgpr otherwise    (1x VGPR)

    // Clear coat
    float    coatIblF;               // Fresnel term for view vector
    float3x3 ltcTransformCoat;       // Inverse transformation for GGX                                 (4x VGPRs)
};

//-----------------------------------------------------------------------------
// LTC evaluation
//-----------------------------------------------------------------------------

// Samples the area light's associated cookie
//  cookieIndex, the index of the cookie texture in the Texture2DArray
//  L, the 4 local-space corners of the area light polygon transformed by the LTC M^-1 matrix
//  F, the *normalized* vector irradiance
float3 SampleAreaLightCookie(float4 cookieScaleOffset, float4x3 L, float3 F, float perceptualRoughness)
{
    // L[0..3] : LL UL UR LR

    float3  origin = L[0];
    float3  right = L[3] - origin;
    float3  up = L[1] - origin;

    float3  normal = cross(right, up);
    float   sqArea = dot(normal, normal);
    normal *= rsqrt(sqArea);

    // Compute intersection of irradiance vector with the area light plane
    float   hitDistance = dot(origin, normal) / dot(F, normal);
    float3  hitPosition = hitDistance * normal;
    hitPosition -= origin;  // Relative to bottom-left corner

    // Here, right and up vectors are not necessarily orthonormal
    // We create the orthogonal vector "ortho" by projecting "up" onto the vector orthogonal to "right"
    //  ortho = up - (up.right') * right'
    // Where right' = right / sqrt( dot( right, right ) ), the normalized right vector
    float   recSqLengthRight = 1.0 / dot(right, right);
    float   upRightMixing = dot(up, right);
    float3  ortho = up - upRightMixing * right * recSqLengthRight;

    // The V coordinate along the "up" vector is simply the projection against the ortho vector
    float   v = dot(hitPosition, ortho) / dot(ortho, ortho);

    // The U coordinate is not only the projection against the right vector
    //  but also the subtraction of the influence of the up vector upon the right vector
    //  (indeed, if the up & right vectors are not orthogonal then a certain amount of
    //  the up coordinate also influences the right coordinate)
    //
    //       |    up
    // ortho ^....*--------*
    //       |   /:       /
    //       |  / :      /
    //       | /  :     /
    //       |/   :    /
    //       +----+-->*----->
    //            : right
    //          mix of up into right that needs to be subtracted from simple projection on right vector
    //
    float   u = (dot(hitPosition, right) - upRightMixing * v) * recSqLengthRight;
    // We create automatic quad emissive mesh for area light. For those to be displayed in the direction
    // of the light when they are single sided, we need to reverse the winding order.
    // Because of this reverse of winding order, to get a matching area light reflection,
    // we need to flip the x axis.
    float2  hitUV = float2(1 - u, v);

    // Assuming the original cosine lobe distribution Do is enclosed in a cone of 90 deg  aperture,
    //  following the idea of orthogonal projection upon the area light's plane we find the intersection
    //  of the cone to be a disk of area PI*d^2 where d is the hit distance we computed above.
    // We also know the area of the transformed polygon A = sqrt( sqArea ) and we pose the ratio of covered area as PI.d^2 / A.
    //
    // Knowing the area in square texels of the cookie texture A_sqTexels = texture width * texture height (default is 128x128 square texels)
    //  we can deduce the actual area covered by the cone in square texels as:
    //  A_covered = Pi.d^2 / A * A_sqTexels
    //
    // From this, we find the mip level as: mip = log2( sqrt( A_covered ) ) = log2( A_covered ) / 2
    // Also, assuming that A_sqTexels is of the form 2^n * 2^n we get the simplified expression: mip = log2( Pi.d^2 / A ) / 2 + n
    //
    // Compute the cookie mip count using the cookie size in the atlas
    float   cookieWidth = cookieScaleOffset.x * _CookieAtlasSize.x; // cookies and atlas are guaranteed to be POT
    float   cookieMipCount = round(log2(cookieWidth));
    float   mipLevel = 0.5 * log2(1e-8 + PI * hitDistance*hitDistance * rsqrt(sqArea)) + cookieMipCount;

    // We want to prevent the texture from accessing to the lower mips when evaluating the specular lobe
    // when operating on low roughness points. We progressively give access from mip 3 the rest of the mips between the range 0.0 -> 0.3
    // in the perceptual roughness space
    float mipTrimming = saturate((0.3 - perceptualRoughness) / 0.3);
    mipLevel = clamp(mipLevel, 0, lerp(cookieMipCount, 3.0, mipTrimming));

    return SampleCookie2D(saturate(hitUV), cookieScaleOffset, mipLevel);
}

// Helper function for rectangular area lights.
// Input: 'ltcVerts' must be inversely transformed in such a way that the transformed BRDF becomes uniform (diffuse).
// Returns unassociated (non-premultiplied) color with alpha (irradiance).
// The calling code must perform alpha-compositing.
float4 EvaluateLTC_Rect(float4x3 ltcVerts, float perceptualRoughness, int cookieMode, float4 cookieScaleOffset)
{
    float4 ltcValue;
    float3 formFactor;

    // Polygon irradiance in the transformed configuration.
    ltcValue.a   = PolygonIrradiance(ltcVerts, formFactor);
    ltcValue.rgb = float3(1,1,1);

    if (cookieMode != COOKIEMODE_NONE)
    {
        ltcValue.rgb = SampleAreaLightCookie(cookieScaleOffset, ltcVerts, formFactor, perceptualRoughness);
    }

    return ltcValue;
}

float4 EvaluateLTC_Area(bool isRectLight, float3 center, float3 right, float3 up, float halfLength, float halfHeight,
                        float3x3 invM, float perceptualRoughness, int cookieMode, float4 cookieScaleOffset)
{
    float3 ortho   = cross(center, right);
    float  orthoSq = dot(ortho, ortho);

    // Check whether the light is in a vertical orientation.
    bool quit = (orthoSq == 0);

    // Check whether the light is entirely below the surface.
    // We must test twice, since a linear transformation
    // may bring the light above the surface (a side-effect).
    quit = quit || (center.z + halfLength * abs(right.z) + halfHeight * abs(up.z) <= 0);

    float4 ltcValue = float4(1, 1, 1, 0);

    if (!quit)
    {
        // Perform a sparse matrix multiplication.
        float3 C = mul(invM, center);
        float3 A = mul(invM, right);
        float3 B = mul(invM, up);

        // Check whether the light is entirely below the surface.
        // We must test twice, since a linear transformation
        // may bring the light below the surface (as expected).
        if (C.z + halfLength * abs(A.z) + halfHeight * abs(B.z) > 0)
        {
            if (isRectLight)
            {
                float4x3 lightVerts;

                lightVerts[0] = C - halfLength * A - halfHeight  * B; // LL
                lightVerts[1] = lightVerts[0] + (2 * halfHeight) * B; // UL
                lightVerts[2] = lightVerts[1] + (2 * halfLength) * A; // UR
                lightVerts[3] = lightVerts[2] - (2 * halfHeight) * B; // LR

                float3 formFactor;

                // Polygon irradiance in the transformed configuration.
                ltcValue.a = PolygonIrradiance(lightVerts, formFactor);

                if (cookieMode != COOKIEMODE_NONE)
                {
                    ltcValue.rgb = SampleAreaLightCookie(cookieScaleOffset, lightVerts, formFactor, perceptualRoughness);
                }
            }
            else // Line light
            {
                float w = ComputeLineWidthFactor(invM, ortho, orthoSq);

                ltcValue.a = I_diffuse_line(C, A, halfLength) * w;
            }
        }
    }

    return ltcValue;
}

// This function transforms a rectangular area light according the the barn door inputs defined by the user.
void RectangularLightApplyBarnDoor(inout AreaLightData lightData, float3 pointPosition)
{
    // If we are above 89° or the depth is smaller than 5cm this is not worth it.
    if (lightData.size.z > 0.017f && lightData.size.w > 0.05f)
    {
        // Compute the half size of the light source
        float halfWidth  = lightData.size.x * 0.5;
        float halfHeight = lightData.size.y * 0.5;

        // Transform the point to light source space. First position then orientation
        float3 lightRelativePointPos = -(lightData.positionRWS - pointPosition);
        float3 pointLS = float3(dot(lightRelativePointPos, lightData.right), dot(lightRelativePointPos, lightData.up), dot(lightRelativePointPos, lightData.forward));

        // Compute the depth of the point in the pyramid space
        float pointDepth = min(pointLS.z, lightData.size.z * lightData.size.w);

        // Compute the ratio between the point's depth and the maximal depth of the pyramid
        float pointDepthRatio = pointDepth / (lightData.size.z * lightData.size.w);
        float sinTheta = sqrt(1 - max(0, lightData.size.z * lightData.size.z));

        // Compute the barn door projection
        float barnDoorProjection = sinTheta * lightData.size.w * pointDepthRatio;

        // Compute the sign of the point when in the local light space
        float2 pointSign = sign(pointLS.xy);
        // Clamp the point to the closest edge
        pointLS.xy = float2(pointSign.x, pointSign.y) * max(abs(pointLS.xy), float2(halfWidth, halfHeight) + barnDoorProjection.xx);

        // Compute the closest rect lignt corner, offset by the barn door size
        float3 closestLightCorner = float3(pointSign.x * (halfWidth + barnDoorProjection), pointSign.y * (halfHeight + barnDoorProjection), pointDepth);

        // Compute the point projection onto the edge and deduce the size that should be removed from the light dimensions
        float3 pointProjection  = pointLS - closestLightCorner;
        // Phi being the angle between the point projection point and the forward vector of the light source
        float  cosPhi = max(0, pointProjection.z);
        // If the angle is too perpendicular, we make the point infinitely far
        float2 tanPhi = cosPhi > 0.001f ? abs(pointProjection.xy) / cosPhi : 99999.0f;
        float2 projectionDistance = pointDepth * tanPhi;

        // Compute the positions of the new vertices of the culled light
        float2 topRight = float2(-halfWidth, halfWidth);
        float2 bottomLeft = float2(-halfHeight, halfHeight);
        topRight += (projectionDistance.x - barnDoorProjection) * float2(max(0, -pointSign.x), -max(0, pointSign.x));
        bottomLeft += (projectionDistance.y - barnDoorProjection) * float2(max(0, -pointSign.y), -max(0, pointSign.y));
        topRight = clamp(topRight, -halfWidth, halfWidth);
        bottomLeft = clamp(bottomLeft, -halfHeight, halfHeight);

        // Compute the offset that needs to be applied to the origin points to match the culling of the barn door
        float2 lightCenterOffset = 0.5f * float2(topRight.x + topRight.y, bottomLeft.x + bottomLeft.y);

        // Change the input data of the light to adjust the rectangular area light
        lightData.size.xy = float2(topRight.y - topRight.x, bottomLeft.y - bottomLeft.x);
        lightData.positionRWS = lightData.positionRWS + lightData.right * lightCenterOffset.x + lightData.up * lightCenterOffset.y;
    }
}

//-----------------------------------------------------------------------------
// Area shadows
//-----------------------------------------------------------------------------

float GetRectAreaShadowAttenuation(HDShadowContext shadowContext, float2 positionSS, float3 positionWS, float3 normalWS, int shadowDataIndex, float3 L, float L_dist)
{
    // We need to disable the scalarization here on xbox due to bad code generated by FXC for the eye shader.
    // This shouldn't have an enormous impact since with Area lights we are already exploded in VGPR by this point.
#if FORCE_SHADOW_SCALAR_READ
    shadowDataIndex = WaveReadLaneFirst(shadowDataIndex);
#endif
    HDShadowData sd = shadowContext.shadowDatas[shadowDataIndex];

    if (sd.isInCachedAtlas > 0) // This is a scalar branch.
    {
        return EvalShadow_AreaDepth(sd, _CachedAreaLightShadowmapAtlas, positionSS, positionWS, normalWS, L, L_dist, true);
    }
    else
    {
        return EvalShadow_AreaDepth(sd, _ShadowmapAreaAtlas, positionSS, positionWS, normalWS, L, L_dist, true);
    }
}

// @IllusionRP: shadow mask and screen space (ray traced) area shadows are not ported.
SHADOW_TYPE EvaluateShadow_RectArea( float2 positionSS, float3 positionWS,
                                     AreaLightData light, float3 N, float3 L, float dist)
{
#ifndef LIGHT_EVALUATION_NO_SHADOWS
    float shadow        = 1.0;
    float shadowMask    = 1.0;

    if ((light.shadowIndex >= 0) && (light.shadowDimmer > 0))
    {
        shadow = GetRectAreaShadowAttenuation(InitShadowContext(), positionSS, positionWS, N, light.shadowIndex, L, dist);

        shadow = lerp(shadowMask, shadow, light.shadowDimmer);
    }

    return shadow;
#else // LIGHT_EVALUATION_NO_SHADOWS
    return 1.0;
#endif
}

//-----------------------------------------------------------------------------
// PreLightData
//-----------------------------------------------------------------------------

// diffuseLtcModel: LTCLIGHTINGMODEL_DISNEY_DIFFUSE or LTCLIGHTINGMODEL_COUNT for Lambert (identity transform).
// ltcLobeMix feeds the second GGX lobe when MATERIALFEATUREFLAGS_SSS_DUAL_LOBE is set.
AreaPreLightData GetAreaPreLightData(float3 V, float3 N, AreaBSDFData bsdfData, uint diffuseLtcModel,
    float ltcLobeMix, float3 specularFGD, float diffuseFGD, float coatIblF)
{
    AreaPreLightData preLightData;
    ZERO_INITIALIZE(AreaPreLightData, preLightData);

    float NdotV = dot(N, V);
    float clampedNdotV = ClampNdotV(NdotV);

    preLightData.specularFGD = specularFGD;
    preLightData.diffuseFGD = diffuseFGD;

    // Area light
    if (diffuseLtcModel == LTCLIGHTINGMODEL_COUNT)
    {
        preLightData.ltcTransformDiffuse = k_identity3x3;
    }
    else
    {
        preLightData.ltcTransformDiffuse = SampleLtcMatrix(bsdfData.perceptualRoughness, clampedNdotV, diffuseLtcModel);
    }

    float perceptualRoughnessA = bsdfData.perceptualRoughness;

    // This is a dynamic branch if the MATERIALFEATUREFLAGS_LIT_SUBSURFACE_SCATTERING flag is enabled.
    if (HasFlag(bsdfData.materialFeatures, MATERIALFEATUREFLAGS_SSS_DUAL_LOBE))
    {
        preLightData.ltcLobeMix = ltcLobeMix;
        preLightData.ltcTransformSpecular[1] = SampleLtcMatrix(bsdfData.perceptualRoughnessB, clampedNdotV, LTCLIGHTINGMODEL_GGX);
    }

    preLightData.ltcTransformSpecular[0] = SampleLtcMatrix(perceptualRoughnessA, clampedNdotV, LTCLIGHTINGMODEL_GGX);

    // Construct a right-handed view-dependent orthogonal basis around the normal
    preLightData.orthoBasisViewNormal = GetOrthoBasisViewNormal(V, N, NdotV);

    preLightData.ltcTransformCoat = 0.0;
    preLightData.coatIblF = 0.0;
    if (HasFlag(bsdfData.materialFeatures, MATERIALFEATUREFLAGS_LIT_CLEAR_COAT))
    {
        preLightData.ltcTransformCoat = SampleLtcMatrix(RoughnessToPerceptualRoughness(bsdfData.coatRoughness), clampedNdotV, LTCLIGHTINGMODEL_GGX);
        preLightData.coatIblF = coatIblF;
    }

    return preLightData;
}

//-----------------------------------------------------------------------------
// EvaluateBSDF_Area - Approximation with Linearly Transformed Cosines
//-----------------------------------------------------------------------------

// evaluateDiffuse / evaluateSpecular let split lighting passes (skin) skip the unused half.
DirectLighting EvaluateBSDF_Area(float2 positionSS, float3 positionWS,
    AreaPreLightData preLightData, AreaLightData lightData,
    AreaBSDFData bsdfData, bool evaluateDiffuse, bool evaluateSpecular)
{
    DirectLighting lighting;
    ZERO_INITIALIZE(DirectLighting, lighting);

    const bool isRectLight = lightData.lightType == GPULIGHTTYPE_RECTANGLE; // static

#if SHADEROPTIONS_BARN_DOOR
    if (isRectLight)
    {
        RectangularLightApplyBarnDoor(lightData, positionWS);
    }
#endif

    // Translate the light s.t. the shaded point is at the origin of the coordinate system.
    float3 unL = lightData.positionRWS - positionWS;

    // These values could be precomputed on CPU to save VGPR or ALU.
    float halfLength = lightData.size.x * 0.5;
    float halfHeight = lightData.size.y * 0.5; // = 0 for a line light

    float intensity = PillowWindowing(unL, lightData.right, lightData.up, halfLength, halfHeight,
                                      lightData.rangeAttenuationScale, lightData.rangeAttenuationBias);

    // Make sure the light is front-facing (and has a non-zero effective area).
    intensity *= (isRectLight && dot(unL, lightData.forward) >= 0) ? 0 : 1;

    bool isVisible = true;

    // Raytracing shadow algorithm require to evaluate lighting without shadow, so it defined SKIP_RASTERIZED_AREA_SHADOWS
    // This is only present in Lit Material as it is the only one using the improved shadow algorithm.
#ifndef SKIP_RASTERIZED_AREA_SHADOWS
    if (isRectLight && intensity > 0)
    {
        SHADOW_TYPE shadow = EvaluateShadow_RectArea(positionSS, positionWS, lightData, bsdfData.normalWS, normalize(lightData.positionRWS), length(lightData.positionRWS));
        lightData.color.rgb *= ComputeShadowColor(shadow, lightData.shadowTint, lightData.penumbraTint);

        isVisible = Max3(lightData.color.r, lightData.color.g, lightData.color.b) > 0;
    }
#endif

    // Terminate if the shaded point is occluded or is too far away.
    if (isVisible && intensity > 0)
    {
        // Rotate the light vectors into the local coordinate system.
        float3 center = mul(preLightData.orthoBasisViewNormal, unL);
        float3 right  = mul(preLightData.orthoBasisViewNormal, lightData.right);
        float3 up     = mul(preLightData.orthoBasisViewNormal, lightData.up);

        float4 ltcValue;

        // ----- 1. Evaluate the diffuse part -----

        if (evaluateDiffuse)
        {
            ltcValue = EvaluateLTC_Area(isRectLight, center, right, up, halfLength, halfHeight,
                                        // LTC light cookies appear broken unless diffuse roughness is set to 1.
                                        transpose(preLightData.ltcTransformDiffuse), /*bsdfData.perceptualRoughness*/ 1.0f,
                                        lightData.cookieMode, lightData.cookieScaleOffset);

            ltcValue.a *= intensity * lightData.diffuseDimmer;

            // We don't multiply by 'bsdfData.diffuseColor' here. It's done only once in PostEvaluateBSDF().
            lighting.diffuse += ltcValue.rgb * ltcValue.a;

            if (HasFlag(bsdfData.materialFeatures, MATERIALFEATUREFLAGS_LIT_TRANSMISSION))
            {
                // Flip the surface while maintaining the view direction.
                float3x3 flipMatrix = float3x3(1,  0,  0,
                                               0, -1,  0,
                                               0,  0, -1);

                // Transform the vectors instead of transforming the basis.
                // Use the Lambertian approximation for performance reasons.
                // TODO: performing the evaluation twice is very inefficient!
                ltcValue = EvaluateLTC_Area(isRectLight, mul(flipMatrix, center), mul(flipMatrix, right), mul(flipMatrix, up), halfLength, halfHeight,
                                            k_identity3x3, 1.0f,
                                            lightData.cookieMode, lightData.cookieScaleOffset);

                ltcValue.a *= intensity * lightData.diffuseDimmer;

                // We use diffuse lighting for accumulation since it is going to be blurred during the SSS pass.
                // We don't multiply by 'bsdfData.diffuseColor' here. It's done only once in PostEvaluateBSDF().
                lighting.diffuse += bsdfData.transmittance * ltcValue.rgb * ltcValue.a;
            }
        }

        // ----- 2. Evaluate the specular part -----

        if (evaluateSpecular)
        {
            float perceptualRoughnessA = bsdfData.perceptualRoughness;
            float perceptualRoughnessB = bsdfData.perceptualRoughnessB;

            // First lobe
            ltcValue = EvaluateLTC_Area(isRectLight, center, right, up, halfLength, halfHeight,
                                        transpose(preLightData.ltcTransformSpecular[0]), perceptualRoughnessA,
                                        lightData.cookieMode, lightData.cookieScaleOffset);

            if (HasFlag(bsdfData.materialFeatures, MATERIALFEATUREFLAGS_SSS_DUAL_LOBE))
            {
                // Second lobe
                float4 ltcValue1 = EvaluateLTC_Area(isRectLight, center, right, up, halfLength, halfHeight,
                                                    transpose(preLightData.ltcTransformSpecular[1]), perceptualRoughnessB,
                                                    lightData.cookieMode, lightData.cookieScaleOffset);

                // Mix the lobes
                ltcValue = lerp(ltcValue, ltcValue1, preLightData.ltcLobeMix);
            }

            ltcValue.a *= intensity * lightData.specularDimmer;

            // We need to multiply by the magnitude of the integral of the BRDF
            // ref: http://advances.realtimerendering.com/s2016/s2016_ltc_fresnel.pdf
            lighting.specular += preLightData.specularFGD * ltcValue.rgb * ltcValue.a;

            // ----- 3. Evaluate the clear coat part -----

            if (HasFlag(bsdfData.materialFeatures, MATERIALFEATUREFLAGS_LIT_CLEAR_COAT))
            {
                ltcValue = EvaluateLTC_Area(isRectLight, center, right, up, halfLength, halfHeight,
                                            transpose(preLightData.ltcTransformCoat), RoughnessToPerceptualRoughness(bsdfData.coatRoughness),
                                            lightData.cookieMode, lightData.cookieScaleOffset);

                ltcValue.a *= intensity * lightData.specularDimmer;

                // For clear coat we don't fetch specularFGD we can use directly the perfect fresnel coatIblF
                // @IllusionRP: coat mask weights the coat like the URP clear coat mix.
                float coatWeight = preLightData.coatIblF * bsdfData.coatMask;
                lighting.diffuse *= 1.0 - coatWeight;
                lighting.specular = lerp(lighting.specular, ltcValue.rgb * ltcValue.a, coatWeight);
            }
        }

        // We need to multiply by the magnitude of the integral of the BRDF
        // ref: http://advances.realtimerendering.com/s2016/s2016_ltc_fresnel.pdf
        lighting.diffuse  *= lightData.color * preLightData.diffuseFGD;
        lighting.specular *= lightData.color;

        // @IllusionRP: URP direct lighting omits the 1/PI of Lambert and scales specular by PI to match,
        // @IllusionRP: LTC irradiance follows the HDRP convention, bring it to URP units once here.
        lighting.diffuse  *= PI;
        lighting.specular *= PI;
    }

    return lighting;
}

#endif // ILLUSION_AREA_LIGHT_EVALUATION_INCLUDED
