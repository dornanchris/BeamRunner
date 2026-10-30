using System;
using System.Collections;
using BeamRunner.Core.Audio;
using BeamRunner.Core.Localization;
using BeamRunner.Core.Meta;
using BeamRunner.Core.Sim;
using BeamRunner.Game;
using BeamRunner.Services;
using UnityEngine;
using UnityEngine.UI;

namespace BeamRunner.UI
{
    /// <summary>
    /// Game-over results: animated "RUN COMPLETE" or celebratory "NEW RECORD!", score count-up, best and distance,
    /// leaderboard placement, gem breakdown with a once-per-run DOUBLE (rewarded ad), rank-up and ship-unlock
    /// banners, completed missions, unlocked achievements and the run's stats grid — revealed as a timed sequence with
    /// sounds. RETRY / MENU stay pinned at the bottom (→ <see cref="GameController.ContinueFromResults"/>); the rest
    /// scrolls. (The rating prompt lives on the menu, every third launch: <c>GameUI.TryReviewPrompt</c>.)
    /// </summary>
    public sealed partial class ResultsScreen : UIScreen
    {
        private const int ConfettiCount = 18;

        private static readonly string[] StatKeys =
        {
            "results.stat_level", "results.stat_bridged", "results.stat_builds", "results.stat_near_misses",
            "results.stat_best_chain", "results.stat_max_multiplier", "results.stat_smashes", "results.stat_time",
        };

        private GameController _game;
        private RunSummary _summary;
        private bool _leaving;
        private bool _doubling;
        private float _adPoll;

        private ScrollList _list;
        private RectTransform _fxLayer;
        private NeonButton _retry;
        private NeonButton _menu;
        private CanvasGroup _buttonsGroup;

        // Header.
        private RectTransform _header;
        private NeonTitle _title;
        private Image _crown;
        private Image _headerGlow;
        private Image _ringA;
        private Image _ringB;
        private readonly Image[] _confetti = new Image[ConfettiCount];

        // Score.
        private RectTransform _scoreSection;
        private Text _scoreText;
        private NumberTicker _scoreTicker;
        private Text _bestValue;
        private RectTransform _bestTag;
        private Text _distanceValue;
        private RectTransform _placement;
        private Text _placementText;

        // Gems.
        private Card _gemsCard;
        private StatRow _pickupsRow;
        private StatRow _missionsRow;
        private StatRow _achievementsRow;
        private StatRow _rankRow;
        private StatRow _dailyRow;
        private RectTransform _totalRow;
        private NumberTicker _totalTicker;
        private NeonButton _double;
        private Text _nextUpgrade;
        private ProgressBar _nextUpgradeBar;

        // Dynamic slots (rebuilt on every show).
        private RectTransform _rankSlot;
        private RectTransform _shipsSlot;
        private RectTransform _missionsSlot;
        private RectTransform _achievementsSlot;

        // Stats.
        private Card _statsCard;
        private readonly Text[] _statValues = new Text[8];

        /// <summary>Connects the screen to the controller (call once, right after creation).</summary>
        public void Bind(GameController game)
        {
            _game = game;
        }

        /// <summary>Re-binds to a (new) summary while visible — e.g. when <see cref="GameController.RunFinished"/> arrives late.</summary>
        public void SetSummary(RunSummary summary)
        {
            if (summary == null || ReferenceEquals(summary, _summary))
            {
                return;
            }

            if (IsVisible)
            {
                Populate(summary);
                PlayReveal();
            }
        }

