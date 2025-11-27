using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering
{
    // Currently only support forward rendering path
    // Reference: UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusionPass
    /// <summary>
    /// Render ground truth ambient occlusion.
    /// </summary>
    public class GroundTruthAmbientOcclusionPass : ScriptableRenderPass, IDisposable
    {
        private const string OrthographicCameraKeyword = "_ORTHOGRAPHIC";

        private const string NormalReconstructionLowKeyword = "_RECONSTRUCT_NORMAL_LOW";

        private const string NormalReconstructionMediumKeyword = "_RECONSTRUCT_NORMAL_MEDIUM";

        private const string NormalReconstructionHighKeyword = "_RECONSTRUCT_NORMAL_HIGH";

        private const string SourceDepthKeyword = "_SOURCE_DEPTH";

        private const string SourceDepthNormalsKeyword = "_SOURCE_DEPTH_NORMALS";

        private const string FullResolutionKeyword = "FULL_RES";

        private const string HalfResolutionKeyword = "HALF_RES";
        
        private const string PackAODepthKeyword = "PACK_AO_DEPTH";

        // Private Variables
        private readonly bool _supportsR8RenderTextureFormat = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.R8);

        private readonly LazyMaterial _material = new(IllusionShaders.GroundTruthAmbientOcclusion);

        private readonly Vector4[] _cameraTopLeftCorner = new Vector4[2];

        private readonly Vector4[] _cameraXExtent = new Vector4[2];

        private readonly Vector4[] _cameraYExtent = new Vector4[2];

        private readonly Vector4[] _cameraZExtent = new Vector4[2];

        private readonly Matrix4x4[] _cameraViewProjections = new Matrix4x4[2];
        
        private readonly ProfilingSampler _tracingSampler = new("Tracing");
        
        private readonly ProfilingSampler _blurSampler = new("Blur");

        private readonly RTHandle[] _ssaoTextures = new RTHandle[4];

        private RenderTextureDescriptor _aoPassDescriptor;
        
        private readonly ComputeShader _tracingCS;

        private readonly ComputeShader _blurCS;
        
        private readonly ComputeShader _upSampleBlurCS;

        private int _rtWidth;

        private int _rtHeight;

        private readonly int _tracingKernel;

        private bool _tracingInCS;
        
        private readonly int _fullDenoiseKernel;
        
        private readonly int _upsampleDenoiseKernel;
        
        private bool _blurInCS;

        private bool _downSample;

        // Constants
        private const string SSAOTextureName = "_ScreenSpaceOcclusionTexture";

        private readonly IllusionRendererData _rendererData;

        private GroundTruthAmbientOcclusionVariables _variables;

        private enum ShaderPasses
        {
            AmbientOcclusion = 0,
            
            BilateralBlurHorizontal = 1,
            BilateralBlurVertical = 2,
            BilateralBlurFinal = 3,
            
            GaussianBlurHorizontal = 4,
            GaussianBlurVertical = 5
        }

        // PARAMETERS DECLARATION GUIDELINES:
        // All data is aligned on Vector4 size, arrays elements included.
        // - Shader side structure will be padded for anything not aligned to Vector4. Add padding accordingly.
        // - Base element size for array should be 4 components of 4 bytes (Vector4 or Vector4Int basically) otherwise the array will be interlaced with padding on shader side.
        // - In Metal the float3 and float4 are both actually sized and aligned to 16 bytes, whereas for Vulkan/SPIR-V, the alignment is the same. Do not use Vector3!
        // Try to keep data grouped by access and rendering system as much as possible (fog params or light params together for example).
        // => Don't move a float parameter away from where it belongs for filling a hole. Add padding in this case.
        private struct GroundTruthAmbientOcclusionVariables
        {
            public Vector4 BufferSize;
            public Vector4 Params0;
            public Vector4 Params1;
            public Vector4 Params2;
            public Vector4 Params3;
            public Vector4 Params4;
            public Vector4 FirstTwoDepthMipOffsets;
            public Vector4 DepthToViewParams;
        }

        public GroundTruthAmbientOcclusionPass(IllusionRendererData rendererData)
        {
            _rendererData = rendererData;
            renderPassEvent = IllusionRenderPassEvent.AmbientOcclusionPass;
            _tracingCS = rendererData.RuntimeResources.groundTruthAOTraceCS;
            _tracingKernel = _tracingCS.FindKernel("GTAOMain");
            _blurCS = rendererData.RuntimeResources.groundTruthSpatialDenoiseCS;
            _fullDenoiseKernel  = _blurCS.FindKernel("SpatialDenoise");
            _upSampleBlurCS = rendererData.RuntimeResources.groundTruthUpsampleDenoiseCS;
            _upsampleDenoiseKernel = _upSampleBlurCS.FindKernel("BlurUpsample");
            profilingSampler = new ProfilingSampler("Ground Truth Ambient Occlusion");
        }

        /// <inheritdoc/>
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var settings = VolumeManager.instance.stack.GetComponent<GroundTruthAmbientOcclusion>();
            int downsampleDivider = settings.downSample.value ? 2 : 1;
            _downSample = settings.downSample.value;
            var actualBlurQuality = settings.blurQuality.value;
            if (actualBlurQuality == AmbientOcclusionBlurQuality.Spatial && !_rendererData.PreferComputeShader)
            {
                actualBlurQuality = AmbientOcclusionBlurQuality.Bilateral;
            }
            
            // Set up the descriptors
            _aoPassDescriptor = renderingData.cameraData.cameraTargetDescriptor;
            _aoPassDescriptor.msaaSamples = 1;
            _aoPassDescriptor.depthBufferBits = 0;

            // AO Pass
            _aoPassDescriptor.width /= downsampleDivider;
            _aoPassDescriptor.height /= downsampleDivider;
            _rtWidth = _aoPassDescriptor.width;
            _rtHeight = _aoPassDescriptor.height;
            
            _tracingInCS = _rendererData.PreferComputeShader;
            _blurInCS = _rendererData.PreferComputeShader && actualBlurQuality == AmbientOcclusionBlurQuality.Spatial;
            
            _aoPassDescriptor.enableRandomWrite = _tracingInCS;
            bool useRedComponentOnly = _supportsR8RenderTextureFormat && actualBlurQuality == AmbientOcclusionBlurQuality.Spatial;
            bool packAODepth = _tracingInCS && _blurInCS;
            // Spatial denoise pack AO & Depth in one channel.
            _aoPassDescriptor.colorFormat = packAODepth ? RenderTextureFormat.RFloat :
                useRedComponentOnly ? RenderTextureFormat.R8 : RenderTextureFormat.ARGB32;


#if ENABLE_VR && ENABLE_XR_MODULE
            int eyeCount = renderingData.cameraData.xr.enabled && renderingData.cameraData.xr.singlePassEnabled ? 2 : 1;
#else
            int eyeCount = 1;
#endif
            for (int eyeIndex = 0; eyeIndex < eyeCount; eyeIndex++)
            {
                Matrix4x4 view = renderingData.cameraData.GetViewMatrix(eyeIndex);
                Matrix4x4 proj = renderingData.cameraData.GetProjectionMatrix(eyeIndex);
                _cameraViewProjections[eyeIndex] = proj * view;

                // camera view space without translation, used by SSAO.hlsl ReconstructViewPos() to calculate view vector.
                Matrix4x4 cview = view;
                cview.SetColumn(3, new Vector4(0.0f, 0.0f, 0.0f, 1.0f));
                Matrix4x4 cviewProj = proj * cview;
                Matrix4x4 cviewProjInv = cviewProj.inverse;

                Vector4 topLeftCorner = cviewProjInv.MultiplyPoint(new Vector4(-1, 1, -1, 1));
                Vector4 topRightCorner = cviewProjInv.MultiplyPoint(new Vector4(1, 1, -1, 1));
                Vector4 bottomLeftCorner = cviewProjInv.MultiplyPoint(new Vector4(-1, -1, -1, 1));
                Vector4 farCentre = cviewProjInv.MultiplyPoint(new Vector4(0, 0, 1, 1));
                _cameraTopLeftCorner[eyeIndex] = topLeftCorner;
                _cameraXExtent[eyeIndex] = topRightCorner - topLeftCorner;
                _cameraYExtent[eyeIndex] = bottomLeftCorner - topLeftCorner;
                _cameraZExtent[eyeIndex] = farCentre;
            }

            float fovRad = renderingData.cameraData.camera.fieldOfView * Mathf.Deg2Rad;
            float invHalfTanFov = 1 / Mathf.Tan(fovRad * 0.5f);
            Vector2 focalLen =
                new Vector2(
                    invHalfTanFov * (renderingData.cameraData.camera.pixelHeight / (float)downsampleDivider /
                                     (renderingData.cameraData.camera.pixelWidth / (float)downsampleDivider)),
                    invHalfTanFov);
            Vector2 invFocalLen = new Vector2(1 / focalLen.x, 1 / focalLen.y);

            var material = _material.Value;
            material.SetVector(ShaderConstants._SSAO_UVToView,
                new Vector4(2 * invFocalLen.x, 2 * invFocalLen.y, -1 * invFocalLen.x, -1 * invFocalLen.y));
            material.SetVector(ShaderConstants._ProjectionParams2,
                new Vector4(1.0f / renderingData.cameraData.camera.nearClipPlane, 0.0f, 0.0f, 0.0f));
            material.SetMatrixArray(ShaderConstants._CameraViewProjections, _cameraViewProjections);
            material.SetVectorArray(ShaderConstants._CameraViewTopLeftCorner, _cameraTopLeftCorner);
            material.SetVectorArray(ShaderConstants._CameraViewXExtent, _cameraXExtent);
            material.SetVectorArray(ShaderConstants._CameraViewYExtent, _cameraYExtent);
            material.SetVectorArray(ShaderConstants._CameraViewZExtent, _cameraZExtent);

            // Update keywords for both pixel shader and compute shader
            UpdateKeywords(renderingData, settings);

            // Update properties
            int frameCount = (int)_rendererData.FrameCount;
            var aoParams0 = new Vector4(
                Mathf.Clamp(settings.thickness.value * settings.thickness.value, 0.0f, 0.99f),
                _aoPassDescriptor.height * invHalfTanFov * 0.25f,
                settings.radius.value,
                settings.stepCount.value
            );

            var aoParams1 = new Vector4(
                settings.intensity.value,
                1.0f / (settings.radius.value * settings.radius.value),
                (frameCount / 6) % 4,
                (frameCount % 6)
            );

            float aspectRatio = (float)_aoPassDescriptor.height / _aoPassDescriptor.width;
            // We start from screen space position, so we bake in this factor the 1 / resolution as well.
            var aoDepthToViewParams = new Vector4(
                2.0f / (invHalfTanFov * aspectRatio * _aoPassDescriptor.width),
                2.0f / (invHalfTanFov * _aoPassDescriptor.height),
                1.0f / (invHalfTanFov * aspectRatio),
                1.0f / invHalfTanFov
            );

            float scaleFactor = (float)_aoPassDescriptor.width * _aoPassDescriptor.height / (540.0f * 960.0f);
            float radInPixels = Mathf.Max(16, settings.maximumRadiusInPixels.value * Mathf.Sqrt(scaleFactor));

            var aoParams2 = new Vector4(
                settings.directionCount.value,
                1.0f / downsampleDivider, // Downsampling
                1.0f / (settings.stepCount.value + 1.0f),
                radInPixels
            );
            
            float stepSize = settings.downSample.value ? 0.5f : 1f;

            float blurTolerance = 1.0f - settings.blurSharpness.value;
            float maxBlurTolerance = 0.25f;
            float minBlurTolerance = -2.5f;
            blurTolerance = minBlurTolerance + (blurTolerance * (maxBlurTolerance - minBlurTolerance));

            float bTolerance = 1f - Mathf.Pow(10f, blurTolerance) * stepSize;
            bTolerance *= bTolerance;
            const float upsampleTolerance = -7.0f; // TODO: Expose?
            float uTolerance = Mathf.Pow(10f, upsampleTolerance);
            float noiseFilterWeight = 1f / (Mathf.Pow(10f, 0.0f) + uTolerance);

            var aoParams3 = new Vector4(
                bTolerance,
                uTolerance,
                noiseFilterWeight,
                stepSize
            );
            
            float upperNudgeFactor = 1.0f - settings.ghostingReduction.value;
            const float maxUpperNudgeLimit = 5.0f;
            const float minUpperNudgeLimit = 0.25f;
            upperNudgeFactor = minUpperNudgeLimit + (upperNudgeFactor * (maxUpperNudgeLimit - minUpperNudgeLimit));
            var aoParams4 = new Vector4(
                0,
                upperNudgeFactor,
                minUpperNudgeLimit,
                settings.spatialBilateralAggressiveness.value * 15.0f
            );

            var depthMipInfo = _rendererData.DepthMipChainInfo;
            var firstTwoDepthMipOffsets = new Vector4(depthMipInfo.mipLevelOffsets[1].x, depthMipInfo.mipLevelOffsets[1].y, depthMipInfo.mipLevelOffsets[2].x, depthMipInfo.mipLevelOffsets[2].y);
            float width = _aoPassDescriptor.width;
            float height = _aoPassDescriptor.height;
            _variables.BufferSize = new Vector4(width, height, 1.0f / width, 1.0f / height);
            _variables.Params0 = aoParams0;
            _variables.Params1 = aoParams1;
            _variables.Params2 = aoParams2;
            _variables.Params3 = aoParams3;
            _variables.Params4 = aoParams4;
            _variables.DepthToViewParams = aoDepthToViewParams;
            _variables.FirstTwoDepthMipOffsets = firstTwoDepthMipOffsets;
            
            SetPixelShaderProperties(material, _variables);

            switch (settings.source.value)
            {
                case AmbientOcclusionDepthSource.Depth:
                    ConfigureInput(ScriptableRenderPassInput.Depth);
                    break;
                case AmbientOcclusionDepthSource.DepthNormals:
                    ConfigureInput(ScriptableRenderPassInput.Normal);// need depthNormal prepass for forward-only geometry
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            // Allocate textures for the AO and blur
            RenderingUtils.ReAllocateIfNeeded(ref _ssaoTextures[0], _aoPassDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_SSAO_OcclusionTexture0");

            _aoPassDescriptor.enableRandomWrite = false;
            if (actualBlurQuality >= AmbientOcclusionBlurQuality.Bilateral)
            {
                RenderingUtils.ReAllocateIfNeeded(ref _ssaoTextures[1], _aoPassDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_SSAO_OcclusionTexture1");
            }
            if (actualBlurQuality == AmbientOcclusionBlurQuality.Bilateral)
            {
                RenderingUtils.ReAllocateIfNeeded(ref _ssaoTextures[2], _aoPassDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_SSAO_OcclusionTexture2");
            }

            // Upsample setup
            _aoPassDescriptor.width *= downsampleDivider;
            _aoPassDescriptor.height *= downsampleDivider;
            _aoPassDescriptor.colorFormat = _supportsR8RenderTextureFormat ? RenderTextureFormat.R8 : RenderTextureFormat.ARGB32;

            // Allocate texture for the final SSAO results
            _aoPassDescriptor.enableRandomWrite = _blurInCS;
            RenderingUtils.ReAllocateIfNeeded(ref _ssaoTextures[3], _aoPassDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_SSAO_OcclusionTexture");

            // Configure targets and clear color
            ConfigureTarget(_ssaoTextures[3]);
            ConfigureClear(ClearFlag.None, Color.white);
       }

        /// <summary>
        /// Use properties instead of constant buffer in pixel shader
        /// </summary>
        /// <param name="material"></param>
        /// <param name="variables"></param>
        private static void SetPixelShaderProperties(Material material, GroundTruthAmbientOcclusionVariables variables)
        {
            material.SetVector(ShaderConstants._AOBufferSize, variables.BufferSize);
            material.SetVector(ShaderConstants._AOParams0, variables.Params0);
            material.SetVector(ShaderConstants._AOParams1, variables.Params1);
            material.SetVector(ShaderConstants._AOParams2, variables.Params2);
            material.SetVector(ShaderConstants._AOParams3, variables.Params3);
            material.SetVector(ShaderConstants._AOParams4, variables.Params4);
            material.SetVector(ShaderConstants._AODepthToViewParams, variables.DepthToViewParams);
            material.SetVector(ShaderConstants._FirstTwoDepthMipOffsets, variables.FirstTwoDepthMipOffsets);
        }

        private void UpdateKeywords(RenderingData renderingData, GroundTruthAmbientOcclusion settings)
        {
            var material = _material.Value;
            CoreUtils.SetKeyword(material, OrthographicCameraKeyword, renderingData.cameraData.camera.orthographic);

            // Always use depth normal source.
            if (settings.source.value == AmbientOcclusionDepthSource.Depth)
            {
                switch (settings.normalSamples.value)
                {
                    case AmbientOcclusionNormalQuality.Low:
                        CoreUtils.SetKeyword(material, NormalReconstructionLowKeyword, true);
                        CoreUtils.SetKeyword(material, NormalReconstructionMediumKeyword, false);
                        CoreUtils.SetKeyword(material, NormalReconstructionHighKeyword, false);
                        break;
                    case AmbientOcclusionNormalQuality.Medium:
                        CoreUtils.SetKeyword(material, NormalReconstructionLowKeyword, false);
                        CoreUtils.SetKeyword(material, NormalReconstructionMediumKeyword, true);
                        CoreUtils.SetKeyword(material, NormalReconstructionHighKeyword, false);
                        break;
                    case AmbientOcclusionNormalQuality.High:
                        CoreUtils.SetKeyword(material, NormalReconstructionLowKeyword, false);
                        CoreUtils.SetKeyword(material, NormalReconstructionMediumKeyword, false);
                        CoreUtils.SetKeyword(material, NormalReconstructionHighKeyword, true);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            switch (settings.source.value)
            {
                case AmbientOcclusionDepthSource.DepthNormals:
                    CoreUtils.SetKeyword(material, SourceDepthKeyword, false);
                    CoreUtils.SetKeyword(material, SourceDepthNormalsKeyword, true);
                    break;
                default:
                    CoreUtils.SetKeyword(material, SourceDepthKeyword, true);
                    CoreUtils.SetKeyword(material, SourceDepthNormalsKeyword, false);
                    break;
            }

            if (settings.downSample.value)
            {
                _tracingCS.EnableKeyword(HalfResolutionKeyword);
                _tracingCS.DisableKeyword(FullResolutionKeyword);
                CoreUtils.SetKeyword(material, HalfResolutionKeyword, true);
                CoreUtils.SetKeyword(material, FullResolutionKeyword, false);
            }
            else
            {
                _tracingCS.DisableKeyword(HalfResolutionKeyword);
                _tracingCS.EnableKeyword(FullResolutionKeyword);
                CoreUtils.SetKeyword(material, HalfResolutionKeyword, false);
                CoreUtils.SetKeyword(material, FullResolutionKeyword, true);
            }

            if (settings.blurQuality.value == AmbientOcclusionBlurQuality.Spatial && _blurInCS)
            {
                _tracingCS.EnableKeyword(PackAODepthKeyword);
            }
            else
            {
                _tracingCS.DisableKeyword(PackAODepthKeyword);
            }
        }

        private void ExecuteAO(CommandBuffer cmd, ref RenderingData renderingData, bool asyncCompute)
        {
            // Stencil has been written to depth attachment in depth normal prepass
            var depthStencilTexture = UniversalRenderingUtility.GetDepthWriteTexture(ref renderingData.cameraData);
            if (!depthStencilTexture.IsValid()) return;

            if (_tracingInCS)
            {
                cmd.SetComputeTextureParam(_tracingCS, _tracingKernel, ShaderConstants._AOPackedData, _ssaoTextures[0]);
                cmd.SetComputeTextureParam(_tracingCS, _tracingKernel, IllusionShaderProperties._StencilTexture, 
                    depthStencilTexture, 0, RenderTextureSubElement.Stencil);
                ConstantBuffer.Push(cmd, _variables, _tracingCS, ShaderConstants.ShaderVariablesAmbientOcclusion);
                
                int groupsX = IllusionRenderingUtils.DivRoundUp(_rtWidth, 8);
                int groupsY = IllusionRenderingUtils.DivRoundUp(_rtHeight, 8);
                cmd.DispatchCompute(_tracingCS, _tracingKernel, groupsX, groupsY, IllusionRendererData.MaxViewCount);
            }
            else
            {
                _material.Value.SetTexture(IllusionShaderProperties._StencilTexture, depthStencilTexture, RenderTextureSubElement.Stencil);
                RTHandle cameraDepthTargetHandle = renderingData.cameraData.renderer.cameraDepthTargetHandle;
                RenderAndSetBaseMap(cmd, cameraDepthTargetHandle, _ssaoTextures[0], ShaderPasses.AmbientOcclusion);
            }
        }
        
        private void ExecuteBlur(CommandBuffer cmd)
        {
            if (_blurInCS)
            {
                ComputeShader blurCS = _downSample ? _upSampleBlurCS : _blurCS;
                int blurKernel = _downSample ? _upsampleDenoiseKernel : _fullDenoiseKernel;
                cmd.SetComputeTextureParam(blurCS, blurKernel, ShaderConstants._AOPackedData, _ssaoTextures[0]);
                cmd.SetComputeTextureParam(blurCS, blurKernel, ShaderConstants._OcclusionTexture, _ssaoTextures[3]);
            
                ConstantBuffer.Push(cmd, _variables, blurCS, ShaderConstants.ShaderVariablesAmbientOcclusion);
                
                int groupsX = IllusionRenderingUtils.DivRoundUp(_rtWidth, 8);
                int groupsY = IllusionRenderingUtils.DivRoundUp(_rtHeight, 8);
                cmd.DispatchCompute(blurCS, blurKernel, groupsX, groupsY, IllusionRendererData.MaxViewCount);
            }
            else
            {
                var settings = VolumeManager.instance.stack.GetComponent<GroundTruthAmbientOcclusion>();
                switch (settings.blurQuality.value)
                {
                    case AmbientOcclusionBlurQuality.Spatial:
                    case AmbientOcclusionBlurQuality.Bilateral:
                        RenderAndSetBaseMap(cmd, _ssaoTextures[0], _ssaoTextures[1], ShaderPasses.BilateralBlurHorizontal);
                        RenderAndSetBaseMap(cmd, _ssaoTextures[1], _ssaoTextures[2], ShaderPasses.BilateralBlurVertical);
                        RenderAndSetBaseMap(cmd, _ssaoTextures[2], _ssaoTextures[3], ShaderPasses.BilateralBlurFinal);
                        break;
                    case AmbientOcclusionBlurQuality.Gaussian:
                        RenderAndSetBaseMap(cmd, _ssaoTextures[0], _ssaoTextures[1], ShaderPasses.GaussianBlurHorizontal);
                        RenderAndSetBaseMap(cmd, _ssaoTextures[1], _ssaoTextures[3], ShaderPasses.GaussianBlurVertical);
                        break;
                }
            }
        }

        /// <inheritdoc/>
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var normalTexture = UniversalRenderingUtility.GetNormalTexture(renderingData.cameraData.renderer);
            if (!normalTexture.IsValid())
            {
                return;
            }
            
            CommandBuffer cmd = CommandBufferPool.Get();
            
            // Set the global SSAO Params
            // From URP14.0.62, renderer clear _AmbientOcclusionParam in ClearRenderingState
            // We should move param setup to Execute instead of OnCameraSetup
            var settings = VolumeManager.instance.stack.GetComponent<GroundTruthAmbientOcclusion>();
            cmd.SetGlobalVector(ShaderConstants._AmbientOcclusionParam, new Vector4(1f, 0f, 0f, settings.directLightingStrength.value));
            
            // Configure Async Compute
            bool useAsyncCompute = _tracingInCS && _blurInCS 
                                   && IllusionRuntimeRenderingConfig.Get().EnableAsyncCompute;
            if (useAsyncCompute)
            {
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();
                cmd.SetExecutionFlags(CommandBufferExecutionFlags.AsyncCompute);
            }
            
            using (new ProfilingScope(cmd, profilingSampler))
            {
                cmd.SetGlobalTexture(SSAOTextureName, _ssaoTextures[3]);

                // Execute the AO Pass
                using (new ProfilingScope(cmd, _tracingSampler))
                {
                    ExecuteAO(cmd, ref renderingData, useAsyncCompute);
                }

                // Execute the Blur Passes
                using (new ProfilingScope(cmd, _blurSampler))
                {
                    ExecuteBlur(cmd);
                }
                
                if (useAsyncCompute)
                {
                    _rendererData.CreateAsyncGraphicsFence(cmd, IllusionGraphicsFenceEvent.AmbientOcclusion);
                }
            }

            if (useAsyncCompute)
            {
                context.ExecuteCommandBufferAsync(cmd, ComputeQueueType.Background);
            }
            else
            {
                context.ExecuteCommandBuffer(cmd);
            }
            CommandBufferPool.Release(cmd);
        }

        private void RenderAndSetBaseMap(CommandBuffer cmd, RTHandle baseMap,
            RTHandle target, ShaderPasses pass)
        {
            if (baseMap.rt == null)
            {
                // Obsolete usage of RTHandle aliasing a RenderTargetIdentifier
                Vector2 viewportScale = baseMap.useScaling ? new Vector2(baseMap.rtHandleProperties.rtHandleScale.x, baseMap.rtHandleProperties.rtHandleScale.y) : Vector2.one;

                // Will set the correct camera viewport as well.
                CoreUtils.SetRenderTarget(cmd, target);
                Blitter.BlitTexture(cmd, baseMap.nameID, viewportScale, _material.Value, (int)pass);
            }
            else
            {
                Blitter.BlitCameraTexture(cmd, baseMap, target, _material.Value, (int)pass);
            }
        }

        public void Dispose()
        {
            _ssaoTextures[0]?.Release();
            _ssaoTextures[1]?.Release();
            _ssaoTextures[2]?.Release();
            _ssaoTextures[3]?.Release();
            _material.DestroyCache();
        }

        private static class ShaderConstants
        {
            public static readonly int _AOBufferSize = MemberNameHelpers.ShaderPropertyID();

            public static readonly int _AmbientOcclusionParam = MemberNameHelpers.ShaderPropertyID();

            public static readonly int _CameraViewXExtent = MemberNameHelpers.ShaderPropertyID();

            public static readonly int _CameraViewYExtent = MemberNameHelpers.ShaderPropertyID();

            public static readonly int _CameraViewZExtent = MemberNameHelpers.ShaderPropertyID();

            public static readonly int _ProjectionParams2 = MemberNameHelpers.ShaderPropertyID();

            public static readonly int _CameraViewProjections = MemberNameHelpers.ShaderPropertyID();

            public static readonly int _CameraViewTopLeftCorner = MemberNameHelpers.ShaderPropertyID();

            public static readonly int _SSAO_UVToView = MemberNameHelpers.ShaderPropertyID();

            public static readonly int _AOParams0 = MemberNameHelpers.ShaderPropertyID();

            public static readonly int _AOParams1 = MemberNameHelpers.ShaderPropertyID();

            public static readonly int _AOParams2 = MemberNameHelpers.ShaderPropertyID();
            
            public static readonly int _AOParams3 = MemberNameHelpers.ShaderPropertyID();
            
            public static readonly int _AOParams4 = MemberNameHelpers.ShaderPropertyID();

            public static readonly int _FirstTwoDepthMipOffsets = MemberNameHelpers.ShaderPropertyID();

            public static readonly int _AODepthToViewParams = MemberNameHelpers.ShaderPropertyID();
            
            public static readonly int ShaderVariablesAmbientOcclusion = MemberNameHelpers.ShaderPropertyID();
            
            public static readonly  int _AOPackedData = MemberNameHelpers.ShaderPropertyID();
            
            public static readonly int _OcclusionTexture = MemberNameHelpers.ShaderPropertyID();
        }
    }
}