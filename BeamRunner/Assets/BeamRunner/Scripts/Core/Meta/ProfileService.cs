using System;
using System.Collections.Generic;
using BeamRunner.Core.Sim;

namespace BeamRunner.Core.Meta
{
    /// <summary>Outcome of trying to buy or unlock a ship.</summary>
    public enum ShipPurchaseResult
    {
        Purchased = 0,
        AlreadyOwned,
        NotEnoughGems,
        Locked,
        UnknownShip,
    }

    /// <summary>Everything that changed because of a finished run — drives the results screen.</summary>
    public sealed class RunSummary
    {
        public RunResult Result;
        public bool NewBestScore;
        public bool NewBestDistance;

        /// <summary>The run reached a level never reached before (not set on the first run).</summary>
        public bool NewBestLevel;
        public long PreviousBestScore;

        /// <summary>Gems picked up in the run.</summary>
        public int GemsCollected;

        public readonly List<Mission> MissionsCompleted = new List<Mission>();
        public int MissionRewards;
        public readonly List<AchievementDefinition> AchievementsUnlocked = new List<AchievementDefinition>();
        public int AchievementRewards;
        public bool RankUp;
        public int NewRank;
        public int RankUpReward;
        public readonly List<ShipDefinition> ShipsUnlocked = new List<ShipDefinition>();

        /// <summary>
        /// Ships whose requirement this run (or its Daily Run) met for the first time and that still have to be bought
        /// in the hangar for their <see cref="ShipDefinition.Price"/>.
        /// </summary>
        public readonly List<ShipDefinition> ShipsAvailable = new List<ShipDefinition>();

        /// <summary>0-based position in the local leaderboard, or −1 if it didn't place.</summary>
        public int LeaderboardPosition = -1;

        /// <summary>Total gems added to the wallet by this run (pickups + rewards).</summary>
        public long TotalGemsEarned;

        /// <summary>Set once the player doubled the pickup gems via a rewarded ad.</summary>
        public bool GemsDoubled;

        /// <summary>How much the hidden rating moved after this run (for debug overlays and tests only).</summary>
        public float RatingChange;

        /// <summary>Daily Run result of this run (null for normal runs); set by <see cref="ProfileService.ApplyDailyRun"/>.</summary>
        public DailyRunOutcome Daily;
    }

    /// <summary>
    /// All meta-game rules: wallet, hangar, missions, achievements, pilot rank, daily rewards, leaderboard,
    /// entitlements. Owns the <see cref="PlayerProfile"/>; the Unity layer persists it when <see cref="Changed"/>
    /// fires (or at safe points).
    /// </summary>
    public sealed partial class ProfileService
    {
        public const int LeaderboardSize = 10;
        public const int ReviveGemCost = 250;
        public const float ScoreBonusPerRank = 0.04f;
        public const int RankUpBaseReward = 250;

        private readonly Func<DateTime> _clock;
        private readonly Rng _rng;

        public ProfileService(PlayerProfile profile, Func<DateTime> localClock = null, int seed = 0)
        {
            Profile = profile ?? PlayerProfile.CreateNew();
            Profile.Sanitize();
            _clock = localClock ?? (() => DateTime.Now);
            _rng = new Rng(seed != 0 ? seed : unchecked((int)DateTime.UtcNow.Ticks)); // not TickCount (boot-time clock)
            MissionSystem.EnsureMissions(Profile.Missions, Profile.PilotRank, _rng);
            if (Profile.Flags.FirstLaunchUtcTicks == 0)
            {
                Profile.Flags.FirstLaunchUtcTicks = DateTime.UtcNow.Ticks;
            }
        }

        public PlayerProfile Profile { get; private set; }

        /// <summary>Raised after any change that should be saved / reflected in UI.</summary>
        public event Action Changed;

        /// <summary>Raised when gems are added (amount, reason) — for UI count-up and analytics.</summary>
        public event Action<long, string> GemsAdded;

        /// <summary>Achievements the last successful <see cref="BuyShip"/> unlocked (their rewards are already paid).</summary>
        public readonly List<AchievementDefinition> LastPurchaseAchievements = new List<AchievementDefinition>();

        public long Gems
        {
            get { return Profile.Gems; }
        }

        public int PilotRank
        {
            get { return Profile.PilotRank; }
        }

        public float ScoreBonus
        {
            get { return (Profile.PilotRank - 1) * ScoreBonusPerRank; }
        }

