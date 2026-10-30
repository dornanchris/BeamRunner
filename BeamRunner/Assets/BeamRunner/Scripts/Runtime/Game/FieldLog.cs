using System.Diagnostics;

namespace BeamRunner.Game
{
    /// <summary>
    /// Game logging with a consistent prefix. <see cref="Info"/> is compiled out of release builds (it is only
    /// kept in the Editor and development builds); warnings and errors always log.
    /// </summary>
    public static class FieldLog
    {
        private const string Prefix = "[BeamRunner] ";

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void Info(string message)
        {
            UnityEngine.Debug.Log(Prefix + message);
        }

        public static void Warn(string message)
        {
            UnityEngine.Debug.LogWarning(Prefix + message);
        }

        public static void Error(string message)
        {
            UnityEngine.Debug.LogError(Prefix + message);
        }

        /// <summary>Logs an exception caught by a guard (with its stack trace) and the context it happened in.</summary>
        public static void Exception(string context, System.Exception exception)
        {
            UnityEngine.Debug.LogError(Prefix + context + " failed: " + (exception != null ? exception.Message : "?"));
            if (exception != null)
            {
                UnityEngine.Debug.LogException(exception);
            }
        }
    }
}
