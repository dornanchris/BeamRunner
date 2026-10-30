using System;
using BeamRunner.Core.Balance;
using BeamRunner.Core.Field;
using BeamRunner.Core.Meta;
using BeamRunner.Core.Sim;
using NUnit.Framework;

namespace BeamRunner.Core.Tests
{
    /// <summary>Progression formulas, the upgrade shop and the adaptive-difficulty rating (design doc §§5–6).</summary>
    public class BalanceTests
    {
        private static readonly DateTime Noon = new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);

        [Test]
        public void UpgradeCost_IsC0TimesGrowthToTheN()
        {
            var b = new BalanceConfig();
            foreach (UpgradeId id in Enum.GetValues(typeof(UpgradeId)))
            {
                for (int n = 0; n < 10; n++)
                {
                    Assert.AreEqual(Math.Round(b.CostBase[(int)id] * Math.Pow(b.CostGrowth, n)), b.UpgradeCost(id, n), id + " level " + n);
                }
            }

            Assert.That(b.CostGrowth, Is.InRange(1.15f, 1.25f), "design range");
            Assert.AreEqual(ShipStats.UpgradeCount, Enum.GetValues(typeof(UpgradeId)).Length);
            Assert.AreEqual(ShipStats.UpgradeCount, b.MaxLevels.Length);
            Assert.AreEqual(ShipStats.UpgradeCount, b.CostBase.Length);
            CollectionAssert.AreEqual(new[] { 0, 0, 0, 2, 4 }, b.UnlockLevels, "tank, efficiency and start fuel from the start");
        }

        [Test]
        public void ShipWithoutUpgrades_IsTheBaseTuning()
        {
            var b = new BalanceConfig();
            ShipStats none = ShipStats.FromLevels(null, b);
            Assert.AreEqual(b.MaxFuel, none.MaxFuel, 1e-5f);
            Assert.AreEqual(b.StartFuel, none.StartFuel, 1e-5f);
            Assert.AreEqual(b.BridgeCost, none.BridgeCost, 1e-5f);
            Assert.AreEqual(b.RampUpCost, none.RampUpCost, 1e-5f);
            Assert.AreEqual(b.RampDownCost, none.RampDownCost, 1e-5f);
            Assert.AreEqual(1f, none.DurationScale, 1e-5f);
            Assert.AreEqual(b.HullBase, none.MaxHull);
            Assert.AreEqual(0, none.Power);
            Assert.AreEqual(none.BridgeCost, none.Cost(BuildKind.Bridge));
            Assert.AreEqual(none.RampUpCost, none.Cost(BuildKind.RampUp));
            Assert.AreEqual(none.RampDownCost, none.Cost(BuildKind.RampDown));
            Assert.AreEqual(0f, none.Cost(BuildKind.None));
            Assert.That(none.RampUpCost, Is.GreaterThan(none.BridgeCost), "climbing costs more than bridging");
            Assert.That(none.BridgeCost, Is.GreaterThan(none.RampDownCost), "dropping is the cheap build");
        }

