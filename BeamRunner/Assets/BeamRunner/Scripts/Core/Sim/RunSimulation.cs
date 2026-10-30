using System;
using System.Collections.Generic;
using BeamRunner.Core.Balance;
using BeamRunner.Core.Field;

namespace BeamRunner.Core.Sim
{
    /// <summary>What the craft is doing.</summary>
    public enum RunPhase
    {
        /// <summary>On a rail (or hovering in Overload).</summary>
        Riding = 0,

        /// <summary>Off its rail, dropping toward the layer below.</summary>
        Falling,

        /// <summary>Being carried up the wall at the end of a level.</summary>
        Climbing,

        /// <summary>Waiting at the top of the wall for the checkpoint to be closed.</summary>
        Checkpoint,

        /// <summary>Dropping from the wall onto the next level's middle layer.</summary>
        Descending,
    }

    /// <summary>
    /// The complete rules of a run, independent of Unity: riding the rails of the 3 lanes × 3 layers grid, lane switches,
    /// building bridges and ramps on the next tile with fuel, falls (one layer = damage, two = death), the ceiling and the
    /// void, obstacles, pickups and power-ups (shield, magnet, Overload, instant build, repair), gem trails, scoring, the
    /// wall checkpoint between levels and tutorial prompts.
    /// <para>
    /// Call <see cref="Step"/> once per rendered frame with the frame's delta time and input; it sub-steps internally at
    /// ≤ 1/120 s. Read the public state to render, and consume <see cref="Events"/> for feedback (clear them with
    /// <see cref="ClearEvents"/> once handled). At a checkpoint the run waits for <see cref="ContinueFromCheckpoint"/>.
    /// </para>
    /// </summary>
    public sealed class RunSimulation
    {
        // ------------------------------------------------------------------ tuning

        public const float MaxSubstep = 1f / 120f;
        public const float ReviveInvulnerability = 2.5f;
        public const float ShieldInvulnerability = 0.8f;
        public const int MaxMultiplier = 8;

        /// <summary>Useful builds, full trails and near misses toward each multiplier level.</summary>
        public const int ChainPerMultiplier = 6;

        /// <summary>Gravity of a fall (m/s²): one layer takes about 0.4 s.</summary>
        public const float Gravity = 38f;

        /// <summary>Seconds to climb the wall, and to drop from it into the next level.</summary>
        public const float ClimbSeconds = 0.9f;

        public const float DescendSeconds = 0.75f;

        /// <summary>Seconds a bot or the menu background waits at a checkpoint.</summary>
        public const float AutoCheckpointSeconds = 0.5f;

        /// <summary>Height of the top of the wall (it rises from the void to just above the ceiling).</summary>
        public const float WallTopY = FieldSpec.CeilingY + 0.5f;

        /// <summary>Base lead time (s) of the gap warning before the adaptive multiplier.</summary>
        public const float GhostLeadSeconds = 1.1f;

        /// <summary>Most a lane switch may wait (queued while on a ramp or with a ramp beside) before it is dropped.</summary>
        private const float LaneQueueSeconds = 0.6f;

        private const float MagnetRangeZ = 40f;
        private const float ChainTimeout = 8f;
        private const int NearMissPoints = 50;
        private const int BuildPoints = 15;
        private const int GemPoints = 10;
        private const int LevelClearPoints = 250;
        private const int SmashPoints = 60;
        private const float LaneGain = 24f;
        private const float LateralAcceleration = 420f;
        private const float PickupDepthWindow = 1.2f;
        private const float FallDeathDepth = 1.8f;

        // ------------------------------------------------------------------ state

        private readonly RunConfig _config;
        private readonly BalanceConfig _balance;
        private readonly ShipStats _ship;
        private readonly DifficultyModifiers _mods;
        private readonly Course _course;
        private readonly TrackGrid _grid = new TrackGrid();
        private readonly TrackGenerator _generator;

        private readonly List<Pickup> _pickups = new List<Pickup>(128);
        private readonly List<Pickup> _pendingPickups = new List<Pickup>(256);
        private readonly List<RunEvent> _events = new List<RunEvent>(64);
        private readonly RunResult _stats = new RunResult();

        private double _score;
        private int _targetLane = FieldSpec.CenterLane;
        private int _previousLane = FieldSpec.CenterLane;
        private double _laneChangeTime = -10.0;
        private int _queuedLaneShift;
        private double _queuedAt;
        private int _layer = FieldSpec.MiddleLayer;
        private long _tile;
        private int _entryLane = FieldSpec.CenterLane;
        private double _tileEnterTime;
        private double _gapSince = -1.0;
        private long _obstacleCheckedTile = -1;
        private float _boost = 1f;
        private float _overloadRemaining;
        private float _graceTimer;
        private float _invulnerableTimer;
        private float _magnetRemaining;
        private float _instantRemaining;
        private float _chainTimer;
        private int _chain;
        private int _multiplier = 1;
        private int _fallFrom;
        private double _phaseFromZ;
        private double _phaseToZ;
        private float _phaseFromY;
        private float _phaseToY;
        private float _phaseT;
        private float _checkpointWait;
        private float _costScale = 1f;
        private float _durationScale = 1f;
        private bool _fuelLowAnnounced;
        private bool _started;
        private int _level;
        private int _revivesUsed;
        private int _hintsShown;

        public RunSimulation(RunConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _balance = config.Balance ?? BalanceConfig.Current;
            _ship = (config.Ship ?? ShipStats.FromLevels(null, _balance)).Clone();
            _mods = (config.Modifiers ?? DifficultyModifiers.Standard).Clone();
            _course = new Course(config.StartLevel);
            _level = _course.StartLevel;
            _generator = new TrackGenerator(config.Seed, _course, config.Tutorial, _mods, _balance, _ship, _grid,
                _pendingPickups);
            ShipX = FieldSpec.LaneX(_targetLane);
            ShipY = FieldSpec.RideY(_layer);
            Speed = _course.SpeedAt(0) * _mods.Speed;
            Fuel = _ship.StartFuel;
            Hull = _ship.MaxHull;
            _stats.Seed = config.Seed;
            _stats.MaxMultiplier = 1;
            _stats.StartLevel = _level;
            _stats.LevelReached = _level;
            _stats.Rating = _mods.Rating;
            _stats.Adaptive = config.Adaptive;
            Activate();
            _entryLane = _targetLane;
        }

        // ------------------------------------------------------------------ public state

        public RunConfig Config
        {
            get { return _config; }
        }

        /// <summary>Maps run distance onto the level course.</summary>
        public Course Course
        {
            get { return _course; }
        }

