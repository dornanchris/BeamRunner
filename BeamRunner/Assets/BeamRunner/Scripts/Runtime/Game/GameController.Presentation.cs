using BeamRunner.Core;
using BeamRunner.Core.Audio;
using BeamRunner.Core.Field;
using BeamRunner.Core.Meta;
using BeamRunner.Core.Sim;
using BeamRunner.Rendering;
using BeamRunner.Services;
using UnityEngine;

namespace BeamRunner.Game
{
    /// <summary>
    /// Presentation: binds the displayed simulation to the rendering layer every frame and turns simulation
    /// events into camera, post-processing, particle and haptic feedback. Audio lives in
    /// <c>GameController.Audio.cs</c> (the <c>Audio*</c> partial methods below).
    /// See Scripts/Runtime/Rendering/README.md for the per-frame call order this follows
    /// (globals → craft → field sync → camera → post).
    /// </summary>
    /// <remarks>
    /// Graceful degradation: everything here runs only once <see cref="BuildPresentation"/> has completed
    /// (<c>_presentationReady</c>). If building the world throws (a boot step catches it), the game keeps running without
    /// the 3D presentation instead of throwing every frame; the audio hooks are still forwarded.
    /// </remarks>
    public sealed partial class GameController
    {
        private const float ThemeFadeSeconds = 2.2f;
        private const float HangarScrollSpeed = 12f;
        private const float NearMissSparkOffset = 0.55f;

        private static readonly Pickup[] NoPickups = new Pickup[0];

        private Transform _world;
        private FxLibrary _fx;
        private SpaceView _space;
        private FieldRenderer _field;
        private TrackRenderer _track;
        private CameraRig _rig;
        private PostFx _post;
        private ShipVisual _ship;
        private string _shipId;

        private ZoneTheme _themeFrom;
        private ZoneTheme _themeTo;
        private float _themeT = 1f;
        private int _themeLevel = -1;

        private float _flash;
        private double _hangarDistance;
        private bool _presentationReady;
        private int _smashFeedbackFrame = -1;

        /// <summary>The gameplay camera (null before boot).</summary>
        public Camera MainCamera
        {
            get { return _rig != null ? _rig.Camera : null; }
        }

        partial void BuildPresentation()
        {
            _world = new GameObject("World").transform;
            _world.SetParent(transform, false);
            _fx = FxLibrary.Create(_world);
            _space = SpaceView.Create(_world);
            _field = FieldRenderer.Create(_world, _fx);
            _track = TrackRenderer.Create(_world, _fx);
            _rig = CameraRig.Create(_world);
            _post = PostFx.Create(_world);
            _post.Initialize(_rig.Camera);

            _field.SetCamera(_rig.Camera);

            SnapTheme(1);
            SpawnShip(Profile.Profile.SelectedShip);
            _rig.SetMode(CameraMode.Menu, true);
            _presentationReady = true;

            // AudioBuild() is called by Boot itself, before the UI and a frame ahead of this: the audio worker is the
            // longest boot task, so it starts first.
        }

        private void SpawnShip(string shipId)
        {
            if (_ship != null && _shipId == shipId)
            {
                return;
            }

            if (_ship != null)
            {
                Destroy(_ship.gameObject);
            }

            _ship = ShipVisual.Create(_world, ShipCatalog.Get(shipId));
            _shipId = shipId;
        }

        partial void OnSimulationReplaced()
        {
            if (!_presentationReady)
            {
                return;
            }

            _field.Clear();
            _track.Clear();
            _fx.Clear();
            _post.ResetPulses();
            RunSimulation sim = DisplayedSim;
            SnapTheme(sim != null ? sim.Level : 1);
            if (_ship != null)
            {
                _ship.SetVisible(true);
            }
        }

        // ------------------------------------------------------------------ per frame

