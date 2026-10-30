using BeamRunner.Core.Localization;
using BeamRunner.Core.Meta;
using BeamRunner.Core.Sim;
using BeamRunner.Game;
using UnityEngine;
using UnityEngine.UI;

namespace BeamRunner.UI
{
    /// <summary>
    /// Crash "continue?" offer: a draining ring (<see cref="GameController.ReviveOfferRemaining"/>), WATCH AD (when a
    /// rewarded ad is ready), a gem revive (◆ icon + cost; greyed "NEED" when the wallet is short) and NO THANKS.
    /// Buttons lock while an ad is on screen. Back declines. Shown by <see cref="GameUI"/> while the offer is active.
    /// </summary>
    public sealed class ReviveScreen : UIScreen
    {
        private GameController _game;
        private RectTransform _content;
        private RectTransform _ringRoot;
        private RingProgress _ring;
        private Text _seconds;
        private Text _score;
        private NeonButton _adButton;
        private NeonButton _gemButton;
        private NeonButton _declineButton;
        private Text _wallet;
        private bool _adPending;
        private bool _adShown = true;
        private int _gemState = -1;
        private int _lastSecond = -1;
        private float _ringShown = -1f;
        private bool _locked;
        private int _shownFrame = -1;
        private long _walletShown = -1;

        /// <summary>Connects the screen to the controller (call once, right after creation).</summary>
        public void Bind(GameController game)
        {
            _game = game;
        }

        protected override void Build()
        {
            Transition = ScreenTransition.Zoom;
            TransitionDuration = 0.32f;

            RunUi.FullBleed(Rect, new Color(0.02f, 0f, 0.04f, 0.5f), true);

            RectTransform column = UIFactory.ContentColumn(Rect, 860f, UITheme.ScreenPadding);
            _content = UIFactory.CreateRect(column, "Content");
            UIFactory.Anchor(_content, AnchorPreset.StretchMiddle, new Vector2(0f, 40f));
            UIFactory.VStack(_content, UITheme.SpaceLg, null, TextAnchor.UpperCenter, true);
            UIFactory.FitContent(_content, false, true);
            // ~1180 units tall: shrink to fit on 1080-unit landscape canvases (else NO THANKS / the title are cut off).
            UIFitHeight.Attach(column, _content);

            UIFactory.NeonTitle(_content, Loc.T("revive.title"), 120);

            _ringRoot = UIFactory.CreateRect(_content, "Ring");
            UIFactory.SetLayout(_ringRoot, -1f, 340f);
            _ring = UIFactory.RingProgress(_ringRoot, 300f, 1f, 20f);
            UIFactory.Anchor(_ring.Rect, AnchorPreset.Center);
            _ring.Label.text = string.Empty;
            _seconds = UIFactory.LabelLine(_ring.Rect, "6", 120, UITheme.TextPrimary, TextAnchor.MiddleCenter, UIFont.Title, TextGlow.Neon);
            UIFactory.Stretch(_seconds.rectTransform);

            _score = UIFactory.LabelLine(_content, string.Empty, UITheme.TextLead, UITheme.TextSecondary, TextAnchor.MiddleCenter, UIFont.Bold, TextGlow.Shadow);
            UIFactory.SetLayout(_score, -1f, 64f);

            RectTransform buttons = UIFactory.VStack(_content, UITheme.SpaceMd, UIFactory.Padding(40, 40, 16, 0), TextAnchor.UpperCenter)
                .GetComponent<RectTransform>();
            _adButton = UIFactory.Button(buttons, Loc.T("revive.watch_ad"), ButtonStyle.Primary, OnWatchAd, IconId.Ad, Loc.T("revive.free_continue"));
            _gemButton = UIFactory.Button(buttons, RunUi.Grouped(ProfileService.ReviveGemCost), ButtonStyle.Secondary, OnGems, IconId.Gem);
            _declineButton = UIFactory.Button(buttons, Loc.T("common.no_thanks"), ButtonStyle.Ghost, OnDecline);

            RectTransform walletRow = UIFactory.HStack(_content, UITheme.SpaceXs, null, TextAnchor.MiddleCenter).GetComponent<RectTransform>();
            walletRow.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
            UIFactory.SetLayout(walletRow, -1f, 48f);
            RunUi.Caption(walletRow, Loc.T("store.you_have"));
            UIFactory.Icon(walletRow, IconId.Gem, 30f, UITheme.GemColor);
            _wallet = UIFactory.LabelLine(walletRow, "0", UITheme.TextCaption, UITheme.GemColor, TextAnchor.MiddleLeft, UIFont.Bold);

            DefaultSelection = _adButton.gameObject;
        }

