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

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_2023_1_OR_NEWER
using UnityEngine.Experimental.Rendering;
#endif

namespace Illusion.Rendering.Shadows
{
    public enum ShadowBoundType
    {
        /// <summary>
        /// Default calculate by mesh bound box
        /// </summary>
        Calculated,
        /// <summary>
        /// Use customized override bound box
        /// </summary>
        Customized
    }

    public class ShadowRendererList
    {
        public ShadowBoundType BoundType;

        public Renderer RenderObject;

        public Bounds Bounds;

        public float Priority;

        private readonly struct RendererEntry
        {
            public readonly Renderer Renderer;
            public readonly int DrawCallIndexStartInclusive;
            public readonly int DrawCallIndexEndExclusive;

            public RendererEntry(Renderer renderer, int drawCallIndexStartInclusive, int drawCallIndexEndExclusive)
            {
                Renderer = renderer;
                DrawCallIndexStartInclusive = drawCallIndexStartInclusive;
                DrawCallIndexEndExclusive = drawCallIndexEndExclusive;
            }
        }

        private readonly struct DrawCallData
        {
            public readonly Material Material;
            public readonly int SubmeshIndex;
            public readonly int ShaderPass;

            public DrawCallData(Material material, int submeshIndex, int shaderPass)
            {
                Material = material;
                SubmeshIndex = submeshIndex;
                ShaderPass = shaderPass;
            }
        }

        private readonly List<RendererEntry> m_Renderers = new();

        private readonly List<DrawCallData> m_DrawCalls = new();

        public bool TryGetWorldBounds(out Bounds worldBounds, ICollection<int> outAppendRendererIndices = null)
        {
            worldBounds = default;
            bool firstBounds = true;

            for (int i = 0; i < m_Renderers.Count; i++)
            {
                RendererEntry entry = m_Renderers[i];

                if (!IsEntryEnabled(in entry))
                {
                    continue;
                }

                outAppendRendererIndices?.Add(i);

                if (firstBounds)
                {
                    worldBounds = entry.Renderer.bounds;
                    firstBounds = false;
                }
                else if (BoundType == ShadowBoundType.Calculated)
                {
                    worldBounds.Encapsulate(entry.Renderer.bounds);
                }
            }

            if (!firstBounds)
            {
                if (BoundType == ShadowBoundType.Customized && RenderObject)
                {
                    if (RenderObject is SkinnedMeshRenderer)
                    {
                        // @IllusionRP: SkinnedMeshRenderer use skin mesh local aabb, so hack in this way
                        var local = RenderObject.localBounds;
                        RenderObject.localBounds = Bounds;
                        worldBounds = RenderObject.bounds;
                        RenderObject.localBounds = local;
#if UNITY_EDITOR
                        // SetLocalAABB will set dirty flag which is not needed in this case.
                        if (!Application.isPlaying) UnityEditor.EditorUtility.ClearDirty(RenderObject);
#endif
                    }
                    else
                    {
                        // @IllusionRP: Render use transform local to world matrix
                        Matrix4x4 matrix = RenderObject.localToWorldMatrix;
                        Vector3 worldMin = matrix.MultiplyPoint3x4(Bounds.min);
                        Vector3 worldMax = matrix.MultiplyPoint3x4(Bounds.max);
                        worldBounds.SetMinMax(worldMin, worldMax);
                    }
                }
            }

            return !firstBounds;
        }

        private static bool IsEntryEnabled(in RendererEntry entry)
        {
            if (entry.DrawCallIndexEndExclusive - entry.DrawCallIndexStartInclusive <= 0)
            {
                return false;
            }

            // Disable if is destroyed
            if (!entry.Renderer) return false;

#if UNITY_EDITOR
            if (UnityEditor.SceneVisibilityManager.instance.IsHidden(entry.Renderer.gameObject))
            {
                return false;
            }
#endif

            return entry.Renderer.enabled && entry.Renderer.gameObject.activeInHierarchy &&
                   entry.Renderer.shadowCastingMode != ShadowCastingMode.Off;
        }

        public void Draw(CommandBuffer cmd, int rendererIndex)
        {
            RendererEntry entry = m_Renderers[rendererIndex];
            for (int i = entry.DrawCallIndexStartInclusive; i < entry.DrawCallIndexEndExclusive; i++)
            {
                DrawCallData dc = m_DrawCalls[i];
                cmd.DrawRenderer(entry.Renderer, dc.Material, dc.SubmeshIndex, dc.ShaderPass);
            }
        }
        
#if UNITY_2023_1_OR_NEWER
        public void Draw(RasterCommandBuffer cmd, int rendererIndex)
        {
            RendererEntry entry = m_Renderers[rendererIndex];
            for (int i = entry.DrawCallIndexStartInclusive; i < entry.DrawCallIndexEndExclusive; i++)
            {
                DrawCallData dc = m_DrawCalls[i];
                cmd.DrawRenderer(entry.Renderer, dc.Material, dc.SubmeshIndex, dc.ShaderPass);
            }
        }
#endif

        public void Clear()
        {
            m_Renderers.Clear();
            m_DrawCalls.Clear();
        }

        public void Add(Renderer renderer)
        {
            int drawCallInitialCount = m_DrawCalls.Count;
            List<Material> materialList = ListPool<Material>.Get();

            try
            {
                renderer.GetSharedMaterials(materialList);
                for (int i = 0; i < materialList.Count; i++)
                {
                    Material material = materialList[i];
                    if (TryGetShadowCasterPass(material, out int passIndex))
                    {
                        m_DrawCalls.Add(new DrawCallData(material, i, passIndex));
                    }
                }

                if (m_DrawCalls.Count > drawCallInitialCount)
                {
                    m_Renderers.Add(new RendererEntry(renderer, drawCallInitialCount, m_DrawCalls.Count));
                }
            }
            catch
            {
                m_DrawCalls.RemoveRange(drawCallInitialCount, m_DrawCalls.Count - drawCallInitialCount);
                throw;
            }
            finally
            {
                ListPool<Material>.Release(materialList);
            }
        }

        private static bool TryGetShadowCasterPass(Material material, out int passIndex)
        {
            Shader shader = material.shader;
            for (int i = 0; i < shader.passCount; i++)
            {
                if (shader.FindPassTagValue(i, ShaderTagIds.LightMode) == ShaderTagIds.ShadowCaster)
                {
                    passIndex = i;
                    return true;
                }
            }

            passIndex = -1;
            return false;
        }

        public ReadOnly AsReadOnly()
        {
            return new ReadOnly(this);
        }

        public readonly struct ReadOnly
        {
            private readonly ShadowRendererList m_List;

            public float Priority { get; }

            internal ReadOnly(ShadowRendererList list)
            {
                m_List = list;
                Priority = list.Priority;
            }

            public bool TryGetWorldBounds(out Bounds worldBounds, ICollection<int> outAppendRendererIndices = null)
            {
                return m_List.TryGetWorldBounds(out worldBounds, outAppendRendererIndices);
            }

            public void Draw(CommandBuffer cmd, int rendererIndex)
            {
                m_List.Draw(cmd, rendererIndex);
            }
            
#if UNITY_2023_1_OR_NEWER
            public void Draw(RasterCommandBuffer cmd, int rendererIndex)
            {
                m_List.Draw(cmd, rendererIndex);
            }
#endif
        }

        private static class ShaderTagIds
        {
            public static readonly ShaderTagId LightMode = MemberNameHelpers.ShaderTagId();

            public static readonly ShaderTagId ShadowCaster = MemberNameHelpers.ShaderTagId();
        }
    }
}
