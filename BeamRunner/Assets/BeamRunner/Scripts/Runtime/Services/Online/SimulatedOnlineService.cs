using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace BeamRunner.Services
{
    /// <summary>
    /// Editor / development-build stand-in for the platform services, enabled with <c>simulateInEditor</c> in
    /// <c>online.json</c>: signs in as "Test Pilot" after a short delay, logs scores and achievements, "shows" the
    /// native UIs as a log line, and keeps the cloud save in a local file
    /// (<c>persistentDataPath/beamrunner_cloud_sim.json</c>) so sync, merge and account-conflict flows can be tried
    /// without a device. Edit or delete that file to simulate another device's progress or an empty cloud.
    /// </summary>
    public sealed class SimulatedOnlineService : OnlineServiceBase
    {
        private const float SignInDelay = 0.6f;
        private bool _started;

        /// <summary>Creates the simulation (cloud save always on).</summary>
        public SimulatedOnlineService(OnlineConfig config)
            : base(config, OnlinePlatform.Simulated)
        {
            CloudSave = new FileCloudSave(CloudFilePath());
        }

        /// <inheritdoc />
        public override string PlatformName
        {
            get { return "Online (simulated)"; }
        }

        /// <inheritdoc />
        public override bool UsesAccount
        {
            get { return true; }
        }

        /// <inheritdoc />
        public override bool CanSignInManually
        {
            get { return SignInState == OnlineSignInState.SignedOut; }
        }

        /// <inheritdoc />
        public override void Start()
        {
            if (_started)
            {
                return;
            }

            _started = true;
            SetState(OnlineSignInState.SigningIn);
            ServiceRunner.Delay(SignInDelay, () => SetState(OnlineSignInState.SignedIn, "Test Pilot"), true);
        }

        /// <inheritdoc />
        public override void SignIn(Action<bool> onDone)
        {
            _started = true;
            SetState(OnlineSignInState.SignedIn, "Test Pilot");
            Complete(onDone, true);
        }

        /// <summary>Simulates signing out (e.g. from a debug menu) so the signed-out UI can be checked.</summary>
        public void SimulateSignOut()
        {
            SetState(OnlineSignInState.SignedOut);
        }

        /// <inheritdoc />
        protected override void SubmitScoreNative(string leaderboardId, long score, long context)
        {
            ServiceLog.Info("Online (simulated): score " + score + " -> " + leaderboardId + " (context " + context + ").");
        }

        /// <inheritdoc />
        protected override void UnlockAchievementNative(string achievementId, bool announce)
        {
            ServiceLog.Info("Online (simulated): achievement " + achievementId + (announce ? " unlocked." : " re-reported."));
        }

        /// <inheritdoc />
        protected override bool ShowLeaderboardsNative()
        {
            ServiceLog.Info("Online (simulated): the platform's leaderboards UI would open now.");
            return true;
        }

        /// <inheritdoc />
        protected override bool ShowAchievementsNative()
        {
            ServiceLog.Info("Online (simulated): the platform's achievements UI would open now.");
            return true;
        }

        private static string CloudFilePath()
        {
            try
            {
                return Path.Combine(Application.persistentDataPath, "beamrunner_cloud_sim.json");
            }
            catch (Exception)
            {
                return "beamrunner_cloud_sim.json";
            }
        }

        /// <summary>Cloud save in a local file with a simulated network delay.</summary>
        private sealed class FileCloudSave : ICloudSave
        {
            private const float Latency = 0.4f;
            private readonly string _path;
            private CloudSyncOperation _operation;

            public FileCloudSave(string path)
            {
                _path = path;
            }

            public bool IsAvailable
            {
                get { return true; }
            }

            public string AccountKey
            {
                get { return string.Empty; }
            }

            public void Sync(Func<string, CloudDocument> merge, Action<CloudSyncResult> onDone)
            {
                if (_operation != null && !_operation.IsDone)
                {
                    CloudSyncOperation.Reject(onDone, CloudSyncStatus.Busy, "busy");
                    return;
                }

                CloudSyncOperation op = _operation = new CloudSyncOperation(onDone, 30f);
                ServiceRunner.Delay(Latency, () =>
                {
                    if (op.IsDone)
                    {
                        return;
                    }

                    string text = null;
                    try
                    {
                        if (File.Exists(_path))
                        {
                            text = File.ReadAllText(_path, Encoding.UTF8);
                        }
                    }
                    catch (Exception e)
                    {
                        op.Finish(CloudSyncResult.Of(CloudSyncStatus.Failed, e.Message));
                        return;
                    }

                    CloudDocument doc = op.Merge(merge, string.IsNullOrEmpty(text) ? null : text);
                    if (doc == null || doc.Text == null)
                    {
                        op.Finish(CloudSyncResult.Of(CloudSyncStatus.Declined, "merge declined"));
                        return;
                    }

                    try
                    {
                        File.WriteAllText(_path, doc.Text, new UTF8Encoding(false));
                        op.Finish(CloudSyncResult.Of(CloudSyncStatus.Success, null, true));
                    }
                    catch (Exception e)
                    {
                        op.Finish(CloudSyncResult.Of(CloudSyncStatus.Failed, e.Message));
                    }
                }, true);
            }
        }
    }
}
