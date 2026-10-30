using System;

namespace BeamRunner.Core.Meta
{
    /// <summary>Cloud-save bookkeeping stored in the profile (see <see cref="ProfileMerge"/>).</summary>
    [Serializable]
    public sealed class CloudSyncState
    {
        /// <summary>
        /// Progress generation. "Reset progress" increments it; when two snapshots disagree the higher epoch wins
        /// (except purchases and the gem ledger), so a reset on one device is not undone by the other device's data.
        /// </summary>
        public int Epoch;

        /// <summary>
        /// Id of the cloud save this profile was last synced with (created with the first upload and stored inside the
        /// cloud copy). A different id in the cloud means another account's save: it is never merged automatically.
        /// Device-local.
        /// </summary>
        public string CloudId = string.Empty;

        /// <summary>UTC ticks of the last successful sync (0 = never). Device-local.</summary>
        public long LastSyncUtcTicks;
    }
}
