using System;
using BeamRunner.Core.Localization;
using BeamRunner.Core.Meta;
using BeamRunner.Services;
using UnityEngine;

namespace BeamRunner.Game
{
    /// <summary>What the Settings screen shows about cloud save.</summary>
    public enum CloudSaveState
    {
        /// <summary>No cloud save here (not configured / unsupported).</summary>
        Off = 0,

        /// <summary>Available but not synced this session yet (or waiting for sign-in).</summary>
        Idle,

        /// <summary>A sync is running.</summary>
        Syncing,

        /// <summary>The last sync succeeded.</summary>
        Synced,

        /// <summary>The last sync failed (offline, service error, time-out); retried at the next trigger.</summary>
        Offline,

        /// <summary>The cloud copy belongs to another account: only merged when the player confirms.</summary>
        AccountConflict,

        /// <summary>The cloud copy was written by a newer version of the game: merged into this device, never overwritten.</summary>
        NeedsUpdate,
    }

    /// <summary>Why a sync was requested.</summary>
    public enum CloudSyncReason
    {
        /// <summary>Boot / sign-in (automatic).</summary>
        Startup = 0,

        /// <summary>The app goes to the background (automatic, rate-limited).</summary>
        Pause,

        /// <summary>Another device changed the cloud copy (automatic, rate-limited).</summary>
        External,

        /// <summary>Back at the menu after a run (automatic, rate-limited).</summary>
        AfterRun,

        /// <summary>Settings › Sync now.</summary>
        Manual,

        /// <summary>Settings › Sync now, confirmed after an account conflict: merge into the other account's save.</summary>
        ManualMergeAccounts,
    }

    /// <summary>
    /// Cloud save for the player profile. Every sync is read → merge (<see cref="ProfileMerge"/>, into the live profile,
    /// on the main thread) → write the merged, signed profile back, through the platform's <see cref="ICloudSave"/>.
    /// Offline-safe: a failed read changes nothing, a failed write leaves the (monotone) local merge in place and the
    /// next sync uploads it. Never runs before the first-launch consent flow (the online services start after it).
    /// <para>Guards: a cloud copy that is not correctly signed by this game is replaced, never merged; a cloud copy
    /// from a newer game version is merged into the device but never overwritten; a cloud copy of another account
    /// (<see cref="CloudSyncState.CloudId"/> / <see cref="ICloudSave.AccountKey"/> mismatch) is left alone until the
    /// player confirms a merge.</para>
    /// </summary>
    public sealed class CloudSync
    {
        private const float AutomaticInterval = 45f;

        private readonly ProfileStore _store;
        private readonly IOnlineService _online;
        private bool _syncing;
        private float _lastAutomatic = -1000f;
        private DeclineReason _decline;
        private ProfileMergeReport _pendingReport;

        private enum DeclineReason
        {
            None = 0,
            AccountConflict,
            NeedsUpdate,
        }

        /// <summary>Creates the coordinator (does nothing until <see cref="Request"/>).</summary>
        public CloudSync(ProfileStore store, IOnlineService online)
        {
            _store = store;
            _online = online ?? new NullOnlineService();
            State = IsAvailable ? CloudSaveState.Idle : CloudSaveState.Off;
        }

        /// <summary>Raised on the main thread whenever <see cref="State"/> changes.</summary>
        public event Action Changed;

        /// <summary>Raised on the main thread after a sync merged cloud data into the live profile.</summary>
        public event Action<ProfileMergeReport> Merged;

        /// <summary>Current status.</summary>
        public CloudSaveState State { get; private set; }

        /// <summary>True when cloud save exists on this platform/configuration.</summary>
        public bool IsAvailable
        {
            get { return _online.CloudSave != null; }
        }

        /// <summary>True while a sync runs.</summary>
        public bool IsSyncing
        {
            get { return _syncing; }
        }

        /// <summary>Signing in to the platform is needed before syncing (Google Play Games while signed out).</summary>
        public bool NeedsSignIn
        {
            get { return IsAvailable && !_online.CloudSave.IsAvailable && _online.UsesAccount && !_online.IsSignedIn; }
        }

