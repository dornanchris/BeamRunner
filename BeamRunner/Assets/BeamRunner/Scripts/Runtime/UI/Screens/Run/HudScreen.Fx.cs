using System;
using BeamRunner.Core;
using BeamRunner.Core.Localization;
using BeamRunner.Core.Meta;
using BeamRunner.Core.Sim;
using BeamRunner.Core.Field;
using BeamRunner.Game;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace BeamRunner.UI
{
    /// <summary>HUD event feedback: craft popups, level / level-clear banners, tutorial hint cards, hit flashes.</summary>
    public sealed partial class HudScreen
    {
        private enum PendingPopup
        {
            None = 0,
            NearMiss = 1,
            Smash = 2,
        }

        private const float HintDuration = 3.5f;
        private const int BigBonus = 200;
        private const float PopupRise = 170f;

        private RectTransform _fx;
        private RectTransform _popups;

        private RectTransform _zoneRoot;
        private CanvasGroup _zoneGroup;
        private Text _zoneCaption;
        private NeonTitle _zoneTitle;
        private RectTransform _zoneLineTop;
        private RectTransform _zoneLineBottom;
        private int _zoneHide;
        private Action _zoneHidden;

        private RectTransform _mileRoot;
        private CanvasGroup _mileGroup;
        private Text _mileText;
        private int _mileHide;
        private Action _mileHidden;

        private RectTransform _hintRoot;
        private CanvasGroup _hintGroup;
        private Image _hintIcon;
        private Image _hintIconGlow;
        private Text _hintHead;
        private RectTransform _hintDetailRow;
        private Text _hintDetail;
        private Image _hintInline;
        private float _hintTimer;
        private float _hintAge;
        private bool _hintVisible;
        private TutorialHint _hintKind;
        private Action _hintHidden;

        private PendingPopup _pending;
        private int _pendingId;
        private int _pendingCount;
        private int _suppressSmashId = int.MinValue;
        private float _lastEmptyPopup = -10f;
        private int _fuelPopupValue;
        private float _lastPopupTime = -10f;
        private int _popupStack;
        private float _popupSide = 1f;

        /// <summary>Reacts to a simulation event of the player's run (forwarded by <see cref="GameUI"/>).</summary>
        public void HandleEvent(RunEvent e)
        {
            if (_game == null || !isActiveAndEnabled)
            {
                return;
            }

            bool live = _game.State == GameState.Running;
            switch (e.Type)
            {
                case RunEventType.NearMiss:
                    if (live)
                    {
                        FlushPendingPopup();
                        _pending = PendingPopup.NearMiss;
                        _pendingId = e.Id;
                        _pendingCount = e.Count;
                    }

                    break;

                case RunEventType.Smashed:
                    if (e.Id == _suppressSmashId)
                    {
                        _suppressSmashId = int.MinValue;
                    }
                    else if (live)
                    {
                        FlushPendingPopup();
                        _pending = PendingPopup.Smash;
                        _pendingId = e.Id;
                        _pendingCount = 0;
                    }

                    break;

                case RunEventType.Bonus:
                    if (!live)
                    {
                        break;
                    }

                    if (_pending != PendingPopup.None && _pendingId == e.Id)
                    {
                        SpawnPending(e.Count);
                    }
                    else if (e.Count > 0)
                    {
                        SpawnAtShip("+" + RunUi.Grouped(e.Count), UITheme.Accent, PopupRise, 0.8f, true);
                    }

                    if (e.Count >= BigBonus)
                    {
                        PunchScore();
                    }

                    break;

                case RunEventType.PickupCollected:
                    if (live)
                    {
                        _fuelPopupValue = (int)e.Value;
                        OnPickup((PickupKind)e.Count);
                    }

                    break;

                case RunEventType.TrailCompleted:
                    if (live)
                    {
                        FlushPendingPopup();
                        SpawnAtShip(Loc.T("hud.full_trail") + "  +" + RunUi.Grouped(e.Count), UITheme.AccentYellow, PopupRise + 50f, 1.15f, false);
                        PunchScore();
                    }

                    break;

                case RunEventType.ShieldBroken:
                    _suppressSmashId = e.Id;
                    OnShieldBroken();
                    break;

                case RunEventType.OverloadStarted:
                    if (live)
                    {
                        SpawnAtShip(Loc.T("hud.overload_popup"), UITheme.AccentYellow, 280f, 1.35f, false);
                    }

                    break;

                case RunEventType.BuildFailed:
                    FlashEmpty();
                    if (live && Time.unscaledTime - _lastEmptyPopup > 0.8f)
                    {
                        _lastEmptyPopup = Time.unscaledTime;
                        SpawnAtShip(Loc.T("hud.no_fuel"), UITheme.Danger, 220f, 0.9f, false);
                    }

                    break;

                case RunEventType.Damaged:
                    if (live)
                    {
                        FlushPendingPopup();
                        SpawnAtShip(Loc.T("hud.hull_hit"), UITheme.Danger, 240f, 1.05f, false);
                    }

                    break;

                case RunEventType.FuelLow:
                    if (live)
                    {
                        UITween.Punch(_meterRoot, 0.08f, 0.35f);
                    }

                    break;

                case RunEventType.LevelStarted:
                    ShowLevelBanner(e.Count);
                    break;

                case RunEventType.LevelCleared:
                    ShowLevelCleared(e.Count, (int)e.Value);
                    break;

                case RunEventType.TutorialHint:
                    ShowHint((TutorialHint)e.Count);
                    break;

                case RunEventType.Crashed:
                    FlushPendingPopup();
                    if (_hintVisible)
                    {
                        HideHint(false);
                    }

                    break;
            }
        }

        // ------------------------------------------------------------------ build

        private void BuildFx()
        {
            _fx = UIFactory.CreateFill(Rect, "Fx");
            _fx.gameObject.AddComponent<Canvas>();
            _popups = UIFactory.CreateFill(_fx, "Popups");

            // Level banner: "LEVEL 4" / EYE OF THE NEEDLE between two neon hairlines.
            _zoneRoot = UIFactory.CreateRect(_fx, "LevelBanner");
            UIFactory.Anchor(_zoneRoot, AnchorPreset.TopCenter, new Vector2(0f, -570f));
            UIFactory.SetSize(_zoneRoot, 1000f, 200f);
            _zoneGroup = RunUi.PassiveGroup(_zoneRoot);
            _zoneCaption = UIFactory.LabelLine(_zoneRoot, Loc.T("hud.level", ("n", 1)), UITheme.TextSmall, UITheme.TextSecondary, TextAnchor.MiddleCenter, UIFont.Bold, TextGlow.Shadow);
            UIFactory.Anchor(_zoneCaption.rectTransform, AnchorPreset.TopCenter, new Vector2(0f, 0f));
            UIFactory.SetSize(_zoneCaption.rectTransform, 600f, 44f);
            Image lineTop = UIFactory.CreateImage(_zoneRoot, UISprites.FadeLine, UITheme.Accent, "LineTop");
            UIAccentFollower.Attach(lineTop, AccentSlot.Primary);
            _zoneLineTop = lineTop.rectTransform;
            UIFactory.Anchor(_zoneLineTop, AnchorPreset.TopCenter, new Vector2(0f, -52f));
            UIFactory.SetSize(_zoneLineTop, 780f, 3f);
            _zoneTitle = UIFactory.NeonTitle(_zoneRoot, Loc.T(Levels.NameKey(LevelKind.Bridges)), 78);
            UIFactory.Anchor(_zoneTitle.Rect, AnchorPreset.StretchTop, new Vector2(0f, -58f));
            Image lineBottom = UIFactory.CreateImage(_zoneRoot, UISprites.FadeLine, UITheme.AccentAlt, "LineBottom");
            UIAccentFollower.Attach(lineBottom, AccentSlot.Secondary);
            _zoneLineBottom = lineBottom.rectTransform;
            UIFactory.Anchor(_zoneLineBottom, AnchorPreset.TopCenter, new Vector2(0f, -168f));
            UIFactory.SetSize(_zoneLineBottom, 780f, 3f);
            _zoneRoot.gameObject.SetActive(false);
            _zoneHidden = () =>
            {
                if (_zoneRoot != null && _zoneGroup.alpha <= 0.01f)
                {
                    _zoneRoot.gameObject.SetActive(false);
                }
            };

            // Level-clear banner: "LEVEL 3 CLEAR" with the bonus beneath.
            _mileRoot = UIFactory.CreateRect(_fx, "LevelClear");
            UIFactory.Anchor(_mileRoot, AnchorPreset.TopCenter, new Vector2(0f, -790f));
            UIFactory.SetSize(_mileRoot, 900f, 120f);
            _mileGroup = RunUi.PassiveGroup(_mileRoot);
            _mileText = UIFactory.LabelLine(_mileRoot, Loc.T("hud.level_clear", ("n", 1)), 80, UITheme.TextPrimary, TextAnchor.MiddleCenter, UIFont.Title, TextGlow.Neon);
            UIFactory.Stretch(_mileText.rectTransform);
            _mileRoot.gameObject.SetActive(false);
            _mileHidden = () =>
            {
                if (_mileRoot != null && _mileGroup.alpha <= 0.01f)
                {
                    _mileRoot.gameObject.SetActive(false);
                }
            };
        }

        private void BuildHint()
        {
            _hintRoot = UIFactory.CreateRect(_fx, "Hint");
            UIFactory.Anchor(_hintRoot, AnchorPreset.TopCenter, new Vector2(0f, -400f));
            HorizontalLayoutGroup h = UIFactory.HStack(_hintRoot, UITheme.SpaceMd, UIFactory.Padding(34, 52, 24, 24), TextAnchor.MiddleLeft, true);
            h.childForceExpandWidth = false;
            UIFactory.FitContent(_hintRoot, true, true);
            _hintGroup = RunUi.PassiveGroup(_hintRoot);

            GlassPanel panel = UIFactory.GlassPanel(_hintRoot, null, UITheme.RadiusLg, 0f, false, true, true);
            UIFactory.Stretch(panel.Rect);
            UIFactory.IgnoreLayout(panel);
            panel.transform.SetAsFirstSibling();

            RectTransform iconWrap = UIFactory.CreateRect(_hintRoot, "IconWrap");
            UIFactory.SetLayout(iconWrap, 84f, 84f);
            _hintIconGlow = UIFactory.CreateImage(iconWrap, UISprites.Glow, UITheme.Accent, "Glow");
            UIFactory.Stretch(_hintIconGlow.rectTransform, -26f, -26f, -26f, -26f);
            UIAccentFollower.Attach(_hintIconGlow, AccentSlot.Primary, 0.45f);
            _hintIcon = UIFactory.Icon(iconWrap, IconId.Hand, 72f, UITheme.Accent);
            UIFactory.Anchor(_hintIcon.rectTransform, AnchorPreset.Center);
            UIAccentFollower.Attach(_hintIcon, AccentSlot.Primary, 1f, 0.2f);

            VerticalLayoutGroup texts = UIFactory.VStack(_hintRoot, 0f, null, TextAnchor.MiddleLeft);
            texts.childForceExpandWidth = false;
            _hintHead = UIFactory.LabelLine(texts.transform, string.Empty, 52, UITheme.TextPrimary, TextAnchor.MiddleLeft, UIFont.Bold, TextGlow.Shadow);
            HorizontalLayoutGroup detailRow = UIFactory.HStack(texts.transform, 10f, null, TextAnchor.MiddleLeft);
            detailRow.childForceExpandWidth = false;
            _hintDetailRow = (RectTransform)detailRow.transform;
            _hintDetail = UIFactory.LabelLine(_hintDetailRow, string.Empty, 38, UITheme.TextSecondary, TextAnchor.MiddleLeft, UIFont.Body);
            _hintInline = UIFactory.Icon(_hintDetailRow, IconId.Bolt, 42f, UITheme.AccentYellow);

            _hintRoot.gameObject.SetActive(false);
            _hintHidden = () =>
            {
                if (_hintRoot != null && !_hintVisible)
                {
                    _hintRoot.gameObject.SetActive(false);
                }
            };
        }

        // ------------------------------------------------------------------ per frame

        private void UpdateFx(float dt)
        {
            if (!_hintVisible)
            {
                return;
            }

            if (_game.State != GameState.Running)
            {
                return;
            }

            _hintAge += dt;
            _hintTimer -= dt;
            if (_hintAge > 1.6f && _game.Input != null &&
                (_hintKind == TutorialHint.Move && _game.Input.IsSteering ||
                 (_hintKind == TutorialHint.Bridge || _hintKind == TutorialHint.Ramps) && _game.Input.IsBuilding))
            {
                _hintTimer = Mathf.Min(_hintTimer, 0.5f);
            }

            if (_hintTimer <= 0f)
            {
                HideHint(false);
            }
        }

        private void HideTransientFx()
        {
            _pending = PendingPopup.None;
            _suppressSmashId = int.MinValue;
            _popupStack = 0;
            if (_hintRoot == null)
            {
                return;
            }

            HideHint(true);
            UITween.KillId(_zoneHide);
            UITween.KillId(_mileHide);
            _zoneHide = 0;
            _mileHide = 0;
            UITween.Kill(_zoneGroup);
            UITween.Kill(_mileGroup);
            _zoneGroup.alpha = 0f;
            _mileGroup.alpha = 0f;
            _zoneRoot.gameObject.SetActive(false);
            _mileRoot.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ popups

        private void FlushPendingPopup()
        {
            if (_pending != PendingPopup.None)
            {
                SpawnPending(0);
            }
        }

        private void SpawnPending(int points)
        {
            PendingPopup kind = _pending;
            int count = _pendingCount;
            _pending = PendingPopup.None;
            if (kind == PendingPopup.NearMiss)
            {
                string text = count >= 2 ? Loc.T("hud.near_miss") + " " + RunUi.Multiplier(count) : Loc.T("hud.near_miss");
                if (points > 0)
                {
                    text += "  +" + RunUi.Grouped(points);
                }

                float scale = 0.82f + 0.05f * Mathf.Min(count, 6);
                SpawnAtShip(text, NearMissColor(count), PopupRise, scale, true);
            }
            else if (kind == PendingPopup.Smash)
            {
                string text = points > 0 ? Loc.T("hud.smash") + "  +" + RunUi.Grouped(points) : Loc.T("hud.smash");
                SpawnAtShip(text, UITheme.DangerAlt, PopupRise + 30f, 1.1f, true);
            }
        }

        /// <summary>
        /// The steering keys in the player's keyboard layout, e.g. "W A S D" (the Input System binds physical keys: the
        /// "W" position is "Z" on AZERTY, "Ц" on a Russian layout).
        /// </summary>
        private static string KeyNames()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null)
            {
                return "A D"; // l10n-ok: key caps
            }

            return KeyCap(kb.aKey, "A") + " " + KeyCap(kb.dKey, "D"); // l10n-ok: key caps
        }

        private static string KeyCap(UnityEngine.InputSystem.Controls.KeyControl key, string fallback)
        {
            string name = key != null ? key.displayName : null;
            return string.IsNullOrEmpty(name) || name.Length > 3 ? fallback : name;
        }

        private static Color NearMissColor(int chain)
        {
            switch (Mathf.Abs(chain) % 3)
            {
                case 1:
                    return UITheme.Accent;
                case 2:
                    return UITheme.AccentAlt;
                default:
                    return UITheme.AccentYellow;
            }
        }

        private void SpawnAtShip(string text, Color color, float up, float scale, bool stack)
        {
            if (_popups == null || _game == null)
            {
                return;
            }

            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_popups, _game.ShipScreenPoint, null, out local))
            {
                local = new Vector2(0f, -_popups.rect.height * 0.2f);
            }

            if (stack)
            {
                float now = Time.unscaledTime;
                _popupStack = now - _lastPopupTime < 0.4f ? Mathf.Min(_popupStack + 1, 3) : 0;
                _lastPopupTime = now;
                _popupSide = -_popupSide;
                local.x += _popupSide * (_popupStack > 0 ? 70f : 24f);
                local.y += _popupStack * 78f;
            }

            local.y += up;
            Rect r = _popups.rect;
            float halfW = Mathf.Max(0f, r.width * 0.5f - 280f);
            local.x = Mathf.Clamp(local.x, -halfW, halfW);
            local.y = Mathf.Clamp(local.y, r.yMin + 380f, Mathf.Max(r.yMin + 380f, r.yMax - 440f));
            FloatingText.Spawn(_popups, text, color, local, scale);
        }

        private void PunchScore()
        {
            UITween.Punch(_scoreRoot, 0.2f, 0.4f);
            UITween.Color(_score, UITheme.AccentYellow, UITheme.TextPrimary, 0.6f).SetEase(UIEase.OutCubic);
        }

        private void OnPickup(PickupKind kind)
        {
            switch (kind)
            {
                case PickupKind.Shield:
                    SpawnAtShip(Loc.T("hud.shield"), UITheme.AccentCyan, 240f, 1.05f, false);
                    break;
                case PickupKind.Magnet:
                    SpawnAtShip(Loc.T("hud.magnet"), UITheme.AccentPurple, 240f, 1.05f, false);
                    break;
                case PickupKind.Overload:
                    SpawnAtShip(Loc.T("hud.overload_cell"), UITheme.AccentYellow, 240f, 1.05f, false);
                    break;
                case PickupKind.InstantBuild:
                    SpawnAtShip(Loc.T("hud.free_builds"), UITheme.AccentCyan, 240f, 1.05f, false);
                    break;
                case PickupKind.Repair:
                    SpawnAtShip(Loc.T("hud.repaired"), UITheme.AccentMagenta, 240f, 1.05f, false);
                    break;
                case PickupKind.Fuel:
                    SpawnAtShip(Loc.T("hud.fuel_plus", ("n", _fuelPopupValue)), new Color(0.35f, 1f, 0.45f), 180f, 0.75f, true);
                    UITween.Punch(_meterRoot, 0.06f, 0.3f);
                    break;
            }
        }

        private void OnShieldBroken()
        {
            bool reduce = _game.Profile != null && _game.Profile.Settings.ReduceFlashing;
            UIRoot root = UIRoot.Instance;
            if (root != null && root.Fader != null && !root.Fader.IsBusy)
            {
                root.Fader.Flash(UITheme.Danger, reduce ? 0.1f : 0.38f, reduce ? 0.3f : 0.45f);
            }

            if (_game.State == GameState.Running)
            {
                SpawnAtShip(Loc.T("hud.shield_down"), UITheme.Danger, 230f, 1.05f, false);
            }
        }

        // ------------------------------------------------------------------ banners

        private void ShowLevelBanner(int level)
        {
            _zoneCaption.text = Loc.T("hud.level", ("n", level));
            _zoneTitle.SetText(Loc.T(Levels.NameKey(Levels.KindOf(level))));
            _zoneRoot.gameObject.SetActive(true);
            UITween.KillId(_zoneHide);
            _zoneGroup.alpha = 0f;
            UITween.Fade(_zoneGroup, 0f, 1f, 0.3f).SetEase(UIEase.OutCubic);
            UITween.Scale(_zoneTitle.Rect, 1.35f, 1f, 0.6f).SetEase(UIEase.OutCubic);
            UITween.Scale(_zoneLineTop, new Vector3(0f, 1f, 1f), Vector3.one, 0.7f).SetDelay(0.05f).SetEase(UIEase.OutCubic);
            UITween.Scale(_zoneLineBottom, new Vector3(0f, 1f, 1f), Vector3.one, 0.7f).SetDelay(0.12f).SetEase(UIEase.OutCubic);
            _zoneHide = UITween.Delay(this, 2.5f, HideZoneBanner).Id;
        }

        private void HideZoneBanner()
        {
            _zoneHide = 0;
            UITween.Fade(_zoneGroup, 0f, 0.5f).SetEase(UIEase.InQuad).OnComplete(_zoneHidden);
        }

        private void ShowLevelCleared(int level, int bonus)
        {
            if (_game.State != GameState.Running)
            {
                return;
            }

            _mileText.text = Loc.T("hud.level_clear", ("n", level));
            if (bonus > 0)
            {
                SpawnAtShip("+" + RunUi.Grouped(bonus), UITheme.AccentYellow, PopupRise + 60f, 1.15f, false);
                PunchScore();
            }

            _mileRoot.gameObject.SetActive(true);
            UITween.KillId(_mileHide);
            _mileGroup.alpha = 0f;
            UITween.Fade(_mileGroup, 0f, 1f, 0.2f).SetEase(UIEase.OutCubic);
            UITween.Scale(_mileRoot, 0.4f, 1f, 0.5f).SetEase(UIEase.OutBack);
            _mileHide = UITween.Delay(this, 1.5f, HideMilestone).Id;
        }

        private void HideMilestone()
        {
            _mileHide = 0;
            UITween.Fade(_mileGroup, 0f, 0.4f).SetEase(UIEase.InQuad).OnComplete(_mileHidden);
        }

        // ------------------------------------------------------------------ tutorial hints

        private void ShowHint(TutorialHint hint)
        {
            IconId icon;
            string head;
            string detail;
            bool inline = false;
            bool desktop = !Application.isMobilePlatform;
            bool pad = Gamepad.current != null;
            ControlMode mode = _game.Profile != null ? _game.Profile.Settings.ControlMode : ControlMode.Swipe;
            switch (hint)
            {
                case TutorialHint.Move:
                    if (desktop)
                    {
                        icon = pad ? IconId.Gamepad : IconId.Hand;
                        head = pad ? Loc.T("tutorial.move_pad") : Loc.T("tutorial.move_keys", ("keys", KeyNames()));
                        detail = pad ? Loc.T("tutorial.move_pad_detail") : Loc.T("tutorial.move_keys_detail");
                    }
                    else if (mode == ControlMode.TapSides)
                    {
                        icon = IconId.Hand;
                        head = Loc.T("tutorial.tap_sides");
                        detail = Loc.T("tutorial.tap_sides_detail");
                    }
                    else
                    {
                        icon = IconId.Hand;
                        head = Loc.T("tutorial.swipe");
                        detail = Loc.T("tutorial.swipe_detail");
                    }

                    break;

                case TutorialHint.Bridge:
                    icon = pad ? IconId.Gamepad : IconId.Hand;
                    head = desktop ? (pad ? Loc.T("tutorial.bridge_pad") : Loc.T("tutorial.bridge_keys")) : Loc.T("tutorial.bridge");
                    detail = Loc.T("tutorial.bridge_detail");
                    break;

                case TutorialHint.Ramps:
                    icon = IconId.ChevronUp;
                    head = desktop ? (pad ? Loc.T("tutorial.ramps_pad") : Loc.T("tutorial.ramps_keys")) : Loc.T("tutorial.ramps");
                    detail = Loc.T("tutorial.ramps_detail");
                    break;

                case TutorialHint.Fuel:
                    icon = IconId.Fuel;
                    head = Loc.T("tutorial.fuel");
                    detail = Loc.T("tutorial.fuel_detail");
                    break;

                case TutorialHint.Edges:
                    icon = IconId.Warning;
                    head = Loc.T("tutorial.edges");
                    detail = Loc.T("tutorial.edges_detail");
                    break;

                case TutorialHint.Obstacles:
                    icon = IconId.Warning;
                    head = Loc.T("tutorial.obstacles");
                    detail = Loc.T("tutorial.obstacles_detail");
                    break;

                default:
                    icon = IconId.Check;
                    head = Loc.T("tutorial.done");
                    detail = null;
                    break;
            }

            _hintKind = hint;
            UIFactory.SetIcon(_hintIcon, icon);
            _hintHead.text = head;
            _hintDetail.text = detail ?? string.Empty;
            _hintDetailRow.gameObject.SetActive(!string.IsNullOrEmpty(detail));
            _hintInline.gameObject.SetActive(inline);
            _hintTimer = HintDuration;
            _hintAge = 0f;

            bool wasVisible = _hintVisible && _hintRoot.gameObject.activeSelf;
            _hintVisible = true;
            _hintRoot.gameObject.SetActive(true);
            if (wasVisible)
            {
                // Superseded: snap the new card in with a punch.
                _hintGroup.alpha = 1f;
                UITween.Kill(_hintGroup);
                UITween.Punch(_hintRoot, 0.12f, 0.35f);
            }
            else
            {
                _hintGroup.alpha = 0f;
                UITween.Fade(_hintGroup, 0f, 1f, 0.28f).SetEase(UIEase.OutCubic);
                UITween.Scale(_hintRoot, 0.8f, 1f, 0.45f).SetEase(UIEase.OutBack);
            }

            UITween.Scale(_hintIcon.rectTransform, 0.3f, 1f, 0.5f).SetDelay(0.08f).SetEase(UIEase.OutBack);
        }

        private void HideHint(bool instant)
        {
            _hintVisible = false;
            if (_hintRoot == null)
            {
                return;
            }

            if (instant || !_hintRoot.gameObject.activeSelf)
            {
                UITween.Kill(_hintGroup);
                _hintGroup.alpha = 0f;
                _hintRoot.gameObject.SetActive(false);
                return;
            }

            UITween.Fade(_hintGroup, 0f, 0.35f).SetEase(UIEase.InQuad).OnComplete(_hintHidden);
        }
    }
}
