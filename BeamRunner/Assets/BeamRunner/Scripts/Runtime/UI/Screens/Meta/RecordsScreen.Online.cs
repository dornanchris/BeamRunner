using BeamRunner.Core.Localization;
using BeamRunner.Services;
using UnityEngine;
using UnityEngine.UI;

namespace BeamRunner.UI
{
    /// <summary>
    /// Records, online part: a card at the top of the LEADERBOARD tab (the platform's leaderboards) and of the
    /// ACHIEVEMENTS tab (the platform's achievements), shown only while signed in to Google Play Games / Game Center and
    /// when the ids are configured. The screen refreshes when the sign-in state changes.
    /// </summary>
    public sealed partial class RecordsScreen
    {
        private bool _onlineSignedIn;

        protected override void OnBind()
        {
            base.OnBind();
            if (Game != null)
            {
                Game.OnlineChanged += OnOnlineChanged;
            }
        }

        protected override void OnDestroy()
        {
            if (Game != null)
            {
                Game.OnlineChanged -= OnOnlineChanged;
            }

            base.OnDestroy();
        }

        private void OnOnlineChanged()
        {
            // Only a sign-in change alters this screen (cloud-sync status changes would needlessly rebuild the list).
            bool signedIn = Game != null && Game.Online.IsSignedIn;
            if (signedIn == _onlineSignedIn)
            {
                return;
            }

            _onlineSignedIn = signedIn;
            if (IsVisible)
            {
                MarkDirty();
            }
        }

        /// <summary>The platform card for the LEADERBOARD (<paramref name="achievements"/> false) or ACHIEVEMENTS tab.</summary>
        private void BuildOnlineCard(RectTransform c, bool achievements)
        {
            IOnlineService online = Game != null ? Game.Online : null;
            if (online == null || !online.IsSignedIn)
            {
                return;
            }

            bool has = achievements
                ? online.HasAchievements
                : online.HasLeaderboard(LeaderboardKind.BestScore) || online.HasLeaderboard(LeaderboardKind.DailyRun);
            if (!has)
            {
                return;
            }

            Card card = UIFactory.Card(c, null, IconId.None, UITheme.AccentCyan, 28f);
            RectTransform row = UIFactory.CreateRect(card.Body, "Online");
            HorizontalLayoutGroup h = UIFactory.HStack(row, UITheme.SpaceMd, UIFactory.Padding(4, 4, 0, 0), TextAnchor.MiddleLeft, true);
            h.childForceExpandWidth = false;

            RectTransform texts = UIFactory.CreateRect(row, "Texts");
            VerticalLayoutGroup v = UIFactory.VStack(texts, 0f, null, TextAnchor.MiddleLeft, true);
            v.childForceExpandWidth = true;
            UIFactory.SetLayout(texts, 0f, -1f, 1f);
            UIFactory.LabelLine(texts, MetaUI.PlatformTitle(online), 34, UITheme.TextMuted, TextAnchor.MiddleLeft, UIFont.Bold);
            UIFactory.LabelLine(texts, achievements ? Loc.T("records.online_achievements_hint") : Loc.T("records.online_leaderboards_hint"), 36,
                UITheme.TextSecondary, TextAnchor.MiddleLeft, UIFont.Body);

            if (achievements)
            {
                UIFactory.Button(row, Loc.T("records.online_achievements"), ButtonStyle.Secondary, OnShowAchievements, IconId.Star);
            }
            else
            {
                UIFactory.Button(row, Loc.T("records.online_leaderboards"), ButtonStyle.Secondary, OnShowLeaderboards, IconId.Trophy);
            }
        }

        private void OnShowLeaderboards()
        {
            if (Game != null)
            {
                Game.ShowOnlineLeaderboards();
            }
        }

        private void OnShowAchievements()
        {
            if (Game != null)
            {
                Game.ShowOnlineAchievements();
            }
        }
    }
}
