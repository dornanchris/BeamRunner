using System;
using System.Collections.Generic;
using System.Reflection;

namespace BeamRunner.Core.Meta
{
    /// <summary>What <see cref="ProfileMerge.MergeInto"/> did (for logs and presentation).</summary>
    public sealed class ProfileMergeReport
    {
        /// <summary>The remote snapshot had a higher <see cref="CloudSyncState.Epoch"/> (a reset elsewhere): it replaced
        /// everything except purchases, the gem ledger and device-local data.</summary>
        public bool AdoptedRemoteEpoch;

        /// <summary>The local snapshot had a higher epoch: remote progress was ignored (purchases and gems still merged).</summary>
        public bool IgnoredOlderRemoteEpoch;

        /// <summary>The remote snapshot was dominant (more play time) — unknown fields were taken from it.</summary>
        public bool RemoteDominant;

        /// <summary>Remove Ads became owned through the merge.</summary>
        public bool GainedAdsRemoved;

        /// <summary>Ships owned now that were not owned locally before.</summary>
        public readonly List<string> NewShips = new List<string>();

        /// <summary>Achievement ids unlocked now that were not unlocked locally before.</summary>
        public readonly List<string> NewAchievements = new List<string>();

        public long GemsBefore;
        public long GemsAfter;
    }

    /// <summary>
    /// Deterministic, conservative merge of two snapshots of the same player's profile (this device's and a cloud
    /// copy). Pure C#; see <c>ProfileMergeTests</c>. Merge rules <see cref="RulesVersion"/> = 1:
    /// <list type="table">
    /// <item><term>Epoch</term><description><see cref="CloudSyncState.Epoch"/> counts "reset progress". A higher remote
    /// epoch replaces all progress; a lower one is ignored. Purchases and the gem ledger merge across epochs.</description></item>
    /// <item><term>Purchases</term><description>Remove Ads: OR — never lost to an older snapshot.</description></item>
    /// <item><term>Gems</term><description>Per-device PN-counter (<see cref="WalletLedger"/>): per device the larger
    /// earned and the larger spent; balance = Σ earned − Σ spent (≥ 0). Exact: gems spent on one device are not
    /// resurrected by the other device's older balance, and none are duplicated or lost (IAP gems included).</description></item>
    /// <item><term>Unlocks</term><description>Ships and achievements: union (achievement rewards were already paid into the
    /// ledger of the device that unlocked them, so the merge pays nothing).</description></item>
    /// <item><term>Stats</term><description>Every numeric <see cref="PlayerStats"/> field (bests AND lifetime totals):
    /// maximum. Idempotent and never double-counts shared history (totals of play split across two offline devices are
    /// under-counted rather than inflated). New numeric stats fields follow automatically.</description></item>
    /// <item><term>Rank &amp; missions</term><description>Pilot rank: max. Missions from the snapshot with more completed
    /// sets (then higher rank); same set: per-mission max progress.</description></item>
    /// <item><term>Local top 10</term><description>Union of both tables (duplicates removed), best 10.</description></item>
    /// <item><term>Login calendar</term><description>The later claim date wins (same date: the longer streak), so a
    /// merge never re-opens a claimed day.</description></item>
    /// <item><term>Daily Run</term><description>The later UTC day's record wins (same day: the better best/ghost, max
    /// attempts/streak); lifetime Daily Run counters: max.</description></item>
    /// <item><term>Device-local</term><description>Settings, ads consent, ad pacing, selected ship (if still owned),
    /// <see cref="WalletLedger.DeviceId"/> and <see cref="CloudSyncState.CloudId"/>/<see cref="CloudSyncState.LastSyncUtcTicks"/>
    /// stay as they are on this device.</description></item>
    /// <item><term>Other fields</term><description>Any field added later and not listed here (top level or in
    /// <see cref="ProfileFlags"/>) is taken from the dominant snapshot: more total play time, then more runs, then
    /// local. Add an explicit rule (and bump <see cref="RulesVersion"/>) when that is not right for a new field.</description></item>
    /// </list>
    /// Merging a snapshot with itself changes nothing. Merging A into B and B into A yields the same progress (same
    /// sets, maxima and balance); only list order and exact ties (missions of equal standing, dominance) favour local.
    /// </summary>
    public static class ProfileMerge
    {
        /// <summary>Version of the rules above. Bump when a rule changes.</summary>
        public const int RulesVersion = 1;

