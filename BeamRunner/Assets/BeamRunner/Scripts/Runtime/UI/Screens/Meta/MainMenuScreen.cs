using System.Collections;
using System.Collections.Generic;
using BeamRunner.Core.Field;
using BeamRunner.Core.Localization;
using BeamRunner.Core.Meta;
using BeamRunner.Services;
using UnityEngine;
using UnityEngine.UI;

namespace BeamRunner.UI
{
    /// <summary>
    /// Main menu over the live 3D attract run: rank badge, gems, settings/gift/no-ads in the corners, the neon logo in
    /// the upper third, a pulsing PLAY in the lower third (with ◀ ▶ to pick the start level once a later level has been
    /// reached; the level shows under PLAY) and the HANGAR / MISSIONS / RECORDS nav. The centre stays clear.
    /// </summary>
    public sealed class MainMenuScreen : MetaScreen
    {
        private const float PlayBottom = 440f;

        private RectTransform _topLeft;
        private RectTransform _topRight;
        private Text _rankNumber;
        private Text _rankMissions;
        private Image _rankRing;
        private CurrencyPill _pill;
        private NeonButton _noAds;
        private NeonButton _gift;
        private Badge _giftBadge;
        private NeonButton _settings;

        private RectTransform _logo;
        private CanvasGroup _logoGroup;
        private Image _logoGlow;
        private int _logoGlowTween;

        private NeonButton _play;
        private NeonButton _levelDown;
        private NeonButton _levelUp;
        private int _playPulse;
        private Text _best;
        private DailyRunCard _dailyRun;
        private RectTransform _hint;
        private CanvasGroup _hintGroup;
        private int _hintTween;
        private int _hintBob;

        private RectTransform _nav;
        private Text _hangarSub;
        private Text _upgradesSub;
        private Badge _upgradesBadge;
        private Text _missionsSub;
        private Text _recordsSub;
        private Badge _hangarBadge;

        private bool _shownOnce;
        private bool _buyingNoAds;
        private float _logoScale = 1f;

        protected override bool ListensToStore
        {
            get { return true; }
        }

        // A few steps so the build queue can spread the menu over frames behind the splash (UIScreen.BuildSteps).
        protected override IEnumerator BuildSteps()
        {
            Transition = ScreenTransition.Fade;
            TransitionDuration = 0.35f;
            RectTransform r = Rect;

            MetaUI.Scrim(r, true, 560f, 0.78f);
            MetaUI.Scrim(r, false, 820f, 0.86f);

            BuildTopBar(r);
            yield return null;
            BuildLogo(r);
            BuildPlay(r);
            yield return null;
            BuildNav(r);
        }

        // ------------------------------------------------------------------ construction