        protected override void OnShow()
        {
            _shownFrame = Time.frameCount;
            _adPending = false;
            _locked = false;
            _gemState = -1;
            _lastSecond = -1;
            _ringShown = -1f;
            _walletShown = -1;
            _adShown = true;

            RunSimulation run = _game != null ? _game.Run : null;
            _score.text = run != null ? Loc.T("revive.score") + "  " + RunUi.Grouped(run.Score) : string.Empty;

            _ring.SetValue(1f, false);
            UpdateState(true);

            UIFactory.StaggerIn(_content, 0.05f, 0.06f);
            UITween.Scale(_ringRoot, 0.4f, 1f, 0.5f).SetDelay(0.08f).SetEase(UIEase.OutBack);
            DefaultSelection = _adShown ? _adButton.gameObject : _gemButton.gameObject;
        }

        /// <summary>Back declines the offer (ignored on the frame it opened or while an ad plays).</summary>
        public override bool OnBack()
        {
            if (Time.frameCount <= _shownFrame + 1 || _adPending || AdShowing())
            {
                return true;
            }

            UIFactory.PlayClickFeedback();
            OnDecline();
            return true;
        }

        private void Update()
        {
            if (_game == null || !IsVisible)
            {
                return;
            }

            UpdateState(false);
        }

        private void UpdateState(bool force)
        {
            float total = GameController.ReviveOfferSeconds;
            float remaining = Mathf.Max(0f, _game.ReviveOfferRemaining);
            float v = total > 0f ? Mathf.Clamp01(remaining / total) : 0f;
            if (force || Mathf.Abs(v - _ringShown) > 0.002f)
            {
                _ringShown = v;
                _ring.SetValue(v, false);
            }

            int second = Mathf.CeilToInt(remaining);
            if (second != _lastSecond)
            {
                bool tick = _lastSecond > 0 && !force;
                _lastSecond = second;
                _seconds.text = RunUi.Small(Mathf.Clamp(second, 0, 99));
                _seconds.color = second <= 2 ? UITheme.DangerAlt : UITheme.TextPrimary;
                if (tick)
                {
                    UITween.Punch(_seconds.rectTransform, 0.28f, 0.35f);
                }
            }

            bool showing = AdShowing() || _adPending;
            bool locked = showing;
            bool adAvailable = _game.CanReviveWithAd || _adPending;
            if (force || adAvailable != _adShown)
            {
                _adShown = adAvailable;
                _adButton.gameObject.SetActive(adAvailable);
            }

            int gemState = _game.CanReviveWithGems ? 1 : 0;
            if (force || gemState != _gemState)
            {
                _gemState = gemState;
                string cost = RunUi.Grouped(_game.ReviveGemCost);
                _gemButton.SetLabel(gemState == 1 ? cost : Loc.T("revive.need", ("cost", cost)));
                _gemButton.SetSubLabel(gemState == 1 ? Loc.T("revive.spend_gems") : null);
            }

            if (force || locked != _locked)
            {
                _locked = locked;
                _adButton.SetInteractable(!locked);
                _declineButton.SetInteractable(!locked);
            }

            _gemButton.SetInteractable(!locked && gemState == 1);

            long wallet = _game.Profile != null ? _game.Profile.Gems : 0;
            if (wallet != _walletShown)
            {
                _walletShown = wallet;
                _wallet.text = RunUi.Grouped(wallet);
            }
        }

        private bool AdShowing()
        {
            return _game != null && _game.Ads != null && _game.Ads.IsShowing;
        }

        private void OnWatchAd()
        {
            if (_game == null || _adPending || AdShowing() || !_game.ReviveOfferActive)
            {
                return;
            }

            _adPending = true;
            _game.ReviveWithAd(OnAdDone);
        }

        private void OnAdDone(bool completed)
        {
            _adPending = false;
            if (!completed && GameUI.Instance != null && _game != null && _game.State == GameState.Crashed)
            {
                GameUI.Instance.Toast(Loc.T("revive.ad_not_completed"), IconId.Ad);
            }
        }

        private void OnGems()
        {
            if (_game == null || _adPending || AdShowing())
            {
                return;
            }

            if (!_game.ReviveWithGems())
            {
                UITween.Shake(_gemButton.Visual, 16f, 0.35f);
            }
        }

        private void OnDecline()
        {
            if (_game == null || _adPending || AdShowing())
            {
                return;
            }

            _game.DeclineRevive();
        }
    }
}
