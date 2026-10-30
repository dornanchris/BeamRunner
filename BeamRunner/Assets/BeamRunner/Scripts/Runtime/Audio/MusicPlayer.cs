using System;
using BeamRunner.Core.Audio;
using UnityEngine;

namespace BeamRunner.Audio
{
    /// <summary>
    /// Plays the music arrangements: one looping AudioSource (+ low-pass filter for ducking) per stem (the always-on
    /// layers come pre-mixed as one Base stem, see <see cref="MusicLayers"/>), all stems
    /// of an arrangement started on the same DSP sample with <see cref="AudioSource.PlayScheduled"/>, equal-power
    /// crossfades between arrangements, intensity/Overdrive layer fades for the Run stems, and a beat clock derived
    /// from the scheduled DSP start (extrapolated between DSP updates and re-synced from <c>timeSamples</c> after
    /// pauses, ads or device changes).
    /// </summary>
    internal sealed class MusicPlayer
    {
        /// <summary>Music bus gain: leaves headroom for SFX on top of the (−14 LUFS) music.</summary>
        private const float BusGain = 0.8f;
        private const float StartLead = 0.08f;
        private const float DuckCutoffHz = 800f;
        private const float OpenCutoffHz = 22000f;

        private readonly Transform _root;
        private readonly Arrangement[] _arrangements = new Arrangement[4];
        private MusicMode _target = MusicMode.Silent;
        private MusicMode _effective = MusicMode.Silent;
        private float _intensity;
        private bool _overdrive;
        private float _duck;
        private float _duckTarget;
        private float _volume = 1f;
        private float _resyncTimer;
        private bool _forceResync;
        private double _lastRawDsp = -1.0;
        private double _lastRawReal;
        private double _lastEstimate;

        public MusicPlayer(Transform root)
        {
            _root = root;
        }

        public MusicMode Target => _target;

        /// <summary>1 on each beat of the audible arrangement, decaying quickly; 0 when silent.</summary>
        public float BeatPulse { get; private set; }

        /// <summary>Position within the current beat (0..1).</summary>
        public float BeatPhase { get; private set; }

        /// <summary>Beats elapsed since the audible arrangement started (useful for bar-level effects).</summary>
        public double BeatTime { get; private set; }

        public bool HasArrangement(MusicMode mode)
        {
            int i = (int)mode;
            return i >= 0 && i < _arrangements.Length && _arrangements[i] != null;
        }

        public void Add(LoadedArrangement data)
        {
            int i = (int)data.Mode;
            if (i <= 0 || i >= _arrangements.Length || _arrangements[i] != null)
            {
                return;
            }

            var arr = new Arrangement(data, _root);
            _arrangements[i] = arr;
            for (int s = 0; s < arr.Layer.Length; s++)
            {
                arr.Layer[s] = LayerTarget(data.Mode, data.Stems[s]);
            }
        }

        public void SetTarget(MusicMode mode)
        {
            _target = mode;
        }

        public void SetIntensity(float intensity)
        {
            _intensity = Mathf.Clamp01(intensity);
        }

        public void SetOverdrive(bool on)
        {
            _overdrive = on;
        }

        public void SetDuck(bool duck)
        {
            _duckTarget = duck ? 1f : 0f;
        }

        public void SetVolume(float gain)
        {
            _volume = Mathf.Clamp01(gain);
        }

        /// <summary>Call after app pause/focus changes: re-derive the beat clock from the sources.</summary>
        public void RequestResync()
        {
            _forceResync = true;
            _lastRawDsp = -1.0;
        }

        /// <summary>Restarts the target arrangement (the audio system was reset and stopped every source, clips intact).</summary>
        public void Restart()
        {
            for (int i = 0; i < _arrangements.Length; i++)
            {
                Arrangement a = _arrangements[i];
                if (a != null && a.Playing)
                {
                    a.Stop();
                    a.Fade = 0f;
                }
            }

            _lastRawDsp = -1.0;
            _lastEstimate = 0.0;
        }