        private void BuildTopBar(RectTransform r)
        {
            // Left: rank badge with the gem pill underneath.
            _topLeft = UIFactory.CreateRect(r, "TopLeft");
            UIFactory.Anchor(_topLeft, AnchorPreset.TopLeft, new Vector2(32f, -28f));
            _topLeft.sizeDelta = new Vector2(420f, 230f);
            VerticalLayoutGroup lv = UIFactory.VStack(_topLeft, UITheme.SpaceSm, null, TextAnchor.UpperLeft, true);
            lv.childForceExpandWidth = false;
            _topLeft.gameObject.AddComponent<CanvasGroup>();

            RectTransform badge = UIFactory.CreateRect(_topLeft, "RankBadge");
            UIFactory.SetLayout(badge, -1f, 112f);
            Image plate = UIFactory.PillImage(badge, UITheme.PanelGlass, "Plate");
            plate.raycastTarget = false;
            UIFactory.IgnoreLayout(plate);
            Image border = UIFactory.CreateImage(badge, null, UITheme.PanelBorder, "Border");
            UIFactory.Stretch(border.rectTransform);
            UISprites.ApplyOutline(border, 56f, 2f);
            UIFactory.IgnoreLayout(border);
            HorizontalLayoutGroup bh = UIFactory.HStack(badge, UITheme.SpaceSm, UIFactory.Padding(8, 36, 0, 0), TextAnchor.MiddleLeft, true);
            bh.childForceExpandWidth = false;

            Image glow;
            RectTransform emblem = MetaUI.Emblem(badge, 96f, UITheme.Accent, out _rankRing, out glow);
            UIAccentFollower.Attach(_rankRing, AccentSlot.Primary);
            UIAccentFollower.Attach(glow, AccentSlot.Primary, 0.4f);
            _rankNumber = UIFactory.LabelLine(emblem, "1", 46, UITheme.TextPrimary, TextAnchor.MiddleCenter, UIFont.Title, TextGlow.Glow);
            UIFactory.Stretch(_rankNumber.rectTransform, 0f, 0f, 0f, 4f);

            RectTransform texts = UIFactory.CreateRect(badge, "Texts");
            VerticalLayoutGroup tv = UIFactory.VStack(texts, -2f, null, TextAnchor.MiddleLeft, true);
            tv.childForceExpandWidth = false;
            UIFactory.LabelLine(texts, Loc.T("menu.pilot_rank"), 28, UITheme.TextMuted, TextAnchor.MiddleLeft, UIFont.Title);
            _rankMissions = UIFactory.LabelLine(texts, Loc.T("menu.rank_missions", ("done", 0), ("total", MissionSystem.ActiveCount)), 36, UITheme.TextPrimary, TextAnchor.MiddleLeft, UIFont.Bold);
            MetaPressable.Attach(badge, MetaUI.ShowMissions, badge);

            _pill = UIFactory.CurrencyPill(_topLeft, 0, IconId.Gem, null, OpenStore);

            // Right: no-ads, gift, settings.
            _topRight = UIFactory.CreateRect(r, "TopRight");
            UIFactory.Anchor(_topRight, AnchorPreset.TopRight, new Vector2(-32f, -28f));
            _topRight.sizeDelta = new Vector2(420f, 120f);
            HorizontalLayoutGroup rh = UIFactory.HStack(_topRight, UITheme.SpaceSm, null, TextAnchor.UpperRight, true);
            rh.childForceExpandWidth = false;
            _topRight.gameObject.AddComponent<CanvasGroup>();

            _noAds = UIFactory.IconButton(_topRight, IconId.Ad, OnNoAds, 112f);
            RectTransform tag = UIFactory.CreateRect(_noAds.Rect, "Tag");
            UIFactory.IgnoreLayout(tag);
            UIFactory.Anchor(tag, AnchorPreset.BottomCenter, new Vector2(0f, -22f));
            tag.sizeDelta = new Vector2(132f, 40f);
            Image tagFill = UIFactory.PillImage(tag, UITheme.AccentYellow, "Fill");
            tagFill.raycastTarget = false;
            Text tagText = UIFactory.LabelLine(tag, Loc.T("menu.no_ads"), 24, UITheme.TextOnAccent, TextAnchor.MiddleCenter, UIFont.Title);
            UIFactory.Stretch(tagText.rectTransform, 0f, 0f, 0f, 2f);

            _gift = UIFactory.IconButton(_topRight, IconId.Gift, MetaUI.ShowDailyReward, 112f);
            _giftBadge = UIFactory.Badge(_gift.Rect, 0);

            _settings = UIFactory.IconButton(_topRight, IconId.Settings, MetaUI.ShowSettings, 112f);
        }

        private void BuildLogo(RectTransform r)
        {
            _logo = UIFactory.CreateRect(r, "Logo");
            UIFactory.Anchor(_logo, AnchorPreset.TopCenter, new Vector2(0f, -300f));
            _logo.sizeDelta = new Vector2(1000f, 330f);
            _logoGroup = _logo.gameObject.AddComponent<CanvasGroup>();
            _logoGroup.blocksRaycasts = false;

            _logoGlow = UIFactory.CreateImage(_logo, UISprites.Glow, UITheme.Accent, "Glow");
            UIFactory.Stretch(_logoGlow.rectTransform, -80f, -120f, -80f, -120f);
            UIAccentFollower.Attach(_logoGlow, AccentSlot.Primary, 0.28f);

            // The logo is the brand: never translated, always Audiowide.
            Font brand = UIFonts.For(LocScript.Latin, UIFont.Title);
            NeonTitle cube = UIFactory.NeonTitle(_logo, "BEAMRUNNER", UITheme.TextHero); // l10n-ok: brand
            cube.SetFont(brand);
            UIFactory.Anchor(cube.Rect, AnchorPreset.StretchTop, new Vector2(0f, 0f));
            cube.Rect.sizeDelta = new Vector2(0f, 200f);

            Image line = UIFactory.CreateImage(_logo, UISprites.FadeLine, UITheme.Accent, "Underline");
            UIAccentFollower.Attach(line, AccentSlot.Secondary, 0.8f);
            UIFactory.Anchor(line.rectTransform, AnchorPreset.TopCenter, new Vector2(0f, -205f));
            line.rectTransform.sizeDelta = new Vector2(620f, 4f);
        }

