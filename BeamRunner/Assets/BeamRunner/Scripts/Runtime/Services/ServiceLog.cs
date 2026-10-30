using System;
using System.Diagnostics;
using Debug = UnityEngine.Debug;

namespace BeamRunner.Services
{
    /// <summary>
    /// Logging for the services layer. Every line carries the <c>[BeamRunner]</c> prefix.
    /// <see cref="Info"/> and <see cref="Warn"/> are compiled out of release players (only the Editor and
    /// development builds log them, including argument evaluation); <see cref="Error"/> always logs because
    /// it only reports genuine faults.
    /// </summary>
    internal static class ServiceLog
    {
        private const string Prefix = "[BeamRunner] ";

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void Info(string message)
        {
            Debug.Log(Prefix + message);
        }

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void Warn(string message)
        {
            Debug.LogWarning(Prefix + message);
        }

        public static void Error(string message)
        {
            Debug.LogError(Prefix + message);
        }

        /// <summary>Logs an unexpected exception with context. Never throws.</summary>
        public static void Exception(string context, Exception exception)
        {
            if (exception == null)
            {
                Error(context);
                return;
            }

            Debug.LogError(Prefix + context + ": " + exception.GetType().Name + ": " + exception.Message);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogException(exception);
#endif
        }
    }
}
