using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BeamRunner.UI
{
    /// <summary>
    /// N-option pill selector with a sliding gradient thumb. Each segment is a navigable <see cref="Button"/>: left/right
    /// moves between segments, Submit selects the focused one, and a <see cref="UIFocusRing"/> frames the focused segment.
    /// Drags that start on a segment bubble to an enclosing ScrollRect (buttons have no drag handler). Build with
    /// <see cref="UIFactory.SegmentedControl"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SegmentedControl : MonoBehaviour
    {
        private const float Inset = 7f;

        private readonly List<Text> _labels = new List<Text>();
        private readonly List<Button> _buttons = new List<Button>();
        private RectTransform _thumb;
        private Image _thumbGlow;
        private int _selected;
        private int _count;
        private float _thumbPos;
        private int _thumbTween;
        private Action<int> _onChanged;

        public int Selected
        {
            get { return _selected; }
        }

        public int Count
        {
            get { return _count; }
        }

        public RectTransform Rect
        {
            get { return (RectTransform)transform; }
        }

        /// <summary>Segment buttons (for navigation wiring or custom tweaks).</summary>
        public IReadOnlyList<Button> Buttons
        {
            get { return _buttons; }
        }

        /// <summary>Selects a segment; <paramref name="notify"/> invokes the change callback.</summary>
        public void Select(int index, bool notify = true, bool animate = true)
        {
            if (_count == 0)
            {
                return;
            }

            index = Mathf.Clamp(index, 0, _count - 1);
            bool changed = index != _selected;
            _selected = index;
            MoveThumb(animate);
            RefreshLabels(animate);
            if (changed && notify && _onChanged != null)
            {
                try
                {
                    _onChanged(index);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        internal void Build(string[] options, int selected, Action<int> onChanged, float height)
        {
            RectTransform root = Rect;
            _count = options != null ? options.Length : 0;
            _onChanged = onChanged;
            root.sizeDelta = new Vector2(Mathf.Max(360f, _count * 220f), height);
            UIFactory.SetLayout(this, root.sizeDelta.x, height);

            UIFactory.PillImage(root, UITheme.PanelGlass, "Background");
            Image border = UIFactory.CreateImage(root, null, UITheme.PanelBorder, "Border");
            UIFactory.Stretch(border.rectTransform);
            UISprites.ApplyOutline(border, height * 0.5f, 2f);

            _thumb = UIFactory.CreateRect(root, "Thumb");
            _thumbGlow = UIFactory.CreateImage(_thumb, null, Color.white, "Glow");
            UISprites.ApplySoft(_thumbGlow, (height - Inset * 2f) * 0.5f, 26f);
            UIGradient.ApplyAccent(_thumbGlow, GradientDirection.Horizontal, 0.55f);
            Image thumbFill = UIFactory.PillImage(_thumb, Color.white, "Fill");
            UIGradient.ApplyAccent(thumbFill, GradientDirection.Horizontal);
            Image sheen = UIFactory.PillImage(_thumb, Color.white, "Sheen");
            UIFactory.Stretch(sheen.rectTransform, 4f, 3f, 4f, (height - Inset * 2f) * 0.5f);
            UIGradient.Apply(sheen, new Color(1f, 1f, 1f, 0.35f), new Color(1f, 1f, 1f, 0f), GradientDirection.Vertical);

            for (int i = 0; i < _count; i++)
            {
                RectTransform seg = UIFactory.CreateRect(root, "Segment" + i);
                seg.anchorMin = new Vector2((float)i / _count, 0f);
                seg.anchorMax = new Vector2((float)(i + 1) / _count, 1f);
                seg.offsetMin = Vector2.zero;
                seg.offsetMax = Vector2.zero;
                UIRaycastTarget hit = seg.gameObject.AddComponent<UIRaycastTarget>();
                Button b = seg.gameObject.AddComponent<Button>();
                b.transition = Selectable.Transition.None;
                b.targetGraphic = hit;
                int index = i;
                b.onClick.AddListener(() => OnSegmentClicked(index));
                _buttons.Add(b);

                Text label = UIFactory.LabelLine(seg, options[i], 38, UITheme.TextSecondary, TextAnchor.MiddleCenter, UIFont.Bold);
                UIFactory.Stretch(label.rectTransform);
                _labels.Add(label);

                // Focus frame on the thumb's outline for this segment (inside the control, so neighbours never overlap).
                UIFocusRing.Attach(b, seg, height * 0.5f, -Inset);
            }

            _selected = Mathf.Clamp(selected, 0, Mathf.Max(0, _count - 1));
            _thumbPos = _selected;
            MoveThumb(false);
            RefreshLabels(false);
        }

        private void OnSegmentClicked(int index)
        {
            UIFactory.PlayClickFeedback();
            if (index != _selected)
            {
                Select(index);
            }
            else
            {
                UITween.Punch(_thumb, 0.06f, 0.3f);
            }
        }

        private void MoveThumb(bool animate)
        {
            if (_thumb == null || _count == 0)
            {
                return;
            }

            UITween.KillId(_thumbTween);
            _thumbTween = 0;
            if (!animate || !Application.isPlaying)
            {
                _thumbPos = _selected;
                PlaceThumb(_thumbPos);
                return;
            }

            _thumbTween = UITween.Value(this, _thumbPos, _selected, 0.32f, PlaceThumb).SetEase(UIEase.OutBack).Id;
        }

        private void PlaceThumb(float pos)
        {
            _thumbPos = pos;
            float w = 1f / _count;
            _thumb.anchorMin = new Vector2(pos * w, 0f);
            _thumb.anchorMax = new Vector2((pos + 1f) * w, 1f);
            _thumb.offsetMin = new Vector2(Inset, Inset);
            _thumb.offsetMax = new Vector2(-Inset, -Inset);
        }

        private void RefreshLabels(bool animate)
        {
            for (int i = 0; i < _labels.Count; i++)
            {
                Color c = i == _selected ? UITheme.TextOnAccent : UITheme.TextSecondary;
                if (animate && Application.isPlaying)
                {
                    UITween.Color(_labels[i], c, 0.2f);
                }
                else
                {
                    _labels[i].color = c;
                }
            }
        }
    }
}
