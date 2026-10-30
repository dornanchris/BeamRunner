using System.Collections;
using System.Collections.Generic;
using BeamRunner.Core.Localization;
using BeamRunner.Core.Meta;
using UnityEngine;
using UnityEngine.UI;

namespace BeamRunner.UI
{
    /// <summary>
    /// Missions overlay: pilot-rank header (rank ring filled by completed missions, score bonus, rank-up reward) and the
    /// three active mission cards with progress, reward and a completed glow.
    /// </summary>
    public sealed class MissionsScreen : MetaScreen
    {
        private sealed class MissionCard
        {
            public Card Card;
            public Image Ring;
            public Image Glow;
            public Image Icon;
            public Text Number;
            public Text Description;
            public ProgressBar Bar;
            public Text Count;
            public Text Reward;
            public Image RewardPlate;
            public bool WasCompleted;
            public bool Bound;
        }

        private readonly List<MissionCard> _cards = new List<MissionCard>();

        private ScrollList _list;
        private RingProgress _rankRing;
        private Text _rankTitle;
        private Text _bonus;
        private Text _rankHint;
        private Text _rankReward;
        private int _lastRank = -1;

        // Header + rank card, then the mission cards: two steps for the build queue (UIScreen.BuildSteps).
        protected override IEnumerator BuildSteps()
        {
            Transition = ScreenTransition.SlideUp;
            RectTransform r = Rect;
            MetaUI.Dimmer(r, 0.72f);
            RectTransform column = UIFactory.ContentColumn(r, 1000f, 32f);
            NeonTitle title;
            MetaUI.Header(column, Loc.T("missions.title"), MetaUI.CloseTop, out title);

            _list = UIFactory.ScrollList(column, UITheme.SpaceMd, UIFactory.Padding(32, 32, 24, 80), 40f);
            UIFactory.Stretch(_list.Rect, -32f, MetaUI.HeaderBottom, -32f, 0f);
            RectTransform c = _list.Content;

            BuildRankCard(c);
            yield return null;
            MetaUI.Section(c, Loc.T("missions.active"), IconId.Missions);
            for (int i = 0; i < MissionSystem.ActiveCount; i++)
            {
                _cards.Add(BuildMissionCard(c, i));
            }

            Text tip = UIFactory.Label(c, Loc.T("missions.tip", ("pct", Mathf.RoundToInt(ProfileService.ScoreBonusPerRank * 100f))),
                UITheme.TextSmall, UITheme.TextMuted, TextAnchor.MiddleCenter, UIFont.Body);
            UIFactory.SetLayout(tip, -1f, -1f);
        }

        private void BuildRankCard(RectTransform parent)
        {
            Card card = UIFactory.Card(parent, null, IconId.None, UITheme.Gold, 36f);
            RectTransform row = UIFactory.CreateRect(card.Body, "RankRow");
            HorizontalLayoutGroup h = UIFactory.HStack(row, UITheme.SpaceLg, null, TextAnchor.MiddleLeft, true);
            h.childForceExpandWidth = false;

            RectTransform ringHost = UIFactory.CreateRect(row, "RingHost");
            UIFactory.SetLayout(ringHost, 200f, 200f);
            Image glow = UIFactory.CreateImage(ringHost, UISprites.Glow, UITheme.Alpha(UITheme.Gold, 0.35f), "Glow");
            UIFactory.Stretch(glow.rectTransform, -50f, -50f, -50f, -50f);
            _rankRing = UIFactory.RingProgress(ringHost, 200f, 0f, 16f, UITheme.Gold);
            UIFactory.Stretch(_rankRing.Rect);
            _rankRing.Label.font = UIFonts.Title;
            _rankRing.Label.fontSize = 84;
            _rankRing.Label.color = UITheme.TextPrimary;

            RectTransform texts = UIFactory.CreateRect(row, "Texts");
            VerticalLayoutGroup v = UIFactory.VStack(texts, 6f, null, TextAnchor.MiddleLeft, true);
            v.childForceExpandWidth = true;
            UIFactory.SetLayout(texts, 0f, -1f, 1f);
            _rankTitle = UIFactory.LabelLine(texts, Loc.T("missions.rank_title", ("rank", 1)), UITheme.TextH3, UITheme.TextPrimary, TextAnchor.MiddleLeft, UIFont.Title, TextGlow.Neon);
            _bonus = UIFactory.LabelLine(texts, Loc.T("missions.bonus", ("pct", 0)), UITheme.TextLead, UITheme.Success, TextAnchor.MiddleLeft, UIFont.Bold, TextGlow.Shadow);
            _rankHint = UIFactory.Label(texts, Loc.T("missions.rank_hint_short"), UITheme.TextSmall, UITheme.TextSecondary, TextAnchor.MiddleLeft, UIFont.Body);
            RectTransform rewardRow = UIFactory.CreateRect(texts, "Reward");
            HorizontalLayoutGroup rh = UIFactory.HStack(rewardRow, 10f, null, TextAnchor.MiddleLeft, true);
            rh.childForceExpandWidth = false;
            UIFactory.SetLayout(rewardRow, -1f, 60f);
            UIFactory.LabelLine(rewardRow, Loc.T("missions.rank_bonus"), 32, UITheme.TextMuted, TextAnchor.MiddleLeft, UIFont.Bold);
            UIFactory.Icon(rewardRow, IconId.Gem, 44f, UITheme.GemColor);
            _rankReward = UIFactory.LabelLine(rewardRow, "+250", 44, UITheme.GemColor, TextAnchor.MiddleLeft, UIFont.Title, TextGlow.Shadow);
        }

