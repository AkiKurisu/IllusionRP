using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Scripting;
using global::UnityRhi;
using RhiTexture = global::UnityRhi.Texture;
using RhiTextureDesc = global::UnityRhi.TextureDesc;

namespace Illusion.Rendering.PathTracing.UnityRHI
{
    [Preserve]
    internal sealed class UnityRHIPathTracingRayReconstruction : IPathTracingRayReconstruction
    {
        private class DispatchPassData
        {
            internal UnityRHIPathTracingRayReconstruction Owner;
            internal CameraContext Context;
            internal DlrrDispatchDesc Desc;
        }

        private readonly Dictionary<int, CameraContext> _contexts = new();

        private readonly ProfilingSampler _sampler = new("Path Tracing Ray Reconstruction");

        private bool _reportedFailure;

        public UnityRHIPathTracingRayReconstruction()
        {
#if UNITY_EDITOR
            RhiDomainReload.RegisterOwner(this);
#endif
        }

        public bool IsAvailable => RhiCore.IsD3D12Active && RhiCore.IsNgxDlrrAvailable;

        public void Record(RenderGraph renderGraph, Camera camera, in PathTracingRayReconstructionInputs inputs, bool resetHistory)
        {
            int id = camera.GetInstanceID();
            if (!_contexts.TryGetValue(id, out var context))
            {
                context = new CameraContext(camera.name);
                _contexts.Add(id, context);
            }
            context.Prepare(inputs, resetHistory);

            using var builder = renderGraph.AddUnsafePass<DispatchPassData>("Path Tracing Ray Reconstruction", out var passData, _sampler);
            passData.Owner = this;
            passData.Context = context;
            passData.Desc = new DlrrDispatchDesc
            {
                WorldToView = ToArray(inputs.WorldToView),
                ViewToClip = ToArray(inputs.ViewToClip),
                CameraJitterPixels = new Vector2(inputs.Jitter.x, -inputs.Jitter.y),
                RenderWidth = inputs.Width,
                RenderHeight = inputs.Height,
                OutputWidth = inputs.OutputWidth,
                OutputHeight = inputs.OutputHeight,
                Mode = inputs.Quality switch
                {
                    PathTracingRayReconstructionQuality.Quality => UpscalerMode.QUALITY,
                    PathTracingRayReconstructionQuality.Balanced => UpscalerMode.BALANCED,
                    PathTracingRayReconstructionQuality.Performance => UpscalerMode.PERFORMANCE,
                    PathTracingRayReconstructionQuality.UltraPerformance => UpscalerMode.ULTRA_PERFORMANCE,
                    _ => UpscalerMode.NATIVE
                },
                Preset = DlssRrPreset.Default,
                UseSpecularMotionVectors = true
            };
            builder.UseTexture(renderGraph.ImportTexture(inputs.Color));
            builder.UseTexture(renderGraph.ImportTexture(inputs.MotionVectors));
            builder.UseTexture(renderGraph.ImportTexture(inputs.Depth));
            builder.UseTexture(renderGraph.ImportTexture(inputs.DiffuseAlbedo));
            builder.UseTexture(renderGraph.ImportTexture(inputs.SpecularAlbedo));
            builder.UseTexture(renderGraph.ImportTexture(inputs.NormalsAndRoughness));
            builder.UseTexture(renderGraph.ImportTexture(inputs.SpecularMotionVectors));
            builder.UseTexture(renderGraph.ImportTexture(inputs.Output), AccessFlags.WriteAll);
            builder.AllowPassCulling(false);
            builder.AllowGlobalStateModification(true);
            builder.SetRenderFunc(static (DispatchPassData data, UnsafeGraphContext graphContext) =>
            {
                try
                {
                    data.Context.Record(CommandBufferHelpers.GetNativeCommandBuffer(graphContext.cmd), data.Desc);
                }
                catch (Exception exception)
                {
                    if (data.Owner._reportedFailure)
                        return;
                    data.Owner._reportedFailure = true;
                    Debug.LogError($"[PathTracing] DLSS Ray Reconstruction failed. {exception}");
                }
            });
        }

        public void Release(int cameraId)
        {
            if (!_contexts.Remove(cameraId, out var context))
                return;
            RhiCore.WaitForGpuIdle();
            context.Dispose();
            Device.Instance.RunGarbageCollection();
        }

