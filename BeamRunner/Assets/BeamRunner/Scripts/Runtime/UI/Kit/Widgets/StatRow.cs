using UnityEngine;
using UnityEngine.UI;

namespace BeamRunner.UI
{
    /// <summary>Row with an optional icon, a label on the left and a value on the right. Build with <see cref="UIFactory.StatRow"/>.</summary>
    [DisallowMultipleComponent]
    public sealed class StatRow : MonoBehaviour
    {
        private Text _label;
        private Text _value;
        private Image _icon;

        public RectTransform Rect
        {
            get { return (RectTransform)transform; }
        }

        public Text Label
        {
            get { return _label; }
        }

        public Text Value
        {
            get { return _value; }
        }

        public Image IconImage
        {
            get { return _icon; }
        }

        public void SetValue(string value)
        {
            _value.text = value ?? string.Empty;
        }

        public void SetValue(string value, Color color)
        {
            _value.text = value ?? string.Empty;
            _value.color = color;
        }

        internal void Build(string label, string value, IconId icon, bool divider, float height)
        {
            RectTransform root = Rect;
            root.sizeDelta = new Vector2(900f, height);
            UIFactory.SetLayout(this, -1f, height);
            HorizontalLayoutGroup row = UIFactory.HStack(root, UITheme.SpaceSm, UIFactory.Padding(4, 4, 0, 0), TextAnchor.MiddleLeft, true);
            row.childForceExpandWidth = false;

            _icon = UIFactory.Icon(root, icon, UITheme.IconSizeSmall, UITheme.Accent);
            UIAccentFollower.Attach(_icon, AccentSlot.Primary);
            _icon.gameObject.SetActive(icon != IconId.None);

            _label = UIFactory.Label(root, label, UITheme.TextBody, UITheme.TextSecondary, TextAnchor.MiddleLeft, UIFont.Body);
            UIFactory.SetLayout(_label, -1f, -1f, 1f);

            _value = UIFactory.LabelLine(root, value ?? string.Empty, 44, UITheme.TextPrimary, TextAnchor.MiddleRight, UIFont.Bold);

            if (divider)
            {
                Image line = UIFactory.Divider(root);
                UIFactory.IgnoreLayout(line);
                RectTransform lrt = line.rectTransform;
                lrt.anchorMin = new Vector2(0f, 0f);
                lrt.anchorMax = new Vector2(1f, 0f);
                lrt.pivot = new Vector2(0.5f, 0f);
                lrt.sizeDelta = new Vector2(0f, 2f);
                lrt.anchoredPosition = Vector2.zero;
            }
        }
    }
}