        /// <summary>
        /// Stops and removes every arrangement (their sources are destroyed; the clips belong to the loader). Used when
        /// the audio system was reset: script-created clips are lost then, so the bank is re-rendered and the fresh
        /// arrangements are <see cref="Add"/>ed again; the target/intensity/duck state is kept and applies to them.
        /// </summary>
        public void Clear()
        {
            for (int i = 0; i < _arrangements.Length; i++)
            {
                Arrangement a = _arrangements[i];
                if (a == null)
                {
                    continue;
                }

                a.Stop();
                a.DestroySources();
                _arrangements[i] = null;
            }

            _lastRawDsp = -1.0;
            _lastEstimate = 0.0;
        }

        public void Tick(float dt)
        {
            // Keep the current music until the requested arrangement has actually loaded.
            if (_target == MusicMode.Silent || HasArrangement(_target))
            {
                _effective = _target;
            }

            double now = DspNow();
            _duck = Mathf.MoveTowards(_duck, _duckTarget, dt / 0.35f);
            float duckShape = _duck * _duck * (3f - 2f * _duck);
            float duckGain = 1f - 0.45f * duckShape;
            float cutoff = _duck > 0.001f ? OpenCutoffHz * Mathf.Pow(DuckCutoffHz / OpenCutoffHz, duckShape) : OpenCutoffHz;
            bool filterOn = _duck > 0.001f;

            Arrangement beatRef = null;
            for (int i = 1; i < _arrangements.Length; i++)
            {
                Arrangement a = _arrangements[i];
                if (a == null)
                {
                    continue;
                }

                bool isTarget = a.Data.Mode == _effective;
                if (isTarget && !a.Playing)
                {
                    a.Start(AudioSettings.dspTime + StartLead);
                    for (int s = 0; s < a.Layer.Length; s++)
                    {
                        a.Layer[s] = LayerTarget(a.Data.Mode, a.Data.Stems[s]);
                    }
                }

                if (!a.Playing)
                {
                    continue;
                }

                float fadeIn = a.Data.Mode == MusicMode.GameOver ? 0.7f : 1.0f;
                a.Fade = Mathf.MoveTowards(a.Fade, isTarget ? 1f : 0f, dt / (isTarget ? fadeIn : 1.2f));
                if (!isTarget && a.Fade <= 0f)
                {
                    a.Stop();
                    continue;
                }

                float fadeGain = Mathf.Sin(a.Fade * Mathf.PI * 0.5f) * _volume * BusGain * duckGain;
                for (int s = 0; s < a.Sources.Length; s++)
                {
                    float target = LayerTarget(a.Data.Mode, a.Data.Stems[s]);
                    float speed = target > a.Layer[s] ? 0.8f : 0.6f;
                    a.Layer[s] = Mathf.MoveTowards(a.Layer[s], target, dt * speed);
                    float l = a.Layer[s];
                    a.Sources[s].volume = fadeGain * l * l * (3f - 2f * l);
                    AudioLowPassFilter f = a.Filters[s];
                    if (f != null)
                    {
                        if (f.enabled != filterOn)
                        {
                            f.enabled = filterOn;
                        }

                        if (filterOn)
                        {
                            f.cutoffFrequency = cutoff;
                        }
                    }
                }

                // Beat reference: the target arrangement if it plays, else the loudest one still fading out.
                if (isTarget || beatRef == null || (beatRef.Data.Mode != _effective && a.Fade > beatRef.Fade))
                {
                    beatRef = a;
                }
            }

            UpdateBeat(beatRef, now, dt);
        }

        private void UpdateBeat(Arrangement a, double now, float dt)
        {
            if (a == null)
            {
                BeatPulse = Mathf.MoveTowards(BeatPulse, 0f, dt * 4f);
                BeatPhase = 0f;
                return;
            }

            _resyncTimer -= dt;
            if (_forceResync || _resyncTimer <= 0f)
            {
                Resync(a, now);
                _resyncTimer = 0.5f;
                _forceResync = false;
            }

            double elapsed = now - a.StartDsp;
            if (elapsed < 0.0)
            {
                BeatPhase = 0f;
                BeatPulse = 0f;
                return;
            }

            double beats = elapsed / a.Data.SecondsPerBeat;
            BeatTime = beats;
            float phase = (float)(beats - Math.Floor(beats));
            BeatPhase = phase;
            float fade = Mathf.Sin(a.Fade * Mathf.PI * 0.5f);
            BeatPulse = Mathf.Exp(-6f * phase) * fade;
        }