        // Three steps for the build queue (UIScreen.BuildSteps); nothing reads these fields before the screen is shown.
        protected override IEnumerator BuildSteps()
        {
            Transition = ScreenTransition.Fade;
            TransitionDuration = 0.45f;

            RectTransform backdrop = UIFactory.Backdrop(Rect, 0.9f, true);
            UIFactory.Stretch(backdrop, -RunUi.Overscan, -RunUi.Overscan, -RunUi.Overscan, -RunUi.Overscan);
            RunUi.FullBleed(Rect, new Color(0f, 0f, 0.02f, 0.35f), true, "Blocker");

            RectTransform column = UIFactory.ContentColumn(Rect, UITheme.ContentMaxWidth, 24f);

            _list = UIFactory.ScrollList(column, UITheme.SpaceLg, UIFactory.Padding(36, 36, 24, 48), 48f);
            UIFactory.Stretch(_list.Rect, 0f, 0f, 0f, 220f);
            RectTransform c = _list.Content;

            BuildHeader(c);
            BuildScore(c);
            _dailySlot = Slot(c, "DailySlot"); // Daily Run panel (ResultsScreen.Daily.cs)
            yield return null;
            BuildGems(c);
            _rankSlot = Slot(c, "RankSlot");
            _shipsSlot = Slot(c, "ShipsSlot");
            _missionsSlot = Slot(c, "MissionsSlot");
            _achievementsSlot = Slot(c, "AchievementsSlot");
            BuildStats(c);
            yield return null;

            // Pinned buttons.
            RectTransform bar = UIFactory.CreateRect(column, "Buttons");
            UIFactory.Anchor(bar, AnchorPreset.StretchBottom, new Vector2(0f, 40f));
            UIFactory.SetSize(bar, -48f, UITheme.ButtonHeightLarge);
            HorizontalLayoutGroup h = UIFactory.HStack(bar, UITheme.SpaceMd, null, TextAnchor.MiddleCenter, true);
            h.childForceExpandWidth = true;
            h.childForceExpandHeight = false;
            _buttonsGroup = RunUi.Group(bar);
            _menu = UIFactory.Button(bar, Loc.T("results.menu"), ButtonStyle.Secondary, OnMenu, IconId.Home, null, UITheme.ButtonHeight, 0f);
            UIFactory.SetLayout(_menu, 0f, -1f, 1f);
            _retry = UIFactory.Button(bar, Loc.T("results.retry"), ButtonStyle.Primary, OnRetry, IconId.Retry, null, UITheme.ButtonHeightLarge, 0f);
            UIFactory.SetLayout(_retry, 0f, -1f, 1.5f);
            DefaultSelection = _retry.gameObject;

            // Celebration particles live outside the scroll mask.
            _fxLayer = UIFactory.CreateFill(Rect, "Fx");
            RunUi.PassiveGroup(_fxLayer);
            Sprite star = UIIcons.Get(IconId.Star);
            Sprite gem = UIIcons.Get(IconId.Gem);
            for (int i = 0; i < ConfettiCount; i++)
            {
                Image p = UIFactory.CreateImage(_fxLayer, i % 3 == 0 ? gem : star, Color.white, "Confetti" + i);
                p.preserveAspect = true;
                UIFactory.SetSize(p.rectTransform, 44f, 44f);
                p.gameObject.SetActive(false);
                _confetti[i] = p;
            }
        }

        private void BuildHeader(RectTransform c)
        {
            _header = UIFactory.CreateRect(c, "Header");
            UIFactory.SetLayout(_header, -1f, 250f);

            _headerGlow = UIFactory.CreateImage(_header, UISprites.Glow, UITheme.Gold, "Glow");
            UIFactory.Anchor(_headerGlow.rectTransform, AnchorPreset.Center, new Vector2(0f, -10f));
            UIFactory.SetSize(_headerGlow.rectTransform, 900f, 420f);
            _ringA = UIFactory.CreateImage(_header, UISprites.Ring(6), UITheme.Gold, "RingA");
            UIFactory.Anchor(_ringA.rectTransform, AnchorPreset.Center, new Vector2(0f, -10f));
            UIFactory.SetSize(_ringA.rectTransform, 360f, 360f);
            _ringB = UIFactory.CreateImage(_header, UISprites.Ring(3), UITheme.AccentMagenta, "RingB");
            UIFactory.Anchor(_ringB.rectTransform, AnchorPreset.Center, new Vector2(0f, -10f));
            UIFactory.SetSize(_ringB.rectTransform, 360f, 360f);

            _crown = UIFactory.Icon(_header, IconId.Crown, 84f, UITheme.Gold);
            UIFactory.Anchor(_crown.rectTransform, AnchorPreset.TopCenter, new Vector2(0f, -4f));

            _title = UIFactory.NeonTitle(_header, Loc.T("results.run_complete"), 104);
            UIFactory.Anchor(_title.Rect, AnchorPreset.StretchBottom, new Vector2(0f, 12f));
        }

