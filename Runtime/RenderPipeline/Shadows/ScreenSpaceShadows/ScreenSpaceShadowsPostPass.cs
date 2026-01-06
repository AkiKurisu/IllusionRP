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

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if UNITY_2023_1_OR_NEWER
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
#endif

namespace Illusion.Rendering.Shadows
{
    public class ScreenSpaceShadowsPostPass : ScriptableRenderPass
    {
        private static readonly RTHandle CurrentActive = RTHandles.Alloc(BuiltinRenderTextureType.CurrentActive);

        public ScreenSpaceShadowsPostPass()
        {
            renderPassEvent = IllusionRenderPassEvent.ScreenSpaceShadowsPostPass;
            profilingSampler = new ProfilingSampler("ScreenSpaceShadows Post");
        }

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            ConfigureTarget(CurrentActive);
        }

#if UNITY_2023_1_OR_NEWER
        private class PassData
        {
            internal RenderingData RenderingData;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, FrameResources frameResources, ref RenderingData renderingData)
        {
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Screen Space Shadows Post Pass", out var passData, profilingSampler))
            {
                UniversalRenderer renderer = (UniversalRenderer)renderingData.cameraData.renderer;
                TextureHandle color = renderer.activeColorTexture;
                builder.UseTextureFragment(color, 0);
                passData.RenderingData = renderingData;

                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc((PassData data, RasterGraphContext rgContext) =>
                {
                    ExecutePass(rgContext.cmd, ref data.RenderingData);
                });
            }
        }

        private static void ExecutePass(RasterCommandBuffer cmd, ref RenderingData renderingData)
        {
            ShadowData shadowData = renderingData.shadowData;
            int cascadesCount = shadowData.mainLightShadowCascadesCount;
            bool mainLightShadows = renderingData.shadowData.supportsMainLightShadows;
            bool receiveShadowsNoCascade = mainLightShadows && cascadesCount == 1;
            bool receiveShadowsCascades = mainLightShadows && cascadesCount > 1;

            // Before transparent object pass, force to disable screen space shadow of main light
            CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.MainLightShadowScreen, false);

            // then enable main light shadows with or without cascades
            CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.MainLightShadows, receiveShadowsNoCascade);
            CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.MainLightShadowCascades, receiveShadowsCascades);
        }
#endif

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get();

            using (new ProfilingScope(cmd, profilingSampler))
            {
                ShadowData shadowData = renderingData.shadowData;
                int cascadesCount = shadowData.mainLightShadowCascadesCount;
                bool mainLightShadows = renderingData.shadowData.supportsMainLightShadows;
                bool receiveShadowsNoCascade = mainLightShadows && cascadesCount == 1;
                bool receiveShadowsCascades = mainLightShadows && cascadesCount > 1;

                // Before transparent object pass, force to disable screen space shadow of main light
                CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.MainLightShadowScreen, false);

                // then enable main light shadows with or without cascades
                CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.MainLightShadows, receiveShadowsNoCascade);
                CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.MainLightShadowCascades, receiveShadowsCascades);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }
}
