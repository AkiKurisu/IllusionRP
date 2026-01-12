using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering;

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
            ConfigureInput(ScriptableRenderPassInput.Normal);
        }

        /// <summary>
        /// Prepare AO parameters for both legacy and RenderGraph paths
        /// </summary>
        private void PrepareAOParameters(ref RenderingData renderingData, out int downsampleDivider, out AmbientOcclusionBlurQuality actualBlurQuality)
        {
            var settings = VolumeManager.instance.stack.GetComponent<GroundTruthAmbientOcclusion>();
            downsampleDivider = settings.downSample.value ? 2 : 1;
            _downSample = settings.downSample.value;
            actualBlurQuality = settings.blurQuality.value;
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

        /// <inheritdoc/>
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resource = frameData.Get<UniversalResourceData>();
            // Validate normal texture availability
            TextureHandle normalBuffer = resource.cameraNormalsTexture;
            if (!normalBuffer.IsValid())
            {
                return;
            }

            var renderingData = new RenderingData(frameData);
            // Prepare parameters
            PrepareAOParameters(ref renderingData, out int downsampleDivider, out var actualBlurQuality);
            
            var settings = VolumeManager.instance.stack.GetComponent<GroundTruthAmbientOcclusion>();
            
            // Determine execution paths
            bool useAsyncCompute = _tracingInCS && _blurInCS 
                                   && IllusionRuntimeRenderingConfig.Get().EnableAsyncCompute;
            
            bool useRedComponentOnly = _supportsR8RenderTextureFormat && actualBlurQuality == AmbientOcclusionBlurQuality.Spatial;
            bool packAODepth = _tracingInCS && _blurInCS;
            
            float scaleFactor = _downSample ? 0.5f : 1.0f;
            
            // Create AO packed data texture (intermediate result from tracing)
            var aoPackedDesc = new TextureDesc(_rtWidth, _rtHeight, false, false)
            {
                colorFormat = packAODepth ? GraphicsFormat.R32_SFloat :
                    useRedComponentOnly ? GraphicsFormat.R8_UNorm : GraphicsFormat.B8G8R8A8_UNorm,
                enableRandomWrite = _tracingInCS,
                name = "AO Packed Data"
            };
            TextureHandle aoPackedData = renderGraph.CreateTexture(aoPackedDesc);
            
            // Create temporary blur textures if needed for bilateral/gaussian blur
            TextureHandle blurTemp1 = TextureHandle.nullHandle;
            TextureHandle blurTemp2 = TextureHandle.nullHandle;
            
            if (!_blurInCS && actualBlurQuality >= AmbientOcclusionBlurQuality.Bilateral)
            {
                var blurDesc = new TextureDesc(_rtWidth, _rtHeight, false, false)
                {
                    colorFormat = aoPackedDesc.colorFormat,
                    name = "AO Blur Temp 1"
                };
                blurTemp1 = renderGraph.CreateTexture(blurDesc);
                
                if (actualBlurQuality == AmbientOcclusionBlurQuality.Bilateral)
                {
                    blurDesc.name = "AO Blur Temp 2";
                    blurTemp2 = renderGraph.CreateTexture(blurDesc);
                }
            }
            
            // Create final output texture (full resolution)
            var outputDesc = new TextureDesc(_rtWidth * downsampleDivider, _rtHeight * downsampleDivider, false, false)
            {
                colorFormat = _supportsR8RenderTextureFormat ? GraphicsFormat.R8_UNorm : GraphicsFormat.B8G8R8A8_UNorm,
                enableRandomWrite = _blurInCS,
                name = SSAOTextureName
            };
            TextureHandle finalAOTexture = renderGraph.CreateTexture(outputDesc);
            
            // Get depth pyramid and stencil textures
            TextureHandle depthPyramid = renderGraph.ImportTexture(_rendererData.DepthPyramidRT);
            TextureHandle depthStencilTexture = frameData.GetDepthWriteTextureHandle();
            
            // Tracing Pass
            TextureHandle tracedAO;
            if (_tracingInCS)
            {
                tracedAO = RenderAOComputePass(renderGraph, aoPackedData, depthPyramid, depthStencilTexture, normalBuffer, useAsyncCompute);
            }
            else
            {
                tracedAO = RenderAORasterPass(renderGraph, aoPackedData, depthPyramid, depthStencilTexture, normalBuffer);
            }
            
            // Blur/Denoise Passes
            TextureHandle denoisedAO;
            if (_blurInCS)
            {
                // Spatial denoise with compute shader
                if (_downSample)
                {
                    // Half-res: spatial denoise then upsample
                    // Create temporary texture for spatial denoised result
                    var spatialDenoisedDesc = new TextureDesc(_rtWidth, _rtHeight, false, false)
                    {
                        colorFormat = GraphicsFormat.R32_SFloat,
                        enableRandomWrite = true,
                        name = "AO Spatial Denoised"
                    };
                    TextureHandle spatialDenoisedTexture = renderGraph.CreateTexture(spatialDenoisedDesc);
                    TextureHandle spatialDenoised = SpatialDenoiseAOPass(renderGraph, tracedAO, spatialDenoisedTexture, useAsyncCompute);
                    denoisedAO = UpsampleAOPass(renderGraph, spatialDenoised, depthPyramid, finalAOTexture, useAsyncCompute);
                }
                else
                {
                    // Full-res: spatial denoise directly to output
                    denoisedAO = SpatialDenoiseAOPass(renderGraph, tracedAO, finalAOTexture, useAsyncCompute);
                }
            }
            else
            {
                // Bilateral or Gaussian blur with raster passes
                switch (actualBlurQuality)
                {
                    case AmbientOcclusionBlurQuality.Spatial:
                    case AmbientOcclusionBlurQuality.Bilateral:
                        TextureHandle blurH = BilateralBlurPass(renderGraph, tracedAO, blurTemp1, ShaderPasses.BilateralBlurHorizontal);
                        TextureHandle blurV = BilateralBlurPass(renderGraph, blurH, blurTemp2, ShaderPasses.BilateralBlurVertical);
                        denoisedAO = BilateralBlurPass(renderGraph, blurV, finalAOTexture, ShaderPasses.BilateralBlurFinal);
                        break;
                    case AmbientOcclusionBlurQuality.Gaussian:
                        TextureHandle gaussH = GaussianBlurPass(renderGraph, tracedAO, blurTemp1, ShaderPasses.GaussianBlurHorizontal);
                        denoisedAO = GaussianBlurPass(renderGraph, gaussH, finalAOTexture, ShaderPasses.GaussianBlurVertical);
                        break;
                    default:
                        denoisedAO = finalAOTexture;
                        break;
                }
            }
            
            // Set global texture and parameters
            SetGlobalAOParam(renderGraph, settings);
            SetGlobalAOTexture(renderGraph, denoisedAO);
        }

        private TextureHandle RenderAOComputePass(RenderGraph renderGraph, TextureHandle aoPackedData, 
            TextureHandle depthPyramid, TextureHandle depthStencilTexture, TextureHandle normalBuffer, bool useAsyncCompute)
        {
            using (var builder = renderGraph.AddComputePass<RenderAOPassData>("GTAO Tracing (Compute)", out var passData, _tracingSampler))
            {
                builder.EnableAsyncCompute(useAsyncCompute);
                
                passData.Variables = _variables;
                passData.TracingCS = _tracingCS;
                passData.TracingKernel = _tracingKernel;
                passData.RTWidth = _rtWidth;
                passData.RTHeight = _rtHeight;
                passData.TracingInCS = _tracingInCS;

                builder.UseTexture(aoPackedData, AccessFlags.Write);
                passData.AOPackedData = aoPackedData;
                builder.UseTexture(depthPyramid);
                passData.DepthTexture = depthPyramid;
                builder.UseTexture(normalBuffer);
                passData.NormalBuffer = normalBuffer;
                builder.UseTexture(depthStencilTexture);
                passData.StencilTexture = depthStencilTexture;
                
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                
                builder.SetRenderFunc((RenderAOPassData data, ComputeGraphContext context) =>
                {
                    ConstantBuffer.Push(context.cmd, data.Variables, data.TracingCS, ShaderConstants.ShaderVariablesAmbientOcclusion);
                    context.cmd.SetComputeTextureParam(data.TracingCS, data.TracingKernel, ShaderConstants._AOPackedData, data.AOPackedData);
                    context.cmd.SetComputeTextureParam(_tracingCS, _tracingKernel, IllusionShaderProperties._StencilTexture, 
                        depthStencilTexture, 0, RenderTextureSubElement.Stencil);
                    context.cmd.SetComputeTextureParam(data.TracingCS, data.TracingKernel, IllusionShaderProperties._CameraDepthTexture, data.DepthTexture);
                    context.cmd.SetComputeTextureParam(data.TracingCS, data.TracingKernel, IllusionShaderProperties._CameraNormalsTexture, data.NormalBuffer);
                    
                    int groupsX = IllusionRenderingUtils.DivRoundUp(data.RTWidth, 8);
                    int groupsY = IllusionRenderingUtils.DivRoundUp(data.RTHeight, 8);
                    context.cmd.DispatchCompute(data.TracingCS, data.TracingKernel, groupsX, groupsY, IllusionRendererData.MaxViewCount);
                });
                
                return passData.AOPackedData;
            }
        }

        private TextureHandle RenderAORasterPass(RenderGraph renderGraph, TextureHandle aoPackedData, 
            TextureHandle depthPyramid, TextureHandle depthStencilTexture, TextureHandle normalBuffer)
        {
            using (var builder = renderGraph.AddRasterRenderPass<RenderAOPassData>("GTAO Tracing (Raster)", out var passData, _tracingSampler))
            {
                passData.Variables = _variables;
                passData.Material = _material.Value;
                passData.TracingInCS = _tracingInCS;

                builder.SetRenderAttachment(aoPackedData, 0);
                passData.AOPackedData = aoPackedData;
                builder.UseTexture(depthPyramid);
                passData.DepthTexture = depthPyramid;
                builder.UseTexture(normalBuffer);
                passData.NormalBuffer = normalBuffer;
                builder.UseTexture(depthStencilTexture);
                passData.StencilTexture = depthStencilTexture;
                
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                
                builder.SetRenderFunc((RenderAOPassData data, RasterGraphContext context) =>
                {
                    data.Material.SetTexture(IllusionShaderProperties._StencilTexture, data.StencilTexture, RenderTextureSubElement.Stencil);
                    // Material properties already set in PrepareAOParameters
                    Blitter.BlitTexture(context.cmd, data.DepthTexture, Vector2.one, data.Material, (int)ShaderPasses.AmbientOcclusion);
                });
                
                return passData.AOPackedData;
            }
        }

        private TextureHandle SpatialDenoiseAOPass(RenderGraph renderGraph, TextureHandle aoPackedData, 
            TextureHandle outputTexture, bool useAsyncCompute)
        {
            using (var builder = renderGraph.AddComputePass<SpatialDenoiseAOPassData>("Spatial Denoise GTAO", out var passData, _blurSampler))
            {
                builder.EnableAsyncCompute(useAsyncCompute);
                
                passData.Variables = _variables;
                passData.SpatialDenoiseCS = _blurCS;
                passData.DenoiseKernel = _fullDenoiseKernel;
                passData.RTWidth = _rtWidth;
                passData.RTHeight = _rtHeight;
                passData.DownSample = _downSample;

                builder.UseTexture(aoPackedData);
                passData.PackedData = aoPackedData;
                builder.UseTexture(outputTexture, AccessFlags.Write);
                passData.DenoiseOutput = outputTexture;
                
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                
                builder.SetRenderFunc((SpatialDenoiseAOPassData data, ComputeGraphContext context) =>
                {
                    ConstantBuffer.Push(context.cmd, data.Variables, data.SpatialDenoiseCS, ShaderConstants.ShaderVariablesAmbientOcclusion);
                    context.cmd.SetComputeTextureParam(data.SpatialDenoiseCS, data.DenoiseKernel, ShaderConstants._AOPackedData, data.PackedData);
                    context.cmd.SetComputeTextureParam(data.SpatialDenoiseCS, data.DenoiseKernel, ShaderConstants._OcclusionTexture, data.DenoiseOutput);
                    
                    int groupsX = IllusionRenderingUtils.DivRoundUp(data.RTWidth, 8);
                    int groupsY = IllusionRenderingUtils.DivRoundUp(data.RTHeight, 8);
                    context.cmd.DispatchCompute(data.SpatialDenoiseCS, data.DenoiseKernel, groupsX, groupsY, IllusionRendererData.MaxViewCount);
                });
                
                return passData.DenoiseOutput;
            }
        }

        private TextureHandle UpsampleAOPass(RenderGraph renderGraph, TextureHandle aoInput, 
            TextureHandle depthPyramid, TextureHandle outputTexture, bool useAsyncCompute)
        {
            using (var builder = renderGraph.AddComputePass<UpsampleAOPassData>("Upsample GTAO", out var passData, _blurSampler))
            {
                builder.EnableAsyncCompute(useAsyncCompute);
                
                passData.Variables = _variables;
                passData.UpsampleCS = _upSampleBlurCS;
                passData.UpsampleKernel = _upsampleDenoiseKernel;
                // Upsample target is full resolution
                passData.RTWidth = _rtWidth * 2;
                passData.RTHeight = _rtHeight * 2;

                builder.UseTexture(aoInput);
                passData.Input = aoInput;
                builder.UseTexture(outputTexture, AccessFlags.Write);
                passData.Output = outputTexture;
                
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                
                builder.SetRenderFunc((UpsampleAOPassData data, ComputeGraphContext context) =>
                {
                    ConstantBuffer.Push(context.cmd, data.Variables, data.UpsampleCS, ShaderConstants.ShaderVariablesAmbientOcclusion);
                    context.cmd.SetComputeTextureParam(data.UpsampleCS, data.UpsampleKernel, ShaderConstants._AOPackedData, data.Input);
                    context.cmd.SetComputeTextureParam(data.UpsampleCS, data.UpsampleKernel, ShaderConstants._OcclusionTexture, data.Output);
                    
                    int groupsX = IllusionRenderingUtils.DivRoundUp(data.RTWidth, 8);
                    int groupsY = IllusionRenderingUtils.DivRoundUp(data.RTHeight, 8);
                    context.cmd.DispatchCompute(data.UpsampleCS, data.UpsampleKernel, groupsX, groupsY, IllusionRendererData.MaxViewCount);
                });
                
                return passData.Output;
            }
        }

        private TextureHandle BilateralBlurPass(RenderGraph renderGraph, TextureHandle source, 
            TextureHandle destination, ShaderPasses pass)
        {
            string passName = pass switch
            {
                ShaderPasses.BilateralBlurHorizontal => "Bilateral Blur Horizontal",
                ShaderPasses.BilateralBlurVertical => "Bilateral Blur Vertical",
                ShaderPasses.BilateralBlurFinal => "Bilateral Blur Final",
                _ => "Bilateral Blur"
            };
            
            using (var builder = renderGraph.AddRasterRenderPass<BilateralBlurAOPassData>(passName, out var passData, _blurSampler))
            {
                passData.Material = _material.Value;
                passData.Pass = pass;

                builder.UseTexture(source);
                passData.Source = source;
                builder.SetRenderAttachment(destination, 0);
                passData.Destination = destination;
                
                builder.AllowPassCulling(false);
                
                builder.SetRenderFunc((BilateralBlurAOPassData data, RasterGraphContext context) =>
                {
                    Blitter.BlitTexture(context.cmd, data.Source, Vector2.one, data.Material, (int)data.Pass);
                });
                
                return passData.Destination;
            }
        }

        private TextureHandle GaussianBlurPass(RenderGraph renderGraph, TextureHandle source, 
            TextureHandle destination, ShaderPasses pass)
        {
            string passName = pass switch
            {
                ShaderPasses.GaussianBlurHorizontal => "Gaussian Blur Horizontal",
                ShaderPasses.GaussianBlurVertical => "Gaussian Blur Vertical",
                _ => "Gaussian Blur"
            };
            
            using (var builder = renderGraph.AddRasterRenderPass<GaussianBlurAOPassData>(passName, out var passData, _blurSampler))
            {
                passData.Material = _material.Value;
                passData.Pass = pass;

                builder.UseTexture(source);
                passData.Source = source;
                builder.SetRenderAttachment(destination, 0);
                passData.Destination = destination;
                
                builder.AllowPassCulling(false);
                
                builder.SetRenderFunc((GaussianBlurAOPassData data, RasterGraphContext context) =>
                {
                    Blitter.BlitTexture(context.cmd, data.Source, Vector2.one, data.Material, (int)data.Pass);
                });
                
                return passData.Destination;
            }
        }
        
        private void SetGlobalAOParam(RenderGraph renderGraph, GroundTruthAmbientOcclusion settings)
        {
            using (var builder = renderGraph.AddUnsafePass<SetGlobalVectorPassData>("Set Global AO Vector", out var passData, profilingSampler))
            {
                passData.AmbientOcclusionParam = new Vector4(1f, 0f, 0f, settings.directLightingStrength.value);
                
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                
                builder.SetRenderFunc((SetGlobalVectorPassData data, UnsafeGraphContext context) =>
                {
                    context.cmd.SetGlobalVector(ShaderConstants._AmbientOcclusionParam, data.AmbientOcclusionParam);
                });
            }
        }

        private void SetGlobalAOTexture(RenderGraph renderGraph, TextureHandle aoTexture)
        {
            using (var builder = renderGraph.AddUnsafePass<SetGlobalAOPassData>("Set Global AO Texture", out var passData, profilingSampler))
            {
                builder.UseTexture(aoTexture);
                passData.AOTexture = aoTexture;

                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                
                builder.SetRenderFunc((SetGlobalAOPassData data, UnsafeGraphContext context) =>
                {
                    context.cmd.SetGlobalTexture(ShaderConstants._ScreenSpaceOcclusionTexture, data.AOTexture);
                });
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

        // RenderGraph PassData structs
        private class RenderAOPassData
        {
            internal GroundTruthAmbientOcclusionVariables Variables;
            internal ComputeShader TracingCS;
            internal int TracingKernel;
            internal Material Material;
            internal int RTWidth;
            internal int RTHeight;
            internal bool TracingInCS;
            
            internal TextureHandle AOPackedData;
            internal TextureHandle DepthTexture;
            internal TextureHandle StencilTexture;
            internal TextureHandle NormalBuffer;
        }

        private class SpatialDenoiseAOPassData
        {
            internal GroundTruthAmbientOcclusionVariables Variables;
            internal ComputeShader SpatialDenoiseCS;
            internal int DenoiseKernel;
            internal int RTWidth;
            internal int RTHeight;
            internal bool DownSample;
            
            internal TextureHandle PackedData;
            internal TextureHandle DenoiseOutput;
        }

        private class UpsampleAOPassData
        {
            internal GroundTruthAmbientOcclusionVariables Variables;
            internal ComputeShader UpsampleCS;
            internal int UpsampleKernel;
            internal int RTWidth;
            internal int RTHeight;
            
            internal TextureHandle Input;
            internal TextureHandle Output;
        }

        private class BilateralBlurAOPassData
        {
            internal Material Material;
            internal TextureHandle Source;
            internal TextureHandle Destination;
            internal ShaderPasses Pass;
        }

        private class GaussianBlurAOPassData
        {
            internal Material Material;
            internal TextureHandle Source;
            internal TextureHandle Destination;
            internal ShaderPasses Pass;
        }
        
        private class SetGlobalVectorPassData
        {
            internal Vector4 AmbientOcclusionParam;
        }

        private class SetGlobalAOPassData
        {
            internal TextureHandle AOTexture;
        }

        private static class ShaderConstants
        {
            public static readonly int _ScreenSpaceOcclusionTexture = MemberNameHelpers.ShaderPropertyID();
            
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