        private void BuildScore(RectTransform c)
        {
            _scoreSection = UIFactory.VStack(c, 0f, null, TextAnchor.UpperCenter).GetComponent<RectTransform>();
            RunUi.Caption(_scoreSection, Loc.T("results.score"));
            _scoreText = UIFactory.LabelLine(_scoreSection, "0", 150, UITheme.TextPrimary, TextAnchor.MiddleCenter, UIFont.Title, TextGlow.Neon);
            UIFactory.SetLayout(_scoreText, -1f, 176f);
            _scoreTicker = NumberTicker.Attach(_scoreText, 0);

            RectTransform row = UIFactory.HStack(_scoreSection, UITheme.SpaceXl, UIFactory.Padding(0, 0, 8, 0), TextAnchor.MiddleCenter)
                .GetComponent<RectTransform>();
            UIFactory.SetLayout(row, -1f, 120f);
            RectTransform best = MiniStat(row, Loc.T("results.best"), out _bestValue);
            _bestTag = UIFactory.CreateRect(best, "NewTag");
            UIFactory.IgnoreLayout(_bestTag);
            UIFactory.Anchor(_bestTag, AnchorPreset.TopRight, new Vector2(-24f, 12f));
            UIFactory.SetSize(_bestTag, 104f, 42f);
            UIFactory.PillImage(_bestTag, UITheme.Gold, "Background");
            Text tagText = UIFactory.LabelLine(_bestTag, Loc.T("results.new_tag"), UITheme.TextCaption, UITheme.TextOnAccent, TextAnchor.MiddleCenter, UIFont.Title);
            UIFactory.Stretch(tagText.rectTransform);
            Image sep = UIFactory.CreateImage(row, null, new Color(1f, 1f, 1f, 0.16f), "Separator");
            UIFactory.SetLayout(sep, 2f, 80f);
            MiniStat(row, Loc.T("results.distance"), out _distanceValue);

            _placement = UIFactory.HStack(_scoreSection, UITheme.SpaceSm, UIFactory.Padding(0, 0, 16, 0), TextAnchor.MiddleCenter)
                .GetComponent<RectTransform>();
            _placement.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
            UIFactory.SetLayout(_placement, -1f, 72f);
            UIFactory.Icon(_placement, IconId.Trophy, 48f, UITheme.Gold);
            _placementText = UIFactory.LabelLine(_placement, string.Empty, UITheme.TextLead, UITheme.Gold, TextAnchor.MiddleLeft, UIFont.Bold, TextGlow.Shadow);
        }

        private static RectTransform MiniStat(Transform parent, string caption, out Text value)
        {
            RectTransform block = UIFactory.VStack(parent, 0f, null, TextAnchor.MiddleCenter).GetComponent<RectTransform>();
            block.GetComponent<VerticalLayoutGroup>().childForceExpandWidth = false;
            UIFactory.SetLayout(block, 380f, 112f);
            RunUi.Caption(block, caption);
            value = UIFactory.LabelLine(block, "0", UITheme.TextH3, UITheme.TextPrimary, TextAnchor.MiddleCenter, UIFont.Title, TextGlow.Shadow);
            UIFactory.SetLayout(value, -1f, 74f);
            return block;
        }

        private void BuildGems(RectTransform c)
        {
            _gemsCard = UIFactory.Card(c, Loc.T("results.gems"), IconId.Gem, UITheme.GemColor);
            RectTransform body = _gemsCard.Body;
            _pickupsRow = UIFactory.StatRow(body, Loc.T("results.row_collected"), "+0", IconId.Gem);
            _missionsRow = UIFactory.StatRow(body, Loc.T("results.row_missions"), "+0", IconId.Missions);
            _achievementsRow = UIFactory.StatRow(body, Loc.T("results.row_achievements"), "+0", IconId.Trophy);
            _rankRow = UIFactory.StatRow(body, Loc.T("results.row_rank_up"), "+0", IconId.Crown);
            _dailyRow = UIFactory.StatRow(body, Loc.T("results.row_daily"), "+0", IconId.Calendar);

            _totalRow = UIFactory.HStack(body, UITheme.SpaceSm, UIFactory.Padding(4, 4, 12, 4), TextAnchor.MiddleLeft).GetComponent<RectTransform>();
            _totalRow.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
            UIFactory.SetLayout(_totalRow, -1f, 96f);
            Text totalLabel = UIFactory.LabelLine(_totalRow, Loc.T("results.total"), UITheme.TextLead, UITheme.TextPrimary, TextAnchor.MiddleLeft, UIFont.Title);
            UIFactory.SetLayout(totalLabel, -1f, -1f, 1f);
            UIFactory.Icon(_totalRow, IconId.Gem, 56f, UITheme.GemColor);
            Text total = UIFactory.LabelLine(_totalRow, "+0", UITheme.TextH2, UITheme.GemColor, TextAnchor.MiddleRight, UIFont.Title, TextGlow.Neon);
            _totalTicker = NumberTicker.Attach(total, 0, "+");

            _double = UIFactory.Button(body, Loc.T("results.double"), ButtonStyle.Primary, OnDouble, IconId.Ad, Loc.T("results.watch_an_ad"));

            // Next-upgrade progress (the design's "one more run" hook): how close the wallet is to the cheapest upgrade.
            _nextUpgrade = UIFactory.LabelLine(body, string.Empty, 36, UITheme.TextSecondary, TextAnchor.MiddleLeft, UIFont.Bold);
            UIFactory.SetLayout(_nextUpgrade, -1f, 52f);
            _nextUpgradeBar = UIFactory.ProgressBar(body, 0f, 18f, UITheme.GemColor, UITheme.Gold);
            UIFactory.SetLayout(_nextUpgradeBar, -1f, 18f);
            UIFactory.Button(body, Loc.T("nav.upgrades"), ButtonStyle.Secondary, MetaUI.ShowUpgrades, IconId.Bolt);
        }

