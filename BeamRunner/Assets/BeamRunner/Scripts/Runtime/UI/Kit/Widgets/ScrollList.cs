using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace BeamRunner.UI
{
    /// <summary>
    /// Vertical scrolling list: ScrollRect (elastic, inertia) + soft-edged RectMask2D viewport + content with a
    /// VerticalLayoutGroup and ContentSizeFitter. Add rows under <see cref="Content"/>. Build with
    /// <see cref="UIFactory.ScrollList"/>.
    /// <para>Keyboard/gamepad: a control focused by navigation is scrolled into view; the right stick and Page Up/Down
    /// scroll the list directly (lists of read-only rows have nothing to focus). Drags that start on its buttons, toggles
    /// and segments scroll it; sliders hand it their mostly-vertical drags (<see cref="ScrollAwareSlider"/>).</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ScrollList : MonoBehaviour
    {
        private static readonly Vector3[] Corners = new Vector3[4];

        /// <summary>Right-stick scroll speed at full tilt, in canvas units per second.</summary>
        private const float StickScrollSpeed = 2200f;
        private const float StickDeadZone = 0.25f;

        private ScrollRect _scroll;
        private float _fadeEdge;
        private RectTransform _viewport;
        private RectTransform _content;
        private VerticalLayoutGroup _layout;
        private GameObject _followed;

        public ScrollRect ScrollRect
        {
            get { return _scroll; }
        }

        public RectTransform Viewport
        {
            get { return _viewport; }
        }

        /// <summary>Parent for list items (vertical layout, width-stretched children).</summary>
        public RectTransform Content
        {
            get { return _content; }
        }

        public VerticalLayoutGroup Layout
        {
            get { return _layout; }
        }

        public RectTransform Rect
        {
            get { return (RectTransform)transform; }
        }

        /// <summary>Destroys all items.</summary>
        public void Clear()
        {
            for (int i = _content.childCount - 1; i >= 0; i--)
            {
                GameObject child = _content.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }
        }

        public void ScrollToTop(bool animate = false)
        {
            ScrollTo(1f, animate);
        }

        public void ScrollToBottom(bool animate = false)
        {
            ScrollTo(0f, animate);
        }

        /// <summary>Scrolls to a normalised vertical position (1 = top, 0 = bottom).</summary>
        public void ScrollTo(float normalized, bool animate = true)
        {
            Canvas.ForceUpdateCanvases();
            _scroll.StopMovement();
            if (!animate || !Application.isPlaying)
            {
                _scroll.verticalNormalizedPosition = normalized;
                return;
            }

            ScrollRect sr = _scroll;
            UITween.Value(this, sr.verticalNormalizedPosition, normalized, 0.4f, v => sr.verticalNormalizedPosition = v)
                .SetEase(UIEase.OutCubic);
        }

        /// <summary>
        /// Scrolls the least distance that shows <paramref name="item"/> (a descendant of Content) completely, clear of the
        /// faded edges; no-op when it already is. An item taller than the view is aligned to its top.
        /// </summary>
        public void ScrollIntoView(RectTransform item, bool animate = true)
        {
            if (item == null || _content == null || !item.IsChildOf(_content))
            {
                return;
            }

            Canvas.ForceUpdateCanvases();
            float contentH = _content.rect.height;
            float viewH = _viewport.rect.height;
            float range = contentH - viewH;
            if (range <= 0.5f || viewH <= 1f)
            {
                return;
            }

            // Item edges measured downwards from the content's top edge.
            item.GetWorldCorners(Corners);
            float top = float.MinValue;
            float bottom = float.MaxValue;
            for (int i = 0; i < 4; i++)
            {
                float y = _content.InverseTransformPoint(Corners[i]).y;
                top = Mathf.Max(top, y);
                bottom = Mathf.Min(bottom, y);
            }

            float contentTop = _content.rect.yMax;
            float itemTop = contentTop - top;
            float itemBottom = contentTop - bottom;

            float offset = (1f - Mathf.Clamp01(_scroll.verticalNormalizedPosition)) * range;
            float margin = Mathf.Min(_fadeEdge + 24f, viewH * 0.25f);
            float target = offset;
            if (itemBottom - itemTop + margin * 2f >= viewH || itemTop - margin < offset)
            {
                target = itemTop - margin;
            }
            else if (itemBottom + margin > offset + viewH)
            {
                target = itemBottom + margin - viewH;
            }

            target = Mathf.Clamp(target, 0f, range);
            if (Mathf.Abs(target - offset) < 1f)
            {
                return;
            }

            ScrollTo(1f - target / range, animate);
        }

        // Right stick / Page Up / Page Down scroll the list while it can take input (its screen visible, no dialog over it).
        private void Update()
        {
            float stick = 0f;
            int page = 0;
            Gamepad pad = Gamepad.current;
            if (pad != null)
            {
                float y = pad.rightStick.ReadValue().y;
                if (Mathf.Abs(y) > StickDeadZone)
                {
                    stick = y;
                }
            }

            Keyboard kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.pageUpKey.wasPressedThisFrame)
                {
                    page = 1;
                }
                else if (kb.pageDownKey.wasPressedThisFrame)
                {
                    page = -1;
                }
            }

            if ((stick == 0f && page == 0) || _scroll == null || !_scroll.IsActive() || !GroupsAllowInput())
            {
                return;
            }

            float range = _content.rect.height - _viewport.rect.height;
            if (range <= 0.5f)
            {
                return;
            }

            if (page != 0)
            {
                float pos = Mathf.Clamp01(_scroll.verticalNormalizedPosition) + page * _viewport.rect.height * 0.8f / range;
                ScrollTo(Mathf.Clamp01(pos), true);
                return;
            }

            // Stick up = see content further up (like pushing the page down).
            UITween.Kill(this);
            _scroll.StopMovement();
            float delta = stick * StickScrollSpeed * Time.unscaledDeltaTime / range;
            _scroll.verticalNormalizedPosition = Mathf.Clamp01(_scroll.verticalNormalizedPosition + delta);
        }

        private bool GroupsAllowInput()
        {
            for (Transform t = transform; t != null; t = t.parent)
            {
                CanvasGroup g = t.GetComponent<CanvasGroup>();
                if (g == null)
                {
                    continue;
                }

                if (!g.interactable || !g.blocksRaycasts)
                {
                    return false;
                }

                if (g.ignoreParentGroups)
                {
                    return true;
                }
            }

            return true;
        }

        // uGUI never scrolls a ScrollRect to the selected control, so keyboard/gamepad focus walked off the visible part of
        // long lists (settings, store). Bring a newly selected descendant into view while the player navigates.
        private void LateUpdate()
        {
            EventSystem es = EventSystem.current;
            GameObject selected = es != null ? es.currentSelectedGameObject : null;
            if (selected == _followed)
            {
                return;
            }

            _followed = selected;
            if (selected == null || _content == null || !selected.activeInHierarchy || !selected.transform.IsChildOf(_content))
            {
                return;
            }

            UIRoot root = UIRoot.Instance;
            if (root != null && root.Stack != null && root.Stack.IsNavigating)
            {
                ScrollIntoView(selected.transform as RectTransform, true);
            }
        }

        internal void Build(float spacing, RectOffset padding, float fadeEdge)
        {
            RectTransform root = Rect;
            root.sizeDelta = new Vector2(900f, 1200f);
            _fadeEdge = Mathf.Max(0f, fadeEdge);

            _viewport = UIFactory.CreateFill(root, "Viewport");
            _viewport.gameObject.AddComponent<UIRaycastTarget>();
            RectMask2D mask = _viewport.gameObject.AddComponent<RectMask2D>();
            if (fadeEdge > 0f)
            {
                mask.softness = new Vector2Int(0, Mathf.RoundToInt(fadeEdge));
            }

            _content = UIFactory.CreateRect(_viewport, "Content");
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.offsetMin = Vector2.zero;
            _content.offsetMax = Vector2.zero;
            _content.sizeDelta = new Vector2(0f, 0f);
            _layout = UIFactory.VStack(_content, spacing, padding ?? UIFactory.Padding(32, 32, Mathf.RoundToInt(fadeEdge * 0.5f), Mathf.RoundToInt(fadeEdge)), TextAnchor.UpperCenter, true);
            UIFactory.FitContent(_content, false, true);

            _scroll = root.gameObject.AddComponent<ScrollRect>();
            _scroll.content = _content;
            _scroll.viewport = _viewport;
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Elastic;
            _scroll.elasticity = 0.12f;
            _scroll.inertia = true;
            _scroll.decelerationRate = 0.135f;
            _scroll.scrollSensitivity = 40f;
        }
    }
}
