using System;
using System.Collections.Generic;
using BeamRunner.Core.Localization;
using BeamRunner.Core.Sim;

namespace BeamRunner.Core.Meta
{
    /// <summary>What a mission measures. "Run" = within a single run, "Total" = accumulated across runs.</summary>
    public enum MissionType
    {
        GemsRun = 0,
        GemsTotal,
        DistanceRun,
        DistanceTotal,
        NearMissesRun,
        NearMissesTotal,
        OverloadsRun,
        OverloadsTotal,
        BridgesTotal,
        SmashesTotal,
        CubesDodgedRun,
        LevelsClearedRun,
        ScoreRun,
        MultiplierRun,
        PowerUpsTotal,
        PlayRuns,
        ChainRun,
        BridgesRun,
        LevelsClearedTotal,
        CubesDodgedTotal,
        BuildsRun,
        BuildsTotal,
    }

    /// <summary>An active mission and its progress.</summary>
    [Serializable]
    public sealed class Mission
    {
        public int Type;
        public long Target;
        public long Progress;
        public int Reward;
        public bool Completed;

        public MissionType MissionType
        {
            get { return (MissionType)Type; }
        }

        public float Progress01
        {
            get { return Target <= 0 ? 1f : MathUtil.Clamp01((float)Progress / Target); }
        }
    }

    /// <summary>The three active missions and how many full sets have been completed.</summary>
    [Serializable]
    public sealed class MissionState
    {
        public List<Mission> Active = new List<Mission>();
        public int SetsCompleted;
    }

    /// <summary>Generates, describes and advances missions.</summary>
    public static class MissionSystem
    {
        public const int ActiveCount = 3;

        private struct Template
        {
            public MissionType Type;
            public long BaseTarget;
            public long TargetPerRank;
            public int BaseReward;
            public int MinRank;

            /// <summary>Upper bound for "in one run" targets (0 = none): see <see cref="MaxTargetFor"/>.</summary>
            public long MaxTarget;

            public Template(MissionType type, long baseTarget, long targetPerRank, int baseReward, int minRank = 1,
                long maxTarget = 0)
            {
                Type = type;
                BaseTarget = baseTarget;
                TargetPerRank = targetPerRank;
                BaseReward = baseReward;
                MinRank = minRank;
                MaxTarget = maxTarget;
            }
        }

        // "In one run" targets are capped so a set can always be cleared (uncapped linear growth would make them impossible
        // at high ranks and freeze pilot rank). The caps sit at what a skilled player does in a run through about six
        // levels. Obstacles appear at level 5, so obstacle missions wait for rank 4; Overload first drops in level 2.
        // Totals accumulate over runs, so they stay uncapped. Re-measure against real runs before release.
        private static readonly Template[] Templates =
        {
            new Template(MissionType.GemsRun, 15, 5, 120, 1, 150),
            new Template(MissionType.GemsTotal, 75, 30, 100),
            new Template(MissionType.DistanceRun, 500, 200, 150, 1, 5000),
            new Template(MissionType.DistanceTotal, 2500, 1000, 120),
            new Template(MissionType.NearMissesRun, 2, 1, 150, 2, 15),
            new Template(MissionType.NearMissesTotal, 8, 4, 120, 2),
            new Template(MissionType.OverloadsRun, 1, 1, 160, 3, 3),
            new Template(MissionType.OverloadsTotal, 2, 1, 120, 2),
            new Template(MissionType.BridgesTotal, 30, 15, 100),
            new Template(MissionType.SmashesTotal, 3, 2, 140, 4),
            new Template(MissionType.CubesDodgedRun, 8, 4, 140, 4, 80),
            new Template(MissionType.LevelsClearedRun, 1, 1, 200, 1, 8),
            new Template(MissionType.ScoreRun, 3000, 1500, 150, 1, 25000),
            new Template(MissionType.MultiplierRun, 2, 1, 180, 2),
            new Template(MissionType.PowerUpsTotal, 3, 1, 110),
            new Template(MissionType.PlayRuns, 3, 1, 90),
            new Template(MissionType.ChainRun, 4, 2, 170, 3, 25),
            new Template(MissionType.BridgesRun, 6, 3, 150, 1, 50),
            new Template(MissionType.LevelsClearedTotal, 3, 2, 130),
            new Template(MissionType.CubesDodgedTotal, 30, 15, 110, 4),
            new Template(MissionType.BuildsRun, 8, 4, 140, 1, 80),
            new Template(MissionType.BuildsTotal, 40, 20, 110),
        };

