using System;
using System.Text;
using BeamRunner.Core.Localization;
using BeamRunner.Core.Meta;
using BeamRunner.Game;
using UnityEngine;

namespace BeamRunner.UI
{
    /// <summary>
    /// Main-menu entry of the Daily Run: a secondary "DAILY RUN" button (calendar icon) whose sub-label shows today's best,
    /// the streak and the countdown to the next track ("NEW TRACK IN 05:42"), with an attention halo and dot while today's
    /// track is still unflown. Hidden until the Daily Run unlocks (first flight). The screen that owns it calls
    /// <see cref="Refresh"/>; while visible it re-checks the countdown once a second on its own.
    /// </summary>
    public sealed class DailyRunCard : MonoBehaviour
    {
        /// <summary>Button label in the current language ("DAILY RUN" in English; what the test probe presses).</summary>
        public static string Label
        {
            get { return Loc.T("daily.button"); }
        }

        private const string Separator = "  |  ";

        private readonly StringBuilder _sb = new StringBuilder(64);
        private NeonButton _button;
        private Badge _badge;
        private GameController _game;
        private string _sub;
        private int _attention = -1;
        private float _nextTick;

        /// <summary>True while the entry is on screen (unlocked).</summary>
        public bool IsShown
        {
            get { return gameObject.activeSelf; }
        }

        /// <summary>The DAILY RUN button.</summary>
        public NeonButton Button
        {
            get { return _button; }
        }

        /// <summary>Builds the (hidden) entry bottom-centred <paramref name="bottom"/> units above <paramref name="parent"/>'s bottom.</summary>
        public static DailyRunCard Create(RectTransform parent, float bottom)
        {
            NeonButton button = UIFactory.Button(parent, Label, ButtonStyle.Secondary, null, IconId.Calendar, Loc.T("daily.sub_new_track"),
                UITheme.ButtonHeight, 560f);
            UIFactory.Anchor(button.Rect, AnchorPreset.BottomCenter, new Vector2(0f, bottom));
            DailyRunCard card = button.gameObject.AddComponent<DailyRunCard>();
            card._button = button;
            card._badge = UIFactory.Badge(button.Rect, 0);
            button.OnClick(card.OnClick);
            button.gameObject.SetActive(false);
            return card;
        }

        /// <summary>Shows/hides the entry and updates its texts from <paramref name="game"/>'s profile.</summary>
        public void Refresh(GameController game, bool animate)
        {
            _game = game;
            ProfileService p = game != null ? game.Profile : null;
            bool show = p != null && game.DailyRunAvailable;
            if (gameObject.activeSelf != show)
            {
                gameObject.SetActive(show);
                _attention = -1;
                if (show && animate && !UITheme.ReducedMotion)
                {
                    UIFactory.PopIn(transform);
                }
            }

            if (show)
            {
                UpdateTexts(p, animate);
            }
        }

        private void Update()
        {
            // The countdown changes by the minute; re-check once a second (menu only, cheap).
            if (Time.unscaledTime < _nextTick || _game == null || _game.Profile == null)
            {
                return;
            }

            _nextTick = Time.unscaledTime + 1f;
            UpdateTexts(_game.Profile, true);
        }

        private void UpdateTexts(ProfileService p, bool animate)
        {
            string sub = SubLabel(p);
            if (sub != _sub)
            {
                _sub = sub;
                _button.SetSubLabel(sub);
            }

            bool fresh = !p.DailyRunPlayedToday && p.DailyRunRecordable;
            int attention = fresh ? 1 : 0;
            if (attention != _attention)
            {
                _attention = attention;
                _button.SetAttention(fresh);
                _badge.SetCount(fresh ? -1 : 0, animate);
            }
        }

        private string SubLabel(ProfileService p)
        {
            if (!p.DailyRunRecordable)
            {
                return Loc.T("daily.sub_practice");
            }

            string countdown = Countdown(p.DailyRunTimeUntilReset);
            int streak = p.DailyRunStreak;
            _sb.Length = 0;
            if (p.DailyRunPlayedToday)
            {
                _sb.Append(Loc.T("daily.sub_best", ("score", p.DailyRunBestScoreToday)));
                if (streak > 0)
                {
                    _sb.Append(Separator).Append(Loc.T("daily.sub_streak", ("n", streak)));
                }

                _sb.Append(Separator).Append(Loc.T("daily.sub_next", ("time", countdown)));
            }
            else if (streak > 0)
            {
                _sb.Append(Loc.T("daily.sub_keep_streak", ("n", streak))).Append(Separator)
                    .Append(Loc.T("daily.sub_left", ("time", countdown)));
            }
            else
            {
                _sb.Append(Loc.T("daily.sub_same_track")).Append(Separator).Append(Loc.T("daily.sub_left", ("time", countdown)));
            }

            return _sb.ToString();
        }

        /// <summary>"HH:MM" left, rounded up to the minute (never shows 00:00 while time is left).</summary>
        public static string Countdown(TimeSpan left)
        {
            int minutes = (int)Math.Ceiling(Math.Max(0.0, left.TotalMinutes));
            return (minutes / 60).ToString("00", MetaUI.Inv) + ":" + (minutes % 60).ToString("00", MetaUI.Inv);
        }

        private void OnClick()
        {
            if (_game != null)
            {
                _game.StartDailyRun();
            }
        }
    }
}