        private void BuildPlay(RectTransform r)
        {
            _hint = UIFactory.CreateRect(r, "Hint");
            UIFactory.Anchor(_hint, AnchorPreset.BottomCenter, new Vector2(0f, PlayBottom + 200f));
            _hint.sizeDelta = new Vector2(700f, 110f);
            _hintGroup = _hint.gameObject.AddComponent<CanvasGroup>();
            _hintGroup.blocksRaycasts = false;
            Text hintText = UIFactory.LabelLine(_hint, Loc.T("menu.tap_play"), UITheme.TextSmall, UITheme.TextPrimary, TextAnchor.MiddleCenter, UIFont.Title, TextGlow.Neon);
            UIFactory.Anchor(hintText.rectTransform, AnchorPreset.StretchTop);
            hintText.rectTransform.sizeDelta = new Vector2(0f, 56f);
            Image chevron = UIFactory.Icon(_hint, IconId.ChevronDown, 48f, UITheme.Accent);
            UIAccentFollower.Attach(chevron, AccentSlot.Primary);
            UIFactory.Anchor(chevron.rectTransform, AnchorPreset.BottomCenter, new Vector2(0f, 0f));

            _play = UIFactory.Button(r, Loc.T("menu.play"), ButtonStyle.Primary, OnPlay, IconId.Play, StartLevelText(1), 184f, 600f);
            UIFactory.Anchor(_play.Rect, AnchorPreset.BottomCenter, new Vector2(0f, PlayBottom));
            _play.Label.fontSize = 64;
            _play.SetLabel(Loc.T("menu.play"));
            _play.SetSubLabel(null);
            DefaultSelection = _play.gameObject;

            // Start-level picker either side of PLAY (shown once a later level has been reached).
            _levelDown = UIFactory.IconButton(r, IconId.ChevronLeft, () => StepStartLevel(-1), 120f, ButtonStyle.Secondary);
            UIFactory.Anchor(_levelDown.Rect, AnchorPreset.BottomCenter, new Vector2(-400f, PlayBottom + 32f));
            _levelUp = UIFactory.IconButton(r, IconId.ChevronRight, () => StepStartLevel(1), 120f, ButtonStyle.Secondary);
            UIFactory.Anchor(_levelUp.Rect, AnchorPreset.BottomCenter, new Vector2(400f, PlayBottom + 32f));
            _levelDown.gameObject.SetActive(false);
            _levelUp.gameObject.SetActive(false);

            _best = UIFactory.LabelLine(r, string.Empty, 40, UITheme.TextPrimary, TextAnchor.MiddleCenter, UIFont.Bold, TextGlow.Shadow);
            UIFactory.Anchor(_best.rectTransform, AnchorPreset.BottomCenter, new Vector2(0f, PlayBottom - 72f));
            _best.rectTransform.sizeDelta = new Vector2(800f, 56f);

            // Daily Run entry above PLAY, where the first-run hint sits (never both: see Refresh).
            _dailyRun = DailyRunCard.Create(r, PlayBottom + 212f);
        }

        private void BuildNav(RectTransform r)
        {
            RectTransform column = UIFactory.ContentColumn(r, 1000f, 32f);
            _nav = UIFactory.CreateRect(column, "Nav");
            UIFactory.Anchor(_nav, AnchorPreset.StretchBottom, new Vector2(0f, 36f));
            _nav.sizeDelta = new Vector2(0f, 236f);
            HorizontalLayoutGroup h = UIFactory.HStack(_nav, UITheme.SpaceMd, null, TextAnchor.MiddleCenter, true);
            h.childForceExpandWidth = true;
            h.childForceExpandHeight = true;

            NeonButton upgrades = NavTile(_nav, IconId.Bolt, Loc.T("nav.upgrades"), MetaUI.ShowUpgrades, out _upgradesSub);
            _upgradesBadge = UIFactory.Badge(upgrades.Rect, 0);
            NeonButton hangar = NavTile(_nav, IconId.Hangar, Loc.T("nav.hangar"), OnHangar, out _hangarSub);
            _hangarBadge = UIFactory.Badge(hangar.Rect, 0);
            NavTile(_nav, IconId.Missions, Loc.T("nav.missions"), MetaUI.ShowMissions, out _missionsSub);
            NavTile(_nav, IconId.Trophy, Loc.T("nav.records"), MetaUI.ShowRecords, out _recordsSub);
        }

