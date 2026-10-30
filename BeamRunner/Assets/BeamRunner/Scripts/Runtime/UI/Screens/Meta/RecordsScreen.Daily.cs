using BeamRunner.Core.Localization;
using BeamRunner.Core.Meta;
using UnityEngine;

namespace BeamRunner.UI
{
    /// <summary>Records, Daily Run part: a "DAILY RUN" card on the STATS tab, right under PERSONAL BESTS.</summary>
    public sealed partial class RecordsScreen
    {
        private static void BuildDailyRunStats(RectTransform c, ProfileService p)
        {
            if (p == null || p.Profile == null)
            {
                return;
            }

            DailyRunState d = p.Profile.DailyRun ?? new DailyRunState();
            Card card = UIFactory.Card(c, Loc.T("daily.card_title"), IconId.Calendar, UITheme.AccentCyan);
            card.transform.SetSiblingIndex(Mathf.Min(1, c.childCount - 1)); // after PERSONAL BESTS

            bool today = p.DailyRunPlayedToday;
            KeyValueGrid g = UIFactory.KeyValueGrid(card.Body, 2, 60);
            g.Add(Loc.T("daily.today_best"), today ? MetaUI.N(p.DailyRunBestScoreToday) : "-", UITheme.Gold);
            g.Add(Loc.T("daily.today_distance"), today ? MetaUI.Meters(p.DailyRunBestDistanceToday) : "-");
            g.Add(Loc.T("daily.current_streak"), DaysText(p.DailyRunStreak), UITheme.AccentMagenta);
            g.Add(Loc.T("daily.best_streak"), DaysText(d.BestStreak));
            g.Add(Loc.T("daily.best_daily_score"), MetaUI.N(d.AllTimeBestScore), UITheme.Gold);
            g.Add(Loc.T("daily.days_flown"), MetaUI.N(d.DaysPlayed));

            UIFactory.StatRow(card.Body, Loc.T("daily.total_runs"), MetaUI.N(d.TotalRuns), IconId.Play);
            UIFactory.StatRow(card.Body, Loc.T("daily.attempts_today"), MetaUI.N(p.DailyRunAttemptsToday), IconId.Retry);
            UIFactory.StatRow(card.Body, Loc.T("daily.next_track_in"),
                Loc.T("daily.next_track_utc", ("time", DailyRunCard.Countdown(p.DailyRunTimeUntilReset))), IconId.Calendar, false);
        }

        private static string DaysText(int days)
        {
            return Loc.T("daily.days", ("n", days));
        }
    }
}
