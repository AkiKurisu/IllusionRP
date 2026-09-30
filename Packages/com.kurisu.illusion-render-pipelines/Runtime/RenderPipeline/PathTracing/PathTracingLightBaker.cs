/*
* Copyright (c) 2025, NVIDIA CORPORATION.  All rights reserved.
*
* NVIDIA CORPORATION and its licensors retain all intellectual property
* and proprietary rights in and to this software, related documentation
* and any modifications thereto.  Any use, reproduction, disclosure or
* distribution of this software and related documentation without an express
* license agreement from NVIDIA CORPORATION is strictly prohibited.
*/


using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Config = Illusion.Rendering.PathTracing.PathTracingLightingConfig;

namespace Illusion.Rendering.PathTracing
{
    internal sealed partial class PathTracingLightBaker : IDisposable
    {
        private const float GlobalTemporalFeedbackWeight = 0.75f;
        private const float LocalToGlobalSampleRatio = 0.65f;
        private const float DistantVsLocalImportanceScale = 1.0f;
        private const float DistantBaseImportanceScale = 0.0002f;
        private const float ScreenSpaceVsWorldSpaceThreshold = 0.3f;
        private const float ReservoirHistoryDropoff = 0.005f;
        private const float DepthDisocclusionThreshold = 1.5f;
        private const float ImportanceBoostIntensityDelta = 64.0f;
        private const float ImportanceBoostFrustumMul = 8.0f;
        private const float ImportanceBoostFrustumFadeDistance = 5.0f;
        private const float AverageContentsDistance = 10.0f;

        private readonly PathTracingLightBakerKernels _kernels;

        private readonly LightingControlData[] _control = new LightingControlData[1];

        private PolymorphicLightInfo[] _uploadLights = Array.Empty<PolymorphicLightInfo>();

        private PolymorphicLightInfoEx[] _uploadLightsEx = Array.Empty<PolymorphicLightInfoEx>();

        private uint[] _uploadCurrentToPast = Array.Empty<uint>();

        private uint[] _uploadPastToCurrent = Array.Empty<uint>();

        private Dictionary<int, uint> _previousIndices = new();

        private Dictionary<int, uint> _currentIndices = new();

        private bool _ping;

        private uint _updateCounter;

        private Vector2 _jitter;

        private Vector2Int _tileJitter;

        private Vector2Int _previousTileJitter;

        private bool _feedbackFilled;

        private bool _previousFeedbackAvailable;

        private uint _historicTotalLightCount;

        private int _previousAnalyticCount;

        private int _previousTriangleCount;

        private int _previousEmissiveGeneration = -1;

        private int _triangleCount;

        private int _lastFrame = -1;

        public PathTracingLightBaker(PathTracingLightBakerKernels kernels)
        {
            _kernels = kernels;
            CreateBuffers();
        }