        /// <summary>Fills empty mission slots for the given rank. Never duplicates a type within a set.</summary>
        public static void EnsureMissions(MissionState state, int rank, Rng rng)
        {
            ClampTargets(state);
            int guard = 0;
            while (state.Active.Count < ActiveCount && guard++ < 100)
            {
                Template t = Templates[rng.Range(0, Templates.Length)];
                if (t.MinRank > rank || HasType(state, t.Type))
                {
                    continue;
                }

                state.Active.Add(Create(t, rank));
            }
        }

        private static bool HasType(MissionState state, MissionType type)
        {
            for (int i = 0; i < state.Active.Count; i++)
            {
                if (state.Active[i].Type == (int)type)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Largest target a mission of <paramref name="type"/> may have (long.MaxValue = uncapped). Missions created by
        /// older versions are clamped to it by <see cref="EnsureMissions"/>, so a stored impossible mission can't
        /// freeze pilot rank either.
        /// </summary>
        public static long MaxTargetFor(MissionType type)
        {
            if (type == MissionType.MultiplierRun)
            {
                return RunSimulation.MaxMultiplier;
            }

            for (int i = 0; i < Templates.Length; i++)
            {
                if (Templates[i].Type == type && Templates[i].MaxTarget > 0)
                {
                    return Templates[i].MaxTarget;
                }
            }

            return long.MaxValue;
        }

        /// <summary>Lowers active mission targets above <see cref="MaxTargetFor"/> to the cap.</summary>
        public static void ClampTargets(MissionState state)
        {
            if (state?.Active == null)
            {
                return;
            }

            for (int i = 0; i < state.Active.Count; i++)
            {
                Mission m = state.Active[i];
                if (m != null && m.Target > MaxTargetFor(m.MissionType))
                {
                    m.Target = MaxTargetFor(m.MissionType);
                }
            }
        }

        private static Mission Create(Template t, int rank)
        {
            int r = Math.Max(0, rank - 1);
            // Levels in one run climb one level every two ranks (a level is about 35 s of flawless flying; +1 per rank
            // would outpace everything else).
            long perRank = t.Type == MissionType.LevelsClearedRun ? r / 2 : t.TargetPerRank * r;
            long target = Math.Min(RoundNice(t.BaseTarget + perRank), MaxTargetFor(t.Type));

            return new Mission
            {
                Type = (int)t.Type,
                Target = target,
                Progress = 0,
                Reward = (int)RoundNice((long)(t.BaseReward * (1f + 0.12f * r))),
                Completed = false,
            };
        }

        /// <summary>Rounds to 1–2 significant "friendly" digits (e.g. 1234 → 1200, 87 → 90).</summary>
        public static long RoundNice(long v)
        {
            if (v < 20)
            {
                return v;
            }

            long magnitude = 1;
            while (v / magnitude >= 100)
            {
                magnitude *= 10;
            }

            long step = magnitude >= 10 ? magnitude : 5;
            return Math.Max(step, (v + step / 2) / step * step);
        }

        /// <summary>Description in the current language, e.g. "Collect 40 gems in one run".</summary>
        public static string Describe(Mission m)
        {
            long n = m.Target;
            switch (m.MissionType)
            {
                case MissionType.GemsRun: return Loc.T("mission.gems_run", ("n", n));
                case MissionType.GemsTotal: return Loc.T("mission.gems_total", ("n", n));
                case MissionType.DistanceRun: return Loc.T("mission.distance_run", ("n", n));
                case MissionType.DistanceTotal: return Loc.T("mission.distance_total", ("n", n));
                case MissionType.NearMissesRun: return Loc.T("mission.near_misses_run", ("n", n));
                case MissionType.NearMissesTotal: return Loc.T("mission.near_misses_total", ("n", n));
                // "once" is keyed on exactly 1: the plural category "one" also covers 21, 31… in Russian.
                case MissionType.OverloadsRun:
                    return n == 1 ? Loc.T("mission.overload_run_once") : Loc.T("mission.overloads_run", ("n", n));
                case MissionType.OverloadsTotal: return Loc.T("mission.overloads_total", ("n", n));
                case MissionType.BridgesTotal: return Loc.T("mission.bridges_total", ("n", n));
                case MissionType.SmashesTotal: return Loc.T("mission.smashes_total", ("n", n));
                case MissionType.CubesDodgedRun: return Loc.T("mission.cubes_run", ("n", n));
                case MissionType.LevelsClearedRun: return Loc.T("mission.levels_run", ("n", n));
                case MissionType.ScoreRun: return Loc.T("mission.score_run", ("n", n));
                case MissionType.MultiplierRun: return Loc.T("mission.multiplier_run", ("n", n));
                case MissionType.PowerUpsTotal: return Loc.T("mission.powerups_total", ("n", n));
                case MissionType.PlayRuns: return Loc.T("mission.play_runs", ("n", n));
                case MissionType.ChainRun: return Loc.T("mission.chain_run", ("n", n));
                case MissionType.BridgesRun: return Loc.T("mission.bridges_run", ("n", n));
                case MissionType.LevelsClearedTotal: return Loc.T("mission.levels_total", ("n", n));
                case MissionType.CubesDodgedTotal: return Loc.T("mission.cubes_total", ("n", n));
                case MissionType.BuildsRun: return Loc.T("mission.builds_run", ("n", n));
                case MissionType.BuildsTotal: return Loc.T("mission.builds_total", ("n", n));
                default: return Loc.T("mission.generic");
            }
        }

        /// <summary>The value a single run contributes to a mission type.</summary>
        public static long RunValue(MissionType type, RunResult r)
        {
            switch (type)
            {
                case MissionType.GemsRun:
                case MissionType.GemsTotal:
                    return r.Gems;
                case MissionType.DistanceRun:
                case MissionType.DistanceTotal:
                    return (long)r.Distance;
                case MissionType.NearMissesRun:
                case MissionType.NearMissesTotal:
                    return r.NearMisses;
                case MissionType.OverloadsRun:
                case MissionType.OverloadsTotal:
                    return r.Overloads;
                case MissionType.BridgesTotal:
                case MissionType.BridgesRun:
                    return r.GapsBridged;
                case MissionType.SmashesTotal:
                    return r.Smashed;
                case MissionType.CubesDodgedRun:
                case MissionType.CubesDodgedTotal:
                    return r.CubesDodged;
                case MissionType.BuildsRun:
                case MissionType.BuildsTotal:
                    return r.Builds;
                case MissionType.LevelsClearedRun:
                case MissionType.LevelsClearedTotal:
                    return r.LevelsCleared;
                case MissionType.ScoreRun:
                    return r.Score;
                case MissionType.MultiplierRun:
                    return r.MaxMultiplier;
                case MissionType.PowerUpsTotal:
                    return r.PowerUps;
                case MissionType.PlayRuns:
                    return 1;
                case MissionType.ChainRun:
                    return r.BestNearMissChain;
                default:
                    return 0;
            }
        }

        public static bool IsCumulative(MissionType type)
        {
            switch (type)
            {
                case MissionType.GemsTotal:
                case MissionType.DistanceTotal:
                case MissionType.NearMissesTotal:
                case MissionType.OverloadsTotal:
                case MissionType.BridgesTotal:
                case MissionType.SmashesTotal:
                case MissionType.PowerUpsTotal:
                case MissionType.PlayRuns:
                case MissionType.LevelsClearedTotal:
                case MissionType.CubesDodgedTotal:
                case MissionType.BuildsTotal:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Progress the mission would show if <paramref name="r"/> were applied now.</summary>
        public static long ProjectedProgress(Mission m, RunResult r)
        {
            long v = RunValue(m.MissionType, r);
            return IsCumulative(m.MissionType) ? m.Progress + v : Math.Max(m.Progress, v);
        }

        /// <summary>True if this mission is not yet complete but would be with the live run result.</summary>
        public static bool WouldComplete(Mission m, RunResult live)
        {
            if (m.Completed || m.MissionType == MissionType.PlayRuns)
            {
                return false;
            }

            return ProjectedProgress(m, live) >= m.Target;
        }

        /// <summary>Applies a finished run. Returns the missions completed by it (already marked).</summary>
        public static List<Mission> ApplyRun(MissionState state, RunResult r)
        {
            var completed = new List<Mission>();
            for (int i = 0; i < state.Active.Count; i++)
            {
                Mission m = state.Active[i];
                if (m.Completed)
                {
                    continue;
                }

                m.Progress = Math.Min(m.Target, ProjectedProgress(m, r));
                if (m.Progress >= m.Target)
                {
                    m.Completed = true;
                    completed.Add(m);
                }
            }

            return completed;
        }

        public static bool AllCompleted(MissionState state)
        {
            if (state.Active.Count < ActiveCount)
            {
                return false;
            }

            for (int i = 0; i < state.Active.Count; i++)
            {
                if (!state.Active[i].Completed)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
