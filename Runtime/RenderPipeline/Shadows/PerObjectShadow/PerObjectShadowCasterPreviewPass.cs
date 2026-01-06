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
using UnityEngine.Rendering.Universal;
#if UNITY_2023_1_OR_NEWER
using UnityEngine.Experimental.Rendering.RenderGraphModule;
#endif

namespace Illusion.Rendering.Shadows
{
    public class PerObjectShadowCasterPreviewPass : ScriptableRenderPass
    {
        public PerObjectShadowCasterPreviewPass()
        {
            renderPassEvent = RenderPassEvent.AfterRenderingShadows;
            profilingSampler = new ProfilingSampler("MainLightPerObjectSceneShadow (Preview)");
        }

#if UNITY_2023_1_OR_NEWER
        private class PassData
        {
            internal int shadowCountPropertyID;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, FrameResources frameResources, ref RenderingData renderingData)
        {
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Per-Object Shadow Preview", out var passData, profilingSampler))
            {
                passData.shadowCountPropertyID = PerObjectShadowCasterPass.PropertyIds.ShadowCount();
                
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                
                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalInt(data.shadowCountPropertyID, 0);
                });
            }
        }
#endif

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get();

            using (new ProfilingScope(cmd, profilingSampler))
            {
                cmd.SetGlobalInt(PerObjectShadowCasterPass.PropertyIds.ShadowCount(), 0);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }
}
