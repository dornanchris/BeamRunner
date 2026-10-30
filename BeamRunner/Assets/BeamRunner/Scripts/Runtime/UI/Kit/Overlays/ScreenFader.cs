using System;
using UnityEngine;
using UnityEngine.UI;

namespace BeamRunner.UI
{
    /// <summary>
    /// Full-screen fade to/from black (or any colour) on the Overlay layer, in unscaled time. Blocks input while not
    /// fully clear. Access through <see cref="Current"/> or <c>UIRoot.Instance.Fader</c>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ScreenFader : MonoBehaviour
    {
        private Image _image;
        private int _tween;
        private bool _busy;

        /// <summary>The fader of the active UI root (created on demand).</summary>
        public static ScreenFader Current
        {
            get
            {
                UIRoot root = UIRoot.Ensure();
                return root != null ? root.Fader : null;
            }
        }

        /// <summary>True while fading or fully covering the screen.</summary>
        public bool IsBusy
        {
            get { return _busy || (_image != null && _image.enabled); }
        }

        /// <summary>True when the screen is fully covered.</summary>
        public bool IsOpaque
        {
            get { return _image != null && _image.enabled && _image.color.a >= 0.999f; }
        }

        public static ScreenFader Create(RectTransform overlayLayer)
        {
            RectTransform rt = UIFactory.CreateFill(overlayLayer, "ScreenFader");
            ScreenFader f = rt.gameObject.AddComponent<ScreenFader>();
            f._image = rt.gameObject.AddComponent<Image>();
            f._image.color = new Color(0f, 0f, 0f, 0f);
            f._image.raycastTarget = true;
            f._image.enabled = false;
            return f;
        }

        /// <summary>Fades to an opaque colour (default black).</summary>
        public void FadeOut(float duration = 0.35f, Color? color = null, Action onComplete = null)
        {
            Color c = color ?? Color.black;
            UITween.KillId(_tween);
            Color start = _image.enabled ? _image.color : new Color(c.r, c.g, c.b, 0f);
            start.r = c.r;
            start.g = c.g;
            start.b = c.b;
            _image.color = start;
            _image.enabled = true;
            _image.raycastTarget = true;
            _busy = true;
            if (duration <= 0f || !Application.isPlaying)
            {
                _image.color = new Color(c.r, c.g, c.b, 1f);
                _busy = false;
                UIFactory.SafeInvoke(onComplete);
                return;
            }

            _tween = UITween.Alpha(_image, 1f, duration).SetEase(UIEase.InOutSine).OnComplete(() =>
            {
                _busy = false;
                UIFactory.SafeInvoke(onComplete);
            }).Id;
        }

        /// <summary>Fades back to fully clear.</summary>
        public void FadeIn(float duration = 0.35f, Action onComplete = null)
        {
            UITween.KillId(_tween);
            if (!_image.enabled)
            {
                UIFactory.SafeInvoke(onComplete);
                return;
            }

            _busy = true;
            if (duration <= 0f || !Application.isPlaying)
            {
                Clear();
                UIFactory.SafeInvoke(onComplete);
                return;
            }

            _tween = UITween.Alpha(_image, 0f, duration).SetEase(UIEase.InOutSine).OnComplete(() =>
            {
                Clear();
                UIFactory.SafeInvoke(onComplete);
            }).Id;
        }

        /// <summary>Fades out, runs <paramref name="atMidpoint"/> while covered, then fades back in.</summary>
        public void FadeThrough(Action atMidpoint, float duration = 0.7f, Color? color = null, Action onComplete = null)
        {
            float half = Mathf.Max(0.01f, duration * 0.5f);
            FadeOut(half, color, () =>
            {
                UIFactory.SafeInvoke(atMidpoint);
                FadeIn(half, onComplete);
            });
        }

        /// <summary>Sets the cover instantly (alpha 0 clears).</summary>
        public void SetInstant(float alpha, Color? color = null)
        {
            UITween.KillId(_tween);
            if (alpha <= 0.001f)
            {
                Clear();
                return;
            }

            Color c = color ?? Color.black;
            _image.color = new Color(c.r, c.g, c.b, Mathf.Clamp01(alpha));
            _image.enabled = true;
        }

        /// <summary>Quick translucent colour flash (e.g. a white hit flash); respects <see cref="UITheme.ReducedMotion"/>.</summary>
        public void Flash(Color color, float peakAlpha = 0.5f, float duration = 0.3f)
        {
            if (UITheme.ReducedMotion)
            {
                peakAlpha *= 0.3f;
            }

            UITween.KillId(_tween);
            _image.color = new Color(color.r, color.g, color.b, peakAlpha);
            _image.enabled = true;
            _image.raycastTarget = false;
            _tween = UITween.Alpha(_image, 0f, duration).SetEase(UIEase.OutQuad).OnComplete(Clear).Id;
        }

        private void Clear()
        {
            _busy = false;
            Color c = _image.color;
            c.a = 0f;
            _image.color = c;
            _image.enabled = false;
            _image.raycastTarget = true;
        }
    }
}