        /// <summary>"Next upgrade: DAMAGE  120 / 158" and its bar (call after the wallet changed).</summary>
        private void PopulateNextUpgrade(bool animate)
        {
            Core.Meta.ProfileService p = _game != null ? _game.Profile : null;
            Core.Balance.UpgradeId? next = p != null ? p.CheapestUpgrade() : null;
            if (next == null)
            {
                _nextUpgrade.text = Loc.T("results.upgrades_maxed");
                _nextUpgradeBar.SetValue(1f, animate);
                return;
            }

            long cost = p.UpgradeCost(next.Value);
            bool ready = p.Gems >= cost;
            _nextUpgrade.text = ready
                ? Loc.T("results.upgrade_ready", ("name", Loc.T(UpgradeNameKey(next.Value))))
                : Loc.T("results.next_upgrade", ("name", Loc.T(UpgradeNameKey(next.Value))), ("have", RunUi.Grouped(p.Gems)),
                    ("cost", RunUi.Grouped(cost)));
            _nextUpgrade.color = ready ? UITheme.Success : UITheme.TextSecondary;
            _nextUpgradeBar.SetValue(p.NextUpgradeProgress01(), animate, 0.8f);
        }

        private static string UpgradeNameKey(Core.Balance.UpgradeId id)
        {
            switch (id)
            {
                case Core.Balance.UpgradeId.FuelTank:
                    return "upgrade.fuel_tank";
                case Core.Balance.UpgradeId.Efficiency:
                    return "upgrade.efficiency";
                case Core.Balance.UpgradeId.StartFuel:
                    return "upgrade.start_fuel";
                case Core.Balance.UpgradeId.Duration:
                    return "upgrade.duration";
                default:
                    return "upgrade.hull";
            }
        }

        private void BuildStats(RectTransform c)
        {
            _statsCard = UIFactory.Card(c, Loc.T("results.run_stats"), IconId.Stats);
            KeyValueGrid grid = UIFactory.KeyValueGrid(_statsCard.Body, 2, 56);
            for (int i = 0; i < StatKeys.Length; i++)
            {
                _statValues[i] = grid.Add(Loc.T(StatKeys[i]), "0");
            }
        }

        private static RectTransform Slot(RectTransform parent, string name)
        {
            RectTransform slot = UIFactory.CreateRect(parent, name);
            UIFactory.VStack(slot, UITheme.SpaceLg, null, TextAnchor.UpperCenter, true);
            slot.gameObject.SetActive(false);
            return slot;
        }

        // ------------------------------------------------------------------ show

        protected override void OnShow()
        {
            _leaving = false;
            _doubling = false;
            _retry.SetInteractable(true);
            _menu.SetInteractable(true);
            Populate(_game != null ? _game.LastSummary : null);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_list.Content);
            _list.ScrollToTop(false);
            PlayReveal();
        }

        protected override void OnHide()
        {
            UITween.Kill(this);
            for (int i = 0; i < ConfettiCount; i++)
            {
                _confetti[i].gameObject.SetActive(false);
            }
        }

        private void Update()
        {
            // A rewarded ad may finish loading after the screen opened: offer DOUBLE as soon as it is ready.
            _adPoll -= Time.unscaledDeltaTime;
            if (_adPoll > 0f || _doubling || _leaving)
            {
                return;
            }

            _adPoll = 0.5f;
            if (!_double.gameObject.activeSelf && CanDouble())
            {
                _double.gameObject.SetActive(true);
                _double.SetInteractable(true);
                UIFactory.PopIn(_double.Rect);
                _double.SetAttention(true);
            }
        }

        /// <summary>Back = MENU (a no-op while the DOUBLE rewarded ad, or any ad, is on screen — like the revive offer).</summary>
        public override bool OnBack()
        {
            bool adShowing = _game != null && _game.Ads != null && _game.Ads.IsShowing;
            if (!Dialogs.IsOpen && !_doubling && !adShowing)
            {
                OnMenu();
            }

            return true;
        }

