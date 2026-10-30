using System.Collections.Generic;
using BeamRunner.Core.Localization;
using BeamRunner.Game;
using BeamRunner.Services;
using UnityEngine;
using UnityEngine.UI;

namespace BeamRunner.UI
{
    /// <summary>
    /// Modal gem store shown inside the hangar: gem packs from the monetization config (localized prices),
    /// REMOVE ADS (until owned), RESTORE PURCHASES where the platform needs it, busy states while the store works, and
    /// "connecting" / "unavailable" states. Granting happens in the controller; this only starts purchases and celebrates.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GemStorePanel : MonoBehaviour
    {
        private sealed class Row
        {
            public string ProductId;
            public bool RemovesAds;
            public RectTransform Rect;
            public NeonButton Buy;
        }

        private readonly List<Row> _rows = new List<Row>();

        private GameController _game;
        private RectTransform _root;
        private CanvasGroup _group;
        private GlassPanel _panel;
        private RectTransform _panelRt;
        private ScrollList _list;
        private Text _balance;
        private NumberTicker _balanceTicker;
        private RectTransform _unavailable;
        private Image _unavailableIcon;
        private Text _unavailableTitle;
        private Text _unavailableBody;
        private NeonButton _retry;
        private NeonButton _restore;
        private bool _open;
        private bool _rowsBuilt;
        private bool _busy;
        private string _busyId;
        private bool _restoring;
        private int _closeTween;

        /// <summary>True while the panel is open (or opening).</summary>
        public bool IsOpen
        {
            get { return _open; }
        }

        /// <summary>Builds the (hidden) panel as a full-size child of <paramref name="parent"/>.</summary>
        public static GemStorePanel Create(RectTransform parent)
        {
            RectTransform rt = UIFactory.CreateFill(parent, "GemStore");
            GemStorePanel p = rt.gameObject.AddComponent<GemStorePanel>();
            p.Build(rt);
            rt.gameObject.SetActive(false);
            return p;
        }

        public void Open(GameController game, bool animate = true)
        {
            _game = game;
            UITween.KillId(_closeTween);
            _closeTween = 0;
            bool wasOpen = _open;
            _open = true;
            _root.SetAsLastSibling();
            _root.gameObject.SetActive(true);
            _group.blocksRaycasts = true;
            _group.interactable = true;
            EnsureRows();
            Refresh(false);
            if (_game != null && _game.Profile != null)
            {
                _balanceTicker.SetValue(_game.Profile.Gems, false);
            }

            _list.ScrollToTop();
            if (!animate || wasOpen)
            {
                _group.alpha = 1f;
                _panelRt.localScale = Vector3.one;
                return;
            }

            UITween.Fade(_group, 0f, 1f, 0.22f).SetEase(UIEase.OutCubic);
            UITween.Scale(_panelRt, 0.92f, 1f, 0.4f).SetEase(UIEase.OutBack);
            UIFactory.StaggerIn(_list.Content, 0.1f, 0.05f);
        }

        public void Close(bool instant = false)
        {
            if (!_open)
            {
                return;
            }

            _open = false;
            _group.blocksRaycasts = false;
            _group.interactable = false;
            if (instant || !_root.gameObject.activeInHierarchy)
            {
                _root.gameObject.SetActive(false);
                return;
            }

            UITween.Scale(_panelRt, 1f, 0.95f, 0.16f).SetEase(UIEase.InQuad);
            _closeTween = UITween.Fade(_group, 0f, 0.18f).SetEase(UIEase.InQuad).OnComplete(() =>
            {
                if (!_open && _root != null)
                {
                    _root.gameObject.SetActive(false);
                }
            }).Id;
        }

        /// <summary>Re-reads availability, prices, ownership and the balance.</summary>
        public void Refresh(bool animateBalance = true)
        {
            if (!_open || _game == null)
            {
                return;
            }

            IIapService iap = _game.Iap;
            bool available = _game.StoreAvailable;
            bool initializing = iap != null && iap.Initializing;
            bool purchaseRunning = _busy || _restoring || (iap != null && iap.PurchaseInProgress);

            if (_game.Profile != null)
            {
                _balanceTicker.SetValue(_game.Profile.Gems, animateBalance);
            }

            _list.gameObject.SetActive(available);
            _unavailable.gameObject.SetActive(!available);
            if (!available)
            {
                _unavailableTitle.text = initializing ? Loc.T("store.connecting") : Loc.T("store.unavailable");
                _unavailableBody.text = initializing ? Loc.T("store.connecting_body") : Loc.T("store.unavailable_body");
                UIFactory.SetIcon(_unavailableIcon, initializing ? IconId.Restore : IconId.Warning);
                _unavailableIcon.color = initializing ? UITheme.AccentCyan : UITheme.Warning;
                _retry.gameObject.SetActive(!initializing && iap != null && ServiceHub.IsInitialized);
            }

            bool adsOwned = (_game.Profile != null && _game.Profile.AdsRemoved) || (iap != null && iap.Owns(ProductIds.RemoveAds));
            for (int i = 0; i < _rows.Count; i++)
            {
                Row row = _rows[i];
                // Remove Ads: hidden once owned, and in a build without an ad network (it would change nothing).
                row.Rect.gameObject.SetActive(!row.RemovesAds || (!adsOwned && _game.AdsAvailable));
                bool busyHere = _busy && _busyId == row.ProductId;
                string price = _game.GetPrice(row.ProductId);
                row.Buy.SetLabel(busyHere ? "..." : (string.IsNullOrEmpty(price) ? Loc.T("common.buy") : price));
                row.Buy.SetInteractable(available && !purchaseRunning);
            }

            bool restore = iap != null && iap.SupportsRestore && available;
            _restore.gameObject.SetActive(restore);
            _restore.SetLabel(_restoring ? Loc.T("store.restoring") : Loc.T("store.restore"));
            _restore.SetInteractable(!purchaseRunning);
        }

        // ------------------------------------------------------------------ construction

        private void Build(RectTransform root)
        {
            _root = root;
            _group = root.gameObject.AddComponent<CanvasGroup>();

            Image dim = MetaUI.Dimmer(root, 0.82f);
            MetaPressable.Attach(dim.rectTransform, () => Close(), null, false);

            RectTransform column = UIFactory.ContentColumn(root, 1000f, 32f);
            _panel = UIFactory.GlassPanel(column, UITheme.GemColor, UITheme.RadiusXl, 40f);
            _panelRt = _panel.Rect;
            UIFactory.Stretch(_panelRt, 0f, 56f, 0f, 48f);
            RectTransform c = _panel.Content;

            // Header
            NeonTitle title = UIFactory.NeonTitle(c, Loc.T("store.title"), UITheme.TextH2, TextAnchor.MiddleCenter, UITheme.GemColor, UITheme.AccentMagenta);
            UIFactory.Anchor(title.Rect, AnchorPreset.StretchTop, new Vector2(0f, 0f));
            title.Rect.sizeDelta = new Vector2(0f, 120f);
            title.Rect.offsetMin = new Vector2(130f, title.Rect.offsetMin.y);
            title.Rect.offsetMax = new Vector2(-130f, title.Rect.offsetMax.y);
            NeonButton close = UIFactory.IconButton(c, IconId.Close, () => Close(), 104f, ButtonStyle.Ghost);
            UIFactory.Anchor(close.Rect, AnchorPreset.TopRight, new Vector2(0f, 8f));

            RectTransform balanceRow = UIFactory.CreateRect(c, "Balance");
            UIFactory.Anchor(balanceRow, AnchorPreset.TopCenter, new Vector2(0f, -124f));
            balanceRow.sizeDelta = new Vector2(700f, 64f);
            HorizontalLayoutGroup bh = UIFactory.HStack(balanceRow, 12f, null, TextAnchor.MiddleCenter, true);
            bh.childForceExpandWidth = false;
            UIFactory.LabelLine(balanceRow, Loc.T("store.you_have"), 34, UITheme.TextMuted, TextAnchor.MiddleLeft, UIFont.Bold);
            UIFactory.Icon(balanceRow, IconId.Gem, 44f, UITheme.GemColor);
            _balance = UIFactory.LabelLine(balanceRow, "0", 44, UITheme.TextPrimary, TextAnchor.MiddleLeft, UIFont.Title, TextGlow.Shadow);
            _balanceTicker = NumberTicker.Attach(_balance, 0);

            // Product list
            _list = UIFactory.ScrollList(c, UITheme.SpaceMd, UIFactory.Padding(36, 36, 20, 40), 40f);
            UIFactory.Stretch(_list.Rect, -40f, 204f, -40f, 116f);

            // Unavailable / connecting state
            _unavailable = UIFactory.CreateRect(c, "Unavailable");
            UIFactory.Stretch(_unavailable, 0f, 220f, 0f, 120f);
            VerticalLayoutGroup uv = UIFactory.VStack(_unavailable, UITheme.SpaceMd, UIFactory.Padding(24, 24, 0, 0), TextAnchor.MiddleCenter, true);
            uv.childForceExpandWidth = true;
            _unavailableIcon = UIFactory.Icon(_unavailable, IconId.Warning, 120f, UITheme.Warning);
            _unavailableIcon.preserveAspect = true;
            _unavailableTitle = UIFactory.Label(_unavailable, Loc.T("store.unavailable"), UITheme.TextH3, UITheme.TextPrimary, TextAnchor.MiddleCenter, UIFont.Title, TextGlow.Shadow);
            _unavailableBody = UIFactory.Label(_unavailable, string.Empty, UITheme.TextBody, UITheme.TextSecondary, TextAnchor.MiddleCenter, UIFont.Body);
            RectTransform retryRow = UIFactory.CreateRect(_unavailable, "RetryRow");
            UIFactory.HStack(retryRow, 0f, null, TextAnchor.MiddleCenter, true).childForceExpandWidth = false;
            UIFactory.SetLayout(retryRow, -1f, UITheme.ButtonHeight + 24f);
            _retry = UIFactory.Button(retryRow, Loc.T("common.try_again"), ButtonStyle.Secondary, OnRetry, IconId.Retry);

            // Footer
            _restore = UIFactory.Button(c, Loc.T("store.restore"), ButtonStyle.Ghost, OnRestore, IconId.Restore);
            UIFactory.Anchor(_restore.Rect, AnchorPreset.BottomCenter, new Vector2(0f, 0f));
        }

        private void EnsureRows()
        {
            if (_rowsBuilt || _game == null || _game.Config == null)
            {
                return;
            }

            _rowsBuilt = true;
            IReadOnlyList<IapProductConfig> products = _game.Config.Products;
            var packs = new List<IapProductConfig>();
            IapProductConfig removeAds = null;
            for (int i = 0; i < products.Count; i++)
            {
                IapProductConfig p = products[i];
                if (p == null || string.IsNullOrEmpty(p.Id))
                {
                    continue;
                }

                if (p.Id == ProductIds.RemoveAds)
                {
                    removeAds = p;
                }
                else if (p.IsConsumable && p.Gems > 0)
                {
                    packs.Add(p);
                }
            }

            packs.Sort((a, b) => a.Gems.CompareTo(b.Gems));
            for (int i = 0; i < packs.Count; i++)
            {
                string tag = null;
                Color tagColor = UITheme.Gold;
                if (packs.Count >= 2 && i == packs.Count - 1)
                {
                    tag = Loc.T("store.best_value");
                }
                else if (packs.Count >= 3 && i == packs.Count - 2)
                {
                    tag = Loc.T("store.popular");
                    tagColor = UITheme.AccentMagenta;
                }

                AddPackRow(packs[i], i, packs.Count, tag, tagColor);
            }

            if (removeAds != null)
            {
                AddRemoveAdsRow(removeAds);
            }

            if (packs.Count == 0 && removeAds == null)
            {
                UIFactory.Label(_list.Content, Loc.T("store.empty"), UITheme.TextBody, UITheme.TextSecondary, TextAnchor.MiddleCenter);
            }
        }

        private void AddPackRow(IapProductConfig product, int tier, int tiers, string tag, Color tagColor)
        {
            Card card = UIFactory.Card(_list.Content, null, IconId.None, tag != null ? tagColor : (Color?)null, 28f);
            RectTransform row = RowLayout(card.Body);

            // Pack art: a glowing cluster that grows with the tier.
            RectTransform art = UIFactory.CreateRect(row, "Art");
            UIFactory.SetLayout(art, 150f, 150f);
            Image glow = UIFactory.CreateImage(art, UISprites.Glow, UITheme.Alpha(UITheme.GemColor, 0.35f + 0.15f * tier), "Glow");
            UIFactory.Stretch(glow.rectTransform, -40f, -40f, -40f, -40f);
            int extra = Mathf.Clamp(tier, 0, 2);
            if (extra >= 1)
            {
                ArtGem(art, new Vector2(-40f, -26f), 58f, -18f);
                ArtGem(art, new Vector2(42f, -30f), 54f, 22f);
            }

            if (extra >= 2)
            {
                ArtGem(art, new Vector2(-6f, 46f), 50f, 8f);
            }

            ArtGem(art, Vector2.zero, 96f + 8f * tier, 0f);

            RectTransform texts = TextColumn(row);
            if (tag != null)
            {
                RectTransform chip = UIFactory.CreateRect(texts, "Tag");
                HorizontalLayoutGroup ch = UIFactory.HStack(chip, 0f, null, TextAnchor.MiddleLeft, true);
                ch.childForceExpandWidth = false;
                UIFactory.SetLayout(chip, -1f, 44f);
                RectTransform pill = UIFactory.CreateRect(chip, "Pill");
                HorizontalLayoutGroup ph = UIFactory.HStack(pill, 0f, UIFactory.Padding(18, 18, 0, 0), TextAnchor.MiddleCenter, true);
                ph.childForceExpandWidth = false;
                Image pillFill = UIFactory.PillImage(pill, tagColor, "Fill");
                UIFactory.IgnoreLayout(pillFill);
                UIFactory.LabelLine(pill, tag, 24, UITheme.TextOnAccent, TextAnchor.MiddleCenter, UIFont.Title);
            }

            UIFactory.Label(texts, StoreTexts.Title(product.Id, _game != null ? _game.Config : null), UITheme.TextLead,
                UITheme.TextPrimary, TextAnchor.MiddleLeft, UIFont.Title, TextGlow.Shadow);
            MetaUI.GemAmount(texts, MetaUI.N(product.Gems), 42, UITheme.GemColor, 1f, UIFont.Title);

            AddBuyButton(row, card.Rect, product.Id, false);
        }

        private void AddRemoveAdsRow(IapProductConfig product)
        {
            Card card = UIFactory.Card(_list.Content, null, IconId.None, UITheme.AccentYellow, 28f);
            RectTransform row = RowLayout(card.Body);

            RectTransform art = UIFactory.CreateRect(row, "Art");
            UIFactory.SetLayout(art, 150f, 150f);
            Image glow = UIFactory.CreateImage(art, UISprites.Glow, UITheme.Alpha(UITheme.AccentYellow, 0.45f), "Glow");
            UIFactory.Stretch(glow.rectTransform, -40f, -40f, -40f, -40f);
            Image ad = UIFactory.Icon(art, IconId.Ad, 104f, UITheme.TextPrimary);
            UIFactory.Stretch(ad.rectTransform, 23f, 23f, 23f, 23f);
            Image crown = UIFactory.Icon(art, IconId.Crown, 64f, UITheme.Gold);
            UIFactory.Anchor(crown.rectTransform, AnchorPreset.TopRight, new Vector2(10f, 14f));

            RectTransform texts = TextColumn(row);
            MonetizationConfig config = _game != null ? _game.Config : null;
            UIFactory.Label(texts, StoreTexts.Title(product.Id, config), UITheme.TextLead,
                UITheme.TextPrimary, TextAnchor.MiddleLeft, UIFont.Title, TextGlow.Shadow);
            string desc = StoreTexts.Description(product.Id, config);
            UIFactory.Label(texts, desc, UITheme.TextSmall, UITheme.TextSecondary, TextAnchor.MiddleLeft, UIFont.Body);

            AddBuyButton(row, card.Rect, product.Id, true);
        }

        private static RectTransform RowLayout(RectTransform body)
        {
            RectTransform row = UIFactory.CreateRect(body, "Row");
            HorizontalLayoutGroup h = UIFactory.HStack(row, UITheme.SpaceMd, null, TextAnchor.MiddleLeft, true);
            h.childForceExpandWidth = false;
            UIFactory.SetLayout(row, -1f, -1f);
            return row;
        }

        private static RectTransform TextColumn(RectTransform row)
        {
            RectTransform texts = UIFactory.CreateRect(row, "Texts");
            VerticalLayoutGroup v = UIFactory.VStack(texts, 2f, null, TextAnchor.MiddleLeft, true);
            v.childForceExpandWidth = true;
            UIFactory.SetLayout(texts, 0f, -1f, 1f);
            return texts;
        }

        private static void ArtGem(RectTransform art, Vector2 pos, float size, float rotation)
        {
            Image s = UIFactory.Icon(art, IconId.Gem, size, UITheme.GemColor);
            s.rectTransform.anchoredPosition = pos;
            s.rectTransform.localEulerAngles = new Vector3(0f, 0f, rotation);
            UIFactory.IgnoreLayout(s);
        }

        private void AddBuyButton(RectTransform row, RectTransform cardRect, string productId, bool removesAds)
        {
            var entry = new Row { ProductId = productId, RemovesAds = removesAds, Rect = cardRect };
            entry.Buy = UIFactory.Button(row, Loc.T("common.buy"), ButtonStyle.Primary, () => Buy(entry), IconId.None, null, 112f, 230f);
            _rows.Add(entry);
        }

        // ------------------------------------------------------------------ actions

        private void Buy(Row row)
        {
            if (_game == null || _busy || _restoring)
            {
                return;
            }

            if (!_game.StoreAvailable)
            {
                Refresh();
                return;
            }

            _busy = true;
            _busyId = row.ProductId;
            Refresh();
            _game.BuyProduct(row.ProductId, result =>
            {
                _busy = false;
                _busyId = null;
                if (this == null)
                {
                    return;
                }

                if (result.IsSuccess && _open && row.Buy != null)
                {
                    Celebrate(row);
                }

                Refresh();
            });
        }

        private void Celebrate(Row row)
        {
            Color c = row.RemovesAds ? UITheme.AccentYellow : UITheme.GemColor;
            MetaFx.BurstAt(_root, row.Buy.transform, c, 26, 320f);
            UITween.Punch(row.Rect, 0.05f, 0.4f);
            UITween.Punch(_balance.transform, 0.3f, 0.5f);
            bool reduce = _game != null && _game.Profile != null && _game.Profile.Settings != null && _game.Profile.Settings.ReduceFlashing;
            MetaUI.Flash(c, 0.2f, reduce);
        }

        private void OnRestore()
        {
            if (_game == null || _busy || _restoring)
            {
                return;
            }

            _restoring = true;
            Refresh();
            _game.RestorePurchases(ok =>
            {
                _restoring = false;
                if (this == null)
                {
                    return;
                }

                Refresh();
            });
        }

        private void OnRetry()
        {
            if (_game == null || _game.Iap == null)
            {
                return;
            }

            _game.Iap.Initialize();
            Refresh();
        }
    }
}
