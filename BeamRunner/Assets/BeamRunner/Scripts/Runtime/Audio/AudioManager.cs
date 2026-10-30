using System;
using BeamRunner.Core.Audio;
using BeamRunner.Game;
using UnityEngine;

namespace BeamRunner.Audio
{
    /// <summary>
    /// The game's audio front end (ARCHITECTURE §6). All music and sound effects are synthesised procedurally by
    /// <see cref="AudioBank"/> on a worker thread at boot and uploaded into AudioClips progressively on the main thread.
    /// <para>
    /// Usage: add to a GameObject, call <see cref="Initialize"/> once at boot, wait for <see cref="IsReady"/> (menu music,
    /// all SFX and loops; Run/GameOver music keeps loading in the background and starts as soon as it exists), then
    /// drive it with <see cref="SetMusic"/>, <see cref="SetIntensity"/>, <see cref="SetOverdrive"/>,
    /// <see cref="SetEngine"/> / <see cref="SetSlipstream"/> (every frame during a run) and <see cref="Play"/>.
    /// Feed <see cref="BeatPulse"/> and <see cref="BeatTime"/> to <c>FieldGlobals.SetBeat</c>. Does not create an AudioListener.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AudioManager : MonoBehaviour
    {
        private const int SfxVoiceCount = 10;
        private const float GemChainWindow = 0.7f;

        // A-minor pentatonic steps (semitones) for chained gem pickups; 19 st ≈ pitch 3.0, Unity's maximum.
        private static readonly int[] GemSteps = { 0, 3, 5, 7, 10, 12, 15, 17, 19 };

        private AudioLoader _loader;
        private MusicPlayer _music;
        private SfxPlayer _sfx;
        private LoopVoice _engine;
        private LoopVoice _slipstream;
        private bool _initialized;
        private bool _menuLoaded;
        private int _loopsLoaded;
        private bool _errorLogged;
        private float _musicGain = 1f;
        private float _sfxGain = 1f;
        private MusicMode _requestedMusic = MusicMode.Silent;
        private float _intensity;
        private bool _overdrive;
        private bool _ducked;
        private float _engineTarget;
        private float _slipTarget;
        private float _lastGemTime = -100f;
        private int _gemStep;
        private bool _reloadPending;
        private float _reloadDelay;
        private int _reloadCount;
        private AudioClip _canary;

        private static readonly float[] CanaryPattern = { 0.25f, -0.5f, 0.125f, 0.75f };

        /// <summary>Quiet period after an audio configuration change before the bank is re-rendered (device changes come in bursts).</summary>
        private const float ReloadDebounceSeconds = 0.3f;

        /// <summary>
        /// Opt-in: set to true before <see cref="Initialize"/> to shrink the DSP buffer to 512 at runtime with
        /// <see cref="AudioSettings.Reset"/>. Off by default: a runtime reset re-initialises the whole audio output
        /// (known to misbehave on some iOS audio-session / Android Bluetooth setups) and the same latency is
        /// configured without any reset in ProjectSettings/AudioManager.asset (DSP Buffer Size = Good latency, 512).
        /// </summary>
        public static bool ConfigureDspBuffer = false;

        /// <summary>The most recently initialised manager (convenience; prefer an explicit reference).</summary>
        public static AudioManager Instance { get; private set; }

        /// <summary>True once the menu music, every SFX and both loops are playable (or loading gave up).</summary>
        public bool IsReady { get; private set; }

        /// <summary>True once every clip, including Run and GameOver music, has been created.</summary>
        public bool IsFullyLoaded => _loader != null && _loader.IsDone;

        /// <summary>Progress toward <see cref="IsReady"/> (0..1) — suitable for a boot/loading bar.</summary>
        public float LoadProgress
        {
            get
            {
                if (IsReady)
                {
                    return 1f;
                }

                return _loader == null ? 0f : Mathf.Clamp01(_loader.UploadedProgress / AudioBank.EssentialsProgress) * 0.99f;
            }
        }

        /// <summary>Progress of the whole bank (0..1).</summary>
        public float TotalProgress => _loader == null ? 0f : (_loader.IsDone ? 1f : _loader.UploadedProgress);

        /// <summary>Sample rate the bank is rendered at (44.1 kHz, or 32 kHz on low-memory devices).</summary>
        public int SampleRate { get; private set; }

        /// <summary>1 on each music beat, decaying quickly; 0 when no music plays. Feed to <c>FieldGlobals.SetBeat</c>.</summary>
        public float BeatPulse => _music != null ? _music.BeatPulse : 0f;

        /// <summary>Beats elapsed since the audible arrangement started (beat 0 is a bar downbeat).</summary>
        public double BeatTime => _music != null ? _music.BeatTime : 0.0;

        /// <summary>Position within the current beat (0..1) of the audible arrangement.</summary>
        public float BeatPhase => _music != null ? _music.BeatPhase : 0f;

        /// <summary>The arrangement most recently requested with <see cref="SetMusic"/>.</summary>
        public MusicMode CurrentMusic => _requestedMusic;

        /// <summary>
        /// Starts rendering the procedural bank on a worker thread. Safe to call more than once.
        /// </summary>
        /// <param name="sampleRate">0 = automatic (44100 Hz; 32000 Hz on devices with less than ~3 GB RAM).</param>
        public void Initialize(int sampleRate = 0)
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            Instance = this;
            ConfigureAudioSettings();
            SampleRate = sampleRate > 0 ? sampleRate : ChooseSampleRate();

            _music = new MusicPlayer(transform);
            _sfx = new SfxPlayer(transform, SfxVoiceCount);
            _engine = new LoopVoice(transform, "Loop_Engine", true);
            _engine.AttackSeconds = 0.25f;
            _engine.ReleaseSeconds = 0.6f;
            _slipstream = new LoopVoice(transform, "Loop_Slipstream", false);
            _slipstream.AttackSeconds = 0.15f;
            _slipstream.ReleaseSeconds = 0.45f;

            _music.SetTarget(_requestedMusic);
            _music.SetIntensity(_intensity);
            _music.SetOverdrive(_overdrive);
            _music.SetDuck(_ducked);
            _music.SetVolume(_musicGain);
            _sfx.SetVolume(_sfxGain);

            CreateCanary();
            AudioSettings.OnAudioConfigurationChanged += OnAudioConfigurationChanged;
            _loader = new AudioLoader(SampleRate, true, OnMusicLoaded, OnSfxLoaded, OnLoopLoaded);
            _loader.Start();
        }

