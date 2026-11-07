using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering.Shadows
{
    internal sealed class ShadowReceiverData
    {
        internal readonly Matrix4x4[] DeviceProjections = new Matrix4x4[IllusionRendererData.ShadowCascadeCount];
        internal readonly Vector4[] DeviceProjectionVectors = new Vector4[IllusionRendererData.ShadowCascadeCount];
        internal ShadowSliceData[] MainSlices { get; private set; } = Array.Empty<ShadowSliceData>();

        private readonly Vector4[] _mainBiases = new Vector4[IllusionRendererData.ShadowCascadeCount + 1];
        private readonly Vector4[] _additionalBiases = new Vector4[UniversalRenderPipeline.maxVisibleAdditionalLights];

        private static float ResolveNormalBias(Light light)
        {
            var asset = UniversalRenderPipeline.asset;
            if (light.TryGetComponent(out UniversalAdditionalLightData data) && !data.usePipelineSettings)
                return light.shadowNormalBias;
            return asset ? asset.shadowNormalBias : light.shadowNormalBias;
        }

        internal static Vector4 GetBias(Light light, Matrix4x4 projection, int resolution, bool perspective)
        {
            float worldTexelSize = 2.0f / Mathf.Abs(projection.m00) / resolution * Mathf.Sqrt(2.0f);
            return new Vector4(worldTexelSize * ResolveNormalBias(light), perspective ? 1 : 0, 1, 0);
        }

        internal void Update(UniversalCameraData camera, UniversalLightData lights, UniversalShadowData shadows,
            UniversalResourceData resources)
        {
            Array.Clear(_mainBiases, 0, _mainBiases.Length);
            Array.Clear(_additionalBiases, 0, _additionalBiases.Length);
            Array.Clear(DeviceProjections, 0, DeviceProjections.Length);
            Array.Clear(DeviceProjectionVectors, 0, DeviceProjectionVectors.Length);
            var main = UniversalRenderingUtility.GetMainLightShadowCasterPass(camera.renderer);
            MainSlices = !resources.mainShadowsTexture.IsValid() || main == null || main.IsEmptyShadowmap
                ? Array.Empty<ShadowSliceData>() : main.CascadeSlices;
            int count = Math.Min(MainSlices.Length, shadows.mainLightShadowCascadesCount);
            for (int i = 0; i < Math.Min(count, DeviceProjections.Length); i++)
            {
                var projection = GL.GetGPUProjectionMatrix(MainSlices[i].projectionMatrix, false);
                DeviceProjections[i] = projection;
                DeviceProjectionVectors[i] = new Vector4(projection.m00, projection.m11, projection.m22, projection.m23);
                if (lights.mainLightIndex >= 0 && MainSlices[i].resolution > 0)
                    _mainBiases[i] = GetBias(lights.visibleLights[lights.mainLightIndex].light,
                        MainSlices[i].projectionMatrix, MainSlices[i].resolution, false);
            }

            var pass = UniversalRenderingUtility.GetAdditionalLightsShadowCasterPass(camera.renderer);
            if (!resources.additionalShadowsTexture.IsValid() || pass == null || pass.IsEmptyShadowmap) return;
            var mapping = pass.VisibleLightIndexToAdditionalLightIndex;
            var casting = pass.VisibleLightIndexToIsCastingShadows;
            var parameters = pass.AdditionalLightIndexToShadowParams;
            var slices = pass.ShadowSlices;
            for (int i = 0; i < lights.visibleLights.Length && i < mapping.Length && i < casting.Length; i++)
            {
                int index = mapping[i];
                if (!casting[i] || index < 0 || index >= _additionalBiases.Length || index >= parameters.Length) continue;
                int first = (int)parameters[index].w;
                if (parameters[index].x <= 0 || first < 0 || first >= slices.Length || slices[first].resolution <= 0) continue;
                _additionalBiases[index] = GetBias(lights.visibleLights[i].light,
                    slices[first].projectionMatrix, slices[first].resolution, true);
            }
        }

        internal void Push(RasterCommandBuffer cmd)
        {
            cmd.SetGlobalVectorArray(IllusionShaderProperties._MainLightShadowCascadeBiases, _mainBiases);
            cmd.SetGlobalVectorArray(IllusionShaderProperties._AdditionalLightShadowReceiverBiases, _additionalBiases);
        }
    }
}
