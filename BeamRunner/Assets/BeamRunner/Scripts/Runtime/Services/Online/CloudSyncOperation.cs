using System;

namespace BeamRunner.Services
{
    /// <summary>
    /// One in-flight <see cref="ICloudSave.Sync"/>: guarantees a single completion (on a later frame), a watchdog in
    /// active app time, and exception-safe merging.
    /// </summary>
    internal sealed class CloudSyncOperation
    {
        private readonly Action<CloudSyncResult> _onDone;
        private readonly int _timer;

        /// <summary>Starts the watchdog; <paramref name="timeoutSeconds"/> counts only while the app runs.</summary>
        public CloudSyncOperation(Action<CloudSyncResult> onDone, float timeoutSeconds)
        {
            _onDone = onDone;
            _timer = ServiceRunner.Delay(timeoutSeconds, () =>
            {
                ServiceLog.Warn("Cloud save: no answer in time.");
                Finish(CloudSyncResult.Of(CloudSyncStatus.TimedOut, "timeout"));
            }, true);
        }

        /// <summary>True once finished (late platform callbacks must then only clean up).</summary>
        public bool IsDone { get; private set; }

        /// <summary>Reports the result once; later calls are ignored.</summary>
        public void Finish(CloudSyncResult result)
        {
            if (IsDone)
            {
                return;
            }

            IsDone = true;
            ServiceRunner.Cancel(_timer);
            if (result.Status != CloudSyncStatus.Success)
            {
                ServiceLog.Info("Cloud save: " + result.Status + (result.Detail.Length > 0 ? " (" + result.Detail + ")" : "") + ".");
            }

            Action<CloudSyncResult> onDone = _onDone;
            if (onDone != null)
            {
                ServiceRunner.Post(() => onDone(result));
            }
        }

        /// <summary>Runs the caller's merge; an exception declines the write.</summary>
        public CloudDocument Merge(Func<string, CloudDocument> merge, string cloudText)
        {
            if (merge == null)
            {
                return null;
            }

            try
            {
                return merge(cloudText);
            }
            catch (Exception e)
            {
                ServiceLog.Exception("Cloud save: merging failed; the cloud copy is left unchanged", e);
                return null;
            }
        }

        /// <summary>A completed-immediately operation result posted to a later frame (busy / unavailable).</summary>
        public static void Reject(Action<CloudSyncResult> onDone, CloudSyncStatus status, string detail)
        {
            if (onDone != null)
            {
                CloudSyncResult result = CloudSyncResult.Of(status, detail);
                ServiceRunner.Post(() => onDone(result));
            }
        }
    }
}