        private static NeonButton NavTile(RectTransform parent, IconId icon, string label, System.Action onClick, out Text sub)
        {
            RectTransform tile = UIFactory.CreateRect(parent, "Nav_" + label);
            UIFactory.SetLayout(tile, 0f, 236f, 1f);
            VerticalLayoutGroup v = UIFactory.VStack(tile, 6f, null, TextAnchor.UpperCenter, true);
            v.childForceExpandWidth = false;
            NeonButton b = UIFactory.IconButton(tile, icon, onClick, UITheme.IconButtonSize);
            UIFactory.Spacer(tile, 4f);
            UIFactory.LabelLine(tile, label, 32, UITheme.TextPrimary, TextAnchor.MiddleCenter, UIFont.Title, TextGlow.Shadow);
            sub = UIFactory.LabelLine(tile, string.Empty, 30, UITheme.TextSecondary, TextAnchor.MiddleCenter, UIFont.Bold, TextGlow.Shadow);
            MetaPressable.Attach(tile, onClick, b.Visual);
            return b;
        }

        // ------------------------------------------------------------------ lifecycle

        protected override void OnShow()
        {
            base.OnShow();
            StartLoops();
            PlayEntrance();
        }

        protected override void OnHide()
        {
            base.OnHide();
            StopLoops();
        }

        protected override void ApplyLayout(bool landscape)
        {
            // Landscape canvases (desktop / editor) are only 1080 units tall: at its portrait spot (300 units down) the
            // 330-unit logo would sit right behind PLAY (440..624 units up). Lift it into the top band between the corner
            // clusters and shrink it.
            _logoScale = landscape ? 0.72f : 1f;
            _logo.anchoredPosition = new Vector2(0f, landscape ? -36f : -300f);
            UITween.Kill(_logo);
            _logo.localScale = new Vector3(_logoScale, _logoScale, 1f);
        }

        protected override void Refresh(bool showing)
        {
            ProfileService p = Profile;
            PlayerProfile data = p.Profile;
            bool animate = !showing || _shownOnce;

            // Rank + missions
            int rank = p.PilotRank;
            int done = CompletedMissions(data);
            int total = Mathf.Max(1, data.Missions != null && data.Missions.Active != null ? data.Missions.Active.Count : MissionSystem.ActiveCount);
            string rankText = rank.ToString(MetaUI.Inv);
            if (animate && _rankNumber.text != rankText)
            {
                UITween.Punch(_rankNumber.transform, 0.3f, 0.45f);
            }

            _rankNumber.text = rankText;
            _rankMissions.text = Loc.T("menu.rank_missions", ("done", done), ("total", total));
            _missionsSub.text = Loc.T("menu.missions_done", ("done", done), ("total", total));
            _missionsSub.color = done >= total ? UITheme.Success : UITheme.TextSecondary;

            // Wallet (+ count-up when coming back with more gems)
            _pill.SetAmount(p.Gems, animate);
            if (_pill.PlusButton != null)
            {
                _pill.PlusButton.gameObject.SetActive(Game.StoreAvailable);
            }

            // Gift / no-ads
            bool gift = p.DailyRewardAvailable;
            _giftBadge.SetCount(gift ? -1 : 0, animate);
            _gift.SetAttention(gift);
            // Not in a build without an ad network: the product would change nothing (Restore stays in the store).
            bool noAds = Game.StoreAvailable && Game.AdsAvailable && !p.AdsRemoved;
            if (_noAds.gameObject.activeSelf != noAds)
            {
                _noAds.gameObject.SetActive(noAds);
                if (noAds && animate)
                {
                    UIFactory.PopIn(_noAds.transform);
                }
            }

            _noAds.SetInteractable(!_buyingNoAds);

            // Best + first-run hint
            PlayerStats stats = data.Stats;
            long best = stats != null ? stats.BestScore : 0;
            bool firstTime = stats == null || stats.Runs == 0;
            _best.gameObject.SetActive(best > 0);
            _best.text = MetaUI.Tint(Loc.T("menu.best") + "  ", UITheme.TextMuted) + MetaUI.N(best);
            _dailyRun.Refresh(Game, animate);
            _hint.gameObject.SetActive(firstTime && !_dailyRun.IsShown);
            RefreshStartLevel(p, data);

            // Nav captions
            ShipDefinition ship = p.SelectedShip;
            _hangarSub.text = ship != null ? ship.DisplayName : string.Empty;
            _hangarBadge.SetCount(HasAffordableShip(p) ? -1 : 0, animate);
            Core.Balance.UpgradeId? next = p.CheapestUpgrade();
            bool canUpgrade = next != null && p.CanBuyUpgrade(next.Value);
            _upgradesBadge.SetCount(canUpgrade || Game.FreeUpgradeOffered ? -1 : 0, animate);
            Core.Balance.ShipStats craft = p.Ship;
            _upgradesSub.text = Loc.T("menu.upgrades_ship", ("fuel", Mathf.RoundToInt(craft.MaxFuel)), ("hull", craft.MaxHull));
            _upgradesSub.color = canUpgrade ? UITheme.Success : UITheme.TextSecondary;
            int unlocked = data.Achievements != null ? data.Achievements.Count : 0;
            _recordsSub.text = MetaUI.N(unlocked) + "/" + MetaUI.N(AchievementCatalog.All.Count);

            _shownOnce = true;
        }

