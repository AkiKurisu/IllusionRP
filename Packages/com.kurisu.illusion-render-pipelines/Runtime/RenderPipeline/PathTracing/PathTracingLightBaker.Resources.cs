/*
* Copyright (c) 2025, NVIDIA CORPORATION.  All rights reserved.
*
* NVIDIA CORPORATION and its licensors retain all intellectual property
* and proprietary rights in and to this software, related documentation
* and any modifications thereto.  Any use, reproduction, disclosure or
* distribution of this software and related documentation without an express
* license agreement from NVIDIA CORPORATION is strictly prohibited.
*/


using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Config = Illusion.Rendering.PathTracing.PathTracingLightingConfig;

namespace Illusion.Rendering.PathTracing
{
    internal sealed class PathTracingLightBakerKernels
    {
        public readonly ComputeShader Shader;
        public readonly int ResetPastToCurrentHistory;
        public readonly int EnvLightsBackupPast;
        public readonly int EnvLightsSubdivideBase;
        public readonly int EnvLightsSubdivideBoost;
        public readonly int EnvLightsFillLookupMap;
        public readonly int EnvLightsMapPastToCurrent;
        public readonly int ResetLightProxyCounters;
        public readonly int ComputeWeights;
        public readonly int ComputeProxyCounts;
        public readonly int ComputeProxyBaselineOffsets;
        public readonly int CreateProxyJobs;
        public readonly int ExecuteProxyJobs;
        public readonly int ProcessFeedbackHistoryPreFilter;
        public readonly int ProcessFeedbackHistoryP0;
        public readonly int ProcessFeedbackHistoryP1a;
        public readonly int ProcessFeedbackHistoryP1b;
        public readonly int ProcessFeedbackHistoryP2;
        public readonly int ProcessFeedbackHistoryP3;
        public readonly int ClearFeedbackHistory;

        public PathTracingLightBakerKernels(ComputeShader shader)
        {
            Shader = shader;
            ResetPastToCurrentHistory = shader.FindKernel(nameof(ResetPastToCurrentHistory));
            EnvLightsBackupPast = shader.FindKernel(nameof(EnvLightsBackupPast));
            EnvLightsSubdivideBase = shader.FindKernel(nameof(EnvLightsSubdivideBase));
            EnvLightsSubdivideBoost = shader.FindKernel(nameof(EnvLightsSubdivideBoost));
            EnvLightsFillLookupMap = shader.FindKernel(nameof(EnvLightsFillLookupMap));
            EnvLightsMapPastToCurrent = shader.FindKernel(nameof(EnvLightsMapPastToCurrent));
            ResetLightProxyCounters = shader.FindKernel(nameof(ResetLightProxyCounters));
            ComputeWeights = shader.FindKernel(nameof(ComputeWeights));
            ComputeProxyCounts = shader.FindKernel(nameof(ComputeProxyCounts));
            ComputeProxyBaselineOffsets = shader.FindKernel(nameof(ComputeProxyBaselineOffsets));
            CreateProxyJobs = shader.FindKernel(nameof(CreateProxyJobs));
            ExecuteProxyJobs = shader.FindKernel(nameof(ExecuteProxyJobs));
            ProcessFeedbackHistoryPreFilter = shader.FindKernel(nameof(ProcessFeedbackHistoryPreFilter));
            ProcessFeedbackHistoryP0 = shader.FindKernel(nameof(ProcessFeedbackHistoryP0));
            ProcessFeedbackHistoryP1a = shader.FindKernel(nameof(ProcessFeedbackHistoryP1a));
            ProcessFeedbackHistoryP1b = shader.FindKernel(nameof(ProcessFeedbackHistoryP1b));
            ProcessFeedbackHistoryP2 = shader.FindKernel(nameof(ProcessFeedbackHistoryP2));
            ProcessFeedbackHistoryP3 = shader.FindKernel(nameof(ProcessFeedbackHistoryP3));
            ClearFeedbackHistory = shader.FindKernel(nameof(ClearFeedbackHistory));
        }
    }

    internal sealed partial class PathTracingLightBaker
    {
        private readonly List<GraphicsBuffer> _buffers = new();

        private readonly List<RenderTexture> _targets = new();

        private GraphicsBuffer _controlBuffer;
        private GraphicsBuffer _lightsBuffer;
        private GraphicsBuffer _lightsExBuffer;
        private GraphicsBuffer _scratchBuffer;
        private GraphicsBuffer _scratchList;
        private GraphicsBuffer _lightWeights;
        private GraphicsBuffer _historyRemapCurrentToPast;
        private GraphicsBuffer _historyRemapPastToCurrent;
        private GraphicsBuffer _perLightProxyCounters;
        private GraphicsBuffer _lightSamplingProxies;
        private GraphicsBuffer _localSamplingBuffer;

        private RenderTexture _envLightLookupMap;
        private RenderTexture _feedbackTotalWeight;
        private RenderTexture _feedbackCandidates;
        private RenderTexture _feedbackTotalWeightScratch;
        private RenderTexture _feedbackCandidatesScratch;
        private RenderTexture _feedbackTotalWeightBlended;
        private RenderTexture _feedbackCandidatesBlended;
        private RenderTexture _historyDepth;

        private int _width;
        private int _height;
        private int _localSamplingWidth;
        private int _localSamplingHeight;

