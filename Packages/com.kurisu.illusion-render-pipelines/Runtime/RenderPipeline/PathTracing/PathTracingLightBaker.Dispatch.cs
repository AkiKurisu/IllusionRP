/*
* Copyright (c) 2025, NVIDIA CORPORATION.  All rights reserved.
*
* NVIDIA CORPORATION and its licensors retain all intellectual property
* and proprietary rights in and to this software, related documentation
* and any modifications thereto.  Any use, reproduction, disclosure or
* distribution of this software and related documentation without an express
* license agreement from NVIDIA CORPORATION is strictly prohibited.
*/


using UnityEngine;
using UnityEngine.Rendering;
using Config = Illusion.Rendering.PathTracing.PathTracingLightingConfig;

namespace Illusion.Rendering.PathTracing
{
    internal sealed partial class PathTracingLightBaker
    {
        private void DispatchUpdateBegin(CommandBuffer cmd, PathTracingEnvironment environment, PathTracingEmissiveTable emissive,
            int analyticCount, uint totalLightCount, int pastToCurrentCount, bool feedbackAvailable, int width, int height)
        {
            var k = _kernels;
            _environmentRadiance = environment.RadianceMap;
            int lightCount = (int)totalLightCount;
            int historicCount = Mathf.Max(lightCount, (int)_control[0].HistoricTotalLightCount);

            Dispatch(cmd, k.ResetLightProxyCounters, DivCeil(lightCount + 1, Config.ComputeThreads), 1);
            Dispatch(cmd, k.ResetPastToCurrentHistory, DivCeil(historicCount, Config.ComputeThreads), 1);
            Dispatch(cmd, k.EnvLightsBackupPast, DivCeil(Config.EnvQuadTotalNodeCount, Config.ComputeThreads), 1);

            int cpuLightCount = Config.EnvQuadTotalNodeCount + analyticCount;
            cmd.SetBufferData(_lightsBuffer, _uploadLights, 0, 0, cpuLightCount);
            cmd.SetBufferData(_lightsExBuffer, _uploadLightsEx, 0, 0, cpuLightCount);
            emissive.WriteLights(cmd, _lightsBuffer, _lightsExBuffer, cpuLightCount, lightCount - cpuLightCount);
            cmd.SetBufferData(_historyRemapCurrentToPast, _uploadCurrentToPast, 0, 0, lightCount);
            cmd.SetBufferData(_historyRemapPastToCurrent, _uploadPastToCurrent, 0, 0, pastToCurrentCount);

            Dispatch(cmd, k.EnvLightsSubdivideBase, 1, 1);
            Dispatch(cmd, k.EnvLightsSubdivideBoost, Config.EnvQuadUnboostedNodeCount, 1);
            Dispatch(cmd, k.EnvLightsFillLookupMap, Config.EnvQuadTotalNodeCount, 1);
            Dispatch(cmd, k.EnvLightsMapPastToCurrent, DivCeil(Config.EnvQuadTotalNodeCount, Config.ComputeThreads), 1);

            if (feedbackAvailable)
            {
                Dispatch(cmd, k.ProcessFeedbackHistoryPreFilter, DivCeil(width, Config.PreprocessBlockSizeInner), DivCeil(height, Config.PreprocessBlockSizeInner));
                Dispatch(cmd, k.ProcessFeedbackHistoryP0, DivCeil(width, Config.ComputeThreads2D), DivCeil(height, Config.ComputeThreads2D));
            }

            Dispatch(cmd, k.ComputeWeights, DivCeil(lightCount, Config.LocalBlockSize * Config.ComputeThreads), 1);
            Dispatch(cmd, k.ComputeProxyCounts, DivCeil(lightCount, Config.ComputeThreads), 1);
            Dispatch(cmd, k.ComputeProxyBaselineOffsets, 1, 1);
            Dispatch(cmd, k.CreateProxyJobs, DivCeil(lightCount, Config.ComputeThreads), 1);
            Dispatch(cmd, k.ExecuteProxyJobs, DivCeil(Config.MaxProxyProcTasks, Config.ComputeThreads), 1);
        }