        /// <summary>Ledger key for gems of a snapshot that was written without a ledger.</summary>
        public const string UnattributedDevice = "unattributed";

        private static readonly HashSet<string> s_explicitProfileFields = new HashSet<string>(StringComparer.Ordinal)
        {
            "Version", "Gems", "PilotRank", "SelectedShip", "OwnedShips", "Stats", "Settings", "Missions",
            "Achievements", "Daily", "Leaderboard", "Flags", "DailyRun", "Wallet", "Sync", "Upgrades",
        };

        private static readonly HashSet<string> s_explicitDailyRunFields = new HashSet<string>(StringComparer.Ordinal)
        {
            "Day", "Streak", "BestStreak", "BestScore", "BestDistance", "BestShip", "Attempts", "Ghost", "DaysPlayed",
            "TotalRuns", "AllTimeBestScore",
        };

        private static readonly HashSet<string> s_explicitFlagFields = new HashSet<string>(StringComparer.Ordinal)
        {
            "TutorialDone", "ConsentAsked", "PersonalizedAds", "AdsRemoved", "RatePrompted",
            "GameOversSinceInterstitial", "LastInterstitialUtcTicks", "FirstLaunchUtcTicks", "LastSeenVersion",
        };

        /// <summary>
        /// Merges <paramref name="remote"/> into <paramref name="local"/> in place (the live profile keeps its identity).
        /// Both are sanitized first and both wallets are reconciled. <paramref name="local"/> must have a
        /// <see cref="WalletLedger.DeviceId"/> (<see cref="ArgumentException"/> otherwise). <paramref name="remote"/> is
        /// consumed: parts of it may be moved into <paramref name="local"/>, so do not use it afterwards.
        /// </summary>
        public static ProfileMergeReport MergeInto(PlayerProfile local, PlayerProfile remote)
        {
            if (local == null)
            {
                throw new ArgumentNullException(nameof(local));
            }

            local.Sanitize();
            if (string.IsNullOrEmpty(local.Wallet.DeviceId))
            {
                // Without a device id the local gems could not be booked and would be lost by the merge.
                throw new ArgumentException("The local profile needs a Wallet.DeviceId before merging.", nameof(local));
            }

            var report = new ProfileMergeReport();
            WalletLedger.Reconcile(local);
            report.GemsBefore = local.Gems;
            if (remote == null)
            {
                report.GemsAfter = local.Gems;
                return report;
            }

            remote.Sanitize();
            if (string.IsNullOrEmpty(remote.Wallet.DeviceId) && remote.Gems != WalletLedger.Balance(remote.Wallet))
            {
                // A snapshot whose writer never kept a ledger: book its unexplained gems under a fixed key, so
                // merging it again (or on another device) books them exactly once.
                remote.Wallet.DeviceId = UnattributedDevice;
            }

            WalletLedger.Reconcile(remote);
            var shipsBefore = new List<string>(local.OwnedShips);
            var achievementsBefore = new List<string>(local.Achievements);
            bool adsBefore = local.Flags.AdsRemoved;

            // Purchases and the wallet ledger merge whatever the epochs say.
            local.Flags.AdsRemoved |= remote.Flags.AdsRemoved;
            WalletLedger.MergeInto(local.Wallet, remote.Wallet);

            int localEpoch = local.Sync.Epoch;
            int remoteEpoch = remote.Sync.Epoch;
            if (remoteEpoch > localEpoch)
            {
                report.AdoptedRemoteEpoch = true;
                AdoptRemoteProgress(local, remote);
                local.Sync.Epoch = remoteEpoch;
            }
            else if (remoteEpoch < localEpoch)
            {
                report.IgnoredOlderRemoteEpoch = true;
            }
            else
            {
                MergeProgress(local, remote, report);
            }

            local.Version = Math.Max(local.Version, remote.Version);
            ApplyWalletBalance(local);
            local.Sanitize();

            report.GainedAdsRemoved = local.Flags.AdsRemoved && !adsBefore;
            for (int i = 0; i < local.OwnedShips.Count; i++)
            {
                if (!shipsBefore.Contains(local.OwnedShips[i]))
                {
                    report.NewShips.Add(local.OwnedShips[i]);
                }
            }

            for (int i = 0; i < local.Achievements.Count; i++)
            {
                if (!achievementsBefore.Contains(local.Achievements[i]))
                {
                    report.NewAchievements.Add(local.Achievements[i]);
                }
            }

            report.GemsAfter = local.Gems;
            return report;
        }

