using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using UnityEngine.Scripting;
using global::UnityRhi;

namespace Illusion.Rendering.UnityRHI
{
    /// <summary>
    /// Full-resolution DLSS Neural Rendering post-process for Unity 6.3 URP.
    /// It consumes raster color, depth and motion vectors and has no RTXPT dependency.
    /// </summary>
    [Preserve]
    internal sealed class UnityRHIDLSSNeuralRenderingBackend : IDLSSNeuralRenderingBackend
    {
        private Material _prepareMaterial;
        private Material _debugMaterial;
        private Material _resolveMaterial;
        private DLSSNeuralRenderingPass _pass;
        private readonly Dictionary<Camera, DLSSNeuralRenderingCameraContext> _contexts =
            new Dictionary<Camera, DLSSNeuralRenderingCameraContext>();
        private readonly List<Camera> _deadCameras = new List<Camera>();
        private bool _warnedUnavailable;
        private bool _warnedHdr;
        private bool _warnedXr;
        private bool _warnedFailure;

        public bool IsAvailable => RhiCore.IsD3D12Active && RhiCore.IsDlssNrAvailable;

        [Preserve]
        public UnityRHIDLSSNeuralRenderingBackend(UnityEngine.Shader prepareInputsShader)
        {
            _prepareMaterial = prepareInputsShader != null
                ? CoreUtils.CreateEngineMaterial(prepareInputsShader) : null;
            // RenderGraph records the prepare and debug draws separately. Keeping
            // independent materials prevents the debug pass texture bindings from
            // mutating the material referenced by the earlier prepare draw.
            _debugMaterial = prepareInputsShader != null
                ? CoreUtils.CreateEngineMaterial(prepareInputsShader) : null;
            _resolveMaterial = prepareInputsShader != null
                ? CoreUtils.CreateEngineMaterial(prepareInputsShader) : null;
            _pass = new DLSSNeuralRenderingPass(this)
            {
                renderPassEvent = IllusionRenderPassEvent.DLSSNeuralRenderingPass,
                requiresIntermediateTexture = true,
            };
            _pass.ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Motion);
#if UNITY_EDITOR
            RhiDomainReload.RegisterOwner(this);
#endif
        }

        [Preserve]
        public static DLSSNeuralRenderingRuntimeStatus GetStatus() => new(true,
            RhiCore.IsD3D12Active,
            RhiCore.IsDlssNrAvailable,
            RhiCore.DlssNrInitResult,
            RhiCore.DlssNrLastCreateResult,
            RhiCore.DlssNrLastEvaluateResult);

        public void Enqueue(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (_prepareMaterial == null || _debugMaterial == null || _resolveMaterial == null)
                return;
            DLSSNeuralRendering volume = VolumeManager.instance.stack?.GetComponent<DLSSNeuralRendering>();
            if (volume == null || !volume.IsActive())
                return;
            if (!IsAvailable)
            {
                if (!_warnedUnavailable)
                {
                    _warnedUnavailable = true;
                    Debug.LogWarning($"[UnityRHI.DLSS Neural Rendering] Pass disabled: D3D12/NR runtime unavailable " +
                        $"(init=0x{unchecked((uint)RhiCore.DlssNrInitResult):X8}).");
                }
                return;
            }

            CameraData cameraData = renderingData.cameraData;
            Camera camera = cameraData.camera;
            if (camera == null || !DLSSNeuralRenderingCameraPolicy.ShouldRender(
                    camera.cameraType, cameraData.resolveFinalTarget))
                return;

            renderer.EnqueuePass(_pass);
        }

        /// <summary>Reset temporal history for all live camera contexts.</summary>
        public void ResetHistory()
        {
            foreach (DLSSNeuralRenderingCameraContext context in _contexts.Values)
                context.ResetHistory();
        }

        public void Dispose()
        {
#if UNITY_EDITOR
            RhiDomainReload.UnregisterOwner(this);
#endif
            foreach (DLSSNeuralRenderingCameraContext context in _contexts.Values)
                context.Dispose();
            _contexts.Clear();
            _deadCameras.Clear();
            if (_prepareMaterial != null)
                CoreUtils.Destroy(_prepareMaterial);
            if (_debugMaterial != null)
                CoreUtils.Destroy(_debugMaterial);
            if (_resolveMaterial != null)
                CoreUtils.Destroy(_resolveMaterial);
            _prepareMaterial = null;
            _debugMaterial = null;
            _resolveMaterial = null;
            _pass = null;
        }

