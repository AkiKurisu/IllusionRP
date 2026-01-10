using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if UNITY_2023_1_OR_NEWER
using UnityEngine.Experimental.Rendering.RenderGraphModule;
#endif

namespace Illusion.Rendering
{
    /// <summary>
    /// Render subsurface scattering.
    /// </summary>
    public class SubsurfaceScatteringPass : ScriptableRenderPass, IDisposable
    {
        private readonly LazyMaterial _scatteringMaterial = new(IllusionShaders.SubsurfaceScattering);

        private readonly IllusionRendererData _rendererData;

        private readonly ComputeShader _computeShader;

        private readonly int _subsurfaceScatteringKernel;

        public SubsurfaceScatteringPass(IllusionRendererData rendererData)
        {
            _rendererData = rendererData;
            renderPassEvent = IllusionRenderPassEvent.SubsurfaceScatteringPass;
            profilingSampler = new ProfilingSampler("Subsurface Scattering");
            _computeShader = rendererData.RuntimeResources.subsurfaceScatteringCS;
            _subsurfaceScatteringKernel = _computeShader.FindKernel("SubsurfaceScattering");
            
            // fill the list with the max number of diffusion profile so we dont have
            // the error: exceeds previous array size (5 vs 3). Cap to previous size.
            _sssShapeParamsAndMaxScatterDists = new Vector4[DiffusionProfileAsset.DIFFUSION_PROFILE_COUNT];
            _sssTransmissionTintsAndFresnel0 = new Vector4[DiffusionProfileAsset.DIFFUSION_PROFILE_COUNT];
            _sssDisabledTransmissionTintsAndFresnel0 = new Vector4[DiffusionProfileAsset.DIFFUSION_PROFILE_COUNT];
            _sssWorldScalesAndFilterRadiiAndThicknessRemaps = new Vector4[DiffusionProfileAsset.DIFFUSION_PROFILE_COUNT];
            _sssDiffusionProfileHashes = new uint[DiffusionProfileAsset.DIFFUSION_PROFILE_COUNT];
            _sssDiffusionProfileUpdate = new int[DiffusionProfileAsset.DIFFUSION_PROFILE_COUNT];
            _sssSetDiffusionProfiles = new DiffusionProfileAsset[DiffusionProfileAsset.DIFFUSION_PROFILE_COUNT];
        }

        private readonly RTHandle[] _diffuseRT = new RTHandle[3];

        private static readonly ShaderTagId SubsurfaceDiffuseShaderTagId = new(IllusionShaderPasses.SubsurfaceDiffuse);

        private int _width;

        private int _height;

        private FilteringSettings _filteringSettings = new(RenderQueueRange.opaque);

        private readonly ProfilingSampler _samplingProfiler = new("Irradiance");

        private readonly ProfilingSampler _scatteringProfiler = new("Scattering");

        private readonly RenderTargetIdentifier[] _colorBuffers = new RenderTargetIdentifier[2];

        private ShaderVariablesSubsurface _shaderVariablesSubsurface;

        private bool _scatteringInCS;

        private bool _nativeRenderPass;

        private readonly bool _supportFastRendering = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RGB111110Float);
        
        // List of every diffusion profile data we need
        private readonly Vector4[] _sssShapeParamsAndMaxScatterDists;

        private readonly Vector4[] _sssTransmissionTintsAndFresnel0;

        private readonly Vector4[] _sssDisabledTransmissionTintsAndFresnel0;

        private readonly Vector4[] _sssWorldScalesAndFilterRadiiAndThicknessRemaps;

        private readonly uint[] _sssDiffusionProfileHashes;

        private readonly int[] _sssDiffusionProfileUpdate;

        private readonly DiffusionProfileAsset[] _sssSetDiffusionProfiles;

        private int _sssActiveDiffusionProfileCount;
        
        private int _sampleBudget;
        