        /// <summary>The craft of this run (upgrades; checkpoint perks change its prices and tank).</summary>
        public ShipStats Ship
        {
            get { return _ship; }
        }

        /// <summary>The adaptive difficulty of this run.</summary>
        public DifficultyModifiers Modifiers
        {
            get { return _mods; }
        }

        /// <summary>The track around the craft (rows from a little behind it to the activation distance ahead).</summary>
        public TrackGrid Grid
        {
            get { return _grid; }
        }

        /// <summary>Metres flown in this run (the craft's track position).</summary>
        public double Distance { get; private set; }

        /// <summary>Seconds of simulated (unpaused) time.</summary>
        public double Time { get; private set; }

        /// <summary>Current forward speed (m/s), including the adaptive multiplier and Overload.</summary>
        public float Speed { get; private set; }

        /// <summary>Normalised speed for effects: 0 at start, 1 at plateau speed, up to ~1.6 in Overload.</summary>
        public float Speed01
        {
            get { return MathUtil.Clamp(Levels.Speed01(Speed), 0f, 1.6f); }
        }

        /// <summary>Craft position on X (right) and Y (up), metres from the field's centre.</summary>
        public float ShipX { get; private set; }

        public float ShipY { get; private set; }

        /// <summary>Craft velocity on X and Y (m/s).</summary>
        public float ShipVelocityX { get; private set; }

        public float ShipVelocityY { get; private set; }

        /// <summary>Lane the craft is heading for.</summary>
        public int TargetLane
        {
            get { return _targetLane; }
        }

        /// <summary>Lane the craft is in now (nearest to its X).</summary>
        public int Lane
        {
            get { return FieldSpec.LaneOf(ShipX); }
        }

        /// <summary>Layer the craft rides (the layer it entered its tile on; the layer it fell from while falling).</summary>
        public int Layer
        {
            get { return _layer; }
        }

        /// <summary>Tile the craft is on.</summary>
        public long Tile
        {
            get { return _tile; }
        }

        public RunPhase Phase { get; private set; }

        public bool IsFalling
        {
            get { return Phase == RunPhase.Falling; }
        }

        /// <summary>True while the run waits at a level wall for <see cref="ContinueFromCheckpoint"/>.</summary>
        public bool AtCheckpoint
        {
            get { return Phase == RunPhase.Checkpoint; }
        }

        /// <summary>Current level (1-based).</summary>
        public int Level
        {
            get { return _level; }
        }

        /// <summary>Spec of the current level.</summary>
        public LevelSpec LevelSpec
        {
            get { return Levels.Get(_level); }
        }

        /// <summary>0..1 progress through the current level.</summary>
        public float LevelProgress01
        {
            get { return Phase == RunPhase.Checkpoint || Phase == RunPhase.Climbing ? 1f : _course.Progress01(Distance); }
        }

        public long Score
        {
            get { return (long)_score; }
        }

        public int Multiplier
        {
            get { return _multiplier; }
        }

        /// <summary>Chain steps toward the next multiplier level (0..ChainPerMultiplier-1; full at the cap).</summary>
        public int ChainProgress
        {
            get { return _multiplier >= MaxMultiplier ? ChainPerMultiplier : _chain % ChainPerMultiplier; }
        }

        /// <summary>0..1 time left before the multiplier decays one level.</summary>
        public float ChainTimer01
        {
            get { return _chain > 0 ? MathUtil.Clamp01(_chainTimer / ChainTimeout) : 0f; }
        }

        public int Gems
        {
            get { return _stats.Gems; }
        }

        /// <summary>Fuel in the tank, and its size (perks may enlarge it).</summary>
        public float Fuel { get; private set; }

        public float MaxFuel
        {
            get { return _ship.MaxFuel; }
        }

        /// <summary>Hull points left, and the most the hull holds.</summary>
        public int Hull { get; private set; }

        public int MaxHull
        {
            get { return _ship.MaxHull; }
        }

        public bool OverloadActive
        {
            get { return _overloadRemaining > 0f; }
        }

        public float OverloadRemaining
        {
            get { return _overloadRemaining; }
        }

        public bool ShieldActive { get; private set; }

        public float MagnetRemaining
        {
            get { return _magnetRemaining; }
        }

        public float InstantBuildRemaining
        {
            get { return _instantRemaining; }
        }

        /// <summary>True while hovering over gaps (Overload, or its grace period).</summary>
        public bool Hovering
        {
            get { return OverloadActive || _graceTimer > 0f; }
        }

        /// <summary>True while invulnerable after a revive or a shield hit (blink the craft).</summary>
        public bool Invulnerable
        {
            get { return _invulnerableTimer > 0f; }
        }

        public bool IsCrashed { get; private set; }

        public bool CanRevive
        {
            get { return IsCrashed && _revivesUsed < _config.MaxRevives; }
        }

        public int RevivesUsed
        {
            get { return _revivesUsed; }
        }

        /// <summary>Active pickups (sorted by Z, except ones pulled by the magnet).</summary>
        public IReadOnlyList<Pickup> Pickups
        {
            get { return _pickups; }
        }

        /// <summary>Events produced since the last <see cref="ClearEvents"/>.</summary>
        public List<RunEvent> Events
        {
            get { return _events; }
        }

        public void ClearEvents()
        {
            _events.Clear();
        }

        /// <summary>Fuel a build costs right now (0 during instant build).</summary>
        public float BuildCost(BuildKind kind)
        {
            return _instantRemaining > 0f ? 0f : _ship.Cost(kind) * _costScale;
        }

        public bool CanAfford(BuildKind kind)
        {
            return Fuel + 1e-4f >= BuildCost(kind);
        }

        // ------------------------------------------------------------------ build target (ghost outline)

        /// <summary>
        /// Where a build made now would land: the tile (the next one, or the current one in the last-second window), the
        /// lane and the layer. False when nothing can be built (airborne, on the wall, at a checkpoint).
        /// </summary>
        public bool BuildTarget(out long tile, out int lane, out int layer)
        {
            tile = _tile + 1;
            lane = _targetLane;
            layer = ExitLayer();
            if (Phase != RunPhase.Riding || IsCrashed)
            {
                return false;
            }

            if (InLateBuildWindow())
            {
                tile = _tile;
                layer = _layer;
                lane = Lane;
            }

            TrackRow row = _grid.Row(tile);
            return row != null && !row.IsWall && layer >= 0 && layer < FieldSpec.LayerCount;
        }