        private static MissionCard BuildMissionCard(RectTransform parent, int index)
        {
            var m = new MissionCard();
            m.Card = UIFactory.Card(parent, null, IconId.None, null, 32f);
            RectTransform row = UIFactory.CreateRect(m.Card.Body, "Row");
            HorizontalLayoutGroup h = UIFactory.HStack(row, UITheme.SpaceMd, null, TextAnchor.MiddleLeft, true);
            h.childForceExpandWidth = false;

            RectTransform emblem = MetaUI.Emblem(row, 112f, UITheme.Accent, out m.Ring, out m.Glow);
            m.Icon = UIFactory.Icon(emblem, IconId.Missions, 56f, UITheme.TextPrimary);
            m.Number = UIFactory.LabelLine(emblem, (index + 1).ToString(MetaUI.Inv), 26, UITheme.TextMuted, TextAnchor.MiddleCenter, UIFont.Title);
            UIFactory.Anchor(m.Number.rectTransform, AnchorPreset.BottomCenter, new Vector2(0f, -30f));
            m.Number.rectTransform.sizeDelta = new Vector2(80f, 34f);

            RectTransform texts = UIFactory.CreateRect(row, "Texts");
            VerticalLayoutGroup v = UIFactory.VStack(texts, 10f, null, TextAnchor.MiddleLeft, true);
            v.childForceExpandWidth = true;
            UIFactory.SetLayout(texts, 0f, -1f, 1f);
            m.Description = UIFactory.Label(texts, string.Empty, 44, UITheme.TextPrimary, TextAnchor.MiddleLeft, UIFont.Bold);
            RectTransform progress = UIFactory.CreateRect(texts, "Progress");
            HorizontalLayoutGroup ph = UIFactory.HStack(progress, UITheme.SpaceSm, null, TextAnchor.MiddleLeft, true);
            ph.childForceExpandWidth = false;
            UIFactory.SetLayout(progress, -1f, 44f);
            m.Bar = UIFactory.ProgressBar(progress, 0f, 20f);
            UIFactory.SetLayout(m.Bar, 0f, 20f, 1f);
            m.Count = UIFactory.LabelLine(progress, "0 / 0", 34, UITheme.TextSecondary, TextAnchor.MiddleRight, UIFont.Bold);

            m.Reward = MetaUI.RewardChip(row, 0, out m.RewardPlate);
            return m;
        }

        protected override void OnShow()
        {
            base.OnShow();
            _list.ScrollToTop();
            if (!UITheme.ReducedMotion)
            {
                UIFactory.StaggerIn(_list.Content, 0.08f, 0.06f);
            }
        }