        [Test]
        public void ShipFromLevels_AppliesEveryUpgrade_AndClampsLevels()
        {
            var b = new BalanceConfig();
            ShipStats some = ShipStats.FromLevels(new[] { 3, 5, 2, 4, 1 }, b);
            float scale = 1f - b.EfficiencyPerLevel * 5;
            Assert.AreEqual(b.MaxFuel + 3 * b.FuelTankPerLevel, some.MaxFuel, 1e-4f);
            Assert.AreEqual(b.BridgeCost * scale, some.BridgeCost, 1e-4f);
            Assert.AreEqual(b.RampUpCost * scale, some.RampUpCost, 1e-4f);
            Assert.AreEqual(b.RampDownCost * scale, some.RampDownCost, 1e-4f);
            Assert.AreEqual(b.StartFuel + 2 * b.StartFuelPerLevel, some.StartFuel, 1e-4f);
            Assert.AreEqual(1f + 4 * b.DurationPerLevel, some.DurationScale, 1e-4f);
            Assert.AreEqual(b.HullBase + 1, some.MaxHull);
            Assert.AreEqual(15, some.Power);

            ShipStats full = ShipStats.FromLevels(new[] { 999, 999, 999, 999, 999 }, b);
            ShipStats max = ShipStats.FromLevels(b.MaxLevels, b);
            Assert.AreEqual(max.MaxFuel, full.MaxFuel, 1e-4f, "levels clamp to their maxima");
            Assert.AreEqual(max.BridgeCost, full.BridgeCost, 1e-4f);
            Assert.AreEqual(max.Power, full.Power);
            Assert.That(full.MaxHull, Is.LessThanOrEqualTo(b.HullCap));
            Assert.That(full.BridgeCost, Is.GreaterThan(0f), "building never becomes free");
            Assert.That(full.StartFuel, Is.LessThanOrEqualTo(full.MaxFuel));

            ShipStats negative = ShipStats.FromLevels(new[] { -5, -1 }, b);
            Assert.AreEqual(ShipStats.FromLevels(null, b).MaxFuel, negative.MaxFuel, 1e-5f, "negative levels count as 0");
            Assert.AreEqual(0, negative.Power);

            var generous = new BalanceConfig { StartFuel = 18f, StartFuelPerLevel = 5f };
            Assert.AreEqual(generous.MaxFuel, ShipStats.FromLevels(new[] { 0, 0, 3 }, generous).StartFuel, 1e-5f,
                "the start fuel never overfills the tank");
        }

        [Test]
        public void Modifiers_MatchTheTables_AndInterpolate()
        {
            var b = new BalanceConfig();
            AssertTable(b, b.RatingSpeed, m => m.Speed, "speed");
            AssertTable(b, b.RatingGaps, m => m.Gaps, "gaps");
            AssertTable(b, b.RatingFuel, m => m.Fuel, "fuel");
            AssertTable(b, b.RatingObstacles, m => m.Obstacles, "obstacles");
            AssertTable(b, b.RatingDrops, m => m.Drops, "drops");
            AssertTable(b, b.RatingGhostLead, m => m.GhostLead, "ghost lead");
            AssertTable(b, b.RatingGems, m => m.Gems, "gems");

            DifficultyModifiers relaxed = AdaptiveDifficulty.For(-2f, b);
            DifficultyModifiers expert = AdaptiveDifficulty.For(2f, b);
            Assert.AreEqual(-2f, relaxed.Rating);
            Assert.AreEqual(2f, AdaptiveDifficulty.For(9f, b).Rating, "clamped");
            Assert.AreEqual(0f, AdaptiveDifficulty.For(float.NaN, b).Rating, "NaN is Standard");
            Assert.That(expert.Speed, Is.GreaterThan(relaxed.Speed));
            Assert.That(expert.Gaps, Is.GreaterThan(relaxed.Gaps));
            Assert.That(expert.Obstacles, Is.GreaterThan(relaxed.Obstacles));
            Assert.That(expert.Fuel, Is.LessThan(relaxed.Fuel), "experts get less fuel");
            Assert.That(expert.Drops, Is.LessThan(relaxed.Drops));
            Assert.That(expert.GhostLead, Is.LessThan(relaxed.GhostLead), "experts get less warning");
            Assert.That(expert.Gems, Is.GreaterThan(relaxed.Gems), "playing badly never pays better");

            DifficultyModifiers standard = DifficultyModifiers.Standard;
            Assert.AreEqual(1f, standard.Speed);
            Assert.AreEqual(1f, standard.Gaps);
            Assert.AreEqual(1f, standard.Fuel);
            Assert.AreEqual(1f, standard.Obstacles);
            Assert.AreEqual(1f, standard.Drops);
            Assert.AreEqual(1f, standard.GhostLead);
            Assert.AreEqual(1f, standard.Gems);
            Assert.AreEqual("difficulty.standard", AdaptiveDifficulty.LabelKey(0.2f));
            Assert.AreEqual("difficulty.expert", AdaptiveDifficulty.LabelKey(5f));
            Assert.AreEqual("difficulty.relaxed", AdaptiveDifficulty.LabelKey(-1.8f));
        }

