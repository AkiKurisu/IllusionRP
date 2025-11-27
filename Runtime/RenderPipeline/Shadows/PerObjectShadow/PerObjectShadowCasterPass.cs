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

using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering.Shadows
{
    public class PerObjectShadowCasterPass : ScriptableRenderPass, IDisposable
    {
        public const int MaxShadowCount = 16;

        private readonly Matrix4x4[] _shadowMatrixArray;

        private readonly Vector4[] _shadowMapRectArray;

        private readonly float[] _shadowCasterIdArray;

        private ShadowCasterManager _casterManager;

        private int _tileResolution;

        private int _shadowMapSizeInTile; // 一行/一列有多少个 tile

        private RTHandle _shadowMap;

        private UniversalAdditionalLightData _mainLightData;

        // Per-object shadow PCSS parameters
        private readonly Vector4[] _perObjShadowPcssParams0;
        private readonly Vector4[] _perObjShadowPcssParams1;
        private readonly Vector4[] _perObjShadowPcssProjs;
        private readonly Vector4[] _perObjShadowBiases;

        private readonly IllusionRendererData _rendererData;

        public PerObjectShadowCasterPass(IllusionRendererData rendererData)
        {
            _rendererData = rendererData;
            renderPassEvent = IllusionRenderPassEvent.PerObjectShadowCasterPass;
            profilingSampler = new ProfilingSampler("MainLightPerObjectSceneShadow");

            _shadowMatrixArray = new Matrix4x4[MaxShadowCount];
            _shadowMapRectArray = new Vector4[MaxShadowCount];
            _shadowCasterIdArray = new float[MaxShadowCount];
            _perObjShadowBiases = new Vector4[MaxShadowCount];

            // Initialize PCSS parameter arrays
            _perObjShadowPcssParams0 = new Vector4[MaxShadowCount];
            _perObjShadowPcssParams1 = new Vector4[MaxShadowCount];
            _perObjShadowPcssProjs = new Vector4[MaxShadowCount];
        }

        public void Dispose()
        {
            _shadowMap?.Release();
        }

        public void Setup(ShadowCasterManager casterManager, ShadowTileResolution tileResolution, DepthBits depthBits)
        {
            _casterManager = casterManager;
            _tileResolution = (int)tileResolution;

            if (casterManager.VisibleCount <= 0)
            {
                return;
            }

            // 保证 shadow map 是正方形
            _shadowMapSizeInTile = Mathf.CeilToInt(Mathf.Sqrt(casterManager.VisibleCount));
            int shadowRTSize = _shadowMapSizeInTile * _tileResolution;
            int shadowRTDepthBits = Mathf.Max((int)depthBits, (int)DepthBits.Depth8);
            ShadowUtils.ShadowRTReAllocateIfNeeded(ref _shadowMap, shadowRTSize, shadowRTSize,
                shadowRTDepthBits, name: "_MainLightPerObjectShadow");

            ConfigureTarget(_shadowMap);
            ConfigureClear(ClearFlag.All, Color.black);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get();

            using (new ProfilingScope(cmd, profilingSampler))
            {
                if (_casterManager.VisibleCount > 0)
                {
                    RenderShadowMap(cmd, ref renderingData);
                    SetShadowSamplingData(cmd);
                    SetPerObjectShadowPCSSData(cmd, ref renderingData);
                }
                else
                {
                    cmd.SetGlobalInt(PropertyIds.ShadowCount(), 0);
                }
            }
            // Reset matrices
            cmd.SetViewProjectionMatrices(renderingData.cameraData.GetViewMatrix(), renderingData.cameraData.GetProjectionMatrix());
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        private void RenderShadowMap(CommandBuffer cmd, ref RenderingData renderingData)
        {
            cmd.SetGlobalDepthBias(1.0f, 2.5f); // these values match HDRP defaults (see https://github.com/Unity-Technologies/Graphics/blob/9544b8ed2f98c62803d285096c91b44e9d8cbc47/com.unity.render-pipelines.high-definition/Runtime/Lighting/Shadow/HDShadowAtlas.cs#L197 )
            CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.CastingPunctualLightShadow, false);

            for (int i = 0; i < _casterManager.VisibleCount; i++)
            {
                _casterManager.GetMatrices(i, out Matrix4x4 viewMatrix, out Matrix4x4 projectionMatrix);

                int mainLightIndex = renderingData.lightData.mainLightIndex;
                VisibleLight mainLight = renderingData.lightData.visibleLights[mainLightIndex];
                Vector4 shadowBias = ShadowUtils.GetShadowBias(ref mainLight, mainLightIndex,
                    ref renderingData.shadowData, projectionMatrix, _shadowMap.rt.width);
                _perObjShadowBiases[i] = shadowBias;
                ShadowUtils.SetupShadowCasterConstantBuffer(cmd, ref mainLight, shadowBias);

                Vector2Int tilePos = new(i % _shadowMapSizeInTile, i / _shadowMapSizeInTile);
                DrawShadow(cmd, i, tilePos, in viewMatrix, in projectionMatrix);
                _shadowMatrixArray[i] = GetShadowMatrix(tilePos, in viewMatrix, projectionMatrix);
                _shadowMapRectArray[i] = GetShadowMapRect(tilePos);
                _shadowCasterIdArray[i] = _casterManager.GetId(i);
            }

            cmd.SetGlobalDepthBias(0.0f, 0.0f); // Restore previous depth bias values
            CoreUtils.SetKeyword(cmd, KeywordNames._CASTING_SELF_SHADOW, false);

            cmd.SetGlobalTexture(PropertyIds.ShadowMap(), _shadowMap);
            cmd.SetGlobalInt(PropertyIds.ShadowCount(), _casterManager.VisibleCount);
            cmd.SetGlobalMatrixArray(PropertyIds.ShadowMatrices(), _shadowMatrixArray);
            cmd.SetGlobalVectorArray(PropertyIds.ShadowMapRects(), _shadowMapRectArray);
            cmd.SetGlobalVectorArray(PropertyIds.ShadowBiases(), _perObjShadowBiases);
            cmd.SetGlobalFloatArray(PropertyIds.ShadowCasterIds(), _shadowCasterIdArray);
        }

        private void SetShadowSamplingData(CommandBuffer cmd)
        {
            int renderTargetWidth = _shadowMap.rt.width;
            int renderTargetHeight = _shadowMap.rt.height;
            float invShadowAtlasWidth = 1.0f / renderTargetWidth;
            float invShadowAtlasHeight = 1.0f / renderTargetHeight;
            float invHalfShadowAtlasWidth = 0.5f * invShadowAtlasWidth;
            float invHalfShadowAtlasHeight = 0.5f * invShadowAtlasHeight;

            cmd.SetGlobalVector(PropertyIds.ShadowOffset0(),
                new Vector4(-invHalfShadowAtlasWidth, -invHalfShadowAtlasHeight, invHalfShadowAtlasWidth, -invHalfShadowAtlasHeight));
            cmd.SetGlobalVector(PropertyIds.ShadowOffset1(),
                new Vector4(-invHalfShadowAtlasWidth, invHalfShadowAtlasHeight, invHalfShadowAtlasWidth, invHalfShadowAtlasHeight));
            cmd.SetGlobalVector(PropertyIds.ShadowMapSize(),
                new Vector4(invShadowAtlasWidth, invShadowAtlasHeight, renderTargetWidth, renderTargetHeight));
        }

        private void SetPerObjectShadowPCSSData(CommandBuffer cmd, ref RenderingData renderingData)
        {
            if (!_rendererData.PCSSShadowSampling)
            {
                return;
            }

            // Get PCSS parameters from renderer data
            var pcssParams = VolumeManager.instance.stack.GetComponent<PercentageCloserSoftShadows>();
            float lightAngularDiameter = pcssParams.angularDiameter.value;
            float dirlightDepth2Radius = Mathf.Tan(0.5f * Mathf.Deg2Rad * lightAngularDiameter);
            float minFilterAngularDiameter =
                Mathf.Max(pcssParams.blockerSearchAngularDiameter.value, pcssParams.minFilterMaxAngularDiameter.value);
            float halfMinFilterAngularDiameterTangent =
                Mathf.Tan(0.5f * Mathf.Deg2Rad * Mathf.Max(minFilterAngularDiameter, lightAngularDiameter));

            float halfBlockerSearchAngularDiameterTangent =
                Mathf.Tan(0.5f * Mathf.Deg2Rad * Mathf.Max(pcssParams.blockerSearchAngularDiameter.value, lightAngularDiameter));

            for (int i = 0; i < _casterManager.VisibleCount; i++)
            {
                _casterManager.GetMatrices(i, out _, out Matrix4x4 projectionMatrix);

                // Calculate shadowmap depth to radial scale for per-object shadow
                float shadowmapDepth2RadialScale = Mathf.Abs(projectionMatrix.m00 / projectionMatrix.m22);

                // PCSS Parameters 0
                _perObjShadowPcssParams0[i].x = dirlightDepth2Radius * shadowmapDepth2RadialScale; // depth2RadialScale
                _perObjShadowPcssParams0[i].y = 1.0f / _perObjShadowPcssParams0[i].x; // radial2DepthScale
                _perObjShadowPcssParams0[i].z = pcssParams.maxPenumbraSize.value / (2.0f * halfMinFilterAngularDiameterTangent); // maxBlockerDistance
                _perObjShadowPcssParams0[i].w = pcssParams.maxSamplingDistance.value; // maxSamplingDistance

                // PCSS Parameters 1
                _perObjShadowPcssParams1[i].x = pcssParams.minFilterSizeTexels.value; // minFilterRadius(in texels)
                _perObjShadowPcssParams1[i].y = 1.0f / (halfMinFilterAngularDiameterTangent * shadowmapDepth2RadialScale); // minFilterRadial2DepthScale
                _perObjShadowPcssParams1[i].z = 1.0f / (halfBlockerSearchAngularDiameterTangent * shadowmapDepth2RadialScale); // blockerRadial2DepthScale
                _perObjShadowPcssParams1[i].w = 0; // unused

                // Projection parameters
                _perObjShadowPcssProjs[i] = new Vector4(projectionMatrix.m00, projectionMatrix.m11, projectionMatrix.m22, projectionMatrix.m23);
            }

            // Set global shader properties
            cmd.SetGlobalVectorArray(PropertyIds._PerObjShadowPcssParams0, _perObjShadowPcssParams0);
            cmd.SetGlobalVectorArray(PropertyIds._PerObjShadowPcssParams1, _perObjShadowPcssParams1);
            cmd.SetGlobalVectorArray(PropertyIds._PerObjShadowPcssProjs, _perObjShadowPcssProjs);
        }

        private void DrawShadow(CommandBuffer cmd, int casterIndex, Vector2Int tilePos, in Matrix4x4 view, in Matrix4x4 proj)
        {
            cmd.SetViewProjectionMatrices(view, proj);

            Rect viewport = new(tilePos * _tileResolution, new Vector2(_tileResolution, _tileResolution));
            cmd.SetViewport(viewport);

            cmd.EnableScissorRect(new Rect(viewport.x + 4, viewport.y + 4, viewport.width - 8, viewport.height - 8));
            _casterManager.Draw(cmd, casterIndex);
            cmd.DisableScissorRect();
        }

        private Matrix4x4 GetShadowMatrix(Vector2Int tilePos, in Matrix4x4 viewMatrix, Matrix4x4 projectionMatrix)
        {
            if (SystemInfo.usesReversedZBuffer)
            {
                projectionMatrix.m20 = -projectionMatrix.m20;
                projectionMatrix.m21 = -projectionMatrix.m21;
                projectionMatrix.m22 = -projectionMatrix.m22;
                projectionMatrix.m23 = -projectionMatrix.m23;
            }

            float oneOverTileCount = 1.0f / _shadowMapSizeInTile;

            Matrix4x4 textureScaleAndBias = Matrix4x4.identity;
            textureScaleAndBias.m00 = 0.5f * oneOverTileCount;
            textureScaleAndBias.m11 = 0.5f * oneOverTileCount;
            textureScaleAndBias.m22 = 0.5f;
            textureScaleAndBias.m03 = (0.5f + tilePos.x) * oneOverTileCount;
            textureScaleAndBias.m13 = (0.5f + tilePos.y) * oneOverTileCount;
            textureScaleAndBias.m23 = 0.5f;

            // Apply texture scale and offset to save a MAD in shader.
            return textureScaleAndBias * projectionMatrix * viewMatrix;
        }

        private Vector4 GetShadowMapRect(Vector2Int tilePos)
        {
            // x: xMin
            // y: xMax
            // z: yMin
            // w: yMax
            return new Vector4(tilePos.x, 1 + tilePos.x, tilePos.y, 1 + tilePos.y) / _shadowMapSizeInTile;
        }

        private static class KeywordNames
        {
            public static readonly string _CASTING_SELF_SHADOW = MemberNameHelpers.String();
        }

        internal static class PropertyIds
        {
            private static readonly int _PerObjSceneShadowMap = MemberNameHelpers.ShaderPropertyID();

            private static readonly int _PerObjSceneShadowCount = MemberNameHelpers.ShaderPropertyID();

            private static readonly int _PerObjSceneShadowMatrices = MemberNameHelpers.ShaderPropertyID();

            private static readonly int _PerObjSceneShadowMapRects = MemberNameHelpers.ShaderPropertyID();

            private static readonly int _PerObjSceneShadowCasterIds = MemberNameHelpers.ShaderPropertyID();

            private static readonly int _PerObjSceneShadowOffset0 = MemberNameHelpers.ShaderPropertyID();

            private static readonly int _PerObjSceneShadowOffset1 = MemberNameHelpers.ShaderPropertyID();

            private static readonly int _PerObjSceneShadowMapSize = MemberNameHelpers.ShaderPropertyID();

            // Per-object shadow PCSS parameters
            public static readonly int _PerObjShadowPcssParams0 = MemberNameHelpers.ShaderPropertyID();
            
            public static readonly int _PerObjShadowPcssParams1 = MemberNameHelpers.ShaderPropertyID();
            
            public static readonly int _PerObjShadowPcssProjs = MemberNameHelpers.ShaderPropertyID();
                        
            private static readonly int _PerObjShadowBiases = MemberNameHelpers.ShaderPropertyID();

            public static int ShadowMap() => _PerObjSceneShadowMap;

            public static int ShadowCount() => _PerObjSceneShadowCount;

            public static int ShadowMatrices() => _PerObjSceneShadowMatrices;

            public static int ShadowMapRects() => _PerObjSceneShadowMapRects;

            public static int ShadowBiases() => _PerObjShadowBiases;

            public static int ShadowCasterIds() => _PerObjSceneShadowCasterIds;

            public static int ShadowOffset0() => _PerObjSceneShadowOffset0;

            public static int ShadowOffset1() => _PerObjSceneShadowOffset1;

            public static int ShadowMapSize() => _PerObjSceneShadowMapSize;
        }
    }
}
