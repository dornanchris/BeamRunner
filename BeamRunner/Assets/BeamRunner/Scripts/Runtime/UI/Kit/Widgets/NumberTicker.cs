using System.Globalization;
using BeamRunner.Core.Localization;
using UnityEngine;
using UnityEngine.UI;

namespace BeamRunner.UI
{
    /// <summary>
    /// Animated count-up/down for a <see cref="UnityEngine.UI.Text"/> (scores, currency). Formats with thousands
    /// separators and optional prefix/suffix; the text is only rebuilt when the displayed integer changes, and the
    /// component disables itself when idle (no per-frame cost).
    /// </summary>
    [RequireComponent(typeof(Text))]
    [DisallowMultipleComponent]
    public sealed class NumberTicker : MonoBehaviour
    {
        private Text _text;
        private long _target;
        private long _shownValue = long.MinValue;
        private double _from;
        private double _current;
        private float _elapsed;
        private float _duration;
        private string _prefix = string.Empty;
        private string _suffix = string.Empty;
        private bool _grouping = true;

        /// <summary>Invoked each time the displayed number changes while animating (e.g. tick sounds).</summary>
        public System.Action<long> OnTick;

        public long Value
        {
            get { return _target; }
        }

        public Text Text
        {
            get
            {
                if (_text == null)
                {
                    _text = GetComponent<Text>();
                }

                return _text;
            }
        }

        /// <summary>Adds (or returns) a ticker on <paramref name="text"/>.</summary>
        public static NumberTicker Attach(Text text, long initial = 0, string prefix = "", string suffix = "")
        {
            NumberTicker t = text.GetComponent<NumberTicker>();
            if (t == null)
            {
                t = text.gameObject.AddComponent<NumberTicker>();
            }

            t._prefix = prefix ?? string.Empty;
            t._suffix = suffix ?? string.Empty;
            t.SetValue(initial, false);
            return t;
        }

        public void SetFormat(string prefix, string suffix, bool grouping = true)
        {
            _prefix = prefix ?? string.Empty;
            _suffix = suffix ?? string.Empty;
            _grouping = grouping;
            _shownValue = long.MinValue;
            Show((long)System.Math.Round(_current));
        }

        /// <summary>
        /// Sets the number. When animating, the duration scales with the size of the change
        /// (0.25–1.2 s unless <paramref name="duration"/> is given).
        /// </summary>
        public void SetValue(long value, bool animate = true, float duration = -1f)
        {
            _target = value;
            if (!animate || !Application.isPlaying)
            {
                _current = value;
                _duration = 0f;
                Show(value);
                enabled = false;
                return;
            }

            double delta = System.Math.Abs(value - _current);
            if (delta < 0.5)
            {
                _current = value;
                Show(value);
                enabled = false;
                return;
            }

            _from = _current;
            _elapsed = 0f;
            _duration = duration > 0f ? duration : Mathf.Clamp(0.25f + (float)System.Math.Log10(delta + 1.0) * 0.22f, 0.25f, 1.2f);
            enabled = true;
        }

        /// <summary>Jumps to the target immediately.</summary>
        public void Complete()
        {
            _current = _target;
            Show(_target);
            enabled = false;
        }

        private void Update()
        {
            if (_duration <= 0f)
            {
                enabled = false;
                return;
            }

            _elapsed += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(_elapsed / _duration);
            float e = UIEaseFunctions.Evaluate(UIEase.OutCubic, p);
            _current = _from + (_target - _from) * e;
            long shown = p >= 1f ? _target : (long)System.Math.Round(_current);
            if (shown != _shownValue)
            {
                Show(shown);
                if (OnTick != null)
                {
                    try
                    {
                        OnTick(shown);
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogException(ex);
                    }
                }
            }

            if (p >= 1f)
            {
                _current = _target;
                _duration = 0f;
                enabled = false;
            }
        }

        private void Show(long value)
        {
            if (value == _shownValue && Text.text.Length > 0)
            {
                return;
            }

            _shownValue = value;
            string num = _grouping ? Loc.N(value) : value.ToString(CultureInfo.InvariantCulture);
            Text.text = _prefix.Length == 0 && _suffix.Length == 0 ? num : _prefix + num + _suffix;
        }
    }
}
