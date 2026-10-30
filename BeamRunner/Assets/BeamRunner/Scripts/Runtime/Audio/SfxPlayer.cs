using BeamRunner.Core.Audio;
using UnityEngine;

namespace BeamRunner.Audio
{
    /// <summary>
    /// Pool of one-shot voices with round-robin allocation, per-effect priority, voice stealing (lowest priority,
    /// then oldest), a per-effect instance cap, same-frame de-duplication and a subtle random pitch variation for
    /// quickly repeated effects. <see cref="Play"/> allocates nothing.
    /// </summary>
    internal sealed class SfxPlayer
    {
        private const float MinRetrigger = 0.03f;
        private const float RepeatWindow = 0.5f;

        private readonly Voice[] _voices;
        private readonly AudioClip[] _clips;
        private readonly float[] _lastPlay;
        private int _next;
        private float _volume = 1f;

        public SfxPlayer(Transform root, int voiceCount)
        {
            int n = AudioBank.SfxCount;
            _clips = new AudioClip[n];
            _lastPlay = new float[n];
            for (int i = 0; i < n; i++)
            {
                _lastPlay[i] = -100f;
            }

            var go = new GameObject("Sfx");
            go.transform.SetParent(root, false);
            _voices = new Voice[voiceCount];
            for (int i = 0; i < voiceCount; i++)
            {
                AudioSource src = go.AddComponent<AudioSource>();
                AudioSourceSetup.Configure(src, 64);
                _voices[i] = new Voice(src);
            }
        }

        public int LoadedCount { get; private set; }

        public void SetClip(SfxId id, AudioClip clip)
        {
            int i = (int)id;
            if (i >= 0 && i < _clips.Length)
            {
                if (_clips[i] == null && clip != null)
                {
                    LoadedCount++;
                }

                _clips[i] = clip;
            }
        }

        public bool HasClip(SfxId id)
        {
            int i = (int)id;
            return i >= 0 && i < _clips.Length && _clips[i] != null;
        }

        public void SetVolume(float gain)
        {
            _volume = Mathf.Clamp01(gain);
        }

        public void StopAll()
        {
            for (int i = 0; i < _voices.Length; i++)
            {
                _voices[i].Source.Stop();
                _voices[i].Id = -1;
            }
        }

        /// <summary>Stops every voice and forgets every clip (they are re-set as the bank reloads).</summary>
        public void ClearClips()
        {
            StopAll();
            for (int i = 0; i < _voices.Length; i++)
            {
                _voices[i].Source.clip = null;
            }

            for (int i = 0; i < _clips.Length; i++)
            {
                _clips[i] = null;
            }

            LoadedCount = 0;
        }

        /// <param name="vary">Apply ±3% random pitch when the same effect repeats within half a second.</param>
        /// <param name="pan">Stereo balance −1 (left) .. 1 (right).</param>
        public bool Play(SfxId id, float volume, float pitch, bool vary, float pan)
        {
            int index = (int)id;
            if (index < 0 || index >= _clips.Length)
            {
                return false;
            }

            AudioClip clip = _clips[index];
            if (clip == null || volume <= 0f || _volume <= 0f)
            {
                return false;
            }

            float now = Time.unscaledTime;
            float since = now - _lastPlay[index];
            if (since < MinRetrigger)
            {
                return false;
            }

            _lastPlay[index] = now;
            int priority = Priority(id);
            int cap = MaxInstances(id);

            Voice free = null;
            Voice victim = null;
            Voice oldestSame = null;
            int sameCount = 0;
            int n = _voices.Length;
            for (int k = 0; k < n; k++)
            {
                Voice v = _voices[(_next + k) % n];
                if (!v.Source.isPlaying)
                {
                    if (free == null)
                    {
                        free = v;
                    }

                    continue;
                }

                if (v.Id == index)
                {
                    sameCount++;
                    if (oldestSame == null || v.StartTime < oldestSame.StartTime)
                    {
                        oldestSame = v;
                    }
                }

                if (v.Priority <= priority &&
                    (victim == null || v.Priority < victim.Priority || (v.Priority == victim.Priority && v.StartTime < victim.StartTime)))
                {
                    victim = v;
                }
            }

            Voice chosen = sameCount >= cap && oldestSame != null ? oldestSame : (free ?? victim);
            if (chosen == null)
            {
                return false;
            }

            _next = (_next + 1) % n;
            if (vary && since < RepeatWindow)
            {
                pitch *= Random.Range(0.97f, 1.03f);
            }

            AudioSource src = chosen.Source;
            src.Stop();
            src.clip = clip;
            src.volume = Mathf.Clamp01(volume) * _volume;
            src.pitch = Mathf.Clamp(pitch, 0.1f, 3f);
            src.panStereo = Mathf.Clamp(pan, -1f, 1f);
            src.Play();
            chosen.Id = index;
            chosen.Priority = priority;
            chosen.StartTime = now;
            return true;
        }

        /// <summary>Higher = more important (steals lower-priority voices when the pool is full).</summary>
        private static int Priority(SfxId id)
        {
            switch (id)
            {
                case SfxId.Crash:
                case SfxId.OverdriveStart:
                case SfxId.Revive:
                case SfxId.NewRecord:
                    return 10;
                case SfxId.GateSmash:
                case SfxId.ShieldBreak:
                case SfxId.CountdownGo:
                case SfxId.CountdownTick:
                case SfxId.RankUp:
                    return 8;
                case SfxId.ZoneChange:
                case SfxId.OverdriveEnd:
                case SfxId.OverdriveReady:
                case SfxId.Purchase:
                case SfxId.Reward:
                case SfxId.MissionComplete:
                    return 7;
                case SfxId.PowerUpPickup:
                case SfxId.ShieldUp:
                case SfxId.MagnetStart:
                case SfxId.NearMiss:
                case SfxId.UiError:
                    return 6;
                case SfxId.UiClick:
                case SfxId.UiBack:
                case SfxId.UiToggle:
                case SfxId.Milestone:
                    return 5;
                case SfxId.MultiplierUp:
                case SfxId.SlipstreamEnter:
                    return 4;
                case SfxId.GemPickup:
                    return 3;
                case SfxId.GatePass:
                    return 1;
                default:
                    return 5;
            }
        }

        private static int MaxInstances(SfxId id)
        {
            switch (id)
            {
                case SfxId.GemPickup:
                    return 4;
                case SfxId.NearMiss:
                case SfxId.GatePass:
                case SfxId.MultiplierUp:
                case SfxId.UiClick:
                case SfxId.UiBack:
                case SfxId.UiToggle:
                    return 2;
                default:
                    return 1;
            }
        }

        private sealed class Voice
        {
            public Voice(AudioSource source)
            {
                Source = source;
            }

            public AudioSource Source { get; }

            public int Id = -1;
            public int Priority;
            public float StartTime;
        }
    }
}
