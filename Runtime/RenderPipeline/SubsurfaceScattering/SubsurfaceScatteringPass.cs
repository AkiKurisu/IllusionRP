using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

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

        private readonly FilteringSettings _filteringSettings = new(RenderQueueRange.opaque);

        private readonly ProfilingSampler _samplingProfiler = new("Irradiance");

        private readonly ProfilingSampler _scatteringProfiler = new("Scattering");

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

        private readonly Color[] _backGroundColors = { Color.clear, Color.clear };
        
        private unsafe struct ShaderVariablesSubsurface
        {
            public fixed float ShapeParamsAndMaxScatterDists[DiffusionProfileAsset.DIFFUSION_PROFILE_COUNT * 4];   // RGB = S = 1 / D, A = d = RgbMax(D)
            
            public fixed float TransmissionTintsAndFresnel0[DiffusionProfileAsset.DIFFUSION_PROFILE_COUNT * 4];  // RGB = 1/4 * color, A = fresnel0

            public fixed float WorldScalesAndFilterRadiiAndThicknessRemaps[DiffusionProfileAsset.DIFFUSION_PROFILE_COUNT * 4]; // X = meters per world unit, Y = filter radius (in mm), Z = remap start, W = end - start
            
            public fixed uint DiffusionProfileHashTable[DiffusionProfileAsset.DIFFUSION_PROFILE_COUNT * 4];
            
            public uint DiffusionProfileCount;
            
            public Vector3 Padding;
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

        private unsafe void UpdateShaderVariablesSubsurface(ref ShaderVariablesSubsurface cb)
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
            UpdateShaderVariablesSubsurface(ref _shaderVariablesSubsurface);
            _sampleBudget = (int)param.sampleBudget.value;
        }
        
        private class SplitLightingPassData
        {
            internal RendererListHandle RendererListHdl;
            internal Color[] BackGroundColors;
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
            internal TextureHandle SubsurfaceLightingTexture;
            internal ShaderVariablesSubsurface ShaderVariablesSubsurface;
            internal Matrix4x4 ViewMatrix;
            internal Matrix4x4 ProjectionMatrix;
            internal int Width;
            internal int Height;
        }

        private void RenderSplitLighting(RenderGraph renderGraph, TextureHandle diffuseHandle, TextureHandle albedoHandle, 
            TextureHandle depthHandle, ContextContainer frameData)
        {
            UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();
            using (var builder = renderGraph.AddRasterRenderPass<SplitLightingPassData>(
                "SSS Split Lighting", out var passData, _samplingProfiler))
            {
                // Setup MRT: diffuse (attachment 0) + albedo (attachment 1)
                builder.SetRenderAttachment(diffuseHandle, 0);
                builder.SetRenderAttachment(albedoHandle, 1);
                builder.SetRenderAttachmentDepth(depthHandle);

                passData.BackGroundColors = _backGroundColors;

                // Create renderer list with SubsurfaceDiffuse shader tag
                var drawingSettings = CreateDrawingSettings(SubsurfaceDiffuseShaderTagId, renderingData, cameraData, lightData, SortingCriteria.None);
                var rendererListParams = new RendererListParams(renderingData.cullResults, drawingSettings, _filteringSettings);
                passData.RendererListHdl = renderGraph.CreateRendererList(rendererListParams);
                builder.UseRendererList(passData.RendererListHdl);

                builder.AllowPassCulling(false);

                builder.SetRenderFunc(static (SplitLightingPassData data, RasterGraphContext context) =>
                {
                    // Clear render targets
                    context.cmd.ClearRenderTarget(RTClearFlags.Color | RTClearFlags.Color1, data.BackGroundColors, 1, 0);
                    
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
                builder.UseTexture(diffuseHandle);
                passData.DiffuseTexture = diffuseHandle;
                builder.UseTexture(albedoHandle);
                passData.AlbedoTexture = albedoHandle;
                builder.UseTexture(lightingHandle, AccessFlags.Write);
                passData.LightingTexture = lightingHandle;
                passData.SampleBudget = _sampleBudget;
                passData.Width = _width;
                passData.Height = _height;

                builder.AllowPassCulling(false);

                builder.SetGlobalTextureAfterPass(lightingHandle, ShaderIDs._SubsurfaceLighting);
                
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
                builder.UseTexture(diffuseHandle);
                passData.DiffuseTexture = diffuseHandle;
                builder.UseTexture(albedoHandle);
                passData.AlbedoTexture = albedoHandle;
                builder.SetRenderAttachment(lightingHandle, 0);
                passData.LightingTexture = lightingHandle;

                builder.AllowPassCulling(false);

                builder.SetGlobalTextureAfterPass(lightingHandle, ShaderIDs._SubsurfaceLighting);
                
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

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resource = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            
            // Prepare subsurface scattering data
            PrepareSubsurfaceData();

            // Setup texture allocation
            _scatteringInCS = _rendererData.PreferComputeShader;
            var desc = cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;
            desc.msaaSamples = 1;
            desc.colorFormat = RenderTextureFormat.ARGBHalf;
            _width = desc.width;
            _height = desc.height;

            // Allocate diffuse and albedo textures
            RenderingUtils.ReAllocateHandleIfNeeded(ref _diffuseRT[0], desc, FilterMode.Point, TextureWrapMode.Clamp,
                name: nameof(ShaderIDs._SubsurfaceDiffuse));

            RenderingUtils.ReAllocateHandleIfNeeded(ref _diffuseRT[1], desc, FilterMode.Point, TextureWrapMode.Clamp,
                name: nameof(ShaderIDs._SubsurfaceAlbedo));

            // Allocate lighting texture
            desc.enableRandomWrite = _scatteringInCS;
            desc.colorFormat = _supportFastRendering ? RenderTextureFormat.RGB111110Float : RenderTextureFormat.ARGBHalf;
            RenderingUtils.ReAllocateHandleIfNeeded(ref _diffuseRT[2], desc, FilterMode.Point, TextureWrapMode.Clamp, 
                name: nameof(ShaderIDs._SubsurfaceLighting));

            // Import textures into RenderGraph
            TextureHandle diffuseHandle = renderGraph.ImportTexture(_diffuseRT[0]);
            TextureHandle albedoHandle = renderGraph.ImportTexture(_diffuseRT[1]);
            TextureHandle lightingHandle = renderGraph.ImportTexture(_diffuseRT[2]);

            // Get depth texture
            TextureHandle depthHandle;
            var preDepthTexture = _rendererData.CameraPreDepthTextureRT;
            if (!preDepthTexture.IsValid() || cameraData.cameraType == CameraType.Preview)
            {
                depthHandle = resource.cameraDepthTexture;
            }
            else
            {
                depthHandle = renderGraph.ImportTexture(preDepthTexture);
            }

            // Check if SSS is enabled
            var param = VolumeManager.instance.stack.GetComponent<SubsurfaceScattering>();
            if (!param.enable.value 
                || !_rendererData.IsLightingActive 
                || cameraData.cameraType is CameraType.Preview or CameraType.Reflection)
            {
                // Set global texture even if disabled
                RenderGraphUtils.SetGlobalTexture(renderGraph, ShaderIDs._SubsurfaceLighting, lightingHandle);
                return;
            }

            // Pass 1: Set global variables and constant buffer
            using (var builder = renderGraph.AddComputePass<SetGlobalPassData>(
                "SSS Setup Global Variables", out var setupPassData, new ProfilingSampler("SSS Setup")))
            {
                builder.UseTexture(lightingHandle);
                setupPassData.SubsurfaceLightingTexture = lightingHandle;
                setupPassData.ShaderVariablesSubsurface = _shaderVariablesSubsurface;
                setupPassData.ViewMatrix = cameraData.GetViewMatrix();
                setupPassData.ProjectionMatrix = cameraData.GetProjectionMatrix();
                setupPassData.Width = _width;
                setupPassData.Height = _height;

                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((SetGlobalPassData data, ComputeGraphContext context) =>
                {
                    // Set global buffer
                    ConstantBuffer.PushGlobal(context.cmd, data.ShaderVariablesSubsurface, ShaderIDs._ShaderVariablesSubsurface);

                    // Set global matrices
                    context.cmd.SetViewProjectionMatrices(data.ViewMatrix, data.ProjectionMatrix);
                    context.cmd.SetViewport(new Rect(0f, 0f, data.Width, data.Height));

                    // Set global texture
                    context.cmd.SetGlobalTexture(ShaderIDs._SubsurfaceLighting, data.SubsurfaceLightingTexture);
                });
            }

            // Pass 2: Split lighting rendering (RasterPass with MRT)
            RenderSplitLighting(renderGraph, diffuseHandle, albedoHandle, depthHandle, frameData);

            // Pass 3: Subsurface scattering (Compute or Raster)
            if (_scatteringInCS)
            {
                RenderScatteringCompute(renderGraph, diffuseHandle, albedoHandle, lightingHandle);
            }
            else
            {
                RenderScatteringRaster(renderGraph, diffuseHandle, albedoHandle, lightingHandle);
            }
        }

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
            
            public static readonly int _SssSampleBudget = MemberNameHelpers.ShaderPropertyID();
        }
    }
}