        public void UpdateBegin(CommandBuffer cmd, PathTracingLightCollector lights, PathTracingEnvironment environment,
            PathTracingEmissiveTable emissive, Vector3 cameraPosition, Matrix4x4 worldToClip, int width, int height)
        {
            bool resetFeedback = _lastFrame < 0 || PathTracingFrame.Index - _lastFrame > 1;
            resetFeedback |= EnsureTargets(width, height);
            _lastFrame = PathTracingFrame.Index;
            _ping = !_ping;
            if (resetFeedback)
            {
                _updateCounter = 0;
                _jitter = Vector2.zero;
                _tileJitter = _previousTileJitter = Vector2Int.zero;
                _feedbackFilled = false;
            }
            AdvanceTileJitter();
            _updateCounter++;

            bool feedbackAvailable = _feedbackFilled;
            int analyticCount = lights.Lights.Count;
            _triangleCount = emissive.TriangleCount;
            if (analyticCount + _triangleCount > Config.MaxLights - Config.EnvQuadTotalNodeCount)
                throw new InvalidOperationException("Path tracing light count exceeds the light buffer capacity.");
            uint totalLightCount = (uint)(Config.EnvQuadTotalNodeCount + analyticCount + _triangleCount);

            ref var control = ref _control[0];
            control = default;
            control.TotalLightCount = totalLightCount;
            control.EnvmapQuadNodeCount = Config.EnvQuadTotalNodeCount;
            control.AnalyticLightCount = (uint)analyticCount;
            control.TriangleLightCount = (uint)_triangleCount;
            control.HistoricTotalLightCount = _historicTotalLightCount;
            control.LastFrameTemporalFeedbackAvailable = feedbackAvailable ? 1u : 0u;
            control.LastFrameLocalSamplesAvailable = _previousFeedbackAvailable && feedbackAvailable ? 1u : 0u;
            control.ImportanceSamplingType = Config.ImportanceSamplingNEEAT;
            control.TemporalFeedbackRequired = 1;
            control.TotalMaxFeedbackCount = feedbackAvailable ? (uint)(DivCeil(width, Config.ComputeThreads2D) * DivCeil(height, Config.ComputeThreads2D) * Config.ComputeThreads2D * Config.ComputeThreads2D) : 0u;
            control.GlobalFeedbackUseWeight = feedbackAvailable ? GlobalTemporalFeedbackWeight : 0.0f;
            control.LocalToGlobalSampleRatio = feedbackAvailable ? LocalToGlobalSampleRatio : 0.0f;
            control.LocalSamplingResolutionX = (uint)_localSamplingWidth;
            control.LocalSamplingResolutionY = (uint)_localSamplingHeight;
            control.TileBufferHeight = (uint)_localSamplingHeight;
            control.ScreenSpaceVsWorldSpaceThreshold = ScreenSpaceVsWorldSpaceThreshold;
            control.LocalSamplingTileJitterX = (uint)_tileJitter.x;
            control.LocalSamplingTileJitterY = (uint)_tileJitter.y;
            control.LocalSamplingTileJitterPrevX = (uint)_previousTileJitter.x;
            control.LocalSamplingTileJitterPrevY = (uint)_previousTileJitter.y;

            float distantImportance = DistantVsLocalImportanceScale * DistantBaseImportanceScale;
            ref var baker = ref control.BakerConstants;
            baker.DistantVsLocalRelativeImportance = distantImportance;
            baker.EnvMapImportanceMapMIPCount = (uint)environment.ImportanceMapMipCount;
            baker.EnvMapImportanceMapResolution = PathTracingEnvironment.ImportanceMapSize;
            baker.EnvMapParams = PathTracingFrameConstants.BuildEnvironment(true, 1.0f, Quaternion.identity);
            baker.FeedbackResolutionX = (uint)width;
            baker.FeedbackResolutionY = (uint)height;
            baker.BlendedFeedbackResolutionX = (uint)DivCeil(width, Config.EarlyFeedbackTileSize);
            baker.BlendedFeedbackResolutionY = (uint)DivCeil(height, Config.EarlyFeedbackTileSize);
            baker.PrevOverCurrentViewportSize = Vector2.one;
            baker.UpdateCounter = _updateCounter;
            baker.ImportanceBoostIntensityDelta = ImportanceBoostIntensityDelta;
            baker.ImportanceBoostFrustumMul = ImportanceBoostFrustumMul;
            baker.ImportanceBoostFrustumFadeDistance = ImportanceBoostFrustumFadeDistance;
            baker.SceneCameraPos = cameraPosition;
            baker.SceneAverageContentsDistance = AverageContentsDistance;
            baker.DepthDisocclusionThreshold = DepthDisocclusionThreshold;
            baker.EnableMotionReprojection = 1;
            baker.ReservoirHistoryDropoff = ReservoirHistoryDropoff;
            baker.CurrentWeightsBufferOffset = _ping ? 0u : (uint)Config.WeightsCountHalf;
            baker.HistoricWeightsBufferOffset = _ping ? (uint)Config.WeightsCountHalf : 0u;
            SetFrustumPlanes(ref baker, worldToClip);

            int pastToCurrentCount = FillUploadData(lights, analyticCount, emissive.Generation);
            _historicTotalLightCount = totalLightCount;
            _previousAnalyticCount = analyticCount;
            _previousTriangleCount = _triangleCount;
            _previousEmissiveGeneration = emissive.Generation;
            _previousFeedbackAvailable = feedbackAvailable;

            cmd.SetBufferData(_controlBuffer, _control);
            DispatchUpdateBegin(cmd, environment, emissive, analyticCount, totalLightCount, pastToCurrentCount, feedbackAvailable, width, height);
        }

        public void UpdateEnd(CommandBuffer cmd, RenderTargetIdentifier depth, RenderTargetIdentifier motionVectors, int width, int height)
        {
            DispatchUpdateEnd(cmd, depth, motionVectors, width, height);
            _feedbackFilled = true;
        }

