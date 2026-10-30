using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace BeamRunner.EditorTools
{
    /// <summary>
    /// Fails Android/iOS <b>release</b> builds (not development builds) that would reach the store unmonetized or
    /// without a privacy policy: no LevelPlay app key or no ad unit for the target platform (the game then silently
    /// shows no ads) or a placeholder <c>privacyPolicyUrl</c> (both stores require a real one for apps with ads and
    /// purchases; it is linked from Settings). Runs for every build path — the BeamRunner build menu,
    /// File ▸ Build and CI. Set <c>"allowIncompleteReleaseBuild": true</c> in <c>monetization.json</c> to build
    /// anyway (e.g. the first test-track upload that Google Play needs before products can be created).
    /// </summary>
    public sealed class FieldMonetizationBuildGuard : IPreprocessBuildWithReport
    {
        /// <summary>The JSON key that turns this guard off.</summary>
        public const string OptOutKey = "allowIncompleteReleaseBuild";

        [Serializable]
        private sealed class ConfigMirror
        {
            public string levelPlayAppKeyAndroid = "";
            public string levelPlayAppKeyIos = "";
            public string rewardedAdUnitAndroid = "";
            public string rewardedAdUnitIos = "";
            public string interstitialAdUnitAndroid = "";
            public string interstitialAdUnitIos = "";
            public string privacyPolicyUrl = "";
            public bool allowIncompleteReleaseBuild;
        }

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            BuildTarget target = report.summary.platform;
            if ((target != BuildTarget.Android && target != BuildTarget.iOS) ||
                (report.summary.options & BuildOptions.Development) != 0)
            {
                return;
            }

            List<string> problems = ReleaseProblems(target, out bool allowed);
            if (problems.Count == 0)
            {
                return;
            }

            string list = "\n  - " + string.Join("\n  - ", problems);
            if (allowed)
            {
                Debug.LogWarning("[BeamRunner] Release build continues WITHOUT full monetization (" + OptOutKey +
                                 " is true in monetization.json):" + list);
                return;
            }

            throw new BuildFailedException("[BeamRunner] This " + target + " release build is not ready for the store:" +
                                           list + "\nFill in " + FieldEditorConstants.MonetizationConfigPath +
                                           " (see Scripts/Runtime/Services/README.md, LevelPlay configuration), or set \"" +
                                           OptOutKey + "\": true there to build anyway (test-track builds only).");
        }

        /// <summary>
        /// What would make a release build for <paramref name="target"/> ship without ads or without a privacy
        /// policy (empty = ready). <paramref name="allowed"/> = the owner opted out of the guard. Never throws.
        /// </summary>
        public static List<string> ReleaseProblems(BuildTarget target, out bool allowed)
        {
            var problems = new List<string>();
            allowed = false;
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(FieldEditorConstants.MonetizationConfigPath);
            ConfigMirror config = null;
            try
            {
                config = asset != null ? JsonUtility.FromJson<ConfigMirror>(asset.text) : null;
            }
            catch (Exception)
            {
                // Reported below like a missing file; Validate Project shows the parse error.
            }

            if (config == null)
            {
                problems.Add("monetization.json is missing or does not parse: the game would run with no ads.");
                return problems;
            }

            allowed = config.allowIncompleteReleaseBuild;
            bool ios = target == BuildTarget.iOS;
            string platform = ios ? "iOS" : "Android";
            string appKey = ios ? config.levelPlayAppKeyIos : config.levelPlayAppKeyAndroid;
            string rewarded = ios ? config.rewardedAdUnitIos : config.rewardedAdUnitAndroid;
            string interstitial = ios ? config.interstitialAdUnitIos : config.interstitialAdUnitAndroid;
            if (string.IsNullOrWhiteSpace(appKey))
            {
                problems.Add("No LevelPlay app key for " + platform + " (levelPlayAppKey" + platform +
                             "): this build would show no ads.");
            }
            else if (string.IsNullOrWhiteSpace(rewarded) && string.IsNullOrWhiteSpace(interstitial))
            {
                problems.Add("No LevelPlay ad unit ids for " + platform + " (rewardedAdUnit" + platform +
                             ", interstitialAdUnit" + platform + "): this build would show no ads.");
            }

            if (FieldProjectValidator.IsPlaceholder(config.privacyPolicyUrl))
            {
                problems.Add("privacyPolicyUrl is a placeholder ('" + config.privacyPolicyUrl +
                             "'): publish docs/PRIVACY_POLICY.md and put its URL there.");
            }

            return problems;
        }
    }
}
