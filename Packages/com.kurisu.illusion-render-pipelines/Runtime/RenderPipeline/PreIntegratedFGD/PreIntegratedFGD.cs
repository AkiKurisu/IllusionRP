using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

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

        public bool IsInit(FGDIndex index)
        {
            return _isInit[(int)index];
        }
        
        public void RenderInit(UnsafeCommandBuffer cmd, TextureHandle textureHandle, FGDIndex index)
        {
            if (_isInit[(int)index])
                return;

            cmd.SetRenderTarget(textureHandle, 0, CubemapFace.Unknown, -1);
            cmd.DrawProcedural(Matrix4x4.identity, _preIntegratedFGDMaterial[(int)index], 0, MeshTopology.Triangles, 3, 1, null);
            _isInit[(int)index] = true;
        }
        
        public void Bind(UnsafeCommandBuffer cmd, TextureHandle textureHandle, FGDIndex index)
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

        public void Cleanup(FGDIndex index)
        {
            _refCounting[(int)index]--;

            if (_refCounting[(int)index] == 0)
            {
                CoreUtils.Destroy(_preIntegratedFGDMaterial[(int)index]);
                _preIntegratedFgd[(int)index].Release();

                _isInit[(int)index] = false;
            }

            Debug.Assert(_refCounting[(int)index] >= 0);
        }
    }
}
