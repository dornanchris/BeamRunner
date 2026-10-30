using System.Collections;
using System.Collections.Generic;
using BeamRunner.Core.Localization;
using BeamRunner.Core.Meta;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BeamRunner.UI
{
    /// <summary>
    /// Daily reward popup (Popups layer): seven day tiles (earlier days checked, today highlighted, day 7 special),
    /// CLAIM with a gem burst and balance count-up, or "come back tomorrow" with the next reward once claimed.
    /// </summary>
    public sealed class DailyRewardScreen : MetaScreen
    {
        private sealed class Tile
        {
            public RectTransform Rect;
            public Image Plate;
            public Image Frame;
            public Image Glow;
            public Text Day;
            public Image Icon;
            public Text Amount;
            public RectTransform Check;
            public int GlowTween;
        }

        private readonly List<Tile> _tiles = new List<Tile>();

        private Card _panel;
        private RectTransform _panelRt;
        private Image _giftGlow;
        private int _giftGlowTween;
        private RectTransform _giftIcon;
        private Text _subtitle;
        private Text _message;
        private RectTransform _nextRow;
        private Text _nextAmount;
        private Text _balance;
        private NumberTicker _balanceTicker;
        private NeonButton _claim;
        private NeonButton _done;
        private bool _claimedThisVisit;

        // Header, tiles, footer: three steps for the build queue (UIScreen.BuildSteps).
        protected override IEnumerator BuildSteps()
        {
            Transition = ScreenTransition.Zoom;
            TransitionDuration = 0.32f;
            RectTransform r = Rect;
            MetaUI.Dimmer(r, 0.78f);

            // Scale wrapper: the card is ~1420 units tall, so on 1080-unit landscape canvases (desktop / editor) CLAIM
            // and the close button would be off-screen. UIFitHeight (attached at the end of Build) shrinks it to fit.
            RectTransform fit = UIFactory.CreateFill(r, "Fit");
            _panel = UIFactory.Card(fit, null, IconId.None, UITheme.Gold, 44f);
            _panelRt = _panel.Rect;
            UIFactory.Anchor(_panelRt, AnchorPreset.Center);
            _panelRt.sizeDelta = new Vector2(960f, 1300f);
            UIFactory.FitContent(_panel, false, true);
            VerticalLayoutGroup pv = _panel.GetComponent<VerticalLayoutGroup>();
            if (pv != null)
            {
                pv.childAlignment = TextAnchor.UpperCenter;
            }

            RectTransform body = _panel.Body;
            VerticalLayoutGroup bv = body.GetComponent<VerticalLayoutGroup>();
            if (bv != null)
            {
                bv.childAlignment = TextAnchor.UpperCenter;
                bv.spacing = UITheme.SpaceMd;
            }

            NeonButton close = UIFactory.IconButton(_panelRt, IconId.Close, MetaUI.CloseTop, 100f, ButtonStyle.Ghost);
            UIFactory.IgnoreLayout(close);
            UIFactory.Anchor(close.Rect, AnchorPreset.TopRight, new Vector2(-16f, -16f));

            // Header: glowing gift, title, subtitle
            RectTransform gift = UIFactory.CreateRect(body, "Gift");
            UIFactory.SetLayout(gift, -1f, 150f);
            _giftGlow = UIFactory.CreateImage(gift, UISprites.Glow, UITheme.Alpha(UITheme.Gold, 0.55f), "Glow");
            UIFactory.Anchor(_giftGlow.rectTransform, AnchorPreset.Center);
            _giftGlow.rectTransform.sizeDelta = new Vector2(300f, 300f);
            Image giftIcon = UIFactory.Icon(gift, IconId.Gift, 132f, UITheme.Gold);
            UIFactory.Anchor(giftIcon.rectTransform, AnchorPreset.Center);
            _giftIcon = giftIcon.rectTransform;

            NeonTitle title = UIFactory.NeonTitle(body, Loc.T("reward.title"), UITheme.TextH2, TextAnchor.MiddleCenter, UITheme.Gold, UITheme.AccentMagenta);
            UIFactory.SetLayout(title, -1f, 100f);
            _subtitle = UIFactory.Label(body, Loc.T("reward.subtitle"), UITheme.TextBody, UITheme.TextSecondary,
                TextAnchor.MiddleCenter, UIFont.Body);

            yield return null;

            // Tiles: 4 on top, 2 + the wide day-7 tile below.
            RectTransform rows = UIFactory.CreateRect(body, "Tiles");
            VerticalLayoutGroup rv = UIFactory.VStack(rows, UITheme.SpaceSm, UIFactory.Padding(0, 0, 8, 8), TextAnchor.UpperCenter, true);
            rv.childForceExpandWidth = false;
            RectTransform row1 = TileRow(rows);
            RectTransform row2 = TileRow(rows);
            int n = DailyRewards.Rewards.Length;
            for (int i = 0; i < n; i++)
            {
                bool last = i == n - 1;
                _tiles.Add(BuildTile(i < 4 ? row1 : row2, i, last));
            }

            yield return null;

            // Message + next reward + balance
            _message = UIFactory.Label(body, string.Empty, UITheme.TextLead, UITheme.TextPrimary, TextAnchor.MiddleCenter, UIFont.Title, TextGlow.Shadow);

            _nextRow = UIFactory.CreateRect(body, "Next");
            HorizontalLayoutGroup nh = UIFactory.HStack(_nextRow, 10f, null, TextAnchor.MiddleCenter, true);
            nh.childForceExpandWidth = false;
            UIFactory.SetLayout(_nextRow, -1f, 60f);
            UIFactory.LabelLine(_nextRow, Loc.T("reward.come_back"), 36, UITheme.TextSecondary, TextAnchor.MiddleLeft, UIFont.Bold);
            UIFactory.Icon(_nextRow, IconId.Gem, 44f, UITheme.GemColor);
            _nextAmount = UIFactory.LabelLine(_nextRow, "0", 42, UITheme.GemColor, TextAnchor.MiddleLeft, UIFont.Title, TextGlow.Shadow);

            RectTransform balanceRow = UIFactory.CreateRect(body, "Balance");
            HorizontalLayoutGroup bh = UIFactory.HStack(balanceRow, 10f, null, TextAnchor.MiddleCenter, true);
            bh.childForceExpandWidth = false;
            UIFactory.SetLayout(balanceRow, -1f, 56f);
            UIFactory.LabelLine(balanceRow, Loc.T("reward.balance"), 32, UITheme.TextMuted, TextAnchor.MiddleLeft, UIFont.Bold);
            UIFactory.Icon(balanceRow, IconId.Gem, 40f, UITheme.GemColor);
            _balance = UIFactory.LabelLine(balanceRow, "0", 40, UITheme.TextPrimary, TextAnchor.MiddleLeft, UIFont.Title);
            _balanceTicker = NumberTicker.Attach(_balance, 0);

            RectTransform buttons = UIFactory.CreateRect(body, "Buttons");
            HorizontalLayoutGroup bb = UIFactory.HStack(buttons, 0f, UIFactory.Padding(0, 0, 8, 0), TextAnchor.MiddleCenter, true);
            bb.childForceExpandWidth = false;
            UIFactory.SetLayout(buttons, -1f, UITheme.ButtonHeightLarge + 16f);
            _claim = UIFactory.Button(buttons, Loc.T("reward.claim"), ButtonStyle.Primary, OnClaim, IconId.Gift, null, UITheme.ButtonHeightLarge, 560f);
            _done = UIFactory.Button(buttons, Loc.T("reward.awesome"), ButtonStyle.Secondary, MetaUI.CloseTop, IconId.Check, null, UITheme.ButtonHeight, 480f);

            UIFitHeight.Attach(fit, _panelRt);
        }

        private static RectTransform TileRow(RectTransform parent)
        {
            RectTransform row = UIFactory.CreateRect(parent, "Row");
            HorizontalLayoutGroup h = UIFactory.HStack(row, UITheme.SpaceSm, null, TextAnchor.MiddleCenter, true);
            h.childForceExpandWidth = false;
            return row;
        }

        private static Tile BuildTile(RectTransform row, int index, bool special)
        {
            var t = new Tile();
            float w = special ? 2f * 196f + UITheme.SpaceSm : 196f;
            const float h = 236f;
            t.Rect = UIFactory.CreateRect(row, "Day" + (index + 1));
            t.Rect.sizeDelta = new Vector2(w, h);
            UIFactory.SetLayout(t.Rect, w, h);

            t.Glow = UIFactory.CreateImage(t.Rect, null, UITheme.Gold, "Glow");
            UIFactory.Stretch(t.Glow.rectTransform, -26f, -26f, -26f, -26f);
            UISprites.ApplySoft(t.Glow, UITheme.RadiusMd + 26f, 30f);
            t.Glow.canvasRenderer.SetAlpha(0f);
            t.Plate = UIFactory.RoundedImage(t.Rect, UITheme.RadiusMd, UITheme.PanelGlassLight, "Plate");
            t.Frame = UIFactory.CreateImage(t.Rect, null, UITheme.PanelBorder, "Frame");
            UIFactory.Stretch(t.Frame.rectTransform);
            UISprites.ApplyOutline(t.Frame, UITheme.RadiusMd, 3f);

            t.Day = UIFactory.LabelLine(t.Rect, Loc.T("reward.day_n", ("day", index + 1)), 28, UITheme.TextSecondary, TextAnchor.MiddleCenter, UIFont.Title);
            UIFactory.Anchor(t.Day.rectTransform, AnchorPreset.StretchTop, new Vector2(0f, -14f));
            t.Day.rectTransform.sizeDelta = new Vector2(0f, 40f);

            t.Icon = UIFactory.Icon(t.Rect, special ? IconId.Crown : IconId.Gem, special ? 96f : 76f, special ? UITheme.Gold : UITheme.GemColor);
            UIFactory.Anchor(t.Icon.rectTransform, AnchorPreset.Center, new Vector2(0f, 8f));

            int reward = DailyRewards.Rewards[index];
            t.Amount = UIFactory.LabelLine(t.Rect, MetaUI.N(reward), special ? 48 : 40, UITheme.TextPrimary, TextAnchor.MiddleCenter, UIFont.Title, TextGlow.Shadow);
            UIFactory.Anchor(t.Amount.rectTransform, AnchorPreset.StretchBottom, new Vector2(0f, 14f));
            t.Amount.rectTransform.sizeDelta = new Vector2(0f, 56f);

            t.Check = UIFactory.CreateRect(t.Rect, "Check");
            UIFactory.Anchor(t.Check, AnchorPreset.TopRight, new Vector2(12f, 12f));
            t.Check.sizeDelta = new Vector2(56f, 56f);
            Image disc = UIFactory.CreateImage(t.Check, UISprites.Circle, UITheme.Success, "Disc");
            UIFactory.Stretch(disc.rectTransform);
            Image tick = UIFactory.Icon(t.Check, IconId.Check, 36f, UITheme.TextOnAccent);
            UIFactory.Stretch(tick.rectTransform, 10f, 10f, 10f, 10f);
            t.Check.gameObject.SetActive(false);
            return t;
        }

        // ------------------------------------------------------------------ lifecycle

        protected override void OnShow()
        {
            _claimedThisVisit = false;
            base.OnShow();
            UITween.KillId(_giftGlowTween);
            _giftGlowTween = UIFactory.Breathe(_giftGlow, 0.35f, 1f, 2.2f);
            if (!UITheme.ReducedMotion)
            {
                UITween.Scale(_panelRt, 0.85f, 1f, 0.45f).SetEase(UIEase.OutBack);
                UIFactory.PopIn(_giftIcon, 0.12f, 0.5f);
                for (int i = 0; i < _tiles.Count; i++)
                {
                    UITween.Scale(_tiles[i].Rect, 0.6f, 1f, 0.4f).SetDelay(0.1f + i * 0.045f).SetEase(UIEase.OutBack);
                }
            }
        }

        protected override void OnHide()
        {
            base.OnHide();
            UITween.KillId(_giftGlowTween);
            _giftGlowTween = 0;
            for (int i = 0; i < _tiles.Count; i++)
            {
                UITween.KillId(_tiles[i].GlowTween);
                _tiles[i].GlowTween = 0;
            }
        }

        protected override void Refresh(bool showing)
        {
            ProfileService p = Profile;
            bool available = p.DailyRewardAvailable;
            int today = Mathf.Clamp(p.DailyRewardDayIndex, 0, DailyRewards.Rewards.Length - 1);

            for (int i = 0; i < _tiles.Count; i++)
            {
                BindTile(_tiles[i], i, today, available);
            }

            if (showing || !_claimedThisVisit)
            {
                _balanceTicker.SetValue(p.Gems, !showing);
            }

            _claim.gameObject.SetActive(available);
            _done.gameObject.SetActive(!available);
            _done.SetLabel(_claimedThisVisit ? Loc.T("reward.awesome") : Loc.T("common.ok"));
            _nextRow.gameObject.SetActive(!available);
            if (available)
            {
                int amount = DailyRewards.Rewards[today];
                _message.text = Loc.T("reward.day_reward", ("day", today + 1), ("n", amount));
                _message.color = UITheme.TextPrimary;
                _claim.SetSubLabel(Loc.T("reward.claim_sub", ("n", amount)));
                _claim.SetAttention(true);
                DefaultSelection = _claim.gameObject;
            }
            else
            {
                int next = today + 1 < DailyRewards.Rewards.Length ? DailyRewards.Rewards[today + 1] : DailyRewards.Rewards[0];
                _message.text = _claimedThisVisit ? Loc.T("reward.claimed") : Loc.T("reward.already_claimed");
                _message.color = _claimedThisVisit ? UITheme.Success : UITheme.TextPrimary;
                _nextAmount.text = MetaUI.N(next);
                _claim.SetAttention(false);
                DefaultSelection = _done.gameObject;
            }
        }

        private void BindTile(Tile t, int index, int today, bool available)
        {
            bool special = index == _tiles.Count - 1;
            bool claimed = index < today || (index == today && !available);
            bool isToday = index == today;
            Color accent = special ? UITheme.Gold : UITheme.GemColor;

            t.Check.gameObject.SetActive(claimed);
            t.Plate.color = isToday
                ? UITheme.Alpha(accent, 0.22f)
                : (special ? UITheme.Alpha(UITheme.Gold, 0.12f) : UITheme.PanelGlassLight);
            t.Frame.color = isToday ? accent : (special ? UITheme.Alpha(UITheme.Gold, 0.55f) : UITheme.PanelBorder);
            t.Day.text = isToday ? Loc.T("reward.today") : Loc.T("reward.day_n", ("day", index + 1));
            t.Day.color = isToday ? UITheme.TextPrimary : UITheme.TextSecondary;
            float dim = claimed && !isToday ? 0.45f : (index > today ? 0.8f : 1f);
            t.Icon.canvasRenderer.SetAlpha(dim);
            t.Amount.canvasRenderer.SetAlpha(dim);

            UITween.KillId(t.GlowTween);
            t.GlowTween = 0;
            t.Glow.color = accent;
            if (isToday && available)
            {
                t.GlowTween = UIFactory.Breathe(t.Glow, 0.25f, 0.9f, 1.4f);
            }
            else
            {
                t.Glow.canvasRenderer.SetAlpha(isToday ? 0.35f : 0f);
            }
        }

        // ------------------------------------------------------------------ claim

        private void OnClaim()
        {
            if (!HasData)
            {
                return;
            }

            int today = Mathf.Clamp(Profile.DailyRewardDayIndex, 0, _tiles.Count - 1);
            long before = Profile.Gems;
            int amount = Game.ClaimDailyReward();
            if (amount <= 0)
            {
                MarkDirty();
                return;
            }

            _claimedThisVisit = true;
            Tile tile = _tiles[today];
            Color c = today == _tiles.Count - 1 ? UITheme.Gold : UITheme.GemColor;

            MetaFx.BurstAt(Rect, tile.Rect, c, 28, 360f);
            MetaFx.FloatAt(Rect, tile.Rect, "+" + MetaUI.N(amount), c, 150f, 1.5f);
            UITween.Punch(tile.Rect, 0.25f, 0.5f);
            UITween.Punch(_giftIcon, 0.3f, 0.5f);
            MetaUI.Flash(c, 0.2f, ReduceFlashing);

            _balanceTicker.SetValue(before, false);
            _balanceTicker.SetValue(Profile.Gems, true, 1.1f);
            UITween.Punch(_balance.transform, 0.25f, 0.5f).SetDelay(0.9f);

            Refresh(false);

            // CLAIM just hid itself: keep keyboard/gamepad focus inside the popup (it is not on the UI stack, so the stack
            // would otherwise refocus the menu's PLAY behind the dimmer).
            EventSystem es = EventSystem.current;
            UIRoot root = UIRoot.Instance;
            if (es != null && root != null && root.Stack.IsNavigating && es.currentSelectedGameObject == _claim.gameObject)
            {
                es.SetSelectedGameObject(_done.gameObject);
            }

            UIFactory.PopIn(tile.Check, 0.15f);
            UIFactory.PopIn(_done.transform, 0.2f);
        }
    }
}
