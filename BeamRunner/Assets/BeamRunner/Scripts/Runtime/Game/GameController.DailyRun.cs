using System;
using BeamRunner.Core.Meta;
using BeamRunner.Core.Sim;
using BeamRunner.Rendering;

namespace BeamRunner.Game
{
    /// <summary>
    /// Daily Run and ghost racing (rules in <c>BeamRunner.Core.Meta.DailyRuns</c> / <c>ProfileService.DailyRun.cs</c>).
    /// A Daily Run is an ordinary run on today's UTC seed with the Daily Run rules (<see cref="ProfileService.CreateDailyRunConfig"/>);
    /// RETRY on its results and RESTART from its pause menu start another Daily Run, PLAY on the menu a normal one. While it
    /// flies, the run is recorded into a <see cref="GhostRecorder"/>; the day's best recording is replayed on later attempts
    /// as a <see cref="GhostView"/> (visual only). Hooks in <c>GameController.cs</c>: <see cref="CreateRunConfigForMode"/>
    /// (BeginRun), <see cref="RecordGhostFrame"/> (TickRunning), <see cref="ApplyDailyRunResult"/> (FinishRun); in
    /// <c>GameController.Presentation.cs</c>: <see cref="TickGhost"/>.
    /// </summary>
    public sealed partial class GameController
    {
        private bool _dailyMode;
        private DailyRunTicket _dailyTicket;
        private GhostRecorder _ghostRecorder;
        private GhostView _ghostView;
        private string _ghostDesign;

        /// <summary>
        /// A Daily Run ended (crash, quit or restart) and was applied to the profile — raised before
        /// <see cref="RunFinished"/>, for every Daily Run including the ones that did not count
        /// (<see cref="DailyRunOutcome.Counted"/>; only counted ones belong on a leaderboard). Also on
        /// <see cref="RunSummary.Daily"/>. Core-level hook for counted runs only: <see cref="ProfileService.DailyRunCompleted"/>.
        /// </summary>
        public event Action<DailyRunOutcome> DailyRunFinished;

        /// <summary>True when the Daily Run can be started (the first flight is done).</summary>
        public bool DailyRunAvailable
        {
            get { return Profile != null && Profile.DailyRunUnlocked; }
        }

        /// <summary>The current run — or, on its results screen, the last run — is a Daily Run.</summary>
        public bool IsDailyRun
        {
            get { return Run != null && _dailyMode && _dailyTicket != null; }
        }

        /// <summary>Daily Run context of the current/last Daily Run (null for normal runs).</summary>
        public DailyRunTicket DailyTicket
        {
            get { return IsDailyRun ? _dailyTicket : null; }
        }

        /// <summary>The ghost raced in the current Daily Run, if any and if enabled in Settings (null otherwise).</summary>
        public GhostPath ActiveGhost
        {
            get
            {
                DailyRunTicket ticket = DailyTicket;
                return ticket != null && ticket.Ghost != null && Profile.Settings.ShowGhost ? ticket.Ghost : null;
            }
        }

        /// <summary>True while the ghost is drawn in front of the camera (tests / diagnostics).</summary>
        public bool GhostVisible
        {
            get { return _ghostView != null && ActiveGhost != null && _ghostView.IsVisibleInView; }
        }

        /// <summary>
        /// Gap to the ghost for the HUD: <paramref name="metres"/> &gt; 0 when the player is ahead of where the ghost was at
        /// the same run time; <paramref name="ghostFinished"/> once the ghost's run has ended (it crashed or was quit
        /// there, <see cref="GhostPath.Length"/> metres in). False when no ghost is raced.
        /// </summary>
        public bool TryGetGhostGap(out double metres, out bool ghostFinished)
        {
            GhostPath ghost = ActiveGhost;
            RunSimulation run = Run;
            if (ghost == null || run == null)
            {
                metres = 0.0;
                ghostFinished = false;
                return false;
            }

            metres = run.Distance - ghost.DistanceAt(run.Time);
            ghostFinished = ghost.IsFinished(run.Time);
            return true;
        }

