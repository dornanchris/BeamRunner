using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BeamRunner.UI
{
    /// <summary>
    /// Stacked, auto-dismissing toast notifications at the top of the safe area (newest on top, max
    /// <see cref="MaxVisible"/>). Views are pooled. Toasts never block input. Lives on the Toasts layer of the
    /// <see cref="UIRoot"/>; call the static <see cref="Show(string, IconId, Color, float)"/> from anywhere.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Toasts : MonoBehaviour
    {
        public const int MaxVisible = 3;
        private const float Height = 112f;
        private const int TextSize = 38;
        private const float Gap = 16f;
        private const float TopMargin = 28f;

        private sealed class View
        {
            public RectTransform Rect;
            public CanvasGroup Group;
            public Image Frame;
            public Image IconGlow;
            public Image Icon;
            public Text Text;
            public float Remaining;
            public bool Leaving;
        }

        private readonly List<View> _active = new List<View>(MaxVisible + 1);
        private readonly Stack<View> _pool = new Stack<View>(MaxVisible + 1);

        /// <summary>Shows a toast with an icon and accent colour.</summary>
        public static void Show(string text, IconId icon, Color accent, float duration = 2.6f)
        {
            UIRoot root = UIRoot.Ensure();
            if (root != null && root.Toasts != null)
            {
                root.Toasts.ShowInternal(text, icon, accent, duration);
            }
        }

        /// <summary>Shows an info toast in the theme accent.</summary>
        public static void Show(string text)
        {
            Show(text, IconId.Info, UITheme.Accent);
        }

        /// <summary>Pre-creates pooled toast views (e.g. from <c>UIRoot.BuildQueue</c>) so the first toast doesn't build one.</summary>
        public static void Prewarm(int count = 1)
        {
            UIRoot root = UIRoot.Instance;
            Toasts t = root != null ? root.Toasts : null;
            if (t == null)
            {
                return;
            }

            for (int i = t._pool.Count + t._active.Count; i < count; i++)
            {
                View v = t.CreateView();
                v.Rect.gameObject.SetActive(false);
                t._pool.Push(v);
            }
        }

        /// <summary>Removes every toast immediately.</summary>
        public static void ClearAll()
        {
            UIRoot root = UIRoot.Instance;
            if (root == null || root.Toasts == null)
            {
                return;
            }

            Toasts t = root.Toasts;
            for (int i = t._active.Count - 1; i >= 0; i--)
            {
                t.Recycle(t._active[i]);
            }

            t._active.Clear();
        }

        private void ShowInternal(string text, IconId icon, Color accent, float duration)
        {
            // Make room: the oldest toast leaves early.
            int visible = 0;
            for (int i = 0; i < _active.Count; i++)
            {
                if (!_active[i].Leaving)
                {
                    visible++;
                }
            }

            if (visible >= MaxVisible)
            {
                for (int i = 0; i < _active.Count; i++)
                {
                    if (!_active[i].Leaving)
                    {
                        Dismiss(_active[i]);
                        break;
                    }
                }
            }

            View v = _pool.Count > 0 ? _pool.Pop() : CreateView();
            v.Rect.gameObject.SetActive(true);
            v.Rect.SetAsLastSibling();
            v.Text.text = text ?? string.Empty;
            v.Icon.sprite = UIIcons.Get(icon);
            v.Icon.enabled = icon != IconId.None;
            v.Icon.color = accent;
            v.IconGlow.color = UITheme.Alpha(accent, 0.45f);
            v.Frame.color = accent;
            v.Remaining = Mathf.Max(0.5f, duration);
            v.Leaving = false;

            float width = Mathf.Min(920f, ((RectTransform)transform).rect.width - 64f);
            v.Rect.sizeDelta = new Vector2(Mathf.Max(400f, width), Height);
            UITextFit.FitWrapped(v.Text, TextSize);
            _active.Insert(0, v);

            v.Rect.anchoredPosition = new Vector2(0f, -TopMargin + 70f);
            UITween.Fade(v.Group, 0f, 1f, 0.22f).SetEase(UIEase.OutQuad);
            UITween.Scale(v.Rect, 0.9f, 1f, 0.4f).SetEase(UIEase.OutBack);
            Relayout();
            enabled = true;
        }

        private void Update()
        {
            if (_active.Count == 0)
            {
                enabled = false;
                return;
            }

            float dt = Time.unscaledDeltaTime;
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                View v = _active[i];
                if (v.Leaving)
                {
                    continue;
                }

                v.Remaining -= dt;
                if (v.Remaining <= 0f)
                {
                    Dismiss(v);
                }
            }
        }

        private void Dismiss(View v)
        {
            if (v.Leaving)
            {
                return;
            }

            v.Leaving = true;
            UITween.Fade(v.Group, 0f, 0.25f).SetEase(UIEase.InQuad).OnComplete(() => Finish(v));
            UITween.Move(v.Rect, v.Rect.anchoredPosition + new Vector2(0f, 50f), 0.25f).SetEase(UIEase.InQuad);
        }

        private void Finish(View v)
        {
            _active.Remove(v);
            Recycle(v);
            Relayout();
        }

        private void Recycle(View v)
        {
            if (v.Rect == null)
            {
                return;
            }

            UITween.Kill(v.Rect);
            UITween.Kill(v.Group);
            v.Rect.gameObject.SetActive(false);
            _pool.Push(v);
        }

        private void Relayout()
        {
            float y = -TopMargin;
            for (int i = 0; i < _active.Count; i++)
            {
                View v = _active[i];
                if (v.Leaving)
                {
                    continue;
                }

                UITween.Move(v.Rect, new Vector2(0f, y), 0.35f).SetEase(UIEase.OutCubic);
                y -= Height + Gap;
            }
        }

        private View CreateView()
        {
            var v = new View();
            RectTransform rt = UIFactory.CreateRect(transform, "Toast");
            UIFactory.Anchor(rt, AnchorPreset.TopCenter);
            rt.sizeDelta = new Vector2(880f, Height);
            v.Rect = rt;
            v.Group = rt.gameObject.AddComponent<CanvasGroup>();
            v.Group.blocksRaycasts = false;
            v.Group.interactable = false;

            Image shadow = UIFactory.CreateImage(rt, null, UITheme.Shadow, "Shadow");
            UISprites.ApplySoft(shadow, Height * 0.5f, 30f, new Vector2(0f, -10f));
            UIFactory.PillImage(rt, new Color(0.07f, 0.045f, 0.15f, 0.94f), "Glass");
            v.Frame = UIFactory.CreateImage(rt, null, UITheme.Accent, "Frame");
            UISprites.ApplyFrame(v.Frame, Height * 0.5f, 3f, 18f);

            v.IconGlow = UIFactory.CreateImage(rt, UISprites.Glow, Color.white, "IconGlow");
            RectTransform grt = v.IconGlow.rectTransform;
            UIFactory.Anchor(grt, AnchorPreset.MiddleLeft, new Vector2(8f, 0f));
            grt.sizeDelta = new Vector2(130f, 130f);
            v.Icon = UIFactory.Icon(rt, IconId.Info, 56f, UITheme.Accent);
            UIFactory.Anchor(v.Icon.rectTransform, AnchorPreset.MiddleLeft, new Vector2(44f, 0f));

            v.Text = UIFactory.Label(rt, string.Empty, TextSize, UITheme.TextPrimary, TextAnchor.MiddleLeft, UIFont.Bold);
            UIFactory.Stretch(v.Text.rectTransform, 124f, 8f, 44f, 8f);
            v.Text.verticalOverflow = VerticalWrapMode.Truncate;
            return v;
        }
    }
}