        private static void AssertTable(BalanceConfig b, float[] table, Func<DifficultyModifiers, float> pick, string name)
        {
            Assert.AreEqual(5, table.Length, name);
            Assert.AreEqual(1f, table[2], 1e-5f, name + ": Standard is 1");
            for (int step = -2; step <= 2; step++)
            {
                Assert.AreEqual(table[step + 2], pick(AdaptiveDifficulty.For(step, b)), 1e-5f, name + " at " + step);
            }

            Assert.AreEqual((table[2] + table[3]) * 0.5f, pick(AdaptiveDifficulty.For(0.5f, b)), 1e-5f,
                name + ": +0.5 is halfway between Standard and Hard");
            Assert.AreEqual(table[4], pick(AdaptiveDifficulty.For(9f, b)), 1e-5f, name + ": clamped");
        }

        [Test]
        public void Rating_MovesAtMostOneStepPerRun_AndStaysInRange()
        {
            var b = new BalanceConfig();
            var s = new SkillState();
            for (int i = 0; i < 40; i++)
            {
                float before = s.Rating;
                AdaptiveDifficulty.ApplyRun(s, 1f, 120f, b);
                Assert.That(s.Rating - before, Is.LessThanOrEqualTo(b.RatingStep + 1e-5f));
            }

            Assert.AreEqual(2f, s.Rating, 1e-5f);
            Assert.That(s.Recent.Count, Is.EqualTo(b.RatingWindow));

            for (int i = 0; i < 60; i++)
            {
                AdaptiveDifficulty.ApplyRun(s, -1f, 120f, b);
            }

            Assert.AreEqual(-2f, s.Rating, 1e-5f);
        }

        [Test]
        public void Rating_RollingAverage_DampsOneLuckyRun()
        {
            var b = new BalanceConfig();
            var s = new SkillState();
            for (int i = 0; i < 4; i++)
            {
                AdaptiveDifficulty.ApplyRun(s, 0f, 120f, b);
            }

            float change = AdaptiveDifficulty.ApplyRun(s, 1f, 120f, b);
            Assert.AreEqual(b.RatingStep / b.RatingWindow, change, 1e-5f);
        }

        [Test]
        public void TwoEarlyDeathsInARow_DropTheRatingAnExtraNotch()
        {
            var b = new BalanceConfig();
            var s = new SkillState();
            AdaptiveDifficulty.ApplyRun(s, 0f, 5f, b);
            Assert.AreEqual(0f, s.Rating, 1e-5f, "one early death: nothing yet");
            AdaptiveDifficulty.ApplyRun(s, 0f, 6f, b);
            Assert.AreEqual(-b.RagePenalty, s.Rating, 1e-5f);
        }

        [Test]
        public void ExpectedLevel_GrowsWithUpgrades()
        {
            var b = new BalanceConfig();
            ShipStats none = ShipStats.FromLevels(null, b);
            ShipStats upgraded = ShipStats.FromLevels(new[] { 10, 10, 0, 0, 0 }, b);
            Assert.AreEqual(b.ExpectedLevelBase, AdaptiveDifficulty.ExpectedLevel(none, b), 1e-5f);
            Assert.AreEqual(b.ExpectedLevelBase, AdaptiveDifficulty.ExpectedLevel(null, b), 1e-5f, "no craft = no upgrades");
            Assert.AreEqual(b.ExpectedLevelBase + 20 * b.ExpectedLevelPerUpgrade, AdaptiveDifficulty.ExpectedLevel(upgraded, b), 1e-4f);
            Assert.That(AdaptiveDifficulty.ExpectedLevel(upgraded, b), Is.GreaterThan(AdaptiveDifficulty.ExpectedLevel(none, b)),
                "a better craft is expected to go deeper");
            var odd = new BalanceConfig { ExpectedLevelBase = 0f, ExpectedLevelPerUpgrade = 0f };
            Assert.That(AdaptiveDifficulty.ExpectedLevel(none, odd), Is.GreaterThanOrEqualTo(1f), "at least level 1");
        }

