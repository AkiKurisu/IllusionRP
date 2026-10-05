using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering.PRTGI
{
    internal static class PRTExistingShadowMaps
    {
        private static readonly Dictionary<(Type, string), FieldInfo> Fields = new();

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
            var shadowData = frameData.Get<UniversalShadowData>();
            int visibleIndex = -1;
            for (int i = 0; i < lightData.visibleLights.Length; i++)
                if (lightData.visibleLights[i].light == light.Source) { visibleIndex = i; break; }
            if (visibleIndex < 0) return;
            if (visibleIndex == lightData.mainLightIndex && resources.mainShadowsTexture.IsValid())
            {
                var pass = UniversalRenderingUtility.GetMainLightShadowCasterPass(camera.renderer);
                if (pass == null || Read<bool>(pass, "m_CreateEmptyShadowmap")) return;
                int count = Read<int>(pass, "m_ShadowCasterCascadesCount");
                var slices = UniversalRenderingUtility.GetMainLightShadowSliceData(pass);
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
                        face.ReceiverBias = ShadowUtils.GetShadowBias(ref visible, visibleIndex, shadowData,
                            slices[i].projectionMatrix, slices[i].resolution);
                        face.ReceiverBias.z = 1;
                    }
                    output.Add(face);
                }
            }
            else if (resources.additionalShadowsTexture.IsValid())
            {
                var pass = UniversalRenderingUtility.GetAdditionalLightsShadowCasterPass(camera.renderer);
                if (pass == null || Read<bool>(pass, "m_CreateEmptyShadowmap")) return;
                var mapping = Read<short[]>(pass, "m_VisibleLightIndexToAdditionalLightIndex");
                var casting = Read<bool[]>(pass, "m_VisibleLightIndexToIsCastingShadows");
                if (visibleIndex >= mapping.Length || visibleIndex >= casting.Length || !casting[visibleIndex]) return;
                int index = mapping[visibleIndex];
                var parameters = Read<Vector4[]>(pass, "m_AdditionalLightIndexToShadowParams");
                if (index < 0 || index >= parameters.Length || parameters[index].x <= 0 || parameters[index].w < 0) return;
                var matrices = Read<Matrix4x4[]>(pass, "m_AdditionalLightShadowSliceIndexTo_WorldShadowMatrix");
                var slices = Read<ShadowSliceData[]>(pass, "m_AdditionalLightsShadowSlices");
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
                        face.ReceiverBias = ShadowUtils.GetShadowBias(ref visible, visibleIndex, shadowData,
                            slices[first + i].projectionMatrix, slices[first + i].resolution);
                        face.ReceiverBias.z = 1;
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

        private static T Read<T>(object pass, string name)
        {
            var key = (pass.GetType(), name);
            if (!Fields.TryGetValue(key, out var field))
            {
                field = key.Item1.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
                Fields.Add(key, field);
            }
            return (T)field.GetValue(pass);
        }
    }
}
