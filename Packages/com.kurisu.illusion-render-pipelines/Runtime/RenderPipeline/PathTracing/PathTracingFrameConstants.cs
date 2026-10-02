using System;
using UnityEngine;

namespace Illusion.Rendering.PathTracing
{
    internal static class PathTracingFrameConstants
    {
        public static PathTracingCameraData BuildCamera(Camera camera, int width, int height, in PathTracingLens lens, int coneHeight = 0)
        {
            var transform = camera.transform;
            float tanY = Mathf.Tan(0.5f * camera.fieldOfView * Mathf.Deg2Rad);
            float aspect = (float)width / height;
            float focus = lens.IsThinLens ? lens.FocusDistance : 1.0f;
            return new PathTracingCameraData
            {
                PosW = transform.position,
                NearZ = camera.nearClipPlane,
                DirectionW = transform.forward,
                PixelConeSpreadAngle = Mathf.Atan(2.0f * tanY / (coneHeight > 0 ? coneHeight : height)),
                CameraU = transform.right * (tanY * aspect * focus),
                FarZ = camera.farClipPlane,
                CameraV = -transform.up * (tanY * focus),
                FocalDistance = focus,
                CameraW = transform.forward * focus,
                AspectRatio = aspect,
                ViewportSizeX = (uint)width,
                ViewportSizeY = (uint)height,
                ApertureRadius = lens.ApertureRadius,
                Jitter = Vector2.zero
            };
        }

        public static PathTracingViewConstants BuildView(Matrix4x4 worldToView, Matrix4x4 viewToClip, int width, int height)
        {
            var worldToClip = viewToClip * worldToView;
            return new PathTracingViewConstants
            {
                MatWorldToView = worldToView,
                MatViewToClip = viewToClip,
                MatWorldToClip = worldToClip,
                MatWorldToClipNoOffset = worldToClip,
                MatClipToWorldNoOffset = worldToClip.inverse,
                ViewportOrigin = Vector2.zero,
                ViewportSize = new Vector2(width, height),
                ViewportSizeInv = new Vector2(1.0f / width, 1.0f / height),
                PixelOffset = Vector2.zero,
                ClipToWindowScale = new Vector2(0.5f * width, -0.5f * height),
                ClipToWindowBias = new Vector2(0.5f * width, 0.5f * height)
            };
        }

        public static PathTracerConstants BuildPathTracer(PathTracing settings, PathTracingCameraContext context,
            PathTracingCameraData camera, bool lightSampling)
        {
            return new PathTracerConstants
            {
                ImageWidth = (uint)context.Width,
                ImageHeight = (uint)context.Height,
                SampleBaseIndex = context.SampleIndex,
                PerPixelJitterAAScale = 1.0f,
                BounceCount = (uint)settings.bounceCount.value,
                DiffuseBounceCount = (uint)settings.diffuseBounceCount.value,
                EnvironmentMapDiffuseSampleMIPLevel = settings.environmentDiffuseMipOffset.value,
                TexLODBias = 0.0f,
                InvSubSampleCount = 1.0f,
                FireflyFilterThreshold = settings.referenceFireflyFilterThreshold.value,
                PreExposedGrayLuminance = 1.0f,
                FrameIndex = (uint)PathTracingFrame.Index,
                StablePlanesSplitStopThreshold = 0.95f,
                StablePlanesAntiAliasingFallthrough = 0.6f,
                ActiveStablePlaneCount = 3,
                MaxStablePlaneVertexDepth = 8,
                AllowPrimarySurfaceReplacement = 1,
                NEEEnabled = lightSampling ? 1u : 0u,
                NEEType = 2,
                NEECandidateSamples = 5,
                NEEFullSamples = 1,
                Camera = camera,
                PrevCamera = context.SampleCount > 0 ? context.PreviousCameraData : camera
            };
        }

        private const int RealtimeJitterPhases = 32;
        private const float RealtimeMicroJitter = 0.1f;
        private const uint MaxStablePlaneVertexDepth = 9;
        private const float DenoiserRadianceClampK = 8.0f;
        private const float RayReconstructionBrightnessClampK = 4096.0f;

        public static Vector2 RealtimeJitter(uint frameIndex, float renderScale)
        {
            int phases = Math.Max(RealtimeJitterPhases, (int)Math.Ceiling(8.0f / (renderScale * renderScale)));
            int index = (int)(frameIndex % (uint)phases) + 1;
            return new Vector2(Halton(index, 2) - 0.5f, Halton(index, 3) - 0.5f);
        }

        public static PathTracerConstants BuildRealtimePathTracer(PathTracing settings, PathTracingCameraContext context,
            PathTracingRealtimeTargets targets, PathTracingCameraData camera)
        {
            int samplesPerPixel = settings.realtimeSamplesPerPixel.value;
            var constants = BuildPathTracer(settings, context, camera, settings.lightSampling.value);
            constants.FireflyFilterThreshold = settings.realtimeFireflyFilterThreshold.value;
            constants.SampleBaseIndex = context.RealtimeFrameIndex * (uint)samplesPerPixel;
            constants.InvSubSampleCount = 1.0f / samplesPerPixel;
            constants.PerPixelJitterAAScale = RealtimeMicroJitter;
            constants.ActiveStablePlaneCount = PathTracingRealtimeTargets.StablePlaneCount;
            constants.MaxStablePlaneVertexDepth = Math.Min(MaxStablePlaneVertexDepth, (uint)settings.bounceCount.value);
            constants.StablePlanesSuppressPrimaryIndirectSpecularK = 0.6f;
            constants.DenoiserRadianceClampK = DenoiserRadianceClampK;
            constants.DLSSRRBrightnessClampK = RayReconstructionBrightnessClampK;
            constants.GenericTSLineStride = targets.LineStride;
            constants.GenericTSPlaneStride = targets.PlaneStride;
            constants.PrevCamera = camera;
            return constants;
        }

        private static float Halton(int index, int radix)
        {
            float result = 0.0f;
            float fraction = 1.0f / radix;
            while (index > 0)
            {
                result += index % radix * fraction;
                index /= radix;
                fraction /= radix;
            }
            return result;
        }

        public static PathTracingEnvMapSceneParams BuildEnvironment(bool enabled, float intensity, Quaternion rotation)
        {
            var transform = Matrix4x4.Rotate(rotation);
            return new PathTracingEnvMapSceneParams
            {
                Transform = PathTracingMatrix3x4.FromMatrix(transform),
                InvTransform = PathTracingMatrix3x4.FromMatrix(transform.inverse),
                ColorMultiplier = Vector3.one * intensity,
                Enabled = enabled ? 1.0f : 0.0f
            };
        }
    }
}
