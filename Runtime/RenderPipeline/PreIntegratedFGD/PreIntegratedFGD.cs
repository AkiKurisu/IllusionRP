using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

namespace Illusion.Rendering
{
    // Reference: UnityEngine.Rendering.HighDefinition.PreIntegratedFGD
    public sealed class PreIntegratedFGD
    {
        private const int FGDTextureResolution = 64;

        public enum FGDIndex
        {
            FGD_GGXAndDisneyDiffuse = 0,
            FGD_CharlieAndFabricLambert = 1,
            Count = 2
        }

        private readonly bool[] _isInit = new bool[(int)FGDIndex.Count];

        private readonly int[] _refCounting = new int[(int)FGDIndex.Count];

        private readonly Material[] _preIntegratedFGDMaterial = new Material[(int)FGDIndex.Count];

        private readonly RTHandle[] _preIntegratedFgd = new RTHandle[(int)FGDIndex.Count];

        private readonly IllusionRenderPipelineResources _renderPipelineResources;

        public PreIntegratedFGD(IllusionRenderPipelineResources renderPipelineResources)
        {
            _renderPipelineResources = renderPipelineResources;
            for (int i = 0; i < (int)FGDIndex.Count; ++i)
            {
                _isInit[i] = false;
                _refCounting[i] = 0;
            }
        }

        public RTHandle Build(FGDIndex index)
        {
            Debug.Assert(index != FGDIndex.Count);
            Debug.Assert(_refCounting[(int)index] >= 0);

            if (_refCounting[(int)index] == 0)
            {
                int res = FGDTextureResolution;

                switch (index)
                {
                    case FGDIndex.FGD_GGXAndDisneyDiffuse:
                        _preIntegratedFGDMaterial[(int)index] = CoreUtils.CreateEngineMaterial(_renderPipelineResources.preIntegratedFGD_GGXDisneyDiffuseShader);
                        _preIntegratedFgd[(int)index] = RTHandles.Alloc(
                            res,
                            res,
                            slices: 1,
                            dimension: TextureDimension.Tex2D,
                            colorFormat: GraphicsFormat.A2B10G10R10_UNormPack32,
                            filterMode: FilterMode.Bilinear,
                            wrapMode: TextureWrapMode.Clamp,
                            name: "PreIntegratedFGD_GGXDisneyDiffuse"
                        );
                        break;

                    case FGDIndex.FGD_CharlieAndFabricLambert:
                        _preIntegratedFGDMaterial[(int)index] = CoreUtils.CreateEngineMaterial(_renderPipelineResources.preIntegratedFGD_CharlieFabricLambertShader);
                        _preIntegratedFgd[(int)index] = RTHandles.Alloc(
                            res,
                            res,
                            slices: 1,
                            dimension: TextureDimension.Tex2D,
                            colorFormat: GraphicsFormat.A2B10G10R10_UNormPack32,
                            filterMode: FilterMode.Bilinear,
                            wrapMode: TextureWrapMode.Clamp,
                            name: "PreIntegratedFGD_CharlieFabricLambert"
                        );
                        break;
                }

                _isInit[(int)index] = false;
            }

            _refCounting[(int)index]++;
            return _preIntegratedFgd[(int)index];
        }

        public void RenderInit(CommandBuffer cmd, FGDIndex index)
        {
            CoreUtils.DrawFullScreen(cmd, _preIntegratedFGDMaterial[(int)index], _preIntegratedFgd[(int)index]);
            _isInit[(int)index] = true;
        }

        public void Bind(CommandBuffer cmd, FGDIndex index)
        {
            switch (index)
            {
                case FGDIndex.FGD_GGXAndDisneyDiffuse:
                    cmd.SetGlobalTexture(IllusionShaderProperties._PreIntegratedFGD_GGXDisneyDiffuse, _preIntegratedFgd[(int)index]);
                    break;

                case FGDIndex.FGD_CharlieAndFabricLambert:
                    cmd.SetGlobalTexture(IllusionShaderProperties._PreIntegratedFGD_CharlieAndFabric, _preIntegratedFgd[(int)index]);
                    break;
                case FGDIndex.Count:
                default:
                    break;
            }
        }
        
#if UNITY_2023_1_OR_NEWER
        public bool IsInit(FGDIndex index)
        {
            return _isInit[(int)index];
        }
        
        public void RenderInit(LowLevelCommandBuffer cmd, TextureHandle textureHandle, FGDIndex index)
        {
            cmd.SetRenderTarget(textureHandle, 0, CubemapFace.Unknown, -1);
            cmd.DrawProcedural(Matrix4x4.identity, _preIntegratedFGDMaterial[(int)index], 0, MeshTopology.Triangles, 3, 1, null);
            _isInit[(int)index] = true;
        }
        
        public void Bind(LowLevelCommandBuffer cmd, TextureHandle textureHandle, FGDIndex index)
        {
            switch (index)
            {
                case FGDIndex.FGD_GGXAndDisneyDiffuse:
                    cmd.SetGlobalTexture(IllusionShaderProperties._PreIntegratedFGD_GGXDisneyDiffuse, textureHandle);
                    break;

                case FGDIndex.FGD_CharlieAndFabricLambert:
                    cmd.SetGlobalTexture(IllusionShaderProperties._PreIntegratedFGD_CharlieAndFabric, textureHandle);
                    break;
                case FGDIndex.Count:
                default:
                    break;
            }
        }
#endif

        public void Cleanup(FGDIndex index)
        {
            _refCounting[(int)index]--;

            if (_refCounting[(int)index] == 0)
            {
                CoreUtils.Destroy(_preIntegratedFGDMaterial[(int)index]);
                CoreUtils.Destroy(_preIntegratedFgd[(int)index]);

                _isInit[(int)index] = false;
            }

            Debug.Assert(_refCounting[(int)index] >= 0);
        }
    }
}
