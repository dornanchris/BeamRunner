using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BeamRunner.UI
{
    /// <summary>
    /// Pooled rising, popping, fading text for HUD feedback ("+250", "NEAR MISS!", "x3"). Items are recycled after
    /// their animation; nothing is allocated in steady state apart from the string you pass.
    /// </summary>
    public static class FloatingText
    {
        private sealed class Item
        {
            public RectTransform Rect;
            public CanvasGroup Group;
            public Text Text;
            public Outline Halo;
            public System.Action Release;
        }

        private static readonly List<Item> Free = new List<Item>(16);

        /// <summary>Default rise distance in units.</summary>
        public static float Rise = 150f;

        /// <summary>Default lifetime in seconds.</summary>
        public static float Lifetime = 0.95f;

        // Minimum distance between a popup's text and the parent's left/right edge.
        private const float EdgeMargin = 24f;

        /// <summary>
        /// Spawns a floating text under <paramref name="parent"/> at <paramref name="anchoredPos"/> (relative to the
        /// parent's centre). <paramref name="scale"/> scales the 64-unit display text.
        /// </summary>
        public static Text Spawn(RectTransform parent, string text, Color color, Vector2 anchoredPos, float scale = 1f)
        {
            if (parent == null)
            {
                return null;
            }

            Item it = Take();
            RectTransform rt = it.Rect;
            rt.SetParent(parent, false);
            rt.SetAsLastSibling();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.gameObject.SetActive(true);

            it.Text.font = UIFonts.Title; // a pooled item may predate a language (font set) change
            it.Text.text = text ?? string.Empty;
            it.Text.color = UITheme.Lighten(color, 0.35f);
            it.Halo.effectColor = UITheme.Alpha(color, 0.6f);

            float s = Mathf.Max(0.05f, scale);
            float rise = Rise * Mathf.Lerp(0.8f, 1.2f, Mathf.Clamp01(s - 0.5f));

            // Keep the whole text inside the parent: shrink a text wider than it (long translations such as
            // "CÉLULA DE OVERDRIVE") and pull a wide one in from the edge it was spawned next to.
            float available = parent.rect.width - EdgeMargin * 2f;
            float width = it.Text.preferredWidth;
            if (available > 0f && width > 0f)
            {
                s = Mathf.Min(s, available / width);
                float half = Mathf.Max(0f, (available - width * s) * 0.5f);
                anchoredPos.x = Mathf.Clamp(anchoredPos.x, -half, half);
            }

            float life = Lifetime;
            UITween.Move(rt, anchoredPos, anchoredPos + new Vector2(0f, rise), life)
                .SetEase(UIEase.OutCubic);
            UITween.Scale(rt, s * 0.4f, s, 0.32f).SetEase(UIEase.OutBack);
            UITween.Fade(it.Group, 1f, 0f, life * 0.45f).SetDelay(life * 0.55f).SetEase(UIEase.InQuad).OnComplete(it.Release);
            return it.Text;
        }

        private static Item Take()
        {
            while (Free.Count > 0)
            {
                Item it = Free[Free.Count - 1];
                Free.RemoveAt(Free.Count - 1);
                if (it.Rect != null)
                {
                    return it;
                }
            }

            return Create();
        }

        private static Item Create()
        {
            var it = new Item();
            RectTransform rt = UIFactory.CreateRect(null, "FloatingText");
            rt.sizeDelta = new Vector2(900f, 110f);
            it.Rect = rt;
            it.Group = rt.gameObject.AddComponent<CanvasGroup>();
            it.Group.blocksRaycasts = false;
            it.Group.interactable = false;
            it.Text = rt.gameObject.AddComponent<Text>();
            it.Text.font = UIFonts.Title;
            it.Text.fontSize = 64;
            it.Text.alignment = TextAnchor.MiddleCenter;
            it.Text.horizontalOverflow = HorizontalWrapMode.Overflow;
            it.Text.verticalOverflow = VerticalWrapMode.Overflow;
            it.Text.raycastTarget = false;
            it.Text.supportRichText = true;
            it.Halo = rt.gameObject.AddComponent<Outline>();
            it.Halo.effectDistance = new Vector2(3f, -3f);
            Shadow drop = rt.gameObject.AddComponent<Shadow>();
            drop.effectColor = new Color(0f, 0f, 0.02f, 0.6f);
            drop.effectDistance = new Vector2(0f, -5f);
            it.Release = () => Recycle(it);
            return it;
        }

        private static void Recycle(Item it)
        {
            if (it.Rect == null)
            {
                return;
            }

            it.Rect.gameObject.SetActive(false);
            Free.Add(it);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Free.Clear();
        }
    }
}