        private void Populate(RunSummary s)
        {
            _summary = s;
            RunResult r = s != null ? s.Result : null;
            bool record = s != null && s.NewBestScore;

            // Header.
            _title.SetText(record ? Loc.T("results.new_record") : Loc.T("results.run_complete"));
            if (record)
            {
                _title.SetColors(UITheme.Gold, UITheme.AccentMagenta);
            }
            else
            {
                _title.SetColors(UITheme.Accent, UITheme.AccentAlt);
            }

            _crown.gameObject.SetActive(record);
            _headerGlow.gameObject.SetActive(record);
            _ringA.gameObject.SetActive(record);
            _ringB.gameObject.SetActive(record);

            // Score.
            long score = r != null ? r.Score : 0;
            long best = _game != null && _game.Profile != null ? Math.Max(score, _game.Profile.Profile.Stats.BestScore) : score;
            _scoreTicker.SetValue(0, false);
            _bestValue.text = RunUi.Grouped(best);
            _bestTag.gameObject.SetActive(record);
            _distanceValue.text = RunUi.Formatted("hud.distance", r != null ? (long)r.Distance : 0L);
            int place = s != null ? s.LeaderboardPosition : -1;
            _placement.gameObject.SetActive(place >= 0);
            if (place >= 0)
            {
                _placementText.text = Loc.T("results.placement", ("place", place + 1));
            }

            // Gems.
            PopulateGems();

            // Dynamic sections.
            BuildDaily(s);
            BuildRankUp(s);
            BuildShips(s);
            BuildMissions(s);
            BuildAchievements(s);

            // Stats.
            _statValues[0].text = RunUi.Grouped(r != null ? Mathf.Max(1, r.LevelReached) : 1);
            _statValues[1].text = RunUi.Grouped(r != null ? r.GapsBridged : 0);
            _statValues[2].text = RunUi.Grouped(r != null ? r.Builds : 0);
            _statValues[3].text = RunUi.Grouped(r != null ? r.NearMisses : 0);
            _statValues[4].text = RunUi.Grouped(r != null ? r.BestNearMissChain : 0);
            _statValues[5].text = RunUi.Multiplier(r != null ? Mathf.Max(1, r.MaxMultiplier) : 1);
            _statValues[6].text = RunUi.Grouped(r != null ? r.Smashed : 0);
            _statValues[7].text = RunUi.Clock(r != null ? r.Duration : 0f);
        }

        private void PopulateGems()
        {
            RunSummary s = _summary;
            int collected = s != null ? s.GemsCollected : 0;
            bool doubled = s != null && s.GemsDoubled;
            _pickupsRow.SetValue(doubled ? "+" + RunUi.Grouped(collected) + "  " + RunUi.Times + "2" : "+" + RunUi.Grouped(collected),
                doubled ? UITheme.Gold : UITheme.TextPrimary);
            SetRow(_missionsRow, s != null ? s.MissionRewards : 0);
            SetRow(_achievementsRow, s != null ? s.AchievementRewards : 0);
            SetRow(_rankRow, s != null ? s.RankUpReward : 0);
            SetRow(_dailyRow, s != null && s.Daily != null ? s.Daily.TotalReward : 0);
            _totalTicker.SetValue(0, false);
            UITween.Kill(_double.Rect);
            _double.Rect.localScale = Vector3.one;
            _double.SetAttention(false);
            _double.SetInteractable(true);
            _double.gameObject.SetActive(CanDouble());
            PopulateNextUpgrade(false);
        }

        private static void SetRow(StatRow row, int amount)
        {
            row.gameObject.SetActive(amount > 0);
            row.SetValue("+" + RunUi.Grouped(amount));
        }

        private bool CanDouble()
        {
            RunSummary s = _summary;
            return s != null && !s.GemsDoubled && s.GemsCollected > 0 && _game != null && _game.Ads != null && _game.Ads.RewardedReady;
        }

        private void BuildRankUp(RunSummary s)
        {
            ClearSlot(_rankSlot);
            if (s == null || !s.RankUp)
            {
                return;
            }

            Card card = UIFactory.Card(_rankSlot, Loc.T("results.rank_up"), IconId.Crown, UITheme.Gold);
            RectTransform row = UIFactory.HStack(card.Body, UITheme.SpaceMd, null, TextAnchor.MiddleLeft).GetComponent<RectTransform>();
            row.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
            RectTransform texts = UIFactory.VStack(row, 0f, null, TextAnchor.MiddleLeft).GetComponent<RectTransform>();
            UIFactory.SetLayout(texts, 0f, -1f, 1f);
            UIFactory.LabelLine(texts, Loc.T("missions.rank_title", ("rank", s.NewRank)), UITheme.TextH3, UITheme.Lighten(UITheme.Gold, 0.3f), TextAnchor.MiddleLeft, UIFont.Title, TextGlow.Neon);
            float bonus = _game != null && _game.Profile != null ? _game.Profile.ScoreBonus : 0f;
            if (bonus > 0f)
            {
                UIFactory.Label(texts, Loc.T("results.score_bonus", ("pct", Mathf.RoundToInt(bonus * 100f))), UITheme.TextBody, UITheme.TextSecondary, TextAnchor.MiddleLeft);
            }

            if (s.RankUpReward > 0)
            {
                RewardTag(row, s.RankUpReward);
            }

            _rankSlot.gameObject.SetActive(true);
        }

