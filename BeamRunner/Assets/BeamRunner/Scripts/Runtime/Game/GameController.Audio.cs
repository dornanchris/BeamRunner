using BeamRunner.Audio;
using BeamRunner.Core.Audio;
using BeamRunner.Core.Meta;
using BeamRunner.Core.Sim;
using BeamRunner.Core.Field;
using UnityEngine;

namespace BeamRunner.Game
{
    /// <summary>
    /// Audio direction: which arrangement plays in each state, how the Run music builds with speed and
    /// multiplier, the engine loop, the wind rush of Overdrive (the airy "slipstream" loop), and a sound for every
    /// gameplay event.
    /// </summary>
    public sealed partial class GameController
    {
        private AudioManager _audio;
        private float _slipstreamAmount;
        private float _lastPingSound;
        private int _lastMultiplier = 1;

        /// <summary>The procedural audio engine (null before boot).</summary>
        public AudioManager Audio
        {
            get { return _audio; }
        }

        partial void AudioBuild()
        {
            var go = new GameObject("Audio");
            go.transform.SetParent(transform, false);
            _audio = go.AddComponent<AudioManager>();
            _audio.Initialize();
            _audio.SetVolumes(Profile.Settings.MusicVolume, Profile.Settings.SfxVolume);
            _audio.SetMusic(MusicMode.Menu);
        }

        partial void QueryPresentationReady(ref bool ready)
        {
            ready = _audio == null || _audio.IsReady;
        }

        partial void QueryPresentationProgress(ref float progress)
        {
            progress = _audio != null ? _audio.LoadProgress : 1f;
        }

        partial void AudioBeat(ref float beat, ref double beatTime)
        {
            if (_audio != null)
            {
                beat = _audio.BeatPulse;
                beatTime = _audio.BeatTime;
            }
        }

        partial void AudioTick(float dt, float unscaledDt)
        {
            if (_audio == null)
            {
                return;
            }

            RunSimulation run = Run;
            bool flying = run != null && (State == GameState.Running || State == GameState.Countdown);
            if (!flying)
            {
                _audio.SetEngine(0f, false);
                _slipstreamAmount = 0f;
                _audio.SetSlipstream(0f);
                return;
            }

            _audio.SetEngine(Mathf.Clamp01(run.Speed01), true);
            float slipTarget = run.OverloadActive ? 1f : 0f;
            _slipstreamAmount = Mathf.MoveTowards(_slipstreamAmount, slipTarget, unscaledDt * 3f);
            _audio.SetSlipstream(_slipstreamAmount);

            // The arrangement builds as the run heats up: speed brings in the arp, a hot multiplier the lead.
            float intensity = 0.2f + Mathf.Clamp01(run.Speed01) * 0.5f + (run.Multiplier - 1) * 0.07f;
            _audio.SetIntensity(Mathf.Clamp01(intensity));
        }

        partial void AudioOnState(GameState from, GameState to)
        {
            if (_audio == null)
            {
                return;
            }

            switch (to)
            {
                case GameState.Menu:
                case GameState.Hangar:
                    _audio.SetOverdrive(false);
                    _audio.SetDuck(false);
                    _audio.SetMusic(MusicMode.Menu);
                    break;

                case GameState.Countdown:
                    if (_countdownIsFresh)
                    {
                        // A new run (also RESTART from Pause): reset per-run audio state and bring in the Run arrangement.
                        _audio.ResetGemChain();
                        _audio.SetOverdrive(false);
                        _audio.SetIntensity(0.2f);
                        _audio.SetMusic(MusicMode.Run);
                        _lastMultiplier = 1;
                    }

                    _audio.SetDuck(false);
                    break;

                case GameState.Running:
                    _audio.SetDuck(false);
                    break;

                case GameState.Paused:
                case GameState.Crashed:
                    _audio.SetDuck(true);
                    break;

                case GameState.GameOver:
                    _audio.SetOverdrive(false);
                    _audio.SetDuck(false);
                    _audio.SetMusic(MusicMode.GameOver);
                    break;
            }
        }

