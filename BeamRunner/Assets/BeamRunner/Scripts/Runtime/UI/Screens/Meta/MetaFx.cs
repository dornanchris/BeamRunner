using UnityEngine;
using UnityEngine.UI;

namespace BeamRunner.UI
{
    /// <summary>Celebration effects for the meta screens (gem bursts, shockwave rings). Self-cleaning.</summary>
    internal static class MetaFx
    {
        /// <summary>
        /// Gem/spark burst with a shockwave ring at <paramref name="pos"/> (local to <paramref name="space"/>,
        /// relative to its centre — see <see cref="MetaUI.LocalPoint"/>).
        /// </summary>
        public static void Burst(RectTransform space, Vector2 pos, Color color, int count = 18, float distance = 280f)
        {
            if (space == null)
            {
                return;
            }

            bool reduced = UITheme.ReducedMotion;
            Ring(space, pos, color, distance * 2.2f, 0.55f);
            if (!reduced)
            {
                Ring(space, pos, UITheme.Lighten(color, 0.5f), distance * 1.4f, 0.4f, 0.08f);
            }

            int n = reduced ? Mathf.Max(4, count / 3) : count;
            for (int i = 0; i < n; i++)
            {
                bool gem = i % 3 != 2;
                float size = gem ? Random.Range(34f, 62f) : Random.Range(60f, 110f);
                Image p = gem
                    ? UIFactory.Icon(space, IconId.Gem, size, i % 2 == 0 ? UITheme.GemColor : UITheme.Lighten(color, 0.35f))
                    : UIFactory.CreateImage(space, UISprites.Glow, UITheme.Alpha(color, 0.9f), "Spark");
                p.raycastTarget = false;
                RectTransform rt = p.rectTransform;
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(size, size);
                rt.anchoredPosition = pos;

                float angle = (i / (float)n) * Mathf.PI * 2f + Random.Range(-0.25f, 0.25f);
                float dist = distance * Random.Range(0.45f, 1.05f);
                Vector2 to = pos + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * dist + new Vector2(0f, Random.Range(-20f, 60f));
                float dur = Random.Range(0.55f, 0.85f);
                UITween.Move(rt, pos, to, dur).SetEase(UIEase.OutCubic);
                UITween.Scale(rt, 0.2f, Random.Range(0.8f, 1.2f), dur * 0.35f).SetEase(UIEase.OutBack);
                if (gem)
                {
                    UITween.Rotate(rt, Random.Range(-240f, 240f), dur).SetEase(UIEase.OutCubic);
                }

                GameObject go = p.gameObject;
                UITween.Alpha(p, 0f, dur * 0.45f).SetDelay(dur * 0.55f).SetEase(UIEase.InQuad)
                    .OnComplete(() => Kill(go));
            }
        }

        /// <summary>Expanding, fading ring.</summary>
        public static void Ring(RectTransform space, Vector2 pos, Color color, float size, float duration, float delay = 0f)
        {
            if (space == null)
            {
                return;
            }

            Image ring = UIFactory.CreateImage(space, UISprites.Ring(10), UITheme.Alpha(color, 0.95f), "BurstRing");
            RectTransform rt = ring.rectTransform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = pos;
            rt.localScale = new Vector3(0.1f, 0.1f, 1f);
            UITween.Scale(rt, 0.1f, 1f, duration).SetDelay(delay).SetEase(UIEase.OutCubic);
            GameObject go = ring.gameObject;
            UITween.Alpha(ring, 0f, duration).SetDelay(delay).SetEase(UIEase.InQuad).OnComplete(() => Kill(go));
        }

        /// <summary>Burst centred on <paramref name="widget"/>.</summary>
        public static void BurstAt(RectTransform space, Transform widget, Color color, int count = 18, float distance = 280f)
        {
            Burst(space, MetaUI.LocalPoint(space, widget), color, count, distance);
        }

        /// <summary>Floating "+N" style text above a widget.</summary>
        public static void FloatAt(RectTransform space, Transform widget, string text, Color color, float yOffset = 60f, float scale = 1.3f)
        {
            if (space == null)
            {
                return;
            }

            FloatingText.Spawn(space, text, color, MetaUI.LocalPoint(space, widget) + new Vector2(0f, yOffset), scale);
        }

        private static void Kill(GameObject go)
        {
            if (go != null)
            {
                Object.Destroy(go);
            }
        }
    }
}
