using UnityEngine;
using UnityEngine.UI;

namespace BeamRunner.UI
{
    /// <summary>Which theme accent a <see cref="UIAccentFollower"/> tracks.</summary>
    public enum AccentSlot
    {
        Primary = 0,
        Secondary = 1,
    }

    /// <summary>Keeps a graphic's colour equal to the current theme accent (with an alpha), updated live.</summary>
    [RequireComponent(typeof(Graphic))]
    [DisallowMultipleComponent]
    public sealed class UIAccentFollower : MonoBehaviour
    {
        private Graphic _graphic;
        private AccentSlot _slot;
        private float _alpha = 1f;
        private float _lighten;

        public AccentSlot Slot
        {
            get { return _slot; }
            set
            {
                _slot = value;
                ApplyNow();
            }
        }

        public float Alpha
        {
            get { return _alpha; }
            set
            {
                _alpha = value;
                ApplyNow();
            }
        }

        /// <summary>Adds (or reconfigures) a follower on <paramref name="graphic"/>.</summary>
        public static UIAccentFollower Attach(Graphic graphic, AccentSlot slot = AccentSlot.Primary, float alpha = 1f, float lighten = 0f)
        {
            UIAccentFollower f = graphic.GetComponent<UIAccentFollower>();
            if (f == null)
            {
                f = graphic.gameObject.AddComponent<UIAccentFollower>();
            }

            f._graphic = graphic;
            f._slot = slot;
            f._alpha = alpha;
            f._lighten = lighten;
            f.enabled = true;
            f.ApplyNow();
            return f;
        }

        /// <summary>Stops following (keeps the current colour); <see cref="Attach"/> resumes.</summary>
        public static void Detach(Graphic graphic)
        {
            if (graphic == null)
            {
                return;
            }

            UIAccentFollower f = graphic.GetComponent<UIAccentFollower>();
            if (f != null)
            {
                f.enabled = false;
            }
        }

        private void OnEnable()
        {
            UITheme.AccentChanged += ApplyNow;
            ApplyNow();
        }

        private void OnDisable()
        {
            UITheme.AccentChanged -= ApplyNow;
        }

        public void ApplyNow()
        {
            if (_graphic == null)
            {
                _graphic = GetComponent<Graphic>();
                if (_graphic == null)
                {
                    return;
                }
            }

            Color c = _slot == AccentSlot.Primary ? UITheme.Accent : UITheme.AccentAlt;
            if (_lighten > 0f)
            {
                c = UITheme.Lighten(c, _lighten);
            }

            c.a = _alpha;
            _graphic.color = c;
        }
    }
}
