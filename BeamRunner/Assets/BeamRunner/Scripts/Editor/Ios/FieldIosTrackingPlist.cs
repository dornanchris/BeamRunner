#if UNITY_IOS
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEngine;
#endif

namespace BeamRunner.EditorTools
{
    /// <summary>
    /// iOS post-build step: writes <c>NSUserTrackingUsageDescription</c> (the App Tracking Transparency prompt text)
    /// and <c>ITSAppUsesNonExemptEncryption</c> = false into the generated Xcode project's <c>Info.plist</c>. Unity 6 has no Player Settings field for it and
    /// <c>com.unity.ads.ios-support</c> 1.2.0 does not add it (its own post-process only merges SKAdNetwork ids);
    /// without it <c>ATTrackingStatusBinding</c> never shows the prompt and ads stay non-personalised.
    /// An existing non-empty value (e.g. set by another plugin) is never overwritten.
    /// </summary>
    /// <remarks>
    /// SKAdNetwork identifiers are NOT written here: enable "Add networks SKAdNetwork IDs" in
    /// <c>Ads Mediation ▸ Network Manager</c> so LevelPlay injects the list for the installed adapters at build time.
    /// </remarks>
    public static class FieldIosTrackingPlist
    {
        /// <summary>Info.plist key read by iOS when the ATT prompt is shown.</summary>
        public const string UsageDescriptionKey = "NSUserTrackingUsageDescription";

        /// <summary>Text shown in the ATT system prompt.</summary>
        public const string UsageDescription =
            "Your data will be used to show you more relevant ads. Ads keep BeamRunner free.";

        /// <summary>
        /// Export compliance: false = the app uses only exempt encryption (HTTPS through the OS, the save file's HMAC),
        /// so App Store Connect doesn't ask on every upload. Change it if the game ever adds its own cryptography.
        /// </summary>
        public const string EncryptionKey = "ITSAppUsesNonExemptEncryption";

        /// <summary>Runs after Unity's own and the packages' Info.plist edits (they use order 0).</summary>
        public const int CallbackOrder = 100;

#if UNITY_IOS
        /// <summary>Unity post-build callback (iOS target only).</summary>
        [PostProcessBuild(CallbackOrder)]
        public static void OnPostprocessBuild(BuildTarget target, string pathToBuiltProject)
        {
            if (target != BuildTarget.iOS)
            {
                return;
            }

            string plistPath = Path.Combine(pathToBuiltProject, "Info.plist");
            try
            {
                if (!File.Exists(plistPath))
                {
                    Debug.LogError("[BeamRunner] iOS: Info.plist not found at " + plistPath +
                                   "; add " + UsageDescriptionKey + " manually or the ATT prompt will never show.");
                    return;
                }

                var plist = new PlistDocument();
                plist.ReadFromString(File.ReadAllText(plistPath));
                PlistElementDict root = plist.root;
                if (root == null)
                {
                    Debug.LogError("[BeamRunner] iOS: Info.plist could not be parsed; " + UsageDescriptionKey +
                                   " not added.");
                    return;
                }

                bool changed = false;
                if (root.values == null || !root.values.ContainsKey(EncryptionKey))
                {
                    root.SetBoolean(EncryptionKey, false);
                    changed = true;
                    Debug.Log("[BeamRunner] iOS: added " + EncryptionKey + " = false to Info.plist.");
                }

                if (root.values != null && root.values.TryGetValue(UsageDescriptionKey, out PlistElement existing) &&
                    HasText(existing))
                {
                    Debug.Log("[BeamRunner] iOS: keeping the existing " + UsageDescriptionKey + ".");
                }
                else
                {
                    root.SetString(UsageDescriptionKey, UsageDescription);
                    changed = true;
                    Debug.Log("[BeamRunner] iOS: added " + UsageDescriptionKey + " to Info.plist.");
                }

                if (changed)
                {
                    File.WriteAllText(plistPath, plist.WriteToString());
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[BeamRunner] iOS: writing " + UsageDescriptionKey + " failed: " + e.Message);
            }
        }

        private static bool HasText(PlistElement element)
        {
            if (element == null)
            {
                return false;
            }

            try
            {
                return !string.IsNullOrWhiteSpace(element.AsString());
            }
            catch (Exception)
            {
                return true; // not a string: someone put something there on purpose — leave it alone
            }
        }
#endif
    }
}
