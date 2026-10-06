using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace Illusion.Rendering.PRTGI
{
    internal sealed class PRTNeutralPublication : IDisposable
    {
        private readonly RTHandle _coefficients;
        private readonly GraphicsBuffer _layout;
        private readonly ComputeShader _shader;
        private readonly int _clearKernel;
        private bool _initialized;

        internal PRTNeutralPublication(ComputeShader shader)
        {
            _shader = shader;
            _clearKernel = shader.FindKernel("CSClearPublication");
            _coefficients = RTHandles.Alloc(1, 1, slices: 9, dimension: TextureDimension.Tex3D,
                colorFormat: GraphicsFormat.R16G16B16A16_SFloat, enableRandomWrite: true, filterMode: FilterMode.Point,
                wrapMode: TextureWrapMode.Clamp, name: "PRT neutral SH");
            _layout = PRTLayoutConstants.Allocate();
            _layout.SetData(new PRTLayoutConstants[1]);
        }

        private sealed class ClearData { internal PRTNeutralPublication Owner; }
        private sealed class NeutralData { internal GraphicsBuffer Layout; }

        internal void Record(RenderGraph graph, ContextContainer frameData)
        {
            var bindings = new PRTShaderBindings
            {
                Coefficients = graph.ImportTexture(_coefficients),
                Layout = graph.ImportBuffer(_layout), LayoutBuffer = _layout
            };
            if (!_initialized)
            {
                using var clear = graph.AddUnsafePass<ClearData>("PRT initialize neutral UAVs", out var data);
                data.Owner = this;
                clear.UseTexture(bindings.Coefficients, AccessFlags.Write);
                clear.AllowPassCulling(false);
                clear.SetRenderFunc(static (ClearData pass, UnsafeGraphContext context) =>
                {
                    var command = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    var owner = pass.Owner;
                    command.SetComputeTextureParam(owner._shader, owner._clearKernel, "_coefficientVoxel3D", owner._coefficients.rt);
                    command.DispatchCompute(owner._shader, owner._clearKernel, 1, 1, 1);
                    owner._initialized = true;
                });
            }
            frameData.GetOrCreate<PRTShaderResources>().Bindings = bindings;
            using var builder = graph.AddUnsafePass<NeutralData>("PRT empty camera publication", out var bind);
            bind.Layout = _layout;
            bindings.DeclareReads(builder);
            builder.SetGlobalTextureAfterPass(bindings.Coefficients, Shader.PropertyToID("_coefficientVoxel3D"));
            builder.AllowGlobalStateModification(true);
            builder.AllowPassCulling(false);
            builder.SetRenderFunc(static (NeutralData pass, UnsafeGraphContext context) =>
            {
                var command = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                command.SetGlobalConstantBuffer(pass.Layout, PRTLayoutConstants.ShaderId, 0, PRTLayoutConstants.Stride);
                command.SetGlobalFloat("_coefficientVoxelGridSize", 0);
            });
        }

        public void Dispose()
        {
            _coefficients.Release();
            _layout.Dispose();
        }
    }
}
