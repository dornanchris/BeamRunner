namespace BeamRunner.Services
{
    /// <summary>
    /// Persistent storage for the player profile document (a single JSON string). Implementations never throw:
    /// failures are logged and reported through return values. Main thread only.
    /// </summary>
    public interface ISaveStorage
    {
        /// <summary>
        /// Returns the last good saved document, or <c>null</c> when nothing valid exists (first launch or
        /// unrecoverable corruption). Falls back to the backup copy when the primary copy is damaged.
        /// </summary>
        string Load();

        /// <summary>Persists <paramref name="json"/> atomically. Returns false if it could not be written.</summary>
        bool Save(string json);

        /// <summary>Removes the saved document and all backups.</summary>
        void Delete();
    }
}
