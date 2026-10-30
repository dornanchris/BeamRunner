using System;
using System.Collections.Generic;
using System.Globalization;
using BeamRunner.Core.Localization;
using BeamRunner.Core.Meta;
using UnityEngine;
using UnityEngine.UI;

namespace BeamRunner.UI
{
    /// <summary>Records overlay with STATS / LEADERBOARD / ACHIEVEMENTS tabs in a scroll list.</summary>
    public sealed partial class RecordsScreen : MetaScreen
    {
        private static readonly Color Silver = UITheme.Hex(0xD6DCF0);
        private static readonly Color Bronze = UITheme.Hex(0xE08A4F);

        private SegmentedControl _tabs;
        private ScrollList _list;
        private int _tab;

        protected override void Build()
        {
            Transition = ScreenTransition.SlideUp;
            RectTransform r = Rect;
            MetaUI.Dimmer(r, 0.74f);
            RectTransform column = UIFactory.ContentColumn(r, 1000f, 32f);
            NeonTitle title;
            MetaUI.Header(column, Loc.T("records.title"), MetaUI.CloseTop, out title);

            _tabs = UIFactory.SegmentedControl(column,
                new[] { Loc.T("records.tab_stats"), Loc.T("records.tab_leaderboard"), Loc.T("records.tab_achievements") }, 0, OnTab);
            UIFactory.Anchor(_tabs.Rect, AnchorPreset.StretchTop, new Vector2(0f, -MetaUI.HeaderBottom));
            _tabs.Rect.sizeDelta = new Vector2(0f, 96f);

            _list = UIFactory.ScrollList(column, UITheme.SpaceMd, UIFactory.Padding(32, 32, 24, 80), 40f);
            UIFactory.Stretch(_list.Rect, -32f, MetaUI.HeaderBottom + 120f, -32f, 0f);
        }

        protected override void OnShow()
        {
            _tabs.Select(_tab, false, false);
            base.OnShow();
        }

        protected override void Refresh(bool showing)
        {
            Rebuild(showing && !UITheme.ReducedMotion);
        }

        private void OnTab(int index)
        {
            if (index == _tab)
            {
                return;
            }

            _tab = index;
            if (HasData)
            {
                Rebuild(!UITheme.ReducedMotion);
            }
        }

        private void Rebuild(bool animate)
        {
            _list.Clear();
            PlayerProfile data = Profile.Profile;
            switch (_tab)
            {
                case 1:
                    BuildOnlineCard(_list.Content, false); // RecordsScreen.Online.cs
                    BuildLeaderboard(_list.Content, data);
                    break;
                case 2:
                    BuildOnlineCard(_list.Content, true);
                    BuildAchievements(_list.Content, data);
                    break;
                default:
                    BuildStats(_list.Content, data);
                    BuildDailyRunStats(_list.Content, Profile); // RecordsScreen.Daily.cs
                    break;
            }

            _list.ScrollToTop();
            if (animate)
            {
                UIFactory.StaggerIn(_list.Content, 0.03f, 0.04f);
            }
        }

        // ------------------------------------------------------------------ stats

