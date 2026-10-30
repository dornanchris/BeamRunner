using BeamRunner.Core.Meta;
using NUnit.Framework;

namespace BeamRunner.Core.Tests
{
    /// <summary>The guards around a cloud merge (<see cref="CloudSyncPolicy"/>): account switches, integrity, versions.</summary>
    public class CloudSyncPolicyTests
    {
        private const string AccountA = "pgs-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        private const string AccountB = "pgs-bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        private const string CloudA = "cloud-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        private const string CloudB = "cloud-bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

        private static int Current
        {
            get { return PlayerProfile.CurrentVersion; }
        }

        private static CloudSyncAction Signed(string bound, string account, string remoteCloudId, bool merge = false,
            int remoteVersion = -1)
        {
            return CloudSyncPolicy.Decide(bound, account, SaveDocumentKind.Signed, SaveEnvelope.FormatVersion,
                remoteVersion < 0 ? Current : remoteVersion, remoteCloudId, merge);
        }

        [Test]
        public void FirstSync_EmptyCloud_Uploads()
        {
            Assert.AreEqual(CloudSyncAction.UploadLocal,
                CloudSyncPolicy.Decide("", AccountA, SaveDocumentKind.Missing, 0, 0, "", false));
            Assert.AreEqual(CloudSyncAction.UploadLocal,
                CloudSyncPolicy.Decide(null, null, SaveDocumentKind.Missing, 0, 0, null, false));
        }

        [Test]
        public void FirstSync_ExistingCloud_Merges()
        {
            // A fresh install (or a pre-cloud save) meets the account's cloud copy: merge, never replace.
            Assert.AreEqual(CloudSyncAction.MergeAndUpload, Signed("", AccountA, AccountA));
            Assert.AreEqual(CloudSyncAction.MergeAndUpload, Signed("", "", CloudA));
        }

        [Test]
        public void SameAccount_Merges()
        {
            Assert.AreEqual(CloudSyncAction.MergeAndUpload, Signed(AccountA, AccountA, AccountA));
            Assert.AreEqual(CloudSyncAction.MergeAndUpload, Signed(CloudA, "", CloudA));
        }

        [Test]
        public void AccountSwitch_KnownAccount_IsAConflict_EvenWhenTheOtherCloudIsEmptyOrBroken()
        {
            // The profile was synced with account A; account B signed in on this device.
            Assert.AreEqual(CloudSyncAction.AccountConflict, Signed(AccountA, AccountB, AccountB));
            Assert.AreEqual(CloudSyncAction.AccountConflict,
                CloudSyncPolicy.Decide(AccountA, AccountB, SaveDocumentKind.Missing, 0, 0, "", false));
            Assert.AreEqual(CloudSyncAction.AccountConflict,
                CloudSyncPolicy.Decide(AccountA, AccountB, SaveDocumentKind.Tampered, 1, 0, "", false));
        }

        [Test]
        public void AccountSwitch_PlayerConfirmed_Merges()
        {
            Assert.AreEqual(CloudSyncAction.MergeAndUpload, Signed(AccountA, AccountB, AccountB, true));
            Assert.AreEqual(CloudSyncAction.UploadLocal,
                CloudSyncPolicy.Decide(AccountA, AccountB, SaveDocumentKind.Missing, 0, 0, "", true));
        }

        [Test]
        public void AccountSwitch_UnknownAccount_DetectedByTheCloudCopysId()
        {
            // iCloud key-value store: no account key; the other account's cloud copy carries another id.
            Assert.AreEqual(CloudSyncAction.AccountConflict, Signed(CloudA, "", CloudB));
            Assert.AreEqual(CloudSyncAction.MergeAndUpload, Signed(CloudA, "", CloudB, true));
            // A cloud copy without an id (written before binding existed) is merged.
            Assert.AreEqual(CloudSyncAction.MergeAndUpload, Signed(CloudA, "", ""));
        }

        [Test]
        public void KnownAccount_IgnoresTheCloudCopysOwnId()
        {
            // With an account key the key decides; a stale id inside the cloud copy does not block the merge.
            Assert.AreEqual(CloudSyncAction.MergeAndUpload, Signed(AccountA, AccountA, CloudB));
        }

        [Test]
        public void UnusableCloudData_IsReplaced_NeverMerged()
        {
            foreach (SaveDocumentKind kind in new[] { SaveDocumentKind.Tampered, SaveDocumentKind.Legacy, SaveDocumentKind.Damaged })
            {
                Assert.AreEqual(CloudSyncAction.UploadLocal,
                    CloudSyncPolicy.Decide(AccountA, AccountA, kind, kind == SaveDocumentKind.Legacy ? 0 : 1, 0, "", false), kind.ToString());
            }
        }

        [Test]
        public void NewerEnvelopeFormat_IsLeftAlone()
        {
            int newer = SaveEnvelope.FormatVersion + 1;
            Assert.AreEqual(CloudSyncAction.NeedsUpdate,
                CloudSyncPolicy.Decide(AccountA, AccountA, SaveDocumentKind.Damaged, newer, 0, "", false));
            Assert.AreEqual(CloudSyncAction.NeedsUpdate,
                CloudSyncPolicy.Decide("", "", SaveDocumentKind.Damaged, newer, 0, "", false));
        }

        [Test]
        public void NewerProfileVersion_MergesButNeverWritesBack()
        {
            Assert.AreEqual(CloudSyncAction.MergeOnly, Signed(AccountA, AccountA, AccountA, false, Current + 1));
            Assert.AreEqual(CloudSyncAction.MergeAndUpload, Signed(AccountA, AccountA, AccountA, false, Current));
            Assert.AreEqual(CloudSyncAction.MergeAndUpload, Signed(AccountA, AccountA, AccountA, false, 1));
        }

        [Test]
        public void ResolveCloudId_Priorities()
        {
            Assert.AreEqual(AccountB, CloudSyncPolicy.ResolveCloudId(AccountA, AccountB, CloudA, "new"));
            Assert.AreEqual(CloudB, CloudSyncPolicy.ResolveCloudId(CloudA, "", CloudB, "new"));
            Assert.AreEqual(CloudA, CloudSyncPolicy.ResolveCloudId(CloudA, "", "", "new"));
            Assert.AreEqual(CloudA, CloudSyncPolicy.ResolveCloudId(CloudA, null, null, "new"));
            Assert.AreEqual("new", CloudSyncPolicy.ResolveCloudId("", "", "", "new"));
            Assert.AreEqual("new", CloudSyncPolicy.ResolveCloudId(null, null, null, "new"));
        }

        [Test]
        public void AfterAConfirmedMerge_TheNewAccountIsBound_SoTheNextSyncIsNotAConflict()
        {
            string bound = CloudSyncPolicy.ResolveCloudId(AccountA, AccountB, AccountB, "new");
            Assert.AreEqual(CloudSyncAction.MergeAndUpload, Signed(bound, AccountB, AccountB));
        }
    }
}