        private DLSSNeuralRenderingCameraContext GetContext(Camera camera, int width, int height)
        {
            PruneDeadCameras();
            if (_contexts.TryGetValue(camera, out DLSSNeuralRenderingCameraContext context))
            {
                if (context.Width == width && context.Height == height)
                    return context;
                context.Dispose();
                _contexts.Remove(camera);
            }

            context = new DLSSNeuralRenderingCameraContext(width, height, camera.name);
            _contexts.Add(camera, context);
            return context;
        }

        private void PruneDeadCameras()
        {
            _deadCameras.Clear();
            foreach (KeyValuePair<Camera, DLSSNeuralRenderingCameraContext> pair in _contexts)
                if (pair.Key == null)
                    _deadCameras.Add(pair.Key);
            foreach (Camera camera in _deadCameras)
            {
                _contexts[camera].Dispose();
                _contexts.Remove(camera);
            }
            _deadCameras.Clear();
        }

        private sealed class DLSSNeuralRenderingPass : ScriptableRenderPass
        {
            private static readonly int InputColorId = UnityEngine.Shader.PropertyToID("_DLSSNeuralRenderingInputColor");
            private static readonly int InputDepthId = UnityEngine.Shader.PropertyToID("_DLSSNeuralRenderingInputDepth");
            private static readonly int InputMotionId = UnityEngine.Shader.PropertyToID("_DLSSNeuralRenderingInputMotion");
            private static readonly int OutputId = UnityEngine.Shader.PropertyToID("_DLSSNeuralRenderingOutput");
            private static readonly int DebugModeId = UnityEngine.Shader.PropertyToID("_DLSSNeuralRenderingDebugMode");
            private static readonly int DebugMotionScaleXId = UnityEngine.Shader.PropertyToID("_DLSSNeuralRenderingDebugMotionScaleX");
            private static readonly int DebugMotionScaleYId = UnityEngine.Shader.PropertyToID("_DLSSNeuralRenderingDebugMotionScaleY");
            private static readonly int DebugMotionRangeId = UnityEngine.Shader.PropertyToID("_DLSSNeuralRenderingDebugMotionRange");
            private static readonly int DebugDepthRangeId = UnityEngine.Shader.PropertyToID("_DLSSNeuralRenderingDebugDepthRange");
            private readonly UnityRHIDLSSNeuralRenderingBackend _feature;

            private sealed class PreparePassData
            {
                public TextureHandle Color;
                public TextureHandle Depth;
                public TextureHandle Motion;
                public Material Material;
            }

            private sealed class DispatchPassData
            {
                public UnityRHIDLSSNeuralRenderingBackend Feature;
                public Camera Camera;
                public DLSSNeuralRenderingCameraContext Context;
                public DLSSNeuralRenderingCameraContext.DispatchParameters Parameters;
            }

            private sealed class DebugPassData
            {
                public TextureHandle Color;
                public TextureHandle Depth;
                public TextureHandle Motion;
                public Material Material;
                public int Mode;
                public float MotionScaleX;
                public float MotionScaleY;
                public float MotionRange;
                public float DepthRange;
            }

            private sealed class ResolvePassData
            {
                public TextureHandle Output;
                public Material Material;
            }

