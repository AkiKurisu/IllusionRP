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

namespace Illusion.Rendering
{
    public class SetKeywordPass : ScriptableRenderPass
    {
        private readonly string _keyword;

        private readonly bool _state;

        private readonly string _name;

        public SetKeywordPass(string keyword, bool state, RenderPassEvent evt)
        {
            renderPassEvent = evt;
            _keyword = keyword;
            _state = state;
            profilingSampler = new ProfilingSampler(_name = $"Set Keyword {keyword} to {state}");
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, profilingSampler))
            {
                CoreUtils.SetKeyword(cmd, _keyword, _state);
            }
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
        
#if UNITY_2023_1_OR_NEWER
        private class SetKeywordPassData
        {
            internal string Keyword;
            internal bool State;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, FrameResources frameResources,
            ref RenderingData renderingData)
        {
            using (var builder = renderGraph.AddLowLevelPass<SetKeywordPassData>(_name, out var passData, profilingSampler))
            {
                passData.Keyword = _keyword;
                passData.State = _state;
                builder.AllowPassCulling(false);

                builder.SetRenderFunc(static (SetKeywordPassData data, LowLevelGraphContext context) =>
                {
                    if (data.State)
                        context.cmd.EnableShaderKeyword(data.Keyword);
                    else
                        context.cmd.DisableShaderKeyword(data.Keyword);
                });
            }
        }
#endif
    }
}
