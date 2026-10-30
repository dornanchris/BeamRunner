using BeamRunner.Core.Meta;
using NUnit.Framework;

namespace BeamRunner.Core.Tests
{
    /// <summary>Signed save documents (<see cref="SaveEnvelope"/>) and the load policy (<see cref="SaveRecovery"/>).</summary>
    public class SaveIntegrityTests
    {
        private const string Iid = "0123456789abcdef0123456789abcdef";
        private const string Payload = "{\"Version\":2,\"Gems\":1234}";

        // BeamRunner signs with its own key context ("beamrunner/save/v1/"); recomputed (HMAC-SHA256, checked outside
        // the game) when the key context was renamed from the LightLancer fork's, before any build shipped. If this test
        // fails, the app key or the signing scheme changed and every save written by a shipped build would be rejected
        // as tampered.
        private const string GoldenDocument =
            "{\"fieldSave\":1,\"iid\":\"0123456789abcdef0123456789abcdef\"," +
            "\"sig\":\"1224dd43465565660e377b5d87bffa53929f653d99ff693f667d32b3232f6d2b\"," +
            "\"data\":{\"Version\":2,\"Gems\":1234}}";

        private static byte[] Key
        {
            get { return SaveIntegrityKey.Get(); }
        }

        private static bool IsProfile(string json)
        {
            // Stand-in for ProfileStore.IsUsableSave (JsonUtility is not available to Core tests).
            return json.Contains("\"Version\"");
        }

        private static SaveDocumentKind Open(string document, out string payload)
        {
            string iid;
            return SaveEnvelope.Open(document, Key, IsProfile, out payload, out iid);
        }

        [Test]
        public void GoldenDocument_KeyAndSchemeNeverChange()
        {
            Assert.AreEqual("8c81a13f2ca2d8d5baa64980a81b833ff026d988ee0e1cb4830ab00d217b2d80", Hex(Key));
            Assert.AreEqual(GoldenDocument, SaveEnvelope.Wrap(Payload, Iid, Key));
            string payload;
            string iid;
            Assert.AreEqual(SaveDocumentKind.Signed, SaveEnvelope.Open(GoldenDocument, Key, IsProfile, out payload, out iid));
            Assert.AreEqual(Payload, payload);
            Assert.AreEqual(Iid, iid);
        }

        [Test]
        public void Wrap_ThenOpen_RoundTripsThePayloadVerbatim()
        {
            const string payload = "{\"Version\":2,\"Name\":\"a \\\"quoted\\\" }{ name\",\"List\":[1,2,{\"x\":\"}\"}]}";
            string doc = SaveEnvelope.Wrap(payload, Iid, Key);
            string back;
            Assert.AreEqual(SaveDocumentKind.Signed, Open(doc, out back));
            Assert.AreEqual(payload, back);
            Assert.IsTrue(SaveEnvelope.LooksLikeEnvelope(doc));
        }

        [Test]
        public void Open_ToleratesByteOrderMarkAndSurroundingWhitespace()
        {
            string payload;
            Assert.AreEqual(SaveDocumentKind.Signed, Open("﻿ \r\n" + GoldenDocument + "\n", out payload));
            Assert.AreEqual(Payload, payload);
        }

        [Test]
        public void EditedPayload_IsTampered()
        {
            string edited = GoldenDocument.Replace("\"Gems\":1234", "\"Gems\":999999");
            string payload;
            Assert.AreEqual(SaveDocumentKind.Tampered, Open(edited, out payload));
        }

        [Test]
        public void EditedInstallIdOrSignature_IsTampered()
        {
            string payload;
            Assert.AreEqual(SaveDocumentKind.Tampered, Open(GoldenDocument.Replace(Iid, "fedcba9876543210fedcba9876543210"), out payload));
            Assert.AreEqual(SaveDocumentKind.Tampered, Open(GoldenDocument.Replace("\"sig\":\"1224", "\"sig\":\"1225"), out payload));
        }