        private void DispatchUpdateEnd(CommandBuffer cmd, RenderTargetIdentifier depth, RenderTargetIdentifier motionVectors, int width, int height)
        {
            var k = _kernels;
            int threads = Config.ComputeThreads2D;
            _depth = depth;
            _motionVectors = motionVectors;
            Dispatch(cmd, k.ProcessFeedbackHistoryP1a, DivCeil(DivCeil(width, Config.EarlyFeedbackTileSize), threads), DivCeil(DivCeil(height, Config.EarlyFeedbackTileSize), threads));
            Dispatch(cmd, k.ProcessFeedbackHistoryP1b, DivCeil(width, threads), DivCeil(height, threads));
            Dispatch(cmd, k.ProcessFeedbackHistoryP2, DivCeil(_localSamplingWidth, threads), DivCeil(_localSamplingHeight, threads));
            Dispatch(cmd, k.ProcessFeedbackHistoryP3, _localSamplingWidth, _localSamplingHeight);
            Dispatch(cmd, k.ClearFeedbackHistory, DivCeil(width, threads), DivCeil(height, threads));
        }

        private RenderTargetIdentifier _environmentRadiance = Texture2D.blackTexture;

        private RenderTargetIdentifier _depth = Texture2D.blackTexture;

        private RenderTargetIdentifier _motionVectors = Texture2D.blackTexture;

        private void Dispatch(CommandBuffer cmd, int kernel, int groupsX, int groupsY)
        {
            if (groupsX <= 0 || groupsY <= 0)
                return;

            var shader = _kernels.Shader;
            cmd.SetComputeBufferParam(shader, kernel, ShaderIDs.u_controlBuffer, _controlBuffer);
            cmd.SetComputeBufferParam(shader, kernel, ShaderIDs.u_lightsBuffer, _lightsBuffer);
            cmd.SetComputeBufferParam(shader, kernel, ShaderIDs.u_lightsExBuffer, _lightsExBuffer);
            cmd.SetComputeBufferParam(shader, kernel, ShaderIDs.u_scratchBuffer, _scratchBuffer);
            cmd.SetComputeBufferParam(shader, kernel, ShaderIDs.u_scratchList, _scratchList);
            cmd.SetComputeBufferParam(shader, kernel, ShaderIDs.u_lightWeights, _lightWeights);
            cmd.SetComputeBufferParam(shader, kernel, ShaderIDs.u_historyRemapCurrentToPast, _historyRemapCurrentToPast);
            cmd.SetComputeBufferParam(shader, kernel, ShaderIDs.u_historyRemapPastToCurrent, _historyRemapPastToCurrent);
            cmd.SetComputeBufferParam(shader, kernel, ShaderIDs.u_perLightProxyCounters, _perLightProxyCounters);
            cmd.SetComputeBufferParam(shader, kernel, ShaderIDs.u_lightSamplingProxies, _lightSamplingProxies);
            cmd.SetComputeBufferParam(shader, kernel, ShaderIDs.u_localSamplingBuffer, _localSamplingBuffer);
            cmd.SetComputeBufferParam(shader, kernel, ShaderIDs.t_controlBufferReadOnly, _controlBuffer);
            cmd.SetComputeBufferParam(shader, kernel, ShaderIDs.t_lightSamplingProxiesReadOnly, _lightSamplingProxies);
            cmd.SetComputeBufferParam(shader, kernel, ShaderIDs.t_localSamplingBufferReadOnly, _localSamplingBuffer);
            cmd.SetComputeTextureParam(shader, kernel, ShaderIDs.u_envLightLookupMap, _envLightLookupMap);
            cmd.SetComputeTextureParam(shader, kernel, ShaderIDs.u_feedbackTotalWeight, _feedbackTotalWeight);
            cmd.SetComputeTextureParam(shader, kernel, ShaderIDs.u_feedbackCandidates, _feedbackCandidates);
            cmd.SetComputeTextureParam(shader, kernel, ShaderIDs.u_feedbackTotalWeightScratch, _feedbackTotalWeightScratch);
            cmd.SetComputeTextureParam(shader, kernel, ShaderIDs.u_feedbackCandidatesScratch, _feedbackCandidatesScratch);
            cmd.SetComputeTextureParam(shader, kernel, ShaderIDs.u_feedbackTotalWeightBlended, _feedbackTotalWeightBlended);
            cmd.SetComputeTextureParam(shader, kernel, ShaderIDs.u_feedbackCandidatesBlended, _feedbackCandidatesBlended);
            cmd.SetComputeTextureParam(shader, kernel, ShaderIDs.u_historyDepth, _historyDepth);
            cmd.SetComputeTextureParam(shader, kernel, ShaderIDs.t_envRadianceAndImportanceMap, _environmentRadiance);
            cmd.SetComputeTextureParam(shader, kernel, ShaderIDs.t_depthBuffer, _depth);
            cmd.SetComputeTextureParam(shader, kernel, ShaderIDs.t_motionVectors, _motionVectors);
            cmd.DispatchCompute(shader, kernel, groupsX, groupsY, 1);
        }