            internal DLSSNeuralRenderingPass(UnityRHIDLSSNeuralRenderingBackend feature)
            {
                _feature = feature;
                profilingSampler = new ProfilingSampler("DLSS Neural Rendering");
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                UniversalResourceData resources = frameData.Get<UniversalResourceData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                Camera camera = cameraData.camera;
                if (camera == null || resources.isActiveTargetBackBuffer)
                    return;
                if (!DLSSNeuralRenderingCameraPolicy.ShouldRender(
                        cameraData.cameraType, cameraData.resolveFinalTarget))
                    return;
                DLSSNeuralRendering volume = VolumeManager.instance.stack?.GetComponent<DLSSNeuralRendering>();
                if (volume == null || !volume.IsActive())
                    return;
                var settings = new DLSSNeuralRenderingSettings(volume, IllusionRuntimeRenderingConfig.Get());
                if (cameraData.isHDROutputActive)
                {
                    if (!_feature._warnedHdr)
                    {
                        _feature._warnedHdr = true;
                        Debug.LogWarning("[UnityRHI.DLSS Neural Rendering] HDR Output is not supported by the full-resolution SDR post-process; bypassing.");
                    }
                    return;
                }
                if (cameraData.xr.enabled)
                {
                    if (!_feature._warnedXr)
                    {
                        _feature._warnedXr = true;
                        Debug.LogWarning("[UnityRHI.DLSS Neural Rendering] XR texture arrays are not supported yet; bypassing.");
                    }
                    return;
                }

                TextureHandle sourceColor = resources.activeColorTexture;
                TextureHandle sourceDepth = resources.cameraDepthTexture;
                TextureHandle sourceMotion = resources.motionVectorColor;
                if (!sourceColor.IsValid() || !sourceDepth.IsValid() || !sourceMotion.IsValid())
                    return;

                UnityEngine.Rendering.RenderGraphModule.TextureDesc sourceDesc =
                    renderGraph.GetTextureDesc(sourceColor);
                int width = sourceDesc.width;
                int height = sourceDesc.height;
                if (width <= 0 || height <= 0)
                    return;

                DLSSNeuralRenderingCameraContext context;
                try
                {
                    context = _feature.GetContext(camera, width, height);
                }
                catch (Exception exception)
                {
                    if (!_feature._warnedFailure)
                    {
                        _feature._warnedFailure = true;
                        Debug.LogError($"[UnityRHI.DLSS Neural Rendering] Resource initialization failed; bypassing. {exception}");
                    }
                    return;
                }

                TextureHandle color = renderGraph.ImportTexture(context.ColorHandle);
                TextureHandle depth = renderGraph.ImportTexture(context.DepthHandle);
                TextureHandle motion = renderGraph.ImportTexture(context.MotionHandle);
                TextureHandle output = renderGraph.ImportTexture(context.OutputHandle);

                using (IRasterRenderGraphBuilder builder =
                    renderGraph.AddRasterRenderPass<PreparePassData>("DLSS Neural Rendering Prepare Inputs",
                        out PreparePassData passData))
                {
                    passData.Color = sourceColor;
                    passData.Depth = sourceDepth;
                    passData.Motion = sourceMotion;
                    passData.Material = _feature._prepareMaterial;
                    builder.UseTexture(sourceColor, AccessFlags.Read);
                    builder.UseTexture(sourceDepth, AccessFlags.Read);
                    builder.UseTexture(sourceMotion, AccessFlags.Read);
                    builder.SetRenderAttachment(color, 0, AccessFlags.WriteAll);
                    builder.SetRenderAttachment(motion, 1, AccessFlags.WriteAll);
                    builder.SetRenderAttachment(depth, 2, AccessFlags.WriteAll);
                    // Seed the output with an exact color fallback. The following
                    // unsafe pass transitions it from RT to UAV before NGX writes it.
                    builder.SetRenderAttachment(output, 3, AccessFlags.WriteAll);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static (PreparePassData data, RasterGraphContext rgContext) =>
                    {
                        data.Material.SetTexture(InputColorId, data.Color);
                        data.Material.SetTexture(InputDepthId, data.Depth);
                        data.Material.SetTexture(InputMotionId, data.Motion);
                        rgContext.cmd.DrawProcedural(Matrix4x4.identity, data.Material, 0,
                            MeshTopology.Triangles, 3, 1);
                    });
                }

