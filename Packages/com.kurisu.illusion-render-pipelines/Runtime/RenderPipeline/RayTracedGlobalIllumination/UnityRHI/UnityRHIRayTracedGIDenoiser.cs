using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Scripting;
using UnityRhi.Nrd;
using UnityRhi.Nri;

namespace Illusion.Rendering.RayTracedGlobalIllumination.UnityRHI
{
    /// <summary>
    /// Denoises the ray traced indirect lighting with NRD through NRIPlugin, on Unity's own Direct3D 12 command list.
    /// </summary>
    [Preserve]
    internal sealed class UnityRHIRayTracedGIDenoiser : IRayTracedGIDenoiser
    {
        private const int NrdRenderEvent = 1;

        [DllImport("NRIPlugin")]
        private static extern IntPtr GetRenderEventAndDataFunc();

        [DllImport("NRIPlugin")]
        private static extern IntPtr WrapD3D12Texture(IntPtr resource, uint format);

        [DllImport("NRIPlugin")]
        private static extern void ReleaseTexture(IntPtr nriTexture);

        private class DispatchPassData
        {
            internal IntPtr RenderEvent;
            internal IntPtr FrameData;
        }

        private readonly Dictionary<int, CameraContext> _contexts = new();

        private readonly ProfilingSampler _sampler = new("Ray Traced GI Denoise");

        private IntPtr _renderEvent;

        public UnityRHIRayTracedGIDenoiser()
        {
#if UNITY_EDITOR
            global::UnityRhi.RhiDomainReload.RegisterOwner(this);
#endif
        }

        public bool IsAvailable => SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D12;

        public void Record(RenderGraph renderGraph, Camera camera, in RayTracedGIDenoiserInputs inputs, bool resetHistory)
        {
            if (_renderEvent == IntPtr.Zero)
                _renderEvent = GetRenderEventAndDataFunc();
            int id = camera.GetInstanceID();
            if (!_contexts.TryGetValue(id, out var context))
            {
                context = new CameraContext();
                _contexts.Add(id, context);
            }
            context.Prepare(inputs);

            using var builder = renderGraph.AddUnsafePass<DispatchPassData>("Ray Traced GI Denoise", out var passData, _sampler);
            passData.RenderEvent = _renderEvent;
            passData.FrameData = context.BuildFrameData(inputs, resetHistory);
            builder.UseTexture(renderGraph.ImportTexture(inputs.ViewZ));
            builder.UseTexture(renderGraph.ImportTexture(inputs.NormalRoughness));
            builder.UseTexture(renderGraph.ImportTexture(inputs.MotionVectors));
            builder.UseTexture(renderGraph.ImportTexture(inputs.Diffuse));
            builder.UseTexture(renderGraph.ImportTexture(inputs.Output), AccessFlags.WriteAll);
            if (inputs.Specular != null)
            {
                builder.UseTexture(renderGraph.ImportTexture(inputs.Specular));
                builder.UseTexture(renderGraph.ImportTexture(inputs.SpecularOutput), AccessFlags.WriteAll);
            }
            builder.AllowPassCulling(false);
            builder.AllowGlobalStateModification(true);

            builder.SetRenderFunc(static (DispatchPassData data, UnsafeGraphContext graphContext) =>
            {
                CommandBufferHelpers.GetNativeCommandBuffer(graphContext.cmd).IssuePluginEventAndData(data.RenderEvent, NrdRenderEvent, data.FrameData);
            });
        }

        public void Release(int cameraId)
        {
            if (!_contexts.Remove(cameraId, out var context))
                return;
            global::UnityRhi.RhiCore.WaitForGpuIdle();
            context.Dispose();
        }

        public void Dispose()
        {
#if UNITY_EDITOR
            global::UnityRhi.RhiDomainReload.UnregisterOwner(this);
#endif
            if (_contexts.Count == 0)
                return;
            global::UnityRhi.RhiCore.WaitForGpuIdle();
            foreach (var context in _contexts.Values)
                context.Dispose();
            _contexts.Clear();
        }

        private sealed unsafe class CameraContext : IDisposable
        {
            private const int FrameDataCount = 3;

            private static readonly NriResourceState ShaderResource = new()
            {
                accessBits = AccessBits.SHADER_RESOURCE, layout = Layout.SHADER_RESOURCE, stageBits = 1 << 7
            };