        public GraphicsBuffer ControlBuffer => _controlBuffer;
        public GraphicsBuffer LightsBuffer => _lightsBuffer;
        public GraphicsBuffer LightsExBuffer => _lightsExBuffer;
        public GraphicsBuffer ProxyCounters => _perLightProxyCounters;
        public GraphicsBuffer ProxyIndices => _lightSamplingProxies;
        public GraphicsBuffer LocalSamplingBuffer => _localSamplingBuffer;
        public RenderTexture EnvLightLookupMap => _envLightLookupMap;
        public RenderTexture FeedbackTotalWeight => _feedbackTotalWeight;
        public RenderTexture FeedbackCandidates => _feedbackCandidates;

        private void CreateBuffers()
        {
            int remapCount = 2 * Config.WeightsCountHalf;
            _controlBuffer = CreateBuffer(GraphicsBuffer.Target.Structured, 1, LightingControlData.Stride);
            _lightsBuffer = CreateBuffer(GraphicsBuffer.Target.Structured, Config.MaxLights, PolymorphicLightInfo.Stride);
            _lightsExBuffer = CreateBuffer(GraphicsBuffer.Target.Structured, Config.MaxLights, PolymorphicLightInfoEx.Stride);
            _scratchBuffer = CreateBuffer(GraphicsBuffer.Target.Raw, Config.MaxProxyProcTasks * Config.ProxyBuildTaskStride / sizeof(uint), sizeof(uint));
            _scratchList = CreateBuffer(GraphicsBuffer.Target.Structured, remapCount, sizeof(uint));
            _lightWeights = CreateBuffer(GraphicsBuffer.Target.Structured, remapCount, sizeof(float));
            _historyRemapCurrentToPast = CreateBuffer(GraphicsBuffer.Target.Structured, remapCount, sizeof(uint));
            _historyRemapPastToCurrent = CreateBuffer(GraphicsBuffer.Target.Structured, remapCount, sizeof(uint));
            _perLightProxyCounters = CreateBuffer(GraphicsBuffer.Target.Structured, remapCount, sizeof(uint));
            _lightSamplingProxies = CreateBuffer(GraphicsBuffer.Target.Structured, Config.MaxSamplingProxies, sizeof(uint));
            _envLightLookupMap = CreateTarget("_PathTracingEnvLightLookup", PathTracingEnvironment.ImportanceMapSize, PathTracingEnvironment.ImportanceMapSize, GraphicsFormat.R32_UInt);
        }

        private bool EnsureTargets(int width, int height)
        {
            if (_feedbackTotalWeight != null && _width == width && _height == height && _feedbackTotalWeight.IsCreated())
                return false;

            ReleaseTargets();
            _width = width;
            _height = height;
            int blendedWidth = DivCeil(width, Config.EarlyFeedbackTileSize);
            int blendedHeight = DivCeil(height, Config.EarlyFeedbackTileSize);
            _feedbackTotalWeight = CreateTarget("_PathTracingFeedbackTotalWeight", width, height, GraphicsFormat.R32_SFloat);
            _feedbackCandidates = CreateTarget("_PathTracingFeedbackCandidates", width, height, GraphicsFormat.R32_UInt);
            _feedbackTotalWeightScratch = CreateTarget("_PathTracingFeedbackTotalWeightScratch", width, height, GraphicsFormat.R32_SFloat);
            _feedbackCandidatesScratch = CreateTarget("_PathTracingFeedbackCandidatesScratch", width, height, GraphicsFormat.R32_UInt);
            _feedbackTotalWeightBlended = CreateTarget("_PathTracingFeedbackTotalWeightBlended", blendedWidth, blendedHeight, GraphicsFormat.R32_SFloat);
            _feedbackCandidatesBlended = CreateTarget("_PathTracingFeedbackCandidatesBlended", blendedWidth, blendedHeight, GraphicsFormat.R32_UInt);
            _historyDepth = CreateTarget("_PathTracingFeedbackHistoryDepth", width, height, GraphicsFormat.R32_SFloat);

            _localSamplingWidth = DivCeil(width, Config.SamplingBufferTileSize) + 1;
            _localSamplingHeight = DivCeil(height, Config.SamplingBufferTileSize) + 1;
            _localSamplingBuffer = CreateBuffer(GraphicsBuffer.Target.Structured, _localSamplingWidth * _localSamplingHeight * Config.LocalProxyCount, sizeof(uint));
            return true;
        }

        private GraphicsBuffer CreateBuffer(GraphicsBuffer.Target target, int count, int stride)
        {
            var buffer = new GraphicsBuffer(target, count, stride);
            _buffers.Add(buffer);
            return buffer;
        }

        private RenderTexture CreateTarget(string name, int width, int height, GraphicsFormat format)
        {
            var target = new RenderTexture(new RenderTextureDescriptor(width, height, format, GraphicsFormat.None)
            {
                enableRandomWrite = true,
                msaaSamples = 1
            })
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point
            };
            target.Create();
            _targets.Add(target);
            return target;
        }

        private void ReleaseTargets()
        {
            foreach (var target in new[] { _feedbackTotalWeight, _feedbackCandidates, _feedbackTotalWeightScratch, _feedbackCandidatesScratch,
                         _feedbackTotalWeightBlended, _feedbackCandidatesBlended, _historyDepth })
            {
                if (target == null)
                    continue;
                _targets.Remove(target);
                CoreUtils.Destroy(target);
            }
            if (_localSamplingBuffer != null)
            {
                _buffers.Remove(_localSamplingBuffer);
                _localSamplingBuffer.Release();
                _localSamplingBuffer = null;
            }
        }

        public void Dispose()
        {
            foreach (var buffer in _buffers)
                buffer.Release();
            _buffers.Clear();
            foreach (var target in _targets)
                CoreUtils.Destroy(target);
            _targets.Clear();
            _feedbackTotalWeight = null;
        }
    }
}
