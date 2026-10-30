using UnityEngine;

namespace BeamRunner.UI
{
    /// <summary>
    /// Uniformly shrinks this rect (stretched over its parent, centre pivot) until <see cref="Target"/> — a direct child
    /// whose height follows its content (ContentSizeFitter / layout) — fits vertically with <see cref="Margin"/> units to
    /// spare above and below. Never enlarges. Fixed-layout overlays (pause, revive offer, daily reward, dialogs) are laid
    /// out for the 1920-unit-tall portrait canvas; in landscape (desktop, editor Game view) the canvas is only 1080 units
    /// tall and their top/bottom rows (MENU, NO THANKS, CLAIM, dialog buttons) would otherwise sit off-screen.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UIFitHeight : MonoBehaviour
    {
        /// <summary>The content to keep on screen (must be a direct child of this rect).</summary>
        public RectTransform Target;

        /// <summary>Free space kept above and below the content (units).</summary>
        public float Margin = 32f;

        /// <summary>Never shrink below this scale.</summary>
        public float MinScale = 0.5f;

        /// <summary>Adds (or reconfigures) the fitter on <paramref name="host"/> for <paramref name="target"/>.</summary>
        public static UIFitHeight Attach(RectTransform host, RectTransform target, float margin = 32f)
        {
            UIFitHeight f = host.GetComponent<UIFitHeight>();
            if (f == null)
            {
                f = host.gameObject.AddComponent<UIFitHeight>();
            }

            f.Target = target;
            f.Margin = margin;
            f.Apply();
            return f;
        }

        private void OnEnable()
        {
            Apply();
        }

        // Polled (cheap): the content's height changes through layout, which notifies the content, not this rect. Layout
        // runs after LateUpdate, so a height change is applied one frame late at most — overlays fade in, so that frame
        // is never seen at full opacity.
        private void LateUpdate()
        {
            Apply();
        }

        public void Apply()
        {
            var host = transform as RectTransform;
            if (host == null || Target == null || Target.parent != host)
            {
                return;
            }

            Rect area = host.rect;
            float half = area.height * 0.5f - Margin;

            // Extent of the content above / below the host centre, in unscaled units (the content's own scale tweens,
            // e.g. a pop-in, are deliberately ignored so the fit doesn't pump while they play).
            float y = Target.localPosition.y - area.center.y;
            Rect r = Target.rect;
            float need = Mathf.Max(y + r.yMax, -(y + r.yMin));

            float s = 1f;
            if (half > 1f && need > half)
            {
                s = Mathf.Max(MinScale, half / need);
            }

            Vector3 current = host.localScale;
            if (Mathf.Abs(current.x - s) > 0.001f || Mathf.Abs(current.y - s) > 0.001f)
            {
                host.localScale = new Vector3(s, s, 1f);
            }
        }
    }
}