        /// <summary>UTC time of the last successful sync on this device (null = never).</summary>
        public DateTime? LastSyncUtc
        {
            get
            {
                PlayerProfile p = _store != null && _store.Service != null ? _store.Service.Profile : null;
                long ticks = p != null && p.Sync != null ? p.Sync.LastSyncUtcTicks : 0;
                return ticks > 0 ? new DateTime(ticks, DateTimeKind.Utc) : (DateTime?)null;
            }
        }

        /// <summary>
        /// Starts a sync if possible. Automatic reasons are rate-limited and skipped while another sync runs;
        /// <paramref name="onDone"/> (optional) gets true when the cloud and this device ended up in sync.
        /// </summary>
        public void Request(CloudSyncReason reason, Action<bool> onDone = null)
        {
            ICloudSave cloud = _online.CloudSave;
            if (cloud == null || _store == null || _store.Service == null)
            {
                Finish(onDone, false);
                return;
            }

            bool manual = reason == CloudSyncReason.Manual || reason == CloudSyncReason.ManualMergeAccounts;
            float now = Time.realtimeSinceStartup;
            if (_syncing || (!manual && now - _lastAutomatic < AutomaticInterval && reason != CloudSyncReason.Startup))
            {
                Finish(onDone, false);
                return;
            }

            if (!cloud.IsAvailable)
            {
                SetState(CloudSaveState.Idle);
                Finish(onDone, false);
                return;
            }

            if (!manual && State == CloudSaveState.AccountConflict)
            {
                // Waiting for the player's decision; automatic syncs would only hit the same conflict.
                Finish(onDone, false);
                return;
            }

            if (!manual)
            {
                _lastAutomatic = now;
            }

            _syncing = true;
            _decline = DeclineReason.None;
            _pendingReport = null;
            SetState(CloudSaveState.Syncing);
            bool mergeAccounts = reason == CloudSyncReason.ManualMergeAccounts;
            string accountKey = SafeAccountKey(cloud);
            ServiceLogInfo("Cloud save: sync (" + reason + ").");
            try
            {
                cloud.Sync(text => MergeWithCloud(text, accountKey, mergeAccounts), result => OnSynced(result, onDone));
            }
            catch (Exception e)
            {
                FieldLog.Exception("Cloud save sync", e);
                _syncing = false;
                SetState(CloudSaveState.Offline);
                Finish(onDone, false);
            }
        }

        // ------------------------------------------------------------------ merge (main thread, inside the sync)

        private CloudDocument MergeWithCloud(string cloudText, string accountKey, bool mergeAccounts)
        {
            PlayerProfile local = _store.Service.Profile;
            byte[] key = SaveIntegrityKey.Get();

            SaveDocumentKind kind = SaveDocumentKind.Missing;
            PlayerProfile remote = null;
            if (!string.IsNullOrEmpty(cloudText))
            {
                string payload;
                string iid;
                kind = SaveEnvelope.Open(cloudText, key, ProfileStore.IsUsableSave, out payload, out iid);
                if (kind == SaveDocumentKind.Signed)
                {
                    remote = JsonUtility.FromJson<PlayerProfile>(payload);
                    if (remote == null)
                    {
                        kind = SaveDocumentKind.Damaged;
                    }
                }
            }

            string remoteCloudId = remote != null && remote.Sync != null ? remote.Sync.CloudId ?? string.Empty : string.Empty;
            CloudSyncAction action = CloudSyncPolicy.Decide(local.Sync.CloudId, accountKey, kind,
                SaveEnvelope.PeekFormatVersion(cloudText), remote != null ? remote.Version : 0, remoteCloudId, mergeAccounts);
            switch (action)
            {
                case CloudSyncAction.AccountConflict:
                    _decline = DeclineReason.AccountConflict;
                    return null;
                case CloudSyncAction.NeedsUpdate:
                    _decline = DeclineReason.NeedsUpdate;
                    return null;
                case CloudSyncAction.UploadLocal:
                    if (kind != SaveDocumentKind.Missing)
                    {
                        // Unsigned, edited or unreadable data cannot come from this game: replace it.
                        FieldLog.Warn("Cloud save: the cloud copy is " + kind + "; replacing it with this device's profile.");
                    }

                    Bind(local, accountKey, null);
                    return Upload(local, key);
            }

            _store.Serialize(); // books any unsaved gem change on this device's ledger entry first
            _pendingReport = ProfileMerge.MergeInto(local, remote);
            Bind(local, accountKey, remoteCloudId);
            _store.MarkDirty();

            if (action == CloudSyncAction.MergeOnly)
            {
                // Merged what this build understands; writing back would drop the newer build's data.
                _decline = DeclineReason.NeedsUpdate;
                return null;
            }

            return Upload(local, key);
        }

