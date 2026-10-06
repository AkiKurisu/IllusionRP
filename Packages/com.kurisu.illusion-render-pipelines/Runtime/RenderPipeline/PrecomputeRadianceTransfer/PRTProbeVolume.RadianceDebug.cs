#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.Rendering;

namespace Illusion.Rendering.PRTGI
{
    public partial class PRTProbeVolume
    {
        private PRTRelightSolver _radianceSolver;
        private Material _radianceMaterial;
        private MaterialPropertyBlock _radianceProperties;
        private static Mesh _radianceSphere;

        private void EnableRadianceDebug() => RenderPipelineManager.beginCameraRendering += DrawProbeRadiance;

        private void DisableRadianceDebug()
        {
            RenderPipelineManager.beginCameraRendering -= DrawProbeRadiance;
            _radianceSolver = null;
            CoreUtils.Destroy(_radianceMaterial);
            _radianceMaterial = null;
        }

        // Draws every probe as one instanced sphere that reads the committed solver state directly.
        private void DrawProbeRadiance(ScriptableRenderContext context, Camera camera)
        {
            if (debugMode != ProbeVolumeDebugMode.ProbeRadiance || selectedProbeDebugMode == ProbeDebugMode.SurfelBrickGrid ||
                PRTVolumeManager.IsBaking || !IsFeatureEnabled || !_isDataInitialized ||
                camera.cameraType is CameraType.Reflection or CameraType.Preview)
                return;
            var solver = _radianceSolver;
            if (solver == null || solver.Disposed)
                return;
            if (!_radianceMaterial)
                _radianceMaterial = CoreUtils.CreateEngineMaterial(IllusionShaders.ProbeSHDebug);
            if (!_radianceSphere)
            {
                var primitive = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                _radianceSphere = primitive.GetComponent<MeshFilter>().sharedMesh;
                DestroyImmediate(primitive);
            }
            _radianceProperties ??= new MaterialPropertyBlock();

            var grid = solver.Grid;
            Vector3 origin = grid.origin + (Vector3)grid.min * grid.spacing;
            bool hideSelected = selectedProbeIndex >= 0 && selectedProbeDebugMode != ProbeDebugMode.IrradianceSphere;
            _radianceProperties.SetBuffer(ShaderProperties.ProbeSH, solver.Previous);
            _radianceProperties.SetBuffer(ShaderProperties.ProbeMetadata, solver.Metadata);
            _radianceProperties.SetVector(ShaderProperties.GridOrigin, new Vector4(origin.x, origin.y, origin.z, grid.spacing));
            _radianceProperties.SetVector(ShaderProperties.GridCount,
                new Vector4(grid.count.x, grid.count.y, grid.count.z, hideSelected ? selectedProbeIndex : -1));
            _radianceProperties.SetFloat(ShaderProperties.ProbeScale, probeHandleSize);

            var bounds = new Bounds();
            bounds.SetMinMax(origin - Vector3.one * probeHandleSize,
                origin + (Vector3)(grid.count - Vector3Int.one) * grid.spacing + Vector3.one * probeHandleSize);
            var parameters = new RenderParams(_radianceMaterial)
            {
                camera = camera, layer = gameObject.layer, matProps = _radianceProperties, worldBounds = bounds,
                shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false
            };
            Graphics.RenderMeshPrimitives(parameters, _radianceSphere, 0, grid.ProbeCount);
        }

        private static class ShaderProperties
        {
            public static readonly int ProbeSH = Shader.PropertyToID("_prtDebugProbeSH");
            public static readonly int ProbeMetadata = Shader.PropertyToID("_prtDebugProbeMetadata");
            public static readonly int GridOrigin = Shader.PropertyToID("_prtDebugGridOrigin");
            public static readonly int GridCount = Shader.PropertyToID("_prtDebugGridCount");
            public static readonly int ProbeScale = Shader.PropertyToID("_prtDebugProbeScale");
        }
    }
}
#endif