        /// <summary>
        /// True when <paramref name="remote"/> counts as the snapshot with more progress: more total play time, then
        /// more runs; ties go to local.
        /// </summary>
        public static bool RemoteIsDominant(PlayerProfile local, PlayerProfile remote)
        {
            PlayerStats l = local.Stats ?? new PlayerStats();
            PlayerStats r = remote.Stats ?? new PlayerStats();
            if (r.TotalPlaySeconds != l.TotalPlaySeconds)
            {
                return r.TotalPlaySeconds > l.TotalPlaySeconds;
            }

            return r.Runs > l.Runs;
        }

        /// <summary>
        /// Copies every public instance field of <typeparamref name="T"/> that is not in <paramref name="handled"/>
        /// from <paramref name="source"/> to <paramref name="target"/> (reference fields are moved, not cloned). The
        /// generic rule for fields a merge does not know about.
        /// </summary>
        public static void CopyUnhandledFields<T>(T target, T source, ICollection<string> handled) where T : class
        {
            if (target == null || source == null)
            {
                return;
            }

            FieldInfo[] fields = typeof(T).GetFields(BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo f = fields[i];
                if (f.IsInitOnly || f.IsLiteral || (handled != null && handled.Contains(f.Name)))
                {
                    continue;
                }

                f.SetValue(target, f.GetValue(source));
            }
        }

        // ------------------------------------------------------------------ epochs

        private static void AdoptRemoteProgress(PlayerProfile local, PlayerProfile remote)
        {
            // Everything that is not device-local, purchases (already OR-ed) or the ledger (already merged) comes from
            // the remote snapshot, including fields this build does not know.
            var keep = new HashSet<string>(StringComparer.Ordinal)
            {
                "Settings", "Flags", "Wallet", "Sync", "SelectedShip", "Version",
            };
            CopyUnhandledFields(local, remote, keep);

            // Flags: shared ones from remote (TutorialDone may legitimately be false after a reset — keep it done).
            ProfileFlags lf = local.Flags;
            ProfileFlags rf = remote.Flags;
            bool tutorial = lf.TutorialDone || rf.TutorialDone;
            bool rated = lf.RatePrompted || rf.RatePrompted;
            CopyUnhandledFields(lf, rf, s_explicitFlagFields);
            lf.TutorialDone = tutorial;
            lf.RatePrompted = rated;
            lf.FirstLaunchUtcTicks = MinNonZero(lf.FirstLaunchUtcTicks, rf.FirstLaunchUtcTicks);
            if (!local.OwnedShips.Contains(local.SelectedShip))
            {
                local.SelectedShip = remote.SelectedShip;
            }
        }

        // ------------------------------------------------------------------ same epoch

        private static void MergeProgress(PlayerProfile local, PlayerProfile remote, ProfileMergeReport report)
        {
            bool remoteDominant = RemoteIsDominant(local, remote);
            report.RemoteDominant = remoteDominant;

            UnionInto(local.OwnedShips, remote.OwnedShips);
            UnionInto(local.Achievements, remote.Achievements);
            MaxNumericFields(local.Stats, remote.Stats);
            MergeMissions(local, remote);
            local.PilotRank = Math.Max(local.PilotRank, remote.PilotRank);
            MergeUpgrades(local, remote);
            local.Leaderboard = MergeLeaderboard(local.Leaderboard, remote.Leaderboard);
            local.Daily = MergeDailyRewards(local.Daily, remote.Daily);
            MergeDailyRun(local.DailyRun, remote.DailyRun);
            MergeFlags(local.Flags, remote.Flags, remoteDominant);

            if (!local.OwnedShips.Contains(local.SelectedShip) && local.OwnedShips.Contains(remote.SelectedShip))
            {
                local.SelectedShip = remote.SelectedShip;
            }

            if (remoteDominant)
            {
                CopyUnhandledFields(local, remote, s_explicitProfileFields);
            }
        }