        private void BuildShips(RunSummary s)
        {
            ClearSlot(_shipsSlot);
            if (s == null || (s.ShipsUnlocked.Count == 0 && s.ShipsAvailable.Count == 0))
            {
                return;
            }

            for (int i = 0; i < s.ShipsUnlocked.Count; i++)
            {
                ShipDefinition ship = s.ShipsUnlocked[i];
                if (ship == null)
                {
                    continue;
                }

                Card card = UIFactory.Card(_shipsSlot, Loc.T("results.new_ship"), IconId.Hangar, UITheme.AccentMagenta);
                UIFactory.LabelLine(card.Body, ship.DisplayName, UITheme.TextH3, UITheme.TextPrimary, TextAnchor.MiddleLeft, UIFont.Title, TextGlow.Neon);
                if (!string.IsNullOrEmpty(ship.Tagline))
                {
                    UIFactory.Label(card.Body, ship.Tagline, UITheme.TextBody, UITheme.TextSecondary, TextAnchor.MiddleLeft);
                }

                UIFactory.Label(card.Body, Loc.T("results.find_in_hangar"), UITheme.TextSmall, UITheme.TextMuted, TextAnchor.MiddleLeft);
            }

            // Requirement met, price still to pay in the hangar.
            for (int i = 0; i < s.ShipsAvailable.Count; i++)
            {
                ShipDefinition ship = s.ShipsAvailable[i];
                if (ship == null)
                {
                    continue;
                }

                Card card = UIFactory.Card(_shipsSlot, Loc.T("results.ship_available"), IconId.Hangar, UITheme.AccentMagenta);
                UIFactory.LabelLine(card.Body, ship.DisplayName, UITheme.TextH3, UITheme.TextPrimary, TextAnchor.MiddleLeft, UIFont.Title, TextGlow.Neon);
                UIFactory.Label(card.Body, Loc.T("results.unlock_in_hangar", ("n", ship.Price)), UITheme.TextSmall, UITheme.TextMuted, TextAnchor.MiddleLeft);
            }

            _shipsSlot.gameObject.SetActive(_shipsSlot.childCount > 0);
        }

        private void BuildMissions(RunSummary s)
        {
            ClearSlot(_missionsSlot);
            if (s == null || s.MissionsCompleted.Count == 0)
            {
                return;
            }

            Card card = UIFactory.Card(_missionsSlot, Loc.T("results.missions_complete"), IconId.Check, UITheme.Success);
            for (int i = 0; i < s.MissionsCompleted.Count; i++)
            {
                Mission m = s.MissionsCompleted[i];
                if (m == null)
                {
                    continue;
                }

                RectTransform row = UIFactory.HStack(card.Body, UITheme.SpaceSm, UIFactory.Padding(0, 0, 6, 6), TextAnchor.MiddleLeft).GetComponent<RectTransform>();
                row.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
                UIFactory.Icon(row, IconId.Check, 48f, UITheme.Success);
                Text d = UIFactory.Label(row, MissionSystem.Describe(m), UITheme.TextBody, UITheme.TextPrimary, TextAnchor.MiddleLeft, UIFont.Bold);
                UIFactory.SetLayout(d, 0f, -1f, 1f);
                RewardTag(row, m.Reward);
            }

            _missionsSlot.gameObject.SetActive(true);
        }

        private void BuildAchievements(RunSummary s)
        {
            ClearSlot(_achievementsSlot);
            if (s == null || s.AchievementsUnlocked.Count == 0)
            {
                return;
            }

            Card card = UIFactory.Card(_achievementsSlot, Loc.T("results.achievements"), IconId.Trophy, UITheme.Gold);
            for (int i = 0; i < s.AchievementsUnlocked.Count; i++)
            {
                AchievementDefinition a = s.AchievementsUnlocked[i];
                if (a == null)
                {
                    continue;
                }

                RectTransform row = UIFactory.HStack(card.Body, UITheme.SpaceSm, UIFactory.Padding(0, 0, 6, 6), TextAnchor.MiddleLeft).GetComponent<RectTransform>();
                row.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
                UIFactory.Icon(row, IconId.Trophy, 52f, UITheme.Gold);
                RectTransform texts = UIFactory.VStack(row, 0f, null, TextAnchor.MiddleLeft).GetComponent<RectTransform>();
                UIFactory.SetLayout(texts, 0f, -1f, 1f);
                UIFactory.Label(texts, a.Title, UITheme.TextLead, UITheme.TextPrimary, TextAnchor.MiddleLeft, UIFont.Bold);
                UIFactory.Label(texts, a.Description, UITheme.TextSmall, UITheme.TextSecondary, TextAnchor.MiddleLeft);
                RewardTag(row, a.Reward);
            }

            _achievementsSlot.gameObject.SetActive(true);
        }