        private static void BuildStats(RectTransform c, PlayerProfile data)
        {
            PlayerStats s = data.Stats ?? new PlayerStats();

            Card bests = UIFactory.Card(c, Loc.T("records.personal_bests"), IconId.Trophy, UITheme.Gold);
            KeyValueGrid g = UIFactory.KeyValueGrid(bests.Body, 2, 60);
            g.Add(Loc.T("records.best_score"), MetaUI.N(s.BestScore), UITheme.Gold);
            g.Add(Loc.T("records.best_distance"), MetaUI.Meters(s.BestDistance));
            g.Add(Loc.T("records.best_multiplier"), "x" + Mathf.Max(1, s.BestMultiplier), UITheme.AccentMagenta);
            g.Add(Loc.T("records.best_level"), Loc.T("hud.level", ("n", Mathf.Max(1, s.BestLevel))), UITheme.AccentCyan);
            g.Add(Loc.T("records.best_chain"), MetaUI.N(s.BestNearMissChain));
            g.Add(Loc.T("records.most_gems"), MetaUI.N(s.BestGemsRun), UITheme.GemColor);

            Card career = UIFactory.Card(c, Loc.T("records.career"), IconId.Stats);
            UIFactory.StatRow(career.Body, Loc.T("records.runs"), MetaUI.N(s.Runs), IconId.Play);
            UIFactory.StatRow(career.Body, Loc.T("records.play_time"), MetaUI.Duration(s.TotalPlaySeconds), IconId.Calendar);
            UIFactory.StatRow(career.Body, Loc.T("records.total_distance"), MetaUI.Kilometers(s.TotalDistance), IconId.Speed);
            UIFactory.StatRow(career.Body, Loc.T("records.gems_collected"), MetaUI.N(s.TotalGems), IconId.Gem);
            UIFactory.StatRow(career.Body, Loc.T("records.near_misses"), MetaUI.N(s.TotalNearMisses), IconId.Hand);
            UIFactory.StatRow(career.Body, Loc.T("records.levels_cleared"), MetaUI.N(s.TotalLevelsCleared), IconId.Star);
            UIFactory.StatRow(career.Body, Loc.T("records.gaps_bridged"), MetaUI.N(s.TotalGapsBridged), IconId.ChevronUp);
            UIFactory.StatRow(career.Body, Loc.T("records.cubes_dodged"), MetaUI.N(s.TotalCubesDodged), IconId.Hand);
            UIFactory.StatRow(career.Body, Loc.T("records.smashed"), MetaUI.N(s.TotalSmashes), IconId.Shield);
            UIFactory.StatRow(career.Body, Loc.T("records.overloads"), MetaUI.N(s.TotalOverloads), IconId.Bolt);
            UIFactory.StatRow(career.Body, Loc.T("records.powerups"), MetaUI.N(s.TotalPowerUps), IconId.Magnet);
            UIFactory.StatRow(career.Body, Loc.T("records.revives"), MetaUI.N(s.Revives), IconId.Heart, false);
        }

        // ------------------------------------------------------------------ leaderboard

        private static void BuildLeaderboard(RectTransform c, PlayerProfile data)
        {
            List<LeaderboardEntry> board = data.Leaderboard;
            int count = board != null ? Mathf.Min(board.Count, ProfileService.LeaderboardSize) : 0;
            if (count == 0)
            {
                Card empty = UIFactory.Card(c, null, IconId.None, null, 56f);
                VerticalLayoutGroup v = empty.Body.GetComponent<VerticalLayoutGroup>();
                if (v != null)
                {
                    v.childAlignment = TextAnchor.UpperCenter;
                }

                Image icon = UIFactory.Icon(empty.Body, IconId.Trophy, 140f, UITheme.Gold);
                icon.preserveAspect = true;
                UIFactory.Label(empty.Body, Loc.T("records.empty_title"), UITheme.TextH3, UITheme.TextPrimary, TextAnchor.MiddleCenter, UIFont.Title, TextGlow.Shadow);
                UIFactory.Label(empty.Body, Loc.T("records.empty_body"), UITheme.TextBody, UITheme.TextSecondary, TextAnchor.MiddleCenter);
                return;
            }

            MetaUI.Section(c, Loc.T("records.top_runs", ("n", count)), IconId.Crown, UITheme.Gold);
            for (int i = 0; i < count; i++)
            {
                LeaderboardEntry e = board[i];
                if (e != null)
                {
                    BuildLeaderboardRow(c, i, e);
                }
            }
        }

