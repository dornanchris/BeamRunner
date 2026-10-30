using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace BeamRunner.EditorTools
{
    /// <summary>
    /// iOS post-build step: makes the generated <c>Podfile</c> use the CocoaPods CDN instead of the legacy git specs
    /// repository before <c>pod install</c> runs. Some ad SDK dependency files (LevelPlay / IronSource, outside this
    /// repo) list <c>https://github.com/CocoaPods/Specs</c> as a source, which makes CocoaPods clone that
    /// multi-gigabyte repository on the first iOS build while Unity sits frozen on its last progress bar. The CDN
    /// serves the same specs.
    /// </summary>
    /// <remarks>
    /// The External Dependency Manager (bundled with LevelPlay) writes the Podfile at post-build order 40 and runs
    /// <c>pod install</c> at 50 (<c>IOSResolver.BUILD_ORDER_GEN_PODFILE</c> / <c>BUILD_ORDER_INSTALL_PODS</c>), so
    /// this runs in between. No Xcode API is needed, so it is not behind <c>UNITY_IOS</c>.
    /// </remarks>
    public static class FieldIosCocoaPodsCdn
    {
        /// <summary>Between EDM4U's Podfile generation (40) and its <c>pod install</c> (50).</summary>
        public const int CallbackOrder = 45;

        /// <summary>The CocoaPods CDN (the default source of CocoaPods 1.8+ and of EDM4U 1.2.170+).</summary>
        public const string CdnSource = "https://cdn.cocoapods.org/";

        private static readonly Regex SourceLine = new Regex(@"^\s*source\s+(['""])(?<url>[^'""]+)\1\s*$");

        /// <summary>Unity post-build callback (iOS target only).</summary>
        [PostProcessBuild(CallbackOrder)]
        public static void OnPostprocessBuild(BuildTarget target, string pathToBuiltProject)
        {
            if (target != BuildTarget.iOS)
            {
                return;
            }

            string podfilePath = Path.Combine(pathToBuiltProject, "Podfile");
            try
            {
                if (!File.Exists(podfilePath))
                {
                    return;
                }

                string rewritten = UseCdn(File.ReadAllText(podfilePath), out int changed);
                if (changed == 0)
                {
                    return;
                }

                File.WriteAllText(podfilePath, rewritten);
                Debug.Log("[BeamRunner] iOS: Podfile now uses the CocoaPods CDN instead of the git specs repo (" +
                          changed + " source line(s)), so pod install does not clone github.com/CocoaPods/Specs.");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[BeamRunner] iOS: could not point the Podfile at the CocoaPods CDN (" + e.Message +
                                 "). If pod install hangs cloning CocoaPods/Specs, replace that source line in " +
                                 podfilePath + " with source '" + CdnSource + "'.");
            }
        }

        /// <summary>
        /// Returns <paramref name="podfile"/> with every git specs source line replaced by the CDN source: in place (so
        /// source order is kept) the first time, dropped once the CDN is already listed. <paramref name="changed"/>
        /// counts the lines replaced or dropped.
        /// </summary>
        public static string UseCdn(string podfile, out int changed)
        {
            changed = 0;
            if (string.IsNullOrEmpty(podfile))
            {
                return podfile;
            }

            string[] lines = podfile.Split('\n');
            bool hasCdn = false;
            foreach (string line in lines)
            {
                if (TryGetSource(line, out string url) && IsCdn(url))
                {
                    hasCdn = true;
                }
            }

            var output = new List<string>(lines.Length);
            foreach (string line in lines)
            {
                if (!TryGetSource(line, out string url) || !IsGitSpecs(url))
                {
                    output.Add(line);
                    continue;
                }

                changed++;
                if (!hasCdn)
                {
                    output.Add("source '" + CdnSource + "'" + (line.EndsWith("\r", StringComparison.Ordinal) ? "\r" : ""));
                    hasCdn = true;
                }
            }

            return changed == 0 ? podfile : string.Join("\n", output);
        }

        private static bool TryGetSource(string line, out string url)
        {
            Match m = SourceLine.Match(line.TrimEnd('\r'));
            url = m.Success ? m.Groups["url"].Value : null;
            return m.Success;
        }

        private static bool IsCdn(string url)
        {
            return Normalize(url) == "cdn.cocoapods.org";
        }

        /// <summary>The legacy master specs repo, in any of its spellings (https / .git / trailing slash / ssh).</summary>
        private static bool IsGitSpecs(string url)
        {
            string n = Normalize(url);
            return n == "github.com/cocoapods/specs" || n == "git@github.com:cocoapods/specs";
        }

        private static string Normalize(string url)
        {
            string n = url.Trim().ToLowerInvariant().TrimEnd('/');
            if (n.EndsWith(".git", StringComparison.Ordinal))
            {
                n = n.Substring(0, n.Length - 4);
            }

            if (n.StartsWith("https://", StringComparison.Ordinal))
            {
                n = n.Substring(8);
            }
            else if (n.StartsWith("http://", StringComparison.Ordinal))
            {
                n = n.Substring(7);
            }

            return n.TrimEnd('/');
        }
    }
}
