#ifndef ILLUSION_SPEEDTREE7_SURFACE_INCLUDED
#define ILLUSION_SPEEDTREE7_SURFACE_INCLUDED

half4 SampleSpeedTree7AlbedoAlpha(float2 uv)
{
    half4 diffuse = SampleAlbedoAlpha(uv, TEXTURE2D_ARGS(_MainTex, sampler_MainTex));
    diffuse.a *= _Color.a;

    #ifdef SPEEDTREE_ALPHATEST
        diffuse.a = AlphaDiscard(diffuse.a, _Cutoff);
    #endif

    return diffuse;
}

void InitializeSpeedTree7Surface(half3 uvHueVariation, half3 detail, half4 vertexColor,
    half4 diffuse, out SurfaceData surfaceData)
{
    half2 uv = uvHueVariation.xy;
    half3 diffuseColor = diffuse.rgb;

    #ifdef GEOM_TYPE_BRANCH_DETAIL
        half4 detailColor = SAMPLE_TEXTURE2D(_DetailTex, sampler_DetailTex, detail.xy);
        diffuseColor.rgb = lerp(diffuseColor.rgb, detailColor.rgb, detail.z < 2.0f ? saturate(detail.z) : detailColor.a);
    #endif

    #ifdef EFFECT_HUE_VARIATION
        half3 shiftedColor = lerp(diffuseColor.rgb, _HueVariation.rgb, uvHueVariation.z);
        half maxBase = max(diffuseColor.r, max(diffuseColor.g, diffuseColor.b));
        half newMaxBase = max(shiftedColor.r, max(shiftedColor.g, shiftedColor.b));
        maxBase /= newMaxBase;
        maxBase = maxBase * 0.5f + 0.5f;
        // preserve vibrance
        shiftedColor.rgb *= maxBase;
        diffuseColor.rgb = saturate(shiftedColor);
    #endif

    #ifdef EFFECT_BUMP
        half3 normalTs = SampleNormal(uv, TEXTURE2D_ARGS(_BumpMap, sampler_BumpMap));
        #ifdef GEOM_TYPE_BRANCH_DETAIL
            half3 detailNormal = SampleNormal(detail.xy, TEXTURE2D_ARGS(_BumpMap, sampler_BumpMap));
            normalTs = lerp(normalTs, detailNormal, detail.z < 2.0f ? saturate(detail.z) : detailColor.a);
        #endif
    #else
        half3 normalTs = half3(0, 0, 1);
    #endif

    #ifdef VERTEX_COLOR
        diffuseColor.rgb *= vertexColor.rgb;
    #else
        diffuseColor.rgb *= _Color.rgb;
    #endif

    surfaceData.albedo = diffuseColor.rgb;
    surfaceData.alpha = diffuse.a;
    surfaceData.emission = half3(0, 0, 0);
    surfaceData.metallic = 0;
    surfaceData.occlusion = 1;
    surfaceData.smoothness = 0;
    surfaceData.specular = half3(0, 0, 0);
    surfaceData.clearCoatMask = 0;
    surfaceData.clearCoatSmoothness = 1;
    surfaceData.normalTS = normalTs;

}

#endif
