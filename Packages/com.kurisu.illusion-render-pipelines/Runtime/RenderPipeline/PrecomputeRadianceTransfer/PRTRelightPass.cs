using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering.PRTGI
{
    public partial class PRTRelightPass : ScriptableRenderPass, IDisposable
    {
        private readonly IllusionRendererData _rendererData;
        private readonly ComputeShader _brickShader, _probeShader;
        private readonly int _brickKernel, _probeKernel, _commitKernel, _publishKernel, _pyramidProbeKernel, _pyramidLevelKernel, _publishLevelKernel;
        private readonly PRTWorldLightCollector _lightCollector = new();
        private readonly List<PRTSectorResident> _selected = new();
        private readonly Dictionary<Camera, PRTCameraPublication> _publications = new();
        private readonly PRTNeutralPublication _neutral;
        private static readonly ProfilerMarker CaptureMarker = new("PRT capture immutable inputs");
        private PRTProbeVolume _volume;
        private PRTRelightSolver _solver;
        private PRTEnvironmentSnapshot _environment;
        private int _runtimeDataId;
        private uint _scheduledFrame = uint.MaxValue;

        public bool FragmentShadowBias { get; set; }

        public PRTRelightPass(IllusionRendererData rendererData)
        {
            _rendererData = rendererData;
            _brickShader = rendererData.RuntimeResources.prtBrickRelightCS;
            _probeShader = rendererData.RuntimeResources.prtProbeRelightCS;
            _brickKernel = _brickShader.FindKernel("CSMain");
            _probeKernel = _probeShader.FindKernel("CSMain");
            _commitKernel = _probeShader.FindKernel("CSCommit");
            _publishKernel = _probeShader.FindKernel("CSPublish");
            _pyramidProbeKernel = _probeShader.FindKernel("CSPyramidFromProbes");
            _pyramidLevelKernel = _probeShader.FindKernel("CSPyramidDownsample");
            _publishLevelKernel = _probeShader.FindKernel("CSPublishLevel");
            _neutral = new PRTNeutralPublication(_probeShader);
            InitializeReflectionNormalization();
            profilingSampler = new ProfilingSampler("PRT Relight");
            renderPassEvent = IllusionRenderPassEvent.PrecomputedRadianceTransferRelightPass;
        }

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            var camera = frameData.Get<UniversalCameraData>();
            RecordReflectionNormalization(graph, frameData.Get<UniversalRenderingData>());
            var volume = PRTVolumeManager.ProbeVolume;
            volume?.EnsureRuntimeData();
            if (_volume != volume || (volume && _runtimeDataId != volume.RuntimeDataId))
            {
                ReleaseSimulation();
                _volume = volume;
                _runtimeDataId = volume ? volume.RuntimeDataId : 0;
            }
            bool enabled = camera.cameraType is not (CameraType.Reflection or CameraType.Preview)
                && _rendererData.SampleProbeVolumes && _rendererData.IsLightingActive && volume && volume.IsActivate();
#if UNITY_EDITOR
            enabled &= !PRTVolumeManager.IsBaking;
#endif
            if (!enabled)
            {
                _neutral.Record(graph, frameData);
                return;
            }
            _solver ??= new PRTRelightSolver(volume);
            if (_scheduledFrame != PRTRelightFrame.Index && PRTRelightFrame.IsDriver(camera.camera))
            {
                _scheduledFrame = PRTRelightFrame.Index;
                RecordSimulation(graph, frameData, camera);
            }
            volume.ObserveSolver(_solver);
            Vector3Int count = new(Mathf.Clamp(volume.voxelProbeSize.x, 1, _solver.Grid.count.x),
                Mathf.Clamp(volume.voxelProbeSize.y, 1, _solver.Grid.count.y),
                Mathf.Clamp(volume.voxelProbeSize.z, 1, _solver.Grid.count.z));
            int cascades = Mathf.Clamp(volume.cascadeCount, 1, PRTLayoutConstants.MaxCascades);
            if (!_publications.TryGetValue(camera.camera, out var publication) || publication.Slots != count ||
                publication.RequestedCascades != cascades)
            {
                publication?.Dispose();
                publication = new PRTCameraPublication(camera.camera, _solver.Grid, _solver.Pyramid, count, cascades);
                _publications[camera.camera] = publication;
            }
            publication.Record(graph, frameData, _solver, volume, _probeShader, _publishKernel, _publishLevelKernel);
            RemoveUnusedCameras();
        }

        private void RecordSimulation(RenderGraph graph, ContextContainer frameData, UniversalCameraData camera)
        {
            PRTRelightLightingSnapshot world;
            using (CaptureMarker.Auto())
                world = _lightCollector.Capture(_volume.asset, _volume.enableRelightShadow, FragmentShadowBias);
            bool directional = false;
            foreach (var light in world.Lights) directional |= light.Gpu.PositionType.w == 0;
            int environmentHash = PRTEnvironmentSnapshot.ComputeHash(directional);
            if (_environment == null || _environment.ContentHash != environmentHash)
            {
                _environment?.Dispose();
                _environment = new PRTEnvironmentSnapshot(environmentHash, directional, world.SceneTime);
            }
            var scheduler = _solver.Scheduler;
            var requested = scheduler.Select(camera.camera.transform.position, Mathf.Max(1, _volume.sectorsPerFrame));
            var residency = _solver.Residency;
            _selected.Clear();
            int uploadBudget = (int)Math.Min(int.MaxValue, Mathf.Max(1, _volume.uploadBudgetMiB) * 1048576L);
            long residentBudget = (long)Mathf.Max(1, _volume.sectorBudgetMiB) * 1024 * 1024;
            residency.BeginFrame(scheduler.NearSector, scheduler.BackgroundSector, residentBudget);
            foreach (int id in requested)
            {
                var lights = PRTRelightWorldLighting.Select(world, _volume.asset.Sectors[id].surfelBounds);
                var sector = residency.Request(id, lights, residentBudget);
                if (sector == null) continue;
                if (!sector.Ready)
                    residency.UploadedBytes += sector.RecordUpload(graph, uploadBudget - residency.UploadedBytes);
                if (sector.Ready) _selected.Add(sector);
            }
            if (_selected.Count == 0) return;
            RecordMetadata(graph);
            TextureHandle environment = _environment.Record(graph, camera, frameData.Get<UniversalLightData>(),
                _rendererData.GetExposureTexture(), frameData.Get<UniversalResourceData>().activeColorTexture);
            foreach (var sector in _selected)
            {
                var lighting = sector.Lighting;
                lighting.FragmentShadowBias = FragmentShadowBias;
                lighting.CollectStats = _volume.enableShadowCacheStats;
                RecordBricks(graph, sector, lighting.Record(graph, frameData));
                RecordProbes(graph, environment, sector);
            }
            foreach (var sector in _selected)
            {
                RecordCommit(graph, sector);
                scheduler.Updated(sector.Index);
            }
            _solver.Pyramid.Record(graph, _probeShader, _pyramidProbeKernel, _pyramidLevelKernel, _solver, _volume.asset, _selected);
            RecordShadowDiagnostics(graph, _selected[0]);
            RecordResidualDiagnostics(graph);
        }

        private void RemoveUnusedCameras()
        {
            using (ListPool<Camera>.Get(out var expired))
            {
                foreach (var pair in _publications)
                    if (!pair.Key || PRTRelightFrame.Index - pair.Value.LastUsedFrame > 120)
                        expired.Add(pair.Key);
                foreach (var camera in expired)
                {
                    _publications[camera].Dispose();
                    _publications.Remove(camera);
                }
            }
        }

        private void ReleaseSimulation()
        {
            _solver?.Dispose();
            _solver = null;
            _environment?.Dispose();
            _environment = null;
            _selected.Clear();
            foreach (var publication in _publications.Values)
                publication.Dispose();
            _publications.Clear();
            _scheduledFrame = uint.MaxValue;
            _metadataHash = int.MinValue;
        }

        public void Dispose()
        {
            ReleaseSimulation();
            _neutral.Dispose();
            _reflectionBuffer.Dispose();
        }
    }
}