        /// <summary>
        /// The nearest gap ahead on the craft's lane and layer within the warning lead time (the ghost outline lights up on
        /// it): −1 when there is none.
        /// </summary>
        public long GapAhead()
        {
            if (Phase != RunPhase.Riding || IsCrashed)
            {
                return -1;
            }

            float lead = GhostLeadSeconds * _mods.GhostLead * Math.Max(1f, Speed);
            int lane = _targetLane;
            int layer = _layer;
            long last = FieldSpec.TileOf(Distance + lead);
            for (long t = _tile; t <= last; t++)
            {
                CellType c = _grid.Get(t, lane, layer);
                if (t == _tile)
                {
                    if (c == CellType.Empty)
                    {
                        return t;
                    }

                    layer = Math.Max(0, Math.Min(FieldSpec.TopLayer, layer + Cells.LayerDelta(c)));
                    continue;
                }

                TrackRow row = _grid.Row(t);
                if (row == null || row.IsWall)
                {
                    return -1;
                }

                if (c == CellType.Empty)
                {
                    return t;
                }

                int next = layer + Cells.LayerDelta(c);
                if (next < 0 || next > FieldSpec.TopLayer)
                {
                    return -1;
                }

                layer = next;
            }

            return -1;
        }

        // ------------------------------------------------------------------ stepping

        /// <summary>Advances the run by <paramref name="dt"/> seconds (clamped to 0.1 s).</summary>
        public void Step(float dt, RunInput input)
        {
            if (IsCrashed || dt <= 0f)
            {
                return;
            }

            if (dt > 0.1f)
            {
                dt = 0.1f;
            }

            if (!_started)
            {
                _started = true;
                Emit(RunEventType.LevelStarted, 0, _level);
                Hint(TutorialHint.Move);
            }

            if (Phase == RunPhase.Checkpoint)
            {
                if (_config.AutoCheckpoint)
                {
                    _checkpointWait += dt;
                    if (_checkpointWait >= AutoCheckpointSeconds)
                    {
                        ContinueFromCheckpoint(CheckpointPerk.None);
                    }
                }

                return;
            }

            if (input.UseAbsoluteLane)
            {
                RequestLane(FieldSpec.ClampLane(input.AbsoluteLane) - _targetLane);
            }

            if (input.LaneShift != 0)
            {
                RequestLane(Math.Sign(input.LaneShift) * Math.Min(Math.Abs(input.LaneShift), 2));
            }

            if (input.Build != BuildKind.None)
            {
                TryBuild(input.Build);
            }

            int steps = (int)Math.Ceiling(dt / MaxSubstep);
            float h = dt / steps;
            for (int i = 0; i < steps && !IsCrashed && Phase != RunPhase.Checkpoint; i++)
            {
                Substep(h);
            }
        }

        /// <summary>
        /// Leaves the checkpoint at the top of a wall with an optional perk: the craft drops onto the next level's middle
        /// layer. False when not at a checkpoint.
        /// </summary>
        public bool ContinueFromCheckpoint(CheckpointPerk perk)
        {
            if (Phase != RunPhase.Checkpoint)
            {
                return false;
            }

            ApplyPerk(perk);
            Phase = RunPhase.Descending;
            _phaseT = 0f;
            _phaseFromZ = Distance;
            _phaseFromY = ShipY;
            long next = _tile + 1;
            _phaseToZ = FieldSpec.TileStart(next) + 1.0;
            _phaseToY = FieldSpec.RideY(FieldSpec.MiddleLayer);
            _layer = FieldSpec.MiddleLayer;
            _level = _course.LevelOfTile(next);
            _stats.LevelReached = Math.Max(_stats.LevelReached, _level);
            Emit(RunEventType.LevelStarted, 0, _level);
            if (_config.Tutorial && _level >= _course.StartLevel + 3)
            {
                Hint(TutorialHint.Complete);
            }

            return true;
        }

        /// <summary>Applies an extra perk while waiting at a checkpoint (the rewarded "take all three" bonus).</summary>
        public bool GrantPerk(CheckpointPerk perk)
        {
            if (Phase != RunPhase.Checkpoint)
            {
                return false;
            }

            ApplyPerk(perk);
            return true;
        }

        /// <summary>
        /// Continues after a crash: puts the craft back on a rail (the middle layer after a fall), lays a few tiles of
        /// track ahead of it, clears obstacles there, restores the hull and some fuel, and grants brief invulnerability.
        /// Returns false if no revive is available.
        /// </summary>
        public bool Revive()
        {
            if (!CanRevive)
            {
                return false;
            }

            _revivesUsed++;
            _stats.Revived = true;
            IsCrashed = false;
            ShipVelocityX = 0f;
            ShipVelocityY = 0f;
            _invulnerableTimer = ReviveInvulnerability;
            _chainTimer = ChainTimeout;
            _queuedLaneShift = 0;
            bool fell = Phase == RunPhase.Falling || _layer < 0 || _layer > FieldSpec.TopLayer;
            _layer = fell ? Math.Max(FieldSpec.MiddleLayer, Math.Min(_fallFrom, FieldSpec.TopLayer))
                : Math.Max(0, Math.Min(_layer, FieldSpec.TopLayer));
            Phase = RunPhase.Riding;
            _targetLane = Lane;
            ShipX = FieldSpec.LaneX(_targetLane);
            ShipY = FieldSpec.RideY(_layer);
            Hull = _ship.MaxHull;
            Fuel = Math.Max(Fuel, Math.Min(_ship.MaxFuel, _ship.StartFuel * 0.5f));
            _fuelLowAnnounced = false;
            for (long t = _tile; t <= _tile + 3; t++)
            {
                TrackRow row = _grid.Row(t);
                if (row == null || row.IsWall)
                {
                    break;
                }

                for (int lane = 0; lane < FieldSpec.LaneCount; lane++)
                {
                    CellType c = row.Get(lane, _layer);
                    if (c == CellType.Obstacle || lane == _targetLane && c != CellType.Track)
                    {
                        _grid.Set(t, lane, _layer, CellType.Track, (float)Time);
                    }
                }
            }

            _entryLane = _targetLane;
            _tileEnterTime = Time;
            _gapSince = -1.0;
            Emit(RunEventType.Revived, 0, 0, 0f, ShipX, ShipY, Distance);
            return true;
        }

        /// <summary>Test hook: grants a shield.</summary>
        internal void DebugGrantShield()
        {
            ShieldActive = true;
        }

        /// <summary>Test hook: grants a power-up as if collected.</summary>
        internal void DebugGrant(PickupKind kind)
        {
            ApplyPickup(kind, kind == PickupKind.Fuel ? (int)Math.Round(_balance.FuelPickupValue) : 0);
        }