        private static void RewardTag(Transform parent, int amount)
        {
            RectTransform tag = UIFactory.HStack(parent, 6f, null, TextAnchor.MiddleRight).GetComponent<RectTransform>();
            tag.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
            UIFactory.SetLayout(tag, 160f, 60f);
            UIFactory.Icon(tag, IconId.Gem, 36f, UITheme.GemColor);
            UIFactory.LabelLine(tag, "+" + RunUi.Grouped(amount), UITheme.TextLead, UITheme.GemColor, TextAnchor.MiddleLeft, UIFont.Bold);
        }

        private static void ClearSlot(RectTransform slot)
        {
            for (int i = slot.childCount - 1; i >= 0; i--)
            {
                GameObject child = slot.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }

            slot.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ reveal

        private void PlayReveal()
        {
            UITween.Kill(this);
            RunSummary s = _summary;
            bool record = s != null && s.NewBestScore;
            float t = 0.1f;

            // Header.
            RevealSection(_header, t, 0.5f);
            if (record)
            {
                UITween.Scale(_title.Rect, 0.2f, 1f, 0.9f).SetDelay(t).SetEase(UIEase.OutElastic);
                UITween.Scale(_crown.rectTransform, 0.01f, 1f, 0.6f).SetDelay(t + 0.25f).SetEase(UIEase.OutBack);
                _headerGlow.canvasRenderer.SetAlpha(0f);
                UITween.RendererAlpha(_headerGlow, 0.85f, 0.6f).SetDelay(t);
                RingBurst(_ringA, t + 0.15f, 2.3f);
                RingBurst(_ringB, t + 0.3f, 2.9f);
                Sfx(SfxId.NewRecord, t + 0.2f);
                UITween.Delay(this, t + 0.25f, BurstConfetti);
            }
            else
            {
                UITween.Scale(_title.Rect, 1.25f, 1f, 0.6f).SetDelay(t).SetEase(UIEase.OutCubic);
            }

            // Score count-up.
            t += 0.3f;
            RevealSection(_scoreSection, t, 0.45f);
            long score = s != null && s.Result != null ? s.Result.Score : 0;
            float countDuration = Mathf.Clamp(0.6f + (float)Math.Log10(score + 1.0) * 0.18f, 0.6f, 1.6f);
            UITween.Delay(this, t + 0.15f, () => _scoreTicker.SetValue(score, true, countDuration));
            if (_placement.gameObject.activeSelf)
            {
                UITween.Delay(this, t + 0.2f + countDuration, () => UITween.Punch(_placement, 0.25f, 0.45f));
            }

            t += 0.35f + countDuration * 0.5f;
            t = RevealDaily(t);

            // Gems.
            RevealSection(_gemsCard.Rect, t, 0.45f);
            long total = s != null ? s.TotalGemsEarned : 0;
            UITween.Delay(this, t + 0.25f, () => _totalTicker.SetValue(total, true));
            if (_double.gameObject.activeSelf)
            {
                UITween.Delay(this, t + 0.9f, () =>
                {
                    if (_double != null && _double.gameObject.activeInHierarchy)
                    {
                        _double.SetAttention(true);
                    }
                });
            }

            t += 0.35f;

            if (_rankSlot.gameObject.activeSelf)
            {
                RevealSection(_rankSlot, t, 0.55f);
                Sfx(SfxId.RankUp, t + 0.05f);
                UITween.Delay(this, t + 0.1f, () => UITween.Punch(_rankSlot, 0.12f, 0.5f));
                t += 0.45f;
            }

            if (_shipsSlot.gameObject.activeSelf)
            {
                RevealSection(_shipsSlot, t, 0.5f);
                t += 0.35f;
            }

            if (_missionsSlot.gameObject.activeSelf)
            {
                RevealSection(_missionsSlot, t, 0.5f);
                Sfx(SfxId.MissionComplete, t + 0.05f);
                t += 0.35f;
            }

            if (_achievementsSlot.gameObject.activeSelf)
            {
                RevealSection(_achievementsSlot, t, 0.5f);
                Sfx(SfxId.Reward, t + 0.05f);
                t += 0.35f;
            }

            RevealSection(_statsCard.Rect, t, 0.45f);
            UIFactory.StaggerIn(_statsCard.Body.GetChild(0), t + 0.1f, 0.05f);

            // Buttons appear early so a quick retry is always possible.
            _buttonsGroup.alpha = 0f;
            UITween.Fade(_buttonsGroup, 0f, 1f, 0.35f).SetDelay(0.45f).SetEase(UIEase.OutCubic);
        }

        private static void RevealSection(RectTransform section, float delay, float duration)
        {
            CanvasGroup g = RunUi.Group(section);
            g.alpha = 0f;
            UITween.Fade(g, 0f, 1f, duration * 0.8f).SetDelay(delay).SetEase(UIEase.OutCubic);
            UITween.Scale(section, 0.92f, 1f, duration).SetDelay(delay).SetEase(UIEase.OutBack);
        }

        private void RingBurst(Image ring, float delay, float scale)
        {
            Color c = ring.color;
            c.a = 0f;
            ring.color = c;
            ring.rectTransform.localScale = new Vector3(0.3f, 0.3f, 1f);
            UITween.Scale(ring.rectTransform, 0.3f, scale, 1.1f).SetDelay(delay).SetEase(UIEase.OutCubic);
            UITween.Value(this, 0f, 1f, 1.1f, v =>
            {
                Color k = ring.color;
                k.a = v < 0.12f ? v / 0.12f : 1f - (v - 0.12f) / 0.88f;
                ring.color = k;
            }).SetDelay(delay);
        }

        private void BurstConfetti()
        {
            if (_fxLayer == null || !isActiveAndEnabled)
            {
                return;
            }

            Vector3 world = _title.Rect.position;
            Vector2 origin = _fxLayer.InverseTransformPoint(world);
            Color[] palette = { UITheme.Gold, UITheme.AccentMagenta, UITheme.AccentCyan, UITheme.Accent, UITheme.AccentYellow };
            for (int i = 0; i < ConfettiCount; i++)
            {
                Image p = _confetti[i];
                RectTransform rt = p.rectTransform;
                p.gameObject.SetActive(true);
                p.color = palette[i % palette.Length];
                float angle = (i / (float)ConfettiCount) * Mathf.PI * 2f + UnityEngine.Random.Range(-0.2f, 0.2f);
                float dist = UnityEngine.Random.Range(260f, 560f);
                Vector2 to = origin + new Vector2(Mathf.Cos(angle) * dist, Mathf.Sin(angle) * dist * 0.75f - 60f);
                float life = UnityEngine.Random.Range(0.9f, 1.4f);
                float size = UnityEngine.Random.Range(0.6f, 1.25f);
                rt.anchoredPosition = origin;
                rt.localEulerAngles = Vector3.zero;
                UITween.Move(rt, origin, to, life).SetEase(UIEase.OutCubic);
                UITween.Scale(rt, 0.1f, size, 0.35f).SetEase(UIEase.OutBack);
                UITween.Rotate(rt, UnityEngine.Random.Range(-360f, 360f), life);
                UITween.Alpha(p, 0f, life * 0.45f).SetDelay(life * 0.55f).OnComplete(() =>
                {
                    if (p != null)
                    {
                        p.gameObject.SetActive(false);
                    }
                });
            }
        }

        private void Sfx(SfxId id, float delay)
        {
            if (_game == null)
            {
                return;
            }

            UITween.Delay(this, delay, () =>
            {
                if (_game != null && IsVisible)
                {
                    _game.PlayUiSfx(id);
                }
            });
        }

        // ------------------------------------------------------------------ actions

        private void OnDouble()
        {
            if (_doubling || _leaving || !CanDouble())
            {
                return;
            }

            _doubling = true;
            _double.SetInteractable(false);
            _double.SetAttention(false);
            RunSummary summary = _summary;
            _game.DoubleGemsWithAd(completed =>
            {
                _doubling = false;
                if (this == null || !ReferenceEquals(summary, _summary))
                {
                    return;
                }

                if (completed)
                {
                    PopulateDoubled();
                }
                else
                {
                    _double.SetInteractable(true);
                    _double.gameObject.SetActive(CanDouble());
                }
            });
        }

        private void PopulateDoubled()
        {
            RunSummary s = _summary;
            if (s == null)
            {
                return;
            }

            _pickupsRow.SetValue("+" + RunUi.Grouped(s.GemsCollected) + "  " + RunUi.Times + "2", UITheme.Gold);
            UITween.Punch(_pickupsRow.Rect, 0.15f, 0.4f);
            _totalTicker.SetValue(s.TotalGemsEarned, true, 0.8f);
            UITween.Punch(_totalRow, 0.2f, 0.5f);
            PopulateNextUpgrade(true);
            _double.SetInteractable(false);
            UITween.Scale(_double.Rect, 0f, 0.25f).SetEase(UIEase.InBack).OnComplete(() =>
            {
                if (_double != null)
                {
                    _double.gameObject.SetActive(false);
                    _double.Rect.localScale = Vector3.one;
                }
            });
        }

        private void OnRetry()
        {
            Leave(true);
        }

        private void OnMenu()
        {
            Leave(false);
        }

        private void Leave(bool retry)
        {
            if (_leaving || _doubling || _game == null || _game.State != GameState.GameOver)
            {
                return;
            }

            _leaving = true;
            _retry.SetInteractable(false);
            _menu.SetInteractable(false);
            _game.ContinueFromResults(retry);
        }
    }
}