            private static readonly NriResourceState Storage = new()
            {
                accessBits = AccessBits.SHADER_RESOURCE_STORAGE, layout = Layout.SHADER_RESOURCE_STORAGE, stageBits = 1 << 10
            };

            private readonly List<WrappedTexture> _textures = new();

            private NativeArray<NrdResourceInput> _resources;

            private NativeArray<NrdFrameData> _frames;

            private int _instance = -1;

            private Denoiser _denoiser;

            private uint _frameIndex;

            private bool _hasPrevious;

            private Matrix4x4 _previousWorldToView;

            private Matrix4x4 _previousViewToClip;

            private float2 _previousJitter;

            private int _previousWidth;

            private int _previousHeight;

            private struct WrappedTexture
            {
                public ResourceType Type;
                public IntPtr Native;
                public IntPtr Nri;
                public NriResourceState State;
            }

            public CameraContext()
            {
                _frames = new NativeArray<NrdFrameData>(FrameDataCount, Allocator.Persistent);
            }

            public void Prepare(in RayTracedGIDenoiserInputs inputs)
            {
                bool specular = inputs.Specular != null;
                var denoiser = inputs.Denoiser == RayTracedGIDenoiser.Reblur
                    ? specular ? Denoiser.REBLUR_DIFFUSE_SPECULAR : Denoiser.REBLUR_DIFFUSE
                    : specular ? Denoiser.RELAX_DIFFUSE_SPECULAR : Denoiser.RELAX_DIFFUSE;
                bool recreated = false;
                if (_instance < 0 || denoiser != _denoiser)
                {
                    DestroyInstance();
                    var desc = new NrdDenoiserDesc(denoiser);
                    _instance = NrdDenoiserHelper.CreateDenoiserInstance((IntPtr)UnsafeUtility.AddressOf(ref desc), 1);
                    _denoiser = denoiser;
                    _hasPrevious = false;
                    recreated = true;
                }

                bool changed = recreated;
                changed |= Wrap(0, ResourceType.IN_VIEWZ, inputs.ViewZ, ShaderResource);
                changed |= Wrap(1, ResourceType.IN_NORMAL_ROUGHNESS, inputs.NormalRoughness, ShaderResource);
                changed |= Wrap(2, ResourceType.IN_MV, inputs.MotionVectors, ShaderResource);
                changed |= Wrap(3, ResourceType.IN_DIFF_RADIANCE_HITDIST, inputs.Diffuse, ShaderResource);
                changed |= Wrap(4, ResourceType.OUT_DIFF_RADIANCE_HITDIST, inputs.Output, Storage);
                if (specular)
                {
                    changed |= Wrap(5, ResourceType.IN_SPEC_RADIANCE_HITDIST, inputs.Specular, ShaderResource);
                    changed |= Wrap(6, ResourceType.OUT_SPEC_RADIANCE_HITDIST, inputs.SpecularOutput, Storage);
                }
                if (!changed)
                    return;

                if (_resources.IsCreated && _resources.Length != _textures.Count)
                    _resources.Dispose();
                if (!_resources.IsCreated)
                    _resources = new NativeArray<NrdResourceInput>(_textures.Count, Allocator.Persistent);
                for (int i = 0; i < _textures.Count; i++)
                    _resources[i] = new NrdResourceInput { type = _textures[i].Type, texture = _textures[i].Nri, state = _textures[i].State };
                NrdDenoiserHelper.UpdateDenoiserResources(_instance, (IntPtr)_resources.GetUnsafePtr(), _resources.Length);
            }

            private bool Wrap(int slot, ResourceType type, RTHandle handle, NriResourceState state)
            {
                var texture = handle.rt;
                IntPtr native = texture.GetNativeTexturePtr();
                if (slot < _textures.Count && _textures[slot].Native == native)
                    return false;
                var wrapped = new WrappedTexture
                {
                    Type = type,
                    Native = native,
                    Nri = WrapD3D12Texture(native, ToDxgiFormat(texture.graphicsFormat)),
                    State = state
                };
                if (slot < _textures.Count)
                {
                    ReleaseTexture(_textures[slot].Nri);
                    _textures[slot] = wrapped;
                }
                else
                {
                    _textures.Add(wrapped);
                }
                return true;
            }

