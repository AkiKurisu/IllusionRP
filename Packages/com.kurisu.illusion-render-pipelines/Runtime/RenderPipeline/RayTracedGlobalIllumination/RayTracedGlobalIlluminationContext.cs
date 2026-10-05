using System;
using Illusion.Rendering.PathTracing;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Illusion.Rendering
{
    /// <summary>
    /// Per-camera reservoirs, secondary surfaces, lighting targets and light sampling state of ray traced global illumination.
    /// </summary>
    internal sealed class RayTracedGlobalIlluminationContext : IDisposable
    {
        private const int ReservoirBlockSize = 16;

        private const int ReservoirArrayCount = 2;

        private const int ReservoirStride = 32;

        private const int SecondarySurfaceStride = 48;

        /// <summary>
        /// The entry count of SHaRC's integration guide.
        /// </summary>
        public const int CacheCapacity = 1 << 22;

        private const int CacheHashEntryStride = 8;

        private const int CacheVoxelStride = 16;

        private const int HistoryFrameGap = 4;

        private const float CutAngle = 45.0f;

        private PathTracingLightBaker _lightBaker;

        private int _accumulationHash;

        private bool _hasHistory;

        private int _historyFrame = -1;

        private int _settingsHash;

        private Vector3 _position;

        private Quaternion _rotation;

        public int Width { get; private set; }

        public int Height { get; private set; }

        public bool Checkerboard { get; private set; }

        public uint FrameIndex { get; private set; }

        public int LastUsedFrame { get; set; }

        public RtxdiReservoirBufferParameters ReservoirParameters { get; private set; }

        public GraphicsBuffer Reservoirs { get; private set; }

        public GraphicsBuffer SecondarySurfaces { get; private set; }

        public RTHandle DiffuseLighting { get; private set; }

        public RTHandle SpecularLighting { get; private set; }

        public RTHandle Accumulation { get; private set; }

        public RTHandle PreviousDepth { get; private set; }

        public RTHandle PreviousNormalRoughness { get; private set; }

        public RayTracedGIView PreviousView { get; private set; }

        public RTHandle ViewZ { get; private set; }

        public RTHandle NormalRoughness { get; private set; }

        public RTHandle MotionVectors { get; private set; }

        public RTHandle DenoisedDiffuse { get; private set; }

        public RTHandle DenoisedSpecular { get; private set; }

        public RTHandle SpecularAccumulation { get; private set; }

        public int AccumulatedFrames { get; private set; }

        public GraphicsBuffer CacheHashEntries { get; private set; }

        public GraphicsBuffer CacheAccumulation { get; private set; }

        public GraphicsBuffer CacheResolved { get; private set; }

        /// <summary>
        /// Whether the radiance cache must be cleared before its next update: it is new, or the history was reset.
        /// </summary>
        public bool CacheNeedsClear { get; set; }

        public Vector3 CacheCameraPosition { get; private set; }

        public Vector3 CacheCameraPositionPrev { get; private set; }

        public PathTracingLightBaker GetLightBaker(PathTracingLightBakerKernels kernels)
        {
            return _lightBaker ??= new PathTracingLightBaker(kernels);
        }

        /// <summary>
        /// Whether ensuring these targets reallocates or releases any of them.
        /// </summary>
        public bool TargetsChange(int width, int height, bool checkerboard, bool denoise, bool specular)
        {
            return !TargetsMatch(width, height, checkerboard) || !DenoiserTargetsMatch(denoise, specular);
        }

        private bool TargetsMatch(int width, int height, bool checkerboard)
        {
            return DiffuseLighting != null && Width == width && Height == height && Checkerboard == checkerboard;
        }

        private bool DenoiserTargetsMatch(bool enabled, bool specular)
        {
            return enabled == (ViewZ != null) && (enabled && specular) == (DenoisedSpecular != null);
        }

        public void EnsureTargets(int width, int height, bool checkerboard)
        {
            if (TargetsMatch(width, height, checkerboard))
                return;
            ReleaseTargets();
            Width = width;
            Height = height;
            Checkerboard = checkerboard;

            int reservoirWidth = checkerboard ? (width + 1) / 2 : width;
            uint blocksX = (uint)((reservoirWidth + ReservoirBlockSize - 1) / ReservoirBlockSize);
            uint blocksY = (uint)((height + ReservoirBlockSize - 1) / ReservoirBlockSize);
            uint rowPitch = blocksX * ReservoirBlockSize * ReservoirBlockSize;
            ReservoirParameters = new RtxdiReservoirBufferParameters
            {
                ReservoirBlockRowPitch = rowPitch,
                ReservoirArrayPitch = rowPitch * blocksY
            };
            int pitch = (int)ReservoirParameters.ReservoirArrayPitch;
            Reservoirs = new GraphicsBuffer(GraphicsBuffer.Target.Structured, pitch * ReservoirArrayCount, ReservoirStride) { name = "RayTracedGIReservoirs" };
            SecondarySurfaces = new GraphicsBuffer(GraphicsBuffer.Target.Structured, pitch, SecondarySurfaceStride) { name = "RayTracedGISecondarySurfaces" };
            DiffuseLighting = Alloc(GraphicsFormat.R16G16B16A16_SFloat, "_RayTracedGIDiffuseLighting");
            SpecularLighting = Alloc(GraphicsFormat.R16G16B16A16_SFloat, "_RayTracedGISpecularLighting");
            PreviousDepth = Alloc(GraphicsFormat.R32_SFloat, "_RayTracedGIPreviousDepth");
            PreviousNormalRoughness = Alloc(GraphicsFormat.R16G16B16A16_SFloat, "_RayTracedGIPreviousNormalRoughness");
            FrameIndex = 0;
            AccumulatedFrames = 0;
            _hasHistory = false;
        }

        /// <summary>
        /// Allocates the NRD guides and outputs while a denoiser runs, and releases them otherwise.
        /// </summary>
        public void EnsureDenoiserTargets(bool enabled, bool specular)
        {
            if (DenoiserTargetsMatch(enabled, specular))
                return;
            ReleaseDenoiserTargets();
            if (!enabled)
                return;
            ViewZ = Alloc(GraphicsFormat.R32_SFloat, "_RayTracedGIViewZ");
            NormalRoughness = Alloc(GraphicsFormat.A2B10G10R10_UNormPack32, "_RayTracedGINormalRoughness");
            MotionVectors = Alloc(GraphicsFormat.R16G16B16A16_SFloat, "_RayTracedGIMotionVectors");
            DenoisedDiffuse = Alloc(GraphicsFormat.R16G16B16A16_SFloat, "_RayTracedGIDenoisedDiffuse");
            if (specular)
                DenoisedSpecular = Alloc(GraphicsFormat.R16G16B16A16_SFloat, "_RayTracedGIDenoisedSpecular");
        }

        private void ReleaseDenoiserTargets()
        {
            RTHandles.Release(ViewZ);
            RTHandles.Release(NormalRoughness);
            RTHandles.Release(MotionVectors);
            RTHandles.Release(DenoisedDiffuse);
            RTHandles.Release(DenoisedSpecular);
            ViewZ = null;
            NormalRoughness = null;
            MotionVectors = null;
            DenoisedDiffuse = null;
            DenoisedSpecular = null;
        }

        /// <summary>
        /// Returns whether the previous frame's receivers and reservoirs may be reused: the camera rendered recently, did
        /// not cut, and the estimate kept its definition.
        /// </summary>
        public bool BeginFrame(Transform camera, int settingsHash, float cutDistance)
        {
            bool valid = _hasHistory
                         && PathTracingFrame.Index - _historyFrame <= HistoryFrameGap
                         && settingsHash == _settingsHash
                         && Vector3.Distance(camera.position, _position) <= cutDistance
                         && Quaternion.Angle(camera.rotation, _rotation) <= CutAngle;
            _settingsHash = settingsHash;
            CacheCameraPositionPrev = valid ? _position : camera.position;
            CacheCameraPosition = camera.position;
            CacheNeedsClear |= !valid;
            _position = camera.position;
            _rotation = camera.rotation;
            return valid;
        }

        public void EndFrame(in RayTracedGIView view)
        {
            PreviousView = view;
            _hasHistory = true;
            _historyFrame = PathTracingFrame.Index;
            FrameIndex++;
        }

        /// <summary>
        /// Advances the static-view average and returns the weight of the current frame, or zero when not averaging.
        /// The average target exists only while averaging.
        /// </summary>
        public float UpdateAccumulation(bool enabled, bool specular, int stateHash, bool forceReset)
        {
            if (!enabled || !specular)
            {
                RTHandles.Release(SpecularAccumulation);
                SpecularAccumulation = null;
            }
            if (!enabled)
            {
                RTHandles.Release(Accumulation);
                Accumulation = null;
                AccumulatedFrames = 0;
                return 0.0f;
            }
            if (Accumulation == null || (specular && SpecularAccumulation == null))
            {
                Accumulation ??= Alloc(GraphicsFormat.R32G32B32A32_SFloat, "_RayTracedGIAccumulation");
                if (specular)
                    SpecularAccumulation = Alloc(GraphicsFormat.R32G32B32A32_SFloat, "_RayTracedGISpecularAccumulation");
                AccumulatedFrames = 0;
            }
            if (forceReset || stateHash != _accumulationHash)
                AccumulatedFrames = 0;
            _accumulationHash = stateHash;
            AccumulatedFrames++;
            return 1.0f / AccumulatedFrames;
        }

        private RTHandle Alloc(GraphicsFormat format, string name)
        {
            return RTHandles.Alloc(Width, Height, colorFormat: format, enableRandomWrite: true,
                filterMode: FilterMode.Point, name: name);
        }

        private void ReleaseTargets()
        {
            ReleaseDenoiserTargets();
            Reservoirs?.Release();
            SecondarySurfaces?.Release();
            RTHandles.Release(DiffuseLighting);
            RTHandles.Release(SpecularLighting);
            RTHandles.Release(Accumulation);
            RTHandles.Release(SpecularAccumulation);
            RTHandles.Release(PreviousDepth);
            RTHandles.Release(PreviousNormalRoughness);
            Reservoirs = null;
            SecondarySurfaces = null;
            DiffuseLighting = null;
            SpecularLighting = null;
            Accumulation = null;
            PreviousDepth = null;
            PreviousNormalRoughness = null;
        }

        /// <summary>
        /// Allocates the radiance cache while it is enabled and releases it otherwise.
        /// </summary>
        public void EnsureRadianceCache(bool enabled)
        {
            if (enabled == (CacheHashEntries != null))
                return;
            ReleaseRadianceCache();
            if (!enabled)
                return;
            CacheHashEntries = new GraphicsBuffer(GraphicsBuffer.Target.Structured, CacheCapacity, CacheHashEntryStride) { name = "RayTracedGICacheHashEntries" };
            CacheAccumulation = new GraphicsBuffer(GraphicsBuffer.Target.Structured, CacheCapacity, CacheVoxelStride) { name = "RayTracedGICacheAccumulation" };
            CacheResolved = new GraphicsBuffer(GraphicsBuffer.Target.Structured, CacheCapacity, CacheVoxelStride) { name = "RayTracedGICacheResolved" };
            CacheNeedsClear = true;
        }

        private void ReleaseRadianceCache()
        {
            CacheHashEntries?.Release();
            CacheAccumulation?.Release();
            CacheResolved?.Release();
            CacheHashEntries = null;
            CacheAccumulation = null;
            CacheResolved = null;
        }

        public void Dispose()
        {
            ReleaseTargets();
            ReleaseRadianceCache();
            _lightBaker?.Dispose();
            _lightBaker = null;
        }
    }
}