        partial void AudioOnRunEvent(RunEvent e)
        {
            if (_audio == null)
            {
                return;
            }

            switch (e.Type)
            {
                case RunEventType.Built:
                    // The snap: a metallic ping pitched by the piece (up = higher), with a soft thump under it.
                    float buildPitch = e.Count == (int)BuildKind.RampUp ? 1.35f : e.Count == (int)BuildKind.RampDown ? 0.85f : 1.1f;
                    _audio.PlayPanned(SfxId.ShotPing, Mathf.Clamp(e.X / 6f, -0.5f, 0.5f), 0.8f, buildPitch);
                    _audio.Play(SfxId.BlockHit, 0.45f, buildPitch * 0.9f);
                    break;

                case RunEventType.BuildFailed:
                    // Out of fuel: the "empty" sound, rate-limited against tap spam.
                    if (Time.unscaledTime - _lastPingSound >= 0.15f)
                    {
                        _lastPingSound = Time.unscaledTime;
                        _audio.Play(SfxId.UiError, 0.8f, 0.85f);
                    }

                    break;

                case RunEventType.NearMiss:
                    float pitch = 1f + Mathf.Min(e.Count, 8) * 0.03f;
                    _audio.Play(SfxId.NearMiss, 1f, pitch);
                    break;

                case RunEventType.LayerChanged:
                    _audio.Play(SfxId.GatePass, 0.35f, e.Value > 0f ? 1.15f : 0.9f);
                    break;

                case RunEventType.Smashed:
                    _audio.Play(SfxId.BlockBreak, 0.9f, Random.Range(0.92f, 1.05f));
                    break;

                case RunEventType.PickupCollected:
                    PickupKind kind = (PickupKind)e.Count;
                    if (kind == PickupKind.Gem)
                    {
                        _audio.PlayGemChain();
                    }
                    else if (kind == PickupKind.Fuel)
                    {
                        _audio.Play(SfxId.GatePass, 0.7f, 1.3f);
                    }
                    else if (kind == PickupKind.Overload || kind == PickupKind.InstantBuild)
                    {
                        _audio.Play(SfxId.PowerUpPickup);
                    }
                    else if (kind == PickupKind.Repair)
                    {
                        _audio.Play(SfxId.Revive, 0.75f, 1.15f);
                    }

                    break;

                case RunEventType.PowerUpStarted:
                    _audio.Play(SfxId.MagnetStart, 0.8f, 1.25f);
                    break;

                case RunEventType.TrailCompleted:
                    // The multiplier sting a fifth up, on top of the gem chain's top note.
                    _audio.Play(SfxId.MultiplierUp, 1f, 1.5f);
                    break;

                case RunEventType.ShieldGained:
                    _audio.Play(SfxId.ShieldUp);
                    break;

                case RunEventType.ShieldBroken:
                    _audio.Play(SfxId.ShieldBreak);
                    break;

                case RunEventType.MagnetStarted:
                    _audio.Play(SfxId.MagnetStart);
                    break;

                case RunEventType.OverloadStarted:
                    _audio.Play(SfxId.OverdriveStart);
                    _audio.SetOverdrive(true);
                    break;

                case RunEventType.OverloadEnded:
                    _audio.Play(SfxId.OverdriveEnd);
                    _audio.SetOverdrive(false);
                    break;

                case RunEventType.FellOff:
                    _audio.Play(SfxId.ShotPing, 0.6f, 0.55f);
                    break;

                case RunEventType.Damaged:
                    _audio.Play(SfxId.BlockBreak, 1f, 0.7f);
                    break;

                case RunEventType.FuelLow:
                    _audio.Play(SfxId.CountdownTick, 0.55f, 0.8f);
                    break;

                case RunEventType.MultiplierChanged:
                    if (e.Count > _lastMultiplier)
                    {
                        _audio.Play(SfxId.MultiplierUp, 1f, 1f + (e.Count - 2) * 0.06f);
                    }

                    _lastMultiplier = e.Count;
                    break;

                case RunEventType.WallReached:
                    _audio.Play(SfxId.OverdriveReady);
                    break;

                case RunEventType.LevelStarted:
                    _audio.Play(SfxId.ZoneChange);
                    break;

                case RunEventType.LevelCleared:
                    _audio.Play(SfxId.Milestone);
                    break;

                case RunEventType.PerkApplied:
                    _audio.Play(SfxId.Purchase, 0.8f);
                    break;

                case RunEventType.Crashed:
                    _audio.SetOverdrive(false);
                    _audio.Play(SfxId.Crash);
                    break;

                case RunEventType.Revived:
                    _audio.Play(SfxId.Revive);
                    break;

                case RunEventType.TutorialHint:
                    _audio.Play(SfxId.UiToggle, 0.6f);
                    break;
            }
        }

        partial void AudioCountdown(int value)
        {
            _audio?.Play(value > 0 ? SfxId.CountdownTick : SfxId.CountdownGo);
        }

        partial void AudioPlay(SfxId id)
        {
            _audio?.Play(id);
        }

        partial void AudioApplySettings(PlayerSettings settings)
        {
            _audio?.SetVolumes(settings.MusicVolume, settings.SfxVolume);
        }
    }
}