        /// <summary>Plays a one-shot effect. No-op until that effect has loaded. Allocation-free.</summary>
        /// <param name="volume">0..1 multiplier on the effect's mastered level.</param>
        /// <param name="pitch">Playback rate (also shifts pitch); clamped to Unity's 0.1..3.</param>
        public void Play(SfxId id, float volume = 1f, float pitch = 1f)
        {
            if (_sfx == null)
            {
                return;
            }

            _sfx.Play(id, volume, pitch, Varies(id), 0f);
        }

        /// <summary>
        /// Plays a one-shot placed in the stereo field, e.g. a near miss on the side of the obstacle
        /// (<paramref name="pan"/> −1 = left, +1 = right). Allocation-free.
        /// </summary>
        public void PlayPanned(SfxId id, float pan, float volume = 1f, float pitch = 1f)
        {
            _sfx?.Play(id, volume, pitch, Varies(id), pan * 0.7f);
        }

        private static bool Varies(SfxId id)
        {
            return id != SfxId.GemPickup && id != SfxId.MultiplierUp && id != SfxId.CountdownTick && id != SfxId.CountdownGo;
        }

        /// <summary>
        /// Gem pickup that climbs the A-minor pentatonic with each consecutive pickup within 0.7 s (up to +19
        /// semitones), resetting after a pause.
        /// </summary>
        public void PlayGemChain(float volume = 1f)
        {
            float now = Time.unscaledTime;
            _gemStep = now - _lastGemTime <= GemChainWindow ? Mathf.Min(_gemStep + 1, GemSteps.Length - 1) : 0;
            _lastGemTime = now;
            float pitch = Mathf.Pow(2f, GemSteps[_gemStep] / 12f);
            _sfx?.Play(SfxId.GemPickup, volume, pitch, false, 0f);
        }

        /// <summary>Restarts the gem chain from its lowest note (e.g. at the start of a run).</summary>
        public void ResetGemChain()
        {
            _lastGemTime = -100f;
            _gemStep = 0;
        }

        /// <summary>
        /// Crossfades to an arrangement (stems start sample-locked via PlayScheduled). If it has not loaded yet it
        /// starts as soon as it has. <see cref="MusicMode.Silent"/> fades everything out.
        /// </summary>
        public void SetMusic(MusicMode mode)
        {
            _requestedMusic = mode;
            _music?.SetTarget(mode);
        }

