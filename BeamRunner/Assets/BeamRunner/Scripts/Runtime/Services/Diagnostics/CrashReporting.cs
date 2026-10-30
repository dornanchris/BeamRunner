using System;
using System.Globalization;
using BeamRunner.Core.Meta;
using Sentry.Unity;
using UnityEngine;

namespace BeamRunner.Services
{
    /// <summary>
    /// Consent-gated crash and exception reporting through Sentry for Unity (<c>io.sentry.unity</c> 4.10.3). Nothing is
    /// captured or sent unless ALL of these hold:
    /// <list type="bullet">
    /// <item>a DSN is configured (<c>crashReportingDsn</c> in <c>monetization.json</c>; empty = off, the game runs the
    /// same without it);</item>
    /// <item>the player said yes — on the first-launch privacy dialog or in Settings ▸ Privacy ▸ "Send crash reports"
    /// (stored per device in <see cref="DevicePrefs"/>; unanswered = no);</item>
    /// <item>not the Unity Editor, unless <c>crashReportingInEditor</c> is set.</item>
    /// </list>
    /// Startup: Sentry's own <c>SubsystemRegistration</c> hook loads <c>Resources/Sentry/SentryOptions.asset</c>, whose
    /// options configuration is <see cref="FieldSentryOptionsConfiguration"/> → <see cref="ConfigureOptions"/>, which keeps
    /// the SDK disabled without consent. Later changes go through <see cref="SetChoice"/> / <see cref="Apply"/>
    /// (<c>SentrySdk.Init</c> / <c>SentrySdk.Close</c>). Privacy: no screenshots, no view hierarchy, no default PII
    /// (<c>SendDefaultPii</c> off: no device name/unique id, no IP-based user), no tracing; native crashes (Android NDK,
    /// iOS) are captured by the native SDKs that Sentry adds to the Gradle/Xcode project when the options asset is valid.
    /// Main thread only; never throws.
    /// </summary>
    public static class CrashReporting
    {
        /// <summary>The player's answer.</summary>
        public enum Choice
        {
            /// <summary>Not answered yet: nothing is sent.</summary>
            Unset = 0,
            Off = 1,
            On = 2,
        }

        /// <summary>DevicePrefs key of the player's choice.</summary>
        public const string ChoiceKey = "BeamRunner.Privacy.CrashReports";

        private static bool s_started;

