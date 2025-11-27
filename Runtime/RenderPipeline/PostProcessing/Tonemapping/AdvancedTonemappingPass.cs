using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering.PostProcessing
{
    public class AdvancedTonemappingPass : ScriptableRenderPass
    {
        public AdvancedTonemappingPass()
        {
            profilingSampler = new ProfilingSampler("Advanced Tonemapping");
            renderPassEvent = RenderPassEvent.BeforeRenderingPrePasses - 1;
        }
        
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var volume = VolumeManager.instance.stack.GetComponent<AdvancedTonemapping>();
            
            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, profilingSampler))
            {
                if (volume.IsActive())
                {
                    // Set material parameters
                    Vector4 param = new Vector4(volume.maxBrightness.value, volume.contrast.value, 
                        volume.linearSectionStart.value, volume.linearSectionLength.value);
                    cmd.SetGlobalVector(ShaderIds._TonemappingParam, param);
                    cmd.SetGlobalFloat(ShaderIds._FilmSlope, volume.slope.value);
                    cmd.SetGlobalFloat(ShaderIds._FilmToe, volume.toe.value);
                    cmd.SetGlobalFloat(ShaderIds._FilmShoulder, volume.shoulder.value);
                    cmd.SetGlobalFloat(ShaderIds._FilmBlackClip, volume.blackClip.value);
                    cmd.SetGlobalFloat(ShaderIds._FilmWhiteClip, volume.whiteClip.value);
                }

                switch (volume.mode.value)
                {
                    case AdvancedTonemappingMode.Filmic_ACES:
                        CoreUtils.SetKeyword(cmd, ShaderKeywordStrings._TONEMAP_ACES_FILMIC, true);
                        CoreUtils.SetKeyword(cmd, ShaderKeywordStrings._TONEMAP_GT, false);
                        break;
                    case AdvancedTonemappingMode.GranTurismo:
                        CoreUtils.SetKeyword(cmd, ShaderKeywordStrings._TONEMAP_ACES_FILMIC, false);
                        CoreUtils.SetKeyword(cmd, ShaderKeywordStrings._TONEMAP_GT, true);
                        break;
                    case AdvancedTonemappingMode.None:
                        CoreUtils.SetKeyword(cmd, ShaderKeywordStrings._TONEMAP_ACES_FILMIC, false);
                        CoreUtils.SetKeyword(cmd, ShaderKeywordStrings._TONEMAP_GT, false);
                        break;
                }
            }
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            CommandBufferPool.Release(cmd);
        }

        private static class ShaderKeywordStrings
        {
            public static readonly string _TONEMAP_ACES_FILMIC = MemberNameHelpers.String();
            
            public static readonly string _TONEMAP_GT = MemberNameHelpers.String();
        }

        private static class ShaderIds
        {
            public static readonly int _TonemappingParam = MemberNameHelpers.ShaderPropertyID();

            public static readonly int _FilmSlope = MemberNameHelpers.ShaderPropertyID();
            
            public static readonly int _FilmToe = MemberNameHelpers.ShaderPropertyID();
            
            public static readonly int _FilmShoulder = MemberNameHelpers.ShaderPropertyID();
            
            public static readonly int _FilmBlackClip = MemberNameHelpers.ShaderPropertyID();
            
            public static readonly int _FilmWhiteClip = MemberNameHelpers.ShaderPropertyID();
        }
    }
}