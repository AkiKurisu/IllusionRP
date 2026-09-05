using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace Illusion.Rendering.AreaLights
{
    /// <summary>
    /// Per frame area light results shared with later passes (shadow atlas debug overlay).
    /// </summary>
    public class AreaLightFrameData : ContextItem
    {
        public TextureHandle ShadowAtlas;

        internal AreaLightShadowAtlas Atlas;

        public int ShadowRequestCount;

        public override void Reset()
        {
            ShadowAtlas = TextureHandle.nullHandle;
            Atlas = null;
            ShadowRequestCount = 0;
        }
    }
}
