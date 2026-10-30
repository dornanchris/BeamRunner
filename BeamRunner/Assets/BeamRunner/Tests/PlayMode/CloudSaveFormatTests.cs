using BeamRunner.Core.Meta;
using NUnit.Framework;
using UnityEngine;

namespace BeamRunner.PlayModeTests
{
    /// <summary>
    /// Signed saves and cloud merges through Unity's real <see cref="JsonUtility"/> (what ProfileStore and CloudSync use),
    /// without booting the game: the envelope carries JsonUtility's output verbatim, the gem ledger and sync state
    /// survive serialization, and a pre-cloud save merges without losing gems. The Core tests cover the rules with plain
    /// objects; these pin the serializer's side.
    /// </summary>
    [Category("Save")]
    public sealed class CloudSaveFormatTests
    {
        private const string Iid = "0123456789abcdef0123456789abcdef";

        private static PlayerProfile RoundTrip(PlayerProfile p)
        {
            PlayerProfile back = JsonUtility.FromJson<PlayerProfile>(JsonUtility.ToJson(p));
            back.Sanitize();
            return back;
        }

        [Test]
        public void SignedEnvelope_CarriesJsonUtilityOutputVerbatim()
        {
            PlayerProfile p = PlayerProfile.CreateNew();
            p.Gems = 1234;
            p.Wallet.DeviceId = "device-a-000000";
            WalletLedger.Reconcile(p);
            string json = JsonUtility.ToJson(p);

            string document = SaveEnvelope.Wrap(json, Iid, SaveIntegrityKey.Get());
            string payload;
            string iid;
            Assert.AreEqual(SaveDocumentKind.Signed, SaveEnvelope.Open(document, SaveIntegrityKey.Get(),
                s => JsonUtility.FromJson<PlayerProfile>(s) != null, out payload, out iid));
            Assert.AreEqual(json, payload);

            PlayerProfile back = JsonUtility.FromJson<PlayerProfile>(payload);
            Assert.AreEqual(1234, back.Gems);
            Assert.AreEqual(1, back.Wallet.Entries.Count);
            Assert.AreEqual(1234, back.Wallet.Entries[0].Earned);

            // Editing a value in the file is detected.
            string edited = document.Replace("\"Gems\":1234", "\"Gems\":999999");
            Assert.AreEqual(SaveDocumentKind.Tampered, SaveEnvelope.Open(edited, SaveIntegrityKey.Get(), null, out payload, out iid));
        }

        [Test]
        public void WalletAndSyncState_SurviveJsonUtility()
        {
            PlayerProfile p = PlayerProfile.CreateNew();
            p.Wallet.DeviceId = "device-a-000000";
            p.Wallet.Entries.Add(new WalletEntry { Device = "device-b-000000", Earned = 500, Spent = 120 });
            p.Gems = 380;
            p.Sync.Epoch = 2;
            p.Sync.CloudId = "pgs-00112233445566778899aabbccddeeff";
            p.Sync.LastSyncUtcTicks = 638000000000000000L;

            PlayerProfile back = RoundTrip(p);
            Assert.AreEqual("device-a-000000", back.Wallet.DeviceId);
            Assert.AreEqual(1, back.Wallet.Entries.Count);
            Assert.AreEqual(500, back.Wallet.Entries[0].Earned);
            Assert.AreEqual(120, back.Wallet.Entries[0].Spent);
            Assert.AreEqual(2, back.Sync.Epoch);
            Assert.AreEqual("pgs-00112233445566778899aabbccddeeff", back.Sync.CloudId);
            Assert.AreEqual(638000000000000000L, back.Sync.LastSyncUtcTicks);
        }

        [Test]
        public void PreCloudSave_MergesWithACloudCopy_WithoutLosingGems()
        {
            // A save without the ledger and sync objects (JsonUtility leaves the missing objects to Sanitize).
            const string oldSave = "{\"Version\":1,\"Gems\":900,\"PilotRank\":2,\"OwnedShips\":[\"arrow\",\"shuttle\",\"manta\"]}";
            PlayerProfile local = JsonUtility.FromJson<PlayerProfile>(oldSave);
            local.Sanitize();
            Assert.IsNotNull(local.Wallet);
            Assert.IsNotNull(local.Sync);
            local.Wallet.DeviceId = "device-a-000000";

            PlayerProfile cloud = PlayerProfile.CreateNew();
            cloud.Wallet.DeviceId = "device-b-000000";
            cloud.Gems = 300;
            WalletLedger.Reconcile(cloud);
            cloud.OwnedShips.Add("viper");
            cloud = RoundTrip(cloud);

            ProfileMerge.MergeInto(local, cloud);
            Assert.AreEqual(1200, local.Gems);
            CollectionAssert.Contains(local.OwnedShips, "manta");
            CollectionAssert.Contains(local.OwnedShips, "viper");
            Assert.AreEqual("device-a-000000", local.Wallet.DeviceId);

            // Merging the same cloud copy again (a second sync) changes nothing.
            ProfileMerge.MergeInto(local, RoundTrip(cloud));
            Assert.AreEqual(1200, local.Gems);
        }
    }
}