        private static class ShaderIDs
        {
            public static readonly int u_controlBuffer = Shader.PropertyToID("u_controlBuffer");
            public static readonly int u_lightsBuffer = Shader.PropertyToID("u_lightsBuffer");
            public static readonly int u_lightsExBuffer = Shader.PropertyToID("u_lightsExBuffer");
            public static readonly int u_scratchBuffer = Shader.PropertyToID("u_scratchBuffer");
            public static readonly int u_scratchList = Shader.PropertyToID("u_scratchList");
            public static readonly int u_lightWeights = Shader.PropertyToID("u_lightWeights");
            public static readonly int u_historyRemapCurrentToPast = Shader.PropertyToID("u_historyRemapCurrentToPast");
            public static readonly int u_historyRemapPastToCurrent = Shader.PropertyToID("u_historyRemapPastToCurrent");
            public static readonly int u_perLightProxyCounters = Shader.PropertyToID("u_perLightProxyCounters");
            public static readonly int u_lightSamplingProxies = Shader.PropertyToID("u_lightSamplingProxies");
            public static readonly int u_envLightLookupMap = Shader.PropertyToID("u_envLightLookupMap");
            public static readonly int u_feedbackTotalWeight = Shader.PropertyToID("u_feedbackTotalWeight");
            public static readonly int u_feedbackCandidates = Shader.PropertyToID("u_feedbackCandidates");
            public static readonly int u_feedbackTotalWeightScratch = Shader.PropertyToID("u_feedbackTotalWeightScratch");
            public static readonly int u_feedbackCandidatesScratch = Shader.PropertyToID("u_feedbackCandidatesScratch");
            public static readonly int u_feedbackTotalWeightBlended = Shader.PropertyToID("u_feedbackTotalWeightBlended");
            public static readonly int u_feedbackCandidatesBlended = Shader.PropertyToID("u_feedbackCandidatesBlended");
            public static readonly int u_historyDepth = Shader.PropertyToID("u_historyDepth");
            public static readonly int u_localSamplingBuffer = Shader.PropertyToID("u_localSamplingBuffer");
            public static readonly int t_controlBufferReadOnly = Shader.PropertyToID("t_controlBufferReadOnly");
            public static readonly int t_lightSamplingProxiesReadOnly = Shader.PropertyToID("t_lightSamplingProxiesReadOnly");
            public static readonly int t_localSamplingBufferReadOnly = Shader.PropertyToID("t_localSamplingBufferReadOnly");
            public static readonly int t_depthBuffer = Shader.PropertyToID("t_depthBuffer");
            public static readonly int t_motionVectors = Shader.PropertyToID("t_motionVectors");
            public static readonly int t_envRadianceAndImportanceMap = Shader.PropertyToID("t_envRadianceAndImportanceMap");
        }
    }
}