        /// <summary>True when a DSN is configured and this platform/build may report (see the class remarks).</summary>
        public static bool IsConfigured
        {
            get
            {
                try
                {
                    MonetizationConfig config = MonetizationConfig.Current;
                    if (string.IsNullOrEmpty(config.CrashReportingDsn))
                    {
                        return false;
                    }

                    return !Application.isEditor || config.CrashReportingInEditor;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        /// <summary>The stored answer (Unset until the privacy dialog or Settings recorded one).</summary>
        public static Choice StoredChoice
        {
            get
            {
                int raw = DevicePrefs.GetInt(ChoiceKey, (int)Choice.Unset);
                return raw == (int)Choice.On ? Choice.On : raw == (int)Choice.Off ? Choice.Off : Choice.Unset;
            }
        }

        /// <summary>True when reports may be sent: configured and the player said yes.</summary>
        public static bool IsAllowed => IsConfigured && StoredChoice == Choice.On;

        /// <summary>True while the Sentry SDK is running.</summary>
        public static bool IsRunning
        {
            get
            {
                try
                {
                    return Sentry.Unity.SentrySdk.IsEnabled;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// The switch's default on the first-launch dialog for this device's region: off (opt-in) in the EEA/UK/CH or when
        /// the region is unknown, on elsewhere (see <see cref="PrivacyRegion"/>).
        /// </summary>
        public static bool RegionalDefault
        {
            get
            {
                string country, zone;
                ReadRegion(out country, out zone);
                bool on = PrivacyRegion.DefaultCrashReportsOn(country, zone);
                ServiceLog.Info("CrashReporting: region country=" + (country ?? "?") + " zone=" + (zone ?? "?") +
                                " -> default " + (on ? "on" : "off") + ".");
                return on;
            }
        }

        /// <summary>
        /// True when the previous session ended in a crash (from the native SDKs; false when unknown, not running or no
        /// native support).
        /// </summary>
        public static bool CrashedLastSession
        {
            get
            {
                try
                {
                    return IsRunning && Sentry.Unity.SentrySdk.GetLastRunState() == Sentry.Unity.SentrySdk.CrashedLastRun.Crashed;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        /// <summary>Records the player's answer and starts or stops reporting accordingly.</summary>
        public static void SetChoice(bool sendReports)
        {
            DevicePrefs.SetInt(ChoiceKey, (int)(sendReports ? Choice.On : Choice.Off));
            ServiceLog.Info("CrashReporting: player chose " + (sendReports ? "on" : "off") + ".");
            Apply();
        }

        /// <summary>
        /// Starts Sentry when allowed and not running, stops it when running but no longer allowed. Also the safety net
        /// at boot: an SDK that self-initialised without consent (options asset not linked to
        /// <see cref="FieldSentryOptionsConfiguration"/>) is closed again.
        /// </summary>
        public static void Apply()
        {
            bool allowed = IsAllowed;
            bool running = IsRunning;
            if (allowed && !running)
            {
                Start();
            }
            else if (!allowed && running)
            {
                Stop();
            }
        }

        /// <summary>
        /// Applies the game's privacy defaults to Sentry's options. Called by <see cref="FieldSentryOptionsConfiguration"/>
        /// at startup and at build time (<paramref name="building"/>: the Gradle/Xcode post-processing, where the SDK must
        /// stay enabled so the native SDKs and the symbol upload are included whenever a DSN is configured).
        /// </summary>
        public static void ConfigureOptions(SentryUnityOptions options, bool building)
        {
            if (options == null)
            {
                return;
            }

            MonetizationConfig config = MonetizationConfig.Current;
            if (string.IsNullOrEmpty(options.Dsn))
            {
                options.Dsn = config.CrashReportingDsn;
            }

            options.SendDefaultPii = false;
            options.AttachScreenshot = false;
            options.AttachViewHierarchy = false;
            options.TracesSampleRate = 0.0;
            options.AutoSessionTracking = true; // crash-free session rate; no user identity
            options.CaptureInEditor = config.CrashReportingInEditor;

            // The native SDKs start with the C# layer (after consent), never before the engine at app launch.
            options.IosNativeInitializationType = NativeInitializationType.Runtime;
            options.AndroidNativeInitializationType = NativeInitializationType.Runtime;

            if (string.IsNullOrEmpty(options.Release))
            {
                options.Release = SafeRelease();
            }

            if (string.IsNullOrEmpty(options.Environment))
            {
                options.Environment = Debug.isDebugBuild ? "development" : "production";
            }

            if (!building)
            {
                options.Enabled = options.Enabled && !string.IsNullOrEmpty(options.Dsn) && StoredChoice == Choice.On &&
                                  (!Application.isEditor || config.CrashReportingInEditor);
            }
        }

        private static void Start()
        {
            try
            {
                // Same options as a startup initialisation (asset + FieldSentryOptionsConfiguration); without the asset
                // (not set up in the Editor) managed exceptions are still reported, native crashes are not.
                SentryUnityOptions options = ScriptableSentryUnityOptions.LoadSentryUnityOptions();
                if (options == null)
                {
                    options = new SentryUnityOptions();
                    ConfigureOptions(options, false);
                }

                if (!options.ShouldInitializeSdk())
                {
                    ServiceLog.Info("CrashReporting: not started (disabled or no DSN).");
                    return;
                }

                Sentry.Unity.SentrySdk.Init(options);
                s_started = true;
                ServiceLog.Info("CrashReporting: started.");
            }
            catch (Exception e)
            {
                ServiceLog.Exception("CrashReporting: starting Sentry failed", e);
            }
        }

        private static void Stop()
        {
            try
            {
                Sentry.Unity.SentrySdk.Close();
                ServiceLog.Info("CrashReporting: stopped" + (s_started ? "." : " (it had started without consent)."));
            }
            catch (Exception e)
            {
                ServiceLog.Exception("CrashReporting: stopping Sentry failed", e);
            }

            s_started = false;
        }

        private static string SafeRelease()
        {
            try
            {
                return "beamrunner@" + Application.version;
            }
            catch (Exception)
            {
                return "beamrunner@unknown";
            }
        }

        private static void ReadRegion(out string country, out string zone)
        {
            country = null;
            zone = null;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var locale = new AndroidJavaClass("java.util.Locale"))
                using (AndroidJavaObject current = locale.CallStatic<AndroidJavaObject>("getDefault"))
                {
                    country = current.Call<string>("getCountry");
                }

                using (var timeZone = new AndroidJavaClass("java.util.TimeZone"))
                using (AndroidJavaObject current = timeZone.CallStatic<AndroidJavaObject>("getDefault"))
                {
                    zone = current.Call<string>("getID");
                }
            }
            catch (Exception e)
            {
                ServiceLog.Warn("CrashReporting: reading the Android locale failed (" + e.Message + ").");
            }
#endif
            if (string.IsNullOrEmpty(country))
            {
                try
                {
                    country = RegionInfo.CurrentRegion.TwoLetterISORegionName;
                }
                catch (Exception)
                {
                    country = null;
                }
            }

            if (string.IsNullOrEmpty(zone))
            {
                try
                {
                    zone = TimeZoneInfo.Local.Id;
                    if (string.Equals(zone, "Local", StringComparison.OrdinalIgnoreCase))
                    {
                        zone = null;
                    }
                }
                catch (Exception)
                {
                    zone = null;
                }
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_started = false;
        }
    }
}