        [Test]
        public void ReformattedDocument_IsNotSigned()
        {
            // Pretty-printing changes the payload bytes: the signature covers the exact text.
            string pretty = GoldenDocument.Replace("{\"Version\":2,", "{ \"Version\": 2,");
            string payload;
            Assert.AreNotEqual(SaveDocumentKind.Signed, Open(pretty, out payload));
        }

        [Test]
        public void TruncatedOrGarbage_IsDamaged_NeverTampered()
        {
            string payload;
            for (int cut = 1; cut < GoldenDocument.Length; cut += 7)
            {
                SaveDocumentKind kind = Open(GoldenDocument.Substring(0, cut), out payload);
                Assert.AreNotEqual(SaveDocumentKind.Signed, kind, "cut " + cut);
                Assert.AreNotEqual(SaveDocumentKind.Tampered, kind, "a torn write must count as damage, cut " + cut);
            }

            Assert.AreEqual(SaveDocumentKind.Damaged, Open("\0\0\0garbage", out payload));
            Assert.AreEqual(SaveDocumentKind.Missing, Open("", out payload));
            Assert.AreEqual(SaveDocumentKind.Missing, Open(null, out payload));
            Assert.AreEqual(SaveDocumentKind.Missing, Open("  \n", out payload));
        }

        [Test]
        public void UnknownEnvelopeVersion_IsDamaged()
        {
            string payload;
            Assert.AreEqual(SaveDocumentKind.Damaged, Open(GoldenDocument.Replace("\"fieldSave\":1", "\"fieldSave\":2"), out payload));
        }

        [Test]
        public void PlainProfile_IsLegacy_AndUnusablePayloadIsDamaged()
        {
            string payload;
            Assert.AreEqual(SaveDocumentKind.Legacy, Open(Payload, out payload));
            Assert.AreEqual(Payload, payload);
            Assert.AreEqual(SaveDocumentKind.Damaged, Open("{\"NotAProfile\":1}", out payload));

            // A correctly signed document whose payload the validator rejects is damage too.
            string signedJunk = SaveEnvelope.Wrap("{\"NotAProfile\":1}", Iid, Key);
            Assert.AreEqual(SaveDocumentKind.Damaged, Open(signedJunk, out payload));
        }

        [Test]
        public void WrongKey_IsTampered()
        {
            byte[] other = SaveIntegrityKey.Get();
            other[0] ^= 1;
            string payload;
            string iid;
            Assert.AreEqual(SaveDocumentKind.Tampered, SaveEnvelope.Open(GoldenDocument, other, IsProfile, out payload, out iid));
        }

        [Test]
        public void Wrap_RejectsBadInput()
        {
            Assert.Throws<System.ArgumentException>(() => SaveEnvelope.Wrap(" {\"a\":1}", Iid, Key));
            Assert.Throws<System.ArgumentException>(() => SaveEnvelope.Wrap("[1]", Iid, Key));
            Assert.Throws<System.ArgumentException>(() => SaveEnvelope.Wrap(Payload, "short", Key));
            Assert.Throws<System.ArgumentException>(() => SaveEnvelope.Wrap(Payload, "has\"quote_______", Key));
            Assert.IsFalse(SaveEnvelope.IsValidInstallId(new string('a', 65)));
            Assert.IsTrue(SaveEnvelope.IsValidInstallId(System.Guid.NewGuid().ToString("N")));
        }

        // ------------------------------------------------------------------ recovery policy

        private static string Signed(int gems)
        {
            return SaveEnvelope.Wrap("{\"Version\":2,\"Gems\":" + gems + "}", Iid, Key);
        }

        private static string Legacy(int gems)
        {
            return "{\"Version\":1,\"Gems\":" + gems + "}";
        }

        private static SaveChoice Choose(bool legacyAllowed, params string[] copies)
        {
            return SaveRecovery.Choose(copies, Key, IsProfile, legacyAllowed);
        }

        [Test]
        public void Recovery_PrefersTheNewestSignedCopy()
        {
            SaveChoice c = Choose(false, Signed(3), Signed(2), Signed(1));
            Assert.AreEqual(0, c.Index);
            Assert.AreEqual(SaveDocumentKind.Signed, c.Kind);
            StringAssert.Contains("\"Gems\":3", c.Payload);
            Assert.AreEqual(Iid, c.InstallId);
        }