        private unsafe struct ShaderVariablesSubsurface
        {
            public fixed float ShapeParamsAndMaxScatterDists[DiffusionProfileAsset.DIFFUSION_PROFILE_COUNT * 4];   // RGB = S = 1 / D, A = d = RgbMax(D)
            
            public fixed float TransmissionTintsAndFresnel0[DiffusionProfileAsset.DIFFUSION_PROFILE_COUNT * 4];  // RGB = 1/4 * color, A = fresnel0

            public fixed float WorldScalesAndFilterRadiiAndThicknessRemaps[DiffusionProfileAsset.DIFFUSION_PROFILE_COUNT * 4]; // X = meters per world unit, Y = filter radius (in mm), Z = remap start, W = end - start
            
            public fixed uint DiffusionProfileHashTable[DiffusionProfileAsset.DIFFUSION_PROFILE_COUNT * 4];
            
            public uint DiffusionProfileCount;
            
            public Vector3 Padding;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            _scatteringInCS = _rendererData.PreferComputeShader;
            _nativeRenderPass = _rendererData.NativeRenderPass && !_scatteringInCS && renderingData.cameraData.isRenderPassSupportedCamera;
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;
            desc.msaaSamples = 1;
            desc.colorFormat = RenderTextureFormat.ARGBHalf;
            _width = desc.width;
            _height = desc.height;

            if (!_nativeRenderPass)
            {
                RenderingUtils.ReAllocateIfNeeded(ref _diffuseRT[0], desc, FilterMode.Point, TextureWrapMode.Clamp,
                    name: nameof(ShaderIDs._SubsurfaceDiffuse));

                RenderingUtils.ReAllocateIfNeeded(ref _diffuseRT[1], desc, FilterMode.Point, TextureWrapMode.Clamp,
                    name: nameof(ShaderIDs._SubsurfaceAlbedo));
            }
            
            desc.enableRandomWrite = _scatteringInCS;
            desc.colorFormat = _supportFastRendering ? RenderTextureFormat.RGB111110Float : RenderTextureFormat.ARGBHalf;
            RenderingUtils.ReAllocateIfNeeded(ref _diffuseRT[2], desc, FilterMode.Point, TextureWrapMode.Clamp,
                name: nameof(ShaderIDs._SubsurfaceLighting));
            