        protected override void Refresh(bool showing)
        {
            ProfileService p = Profile;
            PlayerProfile data = p.Profile;
            int rank = p.PilotRank;
            List<Mission> active = data.Missions != null ? data.Missions.Active : null;
            int count = active != null ? active.Count : 0;
            int done = 0;
            for (int i = 0; i < count; i++)
            {
                if (active[i] != null && active[i].Completed)
                {
                    done++;
                }
            }

            int total = Mathf.Max(1, count);
            _rankRing.Label.text = rank.ToString(MetaUI.Inv);
            _rankRing.SetValue(done / (float)total, true);
            if (!showing && _lastRank >= 0 && rank > _lastRank)
            {
                UITween.Punch(_rankRing.Rect, 0.2f, 0.5f);
            }

            _lastRank = rank;
            _rankTitle.text = Loc.T("missions.rank_title", ("rank", rank));
            _bonus.text = Loc.T("missions.bonus", ("pct", Mathf.RoundToInt(p.ScoreBonus * 100f)));
            _bonus.gameObject.SetActive(rank > 1);
            _rankHint.text = done >= total && count > 0
                ? Loc.T("missions.rank_ready")
                : Loc.T("missions.rank_hint", ("done", done), ("total", total));
            _rankReward.text = "+" + MetaUI.N(ProfileService.RankUpBaseReward + 50 * (rank - 1));

            for (int i = 0; i < _cards.Count; i++)
            {
                MissionCard card = _cards[i];
                Mission m = i < count ? active[i] : null;
                card.Card.gameObject.SetActive(m != null);
                if (m != null)
                {
                    BindMission(card, m, showing);
                }
            }
        }

        private static void BindMission(MissionCard card, Mission m, bool showing)
        {
            bool completed = m.Completed || m.Progress01 >= 1f;
            card.Description.text = MissionSystem.Describe(m);
            card.Bar.SetValue(completed ? 1f : m.Progress01, true, showing ? 0.8f : 0.45f);
            card.Count.text = completed ? Loc.T("missions.complete") : ProgressText(m);
            card.Count.color = completed ? UITheme.Success : UITheme.TextSecondary;
            card.Reward.text = "+" + MetaUI.N(m.Reward);

            Color accent = completed ? UITheme.Success : UITheme.Accent;
            card.Card.Panel.SetAccent(completed ? UITheme.Success : (Color?)null);
            card.Ring.color = accent;
            card.Glow.color = UITheme.Alpha(accent, completed ? 0.6f : 0.25f);
            UIFactory.SetIcon(card.Icon, completed ? IconId.Check : MissionIcon(m.MissionType));
            card.Icon.color = completed ? UITheme.Success : UITheme.TextPrimary;
            card.Number.gameObject.SetActive(!completed);
            card.RewardPlate.color = completed ? UITheme.Alpha(UITheme.Success, 0.25f) : UITheme.PanelGlassLight;
            if (completed)
            {
                card.Bar.SetColors(UITheme.Success, UITheme.Lighten(UITheme.Success, 0.3f));
            }
            else
            {
                card.Bar.SetColors(UITheme.AccentCyan, UITheme.AccentMagenta);
            }

            if (card.Bound && completed && !card.WasCompleted && !showing)
            {
                UITween.Punch(card.Card.Rect, 0.05f, 0.4f);
            }

            card.WasCompleted = completed;
            card.Bound = true;
        }

        private static string ProgressText(Mission m)
        {
            long target = System.Math.Max(0L, m.Target);
            long progress = System.Math.Min(System.Math.Max(0L, m.Progress), target);
            return MetaUI.N(progress) + " / " + MetaUI.N(target);
        }

        private static IconId MissionIcon(MissionType type)
        {
            switch (type)
            {
                case MissionType.GemsRun:
                case MissionType.GemsTotal:
                    return IconId.Gem;
                case MissionType.DistanceRun:
                case MissionType.DistanceTotal:
                    return IconId.Speed;
                case MissionType.NearMissesRun:
                case MissionType.NearMissesTotal:
                case MissionType.ChainRun:
                    return IconId.Hand;
                case MissionType.OverloadsRun:
                case MissionType.OverloadsTotal:
                    return IconId.Bolt;
                case MissionType.BridgesTotal:
                case MissionType.BridgesRun:
                case MissionType.SmashesTotal:
                    return IconId.Shield;
                case MissionType.CubesDodgedRun:
                case MissionType.CubesDodgedTotal:
                    return IconId.Hand;
                case MissionType.LevelsClearedRun:
                case MissionType.LevelsClearedTotal:
                    return IconId.Star;
                case MissionType.ScoreRun:
                case MissionType.MultiplierRun:
                    return IconId.Trophy;
                case MissionType.PowerUpsTotal:
                    return IconId.Magnet;
                case MissionType.BuildsRun:
                case MissionType.BuildsTotal:
                    return IconId.Bolt;
                case MissionType.PlayRuns:
                    return IconId.Play;
                default:
                    return IconId.Missions;
            }
        }
    }
}
