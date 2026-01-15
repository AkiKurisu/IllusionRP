using System.Runtime.CompilerServices;
using UnityEngine.Rendering;

namespace UnityEngine.Experimental.Rendering
{
    internal static class NativeCommandBufferExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static CommandBuffer GetNativeCommandBuffer(this BaseCommandBuffer baseBuffer)
        {
            return baseBuffer.m_WrappedCommandBuffer;
        }
    }
}
