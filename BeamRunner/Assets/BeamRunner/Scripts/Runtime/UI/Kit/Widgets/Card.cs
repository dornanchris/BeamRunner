using UnityEngine;
using UnityEngine.UI;

namespace BeamRunner.UI
{
    /// <summary>
    /// Glass card with a header row (icon + title + right-hand slot) and a body slot; its height follows its content,
    /// so it drops straight into a <see cref="ScrollList"/> or any vertical stack. Build with <see cref="UIFactory.Card"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Card : MonoBehaviour
    {
        private GlassPanel _panel;
        private RectTransform _header;
        private Text _title;
        private Image _icon;
        private RectTransform _headerRight;
        private RectTransform _body;

        public RectTransform Rect
        {
            get { return (RectTransform)transform; }
        }

        public GlassPanel Panel
        {
            get { return _panel; }
        }

        public RectTransform Header
        {
            get { return _header; }
        }

        public Text Title
        {
            get { return _title; }
        }

        public Image IconImage
        {
            get { return _icon; }
        }

        /// <summary>Right side of the header (e.g. a badge, a value or a small button). Horizontal layout.</summary>
        public RectTransform HeaderRight
        {
            get { return _headerRight; }
        }

        /// <summary>Body container (vertical layout, width-stretched children).</summary>
        public RectTransform Body
        {
            get { return _body; }
        }

        public void SetTitle(string text)
        {
            _title.text = text ?? string.Empty;
            _header.gameObject.SetActive(!string.IsNullOrEmpty(text) || _icon.enabled);
        }

        internal void Build(string title, IconId icon, Color? accent, float padding)
        {
            RectTransform root = Rect;
            root.sizeDelta = new Vector2(900f, 300f);
            _panel = gameObject.AddComponent<GlassPanel>();
            _panel.Build(accent, UITheme.RadiusLg, padding, true, false, true, false);

            int pad = Mathf.RoundToInt(padding);
            UIFactory.VStack(root, UITheme.SpaceMd, UIFactory.Padding(pad), TextAnchor.UpperLeft, true);

            _header = UIFactory.CreateRect(root, "Header");
            HorizontalLayoutGroup h = UIFactory.HStack(_header, UITheme.SpaceSm, null, TextAnchor.MiddleLeft, true);
            h.childForceExpandWidth = false;
            UIFactory.SetLayout(_header, -1f, 64f);

            Color iconColor = accent ?? UITheme.Accent;
            _icon = UIFactory.Icon(_header, icon, 52f, iconColor);
            if (!accent.HasValue)
            {
                UIAccentFollower.Attach(_icon, AccentSlot.Primary);
            }

            _icon.gameObject.SetActive(icon != IconId.None);
            _title = UIFactory.LabelLine(_header, title ?? string.Empty, UITheme.TextLead, UITheme.TextPrimary, TextAnchor.MiddleLeft, UIFont.Title);
            _title.horizontalOverflow = HorizontalWrapMode.Wrap;
            UIFactory.SetLayout(_title, -1f, -1f, 1f);
            _headerRight = UIFactory.CreateRect(_header, "Right");
            UIFactory.HStack(_headerRight, UITheme.SpaceSm, null, TextAnchor.MiddleRight, true);
            _header.gameObject.SetActive(!string.IsNullOrEmpty(title) || icon != IconId.None);

            _body = UIFactory.CreateRect(root, "Body");
            UIFactory.VStack(_body, UITheme.SpaceSm, null, TextAnchor.UpperLeft, true);
        }
    }
}
