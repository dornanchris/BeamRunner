using System;
using BeamRunner.Core.Sim;
using UnityEngine;

namespace BeamRunner.Game
{
    /// <summary>
    /// The level wall checkpoint (design doc "Level transition: the wall"): when the craft tops a wall the run waits in
    /// <see cref="GameState.Checkpoint"/> while the UI shows the level's stats and three perks; picking one drops the
    /// craft onto the next level's middle layer. An optional rewarded ad takes all three.
    /// </summary>
    public sealed partial class GameController
    {
        /// <summary>Perks offered at a checkpoint.</summary>
        public const int CheckpointChoices = 3;

        private static readonly CheckpointPerk[] PerkPool =
        {
            CheckpointPerk.Refuel,
            CheckpointPerk.Patch,
            CheckpointPerk.Shield,
            CheckpointPerk.Efficiency,
            CheckpointPerk.BigTank,
            CheckpointPerk.LongerBoosts,
        };

        private readonly CheckpointPerk[] _perks = new CheckpointPerk[CheckpointChoices];
        private bool _checkpointAdPending;

        /// <summary>The perks offered at the current checkpoint.</summary>
        public CheckpointPerk[] CheckpointPerks
        {
            get { return _perks; }
        }

        /// <summary>Level just cleared at the current checkpoint.</summary>
        public int CheckpointLevel { get; private set; }

        /// <summary>True when the rewarded "take all three" bonus can be offered.</summary>
        public bool CanTakeAllPerksWithAd
        {
            get { return Ads != null && Ads.RewardedReady && !_checkpointAdPending; }
        }

        private void OnCheckpointReached(int level)
        {
            CheckpointLevel = level;
            _checkpointAdPending = false;
            RollPerks();
            SetState(GameState.Checkpoint);
        }

        /// <summary>Picks perk <paramref name="index"/> and continues into the next level.</summary>
        public void ChooseCheckpointPerk(int index)
        {
            if (State != GameState.Checkpoint || Run == null || _checkpointAdPending)
            {
                return;
            }

            CheckpointPerk perk = index >= 0 && index < _perks.Length ? _perks[index] : CheckpointPerk.None;
            LeaveCheckpoint(perk);
        }

        /// <summary>Rewarded ad: all three perks, then continue.</summary>
        public void TakeAllPerksWithAd(Action<bool> onDone)
        {
            if (State != GameState.Checkpoint || Run == null || Ads == null || _checkpointAdPending)
            {
                onDone?.Invoke(false);
                return;
            }

            _checkpointAdPending = true;
            RunSimulation run = Run;
            Ads.ShowRewarded(completed =>
            {
                _checkpointAdPending = false;
                if (completed && State == GameState.Checkpoint && ReferenceEquals(run, Run))
                {
                    for (int i = 0; i < _perks.Length - 1; i++)
                    {
                        run.GrantPerk(_perks[i]);
                    }

                    PresentReward();
                    LeaveCheckpoint(_perks[_perks.Length - 1]);
                }

                onDone?.Invoke(completed);
            });
        }

        private void LeaveCheckpoint(CheckpointPerk perk)
        {
            Run.ContinueFromCheckpoint(perk);
            DispatchRunEvents();
            Input.ResetGestures();
            Time.timeScale = 1f;
            SetState(GameState.Running);
        }

        /// <summary>
        /// Three different perks, skipping ones that would do nothing right now. Seeded by the run and the level, so every
        /// pilot of a Daily Run is offered the same picks.
        /// </summary>
        private void RollPerks()
        {
            RunSimulation run = Run;
            var rng = new Core.Rng((run != null ? run.Config.Seed : 0) ^ (CheckpointLevel * 7919));
            int n = 0;
            int guard = 0;
            while (n < _perks.Length && guard++ < 64)
            {
                CheckpointPerk p = PerkPool[rng.Range(0, PerkPool.Length)];
                bool useless = run != null && (p == CheckpointPerk.Patch && run.Hull >= run.MaxHull ||
                                               p == CheckpointPerk.Shield && run.ShieldActive ||
                                               p == CheckpointPerk.Refuel && run.Fuel >= run.MaxFuel - 0.5f);
                if (useless || Array.IndexOf(_perks, p, 0, n) >= 0)
                {
                    continue;
                }

                _perks[n++] = p;
            }

            for (; n < _perks.Length; n++)
            {
                _perks[n] = PerkPool[n];
            }
        }
    }
}