        /// <summary>Test hook: sets the fuel.</summary>
        internal void DebugSetFuel(float fuel)
        {
            Fuel = MathUtil.Clamp(fuel, 0f, _ship.MaxFuel);
        }

        /// <summary>Snapshot of the run's statistics (safe to call mid-run).</summary>
        public RunResult BuildResult()
        {
            RunResult r = _stats.Clone();
            r.Score = Score;
            r.Distance = (float)Distance;
            r.Duration = (float)Time;
            r.LevelReached = Math.Max(r.LevelReached, _level);
            r.LevelProgress = LevelProgress01;
            return r;
        }

        // ------------------------------------------------------------------ lanes

        private void RequestLane(int shift)
        {
            if (shift == 0)
            {
                return;
            }

            if (CanSwitchLane(_targetLane + shift))
            {
                SetTargetLane(_targetLane + shift);
                _queuedLaneShift = 0;
            }
            else
            {
                _queuedLaneShift = Math.Sign(shift) * Math.Min(2, Math.Abs(shift));
                _queuedAt = Time;
            }
        }

        /// <summary>
        /// Lanes switch freely on flat rail and in the air; on a ramp (or toward a ramp beside the craft) the switch waits
        /// for the next tile.
        /// </summary>
        private bool CanSwitchLane(int lane)
        {
            lane = FieldSpec.ClampLane(lane);
            if (Phase == RunPhase.Falling)
            {
                return true;
            }

            if (Phase != RunPhase.Riding)
            {
                return false;
            }

            if (OnRamp())
            {
                return false;
            }

            return !Cells.IsRamp(_grid.Get(_tile, lane, _layer));
        }

        private void SetTargetLane(int lane)
        {
            lane = FieldSpec.ClampLane(lane);
            if (lane == _targetLane)
            {
                return;
            }

            int direction = Math.Sign(lane - _targetLane);
            _previousLane = Lane;
            _laneChangeTime = Time;
            _targetLane = lane;
            Emit(RunEventType.LaneChanged, 0, lane, direction, ShipX, ShipY, Distance);
        }

        /// <summary>
        /// The craft slides toward its target lane's centre: the velocity it asks for is proportional to the distance
        /// (capped at <see cref="FieldSpec.MaxLateralSpeed"/>) and changes at a bounded acceleration — a lane switch takes
        /// about 0.17 s.
        /// </summary>
        private void Steer(float h)
        {
            if (_queuedLaneShift != 0)
            {
                if (Time - _queuedAt > LaneQueueSeconds)
                {
                    _queuedLaneShift = 0;
                }
                else if (CanSwitchLane(_targetLane + _queuedLaneShift))
                {
                    SetTargetLane(_targetLane + _queuedLaneShift);
                    _queuedLaneShift = 0;
                }
            }

            float target = FieldSpec.LaneX(_targetLane);
            float desired = MathUtil.Clamp((target - ShipX) * LaneGain, -FieldSpec.MaxLateralSpeed, FieldSpec.MaxLateralSpeed);
            float dv = MathUtil.Clamp(desired - ShipVelocityX, -LateralAcceleration * h, LateralAcceleration * h);
            ShipVelocityX += dv;
            float x = ShipX + ShipVelocityX * h;
            if ((target - ShipX) * (target - x) < 0f || Math.Abs(target - x) < 0.002f && Math.Abs(ShipVelocityX) < 0.5f)
            {
                x = target;
                ShipVelocityX = 0f;
            }

            ShipX = FieldSpec.ClampToField(x);
        }

        // ------------------------------------------------------------------ building

        private void TryBuild(BuildKind kind)
        {
            long tile;
            int lane;
            int layer;
            if (!BuildTarget(out tile, out lane, out layer))
            {
                return;
            }

            float cost = BuildCost(kind);
            if (Fuel + 1e-4f < cost)
            {
                _stats.EmptyBuilds++;
                Emit(RunEventType.BuildFailed, 0, (int)kind, cost, FieldSpec.LaneX(lane), FieldSpec.RailY(layer),
                    FieldSpec.TileStart(tile));
                Hint(TutorialHint.Fuel);
                return;
            }

            CellType before = _grid.Get(tile, lane, layer);
            if (before == CellType.Obstacle)
            {
                // Building into an obstacle is a crash, unless something breaks it first.
                if (!BreakObstacle(tile, lane, layer, CrashCause.Obstacle))
                {
                    return;
                }
            }

            CellType piece = Cells.FromBuild(kind);
            Fuel = Math.Max(0f, Fuel - cost);
            _stats.FuelSpent += cost;
            _stats.Builds++;
            switch (kind)
            {
                case BuildKind.Bridge:
                    _stats.Bridges++;
                    break;
                case BuildKind.RampUp:
                    _stats.RampsUp++;
                    break;
                case BuildKind.RampDown:
                    _stats.RampsDown++;
                    break;
            }

            bool useful = before == CellType.Empty;
            if (before == piece)
            {
                _stats.FuelWasted += cost;
            }

            _grid.Set(tile, lane, layer, piece, (float)Time);
            Emit(RunEventType.Built, (int)tile, (int)kind, cost, FieldSpec.LaneX(lane), FieldSpec.RailY(layer),
                FieldSpec.TileStart(tile));
            if (useful)
            {
                _stats.GapsBridged++;
                _score += BuildPoints * _multiplier * (1f + _config.ScoreBonus);
                AddChain();
            }

            if (tile == _tile)
            {
                // A last-second build under the craft (CheckCellOnEntry counts it as a near miss).
                _gapSince = -1.0;
                if (lane == _entryLane || lane == Lane)
                {
                    _entryLane = Lane;
                    CheckCellOnEntry();
                }
            }

            CheckFuelLow();
        }

        private void CheckFuelLow()
        {
            float bridge = _ship.BridgeCost * _costScale;
            if (Fuel < bridge - 1e-4f)
            {
                if (!_fuelLowAnnounced)
                {
                    _fuelLowAnnounced = true;
                    Emit(RunEventType.FuelLow, 0, 0, Fuel);
                }
            }
            else
            {
                _fuelLowAnnounced = false;
            }
        }

        /// <summary>True during the forgiving window right after the craft rode onto a gap (a build fills that tile).</summary>
        private bool InLateBuildWindow()
        {
            return Phase == RunPhase.Riding && _gapSince >= 0.0 && Time - _gapSince <= _balance.LateBuildSeconds + 1e-6 &&
                   _grid.Get(_tile, Lane, _layer) == CellType.Empty;
        }