        [Test]
        public void Performance_RewardsGoingDeeperThanExpected_AndPenalisesDamageAndWaste()
        {
            var b = new BalanceConfig();
            ShipStats ship = ShipStats.FromLevels(null, b);
            float expected = AdaptiveDifficulty.ExpectedLevel(ship, b);
            var shallow = new RunResult { StartLevel = 1, LevelReached = 1, FuelSpent = 20f };
            var deep = new RunResult { StartLevel = 1, LevelReached = (int)expected + 1, FuelSpent = 40f };
            float pShallow = AdaptiveDifficulty.Performance(shallow, ship, b);
            float pDeep = AdaptiveDifficulty.Performance(deep, ship, b);
            Assert.That(pShallow, Is.LessThan(0f));
            Assert.That(pDeep, Is.GreaterThan(0f));
            Assert.That(pDeep, Is.InRange(-1f, 1f));
            Assert.AreEqual(0f, AdaptiveDifficulty.Performance(null, ship, b));

            RunResult hurt = deep.Clone();
            hurt.DamageTaken = 2;
            hurt.Falls = 2;
            Assert.That(AdaptiveDifficulty.Performance(hurt, ship, b), Is.LessThan(pDeep), "falls that cost hull count against it");

            RunResult revived = deep.Clone();
            revived.Revived = true;
            Assert.That(AdaptiveDifficulty.Performance(revived, ship, b), Is.LessThan(pDeep));

            RunResult wasteful = deep.Clone();
            wasteful.FuelWasted = 20f;
            Assert.That(AdaptiveDifficulty.Performance(wasteful, ship, b), Is.LessThan(pDeep), "fuel spent on rails that were there");

            RunResult further = deep.Clone();
            further.LevelProgress = 0.8f;
            Assert.That(AdaptiveDifficulty.Performance(further, ship, b), Is.GreaterThan(pDeep), "progress in the last level counts");

            // A run started at a later level must beat its start, not the base expectation.
            var lateStart = new RunResult { StartLevel = 10, LevelReached = 10 };
            Assert.That(AdaptiveDifficulty.Performance(lateStart, ship, b), Is.LessThan(0f));

            // The same depth is worth less for a craft that is expected to go deeper.
            ShipStats strong = ShipStats.FromLevels(new[] { 10, 10, 10, 10, 2 }, b);
            Assert.That(AdaptiveDifficulty.Performance(deep, strong, b), Is.LessThan(pDeep));
        }

        [Test]
        public void Shop_BuysWithGems_RespectsLocksAndMaxima()
        {
            PlayerProfile p = PlayerProfile.CreateNew();
            var service = new ProfileService(p, null, 1);
            Assert.AreEqual(UpgradePurchaseResult.NotEnoughGems, service.BuyUpgrade(UpgradeId.FuelTank));
            p.Gems = 100000;
            long cost = service.UpgradeCost(UpgradeId.FuelTank);
            Assert.AreEqual(UpgradePurchaseResult.Purchased, service.BuyUpgrade(UpgradeId.FuelTank));
            Assert.AreEqual(1, service.UpgradeLevel(UpgradeId.FuelTank));
            Assert.AreEqual(100000 - cost, p.Gems);
            Assert.That(service.UpgradeCost(UpgradeId.FuelTank), Is.GreaterThan(cost));
            Assert.That(service.Ship.MaxFuel, Is.GreaterThan(ShipStats.FromLevels(null).MaxFuel));
            Assert.IsTrue(service.UpgradeUnlocked(UpgradeId.Efficiency));
            Assert.IsTrue(service.UpgradeUnlocked(UpgradeId.StartFuel));

            Assert.AreEqual(2, service.UpgradeUnlockLevel(UpgradeId.Duration));
            Assert.AreEqual(4, service.UpgradeUnlockLevel(UpgradeId.Hull));
            p.Stats.BestLevel = 1;
            Assert.AreEqual(UpgradePurchaseResult.Locked, service.BuyUpgrade(UpgradeId.Duration), "Duration waits for level 2");
            Assert.AreEqual(UpgradePurchaseResult.Locked, service.BuyUpgrade(UpgradeId.Hull), "Hull waits for level 4");
            Assert.IsFalse(service.CanBuyUpgrade(UpgradeId.Hull));

            p.Stats.BestLevel = 2;
            Assert.AreEqual(UpgradePurchaseResult.Purchased, service.BuyUpgrade(UpgradeId.Duration));
            Assert.AreEqual(UpgradePurchaseResult.Locked, service.BuyUpgrade(UpgradeId.Hull));

            p.Stats.BestLevel = 4;
            int hullLevels = service.UpgradeMaxLevel(UpgradeId.Hull);
            for (int i = 0; i < hullLevels; i++)
            {
                Assert.AreEqual(UpgradePurchaseResult.Purchased, service.BuyUpgrade(UpgradeId.Hull));
            }

            Assert.AreEqual(UpgradePurchaseResult.Maxed, service.BuyUpgrade(UpgradeId.Hull));
            Assert.AreEqual(0, service.UpgradeCost(UpgradeId.Hull), "maxed costs nothing");
            ShipStats next = service.CreateRunConfig(1).Ship;
            Assert.AreEqual(ShipStats.FromLevels(null).MaxHull + hullLevels, next.MaxHull, "the next run uses the upgrades");
            Assert.That(next.DurationScale, Is.GreaterThan(1f));
            Assert.AreEqual(service.Ship.MaxFuel, next.MaxFuel, 1e-5f);
        }

