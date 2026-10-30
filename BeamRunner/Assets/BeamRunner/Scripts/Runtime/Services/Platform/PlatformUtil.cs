using System;
using System.Text;
using BeamRunner.Core.Localization;
using UnityEngine;

namespace BeamRunner.Services
{
    /// <summary>
    /// Small platform helpers: store/rating links, legal links, support e-mail, frame-rate and screen-sleep
    /// policy. Main thread only; nothing throws. Links come from <see cref="MonetizationConfig"/>.
    /// </summary>
    public static class PlatformUtil
    {
        private const string PlayStoreUrlFormat = "https://play.google.com/store/apps/details?id={0}";
        private const string AppStoreUrlFormat = "https://apps.apple.com/app/id{0}";
        private const string AppStoreReviewUrlFormat = "https://apps.apple.com/app/id{0}?action=write-review";

        /// <summary>True on phones/tablets (Android, iOS players).</summary>
        public static bool IsMobile
        {
            get
            {
                try
                {
                    return Application.isMobilePlatform;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        /// <summary>The app version string (Player Settings → Version).</summary>
        public static string AppVersion
        {
            get
            {
                try
                {
                    return Application.version ?? "";
                }
                catch (Exception)
                {
                    return "";
                }
            }
        }

        /// <summary>
        /// Store page URL for this platform, or null when unknown (iOS without <c>iosAppId</c>, desktop, WebGL).
        /// In the Editor the Google Play URL is returned for testing.
        /// </summary>
        public static string StorePageUrl
        {
            get
            {
                MonetizationConfig config = MonetizationConfig.Current;
#if UNITY_IOS
                return string.IsNullOrEmpty(config.IosAppId) ? null : string.Format(AppStoreUrlFormat, config.IosAppId);
#else
                if (Application.platform == RuntimePlatform.Android || Application.isEditor)
                {
                    string package = config.AndroidPackage;
                    return string.IsNullOrEmpty(package) ? null : string.Format(PlayStoreUrlFormat, package);
                }

                return null;
#endif
            }
        }

        /// <summary>True when <see cref="OpenStorePage"/> can do something on this platform.</summary>
        public static bool CanOpenStorePage => !string.IsNullOrEmpty(StorePageUrl);

        /// <summary>Opens this game's store page. Returns false if no store link is known.</summary>
        public static bool OpenStorePage()
        {
            string url = StorePageUrl;
            if (string.IsNullOrEmpty(url))
            {
                ServiceLog.Warn("No store page configured for this platform (set iosAppId / androidPackage).");
                return false;
            }

            return OpenUrl(url);
        }

        /// <summary>
        /// Former rating helper. Automatic prompts must use the platforms' own review sheets through
        /// <see cref="StoreReview"/> (gated by <c>ReviewPromptPolicy</c>); a player-initiated "Rate" button uses
        /// <see cref="OpenWriteReviewPage"/>, which this now forwards to.
        /// </summary>
        [Obsolete("Use StoreReview.Request (automatic, policy-gated) or PlatformUtil.OpenWriteReviewPage (explicit Rate button).")]
        public static bool RequestReview()
        {
            return OpenWriteReviewPage();
        }

        /// <summary>
        /// The explicit, player-initiated "Rate" link (e.g. a Settings button): iOS opens the App Store "write a review"
        /// page (needs <c>iosAppId</c>), Android/Editor the store page. Automatic prompts use <see cref="StoreReview"/>
        /// (the platforms' in-app review sheets) instead. Returns false if no store link is known.
        /// </summary>
        public static bool OpenWriteReviewPage()
        {
#if UNITY_IOS && !UNITY_EDITOR
            string appId = MonetizationConfig.Current.IosAppId;
            if (string.IsNullOrEmpty(appId))
            {
                ServiceLog.Warn("No App Store id configured (iosAppId).");
                return false;
            }

            return OpenUrl(string.Format(AppStoreReviewUrlFormat, appId));
#else
            return OpenStorePage();
#endif
        }

        /// <summary>Opens the privacy policy from the config.</summary>
        public static bool OpenPrivacyPolicy()
        {
            return OpenUrl(MonetizationConfig.Current.PrivacyPolicyUrl);
        }

        /// <summary>Opens the terms of service from the config.</summary>
        public static bool OpenTerms()
        {
            return OpenUrl(MonetizationConfig.Current.TermsUrl);
        }

        /// <summary>
        /// Opens an http(s)/mailto/store URL in the system handler. Returns false for empty or unsupported
        /// URLs or if the platform call failed.
        /// </summary>
        public static bool OpenUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                ServiceLog.Warn("OpenUrl: no URL configured.");
                return false;
            }

            url = url.Trim();
            if (!IsAllowedScheme(url))
            {
                ServiceLog.Warn("OpenUrl: refusing unsupported URL '" + url + "'.");
                return false;
            }

            if (url.IndexOf("example.com", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                ServiceLog.Warn("OpenUrl: '" + url + "' is a placeholder; set the real link in monetization.json.");
            }

            try
            {
                Application.OpenURL(url);
                return true;
            }
            catch (Exception e)
            {
                ServiceLog.Exception("OpenUrl failed", e);
                return false;
            }
        }

        /// <summary>
        /// Builds the support <c>mailto:</c> link: configured address, subject with the game name and version,
        /// and a body pre-filled with version / platform / device / OS details below a space for the message.
        /// Returns null when no support address is configured.
        /// </summary>
        public static string BuildSupportMailto(string subject = null)
        {
            string address = MonetizationConfig.Current.SupportEmail;
            if (string.IsNullOrEmpty(address))
            {
                return null;
            }

            string productName = SafeProductName();
            string version = AppVersion;
            if (string.IsNullOrEmpty(subject))
            {
                subject = Loc.T("support.email_subject", ("game", productName), ("version", version));
            }

            // The player's line is localized; the diagnostic lines stay English for the support team.
            var body = new StringBuilder(256);
            body.Append("\n\n\n----\n");
            body.Append(Loc.T("support.email_instruction")).Append('\n');
            body.Append("Game: ").Append(productName).Append(' ').Append(version).Append('\n'); // l10n-ok: for support staff
            body.Append("Platform: ").Append(SafePlatform()).Append('\n'); // l10n-ok: for support staff
            body.Append("Device: ").Append(SafeDeviceModel()).Append('\n'); // l10n-ok: for support staff
            body.Append("OS: ").Append(SafeOperatingSystem()).Append('\n'); // l10n-ok: for support staff
            body.Append("Language: ").Append(Loc.Language.Code).Append('\n'); // l10n-ok: for support staff

            return "mailto:" + address +
                   "?subject=" + Uri.EscapeDataString(subject) +
                   "&body=" + Uri.EscapeDataString(body.ToString());
        }

        /// <summary>Opens the mail app with <see cref="BuildSupportMailto"/>. Returns false if unavailable.</summary>
        public static bool ComposeSupportEmail(string subject = null)
        {
            string mailto = BuildSupportMailto(subject);
            if (mailto == null)
            {
                ServiceLog.Warn("ComposeSupportEmail: no support address configured.");
                return false;
            }

            return OpenUrl(mailto);
        }

        /// <summary>
        /// Applies the frame-rate policy. Mobile: vSync off and <c>Application.targetFrameRate</c> =
        /// <paramref name="preferred"/> (clamped to 30…120; the display still caps it). Desktop/Editor: vSync on
        /// (smooth, no tearing) and no explicit target. WebGL: browser-paced.
        /// </summary>
        public static void ApplyFrameRate(int preferred)
        {
            try
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                Application.targetFrameRate = -1;
#else
                if (IsMobile)
                {
                    QualitySettings.vSyncCount = 0;
                    Application.targetFrameRate = Mathf.Clamp(preferred <= 0 ? 60 : preferred, 30, 120);
                }
                else
                {
                    QualitySettings.vSyncCount = 1;
                    Application.targetFrameRate = -1;
                }
#endif
            }
            catch (Exception e)
            {
                ServiceLog.Exception("ApplyFrameRate failed", e);
            }
        }

        /// <summary>Prevents (true) or allows (false) the screen from dimming/sleeping — keep it on during runs.</summary>
        public static void KeepScreenOn(bool keepOn)
        {
            try
            {
                Screen.sleepTimeout = keepOn ? SleepTimeout.NeverSleep : SleepTimeout.SystemSetting;
            }
            catch (Exception e)
            {
                ServiceLog.Exception("KeepScreenOn failed", e);
            }
        }

        private static bool IsAllowedScheme(string url)
        {
            return url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                   url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                   url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
                   url.StartsWith("market://", StringComparison.OrdinalIgnoreCase) ||
                   url.StartsWith("itms-apps://", StringComparison.OrdinalIgnoreCase);
        }

        private static string SafeProductName()
        {
            try
            {
                string name = Application.productName;
                return string.IsNullOrEmpty(name) ? "BeamRunner" : name;
            }
            catch (Exception)
            {
                return "BeamRunner";
            }
        }

        private static string SafePlatform()
        {
            try
            {
                return Application.platform.ToString();
            }
            catch (Exception)
            {
                return "unknown";
            }
        }

        private static string SafeDeviceModel()
        {
            try
            {
                return SystemInfo.deviceModel;
            }
            catch (Exception)
            {
                return "unknown";
            }
        }

        private static string SafeOperatingSystem()
        {
            try
            {
                return SystemInfo.operatingSystem;
            }
            catch (Exception)
            {
                return "unknown";
            }
        }
    }
}