        partial void TickPresentation(float dt, float unscaledDt)
        {
            if (!_presentationReady)
            {
                AudioTick(dt, unscaledDt);
                return;
            }

            UpdateTheme(unscaledDt);
            _flash = Mathf.MoveTowards(_flash, 0f, unscaledDt * 2.5f);
            FieldGlobals.SetFlash(_flash);
            float beat = 0f;
            double beatTime = 0.0;
            AudioBeat(ref beat, ref beatTime);
            FieldGlobals.SetBeat(beat, beatTime);

            if (State == GameState.Hangar)
            {
                TickHangarVisuals(dt);
            }
            else
            {
                RunSimulation sim = DisplayedSim;
                if (sim != null)
                {
                    TickSimVisuals(sim, dt);
                }
            }

            TickGhost(dt); // Daily Run ghost (GameController.DailyRun.cs)
            _post.Tick(unscaledDt);
            UpdateShipScreenPoint();
            AudioTick(dt, unscaledDt);
        }

        private void TickSimVisuals(RunSimulation sim, float dt)
        {
            bool crashed = sim.IsCrashed;
            // The run's world stands still during the 3-2-1 (start, resume, revive) and at a checkpoint: so must the speed
            // lines and particle drift, which otherwise kept streaming past a frozen track.
            bool frozen = crashed || sim.AtCheckpoint || (ReferenceEquals(sim, Run) && State == GameState.Countdown);

            // 1. globals and environment
            FieldGlobals.SetScroll(sim.Distance);
            FieldGlobals.SetSpeed01(sim.Speed01);
            _fx.SetTrackSpeed(frozen ? 0f : sim.Speed);
            _space.SetEdgeFence(FieldSpec.HalfExtent, false, sim.ShipX, sim.ShipY);
            _space.Tick(dt, frozen ? 0f : sim.Speed, sim.Speed01, sim.OverloadActive);

            // 2. craft
            _ship.SetBoost(sim.OverloadActive ? 1f : sim.InstantBuildRemaining > 0f ? 0.4f : 0f);
            _ship.SetShield(sim.ShieldActive);
            _ship.SetBlink((sim.Invulnerable || !sim.OverloadActive && sim.Hovering) && !sim.ShieldActive && !crashed);
            int ground = sim.IsFalling ? sim.Layer - 1 : sim.Layer;
            _ship.GroundY = ground >= 0 ? FieldSpec.RailY(ground) : FieldMath.FloorY;
            if (!crashed)
            {
                _ship.SetPose(sim.ShipX, sim.ShipY, sim.ShipVelocityX, sim.ShipVelocityY, dt);
                _ship.UpdateTrail(sim.Distance, sim.Speed);
            }

            // 3. track and pickups
            bool playerRun = ReferenceEquals(sim, Run);
            _track.Sync(sim, playerRun && (State == GameState.Running || State == GameState.Countdown) && !FootageMode.Enabled);
            _field.Sync(sim.Pickups, sim.Distance);

            // 4. camera, 5. post
            _rig.Tick(dt, sim.ShipX, sim.ShipY, sim.ShipVelocityX, sim.ShipVelocityY, sim.Speed01, sim.OverloadActive);
            _post.SetOverdrive(sim.OverloadActive);
        }

        /// <summary>Hangar: an empty, slowly streaming field with the craft on its turntable at the middle layer.</summary>
        private void TickHangarVisuals(float dt)
        {
            // Stream the (slowed, hidden) attract run so leaving the hangar doesn't snap the scroll.
            RunSimulation attract = _attract;
            double distance;
            float speed;
            if (attract != null)
            {
                distance = attract.Distance;
                speed = attract.Speed * AttractTimeScale * HangarAttractScale;
            }
            else
            {
                _hangarDistance += HangarScrollSpeed * dt;
                distance = _hangarDistance;
                speed = HangarScrollSpeed;
            }

            FieldGlobals.SetScroll(distance);
            FieldGlobals.SetSpeed01(0f);
            _fx.SetTrackSpeed(speed);
            _space.SetEdgeFence(FieldSpec.HalfExtent, false, 0f, 0f);
            _space.Tick(dt, speed, 0f, false);

            _ship.SetBoost(0f);
            _ship.SetShield(false);
            _ship.SetBlink(false);
            _ship.GroundY = FieldSpec.RailY(FieldSpec.MiddleLayer);
            _ship.SetPose(0f, FieldSpec.ShipY, 0f, 0f, dt);

            _field.Sync(NoPickups, distance);
            _track.Sync(null, false);
            _rig.Tick(dt, 0f, FieldSpec.ShipY, 0f, 0f, 0f, false);
            _post.SetOverdrive(false);
        }

