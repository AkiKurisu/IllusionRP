using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace Illusion.Rendering.PRTGI
{
    internal sealed class PRTNeutralPublication : IDisposable
    {
        private readonly RTHandle _coefficients, _metadata;
        private readonly GraphicsBuffer _layout;
        private readonly ComputeShader _shader;
        private readonly int _clearKernel;
        private bool _initialized;

        internal PRTNeutralPublication(ComputeShader shader)
        {
            _shader = shader;
            _clearKernel = shader.FindKernel("CSClearPublication");
            _coefficients = Allocate(9, GraphicsFormat.R32G32B32A32_SFloat, "PRT neutral SH");
            _metadata = Allocate(1, GraphicsFormat.R32_UInt, "PRT neutral metadata");
            _layout = PRTLayoutConstants.Allocate();
            _layout.SetData(new PRTLayoutConstants[1]);
        }

        private static RTHandle Allocate(int depth, GraphicsFormat format, string name) => RTHandles.Alloc(
            1, 1, slices: depth, dimension: TextureDimension.Tex3D, colorFormat: format,
            enableRandomWrite: true, filterMode: FilterMode.Point, wrapMode: TextureWrapMode.Clamp, name: name);

        private sealed class ClearData { internal PRTNeutralPublication Owner; }
        private sealed class NeutralData { internal GraphicsBuffer Layout; }

        internal void Record(RenderGraph graph, ContextContainer frameData)
        {
            var bindings = new PRTShaderBindings
            {
                Coefficients = graph.ImportTexture(_coefficients),
                Metadata = graph.ImportTexture(_metadata),
                Layout = graph.ImportBuffer(_layout), LayoutBuffer = _layout
            };
            if (!_initialized)
            {
                using var clear = graph.AddUnsafePass<ClearData>("PRT initialize neutral UAVs", out var data);
                data.Owner = this;
                clear.UseTexture(bindings.Coefficients, AccessFlags.Write);
                clear.UseTexture(bindings.Metadata, AccessFlags.Write);
                clear.AllowPassCulling(false);
                clear.SetRenderFunc(static (ClearData pass, UnsafeGraphContext context) =>
                {
                    var command = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    var owner = pass.Owner;
                    command.SetComputeTextureParam(owner._shader, owner._clearKernel, "_coefficientVoxel3D", owner._coefficients.rt);
                    command.SetComputeTextureParam(owner._shader, owner._clearKernel, "_validityVoxel3D", owner._metadata.rt);
                    command.DispatchCompute(owner._shader, owner._clearKernel, 1, 1, 1);
                    owner._initialized = true;
                });
            }
            frameData.GetOrCreate<PRTShaderResources>().Bindings = bindings;
            using var builder = graph.AddUnsafePass<NeutralData>("PRT empty camera publication", out var bind);
            bind.Layout = _layout;
            bindings.DeclareReads(builder);
            builder.SetGlobalTextureAfterPass(bindings.Coefficients, Shader.PropertyToID("_coefficientVoxel3D"));
            builder.SetGlobalTextureAfterPass(bindings.Metadata, Shader.PropertyToID("_validityVoxel3D"));
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
            _metadata.Release();
            _layout.Dispose();
        }
    }
}
