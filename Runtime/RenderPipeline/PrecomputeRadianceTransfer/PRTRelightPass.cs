using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering.PRTGI
{
    public class PRTRelightPass : ScriptableRenderPass, IDisposable
    {
        private PRTProbeVolume _volume;

        private readonly ComputeShader _brickRelightCS;

        private readonly ComputeShader _probeRelightCS;

        private readonly int _brickRelightKernel;

        private readonly int _probeRelightKernel;

        private ComputeBuffer _brickRadianceBuffer;

        private ComputeBuffer _brickIndexMappingBuffer;

        private ComputeBuffer _surfelIndicesBuffer;

        private ComputeBuffer _factorBuffer;

        private ComputeBuffer _shadowCacheBuffer;

        private ComputeBuffer _brickShadowCacheBuffer;

        private ComputeBuffer _shadowCacheStatsBuffer;

#if UNITY_EDITOR
        private ComputeBuffer _shadowCacheDebugBuffer;
#endif

        private ComputeBuffer _validityMaskBuffer;

        private const int BrickRadianceStride = 28; // float3 * 2 + float = 28 bytes

        private const int ShadowCacheEntryStride = 16; // float + uint * 3

        private const int BrickShadowCacheStride = 16; // float * 2 + uint * 2

        private const int ShadowCacheStatsCount = 8;

#if UNITY_EDITOR
        private const int ShadowCacheDebugEntryStride = 16; // float + uint * 3
#endif

        private readonly uint[] _shadowCacheStatsClearValues = new uint[ShadowCacheStatsCount];

        private uint _shadowCacheEpoch;

        private int _lastShadowCacheStateHash;

        private bool _hasShadowCacheState;

        private readonly ProfilingSampler _relightBrickSampler = new("Relight Brick");

        private readonly ProfilingSampler _relightProbeSampler = new("Relight Probe");

        private readonly ProfilingSampler _shadowCacheStatsSampler = new("PRT Shadow Cache Stats Readback");

        private readonly ProfilingSampler _shadowCacheGlobalStatsSampler =
            new("PRT Shadow Cache Global Stats Readback");

#if UNITY_EDITOR
        private readonly ProfilingSampler _shadowCacheDebugSampler = new("PRT Shadow Cache Debug Readback");
#endif

        private struct ShadowCacheEntry
        {
            public float shadow;

            public uint epoch;

            public uint lastUpdateFrame;

            public uint valid;
        }

        private struct BrickShadowCache
        {
            public float meanShadow;

            public float variance;

            public uint epoch;

            public uint lastUpdateFrame;
        }

#if UNITY_EDITOR
        private struct ShadowCacheDebugEntry
        {
            public float shadow;

            public uint status;

            public uint age;

            public uint epoch;
        }
#endif

        private struct ReflectionProbeData
        {
            public Vector4 L0L1;

            public Vector4 L2_1; // First 4 coeffs of L2 {-2, -1, 0, 1}

            public float L2_2;   // Last L2 coeff {2}

            // Whether the probe is normalized by probe volume content.
            public int normalizeWithProbeVolume;

            public Vector2 padding;
        }

        private readonly ReflectionProbeData[] _reflectionProbeData;

        private ComputeBuffer _reflectionProbeComputeBuffer;

        private readonly IllusionRendererData _rendererData;

        public PRTRelightPass(IllusionRendererData rendererData)
        {
            _rendererData = rendererData;
            _brickRelightCS = rendererData.RuntimeResources.prtBrickRelightCS;
            _probeRelightCS = rendererData.RuntimeResources.prtProbeRelightCS;

            _brickRelightKernel = _brickRelightCS.FindKernel("CSMain");
            _probeRelightKernel = _probeRelightCS.FindKernel("CSMain");

            profilingSampler = new ProfilingSampler("PRT Relight");
            renderPassEvent = IllusionRenderPassEvent.PrecomputedRadianceTransferRelightPass;

            _reflectionProbeData = new ReflectionProbeData[UniversalRenderPipeline.maxVisibleReflectionProbes];
            _reflectionProbeComputeBuffer = new ComputeBuffer(_reflectionProbeData.Length, 48);
        }

        private class ReflectionNormalizationPassData
        {
            internal ComputeBuffer ReflectionProbeComputeBuffer;
            internal ReflectionProbeData[] ReflectionProbeData;
            internal NativeArray<VisibleReflectionProbe> VisibleReflectionProbes;
            internal bool HasReflectionNormalization;
            internal Vector4 ReflectionNormalizationFactor;
        }

        private class PRTRelightPassData
        {
            internal PRTProbeVolume Volume;
            internal ComputeShader BrickRelightCS;
            internal ComputeShader ProbeRelightCS;
            internal int BrickRelightKernel;
            internal int ProbeRelightKernel;
            internal ComputeBuffer BrickRadianceBuffer;
            internal ComputeBuffer BrickIndexMappingBuffer;
            internal ComputeBuffer SurfelIndicesBuffer;
            internal ComputeBuffer FactorBuffer;
            internal ComputeBuffer ShadowCacheBuffer;
            internal ComputeBuffer BrickShadowCacheBuffer;
            internal ComputeBuffer ShadowCacheStatsBuffer;
            internal ComputeBuffer ValidityMaskBuffer;
            internal TextureHandle CoefficientVoxel3D;
            internal TextureHandle ValidityVoxel3D;
            internal PRTProbe[] ProbesToUpdate;  // Changed to array to avoid ListPool recycling
            internal int[] BrickIndicesToUpdate;  // Changed to array to avoid ListPool recycling
            internal uint[] ShadowCacheStatsClearValues;
            internal Vector4 VoxelCorner;
            internal Vector4 VoxelSize;
            internal Vector4 BoundingBoxMin;
            internal Vector4 BoundingBoxSize;
            internal Vector4 OriginalBoundingBoxMin;
            internal float VoxelGridSize;
            internal uint ShadowCacheEpoch;
            internal uint ShadowCacheFrameIndex;
            internal int ShadowCacheMaxAge;
            internal float ShadowCacheVarianceThreshold;
            internal bool EnableRelightShadow;
            internal bool EnableShadowCacheStats;
#if UNITY_EDITOR
            internal ProbeVolumeDebugMode DebugMode;
            internal int[] CoefficientClearValue;
            internal bool EnableShadowCacheDebug;
            internal ComputeBuffer ShadowCacheDebugBuffer;
#endif
        }

        private class ShadowCacheStatsReadbackPassData
        {
            internal PRTProbeVolume Volume;
            internal ComputeBuffer ShadowCacheStatsBuffer;
        }

        private class ShadowCacheGlobalStatsReadbackPassData
        {
            internal PRTProbeVolume Volume;
            internal ComputeBuffer ShadowCacheBuffer;
            internal ComputeBuffer BrickShadowCacheBuffer;
        }

#if UNITY_EDITOR
        private class ShadowCacheDebugReadbackPassData
        {
            internal PRTProbeVolume Volume;
            internal ComputeBuffer ShadowCacheDebugBuffer;
            internal ComputeBuffer BrickShadowCacheBuffer;
        }
#endif

        private bool EnableRelight(UniversalCameraData cameraData)
        {
            if (cameraData.cameraType is CameraType.Reflection or CameraType.Preview) return false;
            
#if UNITY_EDITOR
            if (PRTVolumeManager.IsBaking) return false;
#endif
            
            bool enableRelight = _rendererData.SampleProbeVolumes;
            enableRelight &= _rendererData.IsLightingActive;
            return enableRelight;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var cameraData = frameData.Get<UniversalCameraData>();
            var renderingData = frameData.Get<UniversalRenderingData>();
            var lightData = frameData.Get<UniversalLightData>();
            var shadowData = frameData.Get<UniversalShadowData>();
           
            RecordReflectionNormalizationPass(renderGraph, renderingData);

            if (EnableRelight(cameraData))
            {
                PRTProbeVolume volume = PRTVolumeManager.ProbeVolume;
                if (_volume != volume)
                {
                    ReleaseVolumeBuffer();
                }
                _volume = volume;

                // Get probes that need to be updated
                using (ListPool<PRTProbe>.Get(out var probesToUpdate))
                {
                    volume.PrepareRelightCycle(ComputeRelightInputStateHash(volume, lightData, shadowData));

                    // Ensure bounding box update before upload gpu
                    volume.GetProbesToUpdate(probesToUpdate);

                    if (probesToUpdate.Count > 0)
                    {
                        RecordPRTRelightPass(renderGraph, volume, probesToUpdate, lightData, shadowData);
                    }
                }

                volume.AdvanceRenderFrame();
            }
            else
            {
                // Mark voxel invalid using a low-level pass
                using (var builder = renderGraph.AddUnsafePass<EmptyPassData>("PRT Mark Voxel Invalid", 
                    out _, new ProfilingSampler("PRT Mark Voxel Invalid")))
                {
                    builder.AllowPassCulling(false);
                    builder.AllowGlobalStateModification(true);

                    builder.SetRenderFunc(static (EmptyPassData _, UnsafeGraphContext context) =>
                    {
                        context.cmd.SetGlobalFloat(ShaderProperties._coefficientVoxelGridSize, 0);
                    });
                }
            }
        }

        private class EmptyPassData { }

        private void RecordReflectionNormalizationPass(RenderGraph renderGraph, UniversalRenderingData renderingData)
        {
            using (var builder = renderGraph.AddUnsafePass<ReflectionNormalizationPassData>("PRT Reflection Normalization", 
                out var passData, new ProfilingSampler("PRT Reflection Normalization")))
            {
                passData.ReflectionProbeComputeBuffer = _reflectionProbeComputeBuffer;
                passData.ReflectionProbeData = _reflectionProbeData;
                passData.VisibleReflectionProbes = renderingData.cullResults.visibleReflectionProbes;

                // Check if reflection normalization is active
                var reflectionNormalization = VolumeManager.instance.stack.GetComponent<ReflectionNormalization>();
                passData.HasReflectionNormalization = reflectionNormalization != null && reflectionNormalization.IsActive();
                if (passData.HasReflectionNormalization)
                {
                    passData.ReflectionNormalizationFactor = new Vector4(
                        reflectionNormalization.minNormalizationFactor.value,
                        reflectionNormalization.minNormalizationFactor.value,
                        0, reflectionNormalization.probeVolumeWeight.value);
                }
                else
                {
                    passData.ReflectionNormalizationFactor = Vector4.zero;
                }

                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc(static (ReflectionNormalizationPassData data, UnsafeGraphContext context) =>
                {
                    var probes = data.VisibleReflectionProbes;
                    for (int i = 0; i < probes.Length; i++)
                    {
                        if (!PRTVolumeManager.TryGetReflectionProbeAdditionalData(probes[i].reflectionProbe, out var additionalData))
                        {
                            data.ReflectionProbeData[i].normalizeWithProbeVolume = 0;
                            continue;
                        }

                        if (!additionalData.TryGetSHForNormalization(out var outL0L1, out var outL21, out var outL22))
                        {
                            data.ReflectionProbeData[i].normalizeWithProbeVolume = 0;
                            continue;
                        }

                        data.ReflectionProbeData[i].L0L1 = outL0L1;
                        data.ReflectionProbeData[i].L2_1 = outL21;
                        data.ReflectionProbeData[i].L2_2 = outL22;
                        data.ReflectionProbeData[i].normalizeWithProbeVolume = 1;
                    }

                    data.ReflectionProbeComputeBuffer.SetData(data.ReflectionProbeData);
                    context.cmd.SetGlobalBuffer(ShaderProperties._reflectionProbeNormalizationData, data.ReflectionProbeComputeBuffer);
                    context.cmd.SetGlobalVector(ShaderProperties._reflectionProbeNormalizationFactor, data.ReflectionNormalizationFactor);
                });
            }
        }

        private void RecordPRTRelightPass(RenderGraph renderGraph, PRTProbeVolume volume, List<PRTProbe> probesToUpdate,
            UniversalLightData lightData, UniversalShadowData shadowData)
        {
            // Prepare pass data
            Vector3 corner = volume.GetVoxelMinCorner();
            Vector4 voxelCorner = new Vector4(corner.x, corner.y, corner.z, 0);
            Vector4 voxelSize = new Vector4(volume.probeSizeX, volume.probeSizeY, volume.probeSizeZ, 0);
            Vector4 boundingBoxMin = new Vector4(volume.BoundingBoxMin.x, volume.BoundingBoxMin.y, volume.BoundingBoxMin.z, 0);
            Vector4 boundingBoxSize = new Vector4(volume.CurrentVoxelGrid.X, volume.CurrentVoxelGrid.Y, volume.CurrentVoxelGrid.Z, 0);
            Vector4 originalBoundingBoxMin = new Vector4(volume.OriginalBoxMin.x, volume.OriginalBoxMin.y, volume.OriginalBoxMin.z, 0);
            uint shadowCacheEpoch = UpdateShadowCacheEpoch(volume, lightData, shadowData);

            // Get bricks that need to be updated
            using (ListPool<int>.Get(out var brickIndicesToUpdate))
            {
                volume.GetBricksToUpdate(probesToUpdate, brickIndicesToUpdate);

                // Initialize buffers
                InitializeFactorBuffer(volume);
                InitializeValidityMaskBuffer(volume);
                InitializeBrickBuffer(volume, brickIndicesToUpdate);
                InitializeShadowCacheStatsBuffer();

                // Import textures
                TextureHandle coefficientVoxel3D = renderGraph.ImportTexture(RTHandles.Alloc(volume.CoefficientVoxel3D));
                TextureHandle validityVoxel3D = renderGraph.ImportTexture(RTHandles.Alloc(volume.ValidityVoxel3D));

                // Relight Bricks Pass
                using (var builder = renderGraph.AddComputePass<PRTRelightPassData>("PRT Relight Bricks", 
                    out var passData, _relightBrickSampler))
                {
                    SetupBrickRelightPassData(passData, volume, brickIndicesToUpdate,
                        voxelCorner, voxelSize, boundingBoxMin, boundingBoxSize, originalBoundingBoxMin,
                        shadowCacheEpoch);

                    builder.UseTexture(coefficientVoxel3D);
                    passData.CoefficientVoxel3D = coefficientVoxel3D;
                    builder.UseTexture(validityVoxel3D);
                    passData.ValidityVoxel3D = validityVoxel3D;

                    builder.AllowPassCulling(false);
                    builder.AllowGlobalStateModification(true);

                    builder.SetRenderFunc(static (PRTRelightPassData data, ComputeGraphContext context) =>
                    {
                        // Set global variables
                        context.cmd.SetGlobalFloat(ShaderProperties._coefficientVoxelGridSize, data.VoxelGridSize);
                        context.cmd.SetGlobalVector(ShaderProperties._coefficientVoxelSize, data.VoxelSize);
                        context.cmd.SetGlobalVector(ShaderProperties._coefficientVoxelCorner, data.VoxelCorner);
                        context.cmd.SetGlobalVector(ShaderProperties._boundingBoxMin, data.BoundingBoxMin);
                        context.cmd.SetGlobalVector(ShaderProperties._boundingBoxSize, data.BoundingBoxSize);
                        context.cmd.SetGlobalVector(ShaderProperties._originalBoundingBoxMin, data.OriginalBoundingBoxMin);
                        context.cmd.SetGlobalTexture(ShaderProperties._coefficientVoxel3D, data.CoefficientVoxel3D);
                        context.cmd.SetGlobalTexture(ShaderProperties._validityVoxel3D, data.ValidityVoxel3D);

#if UNITY_EDITOR
                        if (data.DebugMode == ProbeVolumeDebugMode.ProbeRadiance)
                        {
                            CoreUtils.SetKeyword(context.cmd, ShaderKeywords._RELIGHT_DEBUG_RADIANCE, true);
                        }
                        else
#endif
                        {
                            CoreUtils.SetKeyword(context.cmd, ShaderKeywords._RELIGHT_DEBUG_RADIANCE, false);
                        }

                        // Set shadow keyword
                        CoreUtils.SetKeyword(context.cmd, ShaderKeywords._PRT_RELIGHT_SHADOW, data.EnableRelightShadow);
#if UNITY_EDITOR
                        CoreUtils.SetKeyword(context.cmd, ShaderKeywords._PRT_SHADOW_CACHE_DEBUG_VIEW,
                            data.EnableShadowCacheDebug);
#endif

                        // Relight bricks
                        context.cmd.SetComputeBufferParam(data.BrickRelightCS, data.BrickRelightKernel,
                            ShaderProperties._surfels, data.Volume.GlobalSurfelBuffer);
                        context.cmd.SetComputeBufferParam(data.BrickRelightCS, data.BrickRelightKernel,
                            ShaderProperties._brickRadiance, data.BrickRadianceBuffer);
                        context.cmd.SetComputeBufferParam(data.BrickRelightCS, data.BrickRelightKernel,
                            ShaderProperties._brickInfo, data.SurfelIndicesBuffer);
                        context.cmd.SetComputeBufferParam(data.BrickRelightCS, data.BrickRelightKernel,
                            ShaderProperties._brickIndexMapping, data.BrickIndexMappingBuffer);
                        context.cmd.SetComputeBufferParam(data.BrickRelightCS, data.BrickRelightKernel,
                            ShaderProperties._shadowCache, data.ShadowCacheBuffer);
                        context.cmd.SetComputeBufferParam(data.BrickRelightCS, data.BrickRelightKernel,
                            ShaderProperties._brickShadowCache, data.BrickShadowCacheBuffer);
                        context.cmd.SetComputeBufferParam(data.BrickRelightCS, data.BrickRelightKernel,
                            ShaderProperties._shadowCacheStats, data.ShadowCacheStatsBuffer);
#if UNITY_EDITOR
                        if (data.EnableShadowCacheDebug)
                        {
                            context.cmd.SetComputeBufferParam(data.BrickRelightCS, data.BrickRelightKernel,
                                ShaderProperties._shadowCacheDebug, data.ShadowCacheDebugBuffer);
                        }
#endif
                        context.cmd.SetComputeIntParam(data.BrickRelightCS, ShaderProperties._brickCount, data.BrickIndicesToUpdate.Length);
                        context.cmd.SetComputeIntParam(data.BrickRelightCS, ShaderProperties._shadowCacheEpoch, (int)data.ShadowCacheEpoch);
                        context.cmd.SetComputeIntParam(data.BrickRelightCS, ShaderProperties._shadowCacheFrameIndex, (int)data.ShadowCacheFrameIndex);
                        context.cmd.SetComputeIntParam(data.BrickRelightCS, ShaderProperties._shadowCacheMaxAge, data.ShadowCacheMaxAge);
                        context.cmd.SetComputeFloatParam(data.BrickRelightCS, ShaderProperties._shadowCacheVarianceThreshold, data.ShadowCacheVarianceThreshold);
                        context.cmd.SetComputeIntParam(data.BrickRelightCS, ShaderProperties._enableShadowCacheStats, data.EnableShadowCacheStats ? 1 : 0);

                        if (data.EnableShadowCacheStats)
                        {
                            context.cmd.SetBufferData(data.ShadowCacheStatsBuffer, data.ShadowCacheStatsClearValues);
                        }

                        int threadGroups = (data.BrickIndicesToUpdate.Length + 63) / 64;
                        context.cmd.DispatchCompute(data.BrickRelightCS, data.BrickRelightKernel, threadGroups, 1, 1);
                    });
                }

                if (volume.enableShadowCacheStats)
                {
                    RecordShadowCacheStatsReadbackPass(renderGraph, volume);
                    if (_shadowCacheBuffer != null &&
                        _brickShadowCacheBuffer != null &&
                        volume.ShouldRequestShadowCacheGlobalStatsReadback(_rendererData.FrameCount))
                    {
                        volume.BeginShadowCacheGlobalStatsReadback(
                            shadowCacheEpoch,
                            _rendererData.FrameCount,
                            Mathf.Max(1, volume.shadowCacheMaxAge),
                            Mathf.Max(0f, volume.shadowCacheVarianceThreshold));
                        RecordShadowCacheGlobalStatsReadbackPass(renderGraph, volume);
                    }
                }

#if UNITY_EDITOR
                if (volume.IsShadowCacheDebugActive &&
                    _shadowCacheDebugBuffer != null &&
                    _brickShadowCacheBuffer != null &&
                    volume.ShouldRequestShadowCacheDebugReadback(_rendererData.FrameCount))
                {
                    volume.BeginShadowCacheDebugReadback(shadowCacheEpoch, _rendererData.FrameCount);
                    RecordShadowCacheDebugReadbackPass(renderGraph, volume);
                }
#endif

                // Relight Probes Pass
                using (var builder = renderGraph.AddComputePass<PRTRelightPassData>("PRT Relight Probes",
                    out var probePassData, _relightProbeSampler))
                {
                    SetupProbeRelightPassData(probePassData, volume, probesToUpdate, brickIndicesToUpdate,
                        voxelCorner, voxelSize, boundingBoxMin, boundingBoxSize, originalBoundingBoxMin,
                        shadowCacheEpoch);

                    builder.UseTexture(coefficientVoxel3D);
                    probePassData.CoefficientVoxel3D = coefficientVoxel3D;
                    builder.UseTexture(validityVoxel3D);
                    probePassData.ValidityVoxel3D = validityVoxel3D;

                    builder.AllowPassCulling(false);
                    builder.AllowGlobalStateModification(true);

                    builder.SetRenderFunc(static (PRTRelightPassData data, ComputeGraphContext context) =>
                    {
                        var allProbes = data.Volume.GetAllProbes();
                        foreach (var probe in data.ProbesToUpdate)
                        {
                            var factorIndices = allProbes[probe.Index];
                            context.cmd.SetComputeVectorParam(data.ProbeRelightCS, ShaderProperties._probePos,
                                new Vector4(probe.Position.x, probe.Position.y, probe.Position.z, 1));
                            context.cmd.SetComputeIntParam(data.ProbeRelightCS, ShaderProperties._factorStart, factorIndices.start);
                            context.cmd.SetComputeIntParam(data.ProbeRelightCS, ShaderProperties._factorCount,
                                factorIndices.end - factorIndices.start + 1);
                            context.cmd.SetComputeIntParam(data.ProbeRelightCS, ShaderProperties._indexInProbeVolume, probe.Index);

                            context.cmd.SetComputeBufferParam(data.ProbeRelightCS, data.ProbeRelightKernel,
                                ShaderProperties._brickRadiance, data.BrickRadianceBuffer);
                            context.cmd.SetComputeBufferParam(data.ProbeRelightCS, data.ProbeRelightKernel,
                                ShaderProperties._factors, data.FactorBuffer);
                            context.cmd.SetComputeTextureParam(data.ProbeRelightCS, data.ProbeRelightKernel,
                                ShaderProperties._coefficientVoxel3D, data.CoefficientVoxel3D);
                            context.cmd.SetComputeTextureParam(data.ProbeRelightCS, data.ProbeRelightKernel,
                                ShaderProperties._validityVoxel3D, data.ValidityVoxel3D);
                            context.cmd.SetComputeBufferParam(data.ProbeRelightCS, data.ProbeRelightKernel,
                                ShaderProperties._validityMasks, data.ValidityMaskBuffer);

#if UNITY_EDITOR
                            // Debug data
                            if (data.DebugMode == ProbeVolumeDebugMode.ProbeRadiance)
                            {
                                var debugData = data.Volume.GetProbeDebugData(probe.Index);
                                context.cmd.SetBufferData(debugData.CoefficientSH9, data.CoefficientClearValue);
                                context.cmd.SetComputeBufferParam(data.ProbeRelightCS, data.ProbeRelightKernel,
                                    ShaderProperties._coefficientSH9, debugData.CoefficientSH9);
                            }
#endif
                            context.cmd.DispatchCompute(data.ProbeRelightCS, data.ProbeRelightKernel, 1, 1, 1);
                        }
                    });
                }
            }
        }

        private void RecordShadowCacheStatsReadbackPass(RenderGraph renderGraph, PRTProbeVolume volume)
        {
            using (var builder = renderGraph.AddUnsafePass<ShadowCacheStatsReadbackPassData>(
                       "PRT Shadow Cache Stats Readback", out var passData, _shadowCacheStatsSampler))
            {
                passData.Volume = volume;
                passData.ShadowCacheStatsBuffer = _shadowCacheStatsBuffer;

                builder.AllowPassCulling(false);

                builder.SetRenderFunc(static (ShadowCacheStatsReadbackPassData data, UnsafeGraphContext context) =>
                {
                    context.cmd.RequestAsyncReadback(data.ShadowCacheStatsBuffer, data.Volume.UpdateShadowCacheStats);
                });
            }
        }

        private void RecordShadowCacheGlobalStatsReadbackPass(RenderGraph renderGraph, PRTProbeVolume volume)
        {
            using (var builder = renderGraph.AddUnsafePass<ShadowCacheGlobalStatsReadbackPassData>(
                       "PRT Shadow Cache Global Stats Readback", out var passData, _shadowCacheGlobalStatsSampler))
            {
                passData.Volume = volume;
                passData.ShadowCacheBuffer = _shadowCacheBuffer;
                passData.BrickShadowCacheBuffer = _brickShadowCacheBuffer;
                builder.AllowPassCulling(false);

                builder.SetRenderFunc(static (ShadowCacheGlobalStatsReadbackPassData data,
                    UnsafeGraphContext context) =>
                {
                    context.cmd.RequestAsyncReadback(data.ShadowCacheBuffer,
                        data.Volume.UpdateShadowCacheGlobalStatsEntries);
                    context.cmd.RequestAsyncReadback(data.BrickShadowCacheBuffer,
                        data.Volume.UpdateBrickShadowCacheGlobalStatsEntries);
                });
            }
        }

#if UNITY_EDITOR
        private void RecordShadowCacheDebugReadbackPass(RenderGraph renderGraph, PRTProbeVolume volume)
        {
            using (var builder = renderGraph.AddUnsafePass<ShadowCacheDebugReadbackPassData>(
                       "PRT Shadow Cache Debug Readback", out var passData, _shadowCacheDebugSampler))
            {
                passData.Volume = volume;
                passData.ShadowCacheDebugBuffer = _shadowCacheDebugBuffer;
                passData.BrickShadowCacheBuffer = _brickShadowCacheBuffer;

                builder.AllowPassCulling(false);

                builder.SetRenderFunc(static (ShadowCacheDebugReadbackPassData data, UnsafeGraphContext context) =>
                {
                    context.cmd.RequestAsyncReadback(data.ShadowCacheDebugBuffer,
                        data.Volume.UpdateShadowCacheDebugEntries);
                    context.cmd.RequestAsyncReadback(data.BrickShadowCacheBuffer,
                        data.Volume.UpdateBrickShadowCacheDebugEntries);
                });
            }
        }
#endif

        private void SetupBrickRelightPassData(PRTRelightPassData passData, PRTProbeVolume volume, 
            List<int> brickIndicesToUpdate, Vector4 voxelCorner, Vector4 voxelSize, 
            Vector4 boundingBoxMin, Vector4 boundingBoxSize, Vector4 originalBoundingBoxMin,
            uint shadowCacheEpoch)
        {
            passData.Volume = volume;
            passData.BrickRelightCS = _brickRelightCS;
            passData.ProbeRelightCS = _probeRelightCS;
            passData.BrickRelightKernel = _brickRelightKernel;
            passData.ProbeRelightKernel = _probeRelightKernel;
            passData.BrickRadianceBuffer = _brickRadianceBuffer;
            passData.BrickIndexMappingBuffer = _brickIndexMappingBuffer;
            passData.SurfelIndicesBuffer = _surfelIndicesBuffer;
            passData.FactorBuffer = _factorBuffer;
            passData.ShadowCacheBuffer = _shadowCacheBuffer;
            passData.BrickShadowCacheBuffer = _brickShadowCacheBuffer;
            passData.ShadowCacheStatsBuffer = _shadowCacheStatsBuffer;
            passData.ValidityMaskBuffer = _validityMaskBuffer;
            passData.BrickIndicesToUpdate = brickIndicesToUpdate.ToArray();  // Copy to avoid ListPool recycling
            passData.ShadowCacheStatsClearValues = _shadowCacheStatsClearValues;
            passData.VoxelCorner = voxelCorner;
            passData.VoxelSize = voxelSize;
            passData.BoundingBoxMin = boundingBoxMin;
            passData.BoundingBoxSize = boundingBoxSize;
            passData.OriginalBoundingBoxMin = originalBoundingBoxMin;
            passData.VoxelGridSize = volume.probeGridSize;
            passData.ShadowCacheEpoch = shadowCacheEpoch;
            passData.ShadowCacheFrameIndex = _rendererData.FrameCount;
            passData.ShadowCacheMaxAge = Mathf.Max(1, volume.shadowCacheMaxAge);
            passData.ShadowCacheVarianceThreshold = Mathf.Max(0f, volume.shadowCacheVarianceThreshold);
            passData.EnableRelightShadow = volume.enableRelightShadow;
            passData.EnableShadowCacheStats = volume.enableShadowCacheStats;
#if UNITY_EDITOR
            passData.DebugMode = volume.debugMode;
            passData.CoefficientClearValue = PRTProbeDebugData.CoefficientClearValue;
            passData.EnableShadowCacheDebug = volume.IsShadowCacheDebugActive && _shadowCacheDebugBuffer != null;
            passData.ShadowCacheDebugBuffer = _shadowCacheDebugBuffer;
#endif
        }

        private void SetupProbeRelightPassData(PRTRelightPassData passData, PRTProbeVolume volume, 
            List<PRTProbe> probesToUpdate, List<int> brickIndicesToUpdate, Vector4 voxelCorner, Vector4 voxelSize, 
            Vector4 boundingBoxMin, Vector4 boundingBoxSize, Vector4 originalBoundingBoxMin,
            uint shadowCacheEpoch)
        {
            passData.Volume = volume;
            passData.BrickRelightCS = _brickRelightCS;
            passData.ProbeRelightCS = _probeRelightCS;
            passData.BrickRelightKernel = _brickRelightKernel;
            passData.ProbeRelightKernel = _probeRelightKernel;
            passData.BrickRadianceBuffer = _brickRadianceBuffer;
            passData.BrickIndexMappingBuffer = _brickIndexMappingBuffer;
            passData.SurfelIndicesBuffer = _surfelIndicesBuffer;
            passData.FactorBuffer = _factorBuffer;
            passData.ShadowCacheBuffer = _shadowCacheBuffer;
            passData.BrickShadowCacheBuffer = _brickShadowCacheBuffer;
            passData.ShadowCacheStatsBuffer = _shadowCacheStatsBuffer;
            passData.ValidityMaskBuffer = _validityMaskBuffer;
            passData.ProbesToUpdate = probesToUpdate.ToArray();  // Copy to avoid ListPool recycling
            passData.BrickIndicesToUpdate = brickIndicesToUpdate.ToArray();  // Copy to avoid ListPool recycling
            passData.ShadowCacheStatsClearValues = _shadowCacheStatsClearValues;
            passData.VoxelCorner = voxelCorner;
            passData.VoxelSize = voxelSize;
            passData.BoundingBoxMin = boundingBoxMin;
            passData.BoundingBoxSize = boundingBoxSize;
            passData.OriginalBoundingBoxMin = originalBoundingBoxMin;
            passData.VoxelGridSize = volume.probeGridSize;
            passData.ShadowCacheEpoch = shadowCacheEpoch;
            passData.ShadowCacheFrameIndex = _rendererData.FrameCount;
            passData.ShadowCacheMaxAge = Mathf.Max(1, volume.shadowCacheMaxAge);
            passData.ShadowCacheVarianceThreshold = Mathf.Max(0f, volume.shadowCacheVarianceThreshold);
            passData.EnableRelightShadow = volume.enableRelightShadow;
            passData.EnableShadowCacheStats = volume.enableShadowCacheStats;
#if UNITY_EDITOR
            passData.DebugMode = volume.debugMode;
            passData.CoefficientClearValue = PRTProbeDebugData.CoefficientClearValue;
            passData.EnableShadowCacheDebug = volume.IsShadowCacheDebugActive && _shadowCacheDebugBuffer != null;
            passData.ShadowCacheDebugBuffer = _shadowCacheDebugBuffer;
#endif
        }

        private uint UpdateShadowCacheEpoch(PRTProbeVolume volume, UniversalLightData lightData,
            UniversalShadowData shadowData)
        {
            int stateHash = ComputeShadowCacheStateHash(volume, lightData, shadowData);
            if (!_hasShadowCacheState || stateHash != _lastShadowCacheStateHash)
            {
                _shadowCacheEpoch = NextShadowCacheEpoch(_shadowCacheEpoch);
                _lastShadowCacheStateHash = stateHash;
                _hasShadowCacheState = true;
            }

            return _shadowCacheEpoch;
        }

        private int ComputeRelightInputStateHash(PRTProbeVolume volume, UniversalLightData lightData,
            UniversalShadowData shadowData)
        {
            var hash = new HashCode();
            hash.Add(ComputeShadowCacheStateHash(volume, lightData, shadowData));
            hash.Add(volume ? volume.relightOnceUntilDirty : false);

            hash.Add(RenderSettings.ambientMode);
            hash.Add(RenderSettings.ambientIntensity);
            AddColor(ref hash, RenderSettings.ambientLight);
            AddColor(ref hash, RenderSettings.ambientSkyColor);
            AddColor(ref hash, RenderSettings.ambientEquatorColor);
            AddColor(ref hash, RenderSettings.ambientGroundColor);
            AddAmbientProbe(ref hash, RenderSettings.ambientProbe);

            int mainLightIndex = lightData.mainLightIndex;
            if (mainLightIndex >= 0 && mainLightIndex < lightData.visibleLights.Length)
            {
                VisibleLight mainLight = lightData.visibleLights[mainLightIndex];
                Light light = mainLight.light;
                if (light)
                {
                    AddColor(ref hash, light.color);
                    hash.Add(light.intensity);
                    hash.Add(light.bounceIntensity);
                    hash.Add(light.colorTemperature);
                    hash.Add(light.useColorTemperature);
                    hash.Add(light.renderingLayerMask);
                }
            }

            return hash.ToHashCode();
        }

        private int ComputeShadowCacheStateHash(PRTProbeVolume volume, UniversalLightData lightData,
            UniversalShadowData shadowData)
        {
            var hash = new HashCode();
            hash.Add(volume ? volume.GetInstanceID() : 0);
            hash.Add(volume && volume.asset ? volume.asset.GetInstanceID() : 0);
            hash.Add(volume && volume.GlobalSurfelBuffer != null ? volume.GlobalSurfelBuffer.count : 0);
            hash.Add(volume && volume.GlobalSurfelBuffer != null ? volume.GlobalSurfelBuffer.GetHashCode() : 0);
            hash.Add(volume ? volume.enableRelightShadow : false);
            if (volume)
            {
                AddMatrix(ref hash, volume.transform.localToWorldMatrix);
            }

            hash.Add(shadowData.supportsMainLightShadows);
            hash.Add(shadowData.supportsSoftShadows);
            hash.Add(shadowData.mainLightShadowCascadesCount);
            hash.Add(shadowData.mainLightShadowmapWidth);
            hash.Add(shadowData.mainLightShadowmapHeight);
            AddVector(ref hash, shadowData.mainLightShadowCascadesSplit);
            hash.Add(shadowData.mainLightShadowCascadeBorder);

            int mainLightIndex = lightData.mainLightIndex;
            if (mainLightIndex >= 0 && mainLightIndex < lightData.visibleLights.Length)
            {
                VisibleLight mainLight = lightData.visibleLights[mainLightIndex];
                hash.Add(mainLight.lightType);
                hash.Add(mainLight.light ? mainLight.light.GetInstanceID() : 0);
                AddMatrix(ref hash, mainLight.localToWorldMatrix);

                Light light = mainLight.light;
                if (light)
                {
                    AddVector(ref hash, light.transform.forward);
                    hash.Add(light.shadows);
                    hash.Add(light.shadowStrength);
                    hash.Add(light.shadowBias);
                    hash.Add(light.shadowNormalBias);
                    hash.Add(light.shadowNearPlane);
                    hash.Add(light.shadowCustomResolution);
                    hash.Add(light.renderingLayerMask);
                }
            }

            return hash.ToHashCode();
        }

        private static uint NextShadowCacheEpoch(uint epoch)
        {
            return epoch == uint.MaxValue ? 1u : epoch + 1u;
        }

        private static void AddVector(ref HashCode hash, Vector3 value)
        {
            hash.Add(value.x);
            hash.Add(value.y);
            hash.Add(value.z);
        }

        private static void AddColor(ref HashCode hash, Color value)
        {
            hash.Add(value.r);
            hash.Add(value.g);
            hash.Add(value.b);
            hash.Add(value.a);
        }

        private static void AddAmbientProbe(ref HashCode hash, SphericalHarmonicsL2 value)
        {
            for (int channel = 0; channel < 3; channel++)
            {
                for (int coefficient = 0; coefficient < 9; coefficient++)
                {
                    hash.Add(value[channel, coefficient]);
                }
            }
        }

        private static void AddMatrix(ref HashCode hash, Matrix4x4 value)
        {
            for (int i = 0; i < 16; i++)
            {
                hash.Add(value[i]);
            }
        }
        
        private void InitializeFactorBuffer(PRTProbeVolume volume)
        {
            var factors = volume.GetAllFactors();

            if (_factorBuffer == null || _factorBuffer.count != factors.Length)
            {
                _factorBuffer?.Release();
                _factorBuffer = new ComputeBuffer(factors.Length, BrickFactor.Stride);
            }

            _factorBuffer.SetData(factors);
        }

        private void InitializeValidityMaskBuffer(PRTProbeVolume volume)
        {
            var validityMasks = volume.GetValidityMasks();
            int probeCount = volume.probeSizeX * volume.probeSizeY * volume.probeSizeZ;

            if (_validityMaskBuffer == null || _validityMaskBuffer.count != probeCount)
            {
                _validityMaskBuffer?.Release();
                _validityMaskBuffer = new ComputeBuffer(probeCount, 4);
            }
            
            _validityMaskBuffer.SetData(validityMasks);
        }

        private void InitializeBrickBuffer(PRTProbeVolume volume, List<int> brickIndicesToUpdate)
        {
            var allBricks = volume.GetAllBricks();
            int totalBrickCount = allBricks.Length;
            int updateCount = brickIndicesToUpdate.Count;

            // Create brick radiance buffer for ALL bricks
            if (_brickRadianceBuffer == null || _brickRadianceBuffer.count != totalBrickCount)
            {
                _brickRadianceBuffer?.Release();
                _brickRadianceBuffer = new ComputeBuffer(totalBrickCount, BrickRadianceStride);
            }

            if (_brickShadowCacheBuffer == null || _brickShadowCacheBuffer.count != totalBrickCount)
            {
                _brickShadowCacheBuffer?.Release();
                _brickShadowCacheBuffer = new ComputeBuffer(totalBrickCount, BrickShadowCacheStride);

                var defaultBrickShadowCache = new NativeArray<BrickShadowCache>(
                    totalBrickCount, Allocator.Temp, NativeArrayOptions.ClearMemory);
                _brickShadowCacheBuffer.SetData(defaultBrickShadowCache);
                defaultBrickShadowCache.Dispose();
            }

            // Create buffers for bricks to update
            var bricksToUpdate = new NativeArray<SurfelIndices>(updateCount, Allocator.Temp);
            var brickIndexMapping = new NativeArray<int>(updateCount, Allocator.Temp);

            for (int i = 0; i < updateCount; i++)
            {
                int brickIndex = brickIndicesToUpdate[i];
                if (brickIndex >= 0 && brickIndex < allBricks.Length)
                {
                    bricksToUpdate[i] = allBricks[brickIndex];
                    brickIndexMapping[i] = brickIndex; // Store the actual brick index
                }
            }

            // Resize surfel indices buffer if needed
            if (_surfelIndicesBuffer == null || _surfelIndicesBuffer.count < updateCount)
            {
                _surfelIndicesBuffer?.Release();
                _surfelIndicesBuffer = new ComputeBuffer(updateCount, SurfelIndices.Stride);
            }

            // Resize brick index mapping buffer if needed
            if (_brickIndexMappingBuffer == null || _brickIndexMappingBuffer.count < updateCount)
            {
                _brickIndexMappingBuffer?.Release();
                _brickIndexMappingBuffer = new ComputeBuffer(updateCount, sizeof(int));
            }

            // Initialize shadow cache buffer with total surfel count
            int totalSurfelCount = volume.GlobalSurfelBuffer.count;
            if (_shadowCacheBuffer == null || _shadowCacheBuffer.count != totalSurfelCount)
            {
                _shadowCacheBuffer?.Release();
                _shadowCacheBuffer = new ComputeBuffer(totalSurfelCount, ShadowCacheEntryStride);

                var defaultShadowValues = new NativeArray<ShadowCacheEntry>(
                    totalSurfelCount, Allocator.Temp, NativeArrayOptions.ClearMemory);
                _shadowCacheBuffer.SetData(defaultShadowValues);
                defaultShadowValues.Dispose();
            }

#if UNITY_EDITOR
            InitializeShadowCacheDebugBuffer(volume, totalSurfelCount);
#endif

            _surfelIndicesBuffer.SetData(bricksToUpdate);
            _brickIndexMappingBuffer.SetData(brickIndexMapping);

            bricksToUpdate.Dispose();
            brickIndexMapping.Dispose();
        }

        private void InitializeShadowCacheStatsBuffer()
        {
            if (_shadowCacheStatsBuffer == null || _shadowCacheStatsBuffer.count != ShadowCacheStatsCount)
            {
                _shadowCacheStatsBuffer?.Release();
                _shadowCacheStatsBuffer = new ComputeBuffer(ShadowCacheStatsCount, sizeof(uint));
                _shadowCacheStatsBuffer.SetData(_shadowCacheStatsClearValues);
            }
        }

#if UNITY_EDITOR
        private void InitializeShadowCacheDebugBuffer(PRTProbeVolume volume, int totalSurfelCount)
        {
            if (!volume.IsShadowCacheDebugActive)
            {
                ReleaseShadowCacheDebugBuffer();
                return;
            }

            if (_shadowCacheDebugBuffer == null || _shadowCacheDebugBuffer.count != totalSurfelCount)
            {
                _shadowCacheDebugBuffer?.Release();
                _shadowCacheDebugBuffer = new ComputeBuffer(totalSurfelCount, ShadowCacheDebugEntryStride);

                var defaultDebugEntries = new NativeArray<ShadowCacheDebugEntry>(
                    totalSurfelCount, Allocator.Temp, NativeArrayOptions.ClearMemory);
                for (int i = 0; i < defaultDebugEntries.Length; i++)
                {
                    defaultDebugEntries[i] = new ShadowCacheDebugEntry
                    {
                        shadow = 1f,
                        status = (uint)ShadowCacheDebugStatus.Unknown,
                        age = 0,
                        epoch = 0
                    };
                }

                _shadowCacheDebugBuffer.SetData(defaultDebugEntries);
                defaultDebugEntries.Dispose();
            }
        }

        private void ReleaseShadowCacheDebugBuffer()
        {
            _shadowCacheDebugBuffer?.Release();
            _shadowCacheDebugBuffer = null;
        }
#endif

        private void ReleaseVolumeBuffer()
        {
            _volume?.ClearShadowCacheGlobalStats();
            _brickRadianceBuffer?.Release();
            _brickRadianceBuffer = null;
            _brickIndexMappingBuffer?.Release();
            _brickIndexMappingBuffer = null;
            _surfelIndicesBuffer?.Release();
            _surfelIndicesBuffer = null;
            _factorBuffer?.Release();
            _factorBuffer = null;
            _shadowCacheBuffer?.Release();
            _shadowCacheBuffer = null;
            _brickShadowCacheBuffer?.Release();
            _brickShadowCacheBuffer = null;
            _shadowCacheStatsBuffer?.Release();
            _shadowCacheStatsBuffer = null;
#if UNITY_EDITOR
            ReleaseShadowCacheDebugBuffer();
#endif
            _validityMaskBuffer?.Release();
            _validityMaskBuffer = null;
            _shadowCacheEpoch = 0;
            _lastShadowCacheStateHash = 0;
            _hasShadowCacheState = false;
        }

        public void Dispose()
        {
            _reflectionProbeComputeBuffer?.Release();
            _reflectionProbeComputeBuffer = null;
            ReleaseVolumeBuffer();
        }

        private static class ShaderKeywords
        {
            public static readonly string _RELIGHT_DEBUG_RADIANCE = MemberNameHelpers.String();

            public static readonly string _PRT_RELIGHT_SHADOW = MemberNameHelpers.String();

            public static readonly string _PRT_SHADOW_CACHE_DEBUG_VIEW = MemberNameHelpers.String();
        }

        private static class ShaderProperties
        {
            public static readonly int _coefficientVoxelGridSize = MemberNameHelpers.ShaderPropertyID();

            public static readonly int _coefficientVoxelSize = MemberNameHelpers.ShaderPropertyID();

            public static readonly int _coefficientVoxelCorner = MemberNameHelpers.ShaderPropertyID();

            public static readonly int _coefficientVoxel3D = MemberNameHelpers.ShaderPropertyID();

            public static readonly int _validityVoxel3D = MemberNameHelpers.ShaderPropertyID();

            public static readonly int _indexInProbeVolume = MemberNameHelpers.ShaderPropertyID();

            public static readonly int _brickCount = MemberNameHelpers.ShaderPropertyID();

            public static readonly int _factorStart = MemberNameHelpers.ShaderPropertyID();

            public static readonly int _factorCount = MemberNameHelpers.ShaderPropertyID();

            public static readonly int _brickRadiance = MemberNameHelpers.ShaderPropertyID();

            public static readonly int _factors = MemberNameHelpers.ShaderPropertyID();

            public static readonly int _probePos = MemberNameHelpers.ShaderPropertyID();

            public static readonly int _surfels = MemberNameHelpers.ShaderPropertyID();

            public static readonly int _brickInfo = MemberNameHelpers.ShaderPropertyID();

            public static readonly int _brickIndexMapping = MemberNameHelpers.ShaderPropertyID();

            public static readonly int _shadowCache = MemberNameHelpers.ShaderPropertyID();

            public static readonly int _brickShadowCache = MemberNameHelpers.ShaderPropertyID();

            public static readonly int _shadowCacheStats = MemberNameHelpers.ShaderPropertyID();

            public static readonly int _shadowCacheDebug = MemberNameHelpers.ShaderPropertyID();

            public static readonly int _shadowCacheEpoch = MemberNameHelpers.ShaderPropertyID();

            public static readonly int _shadowCacheFrameIndex = MemberNameHelpers.ShaderPropertyID();

            public static readonly int _shadowCacheMaxAge = MemberNameHelpers.ShaderPropertyID();

            public static readonly int _shadowCacheVarianceThreshold = MemberNameHelpers.ShaderPropertyID();

            public static readonly int _enableShadowCacheStats = MemberNameHelpers.ShaderPropertyID();

            public static readonly int _coefficientSH9 = MemberNameHelpers.ShaderPropertyID();

            public static readonly int _boundingBoxMin = MemberNameHelpers.ShaderPropertyID();

            public static readonly int _boundingBoxSize = MemberNameHelpers.ShaderPropertyID();

            public static readonly int _originalBoundingBoxMin = MemberNameHelpers.ShaderPropertyID();

            public static readonly int _validityMasks = MemberNameHelpers.ShaderPropertyID();

            // Reflection normalization parameters
            public static readonly int _reflectionProbeNormalizationFactor = MemberNameHelpers.ShaderPropertyID();

            public static readonly int _reflectionProbeNormalizationData = MemberNameHelpers.ShaderPropertyID();
        }
    }
}