        private void UpdateShipScreenPoint()
        {
            Camera cam = _rig.Camera;
            if (cam == null || _ship == null)
            {
                return;
            }

            Vector3 p = cam.WorldToScreenPoint(_ship.Position);
            if (p.z > 0f)
            {
                ShipScreenPoint = new Vector2(p.x, p.y);
            }
        }

        // ------------------------------------------------------------------ themes (one palette per level)

        private void SnapTheme(int level)
        {
            _themeLevel = level;
            _themeFrom = _themeTo = ZoneThemes.ForLevel(level);
            _themeT = 1f;
            FieldGlobals.ApplyTheme(_themeTo);
            if (_field != null)
            {
                _field.SetTheme(_themeTo);
            }
        }

        private void BeginThemeFade(int level)
        {
            if (level == _themeLevel)
            {
                return;
            }

            _themeFrom = _themeTo ?? ZoneThemes.ForLevel(level);
            _themeTo = ZoneThemes.ForLevel(level);
            _themeLevel = level;
            _themeT = 0f;
            _field.SetTheme(_themeTo);
        }

        private void UpdateTheme(float dt)
        {
            if (_themeT >= 1f || _themeFrom == null)
            {
                return;
            }

            _themeT = Mathf.Min(1f, _themeT + dt / ThemeFadeSeconds);
            FieldGlobals.LerpTheme(_themeFrom, _themeTo, MathUtil.SmoothStep(_themeT));
        }

        // ------------------------------------------------------------------ state changes

        partial void OnStatePresentation(GameState from, GameState to)
        {
            if (!_presentationReady)
            {
                AudioOnState(from, to);
                return;
            }

            switch (to)
            {
                case GameState.Menu:
                    SpawnShip(Profile.Profile.SelectedShip);
                    _ship.SetShowcase(false);
                    _ship.SetVisible(true);
                    _rig.SetMode(CameraMode.Menu, from == GameState.Boot);
                    PlatformUtil.KeepScreenOn(false);
                    break;

                case GameState.Hangar:
                    _field.Clear();
                    SpawnShip(PreviewShipId);
                    _ship.SetShowcase(true);
                    _ship.SetVisible(true);
                    _rig.SetMode(CameraMode.Hangar, false);
                    break;

                case GameState.Countdown:
                    if (_countdownIsFresh)
                    {
                        // A new run (also RESTART from Pause); resume and revive countdowns keep the craft as it is.
                        SpawnShip(Profile.Profile.SelectedShip);
                        _ship.SetShowcase(false);
                        _ship.SetVisible(true);
                    }

                    _rig.SetMode(CameraMode.Run, false);
                    PlatformUtil.KeepScreenOn(true);
                    break;

                case GameState.Running:
                    _rig.SetMode(CameraMode.Run, false);
                    break;

                case GameState.Paused:
                    // A paused game left on the table must be allowed to dim/sleep (Countdown re-enables).
                    PlatformUtil.KeepScreenOn(false);
                    break;

                case GameState.GameOver:
                    PlatformUtil.KeepScreenOn(false);
                    break;
            }

            AudioOnState(from, to);
        }

        // ------------------------------------------------------------------ events → feedback

