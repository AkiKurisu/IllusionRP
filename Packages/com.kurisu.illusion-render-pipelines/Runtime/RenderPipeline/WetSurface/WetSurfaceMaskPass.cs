using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering
{
    internal sealed class WetSurfaceResetPass : ScriptableRenderPass
    {
        private static readonly int ActiveId = Shader.PropertyToID("_WetSurfaceActive");
        private static readonly int PackedEnabledId = Shader.PropertyToID("_WetSurfacePackedEnabled");
        private bool _packedEnabled;

        private sealed class PassData { internal bool PackedEnabled; }

        internal WetSurfaceResetPass() => renderPassEvent = RenderPassEvent.BeforeRendering;

        internal void SetEnabled(bool enabled) => _packedEnabled = enabled;

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            using (var builder = renderGraph.AddUnsafePass<PassData>("Reset Wet Surface", out var passData))
            {
                passData.PackedEnabled = _packedEnabled;
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (PassData data, UnsafeGraphContext context) =>
                {
                    context.cmd.SetGlobalFloat(ActiveId, 0f);
                    context.cmd.SetGlobalFloat(PackedEnabledId, data.PackedEnabled ? 1f : 0f);
                });
            }
        }
    }

    internal sealed class WetSurfaceFrameData : ContextItem
    {
        internal TextureHandle Mask;
        internal TextureHandle Coverage;
        internal TextureHandle ForwardBuffer;
        internal TextureHandle ModifiedForwardBuffer;

        public override void Reset()
        {
            Mask = TextureHandle.nullHandle;
            Coverage = TextureHandle.nullHandle;
            ForwardBuffer = TextureHandle.nullHandle;
            ModifiedForwardBuffer = TextureHandle.nullHandle;
        }
    }

    internal sealed class WetSurfaceMaskPass : ScriptableRenderPass, IDisposable
    {
        private static readonly Color[] ClearColors = { Color.clear, Color.clear };
        private static readonly int MaskId = Shader.PropertyToID("_WetSurfaceMask");
        private static readonly int DepthId = Shader.PropertyToID("_WetSourceDepth");
        private static readonly int NormalId = Shader.PropertyToID("_WetSourceNormal");
        private static readonly int BlurInputId = Shader.PropertyToID("_WetBlurInput");
        private static readonly int BlurDepthId = Shader.PropertyToID("_WetBlurDepth");
        private static readonly int BlurMaskId = Shader.PropertyToID("_WetBlurMask");
        private static readonly int BlurCoverageId = Shader.PropertyToID("_WetBlurCoverage");
        private static readonly int BlurTexelSizeId = Shader.PropertyToID("_WetBlurTexelSize");
        private static readonly int ActiveId = Shader.PropertyToID("_WetSurfaceActive");
        private static readonly int WetNormalsId = Shader.PropertyToID("_WetSurfaceNormals");
        private static readonly int ForwardSourceId = Shader.PropertyToID("_WetSurfaceForwardSource");

        private readonly Material _material;
        private readonly Material _blurMaterial;
        private readonly Material _smoothnessMaterial;
        private readonly IllusionRendererData _rendererData;
        private readonly Texture2D _blueNoise;
        private RTHandle _maskRT;
        private WetSurfaceDecalData[] _decals = Array.Empty<WetSurfaceDecalData>();
        private bool _reportedMissingResources;

        private sealed class PassData
        {
            internal TextureHandle Depth;
            internal TextureHandle Normals;
            internal Material Material;
            internal MaterialPropertyBlock[] Properties;
            internal int[] PassIndices;
        }

        private sealed class BlurData
        {
            internal TextureHandle Source;
            internal TextureHandle Depth;
            internal TextureHandle Mask;
            internal TextureHandle Coverage;
            internal Material Material;
            internal Vector4 TexelSize;
            internal int ShaderPass;
        }

        private sealed class SmoothnessData
        {
            internal TextureHandle Source;
            internal TextureHandle Mask;
            internal Material Material;
            internal int ShaderPass;
        }

        internal WetSurfaceMaskPass(IllusionRendererData rendererData, Shader shader, Shader blurShader,
            Shader smoothnessShader, Texture2D blueNoise)
        {
            _rendererData = rendererData;
            _blueNoise = blueNoise;
            renderPassEvent = RenderPassEvent.AfterRenderingPrePasses;
            profilingSampler = new ProfilingSampler("Wet Surface Mask");
            ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal);
            _material = CoreUtils.CreateEngineMaterial(shader);
            _blurMaterial = CoreUtils.CreateEngineMaterial(blurShader);
            _smoothnessMaterial = CoreUtils.CreateEngineMaterial(smoothnessShader);
        }

        internal void SetDecals(IReadOnlyList<WetSurfaceDecalData> decals)
        {
            _decals = new WetSurfaceDecalData[decals.Count];
            for (int i = 0; i < decals.Count; i++)
                _decals[i] = decals[i];
        }

        public void Dispose()
        {
            CoreUtils.Destroy(_material);
            CoreUtils.Destroy(_blurMaterial);
            CoreUtils.Destroy(_smoothnessMaterial);
            _maskRT?.Release();
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalCameraData camera = frameData.Get<UniversalCameraData>();
            UniversalResourceData resources = frameData.Get<UniversalResourceData>();
            TextureHandle depth = frameData.GetDepthWriteTextureHandle();
            TextureHandle normals = resources.cameraNormalsTexture;
            WetSurfaceFrameData wetData = frameData.GetOrCreate<WetSurfaceFrameData>();
            if (!_material || !_blurMaterial || !_smoothnessMaterial || !_blueNoise)
            {
                if (!_reportedMissingResources)
                {
                    Debug.LogError("Wet Surface resources are missing from IllusionRenderPipelineResources.");
                    _reportedMissingResources = true;
                }
                return;
            }
            if (!depth.IsValid() || !normals.IsValid() ||
                !wetData.ForwardBuffer.IsValid())
            {
                if (!_reportedMissingResources)
                {
                    Debug.LogError("Wet Surface depth, normal, or forward material data are unavailable.");
                    _reportedMissingResources = true;
                }
                return;
            }
            if (_decals.Length == 0)
                return;
            _reportedMissingResources = false;

            var descriptor = camera.cameraTargetDescriptor;
            if (!SystemInfo.IsFormatSupported(GraphicsFormat.R32_SFloat, GraphicsFormatUsage.Blend))
            {
                Debug.LogError("Wet Surface requires R32_SFloat blending on this device.");
                return;
            }

            descriptor.graphicsFormat = GraphicsFormat.R32_SFloat;
            descriptor.depthStencilFormat = GraphicsFormat.None;
            descriptor.msaaSamples = 1;
            RenderingUtils.ReAllocateHandleIfNeeded(ref _maskRT, descriptor,
                FilterMode.Point, TextureWrapMode.Clamp, name: "_WetSurfaceMask");
            TextureHandle mask = renderGraph.ImportTexture(_maskRT);
            descriptor.graphicsFormat = GraphicsFormat.R8_UNorm;
            // Separate coverage target instead of a stencil bit: stencil bit 0 belongs to SSAO.
            TextureHandle coverage = UniversalRenderer.CreateRenderGraphTexture(renderGraph,
                descriptor, "_WetSurfaceCoverage", false);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Wet Surface Mask", out var passData,
                       profilingSampler))
            {
                passData.Depth = depth;
                passData.Normals = normals;
                passData.Material = _material;
                passData.Properties = new MaterialPropertyBlock[_decals.Length];
                passData.PassIndices = new int[_decals.Length];
                for (int i = 0; i < _decals.Length; i++)
                {
                    WetSurfaceDecalData decal = _decals[i];
                    var properties = new MaterialPropertyBlock();
                    WetSurfaceDecalProperties.Apply(properties, decal, _blueNoise,
                        1f / _rendererData.ScaleSquaredWorldDistance(decal.WorldProjectionScale));
                    passData.Properties[i] = properties;
                    passData.PassIndices[i] = (decal.IsDry ? 2 : 0) + (decal.IsSphere ? 1 : 0);
                }

                builder.UseTexture(depth, AccessFlags.Read);
                builder.UseTexture(normals, AccessFlags.Read);
                builder.SetRenderAttachment(mask, 0, AccessFlags.Write);
                builder.SetRenderAttachment(coverage, 1, AccessFlags.Write);
                builder.SetGlobalTextureAfterPass(mask, MaskId);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    context.cmd.ClearRenderTarget(
                        RTClearFlags.Color0 | RTClearFlags.Color1, ClearColors, 1f, 0);
                    context.cmd.SetGlobalTexture(DepthId, data.Depth);
                    context.cmd.SetGlobalTexture(NormalId, data.Normals);
                    for (int mode = 0; mode < 2; mode++)
                    for (int i = 0; i < data.Properties.Length; i++)
                    {
                        int pass = data.PassIndices[i];
                        if ((pass >= 2 ? 1 : 0) != mode)
                            continue;
                        context.cmd.DrawProcedural(Matrix4x4.identity, data.Material, pass,
                            MeshTopology.Triangles, 3, 1, data.Properties[i]);
                    }
                });
            }

            wetData.Mask = mask;
            wetData.Coverage = coverage;
            RecordNormalBlur(renderGraph, camera, depth, normals, mask, coverage);
            wetData.ModifiedForwardBuffer = RecordSmoothness(renderGraph, camera,
                mask, wetData.ForwardBuffer);
        }

        private TextureHandle RecordSmoothness(RenderGraph renderGraph, UniversalCameraData camera,
            TextureHandle mask, TextureHandle forwardBuffer)
        {
            if (!_smoothnessMaterial)
                return TextureHandle.nullHandle;

            var descriptor = camera.cameraTargetDescriptor;
            descriptor.graphicsFormat = SystemInfo.IsFormatSupported(GraphicsFormat.R8_UNorm,
                GraphicsFormatUsage.Blend) ? GraphicsFormat.R8_UNorm : GraphicsFormat.B8G8R8A8_UNorm;
            descriptor.depthStencilFormat = GraphicsFormat.None;
            descriptor.msaaSamples = 1;
            TextureHandle modified = UniversalRenderer.CreateRenderGraphTexture(renderGraph,
                descriptor, "_WetSurfaceModifiedSmoothness", false);

            using (var builder = renderGraph.AddRasterRenderPass<SmoothnessData>(
                       "Wet Surface Smoothness Modify", out var passData))
            {
                passData.Source = forwardBuffer;
                passData.Mask = mask;
                passData.Material = _smoothnessMaterial;
                passData.ShaderPass = 0;
                builder.UseTexture(forwardBuffer, AccessFlags.Read);
                builder.UseTexture(mask, AccessFlags.Read);
                builder.SetRenderAttachment(modified, 0, AccessFlags.Write);
                builder.SetGlobalTextureAfterPass(modified, IllusionShaderProperties._ForwardGBuffer);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (SmoothnessData data, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalTexture(ForwardSourceId, data.Source);
                    context.cmd.SetGlobalTexture(MaskId, data.Mask);
                    context.cmd.DrawProcedural(Matrix4x4.identity, data.Material,
                        data.ShaderPass, MeshTopology.Triangles, 3);
                });
            }
            return modified;
        }

        private void RecordNormalBlur(RenderGraph renderGraph, UniversalCameraData camera,
            TextureHandle depth, TextureHandle normals, TextureHandle mask, TextureHandle coverage)
        {
            if (!_blurMaterial)
                return;

            var descriptor = camera.cameraTargetDescriptor;
            descriptor.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
            descriptor.depthStencilFormat = GraphicsFormat.None;
            descriptor.msaaSamples = 1;
            TextureHandle horizontal = UniversalRenderer.CreateRenderGraphTexture(renderGraph,
                descriptor, "_WetSurfaceNormalsHorizontal", true, Color.clear);
            var texelSize = new Vector4(1f / descriptor.width, 1f / descriptor.height,
                descriptor.width, descriptor.height);

            RecordBlurStep(renderGraph, "Wet Surface Normal Blur Horizontal", normals, depth,
                mask, coverage, horizontal, texelSize, 0);
            RecordBlurStep(renderGraph, "Wet Surface Normal Blur Vertical", horizontal, depth,
                mask, coverage, normals, texelSize, 1);
        }

        private void RecordBlurStep(RenderGraph renderGraph, string name, TextureHandle source,
            TextureHandle depth, TextureHandle mask, TextureHandle coverage, TextureHandle destination,
            Vector4 texelSize, int shaderPass)
        {
            using (var builder = renderGraph.AddRasterRenderPass<BlurData>(name, out var passData))
            {
                passData.Source = source;
                passData.Depth = depth;
                passData.Mask = mask;
                passData.Coverage = coverage;
                passData.Material = _blurMaterial;
                passData.TexelSize = texelSize;
                passData.ShaderPass = shaderPass;
                builder.UseTexture(source, AccessFlags.Read);
                builder.UseTexture(depth, AccessFlags.Read);
                builder.UseTexture(mask, AccessFlags.Read);
                builder.UseTexture(coverage, AccessFlags.Read);
                builder.SetRenderAttachment(destination, 0,
                    shaderPass == 1 ? AccessFlags.ReadWrite : AccessFlags.Write);
                builder.AllowGlobalStateModification(true);
                if (shaderPass == 1)
                    builder.SetGlobalTextureAfterPass(destination, WetNormalsId);
                builder.SetRenderFunc(static (BlurData data, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalTexture(BlurInputId, data.Source);
                    context.cmd.SetGlobalTexture(BlurDepthId, data.Depth);
                    context.cmd.SetGlobalTexture(BlurMaskId, data.Mask);
                    context.cmd.SetGlobalTexture(BlurCoverageId, data.Coverage);
                    context.cmd.SetGlobalVector(BlurTexelSizeId, data.TexelSize);
                    if (data.ShaderPass == 1)
                        context.cmd.SetGlobalFloat(ActiveId, 1f);
                    context.cmd.DrawProcedural(Matrix4x4.identity, data.Material,
                        data.ShaderPass, MeshTopology.Triangles, 3);
                });
            }
        }
    }
}
