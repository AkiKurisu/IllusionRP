using System.Collections.Generic;
using Illusion.Rendering.PathTracing;
using UnityEngine.Rendering;

namespace Illusion.Rendering
{
    internal sealed partial class IllusionDebugPanels
    {
        private IEnumerable<DebugUI.Widget> CreatePathTracingWidgets()
        {
            yield return Section("Status",
                Value("Ray Tracing", "Path tracing requires DXR ray tracing shaders.",
                    () => PathTracingPass.IsSupported ? "Supported" : "Unsupported"),
                Value("Renderer Feature", "Path tracing capability of the Illusion renderer feature.", FeatureStatus),
                Value("Ray Reconstruction", "Realtime mode reconstructs frames with DLSS Ray Reconstruction.",
                    () => AnyFeature(feature => feature.IsPathTracingRayReconstructionAvailable) ? "Available" : "Unavailable"));
        }

        private static string FeatureStatus()
        {
            if (Features.Count == 0)
                return "Not found";
            if (AnyFeature(feature => feature.IsPathTracingAvailable))
                return "Ready";
            return AnyFeature(feature => feature.pathTracing) ? "Not created" : "Disabled";
        }

        private static bool AnyFeature(System.Func<IllusionRendererFeature, bool> predicate)
        {
            foreach (var feature in Features)
            {
                if (feature && predicate(feature))
                    return true;
            }
            return false;
        }
    }
}
