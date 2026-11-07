using System.Collections.Generic;
using Illusion.Rendering.Shadows;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering.PRTGI
{
    internal static class PRTExistingShadowMaps
    {
        internal static void Append(PRTWorldLightSnapshot light, PRTRelightLightingSnapshot current, ContextContainer frameData,
            RenderGraph graph, bool fragmentBias, List<PRTShadowFaceGpu> output)
        {
            bool sameEpoch = false;
            foreach (var candidate in current.Lights)
                if (candidate.Gpu.LightId == light.Gpu.LightId && candidate.Gpu.VisibilityEpoch == light.Gpu.VisibilityEpoch)
                    sameEpoch = true;
            if (!sameEpoch || !light.Source) return;
            var camera = frameData.Get<UniversalCameraData>();
            var lightData = frameData.Get<UniversalLightData>();
            var resources = frameData.Get<UniversalResourceData>();
            int visibleIndex = -1;
            for (int i = 0; i < lightData.visibleLights.Length; i++)
                if (lightData.visibleLights[i].light == light.Source) { visibleIndex = i; break; }
            if (visibleIndex < 0) return;
            if (visibleIndex == lightData.mainLightIndex && resources.mainShadowsTexture.IsValid())
            {
                var pass = UniversalRenderingUtility.GetMainLightShadowCasterPass(camera.renderer);
                if (pass == null || pass.IsEmptyShadowmap) return;
                int count = pass.CascadeCount;
                var slices = pass.CascadeSlices;
                var desc = graph.GetTextureDesc(resources.mainShadowsTexture);
                for (int i = 0; i < count; i++)
                {
                    var sphere = slices[i].splitData.cullingSphere;
                    sphere.w *= sphere.w;
                    if (sphere.w <= 0) continue;
                    var face = Face(slices[i], desc.width, desc.height, sphere, 1, (uint)i);
                    if (fragmentBias)
                    {
                        var visible = lightData.visibleLights[visibleIndex];
                        face.ReceiverBias = ShadowReceiverData.GetBias(visible.light,
                            slices[i].projectionMatrix, slices[i].resolution, false);
                    }
                    output.Add(face);
                }
            }
            else if (resources.additionalShadowsTexture.IsValid())
            {
                var pass = UniversalRenderingUtility.GetAdditionalLightsShadowCasterPass(camera.renderer);
                if (pass == null || pass.IsEmptyShadowmap) return;
                var mapping = pass.VisibleLightIndexToAdditionalLightIndex;
                var casting = pass.VisibleLightIndexToIsCastingShadows;
                if (visibleIndex >= mapping.Length || visibleIndex >= casting.Length || !casting[visibleIndex]) return;
                int index = mapping[visibleIndex];
                var parameters = pass.AdditionalLightIndexToShadowParams;
                if (index < 0 || index >= parameters.Length || parameters[index].x <= 0 || parameters[index].w < 0) return;
                var matrices = pass.ShadowSliceIndexToWorldShadowMatrix;
                var slices = pass.ShadowSlices;
                int first = (int)parameters[index].w;
                int count = light.Gpu.PositionType.w == 1 ? 6 : 1;
                if (first + count > slices.Length || first + count > matrices.Length) return;
                var desc = graph.GetTextureDesc(resources.additionalShadowsTexture);
                for (int i = 0; i < count; i++)
                {
                    var face = Face(slices[first + i], desc.width, desc.height, new Vector4(0, 0, 0, -1), 2, (uint)i);
                    face.WorldToShadow = matrices[first + i];
                    if (fragmentBias)
                    {
                        var visible = lightData.visibleLights[visibleIndex];
                        face.ReceiverBias = ShadowReceiverData.GetBias(visible.light,
                            slices[first + i].projectionMatrix, slices[first + i].resolution, true);
                    }
                    output.Add(face);
                }
            }
        }

        private static PRTShadowFaceGpu Face(ShadowSliceData slice, int width, int height,
            Vector4 sphere, uint source, uint face)
        {
            return new PRTShadowFaceGpu
            {
                WorldToShadow = slice.shadowTransform,
                AtlasRect = new Vector4((slice.offsetX + 0.5f) / width, (slice.offsetY + 0.5f) / height,
                    (slice.offsetX + slice.resolution - 0.5f) / width, (slice.offsetY + slice.resolution - 0.5f) / height),
                Sphere = sphere, Source = source, Face = face
            };
        }
    }
}
