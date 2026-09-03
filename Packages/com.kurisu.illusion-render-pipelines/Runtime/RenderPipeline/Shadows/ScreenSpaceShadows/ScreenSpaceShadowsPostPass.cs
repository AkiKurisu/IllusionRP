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

using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering.Shadows
{
    public class ScreenSpaceShadowsPostPass : ScriptableRenderPass
    {
        public ScreenSpaceShadowsPostPass()
        {
            renderPassEvent = IllusionRenderPassEvent.ScreenSpaceShadowsPostPass;
            profilingSampler = new ProfilingSampler("ScreenSpaceShadows Post");
        }

        private class PassData
        {
            internal UniversalShadowData ShadowData;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resource = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            var shadowData = frameData.Get<UniversalShadowData>();
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Screen Space Shadows Post Pass", out var passData, profilingSampler))
            {
                TextureHandle color = resource.activeColorTexture;
                builder.SetRenderAttachment(color, 0);
                passData.ShadowData = shadowData;

                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc((PassData data, RasterGraphContext rgContext) =>
                {
                    ExecutePass(rgContext.cmd, data.ShadowData);
                });
            }
        }

        private static void ExecutePass(RasterCommandBuffer cmd, UniversalShadowData shadowData)
        {
            int cascadesCount = shadowData.mainLightShadowCascadesCount;
            bool mainLightShadows = shadowData.supportsMainLightShadows;
            bool receiveShadowsNoCascade = mainLightShadows && cascadesCount == 1;
            bool receiveShadowsCascades = mainLightShadows && cascadesCount > 1;

            // Before transparent object pass, force to disable screen space shadow of main light
            CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.MainLightShadowScreen, false);

            // then enable main light shadows with or without cascades
            CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.MainLightShadows, receiveShadowsNoCascade);
            CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.MainLightShadowCascades, receiveShadowsCascades);
        }
    }
}