        [Test]
        public void CheapestUpgrade_SkipsLockedOnes()
        {
            PlayerProfile p = PlayerProfile.CreateNew();
            var service = new ProfileService(p, null, 1);
            UpgradeId? cheapest = service.CheapestUpgrade();
            Assert.IsNotNull(cheapest);
            Assert.IsTrue(service.UpgradeUnlocked(cheapest.Value));
            for (int i = 0; i < ShipStats.UpgradeCount; i++)
            {
                var id = (UpgradeId)i;
                if (service.UpgradeUnlocked(id))
                {
                    Assert.That(service.UpgradeCost(cheapest.Value), Is.LessThanOrEqualTo(service.UpgradeCost(id)), id.ToString());
                }
            }

            Assert.AreEqual(0f, service.NextUpgradeProgress01(), "an empty wallet");
            p.Gems = 1000000;
            Assert.AreEqual(1f, service.NextUpgradeProgress01());
        }

        [Test]
        public void FreeUpgradeToken_OncePerUtcDay()
        {
            PlayerProfile p = PlayerProfile.CreateNew();
            var service = new ProfileService(p, null, 1);
            Assert.IsTrue(service.FreeUpgradeAvailable(Noon));
            Assert.IsFalse(service.GrantFreeUpgrade(UpgradeId.Hull, Noon), "not for a locked upgrade");
            Assert.IsTrue(service.FreeUpgradeAvailable(Noon), "a refused grant keeps the token");
            Assert.IsTrue(service.GrantFreeUpgrade(UpgradeId.Efficiency, Noon));
            Assert.IsFalse(service.FreeUpgradeAvailable(Noon.AddHours(5)));
            Assert.IsFalse(service.GrantFreeUpgrade(UpgradeId.Efficiency, Noon.AddHours(5)));
            Assert.IsTrue(service.FreeUpgradeAvailable(Noon.AddDays(1)));
            Assert.AreEqual(1, service.UpgradeLevel(UpgradeId.Efficiency));
            Assert.AreEqual(0, p.Gems, "free means free");
        }

        [Test]
        public void ApplyRun_MovesTheRating_ButNotForTheTutorialOrDailyRun()
        {
            PlayerProfile p = PlayerProfile.CreateNew();
            var service = new ProfileService(p, null, 1);
            RunConfig tutorial = service.CreateRunConfig(1);
            Assert.IsTrue(tutorial.Tutorial);
            Assert.IsFalse(tutorial.Adaptive);
            service.ApplyRun(new RunResult { Duration = 5f, Adaptive = false, StartLevel = 1, LevelReached = 1 });
            Assert.AreEqual(0f, p.Skill.Rating);

            p.Flags.TutorialDone = true;
            Assert.IsTrue(service.CreateRunConfig(2).Adaptive);
            for (int i = 0; i < 3; i++)
            {
                service.ApplyRun(new RunResult
                {
                    Duration = 200f,
                    Adaptive = true,
                    StartLevel = 1,
                    LevelReached = 30,
                    Builds = 50,
                    Bridges = 50,
                    GapsBridged = 50,
                    FuelSpent = 100f,
                });
            }

            Assert.That(p.Skill.Rating, Is.GreaterThan(0f));
            Assert.AreEqual(p.Skill.Rating, service.CreateRunConfig(3).Modifiers.Rating, 1e-5f);
        }