        public ShipDefinition SelectedShip
        {
            get { return ShipCatalog.Get(Profile.SelectedShip); }
        }

        public PlayerSettings Settings
        {
            get { return Profile.Settings; }
        }

        public void NotifyChanged()
        {
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------ wallet

        public void AddGems(long amount, string reason)
        {
            if (amount <= 0)
            {
                return;
            }

            Profile.Gems += amount;
            GemsAdded?.Invoke(amount, reason);
            Changed?.Invoke();
        }

        public bool TrySpend(long amount)
        {
            if (amount < 0 || Profile.Gems < amount)
            {
                return false;
            }

            Profile.Gems -= amount;
            Changed?.Invoke();
            return true;
        }

        // ------------------------------------------------------------------ hangar

        public bool Owns(string shipId)
        {
            return Profile.OwnedShips.Contains(shipId);
        }

        /// <summary>True if the ship's requirement (rank, distance, score, Daily Run days, level) is met, or it has none.</summary>
        public bool RequirementMet(ShipDefinition ship)
        {
            return RequirementCurrent(ship) >= ship.UnlockValue;
        }

        /// <summary>
        /// The player's current value for the ship's requirement: pilot rank, best distance (whole metres), best score,
        /// Daily Run days flown or best level reached. 0 for ships without a requirement.
        /// </summary>
        public long RequirementCurrent(ShipDefinition ship)
        {
            switch (ship.Unlock)
            {
                case UnlockKind.Rank:
                    return Profile.PilotRank;
                case UnlockKind.Distance:
                    return (long)Profile.Stats.BestDistance;
                case UnlockKind.Score:
                    return Profile.Stats.BestScore;
                case UnlockKind.DailyRunDays:
                    return Profile.DailyRun != null ? Profile.DailyRun.DaysPlayed : 0;
                case UnlockKind.Level:
                    return Math.Max(1, Profile.Stats.BestLevel); // every pilot has reached level 1 ("LEVEL 1 / 4")
                default:
                    return 0;
            }
        }

        /// <summary>0..1 progress toward the ship's requirement (1 for ships without one).</summary>
        public float UnlockProgress(ShipDefinition ship)
        {
            return ship.UnlockValue <= 0 ? 1f : MathUtil.Clamp01((float)((double)RequirementCurrent(ship) / ship.UnlockValue));
        }

        /// <summary>Not owned, requirement met and the price affordable: <see cref="BuyShip"/> would succeed now.</summary>
        public bool CanUnlockNow(ShipDefinition ship)
        {
            return !Owns(ship.Id) && RequirementMet(ship) && Profile.Gems >= ship.Price;
        }

        public ShipPurchaseResult BuyShip(string shipId)
        {
            if (!ShipCatalog.Exists(shipId))
            {
                return ShipPurchaseResult.UnknownShip;
            }

            if (Owns(shipId))
            {
                return ShipPurchaseResult.AlreadyOwned;
            }

            ShipDefinition ship = ShipCatalog.Get(shipId);
            if (!RequirementMet(ship))
            {
                return ShipPurchaseResult.Locked;
            }

            if (ship.Price > 0 && !TrySpend(ship.Price))
            {
                return ShipPurchaseResult.NotEnoughGems;
            }

            Profile.OwnedShips.Add(shipId);
            GrantPurchaseAchievements();
            Changed?.Invoke();
            return ShipPurchaseResult.Purchased;
        }

        /// <summary>
        /// Evaluate marks newly met achievements as unlocked and only reports them: pay their rewards here (ApplyRun
        /// skips anything already marked, so a fleet achievement unlocked by a purchase was never paid before).
        /// </summary>
        private void GrantPurchaseAchievements()
        {
            LastPurchaseAchievements.Clear();
            List<AchievementDefinition> unlocked = AchievementCatalog.Evaluate(Profile);
            long reward = 0;
            for (int i = 0; i < unlocked.Count; i++)
            {
                LastPurchaseAchievements.Add(unlocked[i]);
                reward += unlocked[i].Reward;
            }

            if (reward > 0)
            {
                Profile.Gems += reward;
                GemsAdded?.Invoke(reward, "achievement");
            }
        }

        public bool SelectShip(string shipId)
        {
            if (!Owns(shipId))
            {
                return false;
            }

            Profile.SelectedShip = shipId;
            Changed?.Invoke();
            return true;
        }

        // ------------------------------------------------------------------ runs

        /// <summary>Configuration for the next run.</summary>
        public RunConfig CreateRunConfig(int seed)
        {
            bool tutorial = !Profile.Flags.TutorialDone;
            return new RunConfig
            {
                Seed = seed,
                Tutorial = tutorial,
                ScoreBonus = ScoreBonus,
                MaxRevives = 1,
                StartLevel = tutorial ? 1 : StartLevel,
                Ship = Ship,

                // The first run is played Easy and does not move the rating; after that the hidden rating applies.
                Modifiers = tutorial ? Balance.AdaptiveDifficulty.For(-1f) : NextRunModifiers,
                Adaptive = !tutorial,
            };
        }

        /// <summary>Highest level a run can start at: the best level reached so far (at least 1).</summary>
        public int MaxStartLevel
        {
            get
            {
                int reached = Math.Max(1, Profile.Stats.BestLevel);
                return AllStartLevelsUnlocked ? Math.Max(reached, DevStartLevels) : reached;
            }
        }

        /// <summary>
        /// Development convenience (set by the game in the Editor and development builds only): every level of the
        /// progression and one plateau cycle can be picked as the start level without reaching it first.
        /// </summary>
        public bool AllStartLevelsUnlocked { get; set; }

        private static int DevStartLevels
        {
            get { return Field.Levels.PlateauLevel + Field.Levels.PlateauCycle.Length - 1; }
        }

        /// <summary>Level the next normal run starts at: the player's choice, clamped to <see cref="MaxStartLevel"/>.</summary>
        public int StartLevel
        {
            get { return MathUtil.Clamp(Profile.Settings.StartLevel, 1, MaxStartLevel); }
        }

        /// <summary>Chooses the level the next normal runs start at (clamped to the levels reached).</summary>
        public void SetStartLevel(int level)
        {
            int clamped = MathUtil.Clamp(level, 1, MaxStartLevel);
            if (clamped == Profile.Settings.StartLevel)
            {
                return;
            }

            Profile.Settings.StartLevel = clamped;
            Changed?.Invoke();
        }

        /// <summary>Applies a finished run to stats, missions, achievements, rank, leaderboard and wallet.</summary>
        public RunSummary ApplyRun(RunResult r)
        {
            var summary = new RunSummary { Result = r, GemsCollected = r.Gems };
            PlayerStats s = Profile.Stats;
            List<ShipDefinition> gatedBefore = GatedShips();

            summary.PreviousBestScore = s.BestScore;
            summary.NewBestScore = r.Score > s.BestScore && r.Score > 0;
            summary.NewBestDistance = r.Distance > s.BestDistance && r.Distance > 0f;

            s.Runs++;
            s.BestScore = Math.Max(s.BestScore, r.Score);
            s.BestDistance = Math.Max(s.BestDistance, r.Distance);
            s.BestNearMissesRun = Math.Max(s.BestNearMissesRun, r.NearMisses);
            s.BestNearMissChain = Math.Max(s.BestNearMissChain, r.BestNearMissChain);
            s.BestMultiplier = Math.Max(s.BestMultiplier, r.MaxMultiplier);
            summary.NewBestLevel = r.LevelReached > s.BestLevel && s.Runs > 1;
            s.BestLevel = Math.Max(s.BestLevel, r.LevelReached);
            s.BestGemsRun = Math.Max(s.BestGemsRun, r.Gems);
            s.TotalDistance += r.Distance;
            s.TotalGems += r.Gems;
            s.TotalNearMisses += r.NearMisses;
            s.TotalGapsBridged += r.GapsBridged;
            s.TotalCubesDodged += r.CubesDodged;
            s.TotalLevelsCleared += r.LevelsCleared;
            s.TotalSmashes += r.Smashed;
            s.TotalOverloads += r.Overloads;
            s.TotalPowerUps += r.PowerUps;
            s.TotalBuilds += r.Builds;
            s.BestBuildsRun = Math.Max(s.BestBuildsRun, r.Builds);
            summary.RatingChange = ApplySkill(r, true);
            s.TotalPlaySeconds += r.Duration;
            if (r.Revived)
            {
                s.Revives++;
            }

            // A completed first run (any length past the warm-up) ends the tutorial.
            if (r.Distance > 300f || s.Runs >= 2)
            {
                Profile.Flags.TutorialDone = true;
            }

            long earned = r.Gems;

            // Missions (and rank-ups when a full set is done).
            List<Mission> done = MissionSystem.ApplyRun(Profile.Missions, r);
            for (int i = 0; i < done.Count; i++)
            {
                summary.MissionsCompleted.Add(done[i]);
                summary.MissionRewards += done[i].Reward;
            }

            earned += summary.MissionRewards;
            if (MissionSystem.AllCompleted(Profile.Missions))
            {
                Profile.PilotRank++;
                Profile.Missions.SetsCompleted++;
                Profile.Missions.Active.Clear();
                MissionSystem.EnsureMissions(Profile.Missions, Profile.PilotRank, _rng);
                summary.RankUp = true;
                summary.NewRank = Profile.PilotRank;
                summary.RankUpReward = RankUpBaseReward + 50 * (Profile.PilotRank - 2);
                earned += summary.RankUpReward;
            }

            // Requirements met by this run: free ones unlock, priced ones become buyable.
            ReportMetRequirements(gatedBefore, summary);

            // Achievements (after stats, rank and ships are updated).
            List<AchievementDefinition> unlocked = AchievementCatalog.Evaluate(Profile);
            for (int i = 0; i < unlocked.Count; i++)
            {
                summary.AchievementsUnlocked.Add(unlocked[i]);
                summary.AchievementRewards += unlocked[i].Reward;
            }

            earned += summary.AchievementRewards;
            summary.LeaderboardPosition = InsertLeaderboard(r);

            Profile.Gems += earned;
            summary.TotalGemsEarned = earned;
            if (earned > 0)
            {
                GemsAdded?.Invoke(earned, "run");
            }

            Profile.Flags.GameOversSinceInterstitial++;
            Changed?.Invoke();
            return summary;
        }

        /// <summary>Rewarded-ad bonus on the results screen: grants the run's pickup gems again.</summary>
        public long DoubleRunGems(RunSummary summary)
        {
            if (summary == null || summary.GemsDoubled || summary.GemsCollected <= 0)
            {
                return 0;
            }

            summary.GemsDoubled = true;
            summary.TotalGemsEarned += summary.GemsCollected;
            AddGems(summary.GemsCollected, "double");
            return summary.GemsCollected;
        }

        /// <summary>Missions that the live run would complete (for mid-run toasts). Allocation-free when empty.</summary>
        public int CountLiveMissionCompletions(RunResult live, bool[] alreadyAnnounced)
        {
            int count = 0;
            List<Mission> active = Profile.Missions.Active;
            for (int i = 0; i < active.Count && i < alreadyAnnounced.Length; i++)
            {
                if (!alreadyAnnounced[i] && MissionSystem.WouldComplete(active[i], live))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>Ships not owned whose requirement is not met yet (snapshot before a run changes stats).</summary>
        private List<ShipDefinition> GatedShips()
        {
            var gated = new List<ShipDefinition>();
            IReadOnlyList<ShipDefinition> ships = ShipCatalog.All;
            for (int i = 0; i < ships.Count; i++)
            {
                ShipDefinition ship = ships[i];
                if (ship.HasRequirement && !Owns(ship.Id) && !RequirementMet(ship))
                {
                    gated.Add(ship);
                }
            }

            return gated;
        }

        /// <summary>
        /// For each ship of <paramref name="gatedBefore"/> whose requirement is met now: a free one is added to the
        /// hangar (<see cref="RunSummary.ShipsUnlocked"/>), a priced one is reported as <see cref="RunSummary.ShipsAvailable"/>.
        /// </summary>
        private void ReportMetRequirements(List<ShipDefinition> gatedBefore, RunSummary summary)
        {
            for (int i = 0; i < gatedBefore.Count; i++)
            {
                ShipDefinition ship = gatedBefore[i];
                if (Owns(ship.Id) || !RequirementMet(ship))
                {
                    continue;
                }

                if (ship.Price > 0)
                {
                    if (summary != null && !summary.ShipsAvailable.Contains(ship))
                    {
                        summary.ShipsAvailable.Add(ship);
                    }
                }
                else
                {
                    Profile.OwnedShips.Add(ship.Id);
                    if (summary != null)
                    {
                        summary.ShipsUnlocked.Add(ship);
                    }
                }
            }
        }

        private int InsertLeaderboard(RunResult r)
        {
            if (r.Score <= 0)
            {
                return -1;
            }

            List<LeaderboardEntry> board = Profile.Leaderboard;
            int position = board.Count;
            for (int i = 0; i < board.Count; i++)
            {
                if (r.Score > board[i].Score)
                {
                    position = i;
                    break;
                }
            }

            if (position >= LeaderboardSize)
            {
                return -1;
            }

            board.Insert(position, new LeaderboardEntry
            {
                Score = r.Score,
                Distance = r.Distance,
                Date = DailyRewards.DateKey(_clock()),
                Ship = Profile.SelectedShip,
                Level = r.LevelReached,
            });

            if (board.Count > LeaderboardSize)
            {
                board.RemoveRange(LeaderboardSize, board.Count - LeaderboardSize);
            }

            return position;
        }

        // ------------------------------------------------------------------ daily rewards

        public bool DailyRewardAvailable
        {
            get { return DailyRewards.CanClaim(Profile.Daily, _clock()); }
        }

        public int DailyRewardDayIndex
        {
            get { return DailyRewards.NextDayIndex(Profile.Daily, _clock()); }
        }

        /// <summary>Claims today's reward; returns the amount granted (0 if already claimed).</summary>
        public int ClaimDailyReward()
        {
            int amount = DailyRewards.Claim(Profile.Daily, _clock());
            if (amount > 0)
            {
                AddGems(amount, "daily");
            }

            return amount;
        }

        // ------------------------------------------------------------------ monetisation

        public bool AdsRemoved
        {
            get { return Profile.Flags.AdsRemoved; }
        }

        /// <summary>
        /// Grants an IAP entitlement. Idempotent for non-consumables; <paramref name="gems"/> is added for
        /// consumable packs (the store layer dedupes transactions).
        /// </summary>
        public void GrantEntitlement(string productId, bool removesAds, long gems)
        {
            bool changed = false;
            if (removesAds && !Profile.Flags.AdsRemoved)
            {
                Profile.Flags.AdsRemoved = true;
                changed = true;
            }

            if (gems > 0)
            {
                Profile.Gems += gems;
                GemsAdded?.Invoke(gems, "iap:" + productId);
                changed = true;
            }

            if (changed)
            {
                Changed?.Invoke();
            }
        }

        /// <summary>
        /// Whether an interstitial may be shown at this game over: never during the first runs, only every
        /// third game over, at least two minutes apart, and never once ads are removed.
        /// </summary>
        public bool ShouldShowInterstitial(DateTime utcNow)
        {
            ProfileFlags f = Profile.Flags;
            if (f.AdsRemoved || Profile.Stats.Runs < 4 || f.GameOversSinceInterstitial < 3)
            {
                return false;
            }

            return utcNow.Ticks - f.LastInterstitialUtcTicks >= TimeSpan.FromSeconds(120).Ticks;
        }

        public void MarkInterstitialShown(DateTime utcNow)
        {
            Profile.Flags.GameOversSinceInterstitial = 0;
            Profile.Flags.LastInterstitialUtcTicks = utcNow.Ticks;
            Changed?.Invoke();
        }

        public void SetConsent(bool personalized)
        {
            Profile.Flags.ConsentAsked = true;
            Profile.Flags.PersonalizedAds = personalized;
            Changed?.Invoke();
        }

        /// <summary>Wipes all progress but keeps purchases (Remove Ads) and settings.</summary>
        public void ResetProgress()
        {
            bool adsRemoved = Profile.Flags.AdsRemoved;
            bool consentAsked = Profile.Flags.ConsentAsked;
            bool personalized = Profile.Flags.PersonalizedAds;
            PlayerSettings settings = Profile.Settings;
            WalletLedger wallet = Profile.Wallet;
            CloudSyncState sync = Profile.Sync;
            Profile = PlayerProfile.CreateNew();
            Profile.Settings = settings;

            // Cloud save: the gem ledger survives (the next reconcile books the wiped balance as spent on this device,
            // so a merge cannot bring the gems back) and the epoch goes up, so older snapshots on other devices or in
            // the cloud cannot undo the reset (ProfileMerge).
            if (wallet != null)
            {
                Profile.Wallet = wallet;
                WalletLedger.Reconcile(Profile);
            }

            if (sync != null)
            {
                sync.Epoch++;
                Profile.Sync = sync;
            }

            Profile.Flags.AdsRemoved = adsRemoved;
            Profile.Flags.ConsentAsked = consentAsked;
            Profile.Flags.PersonalizedAds = personalized;
            Profile.Flags.TutorialDone = true;
            MissionSystem.EnsureMissions(Profile.Missions, Profile.PilotRank, _rng);
            Changed?.Invoke();
        }
    }
}
