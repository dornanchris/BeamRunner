using System;
using System.Collections.Generic;
using BeamRunner.Core.Localization;
using BeamRunner.Core.Meta;
using BeamRunner.Core.Sim;
using BeamRunner.Game;
using UnityEngine;
using UnityEngine.UI;

namespace BeamRunner.UI
{
    /// <summary>
    /// Pause menu over the dimmed HUD: "PAUSED", the run's score and distance so far, the three active missions with
    /// their live (projected) progress, and RESUME / RESTART / SETTINGS / MENU. Back resumes.
    /// Lives on the Popups layer so it draws above the HUD; <see cref="GameUI"/> makes it the stack root while paused.
    /// </summary>
    public sealed class PauseScreen : UIScreen
    {
        private sealed class MissionRow
        {
            public RectTransform Root;
            public Image Status;
            public Image StatusGlow;
            public Text Description;
            public Text Value;
            public ProgressBar Bar;
            public Text Reward;
        }

        private readonly MissionRow[] _rows = new MissionRow[MissionSystem.ActiveCount];

        private GameController _game;
        private Action _openSettings;
        private RectTransform _content;
        private Text _score;
        private Text _distance;
        private Card _missions;
        private NeonButton _resume;
        private NeonButton _restart;
        private NeonButton _settings;
        private NeonButton _menu;
        private int _shownFrame = -1;

        /// <summary>Connects the screen (call once after creation). <paramref name="openSettings"/> pushes the settings overlay.</summary>
        public void Bind(GameController game, Action openSettings)
        {
            _game = game;
            _openSettings = openSettings;
        }

        protected override void Build()
        {
            Transition = ScreenTransition.Zoom;
            TransitionDuration = 0.28f;

            RunUi.FullBleed(Rect, new Color(0.01f, 0f, 0.035f, 0.55f), true);

            RectTransform column = UIFactory.ContentColumn(Rect, 920f, UITheme.ScreenPadding);
            _content = UIFactory.CreateRect(column, "Content");
            UIFactory.Anchor(_content, AnchorPreset.StretchMiddle);
            UIFactory.VStack(_content, UITheme.SpaceLg, UIFactory.Padding(0, 0, 0, 0), TextAnchor.UpperCenter, true);
            UIFactory.FitContent(_content, false, true);
            // ~1450 units tall: shrink to fit on 1080-unit landscape canvases (else PAUSED / MENU end up off-screen).
            UIFitHeight.Attach(column, _content);

            UIFactory.NeonTitle(_content, Loc.T("pause.title"), 120);

            // Score / distance so far.
            RectTransform stats = UIFactory.HStack(_content, UITheme.SpaceXl, null, TextAnchor.MiddleCenter).GetComponent<RectTransform>();
            UIFactory.SetLayout(stats, -1f, 130f);
            _score = StatBlock(stats, Loc.T("pause.score"));
            Image sep = UIFactory.CreateImage(stats, null, new Color(1f, 1f, 1f, 0.16f), "Separator");
            UIFactory.SetLayout(sep, 2f, 96f);
            _distance = StatBlock(stats, Loc.T("pause.distance"));

            // Missions.
            _missions = UIFactory.Card(_content, Loc.T("pause.missions"), IconId.Missions);
            for (int i = 0; i < _rows.Length; i++)
            {
                _rows[i] = BuildMissionRow(_missions.Body, i < _rows.Length - 1);
            }

            // Buttons.
            // No side padding: RESTART + SETTINGS (icon + Audiowide 40) each need ~445 units; with the old 60-unit padding
            // the row was 800 wide, their min widths (923 total) overflowed it and the pair stuck out past the column.
            RectTransform buttons = UIFactory.VStack(_content, UITheme.SpaceMd, UIFactory.Padding(0, 0, 8, 0), TextAnchor.UpperCenter)
                .GetComponent<RectTransform>();
            _resume = UIFactory.Button(buttons, Loc.T("pause.resume"), ButtonStyle.Primary, OnResume, IconId.Play);
            RectTransform row = UIFactory.HStack(buttons, UITheme.SpaceMd, null, TextAnchor.MiddleCenter).GetComponent<RectTransform>();
            row.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = true;
            UIFactory.SetLayout(row, -1f, UITheme.ButtonHeight);
            _restart = UIFactory.Button(row, Loc.T("pause.restart"), ButtonStyle.Secondary, OnRestart, IconId.Retry, null, -1f, 0f);
            UIFactory.SetLayout(_restart, 0f, -1f, 1f).minWidth = 0f; // share the row equally, never overflow it
            _settings = UIFactory.Button(row, Loc.T("pause.settings"), ButtonStyle.Secondary, OnSettings, IconId.Settings, null, -1f, 0f);
            UIFactory.SetLayout(_settings, 0f, -1f, 1f).minWidth = 0f;
            _menu = UIFactory.Button(buttons, Loc.T("pause.menu"), ButtonStyle.Ghost, OnMenu, IconId.Home);

            DefaultSelection = _resume.gameObject;
        }

