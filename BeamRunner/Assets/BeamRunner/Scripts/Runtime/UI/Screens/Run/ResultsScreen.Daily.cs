using BeamRunner.Core.Audio;
using BeamRunner.Core.Localization;
using BeamRunner.Core.Meta;
using UnityEngine;
using UnityEngine.UI;

namespace BeamRunner.UI
{
    /// <summary>
    /// Results, Daily Run part: a "DAILY RUN" card right under the score for Daily Runs (<see cref="RunSummary.Daily"/>)
    /// — "NEW DAILY BEST!" or today's best, attempts, streak, time to the next track, the first-run and streak-milestone
    /// rewards (also a "Daily run" row in the gems card), or why the run was not recorded.
    /// </summary>
    public sealed partial class ResultsScreen
    {
        private RectTransform _dailySlot;
        private RectTransform _dailyHeadline;

        /// <summary>True while the results are visible with their Daily Run panel (tests / diagnostics).</summary>
        public bool DailyPanelShown
        {
            get { return IsVisible && _dailySlot != null && _dailySlot.gameObject.activeSelf; }
        }

        private void BuildDaily(RunSummary s)
        {
            ClearSlot(_dailySlot);
            _dailyHeadline = null;
            DailyRunOutcome d = s != null ? s.Daily : null;
            if (d == null)
            {
                return;
            }

            bool best = d.Counted && d.NewDailyBest;
            Card card = UIFactory.Card(_dailySlot, Loc.T("daily.card_title"), IconId.Calendar, best ? UITheme.Gold : UITheme.AccentCyan);
            RectTransform body = card.Body;

            if (!d.Counted)
            {
                UIFactory.Label(body, d.ClockRejected ? Loc.T("daily.not_recorded") : Loc.T("daily.too_short"), UITheme.TextH3, UITheme.Warning,
                    TextAnchor.MiddleLeft, UIFont.Title, TextGlow.Shadow);
                string why = d.ClockRejected
                    ? Loc.T("daily.clock_behind")
                    : Loc.T("daily.min_distance", ("n", (int)DailyRuns.MinCountedDistance));
                UIFactory.Label(body, why, UITheme.TextBody, UITheme.TextSecondary, TextAnchor.MiddleLeft);
            }
            else
            {
                Text headline = UIFactory.Label(body, best ? Loc.T("daily.new_best") : Loc.T("daily.best_score", ("score", d.DailyBestScore)),
                    UITheme.TextH3, best ? UITheme.Lighten(UITheme.Gold, 0.25f) : UITheme.TextPrimary, TextAnchor.MiddleLeft, UIFont.Title,
                    best ? TextGlow.Neon : TextGlow.Shadow);
                _dailyHeadline = headline.rectTransform;
            }

            KeyValueGrid grid = UIFactory.KeyValueGrid(body, 2, 52);
            grid.Add(Loc.T("daily.today_best"), d.DailyBestScore > 0 ? RunUi.Grouped(d.DailyBestScore) : "-", UITheme.Gold);
            grid.Add(Loc.T("daily.attempts_caption"), RunUi.Grouped(d.Attempts));
            grid.Add(Loc.T("daily.streak_caption"), Loc.T("daily.days", ("n", d.Streak)), UITheme.AccentMagenta);
            string next = _game != null && _game.Profile != null ? DailyRunCard.Countdown(_game.Profile.DailyRunTimeUntilReset) : "-";
            grid.Add(Loc.T("daily.next_track_caption"), next);

            if (d.FirstRunReward > 0)
            {
                DailyRewardRow(body, IconId.Calendar, Loc.T("daily.first_run_reward"), d.FirstRunReward);
            }

            if (d.StreakReward > 0)
            {
                DailyRewardRow(body, IconId.Star, Loc.T("daily.streak_bonus", ("n", d.StreakMilestone)), d.StreakReward);
            }
            else if (d.Counted && d.Streak > 0)
            {
                int milestone = DailyRuns.NextStreakMilestone(d.Streak);
                UIFactory.Label(body, Loc.T("daily.next_bonus_hint", ("n", milestone)),
                    UITheme.TextSmall, UITheme.TextMuted, TextAnchor.MiddleLeft);
            }

            _dailySlot.gameObject.SetActive(true);
        }

        private static void DailyRewardRow(RectTransform body, IconId icon, string label, int amount)
        {
            RectTransform row = UIFactory.HStack(body, UITheme.SpaceSm, UIFactory.Padding(0, 0, 6, 6), TextAnchor.MiddleLeft)
                .GetComponent<RectTransform>();
            row.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
            UIFactory.Icon(row, icon, 48f, UITheme.Success);
            Text t = UIFactory.Label(row, label, UITheme.TextBody, UITheme.TextPrimary, TextAnchor.MiddleLeft, UIFont.Bold);
            UIFactory.SetLayout(t, 0f, -1f, 1f);
            RewardTag(row, amount);
        }

        /// <summary>Reveals the Daily Run card at <paramref name="t"/>; returns the time for the next section.</summary>
        private float RevealDaily(float t)
        {
            if (_dailySlot == null || !_dailySlot.gameObject.activeSelf)
            {
                return t;
            }

            RevealSection(_dailySlot, t, 0.5f);
            DailyRunOutcome d = _summary != null ? _summary.Daily : null;
            if (d != null && d.Counted && (d.NewDailyBest || d.TotalReward > 0))
            {
                Sfx(SfxId.Reward, t + 0.1f);
                RectTransform headline = _dailyHeadline;
                UITween.Delay(this, t + 0.35f, () =>
                {
                    if (headline != null && !UITheme.ReducedMotion)
                    {
                        UITween.Punch(headline, 0.18f, 0.45f);
                    }
                });
            }

            return t + 0.35f;
        }
    }
}
