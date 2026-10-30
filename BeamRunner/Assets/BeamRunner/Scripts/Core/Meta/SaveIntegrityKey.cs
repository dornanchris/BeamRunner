namespace BeamRunner.Core.Meta
{
    /// <summary>
    /// The app key behind <see cref="SaveEnvelope"/> signatures. It is assembled at run time instead of being one string
    /// literal (literals are trivially visible in IL2CPP's metadata) — obfuscation only: this is a deterrent against
    /// casual save editing, not a secret that survives a determined reverse engineer.
    /// </summary>
    /// <remarks>
    /// NEVER change these values: every save and cloud snapshot written by a shipped build is signed with them, and a
    /// different key turns all of them into "tampered" documents. <c>SaveIntegrityTests.GoldenDocument_*</c> pins the key.
    /// </remarks>
    public static class SaveIntegrityKey
    {
        private static readonly uint[] s_words =
        {
            0xCE5176FBu, 0xC584D886u, 0x853F4016u, 0xC8B7B0C2u,
            0x25BFBCB0u, 0xF80B7B83u, 0x153EAB27u, 0x667A3D01u,
        };

        private const uint Mix = 0x9E3779B9u;

        /// <summary>Returns a fresh copy of the 32-byte key.</summary>
        public static byte[] Get()
        {
            var key = new byte[s_words.Length * 4];
            uint state = Mix;
            for (int i = 0; i < s_words.Length; i++)
            {
                state = unchecked(state * 1664525u + 1013904223u);
                uint w = s_words[i] ^ state;
                key[i * 4] = (byte)(w >> 24);
                key[i * 4 + 1] = (byte)(w >> 16);
                key[i * 4 + 2] = (byte)(w >> 8);
                key[i * 4 + 3] = (byte)w;
            }

            return key;
        }
    }
}