        private static void BuildLeaderboardRow(RectTransform c, int index, LeaderboardEntry e)
        {
            Color medal = index == 0 ? UITheme.Gold : (index == 1 ? Silver : (index == 2 ? Bronze : UITheme.TextMuted));
            bool podium = index < 3;
            Card card = UIFactory.Card(c, null, IconId.None, podium ? medal : (Color?)null, 28f);
            if (!podium)
            {
                card.Panel.SetFrameVisible(false, false);
            }

            RectTransform row = UIFactory.CreateRect(card.Body, "Row");
            HorizontalLayoutGroup h = UIFactory.HStack(row, UITheme.SpaceMd, UIFactory.Padding(4, 8, 0, 0), TextAnchor.MiddleLeft, true);
            h.childForceExpandWidth = false;

            Image ring;
            Image glow;
            RectTransform emblem = MetaUI.Emblem(row, 96f, medal, out ring, out glow);
            glow.gameObject.SetActive(podium);
            if (index == 0)
            {
                Image crown = UIFactory.Icon(emblem, IconId.Crown, 52f, UITheme.Gold);
                UIFactory.Stretch(crown.rectTransform, 22f, 22f, 22f, 22f);
            }
            else
            {
                Text n = UIFactory.LabelLine(emblem, (index + 1).ToString(MetaUI.Inv), 44, podium ? medal : UITheme.TextSecondary, TextAnchor.MiddleCenter, UIFont.Title);
                UIFactory.Stretch(n.rectTransform, 0f, 0f, 0f, 4f);
            }

            RectTransform texts = UIFactory.CreateRect(row, "Texts");
            VerticalLayoutGroup v = UIFactory.VStack(texts, 0f, null, TextAnchor.MiddleLeft, true);
            v.childForceExpandWidth = true;
            UIFactory.SetLayout(texts, 0f, -1f, 1f);
            UIFactory.LabelLine(texts, MetaUI.N(e.Score), UITheme.TextH3, podium ? UITheme.Lighten(medal, 0.4f) : UITheme.TextPrimary,
                TextAnchor.MiddleLeft, UIFont.Title, TextGlow.Shadow);
            ShipDefinition ship = ShipCatalog.Get(e.Ship);
            UIFactory.LabelLine(texts, MetaUI.Meters(e.Distance) + "  ·  " + (ship != null ? ship.DisplayName : "?"), 34, UITheme.TextSecondary,
                TextAnchor.MiddleLeft, UIFont.Bold);

            UIFactory.LabelLine(row, FormatDate(e.Date), 32, UITheme.TextMuted, TextAnchor.MiddleRight, UIFont.Bold);
        }

        private static string FormatDate(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return string.Empty;
            }

            DateTime d;
            if (DateTime.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d))
            {
                return MetaUI.ShortDate(d);
            }

