using BeamRunner.Core.Localization;
using BeamRunner.Core.Sim;
using BeamRunner.Game;
using UnityEngine;
using UnityEngine.UI;

namespace BeamRunner.UI
{
    /// <summary>
    /// The level wall checkpoint: "LEVEL n CLEAR", the run so far (score, gems, distance), three perk buttons (one pick
    /// continues into the next level) and, when a rewarded ad is ready, TAKE ALL THREE. Back picks the first perk.
    /// Shown by <see cref="GameUI"/> in <see cref="GameState.Checkpoint"/>.
    /// </summary>
    public sealed class CheckpointScreen : UIScreen
    {
        private GameController _game;
        private RectTransform _content;
        private NeonTitle _title;
        private Text _stats;
        private readonly NeonButton[] _perks = new NeonButton[GameController.CheckpointChoices];
        private NeonButton _adButton;
        private bool _adPending;
        private int _shownFrame = -1;

        /// <summary>Connects the screen to the controller (call once, right after creation).</summary>
        public void Bind(GameController game)
        {
            _game = game;
        }

        protected override void Build()
        {
            Transition = ScreenTransition.Zoom;
            TransitionDuration = 0.3f;

            RunUi.FullBleed(Rect, new Color(0.02f, 0f, 0.04f, 0.55f), true);

            RectTransform column = UIFactory.ContentColumn(Rect, 900f, UITheme.ScreenPadding);
            _content = UIFactory.CreateRect(column, "Content");
            UIFactory.Anchor(_content, AnchorPreset.StretchMiddle, new Vector2(0f, 20f));
            UIFactory.VStack(_content, UITheme.SpaceMd, null, TextAnchor.UpperCenter, true);
            UIFactory.FitContent(_content, false, true);
            UIFitHeight.Attach(column, _content);

            _title = UIFactory.NeonTitle(_content, Loc.T("hud.level_clear", ("n", 1)), 96);
            _stats = UIFactory.LabelLine(_content, string.Empty, UITheme.TextLead, UITheme.TextSecondary, TextAnchor.MiddleCenter,
                UIFont.Bold, TextGlow.Shadow);
            UIFactory.SetLayout(_stats, -1f, 64f);

            Text pick = UIFactory.LabelLine(_content, Loc.T("checkpoint.pick"), 44, UITheme.TextPrimary, TextAnchor.MiddleCenter,
                UIFont.Title, TextGlow.Neon);
            UIFactory.SetLayout(pick, -1f, 70f);

            RectTransform buttons = UIFactory.VStack(_content, UITheme.SpaceMd, UIFactory.Padding(24, 24, 8, 0), TextAnchor.UpperCenter)
                .GetComponent<RectTransform>();
            for (int i = 0; i < _perks.Length; i++)
            {
                int index = i;
                _perks[i] = UIFactory.Button(buttons, string.Empty, ButtonStyle.Secondary, () => OnPick(index), IconId.Plus,
                    string.Empty);
            }

            _adButton = UIFactory.Button(buttons, Loc.T("checkpoint.take_all"), ButtonStyle.Primary, OnAd, IconId.Ad,
                Loc.T("checkpoint.take_all_sub"));
            DefaultSelection = _perks[0].gameObject;
        }

        protected override void OnShow()
        {
            _shownFrame = Time.frameCount;
            _adPending = false;
            if (_game == null)
            {
                return;
            }

            _title.SetText(Loc.T("hud.level_clear", ("n", _game.CheckpointLevel)));
            RunSimulation run = _game.Run;
            _stats.text = run != null
                ? Loc.T("checkpoint.stats", ("score", RunUi.Grouped(run.Score)), ("gems", RunUi.Grouped(run.Gems)),
                    ("distance", RunUi.Grouped((long)run.Distance)))
                : string.Empty;

            CheckpointPerk[] perks = _game.CheckpointPerks;
            for (int i = 0; i < _perks.Length; i++)
            {
                CheckpointPerk p = perks[i];
                _perks[i].SetLabel(Loc.T(NameKey(p)));
                _perks[i].SetSubLabel(Loc.T(DescriptionKey(p)));
                _perks[i].SetIcon(Icon(p));
                _perks[i].SetInteractable(true);
            }

            _adButton.gameObject.SetActive(_game.CanTakeAllPerksWithAd);
            _adButton.SetInteractable(true);
            UIFactory.StaggerIn(_content, 0.05f, 0.05f);
        }

        /// <summary>Back takes the first perk (ignored on the frame it opened or while an ad plays).</summary>
        public override bool OnBack()
        {
            if (Time.frameCount <= _shownFrame + 1 || _adPending)
            {
                return true;
            }

            OnPick(0);
            return true;
        }

        private void OnPick(int index)
        {
            if (_game == null || _adPending || Time.frameCount <= _shownFrame)
            {
                return;
            }

            _game.ChooseCheckpointPerk(index);
        }

        private void OnAd()
        {
            if (_game == null || _adPending)
            {
                return;
            }

            _adPending = true;
            for (int i = 0; i < _perks.Length; i++)
            {
                _perks[i].SetInteractable(false);
            }

            _adButton.SetInteractable(false);
            _game.TakeAllPerksWithAd(completed =>
            {
                _adPending = false;
                if (!completed && _game.State == GameState.Checkpoint)
                {
                    for (int i = 0; i < _perks.Length; i++)
                    {
                        _perks[i].SetInteractable(true);
                    }

                    _adButton.gameObject.SetActive(false);
                    if (GameUI.Instance != null)
                    {
                        GameUI.Instance.Toast(Loc.T("revive.ad_not_completed"), IconId.Ad);
                    }
                }
            });
        }

        // Literals so tools/check-strings.py sees every key used.
        private static string NameKey(CheckpointPerk perk)
        {
            switch (perk)
            {
                case CheckpointPerk.Refuel:
                    return "perk.refuel";
                case CheckpointPerk.Patch:
                    return "perk.patch";
                case CheckpointPerk.Shield:
                    return "perk.shield";
                case CheckpointPerk.Efficiency:
                    return "perk.efficiency";
                case CheckpointPerk.BigTank:
                    return "perk.big_tank";
                default:
                    return "perk.longer_boosts";
            }
        }

        private static string DescriptionKey(CheckpointPerk perk)
        {
            switch (perk)
            {
                case CheckpointPerk.Refuel:
                    return "perk.refuel_desc";
                case CheckpointPerk.Patch:
                    return "perk.patch_desc";
                case CheckpointPerk.Shield:
                    return "perk.shield_desc";
                case CheckpointPerk.Efficiency:
                    return "perk.efficiency_desc";
                case CheckpointPerk.BigTank:
                    return "perk.big_tank_desc";
                default:
                    return "perk.longer_boosts_desc";
            }
        }

        private static IconId Icon(CheckpointPerk perk)
        {
            switch (perk)
            {
                case CheckpointPerk.Refuel:
                case CheckpointPerk.BigTank:
                    return IconId.Fuel;
                case CheckpointPerk.Patch:
                    return IconId.Heart;
                case CheckpointPerk.Shield:
                    return IconId.Shield;
                case CheckpointPerk.Efficiency:
                    return IconId.Speed;
                default:
                    return IconId.Bolt;
            }
        }
    }
}
