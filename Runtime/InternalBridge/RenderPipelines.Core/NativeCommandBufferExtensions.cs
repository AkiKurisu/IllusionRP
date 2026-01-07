#if UNITY_2023_1_OR_NEWER   
using System.Runtime.CompilerServices;
using UnityEngine.Rendering;

namespace UnityEngine.Experimental.Rendering
{
    public static class NativeCommandBufferExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static CommandBuffer GetNativeCommandBuffer(this BaseCommandBuffer baseBuffer)
        {
            return baseBuffer.m_WrappedCommandBuffer;
        }
    }
}
#endif
