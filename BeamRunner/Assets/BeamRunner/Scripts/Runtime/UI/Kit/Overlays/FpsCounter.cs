using UnityEngine;
using UnityEngine.UI;

namespace BeamRunner.UI
{
    /// <summary>Small debug FPS readout (top-left of the Debug layer). Zero allocations: strings are cached.</summary>
    [DisallowMultipleComponent]
    public sealed class FpsCounter : MonoBehaviour
    {
        private const int MaxFps = 480;
        private static string[] _cache;

        private Text _text;
        private float _accum;
        private int _frames;
        private int _shown = -1;

        public static FpsCounter Create(RectTransform parent)
        {
            if (_cache == null)
            {
                _cache = new string[MaxFps + 1];
                for (int i = 0; i <= MaxFps; i++)
                {
                    _cache[i] = i + " FPS"; // l10n-ok: debug overlay, FPS is a universal unit
                }
            }

            RectTransform rt = UIFactory.CreateRect(parent, "FpsCounter");
            UIFactory.Anchor(rt, AnchorPreset.TopLeft, new Vector2(16f, -16f));
            rt.sizeDelta = new Vector2(220f, 56f);
            Image bg = UIFactory.PillImage(rt, new Color(0f, 0f, 0f, 0.55f), "Bg");
            bg.raycastTarget = false;
            FpsCounter f = rt.gameObject.AddComponent<FpsCounter>();
            f._text = UIFactory.LabelLine(rt, "-- FPS", 32, UITheme.Success, TextAnchor.MiddleCenter, UIFont.Bold); // l10n-ok: debug overlay
            UIFactory.Stretch(f._text.rectTransform);
            return f;
        }

        private void Update()
        {
            _accum += Time.unscaledDeltaTime;
            _frames++;
            if (_accum < 0.5f)
            {
                return;
            }

            int fps = Mathf.Clamp(Mathf.RoundToInt(_frames / _accum), 0, MaxFps);
            _accum = 0f;
            _frames = 0;
            if (fps == _shown)
            {
                return;
            }

            _shown = fps;
            _text.text = _cache[fps];
            _text.color = fps >= 55 ? UITheme.Success : (fps >= 30 ? UITheme.Warning : UITheme.Danger);
        }
    }
}
