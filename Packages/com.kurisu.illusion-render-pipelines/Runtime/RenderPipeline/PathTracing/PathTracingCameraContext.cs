using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Illusion.Rendering.PathTracing
{
    internal sealed class PathTracingCameraContext : IDisposable
    {
        private const float CutDistance = 2.0f;

        private const float CutAngle = 45.0f;

        public RTHandle Radiance { get; private set; }

        public RTHandle Accumulation { get; private set; }

        public RTHandle Depth { get; private set; }

        public RTHandle MotionVectors { get; private set; }

        public RTHandle Throughput { get; private set; }

        public RTHandle SpecularHitT { get; private set; }

        public int Width { get; private set; }

        public int Height { get; private set; }

        public uint SampleCount { get; private set; }

        public uint SampleIndex { get; private set; }

        public PathTracingCameraData PreviousCameraData { get; private set; }

        public int LastUsedFrame { get; set; }

        public PathTracingShaderTime AccumulationTime { get; private set; }

        private int _stateHash;

        private PathTracingLightBaker _lightBaker;

        private PathTracingRealtimeTargets _realtime;

        private int _realtimeHash;

        private Vector3 _realtimePosition;

        private Quaternion _realtimeRotation;

        public uint RealtimeFrameIndex { get; private set; }

        public Matrix4x4 PreviousWorldToView { get; private set; }

        public Matrix4x4 PreviousViewToClip { get; private set; }

        public bool HasRealtimeHistory { get; private set; }

        public Matrix4x4 ReferencePreviousWorldToView { get; private set; }

        public Matrix4x4 ReferencePreviousViewToClip { get; private set; }

        public bool HasReferencePreviousView { get; private set; }

        public PathTracingLightBaker GetLightBaker(PathTracingLightBakerKernels kernels)
        {
            return _lightBaker ??= new PathTracingLightBaker(kernels);
        }

        public PathTracingRealtimeTargets GetRealtimeTargets(int width, int height, int outputWidth, int outputHeight)
        {
            _realtime ??= new PathTracingRealtimeTargets();
            _realtime.Ensure(width, height, outputWidth, outputHeight);
            return _realtime;
        }

        public void ReleaseRealtime()
        {
            _realtime?.Dispose();
            _realtime = null;
            HasRealtimeHistory = false;
        }

        public void BeginRealtimeFrame(int settingsHash, Transform camera)
        {
            HasReferencePreviousView = false;
            bool cut = Vector3.Distance(camera.position, _realtimePosition) > CutDistance
                || Quaternion.Angle(camera.rotation, _realtimeRotation) > CutAngle;
            if (cut || settingsHash != _realtimeHash)
                HasRealtimeHistory = false;
            _realtimeHash = settingsHash;
            _realtimePosition = camera.position;
            _realtimeRotation = camera.rotation;
        }

        public void EndRealtimeFrame(Matrix4x4 worldToView, Matrix4x4 viewToClip)
        {
            RealtimeFrameIndex++;
            PreviousWorldToView = worldToView;
            PreviousViewToClip = viewToClip;
            HasRealtimeHistory = true;
        }

        public void EnsureTargets(int width, int height)
        {
            if (Radiance != null && Width == width && Height == height)
                return;

            ReleaseTargets();
            Width = width;
            Height = height;
            Radiance = Alloc(GraphicsFormat.R32G32B32A32_SFloat, "_PathTracingRadiance");
            Accumulation = Alloc(GraphicsFormat.R32G32B32A32_SFloat, "_PathTracingAccumulation");
            Depth = Alloc(GraphicsFormat.R32_SFloat, "_PathTracingDepth");
            MotionVectors = Alloc(GraphicsFormat.R16G16B16A16_SFloat, "_PathTracingMotionVectors");
            Throughput = Alloc(GraphicsFormat.R32_UInt, "_PathTracingThroughput");
            SpecularHitT = Alloc(GraphicsFormat.R32_SFloat, "_PathTracingSpecularHitT");
            SampleCount = 0;
        }

        public void UpdateAccumulation(int stateHash, bool forceReset)
        {
            if (forceReset || stateHash != _stateHash)
                SampleCount = 0;
            _stateHash = stateHash;
            if (SampleCount == 0)
                AccumulationTime = PathTracingShaderTime.Current;
        }

        public void AdvanceSample(PathTracingCameraData cameraData, Matrix4x4 worldToView, Matrix4x4 viewToClip)
        {
            SampleCount++;
            SampleIndex++;
            PreviousCameraData = cameraData;
            ReferencePreviousWorldToView = worldToView;
            ReferencePreviousViewToClip = viewToClip;
            HasReferencePreviousView = true;
        }

        private RTHandle Alloc(GraphicsFormat format, string name)
        {
            return RTHandles.Alloc(Width, Height, colorFormat: format, enableRandomWrite: true,
                filterMode: FilterMode.Point, name: name);
        }

        private void ReleaseTargets()
        {
            RTHandles.Release(Radiance);
            RTHandles.Release(Accumulation);
            RTHandles.Release(Depth);
            RTHandles.Release(MotionVectors);
            RTHandles.Release(Throughput);
            RTHandles.Release(SpecularHitT);
            Radiance = Accumulation = Depth = MotionVectors = Throughput = SpecularHitT = null;
        }

        public void Dispose()
        {
            ReleaseTargets();
            ReleaseRealtime();
            _lightBaker?.Dispose();
            _lightBaker = null;
        }
    }
}
