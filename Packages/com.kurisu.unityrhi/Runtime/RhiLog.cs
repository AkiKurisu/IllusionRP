using System.Runtime.CompilerServices;
using UnityEngine;

[assembly: InternalsVisibleTo("UnityRhi.Editor")]

namespace UnityRhi
{
    public static class RhiLog
    {
        private const string Prefix = "[UnityRHI] ";

        public static bool VerboseEnabled { get; set; }

        internal static void Error(string message, Object context = null) => Debug.LogError(Prefix + message, context);

        internal static void Warning(string message, Object context = null) => Debug.LogWarning(Prefix + message, context);

        internal static void Info(string message, Object context = null) => Debug.Log(Prefix + message, context);

        internal static void Verbose(string message, Object context = null)
        {
            if (VerboseEnabled) Debug.Log(Prefix + message, context);
        }
    }
}
