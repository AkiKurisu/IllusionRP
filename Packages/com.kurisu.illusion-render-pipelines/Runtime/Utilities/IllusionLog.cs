using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Illusion.Rendering
{
    internal static class IllusionLog
    {
        private const string Prefix = "[IllusionRP] ";

        private static readonly HashSet<string> Reported = new();

        private static bool _verboseEnabled;

        internal static event Action<bool> VerboseEnabledChanged;

        internal static bool VerboseEnabled
        {
            get => _verboseEnabled;
            set
            {
                if (_verboseEnabled == value) return;
                _verboseEnabled = value;
                VerboseEnabledChanged?.Invoke(value);
            }
        }

        internal static void Error(string message, Object context = null) => Debug.LogError(Prefix + message, context);

        internal static void Warning(string message, Object context = null) => Debug.LogWarning(Prefix + message, context);

        internal static void Info(string message, Object context = null) => Debug.Log(Prefix + message, context);

        internal static void Verbose(string message, Object context = null)
        {
            if (_verboseEnabled) Debug.Log(Prefix + message, context);
        }

        internal static void ErrorOnce(string key, string message, Object context = null)
        {
            if (Reported.Add(key)) Error(message, context);
        }

        internal static void WarningOnce(string key, string message, Object context = null)
        {
            if (Reported.Add(key)) Warning(message, context);
        }
    }
}