        partial void PresentRunEvent(RunEvent e)
        {
            if (!_presentationReady)
            {
                AudioOnRunEvent(e);
                return;
            }

            float shake = Profile.Settings.CameraShake;
            bool reduced = Profile.Settings.ReduceFlashing;
            switch (e.Type)
            {
                case RunEventType.Built:
                    _track.OnBuilt(ImpactPoint(e), LayerOfRail(e.Y));
                    _rig.Kick(e.Count == (int)BuildKind.RampUp ? 2f : 1.2f);
                    Haptics.Light();
                    break;

                case RunEventType.BuildFailed:
                    _ship.Flash();
                    Haptics.Selection();
                    break;

                case RunEventType.Smashed:
                    _track.OnSmashed(ImpactPoint(e), e.Count / FieldSpec.LaneCount);

                    // Overload can plough through several cubes in one step: one camera kick and haptic per frame.
                    if (_smashFeedbackFrame != Time.frameCount)
                    {
                        _smashFeedbackFrame = Time.frameCount;
                        _rig.Shake(0.25f * shake);
                        _rig.Kick(4f);
                        Haptics.Medium();
                    }

                    break;

                case RunEventType.PickupCollected:
                    // The field view plays the pickup burst itself (events dispatch before Sync).
                    _field.OnPickupCollected(e.Id);
                    var kind = (PickupKind)e.Count;
                    if (kind != PickupKind.Gem && kind != PickupKind.Fuel)
                    {
                        Haptics.Light();
                    }

                    break;

                case RunEventType.PowerUpStarted:
                    _fx.PowerUpBurst(_ship.Position, FieldGlobals.SecondaryColor);
                    _post.Pulse(PostFxPulse.NearMiss);
                    _rig.Kick(3f);
                    break;

                case RunEventType.NearMiss:
                    _fx.NearMissSpark(_ship.Position + new Vector3(0f, -0.4f, 0.6f), FieldGlobals.PaletteColor(e.Count % 3));
                    _ship.PulseHitRing();
                    _post.Pulse(PostFxPulse.NearMiss);
                    _rig.Shake(0.08f * shake);
                    Haptics.Light();
                    break;

                case RunEventType.TrailCompleted:
                    // A whole gem trail: a burst of the accent colour off the craft and a little kick.
                    _fx.PowerUpBurst(_ship.Position, FieldGlobals.AccentColor);
                    _ship.PulseHitRing();
                    _post.Pulse(PostFxPulse.NearMiss);
                    _rig.Kick(3f);
                    Haptics.Light();
                    break;

                case RunEventType.OverloadStarted:
                    _post.Pulse(PostFxPulse.Overdrive);
                    _rig.Kick(9f);
                    _fx.PowerUpBurst(_ship.Position, FieldGlobals.PrimaryColor);
                    _flash = Mathf.Max(_flash, reduced ? 0.08f : 0.3f);
                    Haptics.Heavy();
                    break;

                case RunEventType.ShieldBroken:
                    _ship.Flash();
                    _post.Pulse(PostFxPulse.Hit);
                    _rig.Shake(0.5f * shake);
                    Haptics.Heavy();
                    break;

                case RunEventType.FellOff:
                    _rig.Shake(0.12f * shake);
                    Haptics.Selection();
                    break;

                case RunEventType.Damaged:
                    _ship.Flash();
                    _post.Pulse(PostFxPulse.Hit);
                    _rig.Shake(0.55f * shake);
                    _flash = Mathf.Max(_flash, reduced ? 0.05f : 0.2f);
                    Haptics.Heavy();
                    break;

                case RunEventType.Landed:
                    _fx.Sparkle(_ship.Position + Vector3.down * 0.5f, FieldGlobals.SecondaryColor);
                    break;

                case RunEventType.Repaired:
                    _fx.PowerUpBurst(_ship.Position, new Color(1f, 0.42f, 0.72f));
                    Haptics.Success();
                    break;

                case RunEventType.LevelStarted:
                    BeginThemeFade(e.Count);
                    _post.Pulse(PostFxPulse.LevelStart);
                    break;

                case RunEventType.WallReached:
                    _rig.Kick(5f);
                    _fx.PowerUpBurst(_ship.Position, FieldGlobals.AccentColor);
                    Haptics.Medium();
                    break;

                case RunEventType.LevelCleared:
                    _post.Pulse(PostFxPulse.LevelCleared);
                    _flash = Mathf.Max(_flash, reduced ? 0.05f : 0.18f);
                    _fx.PowerUpBurst(_ship.Position, FieldGlobals.AccentColor);
                    Haptics.Success();
                    break;

                case RunEventType.Crashed:
                    _ship.Explode(_fx);
                    _rig.SetMode(CameraMode.Crash, false);
                    _rig.Shake(1f * shake);
                    _post.Pulse(PostFxPulse.Crash);
                    _flash = Mathf.Max(_flash, reduced ? 0.12f : 0.55f);
                    Haptics.Failure();
                    break;

                case RunEventType.Revived:
                    _ship.SetVisible(true);
                    _rig.SetMode(CameraMode.Run, false);
                    _fx.PowerUpBurst(_ship.Position, FieldGlobals.SecondaryColor);
                    Haptics.Success();
                    break;
            }

            AudioOnRunEvent(e);
        }