        /// <summary>Run intensity 0..1: 0 = drums+bass+pads, ≥0.5 adds the arp, ~1 adds the lead hook.</summary>
        public void SetIntensity(float intensity01)
        {
            _intensity = Mathf.Clamp01(intensity01);
            _music?.SetIntensity(_intensity);
        }

        /// <summary>Overdrive brings in every Run layer (arp + lead) regardless of intensity.</summary>
        public void SetOverdrive(bool on)
        {
            _overdrive = on;
            _music?.SetOverdrive(on);
        }

        /// <summary>Engine hum: call every frame during a run. Pitch, level and brightness follow speed.</summary>
        public void SetEngine(float speed01, bool active)
        {
            float s = Mathf.Clamp01(speed01);
            _engineTarget = active ? Mathf.Lerp(0.3f, 0.5f, s) : 0f;
            if (_engine != null)
            {
                _engine.TargetPitch = Mathf.Lerp(0.75f, 1.55f, s);
                _engine.TargetCutoff = Mathf.Lerp(1200f, 9000f, s * s);
            }
        }

        /// <summary>Slipstream wind loop amount 0..1 (0 = silent).</summary>
        public void SetSlipstream(float amount01)
        {
            float a = Mathf.Clamp01(amount01);
            _slipTarget = a * 0.75f;
            if (_slipstream != null)
            {
                _slipstream.TargetPitch = 0.92f + 0.16f * a;
            }
        }

        /// <summary>Paused / game over: music sweeps through a low-pass and dips; engine and wind fade out.</summary>
        public void SetDuck(bool duck)
        {
            _ducked = duck;
            _music?.SetDuck(duck);
        }

        /// <summary>User volume sliders (0..1, linear slider positions; a perceptual curve is applied).</summary>
        public void SetVolumes(float music, float sfx)
        {
            _musicGain = Perceptual(music);
            _sfxGain = Perceptual(sfx);
            _music?.SetVolume(_musicGain);
            _sfx?.SetVolume(_sfxGain);
        }

        /// <summary>Stops every one-shot that is currently playing.</summary>
        public void StopAllSfx()
        {
            _sfx?.StopAll();
        }

        private static float Perceptual(float slider)
        {
            float x = Mathf.Clamp01(slider);
            return x * x;
        }

        private static int ChooseSampleRate()
        {
            int mb = SystemInfo.systemMemorySize;
            return mb > 0 && mb < 3000 ? 32000 : AudioBank.DefaultSampleRate;
        }

        private static void ConfigureAudioSettings()
        {
            if (!ConfigureDspBuffer || Application.platform == RuntimePlatform.WebGLPlayer)
            {
                return;
            }

            try
            {
                // Runs before any of our clips/sources exist, so a reset cannot stop or invalidate them.
                AudioConfiguration config = AudioSettings.GetConfiguration();
                bool change = false;
                if (config.dspBufferSize > 512)
                {
                    config.dspBufferSize = 512;
                    change = true;
                }

                if (config.numRealVoices < 32)
                {
                    config.numRealVoices = 32;
                    change = true;
                }

                if (change && !AudioSettings.Reset(config))
                {
                    FieldLog.Warn("AudioSettings.Reset refused the requested DSP configuration; keeping defaults.");
                }
            }
            catch (Exception e)
            {
                FieldLog.Warn("Could not configure the audio DSP buffer: " + e.Message);
            }
        }

        private void Start()
        {
            // Fallback in case the integrator forgot to initialise explicitly.
            Initialize();
        }

        private void Update()
        {
            if (!_initialized)
            {
                return;
            }

            float dt = Time.unscaledDeltaTime;
            if (_reloadPending)
            {
                _reloadDelay -= dt;
                if (_reloadDelay <= 0f)
                {
                    ReloadBank();
                }
            }

            _loader.Pump(IsReady ? 2.0 : 4.0);
            if (_loader.Error != null && !_errorLogged)
            {
                _errorLogged = true;
                FieldLog.Error("Procedural audio rendering failed: " + _loader.Error);
            }

            if (!IsReady && _loader.IsDone)
            {
                IsReady = true;
            }

            _music.Tick(dt);
            float loopsGain = _ducked ? 0f : 1f;
            _engine.TargetVolume = _engineTarget * loopsGain;
            _slipstream.TargetVolume = _slipTarget * loopsGain;
            _engine.Tick(dt, _sfxGain);
            _slipstream.Tick(dt, _sfxGain);
        }