        /// <summary>Upgrades: the higher level of each stat (gems spent on both devices stay spent in the ledger).</summary>
        private static void MergeUpgrades(PlayerProfile local, PlayerProfile remote)
        {
            if (remote.Upgrades == null)
            {
                return;
            }

            for (int i = 0; i < local.Upgrades.Length && i < remote.Upgrades.Length; i++)
            {
                local.Upgrades[i] = Math.Max(local.Upgrades[i], remote.Upgrades[i]);
            }
        }

        private static void MergeFlags(ProfileFlags local, ProfileFlags remote, bool remoteDominant)
        {
            local.TutorialDone |= remote.TutorialDone;
            local.RatePrompted |= remote.RatePrompted;
            local.FirstLaunchUtcTicks = MinNonZero(local.FirstLaunchUtcTicks, remote.FirstLaunchUtcTicks);

            // ConsentAsked / PersonalizedAds / ad pacing / LastSeenVersion: device-local. AdsRemoved: merged earlier.
            if (remoteDominant)
            {
                CopyUnhandledFields(local, remote, s_explicitFlagFields);
            }
        }

        private static void MergeMissions(PlayerProfile local, PlayerProfile remote)
        {
            MissionState l = local.Missions;
            MissionState r = remote.Missions;
            int cmp = r.SetsCompleted.CompareTo(l.SetsCompleted);
            if (cmp == 0)
            {
                cmp = remote.PilotRank.CompareTo(local.PilotRank);
            }

            if (cmp > 0)
            {
                local.Missions = r;
                return;
            }

            if (cmp < 0 || !SameMissions(l.Active, r.Active))
            {
                return;
            }

            for (int i = 0; i < l.Active.Count; i++)
            {
                Mission lm = l.Active[i];
                Mission rm = r.Active[i];
                lm.Progress = Math.Max(lm.Progress, rm.Progress);
                lm.Completed |= rm.Completed;
            }
        }

        private static bool SameMissions(List<Mission> a, List<Mission> b)
        {
            if (a == null || b == null || a.Count != b.Count)
            {
                return false;
            }

            for (int i = 0; i < a.Count; i++)
            {
                if (a[i] == null || b[i] == null || a[i].Type != b[i].Type || a[i].Target != b[i].Target ||
                    a[i].Reward != b[i].Reward)
                {
                    return false;
                }
            }

            return true;
        }

        private static List<LeaderboardEntry> MergeLeaderboard(List<LeaderboardEntry> a, List<LeaderboardEntry> b)
        {
            var all = new List<LeaderboardEntry>();
            AddDistinct(all, a);
            AddDistinct(all, b);
            all.Sort(CompareEntries);
            if (all.Count > ProfileService.LeaderboardSize)
            {
                all.RemoveRange(ProfileService.LeaderboardSize, all.Count - ProfileService.LeaderboardSize);
            }

            return all;
        }

        private static void AddDistinct(List<LeaderboardEntry> into, List<LeaderboardEntry> from)
        {
            if (from == null)
            {
                return;
            }

            for (int i = 0; i < from.Count; i++)
            {
                LeaderboardEntry e = from[i];
                if (e == null)
                {
                    continue;
                }

                bool duplicate = false;
                for (int j = 0; j < into.Count && !duplicate; j++)
                {
                    duplicate = CompareEntries(into[j], e) == 0;
                }

                if (!duplicate)
                {
                    into.Add(e);
                }
            }
        }

        private static int CompareEntries(LeaderboardEntry x, LeaderboardEntry y)
        {
            int c = y.Score.CompareTo(x.Score);
            if (c != 0)
            {
                return c;
            }

            c = y.Distance.CompareTo(x.Distance);
            if (c != 0)
            {
                return c;
            }

            c = string.CompareOrdinal(y.Date ?? string.Empty, x.Date ?? string.Empty);
            if (c != 0)
            {
                return c;
            }

            return string.CompareOrdinal(x.Ship ?? string.Empty, y.Ship ?? string.Empty);
        }