        partial void PresentAttractEvent(RunEvent e)
        {
            if (!_presentationReady)
            {
                return;
            }

            if (State == GameState.Hangar && e.Type != RunEventType.LevelStarted)
            {
                // The track and pickups are hidden in the hangar; no stray FX around the showcase craft.
                return;
            }

            switch (e.Type)
            {
                case RunEventType.Smashed:
                    _track.OnSmashed(ImpactPoint(e), e.Count / FieldSpec.LaneCount);
                    break;
                case RunEventType.Built:
                    _track.OnBuilt(ImpactPoint(e), LayerOfRail(e.Y));
                    break;
                case RunEventType.PickupCollected:
                    _field.OnPickupCollected(e.Id);
                    break;
                case RunEventType.LevelStarted:
                    BeginThemeFade(e.Count);
                    break;
            }
        }

        private static int LayerOfRail(float y)
        {
            return Mathf.Clamp(Mathf.RoundToInt((y + FieldSpec.HalfExtent) / FieldSpec.LayerSpacing), 0, FieldSpec.TopLayer);
        }

        /// <summary>World position of an impact event (X/Y/Z = track point) on the displayed simulation.</summary>
        private Vector3 ImpactPoint(RunEvent e)
        {
            RunSimulation sim = DisplayedSim;
            double distance = sim != null ? sim.Distance : 0.0;
            return new Vector3(e.X, e.Y, FieldMath.WorldZ(e.Z, distance));
        }

        partial void PresentCountdown(int value)
        {
            AudioCountdown(value);
            if (value <= 0)
            {
                Haptics.Light();
            }
        }

        partial void PresentMissionComplete(Mission mission)
        {
            AudioPlay(SfxId.MissionComplete);
            Haptics.Success();
        }

        partial void PresentResults(RunSummary summary)
        {
        }

        partial void PresentPreviewShip(string shipId)
        {
            if (_presentationReady && State == GameState.Hangar)
            {
                SpawnShip(shipId);
                _ship.SetShowcase(true);
            }
        }

        partial void PresentSelectedShip()
        {
            if (!_presentationReady)
            {
                return;
            }

            if (State == GameState.Hangar)
            {
                SpawnShip(PreviewShipId);
                _ship.SetShowcase(true);
            }
            else if (State == GameState.Menu)
            {
                SpawnShip(Profile.Profile.SelectedShip);
            }
        }

        partial void PresentPurchase()
        {
            AudioPlay(SfxId.Purchase);
            Haptics.Success();
        }

        partial void PresentReward()
        {
            AudioPlay(SfxId.Reward);
            Haptics.Success();
        }

        partial void PresentError()
        {
            AudioPlay(SfxId.UiError);
            Haptics.Failure();
        }

        partial void PresentUiSound(SfxId id)
        {
            AudioPlay(id);
        }

        partial void ApplySettingsPresentation(PlayerSettings settings)
        {
            if (!_presentationReady)
            {
                AudioApplySettings(settings);
                return;
            }

            int quality = Mathf.Clamp(settings.GraphicsQuality, 0, 2);
            FieldGlobals.SetQuality(quality);
            _post.SetQuality(quality);
            _space.SetQuality(quality);

            bool reduced = settings.ReduceFlashing;
            FieldGlobals.SetReducedFlashing(reduced);
            _post.SetReducedFlashing(reduced);
            _fx.SetReducedFlashing(reduced);
            ShipVisual.ReducedFlashing = reduced;
            AudioApplySettings(settings);
        }

        // ------------------------------------------------------------------ audio hooks (GameController.Audio.cs)

        partial void AudioBuild();

        partial void AudioTick(float dt, float unscaledDt);

        partial void AudioBeat(ref float beat, ref double beatTime);

        partial void AudioOnState(GameState from, GameState to);

        partial void AudioOnRunEvent(RunEvent e);

        partial void AudioCountdown(int value);

        partial void AudioPlay(SfxId id);

        partial void AudioApplySettings(PlayerSettings settings);
    }
}