        /// <summary>The layer the craft will be on when it enters the next tile.</summary>
        private int ExitLayer()
        {
            if (OnRamp())
            {
                return _layer + Cells.LayerDelta(_grid.Get(_tile, _entryLane, _layer));
            }

            return _layer;
        }

        /// <summary>True while riding a ramp entered at the tile's start.</summary>
        private bool OnRamp()
        {
            return Phase == RunPhase.Riding && Lane == _entryLane && Cells.IsRamp(_grid.Get(_tile, _entryLane, _layer));
        }

        // ------------------------------------------------------------------ substeps

        private void Substep(float h)
        {
            Time += h;
            TickTimers(h);
            switch (Phase)
            {
                case RunPhase.Climbing:
                    Climb(h);
                    return;
                case RunPhase.Descending:
                    Descend(h);
                    return;
            }

            float boostTarget = OverloadActive ? _balance.OverloadSpeedMultiplier : 1f;
            _boost = MathUtil.MoveTowards(_boost, boostTarget, h * (OverloadActive ? 2.5f : 0.9f));
            Speed = _course.SpeedAt(Distance) * _mods.Speed * _boost;
            Steer(h);
            Distance += Speed * h;
            _score += Speed * h * _multiplier * (1f + _config.ScoreBonus);
            if (_layer == FieldSpec.TopLayer && Phase == RunPhase.Riding)
            {
                _stats.TopLayerSeconds += h;
            }

            long tile = FieldSpec.TileOf(Distance);
            while (_tile < tile && !IsCrashed && (Phase == RunPhase.Riding || Phase == RunPhase.Falling))
            {
                EnterTile(_tile + 1);
            }

            if (IsCrashed || Phase == RunPhase.Climbing)
            {
                return;
            }

            if (Phase == RunPhase.Falling)
            {
                Fall(h);
            }
            else
            {
                RideCell(h);
            }

            if (IsCrashed)
            {
                return;
            }

            CheckObstacles();
            if (IsCrashed)
            {
                return;
            }

            Activate();
            UpdatePickups(h);
            UpdateHints();
        }

        private void TickTimers(float h)
        {
            if (_invulnerableTimer > 0f)
            {
                _invulnerableTimer -= h;
            }

            if (_graceTimer > 0f)
            {
                _graceTimer -= h;
            }

            if (_magnetRemaining > 0f)
            {
                _magnetRemaining -= h;
                if (_magnetRemaining <= 0f)
                {
                    _magnetRemaining = 0f;
                    Emit(RunEventType.MagnetEnded);
                }
            }

            if (_instantRemaining > 0f)
            {
                _instantRemaining -= h;
                if (_instantRemaining <= 0f)
                {
                    _instantRemaining = 0f;
                    Emit(RunEventType.PowerUpEnded, 0, (int)PickupKind.InstantBuild);
                }
            }

            if (_overloadRemaining > 0f && Phase != RunPhase.Checkpoint)
            {
                _overloadRemaining -= h;
                if (_overloadRemaining <= 0f)
                {
                    _overloadRemaining = 0f;
                    _graceTimer = _balance.OverloadGrace;
                    Emit(RunEventType.OverloadEnded);
                }
            }

            if (_chain > 0)
            {
                _chainTimer -= h;
                if (_chainTimer <= 0f)
                {
                    _chain = Math.Max(0, (_multiplier - 2) * ChainPerMultiplier);
                    _chainTimer = ChainTimeout;
                    RecomputeMultiplier();
                }
            }
        }

        /// <summary>The craft crosses into <paramref name="tile"/>: finishes the ramp it rode and meets the new cell.</summary>
        private void EnterTile(long tile)
        {
            if (Phase == RunPhase.Riding)
            {
                CellType ridden = _grid.Get(_tile, _entryLane, _layer);
                int delta = _entryLane == Lane ? Cells.LayerDelta(ridden) : 0;
                if (delta != 0)
                {
                    _layer += delta;
                    Emit(RunEventType.LayerChanged, 0, _layer, delta, ShipX, ShipY, Distance);
                    if (_layer < 0)
                    {
                        // A ramp down from the bottom layer leads into the void.
                        _fallFrom = 0;
                        _layer = 0;
                        StartFall();
                        _fallFrom = 0;
                        _tile = tile;
                        _gapSince = -1.0;
                        return;
                    }
                }
            }

            _tile = tile;
            TrackRow row = _grid.Row(tile);
            if (row != null && row.IsWall)
            {
                StartClimb();
                return;
            }

            if (Phase != RunPhase.Riding)
            {
                return;
            }

            _entryLane = Lane;
            _tileEnterTime = Time;
            _gapSince = -1.0;
            CheckCellOnEntry();
        }

        /// <summary>Rules that apply as the craft meets a cell: the ceiling pinch, a last-second build.</summary>
        private void CheckCellOnEntry()
        {
            TrackRow row = _grid.Row(_tile);
            if (row == null)
            {
                return;
            }

            int lane = Lane;
            CellType c = row.Get(lane, _layer);
            if (c == CellType.RampUp && _layer >= FieldSpec.TopLayer)
            {
                // Ceiling pinch: a ramp up from the top layer.
                if (BreakObstacle(_tile, lane, _layer, CrashCause.Ceiling))
                {
                    _grid.Set(_tile, lane, _layer, CellType.Track);
                }

                return;
            }

            float builtAt = row.BuiltAt[FieldSpec.CellIndex(lane, _layer)];
            if (builtAt >= 0f && Time - builtAt < FieldSpec.NearMissWindow && Cells.Solid(c))
            {
                NearMiss(Distance);
            }
        }

        /// <summary>Riding: follow the rail (or a ramp); over a gap, fall once the last-second window is over.</summary>
        private void RideCell(float h)
        {
            int lane = Lane;
            CellType c = _grid.Get(_tile, lane, _layer);
            float y = FieldSpec.RideY(_layer);
            if (c == CellType.Empty)
            {
                if (_gapSince < 0.0)
                {
                    _gapSince = Time;
                }

                if (Hovering)
                {
                    SetY(y, h);
                    return;
                }

                if (Time - _gapSince > _balance.LateBuildSeconds)
                {
                    _gapSince = -1.0;
                    StartFall();
                    return;
                }

                SetY(y, h);
                return;
            }

            _gapSince = -1.0;
            if (Cells.IsRamp(c) && lane == _entryLane)
            {
                double along = (Distance - FieldSpec.TileStart(_tile)) / FieldSpec.TileLength;
                y += (float)(Cells.LayerDelta(c) * FieldSpec.LayerSpacing * MathUtil.Clamp01((float)along));
            }

            SetY(y, h);
        }

