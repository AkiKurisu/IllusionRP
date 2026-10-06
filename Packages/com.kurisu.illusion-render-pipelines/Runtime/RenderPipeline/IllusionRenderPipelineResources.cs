using UnityEngine;

namespace Illusion.Rendering
{
    public class IllusionRenderPipelineResources : ScriptableObject
    {
        // ReSharper disable once UnusedMember.Global
        [Header("Shader")] public Shader[] alwaysIncludedShaders;

        public Shader preIntegratedFGD_CharlieFabricLambertShader;

        public Shader preIntegratedFGD_GGXDisneyDiffuseShader;

        [Header("DLSS Neural Rendering")]
        public Shader dlssNeuralRenderingPrepareInputsShader;

        [Header("Wet Surface")]
        public Shader wetSurfaceMaskShader;

        public Shader wetSurfaceBlurNormalsShader;

        public Shader wetSurfaceSmoothnessShader;

        public Texture2D wetSurfaceBlueNoise;

        [Header("Path Tracing")]
        public UnityEngine.Rendering.RayTracingShader pathTracingRS;

        public UnityEngine.Rendering.RayTracingShader pathTracingBuildRS;

        public UnityEngine.Rendering.RayTracingShader pathTracingFillRS;

        public ComputeShader pathTracingRealtimeCS;

        public ComputeShader pathTracingMotionCS;

        public ComputeShader pathTracingAccumulationCS;

        public ComputeShader pathTracingLightsBakerCS;

        public ComputeShader pathTracingEnvironmentCS;

        public Shader pathTracingEnvironmentLightingShader;

        public ComputeShader pathTracingEmissiveCS;

        public Shader pathTracingOutputShader;

        public Shader pathTracingFGDShader;

        public Shader pathTracingRectangleLightShader;

        [Header("Area Lights")]
        public Shader areaLightShadowClearShader;

        public Shader filterAreaLightCookiesShader;

        public Shader areaLightEmissiveMeshShader;

        public Mesh areaLightEmissiveQuadMesh;

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        // ReSharper disable once UnusedMember.Global
        public Shader[] debugShaders;
#endif

        [Header("Compute Shader")] public ComputeShader clearBuffer2D;

        public ComputeShader subsurfaceScatteringCS;

        public ComputeShader contactShadowsCS;

        public ComputeShader evsmBlurCS;

        public ComputeShader diffuseShadowDenoiserCS;

        public ComputeShader diffuseDenoiserCS;
        
        public ComputeShader temporalFilterCS;

        public ComputeShader groundTruthAOTraceCS;

        public ComputeShader groundTruthSpatialDenoiseCS;

        public ComputeShader groundTruthUpsampleDenoiseCS;

        public ComputeShader screenSpaceReflectionCS;

        public ComputeShader screenSpaceGlobalIlluminationCS;

        public ComputeShader bilateralUpsampleCS;

        public ComputeShader fastFourierTransformCS;

        public ComputeShader fastFourierConvolveCS;

        public ComputeShader depthPyramidCS;

        public ComputeShader colorPyramidCS;

        public ComputeShader copyChannelCS;

        public ComputeShader prtBrickRelightCS;

        public ComputeShader prtProbeRelightCS;

#if UNITY_EDITOR
        public UnityEngine.Rendering.RayTracingShader prtBakeTraceRS;

        public ComputeShader reflectionProbeSampleCS;
#endif

        public ComputeShader volumetricFogRaymarchCS;

        public ComputeShader volumetricFogBlurCS;

        public ComputeShader volumetricFogUpsampleCS;

        public ComputeShader exposureCS;

        public ComputeShader histogramExposureCS;

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        public ComputeShader debugImageHistogramCS;
#endif

        [Header("Noise Texture")] public Texture2D owenScrambled256Tex;

        public Texture2D scramblingTile1SPP;

        public Texture2D rankingTile1SPP;
        
        public Texture2D scramblingTile8SPP;

        public Texture2D rankingTile8SPP;

        public Texture2D scramblingTile256SPP;

        public Texture2D rankingTile256SPP;

        public Texture2D scramblingTex;

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        [Header("Font Texture")] public Texture2D debugFontTex;
#endif
    }
}