        private static void Bind(PlayerProfile local, string accountKey, string remoteCloudId)
        {
            local.Sync.CloudId = CloudSyncPolicy.ResolveCloudId(local.Sync.CloudId, accountKey, remoteCloudId,
                "cloud-" + Guid.NewGuid().ToString("N"));
        }

        private CloudDocument Upload(PlayerProfile local, byte[] key)
        {
            string json = _store.Serialize();
            string installId = local.Wallet != null && SaveEnvelope.IsValidInstallId(local.Wallet.DeviceId)
                ? local.Wallet.DeviceId
                : Guid.NewGuid().ToString("N");
            return new CloudDocument
            {
                Text = SaveEnvelope.Wrap(json, installId, key),
                PlayedSeconds = local.Stats != null ? local.Stats.TotalPlaySeconds : 0.0,
                Description = Loc.T("online.cloud_save_description"),
            };
        }

        // ------------------------------------------------------------------ completion

        private void OnSynced(CloudSyncResult result, Action<bool> onDone)
        {
            _syncing = false;
            ProfileMergeReport report = _pendingReport;
            _pendingReport = null;
            bool ok = false;
            switch (result.Status)
            {
                case CloudSyncStatus.Success:
                    ok = true;
                    MarkSynced();
                    SetState(CloudSaveState.Synced);
                    break;
                case CloudSyncStatus.Declined:
                    if (_decline == DeclineReason.AccountConflict)
                    {
                        SetState(CloudSaveState.AccountConflict);
                    }
                    else if (_decline == DeclineReason.NeedsUpdate)
                    {
                        MarkSynced();
                        SetState(CloudSaveState.NeedsUpdate);
                    }
                    else
                    {
                        SetState(CloudSaveState.Offline);
                    }

                    break;
                case CloudSyncStatus.Unavailable:
                    SetState(CloudSaveState.Idle);
                    break;
                case CloudSyncStatus.Busy:
                    break;
                default:
                    SetState(CloudSaveState.Offline);
                    break;
            }

            // A merge changes the live profile even when the upload failed: save it and tell the game.
            if (report != null)
            {
                _store.SaveNow(true);
                Action<ProfileMergeReport> merged = Merged;
                if (merged != null)
                {
                    try
                    {
                        merged(report);
                    }
                    catch (Exception e)
                    {
                        FieldLog.Exception("Cloud save merge handler", e);
                    }
                }
            }

            Finish(onDone, ok);
        }

        private void MarkSynced()
        {
            PlayerProfile p = _store.Service.Profile;
            p.Sync.LastSyncUtcTicks = DateTime.UtcNow.Ticks;
            _store.MarkDirty();
        }

        private void SetState(CloudSaveState state)
        {
            if (State == state)
            {
                return;
            }

            State = state;
            Action changed = Changed;
            if (changed != null)
            {
                try
                {
                    changed();
                }
                catch (Exception e)
                {
                    FieldLog.Exception("Cloud save state handler", e);
                }
            }
        }

        private static string SafeAccountKey(ICloudSave cloud)
        {
            try
            {
                return cloud.AccountKey ?? string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private static void Finish(Action<bool> onDone, bool ok)
        {
            if (onDone == null)
            {
                return;
            }

            try
            {
                onDone(ok);
            }
            catch (Exception e)
            {
                FieldLog.Exception("Cloud save callback", e);
            }
        }

        private static void ServiceLogInfo(string message)
        {
            FieldLog.Info(message);
        }
    }
}
