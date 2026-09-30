using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace Illusion.Rendering
{
    internal sealed class DLSSNeuralRenderingInputs : ContextItem
    {
        internal TextureHandle Depth;

        internal TextureHandle Motion;

        internal Vector2 MotionScale;

        internal bool IsValid => Depth.IsValid() && Motion.IsValid();

        public override void Reset()
        {
            Depth = TextureHandle.nullHandle;
            Motion = TextureHandle.nullHandle;
            MotionScale = Vector2.one;
        }
    }
}