            cmd.SetGlobalTexture(ShaderIDs._SubsurfaceLighting, _diffuseRT[2]);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            PrepareSubsurfaceData();

            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, profilingSampler))
            {
                // Always set global buffer
                ConstantBuffer.PushGlobal(cmd, _shaderVariablesSubsurface, ShaderIDs._ShaderVariablesSubsurface);
                
                // Match HDRP Projection Matrix, pre-handle reverse z.
                var inverseProjectMatrix = renderingData.cameraData.GetGPUProjectionMatrix().inverse;
                cmd.SetGlobalMatrix(ShaderIDs._InvProjectMatrix, inverseProjectMatrix);
                cmd.SetViewProjectionMatrices(renderingData.cameraData.GetViewMatrix(), renderingData.cameraData.GetProjectionMatrix());
                cmd.SetViewport(new Rect(0f, 0f, _width, _height));

                var param = VolumeManager.instance.stack.GetComponent<SubsurfaceScattering>();
                // Disney sss
                if (param.enable.value 
                    && _rendererData.IsLightingActive 
                    && renderingData.cameraData.cameraType is not (CameraType.Preview or CameraType.Reflection))
                {
                    ExecuteSubsurfaceScattering(cmd, context, ref renderingData);
                }
            }
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        private void SetDiffusionProfileAtIndex(DiffusionProfileAsset settings, int index)
        {
            // if the diffusion profile was already set and it haven't changed then there is nothing to upgrade
            if (_sssSetDiffusionProfiles[index] == settings && _sssDiffusionProfileUpdate[index] == settings.updateCount)
                return;

            // if the settings have not yet been initialized
            if (settings.profile.hash == 0)
                return;

            _sssShapeParamsAndMaxScatterDists[index] = settings.shapeParamAndMaxScatterDist;
            _sssTransmissionTintsAndFresnel0[index] = settings.transmissionTintAndFresnel0;
            _sssDisabledTransmissionTintsAndFresnel0[index] = settings.disabledTransmissionTintAndFresnel0;
            _sssWorldScalesAndFilterRadiiAndThicknessRemaps[index] = settings.worldScaleAndFilterRadiusAndThicknessRemap;
            _sssDiffusionProfileHashes[index] = settings.profile.hash;

            _sssSetDiffusionProfiles[index] = settings;
            _sssDiffusionProfileUpdate[index] = settings.updateCount;
        }

        private void UpdateCurrentDiffusionProfileSettings(SubsurfaceScattering param)
        {
            int profileCount = 0;
            var diffusionProfiles = param.diffusionProfiles;
            if (diffusionProfiles.value != null)
            {
                profileCount = diffusionProfiles.AccumulatedCount;
                for (int i = 0; i < diffusionProfiles.AccumulatedCount; i++)
                    SetDiffusionProfileAtIndex(diffusionProfiles.value[i], i);
            }
            
            _sssActiveDiffusionProfileCount = profileCount;
        }

        private unsafe void UpdateShaderVariablesSubsurface(SubsurfaceScattering param, ref ShaderVariablesSubsurface cb)
        {
            cb.DiffusionProfileCount = (uint)_sssActiveDiffusionProfileCount;
            for (int i = 0; i < _sssActiveDiffusionProfileCount; ++i)
            {
                for (int c = 0; c < 4; ++c) // Vector4 component
                {
                    cb.ShapeParamsAndMaxScatterDists[i * 4 + c] = _sssShapeParamsAndMaxScatterDists[i][c];
                    cb.TransmissionTintsAndFresnel0[i * 4 + c] = _sssTransmissionTintsAndFresnel0[i][c];
                    cb.WorldScalesAndFilterRadiiAndThicknessRemaps[i * 4 + c] = _sssWorldScalesAndFilterRadiiAndThicknessRemaps[i][c];
                }

                cb.DiffusionProfileHashTable[i * 4] = _sssDiffusionProfileHashes[i];
            }
        }

        private void PrepareSubsurfaceData()
        {
            var param = VolumeManager.instance.stack.GetComponent<SubsurfaceScattering>();
            UpdateCurrentDiffusionProfileSettings(param);
            UpdateShaderVariablesSubsurface(param, ref _shaderVariablesSubsurface);
            _sampleBudget = (int)param.sampleBudget.value;
        }

        private RTHandle GetPreDepthTexture(ref RenderingData renderingData)
        {
            var depthTexture = UniversalRenderingUtility.GetDepthTexture(renderingData.cameraData.renderer);
            var preDepthTexture = _rendererData.CameraPreDepthTextureRT;
            if (!preDepthTexture.IsValid() || renderingData.cameraData.cameraType == CameraType.Preview)
            {
                preDepthTexture = depthTexture;
            }

            return preDepthTexture;
        }

        private void ExecuteSubsurfaceScattering(CommandBuffer cmd, ScriptableRenderContext context, ref RenderingData renderingData)
        {
            // Configure Graphics Fence (AO)
            _rendererData.WaitOnAsyncGraphicsFence(cmd, IllusionGraphicsFenceEvent.AmbientOcclusion);
            
            if (_nativeRenderPass)
            {
                using (new ProfilingScope((CommandBuffer)null, profilingSampler))
                {
                    DoNativeRenderPass(context, ref renderingData);
                }
                return;
            }

            var colorTarget = renderingData.cameraData.renderer.cameraColorTargetHandle;
            var depthTarget = renderingData.cameraData.renderer.cameraDepthTargetHandle;
            SetupSplitLightingRenderTargets(cmd, ref renderingData);

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            using (new ProfilingScope(cmd, _samplingProfiler))
            {
                DoSplitLighting(cmd, context, ref renderingData);
            }

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            using (new ProfilingScope(cmd, _scatteringProfiler))
            {
                DoSubsurfaceScattering(cmd);
            }
            
            cmd.SetRenderTarget(colorTarget, depthTarget);

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
        }

        private void DoNativeRenderPass(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var camDesc = renderingData.cameraData.cameraTargetDescriptor;
            var depthTexture = GetPreDepthTexture(ref renderingData);
            int width = camDesc.width, height = camDesc.height, samples = Mathf.Max(1, camDesc.msaaSamples);
            var rtFormat = RenderTextureFormat.ARGBHalf;
            
            var diffuseDesc = new AttachmentDescriptor(rtFormat);
            diffuseDesc.ConfigureClear(Color.clear, 0);
            diffuseDesc.loadStoreTarget = BuiltinRenderTextureType.None;
            
            var albedoDesc = new AttachmentDescriptor(rtFormat);
            albedoDesc.ConfigureClear(Color.clear, 0);
            albedoDesc.loadStoreTarget = BuiltinRenderTextureType.None;
            
            rtFormat = _supportFastRendering ? RenderTextureFormat.RGB111110Float : RenderTextureFormat.ARGBHalf;
            var lightingDesc = new AttachmentDescriptor(rtFormat);
            lightingDesc.ConfigureTarget(_diffuseRT[2], false, true);
            lightingDesc.ConfigureClear(Color.clear, 0);
            
            var depthDesc = new AttachmentDescriptor(camDesc.depthStencilFormat);
            depthDesc.ConfigureTarget(depthTexture, true, true);
            
            const int kDepth = 0;
            const int kDiffuse = 1;
            const int kAlbedo = 2;
            const int kLighting = 3;
            var attachments = new NativeArray<AttachmentDescriptor>(4, Allocator.Temp);
            attachments[kDepth] = depthDesc;     // 0 -> Depth
            attachments[kDiffuse] = diffuseDesc;   // 1 -> Diffuse
            attachments[kAlbedo] = albedoDesc;    // 2 -> Albedo
            attachments[kLighting] = lightingDesc;  // 3 -> Lighting
            using (context.BeginScopedRenderPass(width, height, samples, attachments, depthAttachmentIndex: kDepth))
            {
                attachments.Dispose();

                var compositeBuffer = new NativeArray<int>(2, Allocator.Temp);
                compositeBuffer[0] = kDiffuse;
                compositeBuffer[1] = kAlbedo;
                using (context.BeginScopedSubPass(compositeBuffer, isDepthStencilReadOnly: false))
                {
                    compositeBuffer.Dispose();
                    CommandBuffer cmd = CommandBufferPool.Get();
                    using (new ProfilingScope(cmd, _samplingProfiler))
                    {
                        context.ExecuteCommandBuffer(cmd);
                        cmd.Clear();

                        var diffuseSetting = CreateDrawingSettings(SubsurfaceDiffuseShaderTagId, ref renderingData, SortingCriteria.None);
                        context.DrawRenderers(renderingData.cullResults, ref diffuseSetting, ref _filteringSettings);
                    }

                    context.ExecuteCommandBuffer(cmd);
                    cmd.Clear();
                    CommandBufferPool.Release(cmd);
                    // Need to execute it immediately to avoid sync issues between context and cmd buffer
                    context.ExecuteCommandBuffer(renderingData.commandBuffer);
                    renderingData.commandBuffer.Clear();
                }

                var compositeTarget = new NativeArray<int>(1, Allocator.Temp);
                compositeTarget[0] = kLighting;
                var compositeInput = new NativeArray<int>(2, Allocator.Temp);
                compositeInput[0] = kDiffuse;
                compositeInput[1] = kAlbedo;
                using (context.BeginScopedSubPass(compositeTarget, compositeInput, isDepthStencilReadOnly: true))
                {
                    compositeTarget.Dispose();
                    compositeInput.Dispose();
                    CommandBuffer cmd = CommandBufferPool.Get();
                    using (new ProfilingScope(cmd, _scatteringProfiler))
                    {
                        _scatteringMaterial.Value.EnableKeyword(IllusionShaderKeywords._ILLUSION_RENDER_PASS_ENABLED);
                        Blitter.BlitTexture(cmd, Vector2.one, _scatteringMaterial.Value, 0);
                    }

                    context.ExecuteCommandBuffer(cmd);
                    cmd.Clear();
                    CommandBufferPool.Release(cmd);
                    // Need to execute it immediately to avoid sync issues between context and cmd buffer
                    context.ExecuteCommandBuffer(renderingData.commandBuffer);
                    renderingData.commandBuffer.Clear();
                }
            }
        }

        private void SetupSplitLightingRenderTargets(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var depthTexture = GetPreDepthTexture(ref renderingData);
            // MRT (Diffuse & Albedo & Depth)
            _colorBuffers[0] = _diffuseRT[0];
            _colorBuffers[1] = _diffuseRT[1];
            cmd.SetRenderTarget(_colorBuffers, depthTexture); // ignore transparent post depth
            cmd.ClearRenderTarget(false, true, Color.clear);
        }

        private void DoSplitLighting(CommandBuffer cmd, ScriptableRenderContext context, ref RenderingData renderingData)
        {
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            var diffuseSetting = CreateDrawingSettings(SubsurfaceDiffuseShaderTagId, ref renderingData, SortingCriteria.None);
            context.DrawRenderers(renderingData.cullResults, ref diffuseSetting, ref _filteringSettings);
        }

        private void DoSubsurfaceScattering(CommandBuffer cmd)
        {
            if (_scatteringInCS)
            {
                CoreUtils.SetRenderTarget(cmd, _diffuseRT[2], ClearFlag.Color, Color.clear);
                cmd.SetComputeIntParam(_computeShader, ShaderIDs._SssSampleBudget, _sampleBudget);
                cmd.SetComputeTextureParam(_computeShader, _subsurfaceScatteringKernel, ShaderIDs._SubsurfaceDiffuse, _diffuseRT[0]);
                cmd.SetComputeTextureParam(_computeShader, _subsurfaceScatteringKernel, ShaderIDs._SubsurfaceAlbedo, _diffuseRT[1]);
                cmd.SetComputeTextureParam(_computeShader, _subsurfaceScatteringKernel, ShaderIDs._SubsurfaceLighting, _diffuseRT[2]);

                var numTilesX = (_width + 15) / 16;
                var numTilesY = (_height + 15) / 16;
                var numTilesZ = IllusionRendererData.MaxViewCount;
                // Perform the SSS filtering pass
                cmd.DispatchCompute(_computeShader, _subsurfaceScatteringKernel, numTilesX, numTilesY, numTilesZ);
            }
            else
            {
                _scatteringMaterial.Value.DisableKeyword(IllusionShaderKeywords._ILLUSION_RENDER_PASS_ENABLED);
                _scatteringMaterial.Value.SetTexture(ShaderIDs._SubsurfaceDiffuse, _diffuseRT[0]);
                _scatteringMaterial.Value.SetTexture(ShaderIDs._SubsurfaceAlbedo, _diffuseRT[1]);
                Blitter.BlitCameraTexture(cmd, _diffuseRT[2], _diffuseRT[2], _scatteringMaterial.Value, 0);
            }
        }