        private int FillUploadData(PathTracingLightCollector lights, int analyticCount, int emissiveGeneration)
        {
            int count = Config.EnvQuadTotalNodeCount + analyticCount;
            int totalCount = count + _triangleCount;
            int pastToCurrentCount = Mathf.Max(totalCount, (int)_historicTotalLightCount);
            EnsureCapacity(ref _uploadLights, count);
            EnsureCapacity(ref _uploadLightsEx, count);
            EnsureCapacity(ref _uploadCurrentToPast, totalCount);
            EnsureCapacity(ref _uploadPastToCurrent, pastToCurrentCount);

            var placeholder = new PolymorphicLightInfo { ColorTypeAndFlags = PathTracingLightPacking.TypeBits(PolymorphicLightType.EnvironmentQuad) };
            for (int i = 0; i < Config.EnvQuadTotalNodeCount; i++)
            {
                _uploadLights[i] = placeholder;
                _uploadLightsEx[i] = default;
                _uploadCurrentToPast[i] = Config.InvalidLightIndex;
            }
            for (int i = 0; i < pastToCurrentCount; i++)
                _uploadPastToCurrent[i] = Config.InvalidLightIndex;

            _currentIndices.Clear();
            for (int i = 0; i < analyticCount; i++)
            {
                uint index = (uint)(Config.EnvQuadTotalNodeCount + i);
                int id = lights.LightIds[i];
                _uploadLights[index] = lights.Lights[i];
                _uploadLightsEx[index] = lights.LightsEx[i];
                uint historicIndex = _feedbackFilled && _previousIndices.TryGetValue(id, out var previous) ? previous : Config.InvalidLightIndex;
                _uploadCurrentToPast[index] = historicIndex;
                if (historicIndex != Config.InvalidLightIndex && historicIndex < pastToCurrentCount)
                    _uploadPastToCurrent[historicIndex] = index;
                _currentIndices[id] = index;
            }
            (_previousIndices, _currentIndices) = (_currentIndices, _previousIndices);

            bool triangleHistory = _feedbackFilled && emissiveGeneration == _previousEmissiveGeneration;
            int previousTriangleBase = Config.EnvQuadTotalNodeCount + _previousAnalyticCount;
            for (int t = 0; t < _triangleCount; t++)
            {
                uint index = (uint)(count + t);
                uint historicIndex = triangleHistory && t < _previousTriangleCount ? (uint)(previousTriangleBase + t) : Config.InvalidLightIndex;
                _uploadCurrentToPast[index] = historicIndex;
                if (historicIndex != Config.InvalidLightIndex && historicIndex < pastToCurrentCount)
                    _uploadPastToCurrent[historicIndex] = index;
            }
            return pastToCurrentCount;
        }

        private void AdvanceTileJitter()
        {
            _previousTileJitter = _tileJitter;
            if (_updateCounter % 1024 == 0)
                _jitter = Vector2.zero;
            const float g = 1.32471795724474602596f;
            _jitter.x = (_jitter.x + 1.0f / g) % 1.0f;
            _jitter.y = (_jitter.y + 1.0f / (g * g)) % 1.0f;
            int tile = Config.SamplingBufferTileSize;
            _tileJitter = new Vector2Int(Mathf.Clamp((int)(_jitter.x * tile), 0, tile - 1), Mathf.Clamp((int)(_jitter.y * tile), 0, tile - 1));
        }

        private static readonly Vector4[] FrustumPlanes = new Vector4[6];

        private static unsafe void SetFrustumPlanes(ref LightsBakerConstants baker, Matrix4x4 m)
        {
            var planes = FrustumPlanes;
            planes[0] = Plane(m, 0, 1.0f);
            planes[1] = Plane(m, 0, -1.0f);
            planes[2] = Plane(m, 1, -1.0f);
            planes[3] = Plane(m, 1, 1.0f);
            planes[4] = Plane(m, 2, -1.0f);
            planes[5] = new Vector4(-planes[4].x, -planes[4].y, -planes[4].z, -planes[4].w - Config.DistantLightDistance);
            fixed (float* destination = baker.FrustumPlanes)
            {
                for (int i = 0; i < 6; i++)
                {
                    destination[i * 4 + 0] = planes[i].x;
                    destination[i * 4 + 1] = planes[i].y;
                    destination[i * 4 + 2] = planes[i].z;
                    destination[i * 4 + 3] = planes[i].w;
                }
            }
        }

        private static Vector4 Plane(Matrix4x4 m, int row, float sign)
        {
            var plane = new Vector4(m[3, 0] + sign * m[row, 0], m[3, 1] + sign * m[row, 1], m[3, 2] + sign * m[row, 2],
                -(m[3, 3] + sign * m[row, 3]));
            float length = new Vector3(plane.x, plane.y, plane.z).magnitude;
            return length > 0.0f ? plane / length : plane;
        }

        private static void EnsureCapacity<T>(ref T[] array, int count)
        {
            if (array.Length < count)
                array = new T[Mathf.NextPowerOfTwo(count)];
        }

        private static int DivCeil(int value, int divisor) => (value + divisor - 1) / divisor;
    }
}
