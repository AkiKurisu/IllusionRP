using UnityEngine;
using UnityEngine.Experimental.Rendering;
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

        private readonly RenderTexture[] _preIntegratedFgd = new RenderTexture[(int)FGDIndex.Count];

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

        public void Build(FGDIndex index)
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
                        _preIntegratedFgd[(int)index] = new RenderTexture(res, res, 0, GraphicsFormat.A2B10G10R10_UNormPack32)
                        {
                            hideFlags = HideFlags.HideAndDontSave,
                            filterMode = FilterMode.Bilinear,
                            wrapMode = TextureWrapMode.Clamp,
                            name = CoreUtils.GetRenderTargetAutoName(res, res, 1, GraphicsFormat.A2B10G10R10_UNormPack32, "PreIntegratedFGD_GGXDisneyDiffuse")
                        };
                        _preIntegratedFgd[(int)index].Create();
                        break;

                    case FGDIndex.FGD_CharlieAndFabricLambert:
                        _preIntegratedFGDMaterial[(int)index] = CoreUtils.CreateEngineMaterial(_renderPipelineResources.preIntegratedFGD_CharlieFabricLambertShader);
                        _preIntegratedFgd[(int)index] = new RenderTexture(res, res, 0, GraphicsFormat.A2B10G10R10_UNormPack32)
                        {
                            hideFlags = HideFlags.HideAndDontSave,
                            filterMode = FilterMode.Bilinear,
                            wrapMode = TextureWrapMode.Clamp,
                            name = CoreUtils.GetRenderTargetAutoName(res, res, 1, GraphicsFormat.A2B10G10R10_UNormPack32, "PreIntegratedFGD_CharlieFabricLambert")
                        };
                        _preIntegratedFgd[(int)index].Create();
                        break;
                }

                _isInit[(int)index] = false;
            }

            _refCounting[(int)index]++;
        }

        public void RenderInit(CommandBuffer cmd, FGDIndex index)
        {
            // Here we have to test IsCreated because in some circumstances (like loading RenderDoc), the texture is internally destroyed but we don't know from C# side.
            // In this case IsCreated will return false, allowing us to re-render the texture (setting the texture as current RT during DrawFullScreen will automatically re-create it internally)
            if (_isInit[(int)index] && _preIntegratedFgd[(int)index].IsCreated())
                return;

            // If we are in wireframe mode, the drawfullscreen will not work as expected, but we don't need the LUT anyway
            // So create the texture to avoid errors, it will be initialized by the first render without wireframe
            if (GL.wireframe)
            {
                _preIntegratedFgd[(int)index].Create();
                return;
            }

            CoreUtils.DrawFullScreen(cmd, _preIntegratedFGDMaterial[(int)index], new RenderTargetIdentifier(_preIntegratedFgd[(int)index]));
            _isInit[(int)index] = true;
        }

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
    }
}
