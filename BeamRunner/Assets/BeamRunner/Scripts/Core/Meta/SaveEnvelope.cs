using System;
using System.Security.Cryptography;
using System.Text;

namespace BeamRunner.Core.Meta
{
    /// <summary>What a stored save document turned out to be (see <see cref="SaveEnvelope.Open"/>).</summary>
    public enum SaveDocumentKind
    {
        /// <summary>No document (missing or empty).</summary>
        Missing = 0,

        /// <summary>A signed envelope whose signature matches and whose payload is usable.</summary>
        Signed,

        /// <summary>A plain profile document written before saves were signed (usable payload, no envelope).</summary>
        Legacy,

        /// <summary>An intact envelope with a usable payload whose signature does NOT match: the file was edited.</summary>
        Tampered,

        /// <summary>
        /// Anything unreadable: truncated or garbage text, a broken envelope, an envelope of an unknown (newer) format,
        /// or a payload the validator rejects. Corruption always lands here, never in <see cref="Tampered"/>.
        /// </summary>
        Damaged,
    }

    /// <summary>
    /// Signed save documents. A profile JSON (the payload) is stored as
    /// <c>{"fieldSave":1,"iid":"&lt;install id&gt;","sig":"&lt;64 hex&gt;","data":&lt;payload&gt;}</c> — still one JSON
    /// object, still human-readable, but any edit of the payload (or of the header) no longer matches the signature.
    /// </summary>
    /// <remarks>
    /// <para><b>Deterrence, not security.</b> The signature is HMAC-SHA256 with a key derived from an app key compiled
    /// into the game (<see cref="SaveIntegrityKey"/>) and the install id stored in the envelope. Anyone who extracts the
    /// app key from the binary can sign any document; the goal is only to make casual editing of the save file
    /// ineffective. The install id is not secret: it makes signatures install-specific and travels inside the file, so
    /// a restore that loses PlayerPrefs (or a legitimate copy to another device) still verifies.</para>
    /// <para>The layout is fixed (header fields first, payload last, no whitespace) so the payload can be taken
    /// verbatim without a JSON parser; the payload itself is never re-serialised before verification.</para>
    /// </remarks>
    public static class SaveEnvelope
    {
        /// <summary>Envelope format written by this build. Documents with another value are <see cref="SaveDocumentKind.Damaged"/>.</summary>
        public const int FormatVersion = 1;

        /// <summary>Shortest and longest accepted install id.</summary>
        public const int MinInstallIdLength = 8;

        /// <summary>See <see cref="MinInstallIdLength"/>.</summary>
        public const int MaxInstallIdLength = 64;

        private const string Prefix = "{\"fieldSave\":";
        private const string IidField = ",\"iid\":\"";
        private const string SigField = "\",\"sig\":\"";
        private const string DataField = "\",\"data\":";
        private const int SignatureHexLength = 64;
        private const string KeyContext = "beamrunner/save/v1/";

        private static readonly UTF8Encoding s_utf8 = new UTF8Encoding(false);

