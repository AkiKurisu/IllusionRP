#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Illusion.Rendering
{
    internal class MotionVectorsDebugPass : ScriptableRenderPass, IDisposable
    {
        private readonly LazyMaterial _motionVectorDebugMaterial = new(IllusionShaders.DebugMotionVectors);
        
        public MotionVectorsDebugPass(IllusionRendererData rendererData)
        {
            profilingSampler = new ProfilingSampler("Motion Vectors Debug");
            renderPassEvent = IllusionRenderPassEvent.MotionVectorDebugPass;
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            ref var cameraData = ref renderingData.cameraData;
            var motionVectorColorRT = UniversalRenderingUtility.GetMotionVectorColor(cameraData.renderer);
            if (!motionVectorColorRT.IsValid()) return;
            var colorRT = cameraData.renderer.cameraColorTargetHandle;
            var material = _motionVectorDebugMaterial.Value;
            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, profilingSampler))
            {
                cmd.SetRenderTarget(colorRT);
                material.SetTexture(IllusionShaderProperties._MotionVectorTexture, motionVectorColorRT);
                cmd.Blit( colorRT, colorRT, material);
            }
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public void Dispose()
        {
            _motionVectorDebugMaterial.DestroyCache();
        }
    }
}
#endif