#if UNITY_2023_1_OR_NEWER
        private class SplitLightingPassData
        {
            internal TextureHandle DiffuseTexture;
            internal TextureHandle AlbedoTexture;
            internal TextureHandle DepthTexture;
            internal RendererListHandle RendererListHdl;
            internal RenderingData RenderingData;
        }

        private class ScatteringComputePassData
        {
            internal ComputeShader ComputeShader;
            internal int SubsurfaceScatteringKernel;
            internal TextureHandle DiffuseTexture;
            internal TextureHandle AlbedoTexture;
            internal TextureHandle LightingTexture;
            internal int SampleBudget;
            internal int Width;
            internal int Height;
        }

        private class ScatteringRasterPassData
        {
            internal Material ScatteringMaterial;
            internal TextureHandle DiffuseTexture;
            internal TextureHandle AlbedoTexture;
            internal TextureHandle LightingTexture;
        }

        private class SetGlobalPassData
        {
            internal IllusionRendererData RendererData;
            internal TextureHandle SubsurfaceLightingTexture;
            internal ShaderVariablesSubsurface ShaderVariablesSubsurface;
            internal Matrix4x4 InverseProjectMatrix;
            internal Matrix4x4 ViewMatrix;
            internal Matrix4x4 ProjectionMatrix;
            internal int Width;
            internal int Height;
        }

        private void RenderSplitLighting(RenderGraph renderGraph, TextureHandle diffuseHandle, TextureHandle albedoHandle, 
            TextureHandle depthHandle, ref RenderingData renderingData)
        {
            using (var builder = renderGraph.AddRasterRenderPass<SplitLightingPassData>(
                "SSS Split Lighting", out var passData, _samplingProfiler))
            {
                // Setup MRT: diffuse (attachment 0) + albedo (attachment 1)
                passData.DiffuseTexture = builder.UseTextureFragment(diffuseHandle, 0);
                passData.AlbedoTexture = builder.UseTextureFragment(albedoHandle, 1);
                passData.DepthTexture = builder.UseTextureFragmentDepth(depthHandle, IBaseRenderGraphBuilder.AccessFlags.Read);
                passData.RenderingData = renderingData;

                // Create renderer list with SubsurfaceDiffuse shader tag
                var drawingSettings = CreateDrawingSettings(SubsurfaceDiffuseShaderTagId, ref renderingData, SortingCriteria.None);
                var rendererListParams = new RendererListParams(renderingData.cullResults, drawingSettings, _filteringSettings);
                passData.RendererListHdl = renderGraph.CreateRendererList(rendererListParams);
                builder.UseRendererList(passData.RendererListHdl);

                builder.AllowPassCulling(false);

                builder.SetRenderFunc(static (SplitLightingPassData data, RasterGraphContext context) =>
                {
                    // Clear render targets
                    context.cmd.ClearRenderTarget(false, true, Color.clear);
                    
                    // Draw renderers with SubsurfaceDiffuse shader tag
                    context.cmd.DrawRendererList(data.RendererListHdl);
                });
            }
        }

        private void RenderScatteringCompute(RenderGraph renderGraph, TextureHandle diffuseHandle, 
            TextureHandle albedoHandle, TextureHandle lightingHandle)
        {
            using (var builder = renderGraph.AddComputePass<ScatteringComputePassData>(
                "SSS Scattering (CS)", out var passData, _scatteringProfiler))
            {
                passData.ComputeShader = _computeShader;
                passData.SubsurfaceScatteringKernel = _subsurfaceScatteringKernel;
                passData.DiffuseTexture = builder.UseTexture(diffuseHandle);
                passData.AlbedoTexture = builder.UseTexture(albedoHandle);
                passData.LightingTexture = builder.UseTexture(lightingHandle, IBaseRenderGraphBuilder.AccessFlags.Write);
                passData.SampleBudget = _sampleBudget;
                passData.Width = _width;
                passData.Height = _height;

                builder.AllowPassCulling(false);

                builder.SetRenderFunc(static (ScatteringComputePassData data, ComputeGraphContext context) =>
                {
                    // Set compute shader parameters
                    context.cmd.SetComputeIntParam(data.ComputeShader, ShaderIDs._SssSampleBudget, data.SampleBudget);
                    context.cmd.SetComputeTextureParam(data.ComputeShader, data.SubsurfaceScatteringKernel, 
                        ShaderIDs._SubsurfaceDiffuse, data.DiffuseTexture);
                    context.cmd.SetComputeTextureParam(data.ComputeShader, data.SubsurfaceScatteringKernel, 
                        ShaderIDs._SubsurfaceAlbedo, data.AlbedoTexture);
                    context.cmd.SetComputeTextureParam(data.ComputeShader, data.SubsurfaceScatteringKernel, 
                        ShaderIDs._SubsurfaceLighting, data.LightingTexture);

                    var numTilesX = (data.Width + 15) / 16;
                    var numTilesY = (data.Height + 15) / 16;
                    var numTilesZ = IllusionRendererData.MaxViewCount;
                    
                    // Dispatch compute shader
                    context.cmd.DispatchCompute(data.ComputeShader, data.SubsurfaceScatteringKernel, 
                        numTilesX, numTilesY, numTilesZ);
                });
            }
        }

        private void RenderScatteringRaster(RenderGraph renderGraph, TextureHandle diffuseHandle, 
            TextureHandle albedoHandle, TextureHandle lightingHandle)
        {
            using (var builder = renderGraph.AddRasterRenderPass<ScatteringRasterPassData>(
                "SSS Scattering (FS)", out var passData, _scatteringProfiler))
            {
                passData.ScatteringMaterial = _scatteringMaterial.Value;
                passData.DiffuseTexture = builder.UseTexture(diffuseHandle);
                passData.AlbedoTexture = builder.UseTexture(albedoHandle);
                passData.LightingTexture = builder.UseTextureFragment(lightingHandle, 0);

                builder.AllowPassCulling(false);

                builder.SetRenderFunc(static (ScatteringRasterPassData data, RasterGraphContext context) =>
                {
                    // Disable native render pass keyword
                    data.ScatteringMaterial.DisableKeyword(IllusionShaderKeywords._ILLUSION_RENDER_PASS_ENABLED);
                    
                    // Set material textures
                    data.ScatteringMaterial.SetTexture(ShaderIDs._SubsurfaceDiffuse, data.DiffuseTexture);
                    data.ScatteringMaterial.SetTexture(ShaderIDs._SubsurfaceAlbedo, data.AlbedoTexture);
                    
                    // Blit with scattering material
                    Blitter.BlitTexture(context.cmd, data.LightingTexture, Vector2.one, data.ScatteringMaterial, 0);
                });
            }
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, FrameResources frameResources, ref RenderingData renderingData)
        {
            // Prepare subsurface scattering data
            PrepareSubsurfaceData();

            // Setup texture allocation
            _scatteringInCS = _rendererData.PreferComputeShader;
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;
            desc.msaaSamples = 1;
            desc.colorFormat = RenderTextureFormat.ARGBHalf;
            _width = desc.width;
            _height = desc.height;

            // Allocate diffuse and albedo textures
            RenderingUtils.ReAllocateIfNeeded(ref _diffuseRT[0], desc, FilterMode.Point, TextureWrapMode.Clamp,
                name: nameof(ShaderIDs._SubsurfaceDiffuse));

            RenderingUtils.ReAllocateIfNeeded(ref _diffuseRT[1], desc, FilterMode.Point, TextureWrapMode.Clamp,
                name: nameof(ShaderIDs._SubsurfaceAlbedo));

            // Allocate lighting texture
            desc.enableRandomWrite = _scatteringInCS;
            desc.colorFormat = _supportFastRendering ? RenderTextureFormat.RGB111110Float : RenderTextureFormat.ARGBHalf;
            RenderingUtils.ReAllocateIfNeeded(ref _diffuseRT[2], desc, FilterMode.Point, TextureWrapMode.Clamp, 
                name: nameof(ShaderIDs._SubsurfaceLighting));

            // Import textures into RenderGraph
            TextureHandle diffuseHandle = renderGraph.ImportTexture(_diffuseRT[0]);
            TextureHandle albedoHandle = renderGraph.ImportTexture(_diffuseRT[1]);
            TextureHandle lightingHandle = renderGraph.ImportTexture(_diffuseRT[2]);

            // Get depth texture
            var preDepthTexture = _rendererData.CameraPreDepthTextureRT;
            if (!preDepthTexture.IsValid() || renderingData.cameraData.cameraType == CameraType.Preview)
            {
                preDepthTexture = frameResources.GetTexture(UniversalResource.CameraDepthTexture);
            }
            TextureHandle depthHandle = renderGraph.ImportTexture(preDepthTexture);
            var invProjectMatrix = IllusionRenderingUtils.GetGPUProjectionMatrix(ref renderingData.cameraData, _diffuseRT[2]).inverse;

            // Check if SSS is enabled
            var param = VolumeManager.instance.stack.GetComponent<SubsurfaceScattering>();
            if (!param.enable.value 
                || !_rendererData.IsLightingActive 
                || renderingData.cameraData.cameraType is CameraType.Preview or CameraType.Reflection)
            {
                // Set global texture even if disabled
                RenderGraphUtils.SetGlobalTexture(renderGraph, ShaderIDs._SubsurfaceLighting, lightingHandle);
                return;
            }

            // Pass 1: Set global variables and constant buffer
            using (var builder = renderGraph.AddComputePass<SetGlobalPassData>(
                "SSS Setup Global Variables", out var setupPassData, new ProfilingSampler("SSS Setup")))
            {
                setupPassData.RendererData = _rendererData;
                setupPassData.SubsurfaceLightingTexture = builder.UseTexture(lightingHandle);
                setupPassData.ShaderVariablesSubsurface = _shaderVariablesSubsurface;
                setupPassData.InverseProjectMatrix = invProjectMatrix;
                setupPassData.ViewMatrix = renderingData.cameraData.GetViewMatrix();
                setupPassData.ProjectionMatrix = renderingData.cameraData.GetProjectionMatrix();
                setupPassData.Width = _width;
                setupPassData.Height = _height;

                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((SetGlobalPassData data, ComputeGraphContext context) =>
                {
                    // Set global buffer
                    ComputeConstantBuffer.PushGlobal(context.cmd, data.ShaderVariablesSubsurface, ShaderIDs._ShaderVariablesSubsurface);

                    // Set global matrices
                    context.cmd.SetGlobalMatrix(ShaderIDs._InvProjectMatrix, data.InverseProjectMatrix);
                    context.cmd.SetViewProjectionMatrices(data.ViewMatrix, data.ProjectionMatrix);
                    context.cmd.SetViewport(new Rect(0f, 0f, data.Width, data.Height));

                    // Set global texture
                    context.cmd.SetGlobalTexture(ShaderIDs._SubsurfaceLighting, data.SubsurfaceLightingTexture);
                });
            }

            // Pass 2: Split lighting rendering (RasterPass with MRT)
            RenderSplitLighting(renderGraph, diffuseHandle, albedoHandle, depthHandle, ref renderingData);

            // Pass 3: Subsurface scattering (Compute or Raster)
            if (_scatteringInCS)
            {
                RenderScatteringCompute(renderGraph, diffuseHandle, albedoHandle, lightingHandle);
            }
            else
            {
                RenderScatteringRaster(renderGraph, diffuseHandle, albedoHandle, lightingHandle);
            }

            // Set global texture for shaders
            RenderGraphUtils.SetGlobalTexture(renderGraph, ShaderIDs._SubsurfaceLighting, lightingHandle);
        }
#endif

        public void Dispose()
        {
            foreach (var rt in _diffuseRT)
            {
                rt?.Release();
            }
            _scatteringMaterial.DestroyCache();
        }

        private static class ShaderIDs
        {
            public static readonly int _ShaderVariablesSubsurface = Shader.PropertyToID("ShaderVariablesSubsurface");
            
            public static readonly int _SubsurfaceDiffuse = MemberNameHelpers.ShaderPropertyID();

            public static readonly int _SubsurfaceAlbedo = MemberNameHelpers.ShaderPropertyID();

            public static readonly int _SubsurfaceLighting = MemberNameHelpers.ShaderPropertyID();

            public static readonly int _InvProjectMatrix = MemberNameHelpers.ShaderPropertyID();
            
            public static readonly int _SssSampleBudget = MemberNameHelpers.ShaderPropertyID();
        }
    }
}