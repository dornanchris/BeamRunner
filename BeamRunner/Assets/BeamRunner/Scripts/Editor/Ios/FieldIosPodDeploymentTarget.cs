using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace BeamRunner.EditorTools
{
    /// <summary>
    /// iOS post-build step: adds a <c>post_install</c> hook to the generated <c>Podfile</c> that raises every pod
    /// target's <c>IPHONEOS_DEPLOYMENT_TARGET</c> to the app's minimum iOS version. Pods keep the deployment target of
    /// their podspec (IronSource 10.0/12.0, UnityAds 13.0, …), which Xcode 26 flags as "set to 10.0, but the range of
    /// supported deployment target versions is 15.0 to …" on every build — and targets below iOS 11 can fail to link
    /// (missing <c>libarclite</c>). Pods built for a lower minimum run fine on a higher one, so raising it is safe.
    /// </summary>
    /// <remarks>
    /// Runs between EDM4U's Podfile generation (40) and its <c>pod install</c> (50), after
    /// <see cref="FieldIosCocoaPodsCdn"/> (45). EDM4U writes no <c>post_install</c> hook of its own, but CocoaPods allows
    /// only one, so if one is already there the lines go inside it. No Xcode API is needed, so it is not behind
    /// <c>UNITY_IOS</c>.
    /// </remarks>
    public static class FieldIosPodDeploymentTarget
    {
        /// <summary>After <see cref="FieldIosCocoaPodsCdn"/> (45), before EDM4U's <c>pod install</c> (50).</summary>
        public const int CallbackOrder = 46;

        /// <summary>Unity 6.3's lowest supported iOS; used when the Player Settings value can't be parsed.</summary>
        public const string FallbackVersion = "15.0";

        /// <summary>Marks the inserted lines so a re-run (Append build) doesn't add them twice.</summary>
        public const string Marker = "# BeamRunner: pod deployment target";

        private static readonly Regex PostInstallLine = new Regex(@"^\s*post_install\s+do\s*\|\s*(?<var>\w+)\s*\|");

        /// <summary>Unity post-build callback (iOS target only).</summary>
        [PostProcessBuild(CallbackOrder)]
        public static void OnPostprocessBuild(BuildTarget target, string pathToBuiltProject)
        {
            if (target != BuildTarget.iOS)
            {
                return;
            }

            string podfilePath = Path.Combine(pathToBuiltProject, "Podfile");
            string version = MinimumVersion(PlayerSettings.iOS.targetOSVersionString);
            try
            {
                if (!File.Exists(podfilePath))
                {
                    return;
                }

                string rewritten = AddHook(File.ReadAllText(podfilePath), version, out bool changed);
                if (!changed)
                {
                    return;
                }

                File.WriteAllText(podfilePath, rewritten);
                Debug.Log("[BeamRunner] iOS: Podfile raises every pod's deployment target to iOS " + version +
                          " (post_install hook).");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[BeamRunner] iOS: could not add the pod deployment target hook to " +
                                 podfilePath + " (" + e.Message + "). Xcode will warn that pods target an iOS " +
                                 "version below " + version + "; the build still works.");
            }
        }

        /// <summary>
        /// The deployment target to give the pods: the Player Settings minimum, or <see cref="FallbackVersion"/> when
        /// that is missing, unparsable or lower.
        /// </summary>
        public static string MinimumVersion(string playerSettingsVersion)
        {
            Version fallback = new Version(FallbackVersion);
            if (!Version.TryParse(playerSettingsVersion?.Trim() ?? "", out Version parsed) || parsed < fallback)
            {
                return FallbackVersion;
            }

            return parsed.Major.ToString(CultureInfo.InvariantCulture) + "." +
                   Math.Max(parsed.Minor, 0).ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Returns <paramref name="podfile"/> with the deployment target lines added: inside an existing
        /// <c>post_install</c> block, otherwise in a new one at the end. Unchanged (and <paramref name="changed"/>
        /// false) when the lines are already there.
        /// </summary>
        public static string AddHook(string podfile, string version, out bool changed)
        {
            changed = false;
            if (podfile == null || podfile.Contains(Marker))
            {
                return podfile;
            }

            string newline = podfile.Contains("\r\n") ? "\r\n" : "\n";
            string[] lines = podfile.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                Match m = PostInstallLine.Match(lines[i].TrimEnd('\r'));
                if (!m.Success)
                {
                    continue;
                }

                var sb = new StringBuilder();
                for (int j = 0; j < lines.Length; j++)
                {
                    sb.Append(lines[j]);
                    if (j < lines.Length - 1)
                    {
                        sb.Append('\n');
                    }

                    if (j == i)
                    {
                        if (j == lines.Length - 1)
                        {
                            sb.Append(newline);
                        }

                        sb.Append(Body(m.Groups["var"].Value, version, "  ", newline));
                    }
                }

                changed = true;
                return sb.ToString();
            }

            string trimmed = podfile.TrimEnd('\r', '\n');
            changed = true;
            return trimmed + newline + newline +
                   "post_install do |installer|" + newline +
                   Body("installer", version, "  ", newline) +
                   "end" + newline;
        }

        private static string Body(string installer, string version, string indent, string newline)
        {
            string[] body =
            {
                Marker,
                installer + ".pods_project.targets.each do |pod_target|",
                "  pod_target.build_configurations.each do |config|",
                "    current = config.build_settings['IPHONEOS_DEPLOYMENT_TARGET']",
                "    if current.nil? || current.to_f < " + version,
                "      config.build_settings['IPHONEOS_DEPLOYMENT_TARGET'] = '" + version + "'",
                "    end",
                "  end",
                "end",
            };

            var sb = new StringBuilder();
            foreach (string line in body)
            {
                sb.Append(indent).Append(line).Append(newline);
            }

            return sb.ToString();
        }
    }
}
