using System.Collections;
using System.Collections.Generic;
using BeamRunner.Core.Audio;
using BeamRunner.Core.Localization;
using BeamRunner.Core.Meta;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BeamRunner.UI
{
    /// <summary>
    /// Hangar over the 3D ship turntable: back / title / gems on top, and a glass ship card (bottom in portrait, right
    /// side in landscape) to browse <see cref="ShipCatalog.All"/> with ◀ ▶ or a horizontal swipe. The action button follows
    /// the ship's state: SELECT, SELECTED, BUY (with a "need more" state), UNLOCK or LOCKED with progress. Also hosts the
    /// gem store panel.
    /// </summary>
    public sealed class HangarScreen : MetaScreen, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private const float CardHeight = 790f;
        private const float SwipeThreshold = 110f;

        private enum ActionKind
        {
            None = 0,
            Select,
            Selected,
            Buy,
            Unlock,
            Locked,
        }

        private static bool s_openStoreOnShow;

        private RectTransform _top;
        private CurrencyPill _pill;
        private RectTransform _portraitHost;
        private RectTransform _landscapeHost;
        private GlassPanel _card;
        private RectTransform _cardRt;
        private CanvasGroup _cardGroup;

        private RectTransform _slide;
        private CanvasGroup _slideGroup;
        private Text _name;
        private Outline _nameHalo;
        private Text _tagline;

        private readonly List<Image> _dots = new List<Image>();
        private readonly List<LayoutElement> _dotLayouts = new List<LayoutElement>();

        private Image _statusIcon;
        private Text _statusText;
        private RectTransform _progressRow;
        private ProgressBar _progress;
        private Text _progressText;

        private RectTransform _actionSlot;
        private NeonButton _selectBtn;
        private NeonButton _selectedBtn;
        private NeonButton _buyBtn;
        private NeonButton _unlockBtn;
        private NeonButton _lockedBtn;
        private MetaPressable _buyCatcher;
        private MetaPressable _lockedCatcher;
        private NeonButton _storeBtn;
        private NeonButton _nextBtn;
        private int _storeHintTween;

        private GemStorePanel _store;

        private int _index;
        private ActionKind _action;
        private bool _dragging;
        private float _dragUnits;

        /// <summary>Asks the hangar to open the gem store the next time it is shown (e.g. from the menu's "+").</summary>
        public static void RequestStoreOnShow()
        {
            s_openStoreOnShow = true;
        }

        /// <summary>Opens the gem store panel.</summary>
        public void OpenStore()
        {
            if (_store != null && Game != null)
            {
                _store.Open(Game);
            }
        }

        protected override bool ListensToStore
        {
            get { return true; }
        }

        private ShipDefinition Current
        {
            get
            {
                IReadOnlyList<ShipDefinition> all = ShipCatalog.All;
                return all[Mathf.Clamp(_index, 0, all.Count - 1)];
            }
        }

        // ------------------------------------------------------------------ construction

        // Top bar, ship card, gem store: three steps for the build queue (UIScreen.BuildSteps).
        protected override IEnumerator BuildSteps()
        {
            Transition = ScreenTransition.Fade;
            TransitionDuration = 0.3f;
            RectTransform r = Rect;

            // Invisible full-screen hit area so swipes anywhere (over the 3D ship) browse; drags on children bubble here.
            RectTransform swipe = UIFactory.CreateFill(r, "SwipeArea");
            swipe.gameObject.AddComponent<UIRaycastTarget>();
            UIFactory.Stretch(swipe, -600f, -600f, -600f, -600f);

            MetaUI.Scrim(r, true, 420f, 0.72f);
            BuildTop(r);
            yield return null;

            _portraitHost = UIFactory.ContentColumn(r, 1000f, 32f);
            _portraitHost.name = "PortraitHost";
            _landscapeHost = UIFactory.CreateRect(r, "LandscapeHost");
            _landscapeHost.anchorMin = new Vector2(1f, 0f);
            _landscapeHost.anchorMax = new Vector2(1f, 1f);
            _landscapeHost.pivot = new Vector2(1f, 0.5f);
            _landscapeHost.sizeDelta = new Vector2(980f, 0f);
            _landscapeHost.anchoredPosition = new Vector2(-56f, 0f);

            BuildCard(_portraitHost);
            yield return null;
            _store = GemStorePanel.Create(r);
        }

        private void BuildTop(RectTransform r)
        {
            _top = UIFactory.CreateRect(r, "Top");
            UIFactory.Anchor(_top, AnchorPreset.StretchTop, new Vector2(0f, -24f));
            _top.sizeDelta = new Vector2(-64f, MetaUI.HeaderHeight);

            NeonTitle title = UIFactory.NeonTitle(_top, Loc.T("hangar.title"), UITheme.TextH2);
            UIFactory.Stretch(title.Rect, 290f, 0f, 290f, 0f);

            NeonButton back = UIFactory.IconButton(_top, IconId.Back, OnBackPressed, 120f);
            UIFactory.Anchor(back.Rect, AnchorPreset.MiddleLeft);

            _pill = UIFactory.CurrencyPill(_top, 0, IconId.Gem, null, OpenStore);
            UIFactory.Anchor(_pill.Rect, AnchorPreset.MiddleRight);
        }

        private void BuildCard(RectTransform host)
        {
            _card = UIFactory.GlassPanel(host, UITheme.Accent, UITheme.RadiusXl, 40f);
            _cardRt = _card.Rect;
            _cardGroup = _cardRt.gameObject.AddComponent<CanvasGroup>();
            RectTransform c = _card.Content;
            VerticalLayoutGroup v = UIFactory.VStack(c, 18f, null, TextAnchor.UpperCenter, true);
            v.childForceExpandWidth = true;

            // Name row: ◀ [name / tagline] ▶
            RectTransform header = UIFactory.CreateRect(c, "NameRow");
            HorizontalLayoutGroup hh = UIFactory.HStack(header, UITheme.SpaceSm, null, TextAnchor.MiddleCenter, true);
            hh.childForceExpandWidth = false;
            UIFactory.SetLayout(header, -1f, 210f);
            NeonButton prev = UIFactory.IconButton(header, IconId.ChevronLeft, () => Step(-1), 112f);
            RectTransform nameBlock = UIFactory.CreateRect(header, "NameBlock");
            UIFactory.SetLayout(nameBlock, 0f, 210f, 1f);
            NeonButton next = UIFactory.IconButton(header, IconId.ChevronRight, () => Step(1), 112f);
            _nextBtn = next;

            _slide = UIFactory.CreateFill(nameBlock, "Slide");
            _slideGroup = _slide.gameObject.AddComponent<CanvasGroup>();
            _slideGroup.blocksRaycasts = false;
            _name = UIFactory.LabelLine(_slide, string.Empty, UITheme.TextH2, UITheme.TextPrimary, TextAnchor.MiddleCenter, UIFont.Title, TextGlow.Neon);
            UIFactory.Anchor(_name.rectTransform, AnchorPreset.StretchTop, new Vector2(0f, -8f));
            _name.rectTransform.sizeDelta = new Vector2(0f, 100f);
            _nameHalo = _name.GetComponent<Outline>();
            _tagline = UIFactory.Label(_slide, string.Empty, 36, UITheme.TextSecondary, TextAnchor.UpperCenter, UIFont.Body, TextGlow.Shadow);
            UIFactory.Anchor(_tagline.rectTransform, AnchorPreset.StretchTop, new Vector2(0f, -108f));
            _tagline.rectTransform.sizeDelta = new Vector2(0f, 96f);

            // Page dots
            RectTransform dots = UIFactory.CreateRect(c, "Dots");
            HorizontalLayoutGroup dh = UIFactory.HStack(dots, 14f, null, TextAnchor.MiddleCenter, true);
            dh.childForceExpandWidth = false;
            UIFactory.SetLayout(dots, -1f, 22f);
            IReadOnlyList<ShipDefinition> ships = ShipCatalog.All;
            for (int i = 0; i < ships.Count; i++)
            {
                Image dot = UIFactory.CreateImage(dots, null, new Color(1f, 1f, 1f, 0.25f), "Dot" + i);
                UIShape.MakePill(dot);
                LayoutElement le = UIFactory.SetLayout(dot, 18f, 18f);
                le.minWidth = 18f;
                _dots.Add(dot);
                _dotLayouts.Add(le);
            }

            // Status: [icon] text, optional progress bar
            RectTransform status = UIFactory.CreateRect(c, "Status");
            VerticalLayoutGroup sv = UIFactory.VStack(status, 10f, UIFactory.Padding(24, 24, 0, 0), TextAnchor.MiddleCenter, true);
            sv.childForceExpandWidth = true;
            UIFactory.SetLayout(status, -1f, 126f);
            RectTransform line = UIFactory.CreateRect(status, "Line");
            HorizontalLayoutGroup lh = UIFactory.HStack(line, 12f, null, TextAnchor.MiddleCenter, true);
            lh.childForceExpandWidth = false;
            UIFactory.SetLayout(line, -1f, 60f);
            _statusIcon = UIFactory.Icon(line, IconId.Check, 44f, UITheme.Success);
            _statusText = UIFactory.LabelLine(line, string.Empty, 40, UITheme.TextPrimary, TextAnchor.MiddleLeft, UIFont.Bold, TextGlow.Shadow);

            _progressRow = UIFactory.CreateRect(status, "Progress");
            HorizontalLayoutGroup ph = UIFactory.HStack(_progressRow, UITheme.SpaceSm, null, TextAnchor.MiddleCenter, true);
            ph.childForceExpandWidth = false;
            UIFactory.SetLayout(_progressRow, -1f, 48f);
            _progress = UIFactory.ProgressBar(_progressRow, 0f, 22f);
            UIFactory.SetLayout(_progress, 0f, 22f, 1f);
            _progressText = UIFactory.LabelLine(_progressRow, string.Empty, 34, UITheme.TextSecondary, TextAnchor.MiddleRight, UIFont.Bold);

            // Action slot (one button visible at a time)
            _actionSlot = UIFactory.CreateRect(c, "Action");
            UIFactory.SetLayout(_actionSlot, -1f, UITheme.ButtonHeightLarge + 8f);
            _selectBtn = ActionButton(Loc.T("hangar.select"), ButtonStyle.Primary, OnSelect, IconId.Hangar);
            _selectedBtn = ActionButton(Loc.T("hangar.selected"), ButtonStyle.Secondary, OnSelectedTapped, IconId.Check);
            _selectedBtn.SetColors(UITheme.Success, UITheme.Success);
            _buyBtn = ActionButton(Loc.T("common.buy"), ButtonStyle.Primary, OnBuy, IconId.Gem);
            _unlockBtn = ActionButton(Loc.T("hangar.unlock"), ButtonStyle.Primary, OnUnlock, IconId.Star);
            _lockedBtn = ActionButton(Loc.T("hangar.locked"), ButtonStyle.Secondary, null, IconId.Lock);
            _lockedBtn.SetInteractable(false);
            _buyCatcher = MetaPressable.Catcher(_buyBtn.Rect, OnBuyDenied);
            _lockedCatcher = MetaPressable.Catcher(_lockedBtn.Rect, OnLockedTapped);

            // Store entry
            RectTransform storeRow = UIFactory.CreateRect(c, "StoreRow");
            HorizontalLayoutGroup sh = UIFactory.HStack(storeRow, 0f, null, TextAnchor.MiddleCenter, true);
            sh.childForceExpandWidth = false;
            UIFactory.SetLayout(storeRow, -1f, UITheme.ButtonHeightSmall);
            _storeBtn = UIFactory.Button(storeRow, Loc.T("store.title"), ButtonStyle.Secondary, OpenStore, IconId.Cart, null, UITheme.ButtonHeightSmall, 0f);
        }

        private NeonButton ActionButton(string label, ButtonStyle style, System.Action onClick, IconId icon)
        {
            NeonButton b = UIFactory.Button(_actionSlot, label, style, onClick, icon, null, UITheme.ButtonHeightLarge, 560f);
            UIFactory.Anchor(b.Rect, AnchorPreset.Center);
            b.gameObject.SetActive(false);
            return b;
        }

        // ------------------------------------------------------------------ lifecycle

        protected override void ApplyLayout(bool landscape)
        {
            RectTransform host = landscape ? _landscapeHost : _portraitHost;
            if (_cardRt.parent != host)
            {
                _cardRt.SetParent(host, false);
            }

            if (landscape)
            {
                UIFactory.Anchor(_cardRt, AnchorPreset.StretchMiddle, new Vector2(0f, -70f));
                _cardRt.sizeDelta = new Vector2(0f, CardHeight);
            }
            else
            {
                UIFactory.Anchor(_cardRt, AnchorPreset.StretchBottom, new Vector2(0f, 32f));
                _cardRt.sizeDelta = new Vector2(0f, CardHeight);
            }

            _store.transform.SetAsLastSibling();
        }

        protected override void OnShow()
        {
            _dragging = false;
            base.OnShow();
            if (!UITheme.ReducedMotion)
            {
                UITween.Fade(_cardGroup, 0f, 1f, 0.35f).SetDelay(0.05f).SetEase(UIEase.OutCubic);
                UITween.Scale(_cardRt, 0.94f, 1f, 0.45f).SetDelay(0.05f).SetEase(UIEase.OutBack);
            }

            if (s_openStoreOnShow)
            {
                s_openStoreOnShow = false;
                OpenStore();
            }
        }

        protected override void OnHidden()
        {
            _store.Close(true);
            UITween.KillId(_storeHintTween);
            _storeHintTween = 0;
            _storeBtn.SetAttention(false);
        }

        public override bool OnBack()
        {
            if (_store.IsOpen)
            {
                _store.Close();
                return true;
            }

            OnBackPressed();
            return true;
        }

        protected override void Refresh(bool showing)
        {
            ProfileService p = Profile;
            _pill.SetAmount(p.Gems, !showing);

            if (showing)
            {
                _index = IndexOf(!string.IsNullOrEmpty(Game.PreviewShipId) ? Game.PreviewShipId : p.Profile.SelectedShip);
                ShowShip(0, true);
            }
            else
            {
                UpdateDots(true);
                UpdateState(false);
            }

            _store.Refresh();
        }

        // ------------------------------------------------------------------ browsing

        private void Step(int dir)
        {
            if (!HasData)
            {
                return;
            }

            int n = ShipCatalog.All.Count;
            _index = ((_index + dir) % n + n) % n;
            ShowShip(dir, false);
        }

        private void ShowShip(int dir, bool showing)
        {
            ShipDefinition ship = Current;
            if (Game != null)
            {
                Game.PreviewShip(ship.Id);
            }

            _name.text = ship.Name;
            _tagline.text = ship.Tagline;
            Color glow = ShipGlow(ship);
            _card.SetAccent(glow);
            if (_nameHalo != null)
            {
                _nameHalo.effectColor = UITheme.Alpha(glow, 0.5f);
            }

            UpdateDots(!showing);
            UpdateState(showing);

            if (dir != 0 && !UITheme.ReducedMotion)
            {
                UITween.Move(_slide, new Vector2(dir * 200f, 0f), Vector2.zero, 0.34f).SetEase(UIEase.OutCubic);
                UITween.Fade(_slideGroup, 0f, 1f, 0.26f).SetEase(UIEase.OutCubic);
            }
            else
            {
                UITween.Kill(_slide);
                UITween.Kill(_slideGroup);
                _slide.anchoredPosition = Vector2.zero;
                _slideGroup.alpha = 1f;
            }
        }

        private void UpdateDots(bool animate)
        {
            ProfileService p = Profile;
            IReadOnlyList<ShipDefinition> ships = ShipCatalog.All;
            Color glow = ShipGlow(Current);
            for (int i = 0; i < _dots.Count && i < ships.Count; i++)
            {
                bool active = i == _index;
                bool owned = p != null && p.Owns(ships[i].Id);
                Color c = active ? glow : new Color(1f, 1f, 1f, owned ? 0.6f : 0.2f);
                float w = active ? 52f : 18f;
                LayoutElement le = _dotLayouts[i];
                if (animate)
                {
                    if (!Mathf.Approximately(le.preferredWidth, w))
                    {
                        float from = le.preferredWidth;
                        UITween.Value(le, from, w, 0.25f, x =>
                        {
                            le.preferredWidth = x;
                            le.minWidth = x;
                        }).SetEase(UIEase.OutCubic);
                    }

                    if (_dots[i].color != c)
                    {
                        UITween.Color(_dots[i], c, 0.25f);
                    }
                }
                else
                {
                    UITween.Kill(le);
                    le.preferredWidth = w;
                    le.minWidth = w;
                    UITween.Kill(_dots[i]);
                    _dots[i].color = c;
                }
            }
        }

        private void UpdateState(bool showing)
        {
            if (!HasData)
            {
                return;
            }

            ProfileService p = Profile;
            ShipDefinition ship = Current;
            bool owned = p.Owns(ship.Id);
            bool selected = owned && p.Profile.SelectedShip == ship.Id;
            ActionKind kind;
            bool showProgress = false;

            if (selected)
            {
                kind = ActionKind.Selected;
                SetStatus(IconId.Check, UITheme.Success, Loc.T("hangar.equipped"));
            }
            else if (owned)
            {
                kind = ActionKind.Select;
                SetStatus(IconId.Check, UITheme.TextSecondary, Loc.T("hangar.owned"));
            }
            else if (!p.RequirementMet(ship))
            {
                kind = ActionKind.Locked;
                SetStatus(IconId.Lock, UITheme.TextSecondary, ship.UnlockDescription);
                // The price that follows the requirement is shown up front, so the whole cost is clear.
                _lockedBtn.SetSubLabel(ship.Price > 0 ? Loc.T("hangar.then_price", ("n", ship.Price)) : null);
                showProgress = true;
                string text;
                if (ship.Unlock == UnlockKind.Rank)
                {
                    text = Loc.T("hangar.rank_progress", ("rank", p.PilotRank), ("target", ship.UnlockValue));
                }
                else if (ship.Unlock == UnlockKind.Level)
                {
                    text = Loc.T("hangar.level_progress", ("best", p.RequirementCurrent(ship)), ("target", ship.UnlockValue));
                }
                else if (ship.Unlock == UnlockKind.Distance)
                {
                    text = Loc.T("hangar.distance_progress", ("best", MetaUI.N(p.RequirementCurrent(ship))), ("target", ship.UnlockValue));
                }
                else
                {
                    text = MetaUI.N(p.RequirementCurrent(ship)) + " / " + MetaUI.N(ship.UnlockValue);
                }

                SetProgress(p.UnlockProgress(ship), text, ShipGlow(ship), showing);
            }
            else if (ship.Price > 0)
            {
                kind = ActionKind.Buy;
                long cost = ship.Price;
                bool afford = p.Gems >= cost;
                _buyBtn.SetLabel(Loc.T("common.buy_price", ("price", MetaUI.N(cost))));
                _buyBtn.SetSubLabel(afford ? null : Loc.T("hangar.need_more", ("n", cost - p.Gems)));
                _buyBtn.SetInteractable(afford);
                _buyCatcher.gameObject.SetActive(!afford);
                string status = afford ? Loc.T("hangar.ready")
                    : ship.HasRequirement ? Loc.T("hangar.requirement_met") : Loc.T("hangar.unlock_with_gems");
                SetStatus(IconId.Gem, UITheme.GemColor, status);
                if (!afford)
                {
                    showProgress = true;
                    SetProgress(cost > 0 ? (float)p.Gems / cost : 1f, MetaUI.N(p.Gems) + " / " + MetaUI.N(cost), UITheme.GemColor, showing);
                }
            }
            else
            {
                kind = ActionKind.Unlock;
                SetStatus(IconId.Star, UITheme.Gold, Loc.T("hangar.requirement_met"));
            }

            _progressRow.gameObject.SetActive(showProgress);
            SetAction(kind, !showing);
        }

        private void SetStatus(IconId icon, Color color, string text)
        {
            UIFactory.SetIcon(_statusIcon, icon);
            _statusIcon.color = color;
            _statusText.text = text ?? string.Empty;
            _statusText.color = color == UITheme.TextSecondary ? UITheme.TextSecondary : UITheme.TextPrimary;
        }

        private void SetProgress(float value, string text, Color color, bool showing)
        {
            _progress.SetColors(UITheme.Darken(color, 0.25f), UITheme.Lighten(color, 0.2f));
            _progress.SetValue(Mathf.Clamp01(value), !showing);
            _progressText.text = text;
        }

        private void SetAction(ActionKind kind, bool animate)
        {
            bool changed = kind != _action;
            _action = kind;
            _selectBtn.gameObject.SetActive(kind == ActionKind.Select);
            _selectedBtn.gameObject.SetActive(kind == ActionKind.Selected);
            _buyBtn.gameObject.SetActive(kind == ActionKind.Buy);
            _unlockBtn.gameObject.SetActive(kind == ActionKind.Unlock);
            _lockedBtn.gameObject.SetActive(kind == ActionKind.Locked);
            _lockedCatcher.gameObject.SetActive(kind == ActionKind.Locked);
            if (kind != ActionKind.Buy)
            {
                _buyCatcher.gameObject.SetActive(false);
            }

            NeonButton active = ActiveButton();
            // Locked / unaffordable ships have a disabled action button: fall back to ▶ so gamepad focus is never empty.
            DefaultSelection = active != null && active.interactable ? active.gameObject : _nextBtn.gameObject;
            if (active != null)
            {
                if (changed && animate && !UITheme.ReducedMotion)
                {
                    UITween.Scale(active.transform, 0.6f, 1f, 0.4f).SetEase(UIEase.OutBack);
                }
            }
        }

        private NeonButton ActiveButton()
        {
            switch (_action)
            {
                case ActionKind.Select:
                    return _selectBtn;
                case ActionKind.Selected:
                    return _selectedBtn;
                case ActionKind.Buy:
                    return _buyBtn;
                case ActionKind.Unlock:
                    return _unlockBtn;
                case ActionKind.Locked:
                    return _lockedBtn;
                default:
                    return null;
            }
        }

        // ------------------------------------------------------------------ actions

        private void OnBackPressed()
        {
            if (Game != null)
            {
                Game.ExitHangar();
            }
        }

        private void OnSelect()
        {
            if (!HasData)
            {
                return;
            }

            ShipDefinition ship = Current;
            if (Game.SelectShip(ship.Id))
            {
                Color glow = ShipGlow(ship);
                MetaFx.Ring(Rect, MetaUI.LocalPoint(Rect, _actionSlot), glow, 520f, 0.45f);
                UITween.Punch(_cardRt, 0.03f, 0.3f);
                MarkDirty();
            }
        }

        private void OnSelectedTapped()
        {
            UITween.Punch(_selectedBtn.Visual, 0.08f, 0.3f);
        }

        private void OnBuy()
        {
            if (!HasData)
            {
                return;
            }

            ShipDefinition ship = Current;
            long cost = ship.Price;
            if (Profile.Gems < cost)
            {
                OnBuyDenied();
                return;
            }

            string id = ship.Id;
            Dialogs.Confirm(Loc.T("hangar.confirm_title", ("ship", ship.DisplayName)),
                Loc.T("hangar.confirm_body", ("n", cost), ("ship", ship.DisplayName)),
                Loc.T("common.buy"), Loc.T("common.cancel"), ok =>
                {
                    if (ok)
                    {
                        Purchase(id);
                    }
                }, false, IconId.Gem);
        }

        private void OnUnlock()
        {
            if (HasData)
            {
                Purchase(Current.Id);
            }
        }

        private void Purchase(string shipId)
        {
            if (!HasData || this == null)
            {
                return;
            }

            ShipPurchaseResult result = Game.BuyShip(shipId);
            switch (result)
            {
                case ShipPurchaseResult.Purchased:
                    Celebrate(ShipCatalog.Get(shipId));
                    break;
                case ShipPurchaseResult.NotEnoughGems:
                case ShipPurchaseResult.Locked:
                    // The controller already played the error feedback.
                    ShakeActive();
                    break;
            }

            MarkDirty();
        }

        private void Celebrate(ShipDefinition ship)
        {
            Color glow = ShipGlow(ship);
            MetaFx.BurstAt(Rect, _actionSlot, glow, 30, 360f);
            MetaFx.FloatAt(Rect, _actionSlot, Loc.T("hangar.unlocked_float", ("ship", ship.DisplayName)), glow, 150f, 1.2f);
            UITween.Punch(_cardRt, 0.05f, 0.45f);
            UITween.Punch(_pill.Rect, 0.2f, 0.45f);
            MetaUI.Flash(glow, 0.22f, ReduceFlashing);

            // A purchase can complete a fleet achievement; its reward is already in the balance.
            var unlocked = Game.Profile.LastPurchaseAchievements;
            for (int i = 0; i < unlocked.Count; i++)
            {
                Toasts.Show(unlocked[i].Title + "  +" + MetaUI.N(unlocked[i].Reward), IconId.Trophy, UITheme.Gold);
            }
        }

        private void OnBuyDenied()
        {
            ShakeActive();
            Sfx(SfxId.UiError);
            Haptic();
            _storeBtn.SetAttention(true);
            UITween.Punch(_storeBtn.transform, 0.12f, 0.4f);
            UITween.KillId(_storeHintTween);
            _storeHintTween = UITween.Delay(this, 2.6f, () =>
            {
                if (_storeBtn != null)
                {
                    _storeBtn.SetAttention(false);
                }
            }).Id;
        }

        private void OnLockedTapped()
        {
            ShakeActive();
            Sfx(SfxId.UiError);
            Haptic();
            UITween.Punch(_progressRow, 0.06f, 0.35f);
        }

        private void ShakeActive()
        {
            NeonButton b = ActiveButton();
            if (b != null && b.Visual != null)
            {
                UITween.Shake(b.Visual, 20f, 0.4f);
            }
        }

        // ------------------------------------------------------------------ swipe

        public void OnBeginDrag(PointerEventData eventData)
        {
            _dragging = HasData && !_store.IsOpen && !Dialogs.IsOpen;
            _dragUnits = 0f;
            if (_dragging)
            {
                UITween.Kill(_slide);
                UITween.Kill(_slideGroup);
            }
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!_dragging)
            {
                return;
            }

            _dragUnits = (eventData.position.x - eventData.pressPosition.x) / CanvasScale();
            float x = Mathf.Clamp(_dragUnits * 0.4f, -160f, 160f);
            _slide.anchoredPosition = new Vector2(x, 0f);
            _slideGroup.alpha = 1f - Mathf.Clamp01(Mathf.Abs(x) / 320f);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!_dragging)
            {
                return;
            }

            _dragging = false;
            _dragUnits = (eventData.position.x - eventData.pressPosition.x) / CanvasScale();
            if (Mathf.Abs(_dragUnits) >= SwipeThreshold)
            {
                UIFactory.PlayClickFeedback();
                Step(_dragUnits < 0f ? 1 : -1);
                return;
            }

            UITween.Move(_slide, Vector2.zero, 0.3f).SetEase(UIEase.OutBack);
            UITween.Fade(_slideGroup, 1f, 0.2f);
        }

        // ------------------------------------------------------------------ helpers

        private static float CanvasScale()
        {
            UIRoot root = UIRoot.Instance;
            float s = root != null && root.Canvas != null ? root.Canvas.scaleFactor : 1f;
            return s > 0.0001f ? s : 1f;
        }

        private static int IndexOf(string shipId)
        {
            IReadOnlyList<ShipDefinition> all = ShipCatalog.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].Id == shipId)
                {
                    return i;
                }
            }

            return 0;
        }

        private static Color ShipGlow(ShipDefinition ship)
        {
            Color c = UITheme.ToColor(ship.Glow);
            c.a = 1f;
            return c;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_openStoreOnShow = false;
        }
    }
}