        // ------------------------------------------------------------------ start level

        private static string StartLevelText(int level)
        {
            return Loc.T("menu.start_level", ("n", level), ("name", Loc.T(Levels.NameKey(Levels.KindOf(level)))));
        }

        private void RefreshStartLevel(ProfileService p, PlayerProfile data)
        {
            bool picker = p.MaxStartLevel > 1 && data.Flags != null && data.Flags.TutorialDone;
            int level = p.StartLevel;
            _levelDown.gameObject.SetActive(picker);
            _levelUp.gameObject.SetActive(picker);
            _levelDown.SetInteractable(level > 1);
            _levelUp.SetInteractable(level < p.MaxStartLevel);
            _play.SetSubLabel(picker ? StartLevelText(level) : null);
        }

        private void StepStartLevel(int step)
        {
            ProfileService p = Profile;
            if (p == null)
            {
                return;
            }

            int before = p.StartLevel;
            p.SetStartLevel(before + step);
            if (p.StartLevel != before)
            {
                Game.ApplySettings(); // persists the choice like any other setting
                UITween.Punch(_play.Rect, 0.06f, 0.3f);
            }

            RefreshStartLevel(p, p.Profile);
        }

        // ------------------------------------------------------------------ motion

        private void PlayEntrance()
        {
            if (UITheme.ReducedMotion)
            {
                return;
            }

            SlideIn(_topLeft, new Vector2(32f, -28f), new Vector2(-40f, 60f), 0.05f);
            SlideIn(_topRight, new Vector2(-32f, -28f), new Vector2(40f, 60f), 0.1f);

            UITween.Fade(_logoGroup, 0f, 1f, 0.5f).SetDelay(0.08f).SetEase(UIEase.OutCubic);
            UITween.Scale(_logo, 0.86f * _logoScale, _logoScale, 0.7f).SetDelay(0.08f).SetEase(UIEase.OutBack);

            UIFactory.PopIn(_play.transform, 0.22f, 0.5f);
            if (_dailyRun.IsShown)
            {
                UIFactory.PopIn(_dailyRun.transform, 0.28f, 0.45f);
            }

            UIFactory.StaggerIn(_nav, 0.3f, 0.07f);
        }

        private void SlideIn(RectTransform rt, Vector2 rest, Vector2 offset, float delay)
        {
            CanvasGroup g = rt.GetComponent<CanvasGroup>();
            if (g != null)
            {
                UITween.Fade(g, 0f, 1f, 0.4f).SetDelay(delay).SetEase(UIEase.OutCubic);
            }

            UITween.Move(rt, rest + offset, rest, 0.5f).SetDelay(delay).SetEase(UIEase.OutQuart);
        }

