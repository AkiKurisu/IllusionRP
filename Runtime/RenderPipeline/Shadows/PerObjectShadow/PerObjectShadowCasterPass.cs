/*
 * StarRailNPRShader - Fan-made shaders for Unity URP attempting to replicate
 * the shading of Honkai: Star Rail.
 * https://github.com/stalomeow/StarRailNPRShader
 *
 * Copyright (C) 2023 Stalo <stalowork@163.com>
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering.Shadows
{
    public class PerObjectShadowCasterPass : ScriptableRenderPass, IDisposable
    {
        public const int MaxShadowCount = 16;

        private readonly Matrix4x4[] _shadowMatrixArray;

        private readonly Vector4[] _shadowMapRectArray;

        private readonly float[] _shadowCasterIdArray;

        private ShadowCasterManager _casterManager;

        private RTHandle _shadowMap;

        private PerObjectShadowLightData _lightData;

        // Per-object shadow PCSS parameters
        private readonly Vector4[] _perObjShadowPcssParams0;
        private readonly Vector4[] _perObjShadowPcssParams1;
        private readonly Vector4[] _perObjShadowPcssProjs;
        private readonly Vector4[] _perObjShadowBiases;

        private readonly IllusionRendererData _rendererData;

        private readonly ShadowAllocation[] _allocations = new ShadowAllocation[MaxShadowCount];

        private readonly ShadowAllocation[] _idealAllocations = new ShadowAllocation[MaxShadowCount];

        private readonly ShadowAllocation[] _packingScratch = new ShadowAllocation[MaxShadowCount];

        private readonly bool[] _rejectedUpgrades = new bool[MaxShadowCount];

        private List<RectInt> _freeRects = new(64);

        private List<RectInt> _splitFreeRects = new(64);

        private int _allocationCount;

        private int _atlasWidth;

        private int _atlasHeight;

        private struct ShadowAllocation
        {
            public int CasterIndex;
            public int CasterId;
            public float Priority;
            public float ProjectedCoveragePixels;
            public int Resolution;
            public RectInt Viewport;
        }

        private sealed class ShadowAllocationIdComparer : IComparer<ShadowAllocation>
        {
            public static readonly ShadowAllocationIdComparer Instance = new();

            public int Compare(ShadowAllocation x, ShadowAllocation y)
            {
                return x.CasterId.CompareTo(y.CasterId);
            }
        }

        private sealed class ShadowAllocationComparer : IComparer<ShadowAllocation>
        {
            public static readonly ShadowAllocationComparer Instance = new();

            public int Compare(ShadowAllocation x, ShadowAllocation y)
            {
                int result = y.Resolution.CompareTo(x.Resolution);
                if (result != 0) return result;
                result = x.Priority.CompareTo(y.Priority);
                return result != 0 ? result : x.CasterId.CompareTo(y.CasterId);
            }
        }

        private sealed class ShadowAllocationPriorityComparer : IComparer<ShadowAllocation>
        {
            public static readonly ShadowAllocationPriorityComparer Instance = new();

            public int Compare(ShadowAllocation x, ShadowAllocation y)
            {
                int result = x.Priority.CompareTo(y.Priority);
                return result != 0 ? result : x.CasterId.CompareTo(y.CasterId);
            }
        }

        public PerObjectShadowCasterPass(IllusionRendererData rendererData)
        {
            _rendererData = rendererData;
            renderPassEvent = IllusionRenderPassEvent.PerObjectShadowCasterPass;
            profilingSampler = new ProfilingSampler("MainLightPerObjectSceneShadow");

            _shadowMatrixArray = new Matrix4x4[MaxShadowCount];
            _shadowMapRectArray = new Vector4[MaxShadowCount];
            _shadowCasterIdArray = new float[MaxShadowCount];
            _perObjShadowBiases = new Vector4[MaxShadowCount];

            // Initialize PCSS parameter arrays
            _perObjShadowPcssParams0 = new Vector4[MaxShadowCount];
            _perObjShadowPcssParams1 = new Vector4[MaxShadowCount];
            _perObjShadowPcssProjs = new Vector4[MaxShadowCount];
        }

        public void Dispose()
        {
            _shadowMap?.Release();
        }

        public void Setup(ShadowCasterManager casterManager, PerObjectShadows settings, DepthBits depthBits,
            in PerObjectShadowLightData lightData)
        {
            _casterManager = casterManager;
            _lightData = lightData;
            _allocationCount = casterManager.VisibleCount;

            if (_allocationCount <= 0)
            {
                UpdateAllocationSignature(settings.adaptiveTileResolution.value);
                return;
            }

            if (settings.adaptiveTileResolution.value)
            {
                SetupAdaptiveAllocations(settings);
            }
            else
            {
                SetupFixedAllocations((int)settings.perObjectShadowTileResolution.value);
            }

            int shadowRTDepthBits = Mathf.Max((int)depthBits, (int)DepthBits.Depth8);
            ShadowUtils.ShadowRTReAllocateIfNeeded(ref _shadowMap, _atlasWidth, _atlasHeight,
                shadowRTDepthBits, name: "_MainLightPerObjectShadow");
            UpdateAllocationSignature(settings.adaptiveTileResolution.value);
        }

        private void SetupFixedAllocations(int tileResolution)
        {
            int tileCount = Mathf.CeilToInt(Mathf.Sqrt(_allocationCount));
            _atlasWidth = tileCount * tileResolution;
            _atlasHeight = _atlasWidth;

            for (int i = 0; i < _allocationCount; i++)
            {
                _allocations[i] = new ShadowAllocation
                {
                    CasterIndex = i,
                    CasterId = _casterManager.GetId(i),
                    Priority = _casterManager.GetPriority(i),
                    Resolution = tileResolution,
                    Viewport = new RectInt(i % tileCount * tileResolution, i / tileCount * tileResolution,
                        tileResolution, tileResolution)
                };
            }
        }

        private void SetupAdaptiveAllocations(PerObjectShadows settings)
        {
            _atlasWidth = Mathf.Max(256, (int)settings.adaptiveAtlasResolution.value);
            _atlasHeight = _atlasWidth;
            int maximumResolution = Mathf.Clamp((int)settings.maximumAdaptiveTileResolution.value, 256, _atlasWidth);
            IllusionRendererData.PerObjectShadowAtlasState cameraState =
                _rendererData.CurrentPerObjectShadowAtlasState;

            int visibleCount = _allocationCount;
            for (int i = 0; i < visibleCount; i++)
            {
                int casterId = _casterManager.GetId(i);
                _idealAllocations[i] = new ShadowAllocation
                {
                    CasterIndex = i,
                    CasterId = casterId,
                    Priority = _casterManager.GetPriority(i),
                    ProjectedCoveragePixels = _casterManager.GetScreenCoveragePixels(i),
                    Resolution = 256
                };
            }

            Array.Sort(_idealAllocations, 0, visibleCount, ShadowAllocationPriorityComparer.Instance);
            int minimumTileCountPerAxis = _atlasWidth / 256;
            int minimumTileCapacity = minimumTileCountPerAxis * minimumTileCountPerAxis;
            _allocationCount = Mathf.Min(visibleCount, minimumTileCapacity);

            BuildIdealAdaptiveAllocations(maximumResolution);
            if (_allocationCount <= 0)
            {
                cameraState.Prune(_rendererData.FrameCount);
                return;
            }

            for (int i = 0; i < _allocationCount; i++)
            {
                ShadowAllocation ideal = _idealAllocations[i];
                ideal.Resolution = cameraState.ResolveTileResolution(ideal.CasterId, ideal.Resolution,
                    maximumResolution, _rendererData.FrameCount);
                _allocations[i] = ideal;
            }

            cameraState.Prune(_rendererData.FrameCount);
            while (!TryPackAdaptiveAllocations(_allocations))
            {
                int downgradeIndex = FindLowestPriorityDowngradeCandidate();
                if (downgradeIndex < 0)
                {
                    _allocationCount = 0;
                    break;
                }

                ShadowAllocation allocation = _allocations[downgradeIndex];
                allocation.Resolution = GetLowerResolution(allocation.Resolution);
                _allocations[downgradeIndex] = allocation;
                cameraState.ForceTileResolution(allocation.CasterId, allocation.Resolution,
                    _rendererData.FrameCount);
            }

            Array.Sort(_allocations, 0, _allocationCount, ShadowAllocationIdComparer.Instance);
        }

        private void BuildIdealAdaptiveAllocations(int maximumResolution)
        {
            if (!TryPackAdaptiveAllocations(_idealAllocations))
            {
                _allocationCount = 0;
                return;
            }

            while (true)
            {
                Array.Clear(_rejectedUpgrades, 0, _allocationCount);
                bool acceptedUpgrade = false;
                while (true)
                {
                    int candidateIndex = FindBestUpgradeCandidate(maximumResolution);
                    if (candidateIndex < 0)
                        break;

                    ShadowAllocation candidate = _idealAllocations[candidateIndex];
                    int previousResolution = candidate.Resolution;
                    candidate.Resolution = GetHigherResolution(previousResolution, maximumResolution);
                    _idealAllocations[candidateIndex] = candidate;

                    if (TryPackAdaptiveAllocations(_idealAllocations))
                    {
                        acceptedUpgrade = true;
                        break;
                    }

                    candidate.Resolution = previousResolution;
                    _idealAllocations[candidateIndex] = candidate;
                    _rejectedUpgrades[candidateIndex] = true;
                }

                if (!acceptedUpgrade)
                    break;
            }
        }

        private int FindBestUpgradeCandidate(int maximumResolution)
        {
            int candidate = -1;
            for (int i = 0; i < _allocationCount; i++)
            {
                ShadowAllocation allocation = _idealAllocations[i];
                if (_rejectedUpgrades[i] || allocation.Resolution >= maximumResolution)
                    continue;

                if (candidate < 0 || IsBetterUpgradeCandidate(allocation, _idealAllocations[candidate]))
                    candidate = i;
            }

            return candidate;
        }

        private static bool IsBetterUpgradeCandidate(ShadowAllocation candidate, ShadowAllocation current)
        {
            float candidateDensity = candidate.ProjectedCoveragePixels / candidate.Resolution;
            float currentDensity = current.ProjectedCoveragePixels / current.Resolution;
            if (!Mathf.Approximately(candidateDensity, currentDensity))
                return candidateDensity > currentDensity;
            if (!Mathf.Approximately(candidate.Priority, current.Priority))
                return candidate.Priority < current.Priority;
            if (!Mathf.Approximately(candidate.ProjectedCoveragePixels, current.ProjectedCoveragePixels))
                return candidate.ProjectedCoveragePixels > current.ProjectedCoveragePixels;
            return candidate.CasterId < current.CasterId;
        }

        private bool TryPackAdaptiveAllocations(ShadowAllocation[] allocations)
        {
            Array.Copy(allocations, _packingScratch, _allocationCount);
            Array.Sort(_packingScratch, 0, _allocationCount, ShadowAllocationComparer.Instance);

            _freeRects.Clear();
            _freeRects.Add(new RectInt(0, 0, _atlasWidth, _atlasHeight));
            for (int i = 0; i < _allocationCount; i++)
            {
                ShadowAllocation allocation = _packingScratch[i];
                if (!TryFindBestShortSideFit(allocation.Resolution, out RectInt viewport))
                    return false;

                allocation.Viewport = viewport;
                _packingScratch[i] = allocation;
                SplitFreeRects(viewport);
            }

            for (int i = 0; i < _allocationCount; i++)
            {
                int casterId = _packingScratch[i].CasterId;
                for (int j = 0; j < _allocationCount; j++)
                {
                    if (allocations[j].CasterId != casterId)
                        continue;

                    ShadowAllocation allocation = allocations[j];
                    allocation.Viewport = _packingScratch[i].Viewport;
                    allocations[j] = allocation;
                    break;
                }
            }

            return true;
        }

        private bool TryFindBestShortSideFit(int resolution, out RectInt result)
        {
            int bestShortSide = int.MaxValue;
            int bestLongSide = int.MaxValue;
            result = default;
            bool found = false;

            for (int i = 0; i < _freeRects.Count; i++)
            {
                RectInt free = _freeRects[i];
                if (resolution > free.width || resolution > free.height)
                    continue;

                int leftoverWidth = free.width - resolution;
                int leftoverHeight = free.height - resolution;
                int shortSide = Mathf.Min(leftoverWidth, leftoverHeight);
                int longSide = Mathf.Max(leftoverWidth, leftoverHeight);
                if (found && (shortSide > bestShortSide ||
                              shortSide == bestShortSide && longSide > bestLongSide ||
                              shortSide == bestShortSide && longSide == bestLongSide && free.y > result.y ||
                              shortSide == bestShortSide && longSide == bestLongSide && free.y == result.y &&
                              free.x >= result.x))
                    continue;

                bestShortSide = shortSide;
                bestLongSide = longSide;
                result = new RectInt(free.x, free.y, resolution, resolution);
                found = true;
            }

            return found;
        }

        private void SplitFreeRects(RectInt used)
        {
            _splitFreeRects.Clear();
            for (int i = 0; i < _freeRects.Count; i++)
            {
                RectInt free = _freeRects[i];
                if (!Intersects(free, used))
                {
                    _splitFreeRects.Add(free);
                    continue;
                }

                if (used.xMin > free.xMin)
                    _splitFreeRects.Add(new RectInt(free.xMin, free.yMin, used.xMin - free.xMin, free.height));
                if (used.xMax < free.xMax)
                    _splitFreeRects.Add(new RectInt(used.xMax, free.yMin, free.xMax - used.xMax, free.height));
                if (used.yMin > free.yMin)
                    _splitFreeRects.Add(new RectInt(free.xMin, free.yMin, free.width, used.yMin - free.yMin));
                if (used.yMax < free.yMax)
                    _splitFreeRects.Add(new RectInt(free.xMin, used.yMax, free.width, free.yMax - used.yMax));
            }

            PruneContainedFreeRects(_splitFreeRects);
            (_freeRects, _splitFreeRects) = (_splitFreeRects, _freeRects);
        }

        private static bool Intersects(RectInt a, RectInt b)
        {
            return a.xMin < b.xMax && a.xMax > b.xMin && a.yMin < b.yMax && a.yMax > b.yMin;
        }

        private static void PruneContainedFreeRects(List<RectInt> freeRects)
        {
            for (int i = freeRects.Count - 1; i >= 0; i--)
            {
                RectInt candidate = freeRects[i];
                if (candidate.width <= 0 || candidate.height <= 0)
                {
                    freeRects.RemoveAt(i);
                    continue;
                }

                for (int j = 0; j < freeRects.Count; j++)
                {
                    if (i == j || !Contains(freeRects[j], candidate))
                        continue;

                    freeRects.RemoveAt(i);
                    break;
                }
            }
        }

        private static bool Contains(RectInt container, RectInt candidate)
        {
            return candidate.xMin >= container.xMin && candidate.yMin >= container.yMin &&
                   candidate.xMax <= container.xMax && candidate.yMax <= container.yMax;
        }

        private int FindLowestPriorityDowngradeCandidate()
        {
            int candidate = -1;
            for (int i = 0; i < _allocationCount; i++)
            {
                if (_allocations[i].Resolution <= 256)
                {
                    continue;
                }

                if (candidate < 0 || _allocations[i].Priority > _allocations[candidate].Priority ||
                    Mathf.Approximately(_allocations[i].Priority, _allocations[candidate].Priority) &&
                    _allocations[i].CasterId > _allocations[candidate].CasterId)
                {
                    candidate = i;
                }
            }

            return candidate;
        }

        private static int GetHigherResolution(int resolution, int maximumResolution)
        {
            int next = resolution switch
            {
                < 512 => 512,
                < 1024 => 1024,
                < 1280 => 1280,
                < 1536 => 1536,
                < 2048 => 2048,
                < 3072 => 3072,
                _ => 4096
            };
            return Mathf.Min(next, maximumResolution);
        }

        private static int GetLowerResolution(int resolution)
        {
            if (resolution > 3072) return 3072;
            if (resolution > 2048) return 2048;
            if (resolution > 1536) return 1536;
            if (resolution > 1280) return 1280;
            if (resolution > 1024) return 1024;
            if (resolution > 512) return 512;
            return 256;
        }

        private void UpdateAllocationSignature(bool adaptive)
        {
            const ulong offsetBasis = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            ulong signature = offsetBasis;
            AddSignatureValue(ref signature, adaptive ? 1 : 0, prime);
            AddSignatureValue(ref signature, _atlasWidth, prime);
            AddSignatureValue(ref signature, _atlasHeight, prime);
            AddSignatureValue(ref signature, _allocationCount, prime);
            for (int i = 0; i < _allocationCount; i++)
            {
                ShadowAllocation allocation = _allocations[i];
                AddSignatureValue(ref signature, allocation.CasterId, prime);
                AddSignatureValue(ref signature, allocation.Resolution, prime);
                AddSignatureValue(ref signature, allocation.Viewport.x, prime);
                AddSignatureValue(ref signature, allocation.Viewport.y, prime);
            }

            _rendererData.CurrentPerObjectShadowAtlasState.UpdateAllocationSignature(signature);
        }

        private static void AddSignatureValue(ref ulong signature, int value, ulong prime)
        {
            signature ^= unchecked((uint)value);
            signature *= prime;
        }

        private class PassData
        {
            internal PerObjectShadowCasterPass Pass;
            internal TextureHandle ShadowmapTexture;
            internal PerObjectShadowLightData LightData;
            internal Vector2 ShadowBias;
            internal float SoftShadowQuality;
            internal ShadowCasterManager CasterManager;
            internal ShadowAllocation[] Allocations;
            internal int AllocationCount;
            internal int AtlasWidth;
            internal int AtlasHeight;
            internal Matrix4x4[] ShadowMatrixArray;
            internal Vector4[] ShadowMapRectArray;
            internal float[] ShadowCasterIdArray;
            internal Vector4[] ShadowBiases;
            internal Vector4[] PerObjShadowPcssParams0;
            internal Vector4[] PerObjShadowPcssParams1;
            internal Vector4[] PerObjShadowPcssProjs;
            internal IllusionRendererData RendererData;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resource = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            if (_allocationCount <= 0)
            {
                // No shadows to render, set shadow count to 0
                using (var builder = renderGraph.AddRasterRenderPass<PassData>("Per-Object Shadow (No Shadows)", out var passData, profilingSampler))
                {
                    passData.Pass = this;
                    
                    builder.AllowPassCulling(false);
                    builder.AllowGlobalStateModification(true);
                    
                    builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                    {
                        context.cmd.SetGlobalInt(PropertyIds.ShadowCount(), 0);
                        context.cmd.SetGlobalInt(PropertyIds.SourceMode(), (int)data.Pass._lightData.Mode);
                        context.cmd.SetGlobalInt(PropertyIds.AdditionalLightIndex(), data.Pass._lightData.AdditionalLightIndex);
                        context.cmd.SetGlobalVector(PropertyIds.LightDirection(), GetLightDirection(data.Pass._lightData));
                    });
                }
                return;
            }

            // Pass 1: Render shadow map
            TextureHandle shadowTexture;
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Per-Object Shadowmap", out var passData, profilingSampler))
            {
                InitPassData(ref passData);
                
                passData.ShadowmapTexture = UniversalRenderer.CreateRenderGraphTexture(renderGraph, _shadowMap.rt.descriptor, "_MainLightPerObjectShadow", true);
                builder.SetRenderAttachmentDepth(passData.ShadowmapTexture);
                
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                
                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    RenderShadowMap(context.cmd, data);
                });
                
                shadowTexture = passData.ShadowmapTexture;
            }
            
            // Pass 2: Set global shadow properties
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Set Per-Object Shadow Globals", out var passData, profilingSampler))
            {
                InitPassData(ref passData);
                passData.ShadowmapTexture = shadowTexture;
                
                if (shadowTexture.IsValid())
                    builder.UseTexture(shadowTexture);
                
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                
                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    SetupShadowGlobalVariables(context.cmd, data);
                });
            }
            
            // The camera need to be setup again after the shadows since those passes override some settings
            UniversalRenderer renderer = (UniversalRenderer)cameraData.renderer;
            renderer.SetupRenderGraphCameraProperties(renderGraph, resource.activeColorTexture);
        }

        private void InitPassData(ref PassData passData)
        {
            passData.Pass = this;
            passData.LightData = _lightData;
            passData.ShadowBias = ResolveShadowBias(_lightData.VisibleLight.light, out bool supportsSoftShadows);
            passData.SoftShadowQuality = ResolveSoftShadowQuality(_lightData.VisibleLight.light, supportsSoftShadows);
            passData.CasterManager = _casterManager;
            passData.Allocations = _allocations;
            passData.AllocationCount = _allocationCount;
            passData.AtlasWidth = _atlasWidth;
            passData.AtlasHeight = _atlasHeight;
            passData.ShadowMatrixArray = _shadowMatrixArray;
            passData.ShadowMapRectArray = _shadowMapRectArray;
            passData.ShadowCasterIdArray = _shadowCasterIdArray;
            passData.ShadowBiases = _perObjShadowBiases;
            passData.PerObjShadowPcssParams0 = _perObjShadowPcssParams0;
            passData.PerObjShadowPcssParams1 = _perObjShadowPcssParams1;
            passData.PerObjShadowPcssProjs = _perObjShadowPcssProjs;
            passData.RendererData = _rendererData;
        }

        private static void RenderShadowMap(RasterCommandBuffer cmd, PassData data)
        {
            cmd.SetGlobalDepthBias(1.0f, 2.5f);
            CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.CastingPunctualLightShadow, false);

            for (int i = 0; i < data.AllocationCount; i++)
            {
                ShadowAllocation allocation = data.Allocations[i];
                int casterIndex = allocation.CasterIndex;
                data.CasterManager.GetMatrices(casterIndex, out Matrix4x4 viewMatrix,
                    out Matrix4x4 projectionMatrix);

                VisibleLight shadowLight = data.LightData.VisibleLight;
                Vector4 shadowBias = GetDirectionalShadowBias(ref shadowLight, data.ShadowBias,
                    data.SoftShadowQuality, projectionMatrix, allocation.Resolution);
                data.ShadowBiases[i] = shadowBias;
                ShadowUtils.SetupShadowCasterConstantBuffer(cmd, ref shadowLight, shadowBias);

                DrawShadow(cmd, data, casterIndex, allocation.Viewport, in viewMatrix, in projectionMatrix);
                data.ShadowMatrixArray[i] = GetShadowMatrix(allocation.Viewport, data.AtlasWidth,
                    data.AtlasHeight, in viewMatrix, projectionMatrix);
                data.ShadowMapRectArray[i] = GetShadowMapRect(allocation.Viewport, data.AtlasWidth,
                    data.AtlasHeight);
                data.ShadowCasterIdArray[i] = allocation.CasterId;
            }

            cmd.SetGlobalDepthBias(0.0f, 0.0f);
            CoreUtils.SetKeyword(cmd, KeywordNames._CASTING_SELF_SHADOW, false);
        }

        // @IllusionRP: URP only populates UniversalShadowData.bias when it allocates a standard shadow atlas.
        // The per-object atlas remains valid without that atlas, so resolve the same source settings independently.
        private static Vector2 ResolveShadowBias(Light light, out bool supportsSoftShadows)
        {
            UniversalRenderPipelineAsset asset = UniversalRenderPipeline.asset;
            supportsSoftShadows = asset && asset.supportsSoftShadows;

            if (!light)
                return Vector2.zero;

            if (light.TryGetComponent(out UniversalAdditionalLightData additionalLightData) &&
                !additionalLightData.usePipelineSettings)
                return new Vector2(light.shadowBias, light.shadowNormalBias);

            return asset
                ? new Vector2(asset.shadowDepthBias, asset.shadowNormalBias)
                : new Vector2(light.shadowBias, light.shadowNormalBias);
        }

        private static Vector4 GetDirectionalShadowBias(ref VisibleLight shadowLight, Vector2 bias,
            float softShadowQuality, Matrix4x4 lightProjectionMatrix, float shadowResolution)
        {
            float frustumSize = 2.0f / lightProjectionMatrix.m00;
            float texelSize = frustumSize / shadowResolution;
            float depthBias = -bias.x * texelSize;
            float normalBias = -bias.y * texelSize;

            if (softShadowQuality > 0.0f)
            {
                float kernelRadius = (SoftShadowQuality)softShadowQuality switch
                {
                    SoftShadowQuality.High => 3.5f,
                    SoftShadowQuality.Low => 1.5f,
                    _ => 2.5f
                };

                depthBias *= kernelRadius;
                normalBias *= kernelRadius;
            }

            return new Vector4(depthBias, normalBias, (float)LightType.Directional, 0.0f);
        }

        private static float ResolveSoftShadowQuality(Light light, bool supportsSoftShadows)
        {
            if (!light || !supportsSoftShadows || light.shadows != LightShadows.Soft)
                return 0.0f;

            return ShadowUtils.SoftShadowQualityToShaderProperty(light, true);
        }

        private static void DrawShadow(RasterCommandBuffer cmd, PassData data, int casterIndex, RectInt viewportRect,
            in Matrix4x4 view, in Matrix4x4 proj)
        {
            cmd.SetViewProjectionMatrices(view, proj);

            Rect viewport = new(viewportRect.x, viewportRect.y, viewportRect.width, viewportRect.height);
            cmd.SetViewport(viewport);

            cmd.EnableScissorRect(new Rect(viewport.x + 4, viewport.y + 4, viewport.width - 8, viewport.height - 8));
            data.CasterManager.Draw(cmd, casterIndex);
            cmd.DisableScissorRect();
        }

        private static void SetupShadowGlobalVariables(RasterCommandBuffer cmd, PassData data)
        {
            // Set shadow map texture
            cmd.SetGlobalTexture(PropertyIds.ShadowMap(), data.ShadowmapTexture);
            cmd.SetGlobalInt(PropertyIds.ShadowCount(), data.AllocationCount);
            cmd.SetGlobalInt(PropertyIds.SourceMode(), (int)data.LightData.Mode);
            cmd.SetGlobalInt(PropertyIds.AdditionalLightIndex(), data.LightData.AdditionalLightIndex);
            cmd.SetGlobalVector(PropertyIds.LightDirection(), GetLightDirection(data.LightData));
            Light source = data.LightData.VisibleLight.light;
            cmd.SetGlobalVector(PropertyIds.ShadowParams(), new Vector4(
                source ? source.shadowStrength : 1.0f,
                data.SoftShadowQuality, 0.0f, 0.0f));
            cmd.SetGlobalMatrixArray(PropertyIds.ShadowMatrices(), data.ShadowMatrixArray);
            cmd.SetGlobalVectorArray(PropertyIds.ShadowMapRects(), data.ShadowMapRectArray);
            cmd.SetGlobalVectorArray(PropertyIds.ShadowBiases(), data.ShadowBiases);
            cmd.SetGlobalFloatArray(PropertyIds.ShadowCasterIds(), data.ShadowCasterIdArray);

            // Set shadow sampling data
            int renderTargetWidth = data.Pass._shadowMap.rt.width;
            int renderTargetHeight = data.Pass._shadowMap.rt.height;
            float invShadowAtlasWidth = 1.0f / renderTargetWidth;
            float invShadowAtlasHeight = 1.0f / renderTargetHeight;
            float invHalfShadowAtlasWidth = 0.5f * invShadowAtlasWidth;
            float invHalfShadowAtlasHeight = 0.5f * invShadowAtlasHeight;

            cmd.SetGlobalVector(PropertyIds.ShadowOffset0(),
                new Vector4(-invHalfShadowAtlasWidth, -invHalfShadowAtlasHeight, invHalfShadowAtlasWidth, -invHalfShadowAtlasHeight));
            cmd.SetGlobalVector(PropertyIds.ShadowOffset1(),
                new Vector4(-invHalfShadowAtlasWidth, invHalfShadowAtlasHeight, invHalfShadowAtlasWidth, invHalfShadowAtlasHeight));
            cmd.SetGlobalVector(PropertyIds.ShadowMapSize(),
                new Vector4(invShadowAtlasWidth, invShadowAtlasHeight, renderTargetWidth, renderTargetHeight));

            // Set PCSS data if enabled
            if (data.RendererData.PCSSShadowSampling)
            {
                var pcssParams = VolumeManager.instance.stack.GetComponent<PercentageCloserSoftShadows>();
                float lightAngularDiameter = pcssParams.angularDiameter.value;
                float dirlightDepth2Radius = Mathf.Tan(0.5f * Mathf.Deg2Rad * lightAngularDiameter);
                float minFilterAngularDiameter =
                    Mathf.Max(pcssParams.blockerSearchAngularDiameter.value, pcssParams.minFilterMaxAngularDiameter.value);
                float halfMinFilterAngularDiameterTangent =
                    Mathf.Tan(0.5f * Mathf.Deg2Rad * Mathf.Max(minFilterAngularDiameter, lightAngularDiameter));

                float halfBlockerSearchAngularDiameterTangent =
                    Mathf.Tan(0.5f * Mathf.Deg2Rad * Mathf.Max(pcssParams.blockerSearchAngularDiameter.value, lightAngularDiameter));

                for (int i = 0; i < data.AllocationCount; i++)
                {
                    data.CasterManager.GetMatrices(data.Allocations[i].CasterIndex, out _,
                        out Matrix4x4 projectionMatrix);

                    // Calculate shadowmap depth to radial scale for per-object shadow
                    float shadowmapDepth2RadialScale = Mathf.Abs(projectionMatrix.m00 / projectionMatrix.m22);

                    // PCSS Parameters 0
                    data.PerObjShadowPcssParams0[i].x = dirlightDepth2Radius * shadowmapDepth2RadialScale;
                    data.PerObjShadowPcssParams0[i].y = 1.0f / data.PerObjShadowPcssParams0[i].x;
                    // Match the scaled metric caps used by the main-light path.
                    data.PerObjShadowPcssParams0[i].z = data.RendererData.ScaleWorldDistance(pcssParams.maxPenumbraSize.value)
                                                           / (2.0f * halfMinFilterAngularDiameterTangent);
                    data.PerObjShadowPcssParams0[i].w = data.RendererData.ScaleWorldDistance(pcssParams.maxSamplingDistance.value);

                    // PCSS Parameters 1
                    data.PerObjShadowPcssParams1[i].x = pcssParams.minFilterSizeTexels.value;
                    data.PerObjShadowPcssParams1[i].y = 1.0f / (halfMinFilterAngularDiameterTangent * shadowmapDepth2RadialScale);
                    data.PerObjShadowPcssParams1[i].z = 1.0f / (halfBlockerSearchAngularDiameterTangent * shadowmapDepth2RadialScale);
                    data.PerObjShadowPcssParams1[i].w = 0;

                    // Projection parameters
                    data.PerObjShadowPcssProjs[i] = new Vector4(projectionMatrix.m00, projectionMatrix.m11, projectionMatrix.m22, projectionMatrix.m23);
                }

                // Set global shader properties
                cmd.SetGlobalVectorArray(PropertyIds._PerObjShadowPcssParams0, data.PerObjShadowPcssParams0);
                cmd.SetGlobalVectorArray(PropertyIds._PerObjShadowPcssParams1, data.PerObjShadowPcssParams1);
                cmd.SetGlobalVectorArray(PropertyIds._PerObjShadowPcssProjs, data.PerObjShadowPcssProjs);
            }
        }

        private static Vector4 GetLightDirection(in PerObjectShadowLightData lightData)
        {
            if (!lightData.IsValid)
                return Vector4.zero;

            Vector3 direction = -lightData.VisibleLight.localToWorldMatrix.GetColumn(2);
            return new Vector4(direction.x, direction.y, direction.z, 0.0f);
        }

        private static Matrix4x4 GetShadowMatrix(RectInt viewport, int atlasWidth, int atlasHeight,
            in Matrix4x4 viewMatrix, Matrix4x4 projectionMatrix)
        {
            if (SystemInfo.usesReversedZBuffer)
            {
                projectionMatrix.m20 = -projectionMatrix.m20;
                projectionMatrix.m21 = -projectionMatrix.m21;
                projectionMatrix.m22 = -projectionMatrix.m22;
                projectionMatrix.m23 = -projectionMatrix.m23;
            }

            Matrix4x4 textureScaleAndBias = Matrix4x4.identity;
            textureScaleAndBias.m00 = 0.5f * viewport.width / atlasWidth;
            textureScaleAndBias.m11 = 0.5f * viewport.height / atlasHeight;
            textureScaleAndBias.m22 = 0.5f;
            textureScaleAndBias.m03 = (viewport.x + 0.5f * viewport.width) / atlasWidth;
            textureScaleAndBias.m13 = (viewport.y + 0.5f * viewport.height) / atlasHeight;
            textureScaleAndBias.m23 = 0.5f;

            // Apply texture scale and offset to save a MAD in shader.
            return textureScaleAndBias * projectionMatrix * viewMatrix;
        }

        private static Vector4 GetShadowMapRect(RectInt viewport, int atlasWidth, int atlasHeight)
        {
            // x: xMin
            // y: xMax
            // z: yMin
            // w: yMax
            return new Vector4(
                (float)viewport.x / atlasWidth,
                (float)viewport.xMax / atlasWidth,
                (float)viewport.y / atlasHeight,
                (float)viewport.yMax / atlasHeight);
        }

        private static class KeywordNames
        {
            public static readonly string _CASTING_SELF_SHADOW = MemberNameHelpers.String();
        }

        internal static class PropertyIds
        {
            private static readonly int _PerObjSceneShadowMap = MemberNameHelpers.ShaderPropertyID();

            private static readonly int _PerObjSceneShadowCount = MemberNameHelpers.ShaderPropertyID();

            private static readonly int _PerObjSceneShadowMatrices = MemberNameHelpers.ShaderPropertyID();

            private static readonly int _PerObjSceneShadowMapRects = MemberNameHelpers.ShaderPropertyID();

            private static readonly int _PerObjSceneShadowCasterIds = MemberNameHelpers.ShaderPropertyID();

            private static readonly int _PerObjSceneShadowOffset0 = MemberNameHelpers.ShaderPropertyID();

            private static readonly int _PerObjSceneShadowOffset1 = MemberNameHelpers.ShaderPropertyID();

            private static readonly int _PerObjSceneShadowMapSize = MemberNameHelpers.ShaderPropertyID();

            private static readonly int _PerObjSceneShadowSourceMode = MemberNameHelpers.ShaderPropertyID();

            private static readonly int _PerObjSceneShadowAdditionalLightIndex = MemberNameHelpers.ShaderPropertyID();

            private static readonly int _PerObjSceneShadowLightDirection = MemberNameHelpers.ShaderPropertyID();

            private static readonly int _PerObjSceneShadowParams = MemberNameHelpers.ShaderPropertyID();

            // Per-object shadow PCSS parameters
            public static readonly int _PerObjShadowPcssParams0 = MemberNameHelpers.ShaderPropertyID();
            
            public static readonly int _PerObjShadowPcssParams1 = MemberNameHelpers.ShaderPropertyID();
            
            public static readonly int _PerObjShadowPcssProjs = MemberNameHelpers.ShaderPropertyID();
                        
            private static readonly int _PerObjShadowBiases = MemberNameHelpers.ShaderPropertyID();

            public static int ShadowMap() => _PerObjSceneShadowMap;

            public static int ShadowCount() => _PerObjSceneShadowCount;

            public static int ShadowMatrices() => _PerObjSceneShadowMatrices;

            public static int ShadowMapRects() => _PerObjSceneShadowMapRects;

            public static int ShadowBiases() => _PerObjShadowBiases;

            public static int ShadowCasterIds() => _PerObjSceneShadowCasterIds;

            public static int ShadowOffset0() => _PerObjSceneShadowOffset0;

            public static int ShadowOffset1() => _PerObjSceneShadowOffset1;

            public static int ShadowMapSize() => _PerObjSceneShadowMapSize;

            public static int SourceMode() => _PerObjSceneShadowSourceMode;

            public static int AdditionalLightIndex() => _PerObjSceneShadowAdditionalLightIndex;

            public static int LightDirection() => _PerObjSceneShadowLightDirection;

            public static int ShadowParams() => _PerObjSceneShadowParams;
        }
    }
}
