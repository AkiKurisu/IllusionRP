using UnityEngine.Rendering;

namespace Illusion.Rendering.PathTracing
{
    internal static class PathTracingFrame
    {
        public static int Index { get; private set; }

        static PathTracingFrame()
        {
            RenderPipelineManager.beginContextRendering += (_, _) => Index++;
        }
    }
}
