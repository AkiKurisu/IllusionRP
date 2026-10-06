using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace Illusion.Rendering.PRTGI
{
    internal struct PRTShaderBindings
    {
        internal TextureHandle Coefficients;
        internal BufferHandle Layout;
        internal GraphicsBuffer LayoutBuffer;

        internal void DeclareReads(IBaseRenderGraphBuilder builder)
        {
            builder.UseTexture(Coefficients, AccessFlags.Read);
            builder.UseBuffer(Layout, AccessFlags.Read);
        }

        internal void Bind(ComputeCommandBuffer command, ComputeShader shader, int kernel)
        {
            command.SetComputeTextureParam(shader, kernel, Shader.PropertyToID("_coefficientVoxel3D"), Coefficients);
            command.SetComputeConstantBufferParam(shader, PRTLayoutConstants.ShaderId, LayoutBuffer, 0, PRTLayoutConstants.Stride);
        }
    }

    internal sealed class PRTShaderResources : ContextItem
    {
        internal PRTShaderBindings Bindings;
        public override void Reset() => Bindings = default;
    }
}
