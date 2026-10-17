using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Illusion.Rendering.PathTracing
{
    internal sealed class PathTracingScene : IDisposable
    {
        private const int AlphaTestQueueMin = (int)RenderQueue.AlphaTest;

        private const int TransparentQueueMin = (int)RenderQueue.GeometryLast + 1;

        private readonly RayTracingAccelerationStructure _accelerationStructure;

        private readonly RayTracingInstanceCullingTest[] _instanceTests = new RayTracingInstanceCullingTest[3];

        private int _updatedFrame = -1;

        private readonly PathTracingInstanceTable _instances = new();

        public RayTracingAccelerationStructure AccelerationStructure => _accelerationStructure;

        public PathTracingInstanceTable Instances => _instances;

        public int SceneHash { get; private set; }

        public bool TransformsChanged { get; private set; }

        public PathTracingScene()
        {
            var settings = new RayTracingAccelerationStructure.Settings(
                RayTracingAccelerationStructure.ManagementMode.Manual,
                RayTracingAccelerationStructure.RayTracingModeMask.Everything, -1);
            _accelerationStructure = new RayTracingAccelerationStructure(settings);
        }

        public bool Update(Camera camera, int layerMask)
        {
            if (_updatedFrame == PathTracingFrame.Index)
                return false;
            _updatedFrame = PathTracingFrame.Index;

            _accelerationStructure.ClearInstances();

            _instanceTests[0] = InstanceTest(layerMask, 1 << (int)ShadowCastingMode.Off, PathTracingInstanceTable.SceneMask);
            _instanceTests[1] = InstanceTest(layerMask, (1 << (int)ShadowCastingMode.On) | (1 << (int)ShadowCastingMode.TwoSided),
                PathTracingInstanceTable.SceneMask | PathTracingInstanceTable.ShadowMask);
            _instanceTests[2] = InstanceTest(layerMask, 1 << (int)ShadowCastingMode.ShadowsOnly, PathTracingInstanceTable.ShadowMask);

            var config = new RayTracingInstanceCullingConfig
            {
                flags = RayTracingInstanceCullingFlags.EnableLODCulling,
                instanceTests = _instanceTests,
                // @IllusionRP: ASE can enable alpha testing through defines instead of material keywords.
                subMeshFlagsConfig = new RayTracingSubMeshFlagsConfig
                {
                    opaqueMaterials = RayTracingSubMeshFlags.Enabled | RayTracingSubMeshFlags.ClosestHitOnly,
                    alphaTestedMaterials = RayTracingSubMeshFlags.Enabled | RayTracingSubMeshFlags.UniqueAnyHitCalls,
                    transparentMaterials = RayTracingSubMeshFlags.Enabled | RayTracingSubMeshFlags.UniqueAnyHitCalls
                },
                alphaTestedMaterialConfig = new RayTracingInstanceMaterialConfig
                {
                    renderQueueLowerBound = AlphaTestQueueMin,
                    renderQueueUpperBound = TransparentQueueMin - 1,
                    optionalShaderKeywords = new[] { "_ALPHATEST_ON" }
                },
                transparentMaterialConfig = new RayTracingInstanceMaterialConfig
                {
                    renderQueueLowerBound = TransparentQueueMin,
                    renderQueueUpperBound = (int)RenderQueue.Overlay,
                    optionalShaderKeywords = new[] { "_SURFACE_TYPE_TRANSPARENT" }
                },
                triangleCullingConfig = new RayTracingInstanceTriangleCullingConfig
                {
                    optionalDoubleSidedShaderKeywords = Array.Empty<string>(),
                    frontTriangleCounterClockwise = false,
                    checkDoubleSidedGIMaterial = false,
                    forceDoubleSided = true
                },
                lodParameters = new LODParameters
                {
                    cameraPosition = camera.transform.position,
                    fieldOfView = camera.fieldOfView,
                    isOrthographic = camera.orthographic,
                    orthoSize = camera.orthographicSize,
                    cameraPixelHeight = camera.pixelHeight
                }
            };

            var results = _accelerationStructure.CullInstances(ref config);
            _instances.Update(_accelerationStructure, layerMask);
            if (_instances.Changed)
                _instances.Upload();
            SceneHash = HashCode.Combine(_instances.Hash, _instances.Version);
            TransformsChanged = results.transformsChanged;
            return true;
        }

        private static RayTracingInstanceCullingTest InstanceTest(int layerMask, int shadowCastingModes, uint instanceMask)
        {
            return new RayTracingInstanceCullingTest
            {
                allowOpaqueMaterials = true,
                allowAlphaTestedMaterials = true,
                allowTransparentMaterials = true,
                allowVisualEffects = false,
                layerMask = layerMask,
                shadowCastingModeMask = shadowCastingModes,
                instanceMask = instanceMask
            };
        }

        public void Dispose()
        {
            _accelerationStructure.Dispose();
            _instances.Dispose();
        }
    }
}