        /// <summary>
        /// Wraps <paramref name="payload"/> (a JSON object without surrounding whitespace) into a signed envelope.
        /// Throws <see cref="ArgumentException"/> for an invalid install id or a payload that is not a JSON object.
        /// </summary>
        public static string Wrap(string payload, string installId, byte[] appKey)
        {
            if (!IsPlausiblePayload(payload))
            {
                throw new ArgumentException("The payload must be a JSON object without surrounding whitespace.", nameof(payload));
            }

            if (!IsValidInstallId(installId))
            {
                throw new ArgumentException("Invalid install id.", nameof(installId));
            }

            string sig = Sign(payload, installId, appKey);
            var sb = new StringBuilder(payload.Length + 128);
            sb.Append(Prefix).Append(FormatVersion.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Append(IidField).Append(installId)
                .Append(SigField).Append(sig)
                .Append(DataField).Append(payload)
                .Append('}');
            return sb.ToString();
        }

        /// <summary>
        /// Classifies <paramref name="document"/> and extracts its payload. <paramref name="payloadValidator"/> decides
        /// whether a payload is usable (e.g. "parses into a profile"); null accepts any JSON-object-shaped payload.
        /// <paramref name="payload"/> is set for <see cref="SaveDocumentKind.Signed"/>, <see cref="SaveDocumentKind.Legacy"/>
        /// and <see cref="SaveDocumentKind.Tampered"/> (the latter for diagnostics only — never load it);
        /// <paramref name="installId"/> for intact envelopes. Never throws.
        /// </summary>
        public static SaveDocumentKind Open(string document, byte[] appKey, Func<string, bool> payloadValidator,
            out string payload, out string installId)
        {
            payload = null;
            installId = null;
            if (string.IsNullOrEmpty(document))
            {
                return SaveDocumentKind.Missing;
            }

            string text = Trim(document);
            if (text.Length == 0)
            {
                return SaveDocumentKind.Missing;
            }

            if (!text.StartsWith(Prefix, StringComparison.Ordinal))
            {
                // Not an envelope: a pre-integrity profile if the validator accepts it.
                if (IsUsable(text, payloadValidator))
                {
                    payload = text;
                    return SaveDocumentKind.Legacy;
                }

                return SaveDocumentKind.Damaged;
            }

            string sig;
            string iid;
            string data;
            int version;
            if (!TryParse(text, out version, out iid, out sig, out data) || version != FormatVersion)
            {
                return SaveDocumentKind.Damaged;
            }

            installId = iid;
            bool usable = IsUsable(data, payloadValidator);
            bool signatureOk;
            try
            {
                signatureOk = FixedTimeEquals(sig, Sign(data, iid, appKey));
            }
            catch (Exception)
            {
                signatureOk = false;
            }

            if (!usable)
            {
                return SaveDocumentKind.Damaged;
            }

            payload = data;
            return signatureOk ? SaveDocumentKind.Signed : SaveDocumentKind.Tampered;
        }

        /// <summary>
        /// The envelope format number of <paramref name="document"/> (0 when it is not an envelope). A value above
        /// <see cref="FormatVersion"/> means a newer build wrote it: never overwrite such a document.
        /// </summary>
        public static int PeekFormatVersion(string document)
        {
            if (!LooksLikeEnvelope(document))
            {
                return 0;
            }

            string text = Trim(document);
            int version = 0;
            for (int i = Prefix.Length; i < text.Length && i - Prefix.Length < 9 && text[i] >= '0' && text[i] <= '9'; i++)
            {
                version = version * 10 + (text[i] - '0');
            }

            return version;
        }

        /// <summary>True when the text starts like an envelope (after whitespace / a byte order mark).</summary>
        public static bool LooksLikeEnvelope(string document)
        {
            return !string.IsNullOrEmpty(document) && Trim(document).StartsWith(Prefix, StringComparison.Ordinal);
        }

        /// <summary>Lower-case hex HMAC-SHA256 of the payload, keyed per install id (see remarks on the class).</summary>
        public static string Sign(string payload, string installId, byte[] appKey)
        {
            if (appKey == null || appKey.Length == 0)
            {
                throw new ArgumentException("An app key is required.", nameof(appKey));
            }

            byte[] derived;
            using (var kdf = new HMACSHA256(appKey))
            {
                derived = kdf.ComputeHash(s_utf8.GetBytes(KeyContext + (installId ?? string.Empty)));
            }

            byte[] mac;
            using (var hmac = new HMACSHA256(derived))
            {
                mac = hmac.ComputeHash(s_utf8.GetBytes((installId ?? string.Empty) + "\n" + (payload ?? string.Empty)));
            }

            return ToHex(mac);
        }

        /// <summary>Install ids are 8..64 characters of [0-9A-Za-z_-] (a GUID in "N" format is typical).</summary>
        public static bool IsValidInstallId(string installId)
        {
            if (string.IsNullOrEmpty(installId) || installId.Length < MinInstallIdLength || installId.Length > MaxInstallIdLength)
            {
                return false;
            }

            for (int i = 0; i < installId.Length; i++)
            {
                char c = installId[i];
                bool ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_' || c == '-';
                if (!ok)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryParse(string text, out int version, out string iid, out string sig, out string data)
        {
            version = 0;
            iid = null;
            sig = null;
            data = null;

            int i = Prefix.Length;
            int digitsStart = i;
            while (i < text.Length && text[i] >= '0' && text[i] <= '9' && i - digitsStart < 9)
            {
                version = version * 10 + (text[i] - '0');
                i++;
            }

            if (i == digitsStart || !Match(text, i, IidField))
            {
                return false;
            }

            i += IidField.Length;
            int iidEnd = text.IndexOf('"', i);
            if (iidEnd < 0)
            {
                return false;
            }

            iid = text.Substring(i, iidEnd - i);
            if (!IsValidInstallId(iid) || !Match(text, iidEnd, SigField))
            {
                return false;
            }

            i = iidEnd + SigField.Length;
            if (i + SignatureHexLength > text.Length)
            {
                return false;
            }

            sig = text.Substring(i, SignatureHexLength);
            if (!IsLowerHex(sig))
            {
                return false;
            }

            i += SignatureHexLength;
            if (!Match(text, i, DataField))
            {
                return false;
            }

            i += DataField.Length;
            if (text[text.Length - 1] != '}')
            {
                return false;
            }

            data = text.Substring(i, text.Length - 1 - i);
            return IsPlausiblePayload(data);
        }

        private static bool Match(string text, int index, string token)
        {
            return index >= 0 && index + token.Length <= text.Length &&
                   string.CompareOrdinal(text, index, token, 0, token.Length) == 0;
        }

        private static bool IsPlausiblePayload(string payload)
        {
            return !string.IsNullOrEmpty(payload) && payload.Length >= 2 && payload[0] == '{' &&
                   payload[payload.Length - 1] == '}';
        }

        private static bool IsUsable(string payload, Func<string, bool> validator)
        {
            if (!IsPlausiblePayload(payload))
            {
                return false;
            }

            if (validator == null)
            {
                return true;
            }

            try
            {
                return validator(payload);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string Trim(string document)
        {
            int start = 0;
            int end = document.Length - 1;
            while (start <= end && (IsWhite(document[start]) || document[start] == '﻿'))
            {
                start++;
            }

            while (end >= start && IsWhite(document[end]))
            {
                end--;
            }

            return start == 0 && end == document.Length - 1 ? document : document.Substring(start, end - start + 1);
        }

        private static bool IsWhite(char c)
        {
            return c == ' ' || c == '\t' || c == '\r' || c == '\n';
        }

        private static bool IsLowerHex(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
                {
                    return false;
                }
            }

            return true;
        }

        private static string ToHex(byte[] bytes)
        {
            const string digits = "0123456789abcdef";
            var chars = new char[bytes.Length * 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                chars[i * 2] = digits[bytes[i] >> 4];
                chars[i * 2 + 1] = digits[bytes[i] & 0xF];
            }

            return new string(chars);
        }

        /// <summary>Constant-time comparison (both strings are 64 lower-case hex characters when well formed).</summary>
        private static bool FixedTimeEquals(string a, string b)
        {
            if (a == null || b == null || a.Length != b.Length)
            {
                return false;
            }

            int diff = 0;
            for (int i = 0; i < a.Length; i++)
            {
                diff |= a[i] ^ b[i];
            }

            return diff == 0;
        }
    }
}