        private void SetY(float y, float h)
        {
            ShipVelocityY = h > 0f ? (y - ShipY) / h : 0f;
            ShipY = y;
        }

        private void StartFall()
        {
            Phase = RunPhase.Falling;
            _fallFrom = _layer;
            _queuedLaneShift = 0;
            Emit(RunEventType.FellOff, 0, _layer, 0f, ShipX, ShipY, Distance);
            ResetChain();
        }

        /// <summary>Falling: drop under gravity; land on the layer below (damage) or keep falling to death.</summary>
        private void Fall(float h)
        {
            ShipVelocityY -= Gravity * h;
            ShipY += ShipVelocityY * h;
            int below = _fallFrom - 1;
            if (below >= 0)
            {
                CellType c = _grid.Get(_tile, Lane, below);
                if (Cells.Solid(c) && c != CellType.Wall)
                {
                    float landY = FieldSpec.RideY(below);
                    if (Cells.IsRamp(c))
                    {
                        double along = (Distance - FieldSpec.TileStart(_tile)) / FieldSpec.TileLength;
                        landY += (float)(Cells.LayerDelta(c) * FieldSpec.LayerSpacing * MathUtil.Clamp01((float)along));
                    }

                    if (ShipY <= landY && ShipY > landY - 0.9f)
                    {
                        Land(below, landY);
                        return;
                    }
                }
            }

            if (ShipY < FieldSpec.RideY(below) - FallDeathDepth)
            {
                if (_config.Invincible)
                {
                    // Footage and the menu background never die: catch the craft on a fresh rail.
                    int layer = Math.Max(0, below);
                    _grid.Set(_tile, Lane, layer, CellType.Track, (float)Time);
                    _grid.Set(_tile + 1, Lane, layer, CellType.Track, (float)Time);
                    Land(layer, FieldSpec.RideY(layer));
                    return;
                }

                Crash(below < 0 ? CrashCause.Void : CrashCause.LongFall);
            }
        }

        private void Land(int layer, float y)
        {
            Phase = RunPhase.Riding;
            _layer = layer;
            ShipY = y;
            ShipVelocityY = 0f;
            _entryLane = Lane;
            _tileEnterTime = Time;
            _gapSince = -1.0;
            bool shielded = false;
            if (ShieldActive)
            {
                ShieldActive = false;
                shielded = true;
                _stats.ShieldsBroken++;
                Emit(RunEventType.ShieldBroken, 0, (int)CrashCause.HullBroken, 0f, ShipX, ShipY, Distance);
            }
            else if (!_config.Invincible && _invulnerableTimer <= 0f)
            {
                Hull--;
                _stats.DamageTaken++;
                Emit(RunEventType.Damaged, 0, Hull, 0f, ShipX, ShipY, Distance);
            }

            _stats.Falls++;
            Emit(RunEventType.Landed, 0, Hull, shielded ? 1f : 0f, ShipX, ShipY, Distance);
            if (Hull <= 0)
            {
                Crash(CrashCause.HullBroken);
            }
        }

        // ------------------------------------------------------------------ obstacles

