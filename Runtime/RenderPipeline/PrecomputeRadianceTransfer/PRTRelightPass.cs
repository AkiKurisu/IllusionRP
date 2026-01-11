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

        private ComputeBuffer _validityMaskBuffer;

        private const int BrickRadianceStride = 28; // float3 * 2 + float = 28 bytes

        private readonly ProfilingSampler _relightBrickSampler = new("Relight Brick");

        private readonly ProfilingSampler _relightProbeSampler = new("Relight Probe");

        private static int[] _coefficientClearValue;

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

            _coefficientClearValue = new int[27];
            for (int i = 0; i < 27; i++)
            {
                _coefficientClearValue[i] = 0;
            }
        }

        private class ReflectionNormalizationPassData
        {
            internal IllusionRendererData RendererData;
            internal ComputeBuffer ReflectionProbeComputeBuffer;
            internal ReflectionProbeData[] ReflectionProbeData;
            internal NativeArray<VisibleReflectionProbe> VisibleReflectionProbes;
            internal bool HasReflectionNormalization;
            internal Vector4 ReflectionNormalizationFactor;
        }

        private class PRTRelightPassData
        {
            internal IllusionRendererData RendererData;
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
            internal ComputeBuffer ValidityMaskBuffer;
            internal TextureHandle CoefficientVoxel3D;
            internal TextureHandle ValidityVoxel3D;
            internal PRTProbe[] ProbesToUpdate;  // Changed to array to avoid ListPool recycling
            internal int[] BrickIndicesToUpdate;  // Changed to array to avoid ListPool recycling
            internal Vector4 VoxelCorner;
            internal Vector4 VoxelSize;
            internal Vector4 BoundingBoxMin;
            internal Vector4 BoundingBoxSize;
            internal Vector4 OriginalBoundingBoxMin;
            internal float VoxelGridSize;
            internal bool EnableRelightShadow;
#if UNITY_EDITOR
            internal ProbeVolumeDebugMode DebugMode;
            internal int[] CoefficientClearValue;
#endif
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resource = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            var renderingData = frameData.Get<UniversalRenderingData>();
            if (cameraData.cameraType is CameraType.Reflection or CameraType.Preview) return;
#if UNITY_EDITOR
            if (PRTVolumeManager.IsBaking) return;
#endif

            // Reflection Normalization Pass
            RecordReflectionNormalizationPass(renderGraph, renderingData);

            PRTProbeVolume volume = PRTVolumeManager.ProbeVolume;
            bool enableRelight = _rendererData.SampleProbeVolumes;
            enableRelight &= _rendererData.IsLightingActive;

            if (enableRelight)
            {
                if (_volume != volume)
                {
                    ReleaseVolumeBuffer();
                }
                _volume = volume;

                // Get probes that need to be updated
                using (ListPool<PRTProbe>.Get(out var probesToUpdate))
                {
                    // Ensure bounding box update before upload gpu
                    volume.GetProbesToUpdate(probesToUpdate);

                    if (probesToUpdate.Count > 0)
                    {
                        RecordPRTRelightPass(renderGraph, volume, probesToUpdate);
                    }
                }

                volume.AdvanceRenderFrame();
            }
            else
            {
                // Mark voxel invalid using a low-level pass
                using (var builder = renderGraph.AddUnsafePass<EmptyPassData>("PRT Mark Voxel Invalid", 
                    out var passData, new ProfilingSampler("PRT Mark Voxel Invalid")))
                {
                    builder.AllowPassCulling(false);
                    builder.AllowGlobalStateModification(true);

                    builder.SetRenderFunc(static (EmptyPassData data, UnsafeGraphContext context) =>
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
                passData.RendererData = _rendererData;
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

        private void RecordPRTRelightPass(RenderGraph renderGraph, PRTProbeVolume volume, List<PRTProbe> probesToUpdate)
        {
            // Prepare pass data
            Vector3 corner = volume.GetVoxelMinCorner();
            Vector4 voxelCorner = new Vector4(corner.x, corner.y, corner.z, 0);
            Vector4 voxelSize = new Vector4(volume.probeSizeX, volume.probeSizeY, volume.probeSizeZ, 0);
            Vector4 boundingBoxMin = new Vector4(volume.BoundingBoxMin.x, volume.BoundingBoxMin.y, volume.BoundingBoxMin.z, 0);
            Vector4 boundingBoxSize = new Vector4(volume.CurrentVoxelGrid.X, volume.CurrentVoxelGrid.Y, volume.CurrentVoxelGrid.Z, 0);
            Vector4 originalBoundingBoxMin = new Vector4(volume.OriginalBoxMin.x, volume.OriginalBoxMin.y, volume.OriginalBoxMin.z, 0);

            // Get bricks that need to be updated
            using (ListPool<int>.Get(out var brickIndicesToUpdate))
            {
                volume.GetBricksToUpdate(probesToUpdate, brickIndicesToUpdate);

                // Initialize buffers
                InitializeFactorBuffer(volume);
                InitializeValidityMaskBuffer(volume);
                InitializeBrickBuffer(volume, brickIndicesToUpdate);

                // Import textures
                TextureHandle coefficientVoxel3D = renderGraph.ImportTexture(RTHandles.Alloc(volume.CoefficientVoxel3D));
                TextureHandle validityVoxel3D = renderGraph.ImportTexture(RTHandles.Alloc(volume.ValidityVoxel3D));

                // Relight Bricks Pass
                using (var builder = renderGraph.AddComputePass<PRTRelightPassData>("PRT Relight Bricks", 
                    out var passData, _relightBrickSampler))
                {
                    SetupBrickRelightPassData(passData, volume, brickIndicesToUpdate, coefficientVoxel3D, validityVoxel3D,
                        voxelCorner, voxelSize, boundingBoxMin, boundingBoxSize, originalBoundingBoxMin);

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
                        context.cmd.SetComputeIntParam(data.BrickRelightCS, ShaderProperties._brickCount, data.BrickIndicesToUpdate.Length);

                        int threadGroups = (data.BrickIndicesToUpdate.Length + 63) / 64;
                        context.cmd.DispatchCompute(data.BrickRelightCS, data.BrickRelightKernel, threadGroups, 1, 1);
                    });
                }

                // Relight Probes Pass
                using (var builder = renderGraph.AddComputePass<PRTRelightPassData>("PRT Relight Probes", 
                    out var probePassData, _relightProbeSampler))
                {
                    SetupProbeRelightPassData(probePassData, volume, probesToUpdate, brickIndicesToUpdate, 
                        coefficientVoxel3D, validityVoxel3D, voxelCorner, voxelSize, boundingBoxMin, boundingBoxSize, originalBoundingBoxMin);

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

        private void SetupBrickRelightPassData(PRTRelightPassData passData, PRTProbeVolume volume, 
            List<int> brickIndicesToUpdate, TextureHandle coefficientVoxel3D, TextureHandle validityVoxel3D,
            Vector4 voxelCorner, Vector4 voxelSize, Vector4 boundingBoxMin, Vector4 boundingBoxSize, Vector4 originalBoundingBoxMin)
        {
            passData.RendererData = _rendererData;
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
            passData.ValidityMaskBuffer = _validityMaskBuffer;
            passData.BrickIndicesToUpdate = brickIndicesToUpdate.ToArray();  // Copy to avoid ListPool recycling
            passData.VoxelCorner = voxelCorner;
            passData.VoxelSize = voxelSize;
            passData.BoundingBoxMin = boundingBoxMin;
            passData.BoundingBoxSize = boundingBoxSize;
            passData.OriginalBoundingBoxMin = originalBoundingBoxMin;
            passData.VoxelGridSize = volume.probeGridSize;
            passData.EnableRelightShadow = volume.enableRelightShadow;
#if UNITY_EDITOR
            passData.DebugMode = volume.debugMode;
            passData.CoefficientClearValue = _coefficientClearValue;
#endif
        }

        private void SetupProbeRelightPassData(PRTRelightPassData passData, PRTProbeVolume volume, 
            List<PRTProbe> probesToUpdate, List<int> brickIndicesToUpdate, TextureHandle coefficientVoxel3D, TextureHandle validityVoxel3D,
            Vector4 voxelCorner, Vector4 voxelSize, Vector4 boundingBoxMin, Vector4 boundingBoxSize, Vector4 originalBoundingBoxMin)
        {
            passData.RendererData = _rendererData;
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
            passData.ValidityMaskBuffer = _validityMaskBuffer;
            passData.ProbesToUpdate = probesToUpdate.ToArray();  // Copy to avoid ListPool recycling
            passData.BrickIndicesToUpdate = brickIndicesToUpdate.ToArray();  // Copy to avoid ListPool recycling
            passData.VoxelCorner = voxelCorner;
            passData.VoxelSize = voxelSize;
            passData.BoundingBoxMin = boundingBoxMin;
            passData.BoundingBoxSize = boundingBoxSize;
            passData.OriginalBoundingBoxMin = originalBoundingBoxMin;
            passData.VoxelGridSize = volume.probeGridSize;
            passData.EnableRelightShadow = volume.enableRelightShadow;
#if UNITY_EDITOR
            passData.DebugMode = volume.debugMode;
            passData.CoefficientClearValue = _coefficientClearValue;
#endif
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
            if (_shadowCacheBuffer == null || _shadowCacheBuffer.count < totalSurfelCount)
            {
                _shadowCacheBuffer?.Release();
                _shadowCacheBuffer = new ComputeBuffer(totalSurfelCount, sizeof(float));

                // Initialize shadow cache with default value of 1.0 (no shadow)
                var defaultShadowValues = new NativeArray<float>(totalSurfelCount, Allocator.Temp);
                for (int i = 0; i < totalSurfelCount; i++)
                {
                    defaultShadowValues[i] = 1.0f;
                }
                _shadowCacheBuffer.SetData(defaultShadowValues);
                defaultShadowValues.Dispose();
            }

            _surfelIndicesBuffer.SetData(bricksToUpdate);
            _brickIndexMappingBuffer.SetData(brickIndexMapping);

            bricksToUpdate.Dispose();
            brickIndexMapping.Dispose();
        }

        private void ReleaseVolumeBuffer()
        {
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
            _validityMaskBuffer?.Release();
            _validityMaskBuffer = null;
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
