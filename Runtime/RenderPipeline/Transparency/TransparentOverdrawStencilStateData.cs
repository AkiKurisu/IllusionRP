using System;
using UnityEngine.Rendering;

namespace Illusion.Rendering
{
    [Serializable]
    public class TransparentOverdrawStencilStateData
    {
        /// <summary>
        /// Used to mark whether the stencil values should be overridden or not.
        /// </summary>
        public bool overrideStencilState;

        /// <summary>
        /// The stencil reference value.
        /// </summary>
        public int stencilReference;

        /// <summary>
        /// The stencil read mask value.
        /// </summary>
        public byte stencilReadMask;

        /// <summary>
        /// The comparison function to use.
        /// </summary>
        public CompareFunction stencilCompareFunction = CompareFunction.Always;

        /// <summary>
        /// The stencil operation to use when the stencil test passes.
        /// </summary>
        public StencilOp passOperation = StencilOp.Keep;

        /// <summary>
        /// The stencil operation to use when the stencil test fails.
        /// </summary>
        public StencilOp failOperation = StencilOp.Keep;

        /// <summary>
        /// The stencil operation to use when the stencil test fails because of depth.
        /// </summary>
        public StencilOp zFailOperation = StencilOp.Keep;
    }
}