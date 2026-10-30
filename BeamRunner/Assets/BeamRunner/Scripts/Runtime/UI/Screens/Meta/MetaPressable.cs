using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace BeamRunner.UI
{
    /// <summary>
    /// Lightweight tap target for custom meta-screen widgets (rank badge, catchers over disabled buttons): optional press
    /// squash on <see cref="Visual"/>, click feedback, and a click callback. Drags pass through to parents (scroll lists,
    /// swipe areas) because it implements no drag handlers.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MetaPressable : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
    {
        /// <summary>Invoked on click.</summary>
        public Action Clicked;

        /// <summary>Transform squashed while pressed (null = no squash).</summary>
        public Transform Visual;

        /// <summary>Play the kit's click sound/haptic on click.</summary>
        public bool Feedback = true;

        /// <summary>Adds a hit area (invisible graphic) plus a pressable to <paramref name="target"/>.</summary>
        public static MetaPressable Attach(RectTransform target, Action onClick, Transform visual = null, bool feedback = true)
        {
            if (target.GetComponent<UnityEngine.UI.Graphic>() == null)
            {
                target.gameObject.AddComponent<UIRaycastTarget>();
            }

            MetaPressable p = target.GetComponent<MetaPressable>();
            if (p == null)
            {
                p = target.gameObject.AddComponent<MetaPressable>();
            }

            p.Clicked = onClick;
            p.Visual = visual;
            p.Feedback = feedback;
            return p;
        }

        /// <summary>
        /// Invisible catcher stretched over a button: while active it receives taps (e.g. on a disabled BUY button to
        /// explain why). Starts inactive.
        /// </summary>
        public static MetaPressable Catcher(RectTransform button, Action onClick)
        {
            RectTransform rt = UIFactory.CreateFill(button, "TapCatcher");
            UIFactory.IgnoreLayout(rt);
            MetaPressable p = Attach(rt, onClick, null, false);
            rt.gameObject.SetActive(false);
            return p;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (Visual != null)
            {
                UITween.Scale(Visual, 0.95f, 0.1f).SetEase(UIEase.OutCubic);
            }
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (Visual != null)
            {
                UITween.Scale(Visual, 1f, 0.35f).SetEase(UIEase.OutBack);
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData != null && eventData.button != PointerEventData.InputButton.Left)
            {
                return;
            }

            if (Feedback)
            {
                UIFactory.PlayClickFeedback();
            }

            if (Clicked != null)
            {
                try
                {
                    Clicked();
                }
                catch (Exception e)
                {
                    Debug.LogException(e, this);
                }
            }
        }

        private void OnDisable()
        {
            if (Visual != null)
            {
                UITween.Kill(Visual);
                Visual.localScale = Vector3.one;
            }
        }
    }
}