        [Test]
        public void Recovery_EditedMainFile_FallsBackToSignedBackup()
        {
            string edited = Signed(3).Replace("\"Gems\":3", "\"Gems\":99999");
            SaveChoice c = Choose(false, edited, null, Signed(2));
            Assert.AreEqual(2, c.Index, "the edit must be ineffective");
            StringAssert.Contains("\"Gems\":2", c.Payload);
            Assert.IsTrue(c.SawTampered);
        }

        [Test]
        public void Recovery_UnsignedReplacementAfterMigration_IsIgnored()
        {
            // Stripping the envelope does not help: the signed backup wins, and without one nothing is loaded.
            SaveChoice withBackup = Choose(false, Legacy(99999), null, Signed(2));
            Assert.AreEqual(2, withBackup.Index);

            SaveChoice alone = Choose(false, Legacy(99999), null, null);
            Assert.AreEqual(-1, alone.Index);
            Assert.IsTrue(alone.RejectedLegacy);
        }

        [Test]
        public void Recovery_LegacySave_IsAcceptedOnceForMigration()
        {
            SaveChoice c = Choose(true, Legacy(50), null, Legacy(40));
            Assert.AreEqual(0, c.Index);
            Assert.AreEqual(SaveDocumentKind.Legacy, c.Kind);
            Assert.IsNull(c.InstallId);

            // Right after the first signed save: main signed, backup = the migrated legacy file.
            SaveChoice after = Choose(false, Signed(60), null, Legacy(50));
            Assert.AreEqual(0, after.Index);
            Assert.AreEqual(SaveDocumentKind.Signed, after.Kind);
        }

        [Test]
        public void Recovery_CorruptionNeverWipesProgress()
        {
            string torn = Signed(60).Substring(0, 40);

            // Torn main file, signed backup: backup.
            Assert.AreEqual(2, Choose(false, torn, null, Signed(50)).Index);

            // Torn main file, only the pre-migration backup left: the legacy copy is still accepted (corruption, not
            // editing: no copy has a mismatching signature).
            SaveChoice c = Choose(false, torn, null, Legacy(50));
            Assert.AreEqual(2, c.Index);
            Assert.AreEqual(SaveDocumentKind.Legacy, c.Kind);
            Assert.IsTrue(c.SawDamaged);

            // ...but not when there is evidence of editing.
            string edited = Signed(60).Replace("\"Gems\":60", "\"Gems\":61");
            Assert.AreEqual(-1, Choose(false, torn, edited, Legacy(50)).Index);
        }

        [Test]
        public void Recovery_InterruptedSave_UsesTheCompleteTempFile()
        {
            SaveChoice c = Choose(false, null, Signed(7), Signed(6));
            Assert.AreEqual(1, c.Index);
        }

        [Test]
        public void Recovery_NothingUsable_ReturnsNone()
        {
            SaveChoice c = Choose(true, null, "", "garbage");
            Assert.AreEqual(-1, c.Index);
            Assert.IsNull(c.Payload);
            Assert.IsTrue(c.SawDamaged);
        }

        [Test]
        public void Recovery_ReportsEveryCopyKind()
        {
            var kinds = new SaveDocumentKind[3];
            SaveRecovery.Choose(new[] { Signed(1).Replace("1}", "2}"), Legacy(1), null }, Key, IsProfile, true, kinds);
            Assert.AreEqual(SaveDocumentKind.Tampered, kinds[0]);
            Assert.AreEqual(SaveDocumentKind.Legacy, kinds[1]);
            Assert.AreEqual(SaveDocumentKind.Missing, kinds[2]);
        }

        private static string Hex(byte[] bytes)
        {
            var sb = new System.Text.StringBuilder();
            foreach (byte b in bytes)
            {
                sb.Append(b.ToString("x2"));
            }

            return sb.ToString();
        }
    }
}
