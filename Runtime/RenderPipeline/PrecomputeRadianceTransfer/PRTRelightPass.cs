using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
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

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (renderingData.cameraData.cameraType is CameraType.Reflection or CameraType.Preview) return;
#if UNITY_EDITOR
            if (PRTVolumeManager.IsBaking) return;
#endif
            var cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, profilingSampler))
            {
                DoReflectionNormalization(cmd, ref renderingData);

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
                    DoRelight(cmd, volume);
                }
                else
                {
                    // Mark voxel invalid
                    cmd.SetGlobalFloat(ShaderProperties._coefficientVoxelGridSize, 0);
                }
            }
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        private void DoReflectionNormalization(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var probes = renderingData.cullResults.visibleReflectionProbes;
            for (int i = 0; i < probes.Length; i++)
            {
                if (!PRTVolumeManager.TryGetReflectionProbeAdditionalData(probes[i].reflectionProbe, out var additionalData))
                {
                    _reflectionProbeData[i].normalizeWithProbeVolume = 0;
                    continue;
                }

                if (!additionalData.TryGetSHForNormalization(out var outL0L1, out var outL21, out var outL22))
                {
                    _reflectionProbeData[i].normalizeWithProbeVolume = 0;
                    continue;
                }

                _reflectionProbeData[i].L0L1 = outL0L1;
                _reflectionProbeData[i].L2_1 = outL21;
                _reflectionProbeData[i].L2_2 = outL22;
                _reflectionProbeData[i].normalizeWithProbeVolume = 1;
            }
            _reflectionProbeComputeBuffer.SetData(_reflectionProbeData);
            cmd.SetGlobalBuffer(ShaderProperties._reflectionProbeNormalizationData, _reflectionProbeComputeBuffer);

            // Set reflection normalization parameters
            var reflectionNormalization = VolumeManager.instance.stack.GetComponent<ReflectionNormalization>();
            if (reflectionNormalization != null && reflectionNormalization.IsActive())
            {
                Vector4 factor = new Vector4(reflectionNormalization.minNormalizationFactor.value,
                    reflectionNormalization.minNormalizationFactor.value,
                    0, reflectionNormalization.probeVolumeWeight.value);
                cmd.SetGlobalVector(ShaderProperties._reflectionProbeNormalizationFactor, factor);
            }
            else
            {
                cmd.SetGlobalVector(ShaderProperties._reflectionProbeNormalizationFactor, Vector4.zero);
            }
        }

        private void DoRelight(CommandBuffer cmd, PRTProbeVolume volume)
        {
            // Get probes that need to be updated
            using (ListPool<PRTProbe>.Get(out var probesToUpdate))
            {
                // Ensure bounding box update before upload gpu
                volume.GetProbesToUpdate(probesToUpdate);

                if (probesToUpdate.Count > 0)
                {
                    ExecuteRelightProbes(cmd, volume, probesToUpdate);
                }
            }

            volume.AdvanceRenderFrame();
        }

        private void ExecuteRelightProbes(CommandBuffer cmd, PRTProbeVolume volume, List<PRTProbe> probesToUpdate)
        {
            Vector3 corner = volume.GetVoxelMinCorner();
            Vector4 voxelCorner = new Vector4(corner.x, corner.y, corner.z, 0);
            Vector4 voxelSize = new Vector4(volume.probeSizeX, volume.probeSizeY, volume.probeSizeZ, 0);

            // Bounding box parameters
            Vector4 boundingBoxMin = new Vector4(volume.BoundingBoxMin.x, volume.BoundingBoxMin.y, volume.BoundingBoxMin.z, 0);
            Vector4 boundingBoxSize = new Vector4(volume.CurrentVoxelGrid.X, volume.CurrentVoxelGrid.Y, volume.CurrentVoxelGrid.Z, 0);
            Vector4 originalBoundingBoxMin = new Vector4(volume.OriginalBoxMin.x, volume.OriginalBoxMin.y, volume.OriginalBoxMin.z, 0);

            // Initialize Factor buffer
            InitializeFactorBuffer(volume);

            // Initialize Validity Mask buffer
            InitializeValidityMaskBuffer(volume);

            cmd.SetGlobalFloat(ShaderProperties._coefficientVoxelGridSize, volume.probeGridSize);
            cmd.SetGlobalVector(ShaderProperties._coefficientVoxelSize, voxelSize);
            cmd.SetGlobalVector(ShaderProperties._coefficientVoxelCorner, voxelCorner);
            cmd.SetGlobalVector(ShaderProperties._boundingBoxMin, boundingBoxMin);
            cmd.SetGlobalVector(ShaderProperties._boundingBoxSize, boundingBoxSize);
            cmd.SetGlobalVector(ShaderProperties._originalBoundingBoxMin, originalBoundingBoxMin);
            cmd.SetGlobalTexture(ShaderProperties._coefficientVoxel3D, volume.CoefficientVoxel3D);
            cmd.SetGlobalTexture(ShaderProperties._validityVoxel3D, volume.ValidityVoxel3D);

#if UNITY_EDITOR
            if (volume.debugMode == ProbeVolumeDebugMode.ProbeRadiance)
            {
                CoreUtils.SetKeyword(cmd, ShaderKeywords._RELIGHT_DEBUG_RADIANCE, true);
            }
            else
#endif
            {
                CoreUtils.SetKeyword(cmd, ShaderKeywords._RELIGHT_DEBUG_RADIANCE, false);
            }

            // Set shadow keyword
            CoreUtils.SetKeyword(cmd, ShaderKeywords._PRT_RELIGHT_SHADOW, volume.enableRelightShadow);

            // Relight Bricks
            using (new ProfilingScope(cmd, _relightBrickSampler))
            {
                RelightBricks(cmd, volume, probesToUpdate);
            }

            // Relight Probes
            using (new ProfilingScope(cmd, _relightProbeSampler))
            {
                foreach (var probe in probesToUpdate)
                {
                    RelightProbe(cmd, volume, probe);
                }
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

        private void RelightBricks(CommandBuffer cmd, PRTProbeVolume volume, List<PRTProbe> probesToUpdate)
        {
            // Get bricks that need to be updated based on the probes being updated
            using (ListPool<int>.Get(out var brickIndicesToUpdate))
            {
                volume.GetBricksToUpdate(probesToUpdate, brickIndicesToUpdate);

                // Initialize buffer with only the bricks that need updating
                InitializeBrickBuffer(volume, brickIndicesToUpdate);

                cmd.SetComputeBufferParam(_brickRelightCS, _brickRelightKernel,
                    ShaderProperties._surfels, volume.GlobalSurfelBuffer);
                cmd.SetComputeBufferParam(_brickRelightCS, _brickRelightKernel,
                    ShaderProperties._brickRadiance, _brickRadianceBuffer);
                cmd.SetComputeBufferParam(_brickRelightCS, _brickRelightKernel,
                    ShaderProperties._brickInfo, _surfelIndicesBuffer);
                cmd.SetComputeBufferParam(_brickRelightCS, _brickRelightKernel,
                    ShaderProperties._brickIndexMapping, _brickIndexMappingBuffer);
                cmd.SetComputeBufferParam(_brickRelightCS, _brickRelightKernel,
                    ShaderProperties._shadowCache, _shadowCacheBuffer);
                cmd.SetComputeIntParam(_brickRelightCS, ShaderProperties._brickCount, brickIndicesToUpdate.Count);

                int threadGroups = (brickIndicesToUpdate.Count + 63) / 64;
                cmd.DispatchCompute(_brickRelightCS, _brickRelightKernel, threadGroups, 1, 1);
            }
        }

        private void RelightProbe(CommandBuffer cmd, PRTProbeVolume volume, PRTProbe probe)
        {
            var factorIndices = volume.GetAllProbes()[probe.Index];
            cmd.SetComputeVectorParam(_probeRelightCS, ShaderProperties._probePos,
                new Vector4(probe.Position.x, probe.Position.y, probe.Position.z, 1));
            cmd.SetComputeIntParam(_probeRelightCS, ShaderProperties._factorStart, factorIndices.start);
            cmd.SetComputeIntParam(_probeRelightCS, ShaderProperties._factorCount,
                factorIndices.end - factorIndices.start + 1);
            cmd.SetComputeIntParam(_probeRelightCS, ShaderProperties._indexInProbeVolume, probe.Index);

            cmd.SetComputeBufferParam(_probeRelightCS, _probeRelightKernel,
                ShaderProperties._brickRadiance, _brickRadianceBuffer);
            cmd.SetComputeBufferParam(_probeRelightCS, _probeRelightKernel,
                ShaderProperties._factors, _factorBuffer);
            cmd.SetComputeTextureParam(_probeRelightCS, _probeRelightKernel,
                ShaderProperties._coefficientVoxel3D, volume.CoefficientVoxel3D);
            cmd.SetComputeTextureParam(_probeRelightCS, _probeRelightKernel,
                ShaderProperties._validityVoxel3D, volume.ValidityVoxel3D);
            cmd.SetComputeBufferParam(_probeRelightCS, _probeRelightKernel,
                ShaderProperties._validityMasks, _validityMaskBuffer);

#if UNITY_EDITOR
            // Debug data
            if (volume.debugMode == ProbeVolumeDebugMode.ProbeRadiance)
            {
                var debugData = volume.GetProbeDebugData(probe.Index);
                cmd.SetBufferData(debugData.CoefficientSH9, _coefficientClearValue);
                cmd.SetComputeBufferParam(_probeRelightCS, _probeRelightKernel, ShaderProperties._coefficientSH9, debugData.CoefficientSH9);
            }
#endif
            cmd.DispatchCompute(_probeRelightCS, _probeRelightKernel, 1, 1, 1);
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
