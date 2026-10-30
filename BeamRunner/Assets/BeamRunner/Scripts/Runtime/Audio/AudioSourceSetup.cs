using UnityEngine;

namespace BeamRunner.Audio
{
    /// <summary>Common AudioSource configuration: 2D, no play-on-awake, no doppler/reverb zones.</summary>
    internal static class AudioSourceSetup
    {
        /// <param name="priority">Unity voice priority, 0 (most important) .. 256.</param>
        public static void Configure(AudioSource src, int priority)
        {
            src.playOnAwake = false;
            src.spatialBlend = 0f;
            src.dopplerLevel = 0f;
            src.bypassReverbZones = true;
            src.priority = priority;
            src.loop = false;
            src.volume = 1f;
            src.pitch = 1f;
        }
    }
}
