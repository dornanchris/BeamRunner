using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BeamRunner.UI
{
    /// <summary>
    /// A uGUI <see cref="Slider"/> that shares its drags with an enclosing <see cref="ScrollRect"/> (e.g. a settings list).
    /// uGUI's Slider claims every drag that starts on it, without a drag threshold, so a list could not be scrolled by a
    /// swipe that happened to start on a slider. Inside a ScrollRect this slider waits until the pointer has moved past
    /// the EventSystem's drag threshold and then arbitrates by direction:
    /// <list type="bullet">
    /// <item>mostly across its axis (vertical, for a horizontal slider) → the gesture is forwarded to the ScrollRect
    /// (initialize / begin / drag / end drag), so the list scrolls and flings as if the drag had started on its background;</item>
    /// <item>mostly along its axis → the value follows the pointer, as with a plain Slider (grabbing the handle keeps its offset);</item>
    /// <item>no drag (a tap) → the value jumps to the tapped point on release (on the handle: unchanged), like a plain
    /// Slider's press.</item>
    /// </list>
    /// A non-interactable slider forwards every drag. Outside a ScrollRect it is exactly a Slider. Keyboard/gamepad
    /// (left/right steps the value) is unchanged.
    /// </summary>
    public sealed class ScrollAwareSlider : Slider, IBeginDragHandler, IEndDragHandler
    {
        private enum Gesture
        {
            /// <summary>No press, or a press outside any ScrollRect (plain Slider behaviour).</summary>
            None = 0,

            /// <summary>Pressed inside a ScrollRect; not moved past the drag threshold yet.</summary>
            Pending = 1,

            /// <summary>Dragging the value.</summary>
            Slide = 2,

            /// <summary>The gesture belongs to the ScrollRect.</summary>
            Scroll = 3,
        }

        private Gesture _gesture;
        private ScrollRect _scroll;
        private PointerEventData _scrollEvent;
        private bool _canSlide;
        private bool _pressOnHandle;
        private Vector2 _offset;

        /// <summary>Raised when a press on the slider turned into a list scroll (e.g. to release a pressed look).</summary>
        public event Action ScrollStarted;

        /// <summary>True while the current drag is being forwarded to the enclosing ScrollRect.</summary>
        public bool IsScrollingParent
        {
            get { return _gesture == Gesture.Scroll; }
        }

        private bool Horizontal
        {
            get { return direction == Direction.LeftToRight || direction == Direction.RightToLeft; }
        }

        private bool Reversed
        {
            get { return direction == Direction.RightToLeft || direction == Direction.TopToBottom; }
        }

        public override void OnPointerDown(PointerEventData eventData)
        {
            EndScroll();
            _gesture = Gesture.None;
            _scroll = FindScrollRect();
            if (_scroll == null || eventData.button != PointerEventData.InputButton.Left)
            {
                base.OnPointerDown(eventData);
                return;
            }

            // Whether this press is a tap, a slide or a list scroll is decided once it moves or ends; until then only
            // the selection changes (as in Selectable.OnPointerDown) and the value stays put.
            _gesture = Gesture.Pending;
            _canSlide = IsActive() && IsInteractable();
            _pressOnHandle = false;
            _offset = Vector2.zero;
            if (!_canSlide)
            {
                return;
            }

            if (navigation.mode != Navigation.Mode.None && EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(gameObject, eventData);
            }

            RectTransform handle = handleRect;
            Vector2 pressPoint = eventData.pointerPressRaycast.screenPosition;
            if (handle != null && RectTransformUtility.RectangleContainsScreenPoint(handle, pressPoint, eventData.enterEventCamera))
            {
                _pressOnHandle = true;
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(handle, pressPoint, eventData.pressEventCamera, out Vector2 local))
                {
                    _offset = local;
                }
            }
        }

        public override void OnInitializePotentialDrag(PointerEventData eventData)
        {
            if (_gesture != Gesture.Pending)
            {
                base.OnInitializePotentialDrag(eventData);
                return;
            }

            // Keep the threshold, so taps stay taps and a real drag has a direction; and stop a flinging list, as a touch
            // anywhere else on it would.
            eventData.useDragThreshold = true;
            if (_scroll != null)
            {
                _scroll.OnInitializePotentialDrag(eventData);
            }
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (_gesture != Gesture.Pending)
            {
                return;
            }

            Vector2 d = eventData.position - eventData.pressPosition;
            bool alongAxis = Horizontal ? Mathf.Abs(d.x) >= Mathf.Abs(d.y) : Mathf.Abs(d.y) >= Mathf.Abs(d.x);
            if (_canSlide && alongAxis)
            {
                _gesture = Gesture.Slide;
                SetFromScreenPoint(eventData.position, eventData.pressEventCamera);
                return;
            }

            _gesture = Gesture.Scroll;
            if (_scroll != null && _scroll.isActiveAndEnabled)
            {
                _scrollEvent = eventData;
                _scroll.OnBeginDrag(eventData);
            }

            Action handler = ScrollStarted;
            if (handler != null)
            {
                try
                {
                    handler();
                }
                catch (Exception e)
                {
                    Debug.LogException(e, this);
                }
            }
        }

        public override void OnDrag(PointerEventData eventData)
        {
            switch (_gesture)
            {
                case Gesture.None:
                    base.OnDrag(eventData);
                    break;

                case Gesture.Slide:
                    if (IsActive() && IsInteractable() && eventData.button == PointerEventData.InputButton.Left)
                    {
                        SetFromScreenPoint(eventData.position, eventData.pressEventCamera);
                    }

                    break;

                case Gesture.Scroll:
                    if (_scrollEvent != null && _scroll != null && _scroll.isActiveAndEnabled)
                    {
                        _scroll.OnDrag(eventData);
                    }

                    break;
            }
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (_gesture == Gesture.Scroll)
            {
                EndScroll();
            }

            _gesture = Gesture.None;
        }

        public override void OnPointerUp(PointerEventData eventData)
        {
            if (_gesture == Gesture.Pending)
            {
                // A tap (the drag never began): jump to the tapped point, like a plain Slider's press does.
                if (_canSlide && !_pressOnHandle && !eventData.dragging && IsActive() && IsInteractable())
                {
                    SetFromScreenPoint(eventData.pressPosition, eventData.pressEventCamera);
                }

                _gesture = Gesture.None;
            }

            base.OnPointerUp(eventData);
        }

        protected override void OnDisable()
        {
            // Hidden mid-gesture: no more events arrive here, so hand the ScrollRect its end of drag now.
            EndScroll();
            _gesture = Gesture.None;
            base.OnDisable();
        }

        private void EndScroll()
        {
            PointerEventData e = _scrollEvent;
            _scrollEvent = null;
            if (e != null && _scroll != null)
            {
                _scroll.OnEndDrag(e);
            }
        }

        private ScrollRect FindScrollRect()
        {
            Transform parent = transform.parent;
            ScrollRect scroll = parent != null ? parent.GetComponentInParent<ScrollRect>() : null;
            if (scroll == null || !scroll.isActiveAndEnabled)
            {
                return null;
            }

            // Only a list that scrolls across this slider's axis competes for the drag.
            return (Horizontal ? scroll.vertical : scroll.horizontal) ? scroll : null;
        }

        // Same mapping as Slider.UpdateDrag (private there): the handle's container spans the value range.
        private void SetFromScreenPoint(Vector2 screenPoint, Camera cam)
        {
            RectTransform container = handleRect != null ? handleRect.parent as RectTransform : null;
            if (container == null && fillRect != null)
            {
                container = fillRect.parent as RectTransform;
            }

            if (container == null)
            {
                return;
            }

            int axis = Horizontal ? 0 : 1;
            Rect r = container.rect;
            if (r.size[axis] <= 0f || !RectTransformUtility.ScreenPointToLocalPointInRectangle(container, screenPoint, cam, out Vector2 local))
            {
                return;
            }

            local -= r.position;
            float t = Mathf.Clamp01((local - _offset)[axis] / r.size[axis]);
            normalizedValue = Reversed ? 1f - t : t;
        }
    }
}