        /// <summary>Crashes into obstacle cubes the craft overlaps; counts the ones it slipped past.</summary>
        private void CheckObstacles()
        {
            TrackRow row = _grid.Row(_tile);
            if (row == null)
            {
                return;
            }

            double start = FieldSpec.TileStart(_tile);
            double centre = start + FieldSpec.TileLength * 0.5;
            double reach = FieldSpec.ObstacleHalfSize + FieldSpec.ShipHalfLength;
            if (Math.Abs(Distance - centre) < reach)
            {
                for (int layer = 0; layer < FieldSpec.LayerCount; layer++)
                {
                    float cy = FieldSpec.RailY(layer) + FieldSpec.ObstacleHalfSize;
                    if (Math.Abs(ShipY - cy) > FieldSpec.ObstacleHalfSize + FieldSpec.ShipRadius * 0.8f)
                    {
                        continue;
                    }

                    for (int lane = 0; lane < FieldSpec.LaneCount; lane++)
                    {
                        if (row.Get(lane, layer) != CellType.Obstacle)
                        {
                            continue;
                        }

                        if (Math.Abs(ShipX - FieldSpec.LaneX(lane)) > FieldSpec.ObstacleHalfSize + FieldSpec.ShipRadius * 0.8f)
                        {
                            continue;
                        }

                        BreakObstacle(_tile, lane, layer, CrashCause.Obstacle);
                        if (IsCrashed)
                        {
                            return;
                        }
                    }
                }
            }
            else if (Distance > centre + reach && _obstacleCheckedTile != _tile)
            {
                _obstacleCheckedTile = _tile;
                for (int lane = 0; lane < FieldSpec.LaneCount; lane++)
                {
                    for (int layer = Math.Max(0, _layer - 1); layer <= Math.Min(FieldSpec.TopLayer, _layer + 1); layer++)
                    {
                        if (row.Get(lane, layer) != CellType.Obstacle || lane == Lane && layer == _layer)
                        {
                            continue;
                        }

                        _stats.CubesDodged++;
                        if (lane == _previousLane && layer == _layer && Lane != lane &&
                            Time - _laneChangeTime < FieldSpec.NearMissWindow + 0.15)
                        {
                            NearMiss(centre);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// The craft meets something deadly (an obstacle, the ceiling): Overload, invulnerability or an invincible run
        /// break it, a shield breaks instead, otherwise the run crashes. True if the craft survived.
        /// </summary>
        private bool BreakObstacle(long tile, int lane, int layer, CrashCause cause)
        {
            bool obstacle = _grid.Get(tile, lane, layer) == CellType.Obstacle;
            if (OverloadActive || _invulnerableTimer > 0f || _config.Invincible)
            {
                if (obstacle)
                {
                    Smash(tile, lane, layer, OverloadActive);
                }

                return true;
            }

            if (ShieldActive)
            {
                ShieldActive = false;
                _stats.ShieldsBroken++;
                _invulnerableTimer = ShieldInvulnerability;
                Emit(RunEventType.ShieldBroken, 0, (int)cause, 0f, ShipX, ShipY, Distance);
                if (obstacle)
                {
                    Smash(tile, lane, layer, false);
                }

                return true;
            }

            Crash(cause);
            return false;
        }

        private void Smash(long tile, int lane, int layer, bool award)
        {
            _stats.Smashed++;
            _grid.Set(tile, lane, layer, CellType.Track);
            double z = FieldSpec.TileStart(tile) + FieldSpec.TileLength * 0.5;
            Emit(RunEventType.Smashed, (int)tile, FieldSpec.CellIndex(lane, layer), 0f, FieldSpec.LaneX(lane),
                FieldSpec.RailY(layer) + FieldSpec.ObstacleHalfSize, z);
            if (award)
            {
                int points = SmashPoints * _multiplier;
                _score += points * (1f + _config.ScoreBonus);
                Emit(RunEventType.Bonus, 0, points, _multiplier);
            }
        }

        private void Crash(CrashCause cause)
        {
            if (IsCrashed)
            {
                return;
            }

            IsCrashed = true;
            ShipVelocityX = 0f;
            _overloadRemaining = 0f;
            _stats.Cause = cause;
            Emit(RunEventType.Crashed, 0, (int)cause, 0f, ShipX, ShipY, Distance);
        }

        // ------------------------------------------------------------------ the wall

        private void StartClimb()
        {
            Phase = RunPhase.Climbing;
            _phaseT = 0f;
            _phaseFromZ = Distance;
            _phaseFromY = ShipY;
            _phaseToZ = FieldSpec.TileStart(_tile) + FieldSpec.TileLength * 0.5;
            _phaseToY = WallTopY + 1.2f;
            _queuedLaneShift = 0;
            _graceTimer = 0f;
            Emit(RunEventType.WallReached, 0, _level, 0f, ShipX, ShipY, Distance);
        }

        private void Climb(float h)
        {
            _phaseT = Math.Min(1f, _phaseT + h / ClimbSeconds);
            float t = _phaseT;
            float along = 1f - (1f - t) * (1f - t);
            float rise = MathUtil.SmoothStep(Math.Min(1f, t * 1.25f));
            double z = _phaseFromZ + (_phaseToZ - _phaseFromZ) * along;
            Speed = (float)Math.Max(0.0, (z - Distance) / Math.Max(1e-4f, h));
            Distance = z;
            SetY(_phaseFromY + (_phaseToY - _phaseFromY) * rise, h);
            Steer(h);
            if (_phaseT >= 1f)
            {
                ReachCheckpoint();
            }
        }

        private void ReachCheckpoint()
        {
            Phase = RunPhase.Checkpoint;
            ShipVelocityY = 0f;
            Speed = 0f;
            _checkpointWait = 0f;
            int cleared = _level;
            int bonus = (int)(LevelClearPoints * cleared * (1f + _config.ScoreBonus));
            _score += bonus;
            _stats.LevelsCleared++;
            Emit(RunEventType.LevelCleared, 0, cleared, bonus);
            Emit(RunEventType.CheckpointReached, 0, cleared, 0f, ShipX, ShipY, Distance);
        }

        private void Descend(float h)
        {
            _phaseT = Math.Min(1f, _phaseT + h / DescendSeconds);
            float t = _phaseT;
            float along = t * t * (3f - 2f * t);
            double z = _phaseFromZ + (_phaseToZ - _phaseFromZ) * along;
            Speed = (float)Math.Max(0.0, (z - Distance) / Math.Max(1e-4f, h));
            Distance = z;
            SetY(_phaseFromY + (_phaseToY - _phaseFromY) * (t * t), h);
            Steer(h);
            if (_phaseT >= 1f)
            {
                Phase = RunPhase.Riding;
                _tile = FieldSpec.TileOf(Distance);
                _layer = FieldSpec.MiddleLayer;
                ShipY = FieldSpec.RideY(_layer);
                ShipVelocityY = 0f;
                _entryLane = Lane;
                _tileEnterTime = Time;
                _gapSince = -1.0;
            }

            Activate();
        }

        private void ApplyPerk(CheckpointPerk perk)
        {
            switch (perk)
            {
                case CheckpointPerk.Refuel:
                    Fuel = _ship.MaxFuel;
                    break;
                case CheckpointPerk.Patch:
                    Hull = Math.Min(_ship.MaxHull, Hull + 1);
                    break;
                case CheckpointPerk.Shield:
                    if (!ShieldActive)
                    {
                        ShieldActive = true;
                        Emit(RunEventType.ShieldGained);
                    }

                    break;
                case CheckpointPerk.Efficiency:
                    _costScale *= 0.85f;
                    break;
                case CheckpointPerk.BigTank:
                    _ship.MaxFuel += 5f;
                    Fuel += 5f;
                    break;
                case CheckpointPerk.LongerBoosts:
                    _durationScale *= 1.25f;
                    break;
                default:
                    return;
            }

            CheckFuelLow();
            Emit(RunEventType.PerkApplied, 0, (int)perk);
        }

        // ------------------------------------------------------------------ pickups

        /// <summary>Generates the track ahead, drops rows behind, and moves pending pickups into the active window.</summary>
        private void Activate()
        {
            double limit = Distance + FieldSpec.SpawnAhead;
            _generator.GenerateUntil(FieldSpec.TileOf(limit + 80.0) + 1);
            _grid.DropBefore(FieldSpec.TileOf(Distance - FieldSpec.DespawnBehind) - 1);

            int moved = 0;
            while (moved < _pendingPickups.Count && _pendingPickups[moved].Z < limit)
            {
                Pickup p = _pendingPickups[moved];
                _pickups.Add(p);
                Emit(RunEventType.PickupSpawned, p.Id, (int)p.Kind, 0f, p.X, p.Y, p.Z);
                moved++;
            }

            if (moved > 0)
            {
                _pendingPickups.RemoveRange(0, moved);
            }
        }

        private void UpdatePickups(float h)
        {
            bool magnet = _magnetRemaining > 0f;
            float pull = MathUtil.Damp(9f, h);
            double despawn = Distance - FieldSpec.DespawnBehind;

            for (int i = _pickups.Count - 1; i >= 0; i--)
            {
                Pickup p = _pickups[i];
                float dx = p.X - ShipX;
                float dy = p.Y - ShipY;
                if (magnet && !p.Attracted && p.IsResource && p.Z > Distance - 1.0 && p.Z - Distance < MagnetRangeZ &&
                    Math.Abs(dx) < FieldSpec.MagnetRadius && Math.Abs(dy) < FieldSpec.LayerSpacing * 1.6f)
                {
                    p.Attracted = true;
                }

                if (p.Attracted)
                {
                    p.X += (ShipX - p.X) * pull;
                    p.Y += (ShipY - p.Y) * pull;
                    p.Z += (Distance + 0.2 - p.Z) * pull;
                    dx = p.X - ShipX;
                    dy = p.Y - ShipY;
                }

                float radius = p.IsResource ? FieldSpec.PickupRadius : FieldSpec.PowerUpPickupRadius;
                if (Math.Abs(p.Z - Distance) < PickupDepthWindow && dx * dx + dy * dy < radius * radius &&
                    Phase != RunPhase.Climbing)
                {
                    Collect(p);
                    _pickups.RemoveAt(i);
                    continue;
                }

                if (p.Z < despawn)
                {
                    if (p.Group != null)
                    {
                        p.Group.Broken = true; // a gem left behind: its line pays no bonus
                    }

                    Emit(RunEventType.PickupDespawned, p.Id);
                    _pickups.RemoveAt(i);
                }
            }
        }

        private void Collect(Pickup p)
        {
            p.Collected = true;
            Emit(RunEventType.PickupCollected, p.Id, (int)p.Kind, p.IsResource ? p.Value : 0, p.X, p.Y, p.Z);
            if (p.Kind == PickupKind.Gem)
            {
                _stats.Gems += p.Value;
                _score += GemPoints * _multiplier * (1f + _config.ScoreBonus);
                CountTrailGem(p.Group);
                return;
            }

            if (p.Kind != PickupKind.Fuel)
            {
                _stats.PowerUps++;
            }

            ApplyPickup(p.Kind, p.Value);
        }

        private void ApplyPickup(PickupKind kind, int value)
        {
            float duration = _ship.DurationScale * _durationScale;
            switch (kind)
            {
                case PickupKind.Fuel:
                    float before = Fuel;
                    Fuel = Math.Min(_ship.MaxFuel, Fuel + value);
                    _stats.FuelCollected += Fuel - before;
                    CheckFuelLow();
                    break;
                case PickupKind.Shield:
                    if (!ShieldActive)
                    {
                        ShieldActive = true;
                        Emit(RunEventType.ShieldGained);
                    }

                    break;
                case PickupKind.Repair:
                    Hull = _ship.MaxHull;
                    _stats.Repairs++;
                    Emit(RunEventType.Repaired, 0, Hull);
                    break;
                case PickupKind.Magnet:
                    bool wasActive = _magnetRemaining > 0f;
                    _magnetRemaining = _balance.MagnetSeconds * duration;
                    if (!wasActive)
                    {
                        Emit(RunEventType.MagnetStarted);
                    }

                    break;
                case PickupKind.InstantBuild:
                    _instantRemaining = _balance.InstantBuildSeconds * duration;
                    Emit(RunEventType.PowerUpStarted, 0, (int)kind, _instantRemaining);
                    break;
                case PickupKind.Overload:
                    _overloadRemaining = Math.Max(_overloadRemaining, _balance.OverloadSeconds * duration);
                    _graceTimer = 0f;
                    _stats.Overloads++;
                    Emit(RunEventType.OverloadStarted, 0, 0, _overloadRemaining);
                    break;
            }
        }

        /// <summary>
        /// Counts a collected gem toward its line; once every gem of the line is in, pays the full-line bonus: as many
        /// points again as its gems gave (at the current multiplier).
        /// </summary>
        private void CountTrailGem(GemTrail trail)
        {
            if (trail == null || trail.Broken || trail.Size < TrackGenerator.MinTrailGems)
            {
                return;
            }

            trail.Collected++;
            if (trail.Collected != trail.Size)
            {
                return;
            }

            _stats.FullTrails++;
            int points = (int)Math.Round(trail.Size * GemPoints * _multiplier * (1f + _config.ScoreBonus));
            _score += points;
            AddChain();
            Emit(RunEventType.TrailCompleted, trail.Id, points, trail.Size, ShipX, ShipY, Distance);
        }

        // ------------------------------------------------------------------ scoring and hints

        private void NearMiss(double z)
        {
            _stats.NearMisses++;
            _laneChangeTime = -10.0; // one dodge, one near miss
            AddChain();
            int points = NearMissPoints * _multiplier * (OverloadActive ? 2 : 1);
            _score += points * (1f + _config.ScoreBonus);
            Emit(RunEventType.NearMiss, 0, _chain, 0f, ShipX, ShipY, z);
            Emit(RunEventType.Bonus, 0, points, _multiplier);
        }

        private void AddChain()
        {
            _chain++;
            _chainTimer = ChainTimeout;
            if (_chain > _stats.BestNearMissChain)
            {
                _stats.BestNearMissChain = _chain;
            }

            RecomputeMultiplier();
        }

        private void ResetChain()
        {
            _chain = 0;
            _chainTimer = 0f;
            RecomputeMultiplier();
        }

        private void RecomputeMultiplier()
        {
            int m = Math.Min(MaxMultiplier, 1 + _chain / ChainPerMultiplier);
            if (m != _multiplier)
            {
                _multiplier = m;
                if (m > _stats.MaxMultiplier)
                {
                    _stats.MaxMultiplier = m;
                }

                Emit(RunEventType.MultiplierChanged, 0, m);
            }
        }

        private void UpdateHints()
        {
            if (!_config.Tutorial || _hintsShown == -1)
            {
                return;
            }

            long gap = GapAhead();
            if (gap > _tile && gap - _tile <= 3)
            {
                Hint(TutorialHint.Bridge);
            }

            if (_level >= 2)
            {
                for (long t = _tile + 1; t <= _tile + 4; t++)
                {
                    for (int layer = 0; layer < FieldSpec.LayerCount; layer++)
                    {
                        if (Cells.IsRamp(_grid.Get(t, Lane, layer)))
                        {
                            Hint(TutorialHint.Ramps);
                        }

                        if (_grid.Get(t, Lane, layer) == CellType.Obstacle)
                        {
                            Hint(TutorialHint.Obstacles);
                        }
                    }
                }
            }

            if (_layer == FieldSpec.TopLayer && _level >= 2)
            {
                Hint(TutorialHint.Edges);
            }

            if (Fuel < _ship.RampUpCost)
            {
                Hint(TutorialHint.Fuel);
            }
        }

        private void Hint(TutorialHint hint)
        {
            if (!_config.Tutorial)
            {
                return;
            }

            int bit = 1 << (int)hint;
            if ((_hintsShown & bit) != 0)
            {
                return;
            }

            _hintsShown |= bit;
            Emit(RunEventType.TutorialHint, 0, (int)hint);
        }

        private void Emit(RunEventType type, int id = 0, int count = 0, float value = 0f, float x = 0f, float y = 0f,
            double z = 0)
        {
            _events.Add(new RunEvent(type, id, count, value, x, y, z));
        }
    }
}
