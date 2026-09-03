using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityRhi.Nri;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;

namespace UnityRhi.Nrd
{
    // -----------------------------------------------------------------------
    // Abstract base – holds all common infrastructure shared by the three
    // typed denoiser subclasses (Sigma / Reblur / Relax).
    // -----------------------------------------------------------------------
    public abstract class NrdDenoiser<TSettings> : IDisposable where TSettings : unmanaged
    {
        protected Denoiser                      _denoiser;
        private   NativeArray<NrdResourceInput> _resourceCache;

        protected readonly int    _nrdInstanceId;
        protected readonly string _cameraName;

        private       NativeArray<NrdFrameData> _buffer;
        private const int                       BufferCount = 3;

        // -----------------------------------------------------------------------
        // Fields shared by all denoiser types – filled from camera/frame state.
        // -----------------------------------------------------------------------


        // -----------------------------------------------------------------------
        protected NrdDenoiser(string camName, NrdDenoiserDesc desc)
        {
            _cameraName = camName;
            var descs = new[] { desc };
            unsafe
            {
                fixed (NrdDenoiserDesc* p = descs)
                {
                    _nrdInstanceId = NrdDenoiserHelper.CreateDenoiserInstance((IntPtr)p, 1);
                }
            }

            _buffer = new NativeArray<NrdFrameData>(BufferCount, Allocator.Persistent);
            Debug.Log($"[NRD] Created Denoiser Instance {_nrdInstanceId} | camera={_cameraName} | type={desc.denoiser} (id={desc.identifier}, enum={(uint)desc.denoiser})");
        }


        public unsafe IntPtr GetInteropDataPtr(CommonSettings common, TSettings settings)
        {
            var data = NrdFrameData._default;
            data.instanceId     = _nrdInstanceId;
            data.width          = common.resourceSize[0];
            data.height         = common.resourceSize[1];
            data.commonSettings = common;

            data.denoiserCount = 1;
            ref var entry = ref NrdFrameData.GetEntry(ref data, 0);
            entry.identifier = 0;
            entry.denoiser   = _denoiser;
            entry.Write(settings);

            return StoreAndGetPtr(data, common.frameIndex);
        }

        // -----------------------------------------------------------------------
        public unsafe void UpdateResources(params (ResourceType type, NriTextureResource resource)[] entries)
        {
            if (entries == null || entries.Length == 0) return;

            if (!_resourceCache.IsCreated || _resourceCache.Length < entries.Length)
            {
                if (_resourceCache.IsCreated) _resourceCache.Dispose();
                _resourceCache = new NativeArray<NrdResourceInput>(entries.Length, Allocator.Persistent);
            }

            int idx = 0;
            var ptr = (NrdResourceInput*)_resourceCache.GetUnsafePtr();

            foreach (var (type, resource) in entries)
            {
                if (resource == null) continue;
                ptr[idx++] = new NrdResourceInput { type = type, texture = resource.NriPtr, state = resource.ResourceState };
            }

            NrdDenoiserHelper.UpdateDenoiserResources(_nrdInstanceId, (IntPtr)ptr, idx);
        }


        protected unsafe IntPtr StoreAndGetPtr(NrdFrameData data, uint frameIndex)
        {
            var index = (int)(frameIndex % BufferCount);
            _buffer[index] = data;
            return (IntPtr)_buffer.GetUnsafePtr() + index * sizeof(NrdFrameData);
        }

        public void Dispose()
        {
            if (_buffer.IsCreated) _buffer.Dispose();
            if (_resourceCache.IsCreated) _resourceCache.Dispose();
            NrdDenoiserHelper.DestroyDenoiserInstance(_nrdInstanceId);
            Debug.Log($"[NRD] Destroyed Denoiser Instance {_nrdInstanceId} for Camera {_cameraName}");
        }
    }
}