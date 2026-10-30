using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using BeamRunner.Core.Diagnostics;
#endif

namespace BeamRunner.Services
{
    /// <summary>
    /// Small per-device settings that must not travel with the profile (cloud save, another device): reminder opt-in,
    /// crash-report consent, review-prompt bookkeeping. Stored in <see cref="PlayerPrefs"/> under
    /// <c>BeamRunner.*</c> keys (iOS: UserDefaults, declared in the privacy manifest as CA92.1). Readable very early
    /// (crash reporting reads its consent before the first scene loads). Never throws; while automated tests redirect the
    /// save (<c>TestLaunchOptions</c>, Editor/development builds only) values live in memory so the developer's real
    /// PlayerPrefs are never touched.
    /// </summary>
    public static class DevicePrefs
    {
        private static readonly Dictionary<string, string> s_memory = new Dictionary<string, string>();

        private static bool InMemory
        {
            get
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                return TestLaunchOptions.IsActive;
#else
                return false;
#endif
            }
        }

        public static int GetInt(string key, int fallback)
        {
            if (InMemory)
            {
                string text;
                int value;
                return s_memory.TryGetValue(key, out text) && int.TryParse(text, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out value) ? value : fallback;
            }

            try
            {
                return PlayerPrefs.GetInt(key, fallback);
            }
            catch (Exception e)
            {
                ServiceLog.Warn("DevicePrefs: reading " + key + " failed (" + e.Message + ").");
                return fallback;
            }
        }

        public static void SetInt(string key, int value)
        {
            if (InMemory)
            {
                s_memory[key] = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                return;
            }

            try
            {
                PlayerPrefs.SetInt(key, value);
                PlayerPrefs.Save();
            }
            catch (Exception e)
            {
                ServiceLog.Warn("DevicePrefs: writing " + key + " failed (" + e.Message + ").");
            }
        }

        /// <summary>Reads a 64-bit value (stored as a decimal string; PlayerPrefs has no long).</summary>
        public static long GetLong(string key, long fallback)
        {
            string text = GetString(key, null);
            long value;
            return text != null && long.TryParse(text, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out value) ? value : fallback;
        }

        public static void SetLong(string key, long value)
        {
            SetString(key, value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        public static string GetString(string key, string fallback)
        {
            if (InMemory)
            {
                string text;
                return s_memory.TryGetValue(key, out text) ? text : fallback;
            }

            try
            {
                return PlayerPrefs.HasKey(key) ? PlayerPrefs.GetString(key, fallback) : fallback;
            }
            catch (Exception e)
            {
                ServiceLog.Warn("DevicePrefs: reading " + key + " failed (" + e.Message + ").");
                return fallback;
            }
        }

        public static void SetString(string key, string value)
        {
            if (InMemory)
            {
                s_memory[key] = value ?? string.Empty;
                return;
            }

            try
            {
                PlayerPrefs.SetString(key, value ?? string.Empty);
                PlayerPrefs.Save();
            }
            catch (Exception e)
            {
                ServiceLog.Warn("DevicePrefs: writing " + key + " failed (" + e.Message + ").");
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_memory.Clear();
        }
    }
}