        /// <summary>Starts today's Daily Run from the menu or hangar (the main menu's DAILY RUN button).</summary>
        public void StartDailyRun()
        {
            if (!IsBootComplete || !DailyRunAvailable ||
                (State != GameState.Menu && State != GameState.Hangar && State != GameState.GameOver))
            {
                return;
            }

            _dailyMode = true;
            BeginRun();
        }

        // ------------------------------------------------------------------ hooks (GameController.cs)

        /// <summary>BeginRun: the run's configuration for the current mode; prepares the Daily Run ticket and recorder.</summary>
        private RunConfig CreateRunConfigForMode(int seed)
        {
            _dailyTicket = null;
            _ghostRecorder = null;
            if (!_dailyMode || !DailyRunAvailable)
            {
                _dailyMode = false;
                return Profile.CreateRunConfig(seed);
            }

            _dailyTicket = Profile.BeginDailyRun();
            _ghostRecorder = new GhostRecorder(_dailyTicket.Seed, DailyRuns.GeneratorVersion);
            _ghostDesign = GhostDesign();
            if (_ghostView != null)
            {
                _ghostView.Hide();
            }

            return Profile.CreateDailyRunConfig(_dailyTicket);
        }

        /// <summary>BeginRun (distance 0) and TickRunning (after each step): feeds the ghost recorder.</summary>
        private void RecordGhostFrame()
        {
            RunSimulation run = Run;
            if (_ghostRecorder != null && run != null && _dailyMode)
            {
                _ghostRecorder.Record(run.Distance, run.ShipX, run.ShipY, run.Time);
            }
        }

        /// <summary>FinishRun, right after <see cref="ProfileService.ApplyRun"/>: records the Daily Run (best, ghost, streak, rewards).</summary>
        private void ApplyDailyRunResult(RunSummary summary)
        {
            DailyRunTicket ticket = _dailyTicket;
            if (!_dailyMode || ticket == null || ticket.Applied || summary == null)
            {
                return;
            }

            DailyRunOutcome outcome;
            try
            {
                RecordGhostFrame();
                GhostPath recorded = _ghostRecorder != null ? _ghostRecorder.ToPath() : null;
                outcome = Profile.ApplyDailyRun(summary, ticket, recorded);
            }
            catch (Exception e)
            {
                // A throwing DailyRunCompleted handler: the run is already recorded; never abort FinishRun.
                FieldLog.Exception("Daily Run result", e);
                outcome = ticket.Outcome;
            }

            _ghostRecorder = null;
            if (outcome == null)
            {
                return;
            }

            try
            {
                DailyRunFinished?.Invoke(outcome);
            }
            catch (Exception e)
            {
                FieldLog.Exception("DailyRunFinished handler", e);
            }
        }

        // ------------------------------------------------------------------ presentation (GameController.Presentation.cs)

        /// <summary>Per frame: flies the ghost at the player's run time, or hides it.</summary>
        private void TickGhost(float dt)
        {
            GhostPath ghost = State == GameState.Hangar || State == GameState.GameOver ? null : ActiveGhost;
            RunSimulation run = Run;
            if (ghost == null || run == null || _world == null)
            {
                if (_ghostView != null)
                {
                    _ghostView.Hide();
                }

                return;
            }

            if (_ghostView == null)
            {
                _ghostView = GhostView.Create(_world);
            }

            _ghostView.SetDesign(_ghostDesign);
            double distance = ghost.DistanceAt(run.Time);
            float x;
            float y;
            ghost.PositionAt(distance, out x, out y);
            float worldZ = FieldMath.WorldZ(distance, run.Distance);
            _ghostView.Tick(x, y, worldZ, run.ShipX, run.ShipY, !ghost.IsFinished(run.Time), dt);
        }

        private string GhostDesign()
        {
            string ship = Profile.Profile.DailyRun != null ? Profile.Profile.DailyRun.BestShip : null;
            if (string.IsNullOrEmpty(ship) || !ShipCatalog.Exists(ship))
            {
                ship = Profile.Profile.SelectedShip;
            }

            return ShipCatalog.Get(ship).Design;
        }
    }
}