        protected override void OnShow()
        {
            _shownFrame = Time.frameCount;
            Refresh();
        }

        /// <summary>Back (Escape / Android back / gamepad B) resumes — except on the very frame the pause opened.</summary>
        public override bool OnBack()
        {
            if (Time.frameCount <= _shownFrame + 1 || Dialogs.IsOpen)
            {
                return true;
            }

            UIFactory.PlayClickFeedback();
            OnResume();
            return true;
        }

        /// <summary>Frame on which the screen was last shown (lets the flow owner ignore same-frame input).</summary>
        public int ShownFrame
        {
            get { return _shownFrame; }
        }

        private void Refresh()
        {
            RunSimulation run = _game != null ? _game.Run : null;
            _score.text = run != null ? RunUi.Grouped(run.Score) : "0";
            _distance.text = Loc.T("hud.level", ("n", run != null ? run.Level : 1)) + RunUi.Dot +
                             RunUi.Formatted("hud.distance", run != null ? (long)run.Distance : 0L);

            List<Mission> active = _game != null && _game.Profile != null ? _game.Profile.Profile.Missions.Active : null;
            RunResult live = run != null ? run.BuildResult() : null;
            int shown = 0;
            for (int i = 0; i < _rows.Length; i++)
            {
                MissionRow row = _rows[i];
                Mission m = active != null && i < active.Count ? active[i] : null;
                row.Root.gameObject.SetActive(m != null);
                if (m == null)
                {
                    continue;
                }

                shown++;
                long progress = live != null ? MissionSystem.ProjectedProgress(m, live) : m.Progress;
                bool done = m.Completed || progress >= m.Target;
                float p01 = m.Target <= 0 ? 1f : Mathf.Clamp01((float)progress / m.Target);
                row.Description.text = MissionSystem.Describe(m);
                row.Value.text = done ? Loc.T("pause.mission_done") : RunUi.MissionProgress(m, progress);
                row.Value.color = done ? UITheme.Success : UITheme.TextSecondary;
                row.Reward.text = RunUi.Grouped(m.Reward);
                row.Bar.SetValue(0f, false);
                row.Bar.SetValue(p01, true, 0.7f);
                if (done)
                {
                    row.Bar.SetColors(UITheme.Success, UITheme.Lighten(UITheme.Success, 0.3f));
                }
                else
                {
                    row.Bar.SetColors(UITheme.AccentAlt, UITheme.Accent);
                }

                UIFactory.SetIcon(row.Status, done ? IconId.Check : IconId.Missions);
                row.Status.color = done ? UITheme.Success : UITheme.TextMuted;
                row.StatusGlow.enabled = done;
            }

            _missions.gameObject.SetActive(shown > 0);

            bool enabled = _game != null;
            _resume.SetInteractable(enabled);
            _restart.SetInteractable(enabled);
            _settings.SetInteractable(enabled && _openSettings != null);
            _menu.SetInteractable(enabled);

            UIFactory.StaggerIn(_content, 0.04f, 0.05f);
        }

