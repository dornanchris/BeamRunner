namespace BeamRunner.Core.Meta
{
    /// <summary>What one cloud sync does with the cloud copy it read (see <see cref="CloudSyncPolicy.Decide"/>).</summary>
    public enum CloudSyncAction
    {
        /// <summary>Write this device's profile to the cloud without merging (empty cloud, or unusable cloud data).</summary>
        UploadLocal = 0,

        /// <summary>Merge the cloud copy into this device, then write the merged profile back.</summary>
        MergeAndUpload,

        /// <summary>Merge into this device but never write back (the cloud copy comes from a newer game version).</summary>
        MergeOnly,

        /// <summary>The cloud copy belongs to another account: touch nothing until the player confirms.</summary>
        AccountConflict,

        /// <summary>Unreadable newer-format cloud data: touch nothing (updating the game will read it).</summary>
        NeedsUpdate,
    }

    /// <summary>
    /// The guards around a cloud merge (pure rules; the Unity layer's <c>CloudSync</c> applies them):
    /// <list type="number">
    /// <item><b>Account switch.</b> A profile remembers the cloud it was synced with (<see cref="CloudSyncState.CloudId"/>:
    /// the hashed platform account when the backend knows it, else the id stored inside the cloud copy). A different
    /// account is never merged automatically — even an empty one (a new account must not silently receive another
    /// player's progress) — unless the player confirmed (<c>mergeAccounts</c>).</item>
    /// <item><b>Integrity.</b> Cloud data that is not a correctly signed document of this game (tampered, unsigned,
    /// unreadable) is never merged; this device's profile replaces it. A document of a newer envelope format is left
    /// alone.</item>
    /// <item><b>Versions.</b> A profile written by a newer game version is merged into this device (only the fields this
    /// build knows, all monotone) but never written back, which would drop the newer fields.</item>
    /// </list>
    /// </summary>
    public static class CloudSyncPolicy
    {
        /// <summary>
        /// Decides the action. <paramref name="boundCloudId"/>: this profile's <see cref="CloudSyncState.CloudId"/>;
        /// <paramref name="accountKey"/>: the backend's hashed account ("" = unknown); <paramref name="cloudKind"/> /
        /// <paramref name="cloudFormatVersion"/>: <see cref="SaveEnvelope.Open"/> / <see cref="SaveEnvelope.PeekFormatVersion"/>
        /// of the cloud text; <paramref name="remoteProfileVersion"/> / <paramref name="remoteCloudId"/>: from the cloud
        /// profile (only meaningful when signed).
        /// </summary>
        public static CloudSyncAction Decide(string boundCloudId, string accountKey, SaveDocumentKind cloudKind,
            int cloudFormatVersion, int remoteProfileVersion, string remoteCloudId, bool mergeAccounts)
        {
            string bound = boundCloudId ?? string.Empty;
            string account = accountKey ?? string.Empty;
            string remote = remoteCloudId ?? string.Empty;

            if (!mergeAccounts && account.Length > 0 && bound.Length > 0 && bound != account)
            {
                return CloudSyncAction.AccountConflict;
            }

            if (cloudKind == SaveDocumentKind.Missing)
            {
                return CloudSyncAction.UploadLocal;
            }

            if (cloudKind != SaveDocumentKind.Signed)
            {
                return cloudFormatVersion > SaveEnvelope.FormatVersion ? CloudSyncAction.NeedsUpdate : CloudSyncAction.UploadLocal;
            }

            if (!mergeAccounts && account.Length == 0 && bound.Length > 0 && remote.Length > 0 && bound != remote)
            {
                return CloudSyncAction.AccountConflict;
            }

            return remoteProfileVersion > PlayerProfile.CurrentVersion ? CloudSyncAction.MergeOnly : CloudSyncAction.MergeAndUpload;
        }

        /// <summary>
        /// The cloud id this profile is bound to after a successful merge/upload: the account key when known, else the
        /// cloud copy's id, else the existing binding, else <paramref name="newId"/>.
        /// </summary>
        public static string ResolveCloudId(string boundCloudId, string accountKey, string remoteCloudId, string newId)
        {
            if (!string.IsNullOrEmpty(accountKey))
            {
                return accountKey;
            }

            if (!string.IsNullOrEmpty(remoteCloudId))
            {
                return remoteCloudId;
            }

            return !string.IsNullOrEmpty(boundCloudId) ? boundCloudId : newId;
        }
    }
}