        public void Dispose()
        {
#if UNITY_EDITOR
            RhiDomainReload.UnregisterOwner(this);
#endif
            if (_contexts.Count == 0)
                return;
            RhiCore.WaitForGpuIdle();
            foreach (var context in _contexts.Values)
                context.Dispose();
            _contexts.Clear();
            Device.Instance.RunGarbageCollection();
        }

        private static float[] ToArray(Matrix4x4 matrix)
        {
            var values = new float[16];
            for (int row = 0; row < 4; row++)
            for (int column = 0; column < 4; column++)
                values[row * 4 + column] = matrix[row, column];
            return values;
        }

        private sealed class CameraContext : IDisposable
        {
            private readonly string _name;

            private readonly RhiTexture[] _textures = new RhiTexture[8];

            private readonly RenderTexture[] _sources = new RenderTexture[8];

            private readonly Vector2Int[] _sizes = new Vector2Int[8];

            private DlrrContext _rayReconstruction;

            private CommandList _commandList = new(8);

            public CameraContext(string name)
            {
                _name = name;
            }

            public void Prepare(in PathTracingRayReconstructionInputs inputs, bool resetHistory)
            {
                Wrap(0, inputs.Color, Format.RGBA16_FLOAT, ResourceStates.ShaderResource);
                Wrap(1, inputs.MotionVectors, Format.RGBA16_FLOAT, ResourceStates.ShaderResource);
                Wrap(2, inputs.Depth, Format.R32_FLOAT, ResourceStates.ShaderResource);
                Wrap(3, inputs.DiffuseAlbedo, Format.RGBA16_FLOAT, ResourceStates.ShaderResource);
                Wrap(4, inputs.SpecularAlbedo, Format.RGBA16_FLOAT, ResourceStates.ShaderResource);
                Wrap(5, inputs.NormalsAndRoughness, Format.RGBA16_FLOAT, ResourceStates.ShaderResource);
                Wrap(6, inputs.SpecularMotionVectors, Format.RG16_FLOAT, ResourceStates.ShaderResource);
                Wrap(7, inputs.Output, Format.RGBA16_FLOAT, ResourceStates.UnorderedAccess);
                if (resetHistory && _rayReconstruction != null)
                {
                    RhiCore.WaitForGpuIdle();
                    _rayReconstruction.Dispose();
                    _rayReconstruction = null;
                    Device.Instance.RunGarbageCollection();
                }
                _rayReconstruction ??= new DlrrContext();
            }

            private void Wrap(int index, RTHandle handle, Format format, ResourceStates state)
            {
                var renderTexture = handle.rt;
                var size = new Vector2Int(renderTexture.width, renderTexture.height);
                if (_textures[index] != null && _sources[index] == renderTexture && _sizes[index] == size)
                    return;
                IntPtr native = renderTexture.GetNativeTexturePtr();
                _textures[index]?.Dispose();
                _sources[index] = renderTexture;
                _sizes[index] = size;
                _textures[index] = Device.Instance.CreateTextureFromNativeResource(native, new RhiTextureDesc
                {
                    Width = (uint)renderTexture.width,
                    Height = (uint)renderTexture.height,
                    Format = format,
                    IsShaderResource = true,
                    IsUAV = true,
                    IsRenderTarget = false,
                    InitialState = state,
                    KeepInitialState = true,
                    DebugName = $"Path Tracing {_name} {renderTexture.name}"
                });
            }

            public void Record(CommandBuffer commandBuffer, DlrrDispatchDesc desc)
            {
                desc.Input = _textures[0];
                desc.MotionVectors = _textures[1];
                desc.Depth = _textures[2];
                desc.DiffuseAlbedo = _textures[3];
                desc.SpecularAlbedo = _textures[4];
                desc.NormalRoughness = _textures[5];
                desc.SpecularMotionVectors = _textures[6];
                desc.Output = _textures[7];
                _commandList.Open();
                try
                {
                    _commandList.BeginMarker("PathTracing.RayReconstruction");
                    _rayReconstruction.Record(_commandList, desc);
                    _commandList.EndMarker();
                    _commandList.Close();
                    _commandList.SubmitAndForget(commandBuffer);
                }
                catch
                {
                    _commandList.Dispose();
                    _commandList = new CommandList(8);
                    throw;
                }
            }

            public void Dispose()
            {
                _commandList?.Dispose();
                _commandList = null;
                _rayReconstruction?.Dispose();
                _rayReconstruction = null;
                for (int i = 0; i < _textures.Length; i++)
                {
                    _textures[i]?.Dispose();
                    _textures[i] = null;
                }
            }
        }
    }
}