        private static Text StatBlock(Transform parent, string caption)
        {
            RectTransform block = UIFactory.VStack(parent, 0f, null, TextAnchor.MiddleCenter).GetComponent<RectTransform>();
            block.GetComponent<VerticalLayoutGroup>().childForceExpandWidth = false;
            UIFactory.SetLayout(block, 400f, 130f);
            RunUi.Caption(block, caption);
            Text value = UIFactory.LabelLine(block, "0", UITheme.TextH2, UITheme.TextPrimary, TextAnchor.MiddleCenter, UIFont.Title, TextGlow.Neon);
            UIFactory.SetLayout(value, -1f, 92f);
            return value;
        }

        private static MissionRow BuildMissionRow(Transform parent, bool divider)
        {
            var row = new MissionRow();
            RectTransform root = UIFactory.CreateRect(parent, "Mission");
            HorizontalLayoutGroup h = UIFactory.HStack(root, UITheme.SpaceMd, UIFactory.Padding(0, 0, 10, 10), TextAnchor.MiddleLeft, true);
            h.childForceExpandWidth = false;
            row.Root = root;

            RectTransform statusWrap = UIFactory.CreateRect(root, "Status");
            UIFactory.SetLayout(statusWrap, 64f, 64f);
            row.StatusGlow = UIFactory.CreateImage(statusWrap, UISprites.Glow, UITheme.Alpha(UITheme.Success, 0.5f), "Glow");
            UIFactory.Stretch(row.StatusGlow.rectTransform, -24f, -24f, -24f, -24f);
            row.Status = UIFactory.Icon(statusWrap, IconId.Missions, 52f, UITheme.TextMuted);
            UIFactory.Stretch(row.Status.rectTransform, 6f, 6f, 6f, 6f);

            RectTransform texts = UIFactory.VStack(root, UITheme.SpaceXs, null, TextAnchor.MiddleLeft).GetComponent<RectTransform>();
            UIFactory.SetLayout(texts, 0f, -1f, 1f);
            row.Description = UIFactory.Label(texts, string.Empty, UITheme.TextBody, UITheme.TextPrimary, TextAnchor.MiddleLeft, UIFont.Bold);

            RectTransform progressRow = UIFactory.HStack(texts, UITheme.SpaceSm, null, TextAnchor.MiddleLeft).GetComponent<RectTransform>();
            progressRow.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
            UIFactory.SetLayout(progressRow, -1f, 40f);
            row.Bar = UIFactory.ProgressBar(progressRow, 0f, 14f);
            UIFactory.SetLayout(row.Bar, 0f, 14f, 1f);
            row.Value = UIFactory.LabelLine(progressRow, string.Empty, UITheme.TextCaption, UITheme.TextSecondary, TextAnchor.MiddleRight, UIFont.Bold);
            UIFactory.SetLayout(row.Value, 230f, 40f); // "105,000 / 105,000" (high-rank score missions) is ~228 units

            RectTransform reward = UIFactory.HStack(root, 6f, null, TextAnchor.MiddleRight).GetComponent<RectTransform>();
            reward.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
            UIFactory.SetLayout(reward, 150f, 64f);
            UIFactory.Icon(reward, IconId.Gem, 36f, UITheme.GemColor);
            row.Reward = UIFactory.LabelLine(reward, "0", UITheme.TextBody, UITheme.GemColor, TextAnchor.MiddleLeft, UIFont.Bold);

            if (divider)
            {
                UIFactory.Divider(parent);
            }

            return row;
        }

        private void OnResume()
        {
            if (_game != null)
            {
                _game.ResumeRun();
            }
        }

        private void OnRestart()
        {
            if (_game != null)
            {
                _game.RestartRun();
            }
        }

        private void OnSettings()
        {
            if (_openSettings != null)
            {
                _openSettings();
            }
        }

        private void OnMenu()
        {
            Dialogs.Confirm(Loc.T("pause.quit_title"), Loc.T("pause.quit_body"), Loc.T("pause.quit_confirm"), Loc.T("quit.cancel"), leave =>
            {
                if (leave && _game != null && _game.State == GameState.Paused)
                {
                    _game.QuitToMenu();
                }
            }, false, IconId.Home);
        }
    }
}