        [Test]
        public void UpgradesSurviveASaveRoundTrip_AndBadLevelsAreClamped()
        {
            PlayerProfile p = PlayerProfile.CreateNew();
            p.Upgrades = new[] { 3, 999, -4 };
            p.Sanitize();
            Assert.AreEqual(ShipStats.UpgradeCount, p.Upgrades.Length);
            Assert.AreEqual(3, p.Upgrades[0]);
            Assert.AreEqual(BalanceConfig.Current.MaxLevel(UpgradeId.Efficiency), p.Upgrades[1]);
            Assert.AreEqual(0, p.Upgrades[2]);
            Assert.AreEqual(0, p.Upgrades[(int)UpgradeId.Hull]);
        }

        [Test]
        public void SanitizedConfig_RepairsABrokenFile()
        {
            var d = new BalanceConfig();
            var broken = new BalanceConfig
            {
                BridgeCost = float.NaN,
                RampUpCost = 999f,
                MaxFuel = -5f,
                StartFuel = 500f,
                LateBuildSeconds = 5f,
                HullBase = 0,
                HullCap = 100,
                EfficiencyPerLevel = 0.5f,
                CostGrowth = 99f,
                PowerUpIntervalMin = 10f,
                PowerUpIntervalMax = 5f,
                MaxLevels = new[] { 5 },
                UnlockLevels = null,
                RatingGaps = null,
                RatingFuel = new[] { float.NaN, -1f, 0.5f },
                ExpectedLevelBase = 0f,
            };
            BalanceConfig c = broken.Sanitized();
            Assert.AreEqual(d.BridgeCost, c.BridgeCost, "NaN falls back to the default");
            Assert.AreEqual(50f, c.RampUpCost);
            Assert.AreEqual(d.MaxFuel, c.MaxFuel, "a tank must hold fuel");
            Assert.AreEqual(c.MaxFuel, c.StartFuel, "the start fuel fits the tank");
            Assert.AreEqual(0.3f, c.LateBuildSeconds, 1e-6f);
            Assert.AreEqual(1, c.HullBase, "at least one hull point");
            Assert.AreEqual(30, c.HullCap);
            Assert.That(c.EfficiencyPerLevel * c.MaxLevel(UpgradeId.Efficiency), Is.LessThan(1f), "building never becomes free");
            Assert.AreEqual(3f, c.CostGrowth);
            Assert.AreEqual(c.PowerUpIntervalMin, c.PowerUpIntervalMax, "max is at least min");
            Assert.AreEqual(ShipStats.UpgradeCount, c.MaxLevels.Length);
            Assert.AreEqual(5, c.MaxLevels[0]);
            Assert.AreEqual(d.MaxLevels[1], c.MaxLevels[1]);
            CollectionAssert.AreEqual(d.UnlockLevels, c.UnlockLevels);
            CollectionAssert.AreEqual(d.RatingGaps, c.RatingGaps);
            Assert.AreEqual(5, c.RatingFuel.Length);
            Assert.AreEqual(d.RatingFuel[0], c.RatingFuel[0], "NaN entry");
            Assert.AreEqual(d.RatingFuel[1], c.RatingFuel[1], "negative entry");
            Assert.AreEqual(0.5f, c.RatingFuel[2]);
            Assert.AreEqual(d.RatingFuel[3], c.RatingFuel[3], "missing entry");
            Assert.That(c.ExpectedLevelBase, Is.GreaterThanOrEqualTo(1f));
            Assert.IsTrue(float.IsNaN(broken.BridgeCost), "Sanitized returns a copy");

            BalanceConfig defaults = d.Sanitized();
            Assert.AreEqual(d.StartFuel, defaults.StartFuel, "the shipped tuning is already sane");
            Assert.AreEqual(d.LateBuildSeconds, defaults.LateBuildSeconds);
            CollectionAssert.AreEqual(d.RatingSpeed, defaults.RatingSpeed);
        }

        [Test]
        public void CurrentConfig_IsNeverNull_AndAlwaysSanitized()
        {
            BalanceConfig before = BalanceConfig.Current;
            try
            {
                BalanceConfig.Current = null;
                Assert.IsNotNull(BalanceConfig.Current);
                BalanceConfig.Current = new BalanceConfig { CostGrowth = 99f };
                Assert.AreEqual(3f, BalanceConfig.Current.CostGrowth);
            }
            finally
            {
                BalanceConfig.Current = before;
            }
        }
    }
}