        private void OnMusicLoaded(LoadedArrangement arrangement)
        {
            _music.Add(arrangement);
            if (arrangement.Mode == MusicMode.Menu)
            {
                _menuLoaded = true;
            }

            UpdateReady();
        }

        private void OnSfxLoaded(SfxId id, AudioClip clip)
        {
            _sfx.SetClip(id, clip);
            UpdateReady();
        }

        private void OnLoopLoaded(LoopId id, AudioClip clip)
        {
            if (id == LoopId.Engine)
            {
                _engine.Clip = clip;
            }
            else
            {
                _slipstream.Clip = clip;
            }

            _loopsLoaded++;
            UpdateReady();
        }

        private void UpdateReady()
        {
            if (!IsReady && _menuLoaded && _sfx.LoadedCount >= AudioBank.SfxCount && _loopsLoaded >= AudioBank.LoopCount)
            {
                IsReady = true;
            }
        }

        private void OnAudioConfigurationChanged(bool deviceWasChanged)
        {
            // Any audio reset (AudioSettings.Reset, or an output device change: headphones, Bluetooth, HDMI, USB) stops
            // every source AND loses every clip made with AudioClip.Create — Unity only reloads disk-based clips
            // (Unity Manual, "Audio Settings": "any AudioClips generated or modified by scripts are lost and have to
            // be recreated"). Merely restarting the sources would leave the game silent for the rest of the session,
            // so re-render the bank (a few hundred ms to a few s of worker time) unless the canary clip shows the data
            // survived. Debounced and done in Update (never re-enters Unity's audio reset from inside its callback).
            _reloadPending = true;
            _reloadDelay = ReloadDebounceSeconds;
        }

        /// <summary>A 4-sample script clip whose contents reveal whether an audio reset wiped script-created clips.</summary>
        private void CreateCanary()
        {
            try
            {
                _canary = AudioClip.Create("FieldAudioCanary", CanaryPattern.Length, 1, 8000, false);
                _canary.SetData(CanaryPattern, 0);
            }
            catch (Exception e)
            {
                _canary = null;
                FieldLog.Warn("Audio canary clip unavailable (" + e.Message + "); every audio reset re-renders the bank.");
            }
        }

        private bool ClipsSurvivedReset()
        {
            if (_canary == null)
            {
                return false;
            }

            try
            {
                var data = new float[CanaryPattern.Length];
                if (!_canary.GetData(data, 0))
                {
                    return false;
                }

                for (int i = 0; i < data.Length; i++)
                {
                    if (Mathf.Abs(data[i] - CanaryPattern[i]) > 0.001f)
                    {
                        return false;
                    }
                }

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void ReloadBank()
        {
            _reloadPending = false;
            if (ClipsSurvivedReset())
            {
                // This platform/version kept script clip data: only playback state was lost. Sources were stopped;
                // restart the music (loops restart themselves in LoopVoice.Tick, SFX simply play next time).
                _music.Restart();
                return;
            }

            _reloadCount++;
            FieldLog.Info("Audio configuration changed: re-rendering the procedural audio bank (#" + _reloadCount + ").");

            _loader.Cancel();
            _music.Clear();
            _sfx.ClearClips();
            _engine.ClearClip();
            _slipstream.ClearClip();
            _loader.DestroyClips();
            if (_canary != null)
            {
                Destroy(_canary);
            }

            CreateCanary();
            _menuLoaded = false;
            _loopsLoaded = 0;
            _errorLogged = false;

            // IsReady stays true if it already was (boot is over; sounds come back as they load). Single-threaded:
            // this can happen mid-run, where saturating every core would drop frames.
            _loader = new AudioLoader(SampleRate, false, OnMusicLoaded, OnSfxLoaded, OnLoopLoaded);
            _loader.Start();
        }

        private void OnApplicationPause(bool paused)
        {
            if (!paused)
            {
                _music?.RequestResync();
            }
        }

        private void OnApplicationFocus(bool focus)
        {
            if (focus)
            {
                _music?.RequestResync();
            }
        }

        private void OnDestroy()
        {
            AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged;
            if (_loader != null)
            {
                _loader.Cancel();
                _loader.DestroyClips();
            }

            if (_canary != null)
            {
                Destroy(_canary);
                _canary = null;
            }

            if (Instance == this)
            {
                Instance = null;
            }
        }
    }
}