        /// <summary>Corrects the DSP start estimate from the playback position (handles pauses and ads).</summary>
        private static void Resync(Arrangement a, double now)
        {
            AudioSource src = a.Sources[0];
            if (src == null || !src.isPlaying || now < a.StartDsp + 0.25)
            {
                return;
            }

            double loop = a.Data.LoopSeconds;
            double position = src.timeSamples / (double)a.Data.SampleRate;
            double expected = (now - a.StartDsp) % loop;
            double diff = position - expected;
            if (diff > loop * 0.5)
            {
                diff -= loop;
            }
            else if (diff < -loop * 0.5)
            {
                diff += loop;
            }

            if (Math.Abs(diff) > 0.05)
            {
                a.StartDsp -= diff;
            }
        }

        /// <summary>DSP time extrapolated with real time between the audio thread's (buffer-sized) updates.</summary>
        private double DspNow()
        {
            double raw = AudioSettings.dspTime;
            double real = Time.realtimeSinceStartupAsDouble;
            if (raw != _lastRawDsp)
            {
                _lastRawDsp = raw;
                _lastRawReal = real;
            }

            double estimate = raw + Math.Min(real - _lastRawReal, 0.1);
            if (estimate < _lastEstimate && _lastEstimate - estimate < 0.25)
            {
                estimate = _lastEstimate;
            }

            _lastEstimate = estimate;
            return estimate;
        }

        private float LayerTarget(MusicMode mode, MusicStem stem)
        {
            // Only the layers MusicLayers marks as faded get a clip of their own: the always-on layers arrive
            // pre-mixed into one Base stem (identical gain for all of them is what makes that pre-mix exact).
            if (!MusicLayers.IsFaded(mode, stem))
            {
                return 1f;
            }

            switch (stem)
            {
                case MusicStem.Arp:
                    return _overdrive ? 1f : Smooth01((_intensity - 0.3f) / 0.25f);
                case MusicStem.Lead:
                    return _overdrive ? 1f : Smooth01((_intensity - 0.85f) / 0.12f);
                default:
                    return 1f;
            }
        }

        private static float Smooth01(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        /// <summary>Runtime state of one arrangement.</summary>
        private sealed class Arrangement
        {
            public Arrangement(LoadedArrangement data, Transform root)
            {
                Data = data;
                int n = data.Clips.Length;
                Sources = new AudioSource[n];
                Filters = new AudioLowPassFilter[n];
                Layer = new float[n];
                for (int i = 0; i < n; i++)
                {
                    var go = new GameObject("Music_" + data.Mode + "_" + data.Stems[i]);
                    go.transform.SetParent(root, false);
                    AudioSource src = go.AddComponent<AudioSource>();
                    AudioSourceSetup.Configure(src, 0);
                    src.clip = data.Clips[i];
                    src.loop = true;
                    src.volume = 0f;
                    Sources[i] = src;
                    AudioLowPassFilter lp = go.AddComponent<AudioLowPassFilter>();
                    lp.cutoffFrequency = OpenCutoffHz;
                    lp.lowpassResonanceQ = 1f;
                    lp.enabled = false;
                    Filters[i] = lp;
                }
            }

            public LoadedArrangement Data { get; }

            public AudioSource[] Sources { get; }

            public AudioLowPassFilter[] Filters { get; }

            public float[] Layer { get; }

            public float Fade;
            public double StartDsp;
            public bool Playing;

            public void Start(double dspTime)
            {
                for (int i = 0; i < Sources.Length; i++)
                {
                    AudioSource src = Sources[i];
                    src.clip = Data.Clips[i];
                    src.loop = true;
                    src.volume = 0f;
                    src.PlayScheduled(dspTime);
                }

                StartDsp = dspTime;
                Playing = true;
                Fade = 0f;
            }

            public void Stop()
            {
                for (int i = 0; i < Sources.Length; i++)
                {
                    if (Sources[i] != null)
                    {
                        Sources[i].Stop();
                    }
                }

                Playing = false;
            }

            public void DestroySources()
            {
                for (int i = 0; i < Sources.Length; i++)
                {
                    if (Sources[i] != null)
                    {
                        UnityEngine.Object.Destroy(Sources[i].gameObject);
                    }
                }
            }
        }
    }
}