        private static DailyRewardState MergeDailyRewards(DailyRewardState local, DailyRewardState remote)
        {
            int c = string.CompareOrdinal(remote.LastClaimDate ?? string.Empty, local.LastClaimDate ?? string.Empty);
            if (c > 0)
            {
                return remote;
            }

            if (c == 0)
            {
                local.Streak = Math.Max(local.Streak, remote.Streak);
            }

            return local;
        }

        private static void MergeDailyRun(DailyRunState local, DailyRunState remote)
        {
            if (local == null || remote == null)
            {
                return;
            }

            int bestStreak = Math.Max(local.BestStreak, remote.BestStreak);
            int daysPlayed = Math.Max(local.DaysPlayed, remote.DaysPlayed);
            int totalRuns = Math.Max(local.TotalRuns, remote.TotalRuns);
            long allTimeBest = Math.Max(local.AllTimeBestScore, remote.AllTimeBestScore);

            if (remote.Day > local.Day)
            {
                // Fields added to DailyRunState later belong to the newer day's record as well.
                CopyUnhandledFields(local, remote, s_explicitDailyRunFields);
                local.Day = remote.Day;
                local.Streak = remote.Streak;
                local.BestScore = remote.BestScore;
                local.BestDistance = remote.BestDistance;
                local.BestShip = remote.BestShip;
                local.Attempts = remote.Attempts;
                local.Ghost = remote.Ghost;
            }
            else if (remote.Day == local.Day)
            {
                local.Streak = Math.Max(local.Streak, remote.Streak);
                local.Attempts = Math.Max(local.Attempts, remote.Attempts);
                if (remote.BestScore > local.BestScore)
                {
                    local.BestScore = remote.BestScore;
                    local.BestDistance = remote.BestDistance;
                    local.BestShip = remote.BestShip;
                    local.Ghost = remote.Ghost;
                }
            }

            local.BestStreak = bestStreak;
            local.DaysPlayed = daysPlayed;
            local.TotalRuns = totalRuns;
            local.AllTimeBestScore = allTimeBest;
        }

        // ------------------------------------------------------------------ helpers

        private static void ApplyWalletBalance(PlayerProfile profile)
        {
            profile.Gems = Math.Max(0L, WalletLedger.Balance(profile.Wallet));
        }

        private static void UnionInto(List<string> target, List<string> source)
        {
            if (source == null)
            {
                return;
            }

            for (int i = 0; i < source.Count; i++)
            {
                string id = source[i];
                if (!string.IsNullOrEmpty(id) && !target.Contains(id))
                {
                    target.Add(id);
                }
            }
        }

        /// <summary>Every int/long/float/double public field of <paramref name="target"/> becomes the maximum of both.</summary>
        private static void MaxNumericFields<T>(T target, T source) where T : class
        {
            if (target == null || source == null)
            {
                return;
            }

            FieldInfo[] fields = typeof(T).GetFields(BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo f = fields[i];
                if (f.IsInitOnly || f.IsLiteral)
                {
                    continue;
                }

                Type t = f.FieldType;
                object a = f.GetValue(target);
                object b = f.GetValue(source);
                if (t == typeof(int))
                {
                    f.SetValue(target, Math.Max((int)a, (int)b));
                }
                else if (t == typeof(long))
                {
                    f.SetValue(target, Math.Max((long)a, (long)b));
                }
                else if (t == typeof(float))
                {
                    f.SetValue(target, MaxFinite((float)a, (float)b));
                }
                else if (t == typeof(double))
                {
                    f.SetValue(target, MaxFinite((double)a, (double)b));
                }
            }
        }

        private static float MaxFinite(float a, float b)
        {
            if (float.IsNaN(a) || float.IsInfinity(a))
            {
                a = 0f;
            }

            if (float.IsNaN(b) || float.IsInfinity(b))
            {
                b = 0f;
            }

            return Math.Max(a, b);
        }

        private static double MaxFinite(double a, double b)
        {
            if (double.IsNaN(a) || double.IsInfinity(a))
            {
                a = 0.0;
            }

            if (double.IsNaN(b) || double.IsInfinity(b))
            {
                b = 0.0;
            }

            return Math.Max(a, b);
        }

        private static long MinNonZero(long a, long b)
        {
            if (a <= 0)
            {
                return Math.Max(0L, b);
            }

            if (b <= 0)
            {
                return a;
            }

            return Math.Min(a, b);
        }
    }
}
