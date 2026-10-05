using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace Illusion.Rendering.PRTGI
{
    public partial class PRTRelightPass
    {
        private bool _residualPending;
        private uint _residualFrame;
        private sealed class ResidualReadback
        {
            public GraphicsBuffer buffer;
            public PRTRelightPass owner;
            public PRTRelightSolver solver;
        }

        private void RecordResidualDiagnostics(RenderGraph graph)
        {
            if (!_volume.enableShadowCacheStats || _residualPending ||
                PRTRelightFrame.Index - _residualFrame < (uint)Mathf.Max(1, _volume.shadowCacheStatsReadbackInterval)) return;
            _residualFrame = PRTRelightFrame.Index;
            _residualPending = true;
            var maximum = Vector4.zero;
            int remaining = _selected.Count;
            foreach (var sector in _selected)
            {
                using var builder = graph.AddUnsafePass<ResidualReadback>($"PRT sector {sector.Index} residual diagnostics", out var pass);
                pass.owner = this; pass.solver = _solver; pass.buffer = sector.Residuals;
                builder.UseBuffer(graph.ImportBuffer(pass.buffer), AccessFlags.Read);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc((ResidualReadback data, UnsafeGraphContext context) =>
                    CommandBufferHelpers.GetNativeCommandBuffer(context.cmd).RequestAsyncReadback(data.buffer, request =>
                    {
                        if (!request.hasError)
                            foreach (var value in request.GetData<Vector4>())
                            {
                                maximum.x = Mathf.Max(maximum.x, value.x);
                                maximum.y = Mathf.Max(maximum.y, value.y);
                                maximum.w += value.w;
                            }
                        else maximum.x = maximum.y = float.NaN;
                        if (--remaining != 0) return;
                        data.owner._residualPending = false;
                        if (data.solver.Disposed) return;
                        data.solver.Residual = maximum.x;
                        data.solver.AbsoluteResidual = maximum.y;
                        data.solver.NonFiniteCount = (int)maximum.w;
                    }));
            }
        }
    }
}