            public IntPtr BuildFrameData(in RayTracedGIDenoiserInputs inputs, bool resetHistory)
            {
                if (!_hasPrevious || resetHistory || _previousWidth != inputs.Width || _previousHeight != inputs.Height)
                {
                    _previousWorldToView = inputs.WorldToView;
                    _previousViewToClip = inputs.ViewToClip;
                    _previousJitter = inputs.Jitter;
                    _previousWidth = inputs.Width;
                    _previousHeight = inputs.Height;
                }

                var common = CommonSettings._default;
                common.viewToClipMatrix = inputs.ViewToClip;
                common.viewToClipMatrixPrev = _previousViewToClip;
                common.worldToViewMatrix = inputs.WorldToView;
                common.worldToViewMatrixPrev = _previousWorldToView;
                // URP motion vectors hold the current minus the previous UV; without them NRD takes the camera motion from the matrices.
                common.motionVectorScale = inputs.ScreenMotion ? new float3(-1.0f, -1.0f, 0.0f) : float3.zero;
                common.isMotionVectorInWorldSpace = !inputs.ScreenMotion;
                common.cameraJitter = inputs.Jitter;
                common.cameraJitterPrev = _previousJitter;
                common.resourceSize[0] = (ushort)inputs.Width;
                common.resourceSize[1] = (ushort)inputs.Height;
                common.rectSize[0] = (ushort)inputs.Width;
                common.rectSize[1] = (ushort)inputs.Height;
                common.resourceSizePrev[0] = (ushort)_previousWidth;
                common.resourceSizePrev[1] = (ushort)_previousHeight;
                common.rectSizePrev[0] = (ushort)_previousWidth;
                common.rectSizePrev[1] = (ushort)_previousHeight;
                common.denoisingRange = inputs.DenoisingRange;
                common.accumulationMode = !_hasPrevious || resetHistory ? AccumulationMode.CLEAR_AND_RESTART : AccumulationMode.CONTINUE;
                // The checkerboard field of NRD follows this frame index, so it must be the one the resampling passes use.
                common.frameIndex = inputs.FrameIndex;

                var data = NrdFrameData._default;
                data.commonSettings = common;
                data.instanceId = _instance;
                data.width = (ushort)inputs.Width;
                data.height = (ushort)inputs.Height;
                data.denoiserCount = 1;
                ref var entry = ref NrdFrameData.GetEntry(ref data, 0);
                entry.identifier = 0;
                entry.denoiser = _denoiser;
                var checkerboardMode = inputs.Checkerboard ? CheckerboardMode.BLACK : CheckerboardMode.OFF;
                if (_denoiser is Denoiser.REBLUR_DIFFUSE or Denoiser.REBLUR_DIFFUSE_SPECULAR)
                {
                    var settings = ReblurSettings._default;
                    settings.checkerboardMode = checkerboardMode;
                    entry.Write(settings);
                }
                else
                {
                    var settings = RelaxSettings._default;
                    settings.checkerboardMode = checkerboardMode;
                    entry.Write(settings);
                }

                int index = (int)(_frameIndex % FrameDataCount);
                _frames[index] = data;
                _frameIndex++;
                _hasPrevious = true;
                _previousWorldToView = inputs.WorldToView;
                _previousViewToClip = inputs.ViewToClip;
                _previousJitter = inputs.Jitter;
                _previousWidth = inputs.Width;
                _previousHeight = inputs.Height;
                return (IntPtr)_frames.GetUnsafePtr() + index * sizeof(NrdFrameData);
            }

            private static uint ToDxgiFormat(GraphicsFormat format)
            {
                return format switch
                {
                    GraphicsFormat.R32_SFloat => 41,
                    GraphicsFormat.A2B10G10R10_UNormPack32 => 24,
                    GraphicsFormat.R16G16B16A16_SFloat => 10,
                    _ => throw new NotSupportedException($"No DXGI format mapping for {format}.")
                };
            }

            private void DestroyInstance()
            {
                if (_instance >= 0)
                    NrdDenoiserHelper.DestroyDenoiserInstance(_instance);
                _instance = -1;
                foreach (var texture in _textures)
                    ReleaseTexture(texture.Nri);
                _textures.Clear();
                if (_resources.IsCreated)
                    _resources.Dispose();
            }

            public void Dispose()
            {
                DestroyInstance();
                if (_frames.IsCreated)
                    _frames.Dispose();
            }
        }
    }
}