            return raw;
        }

        // ------------------------------------------------------------------ achievements

        private static void BuildAchievements(RectTransform c, PlayerProfile data)
        {
            IReadOnlyList<AchievementDefinition> all = AchievementCatalog.All;
            List<string> owned = data.Achievements ?? new List<string>();
            int unlocked = 0;
            for (int i = 0; i < all.Count; i++)
            {
                if (owned.Contains(all[i].Id))
                {
                    unlocked++;
                }
            }

            Card header = UIFactory.Card(c, null, IconId.None, UITheme.Gold, 36f);
            RectTransform top = UIFactory.CreateRect(header.Body, "Top");
            HorizontalLayoutGroup th = UIFactory.HStack(top, UITheme.SpaceMd, null, TextAnchor.MiddleLeft, true);
            th.childForceExpandWidth = false;
            UIFactory.SetLayout(top, -1f, 96f);
            UIFactory.Icon(top, IconId.Trophy, 80f, UITheme.Gold);
            Text count = UIFactory.LabelLine(top, MetaUI.N(unlocked) + " / " + MetaUI.N(all.Count), UITheme.TextH2, UITheme.TextPrimary, TextAnchor.MiddleLeft, UIFont.Title, TextGlow.Neon);
            UIFactory.SetLayout(count, -1f, -1f, 1f);
            UIFactory.LabelLine(top, Loc.T("records.unlocked"), 34, UITheme.TextMuted, TextAnchor.MiddleRight, UIFont.Bold);
            ProgressBar total = UIFactory.ProgressBar(header.Body, 0f, 22f, UITheme.Gold, UITheme.AccentYellow);
            total.SetValue(all.Count > 0 ? unlocked / (float)all.Count : 0f, true, 0.8f);

            // Unlocked first (keeps catalog order within each group).
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < all.Count; i++)
                {
                    AchievementDefinition def = all[i];
                    bool isUnlocked = owned.Contains(def.Id);
                    if (isUnlocked == (pass == 0))
                    {
                        BuildAchievementRow(c, def, isUnlocked, data);
                    }
                }
            }
        }

        private static void BuildAchievementRow(RectTransform c, AchievementDefinition def, bool unlocked, PlayerProfile data)
        {
            Card card = UIFactory.Card(c, null, IconId.None, unlocked ? UITheme.Gold : (Color?)null, 28f);
            if (!unlocked)
            {
                card.Panel.SetFrameVisible(false, false);
            }

            RectTransform row = UIFactory.CreateRect(card.Body, "Row");
            HorizontalLayoutGroup h = UIFactory.HStack(row, UITheme.SpaceMd, null, TextAnchor.MiddleLeft, true);
            h.childForceExpandWidth = false;

            Image ring;
            Image glow;
            Color tone = unlocked ? UITheme.Gold : UITheme.TextMuted;
            RectTransform emblem = MetaUI.Emblem(row, 100f, tone, out ring, out glow);
            glow.gameObject.SetActive(unlocked);
            Image icon = UIFactory.Icon(emblem, unlocked ? IconId.Trophy : IconId.Lock, 52f, unlocked ? UITheme.Gold : UITheme.TextMuted);
            UIFactory.Stretch(icon.rectTransform, 24f, 24f, 24f, 24f);
            if (unlocked)
            {
                RectTransform check = UIFactory.CreateRect(emblem, "Check");
                UIFactory.Anchor(check, AnchorPreset.BottomRight, new Vector2(8f, -6f));
                check.sizeDelta = new Vector2(44f, 44f);
                Image disc = UIFactory.CreateImage(check, UISprites.Circle, UITheme.Success, "Disc");
                UIFactory.Stretch(disc.rectTransform);
                Image tick = UIFactory.Icon(check, IconId.Check, 30f, UITheme.TextOnAccent);
                UIFactory.Stretch(tick.rectTransform, 7f, 7f, 7f, 7f);
            }

            RectTransform texts = UIFactory.CreateRect(row, "Texts");
            VerticalLayoutGroup v = UIFactory.VStack(texts, 4f, null, TextAnchor.MiddleLeft, true);
            v.childForceExpandWidth = true;
            UIFactory.SetLayout(texts, 0f, -1f, 1f);
            UIFactory.Label(texts, def.Title, 40, unlocked ? UITheme.TextPrimary : UITheme.TextSecondary, TextAnchor.MiddleLeft, UIFont.Title,
                unlocked ? TextGlow.Glow : TextGlow.None);
            UIFactory.Label(texts, def.Description, 36, UITheme.TextSecondary, TextAnchor.MiddleLeft, UIFont.Body);
            if (!unlocked)
            {
                RectTransform progress = UIFactory.CreateRect(texts, "Progress");
                HorizontalLayoutGroup ph = UIFactory.HStack(progress, UITheme.SpaceSm, UIFactory.Padding(0, 0, 6, 0), TextAnchor.MiddleLeft, true);
                ph.childForceExpandWidth = false;
                UIFactory.SetLayout(progress, -1f, 44f);
                ProgressBar bar = UIFactory.ProgressBar(progress, 0f, 16f);
                UIFactory.SetLayout(bar, 0f, 16f, 1f);
                bar.SetValue(def.Progress01(data), true, 0.7f);
                double target = def.Target;
                double value = Math.Min(Math.Max(0.0, def.Value(data)), target);
                UIFactory.LabelLine(progress, MetaUI.N(value) + " / " + MetaUI.N(target), 30, UITheme.TextMuted, TextAnchor.MiddleRight, UIFont.Bold);
            }

            Image plate;
            Text reward = MetaUI.RewardChip(row, def.Reward, out plate);
            if (unlocked)
            {
                plate.color = UITheme.Alpha(UITheme.Gold, 0.22f);
                reward.color = UITheme.Lighten(UITheme.Gold, 0.3f);
            }
        }
    }
}