                if (settings.DebugMode != DLSSNeuralRenderingDebugMode.Off)
                {
                    using (IRasterRenderGraphBuilder builder =
                        renderGraph.AddRasterRenderPass<DebugPassData>("DLSS Neural Rendering Debug Inputs",
                            out DebugPassData passData, profilingSampler))
                    {
                        passData.Color = color;
                        passData.Depth = depth;
                        passData.Motion = motion;
                        passData.Material = _feature._debugMaterial;
                        passData.Mode = (int)settings.DebugMode;
                        passData.MotionScaleX = -width * settings.MotionVectorScale.x;
                        passData.MotionScaleY = -height * settings.MotionVectorScale.y;
                        passData.MotionRange = settings.DebugMotionRange;
                        passData.DepthRange = settings.DebugDepthRange;
                        builder.UseTexture(color, AccessFlags.Read);
                        builder.UseTexture(depth, AccessFlags.Read);
                        builder.UseTexture(motion, AccessFlags.Read);
                        builder.SetRenderAttachment(output, 0, AccessFlags.WriteAll);
                        builder.AllowPassCulling(false);
                        builder.SetRenderFunc(static (DebugPassData data, RasterGraphContext rgContext) =>
                        {
                            data.Material.SetTexture(InputColorId, data.Color);
                            data.Material.SetTexture(InputDepthId, data.Depth);
                            data.Material.SetTexture(InputMotionId, data.Motion);
                            data.Material.SetInt(DebugModeId, data.Mode);
                            data.Material.SetFloat(DebugMotionScaleXId, data.MotionScaleX);
                            data.Material.SetFloat(DebugMotionScaleYId, data.MotionScaleY);
                            data.Material.SetFloat(DebugMotionRangeId, data.MotionRange);
                            data.Material.SetFloat(DebugDepthRangeId, data.DepthRange);
                            rgContext.cmd.DrawProcedural(Matrix4x4.identity, data.Material, 2,
                                MeshTopology.Triangles, 3, 1);
                        });
                    }

                    // Debug shows the exact prepared inputs and deliberately skips
                    // NGX so its output cannot obscure an input-contract problem.
                    resources.cameraColor = output;
                    return;
                }

                using (IUnsafeRenderGraphBuilder builder =
                    renderGraph.AddUnsafePass<DispatchPassData>("DLSS Neural Rendering",
                        out DispatchPassData passData, profilingSampler))
                {
                    passData.Feature = _feature;
                    passData.Camera = camera;
                    passData.Context = context;
                    passData.Parameters = context.BeginFrame(camera, Time.frameCount, settings);
                    // @IllusionRP: declare all native reads. RenderGraph transitions the
                    // imported inputs to SRV, matching the UnityRHI wrapper contract.
                    builder.UseTexture(color, AccessFlags.Read);
                    builder.UseTexture(depth, AccessFlags.Read);
                    builder.UseTexture(motion, AccessFlags.Read);
                    builder.UseTexture(output, AccessFlags.WriteAll);
                    builder.AllowPassCulling(false);
                    builder.AllowGlobalStateModification(true);
                    builder.SetRenderFunc(static (DispatchPassData data, UnsafeGraphContext unsafeContext) =>
                    {
                        try
                        {
                            CommandBuffer commandBuffer =
                                CommandBufferHelpers.GetNativeCommandBuffer(unsafeContext.cmd);
                            data.Context.Record(commandBuffer, data.Parameters);
                        }
                        catch (Exception exception)
                        {
                            if (!data.Feature._warnedFailure)
                            {
                                data.Feature._warnedFailure = true;
                                Debug.LogError($"[UnityRHI.DLSS Neural Rendering] Dispatch failed. {exception}");
                            }
                        }
                    });
                }

                using (IRasterRenderGraphBuilder builder =
                    renderGraph.AddRasterRenderPass<ResolvePassData>("DLSS Neural Rendering Resolve Display sRGB",
                        out ResolvePassData passData, profilingSampler))
                {
                    passData.Output = output;
                    passData.Material = _feature._resolveMaterial;
                    builder.UseTexture(output, AccessFlags.Read);
                    builder.SetRenderAttachment(color, 0, AccessFlags.WriteAll);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static (ResolvePassData data, RasterGraphContext rgContext) =>
                    {
                        data.Material.SetTexture(OutputId, data.Output);
                        rgContext.cmd.DrawProcedural(Matrix4x4.identity, data.Material, 1,
                            MeshTopology.Triangles, 3, 1);
                    });
                }
                resources.cameraColor = color;
            }
        }
    }
}