        private void StartLoops()
        {
            StopLoops();
            _logoGlowTween = UIFactory.Breathe(_logoGlow, 0.45f, 1f, 3.6f);
            _play.SetAttention(true);
            if (!UITheme.ReducedMotion)
            {
                // Start after the entrance pop (a new scale tween would replace it).
                _playPulse = UITween.Delay(this, 0.8f, StartPulse).Id;
            }

            if (_hint.gameObject.activeSelf)
            {
                _hintTween = UITween.Fade(_hintGroup, 0.35f, 1f, 0.9f).SetEase(UIEase.InOutSine).SetLoops(-1, true).Id;
                Vector2 rest = new Vector2(0f, PlayBottom + 200f);
                if (!UITheme.ReducedMotion)
                {
                    _hintBob = UITween.Move(_hint, rest, rest + new Vector2(0f, -14f), 0.9f).SetEase(UIEase.InOutSine).SetLoops(-1, true).Id;
                }
            }
        }

        private void StartPulse()
        {
            if (_play == null || !IsVisible)
            {
                return;
            }

            _playPulse = UITween.Scale(_play.transform, 1f, 1.045f, 0.85f).SetEase(UIEase.InOutSine).SetLoops(-1, true).Id;
        }

        private void StopLoops()
        {
            UITween.KillId(_logoGlowTween);
            UITween.KillId(_playPulse);
            UITween.KillId(_hintTween);
            UITween.KillId(_hintBob);
            _logoGlowTween = 0;
            _playPulse = 0;
            _hintTween = 0;
            _hintBob = 0;
            if (_play != null)
            {
                _play.transform.localScale = Vector3.one;
            }
        }

        // ------------------------------------------------------------------ actions

        private void OnPlay()
        {
            if (Game != null)
            {
                Game.StartRun();
            }
        }

        private void OnHangar()
        {
            if (Game != null)
            {
                Game.EnterHangar();
            }
        }

        private void OpenStore()
        {
            if (Game == null)
            {
                return;
            }

            HangarScreen.RequestStoreOnShow();
            Game.EnterHangar();
        }

        private void OnNoAds()
        {
            if (!HasData || _buyingNoAds)
            {
                return;
            }

            if (!Game.StoreAvailable)
            {
                MetaUI.Toast(Loc.T("store.unavailable_toast"), IconId.Warning);
                return;
            }

            string price = Game.GetPrice(ProductIds.RemoveAds);
            string body = StoreTexts.Description(ProductIds.RemoveAds, Game.Config);
            string confirm = string.IsNullOrEmpty(price) ? Loc.T("common.buy") : Loc.T("common.buy_price", ("price", price));
            Dialogs.Confirm(StoreTexts.Title(ProductIds.RemoveAds, Game.Config), body, confirm, Loc.T("common.not_now"), ok =>
            {
                if (ok)
                {
                    BuyNoAds();
                }
            }, false, IconId.Crown);
        }

        private void BuyNoAds()
        {
            if (Game == null)
            {
                return;
            }

            _buyingNoAds = true;
            _noAds.SetInteractable(false);
            Game.BuyProduct(ProductIds.RemoveAds, result =>
            {
                _buyingNoAds = false;
                if (this == null)
                {
                    return;
                }

                if (result.IsSuccess && IsVisible)
                {
                    MetaFx.BurstAt(Rect, _noAds.transform, UITheme.AccentYellow, 16, 220f);
                }

                MarkDirty();
            });
        }

        // ------------------------------------------------------------------ helpers

        private static int CompletedMissions(PlayerProfile data)
        {
            if (data.Missions == null || data.Missions.Active == null)
            {
                return 0;
            }

            int n = 0;
            List<Mission> active = data.Missions.Active;
            for (int i = 0; i < active.Count; i++)
            {
                if (active[i] != null && active[i].Completed)
                {
                    n++;
                }
            }

            return n;
        }

        private static bool HasAffordableShip(ProfileService p)
        {
            IReadOnlyList<ShipDefinition> ships = ShipCatalog.All;
            for (int i = 0; i < ships.Count; i++)
            {
                ShipDefinition s = ships[i];
                if (p.Owns(s.Id))
                {
                    continue;
                }

                if (p.CanUnlockNow(s))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
