using System;
using UnityEngine;
using UnityEngine.UI;

namespace BeamRunner.UI
{
    /// <summary>
    /// Glass pill showing a currency icon (gem by default) and an animated count. Sizes itself to its content.
    /// Build with <see cref="UIFactory.CurrencyPill"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CurrencyPill : MonoBehaviour
    {
        private NumberTicker _ticker;
        private Image _icon;
        private Image _iconGlow;
        private RectTransform _iconWrap;
        private NeonButton _plus;

        public RectTransform Rect
        {
            get { return (RectTransform)transform; }
        }

        public long Amount
        {
            get { return _ticker.Value; }
        }

        public Text AmountText
        {
            get { return _ticker.Text; }
        }

        public Image IconImage
        {
            get { return _icon; }
        }

        /// <summary>The optional "+" button (null unless created with a plus handler).</summary>
        public NeonButton PlusButton
        {
            get { return _plus; }
        }

        /// <summary>Sets the amount; counts up/down when animated and flashes the icon on increases.</summary>
        public void SetAmount(long amount, bool animate = true)
        {
            long previous = _ticker.Value;
            _ticker.SetValue(amount, animate);
            if (animate && amount > previous && Application.isPlaying)
            {
                Celebrate();
            }
        }

        /// <summary>Punches the icon and flashes its glow.</summary>
        public void Celebrate()
        {
            UITween.Punch(_iconWrap, 0.35f, 0.5f);
            _iconGlow.canvasRenderer.SetAlpha(1f);
            UITween.RendererAlpha(_iconGlow, 0.35f, 0.8f).SetEase(UIEase.OutCubic);
        }

        internal void Build(long amount, IconId icon, Color? iconColor, Action onPlus)
        {
            const float height = 88f;
            RectTransform root = Rect;
            root.sizeDelta = new Vector2(260f, height);
            HorizontalLayoutGroup row = UIFactory.HStack(root, 12f, UIFactory.Padding(14, onPlus != null ? 12 : 32, 0, 0), TextAnchor.MiddleLeft, true);
            row.childForceExpandHeight = false;
            UIFactory.SetLayout(this, -1f, height);
            UIFactory.FitContent(this, true, false);

            Image bg = UIFactory.PillImage(root, UITheme.PanelGlass, "Background");
            UIFactory.IgnoreLayout(bg);
            Image border = UIFactory.CreateImage(root, null, UITheme.PanelBorder, "Border");
            UIFactory.Stretch(border.rectTransform);
            UISprites.ApplyOutline(border, height * 0.5f, 2f);
            UIFactory.IgnoreLayout(border);

            Color c = iconColor ?? UITheme.GemColor;
            _iconWrap = UIFactory.CreateRect(root, "IconWrap");
            UIFactory.SetLayout(_iconWrap, 62f, 62f);
            _iconGlow = UIFactory.CreateImage(_iconWrap, UISprites.Glow, UITheme.Alpha(c, 0.6f), "Glow");
            UIFactory.Stretch(_iconGlow.rectTransform, -26f, -26f, -26f, -26f);
            _iconGlow.canvasRenderer.SetAlpha(0.35f);
            _icon = UIFactory.Icon(_iconWrap, icon, 54f, c);
            UIFactory.Stretch(_icon.rectTransform, 4f, 4f, 4f, 4f);

            Text amountText = UIFactory.LabelLine(root, "0", 46, UITheme.TextPrimary, TextAnchor.MiddleLeft, UIFont.Bold, TextGlow.Shadow);
            _ticker = NumberTicker.Attach(amountText, amount);

            if (onPlus != null)
            {
                _plus = UIFactory.IconButton(root, IconId.Plus, onPlus, 64f, ButtonStyle.Primary);
            }
        }
    }
}
