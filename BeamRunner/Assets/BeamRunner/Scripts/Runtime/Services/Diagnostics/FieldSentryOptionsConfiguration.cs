using System;
using Sentry.Unity;
using UnityEngine;

namespace BeamRunner.Services
{
    /// <summary>
    /// Sentry "Options Configuration" script (Tools ▸ Sentry ▸ Options Config, or <c>BeamRunner ▸ Crash Reporting ▸
    /// Set Up Sentry</c>): the asset <c>Assets/Resources/Sentry/FieldSentryOptionsConfiguration.asset</c> is referenced by
    /// <c>SentryOptions.asset</c>, so Sentry calls <see cref="Configure"/> when the game starts and while building. It
    /// injects the DSN from <c>monetization.json</c> and applies <see cref="CrashReporting.ConfigureOptions"/> — at startup
    /// that keeps the SDK off until the player has agreed to crash reports.
    /// </summary>
    [CreateAssetMenu(fileName = "FieldSentryOptionsConfiguration", menuName = "BeamRunner/Sentry Options Configuration")]
    public sealed class FieldSentryOptionsConfiguration : SentryOptionsConfiguration
    {
        /// <summary>Asset path (under Resources, next to Sentry's own options asset).</summary>
        public const string AssetPath = "Assets/Resources/Sentry/FieldSentryOptionsConfiguration.asset";

        public override void Configure(SentryUnityOptions options)
        {
            bool building;
            try
            {
                building = !Application.isPlaying;
            }
            catch (Exception)
            {
                building = true; // not on the main thread: only the Editor's build pipeline calls from elsewhere
            }

            try
            {
                CrashReporting.ConfigureOptions(options, building);
            }
            catch (Exception e)
            {
                // Never let a configuration problem start the SDK without consent.
                if (!building && options != null)
                {
                    options.Enabled = false;
                }

                Debug.LogError("[BeamRunner] Sentry options configuration failed: " + e.Message);
            }
        }
    }
}
