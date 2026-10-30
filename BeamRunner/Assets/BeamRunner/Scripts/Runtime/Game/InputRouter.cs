using System.Collections.Generic;
using BeamRunner.Core.Field;
using BeamRunner.Core.Meta;
using BeamRunner.Core.Sim;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.UI;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace BeamRunner.Game
{
    /// <summary>
    /// Turns touch, mouse, keyboard and gamepad into <see cref="RunInput"/>: the design's five inputs, all swipes or a
    /// tap anywhere on screen. <see cref="ControlMode.Swipe"/> (default): swipe left / right moves a lane (keep sliding the
    /// same finger for a second lane), swipe up builds a ramp up, swipe down a ramp down, and a tap — a touch that barely
    /// moved — builds a bridge. Every finger is its own gesture, so a second thumb can tap while the first swipes.
    /// <see cref="ControlMode.TapSides"/> adds taps on the outer thirds of the screen as lane moves (the middle still
    /// bridges). Keyboard A/D ←/→ move, W/↑ and S/↓ build ramps, Space bridges; the gamepad's d-pad / stick flicks move
    /// and build ramps, the south button bridges. Touches that start on a button never steer or build.
    /// </summary>
    public sealed class InputRouter
    {
        /// <summary>A swipe must travel this fraction of the screen width (scaled by 1 / sensitivity).</summary>
        private const float SwipeFraction = 0.07f;

        /// <summary>A touch shorter than this (s) that barely moved is a tap.</summary>
        private const float TapSeconds = 0.3f;

        /// <summary>Taps on the outer fraction of the screen move a lane in <see cref="ControlMode.TapSides"/>.</summary>
        private const float SideFraction = 0.3f;

        private const float StickFlick = 0.6f;
        private const float StickRelease = 0.3f;
        private const int MaxGestures = 5;
        private const int MaxQueuedBuilds = 3;

        private struct Gesture
        {
            public int Id;
            public Vector2 Anchor;
            public Vector2 Start;
            public float StartTime;
            public bool Moved;
            public bool Vertical;
        }

        private readonly List<RaycastResult> _uiHits = new List<RaycastResult>(16);
        private readonly List<int> _uiTouchIds = new List<int>(16);
        private readonly Gesture[] _gestures = new Gesture[MaxGestures];
        private readonly Queue<BuildKind> _builds = new Queue<BuildKind>(MaxQueuedBuilds);
        private PointerEventData _pointerData;

        private int _gestureCount;
        private bool _mouseDown;
        private bool _stickLatchedX;
        private bool _stickLatchedY;
        private int _queuedShift;
        private ControlMode _mode = ControlMode.Swipe;

        // Set by ResetGestures, consumed by the next Sample(): a finger already down when the run (re)starts must not
        // count as a tap or a swipe when it lifts.
        private bool _ignoreHeldPointers;

        public InputRouter()
        {
            if (!EnhancedTouchSupport.enabled)
            {
                EnhancedTouchSupport.Enable();
            }
        }

        public ControlMode Mode
        {
            get { return _mode; }
            set { _mode = value; }
        }

        /// <summary>Swipe sensitivity (0.4–2.5; higher = shorter swipes).</summary>
        public float Sensitivity { get; set; } = 1f;

        /// <summary>Swaps swipe up and swipe down (accessibility).</summary>
        public bool InvertY { get; set; }

        /// <summary>True on frames the player switched lanes (dismisses the "swipe to move" hint).</summary>
        public bool IsSteering { get; private set; }

        /// <summary>True on frames the player asked for a build.</summary>
        public bool IsBuilding { get; private set; }

        /// <summary>Queues a build from a HUD button (or a test).</summary>
        public void RequestBuild(BuildKind kind)
        {
            QueueBuild(kind);
        }

        /// <summary>Forgets every gesture in progress (run start, resume, revive).</summary>
        public void ResetGestures()
        {
            _gestureCount = 0;
            _mouseDown = false;
            _queuedShift = 0;
            _builds.Clear();
            IsSteering = false;
            IsBuilding = false;
            _uiTouchIds.Clear();
            _ignoreHeldPointers = true;
        }

        public bool PausePressed()
        {
            Keyboard kb = Keyboard.current;
            if (kb != null && (kb.escapeKey.wasPressedThisFrame || kb.pKey.wasPressedThisFrame))
            {
                return true;
            }

            Gamepad pad = Gamepad.current;
            return pad != null && pad.startButton.wasPressedThisFrame;
        }

        public RunInput Sample()
        {
            int shift = _queuedShift;
            _queuedShift = 0;
            ReadTouches(ref shift);
            ReadMouse(ref shift);
            ReadKeyboardAndGamepad(ref shift);

            var input = new RunInput
            {
                LaneShift = Mathf.Clamp(shift, -2, 2),
                Build = _builds.Count > 0 ? _builds.Dequeue() : BuildKind.None,
            };
            _ignoreHeldPointers = false;
            IsSteering = shift != 0;
            IsBuilding = input.Build != BuildKind.None;
            return input;
        }

        private float SwipeDistance
        {
            get { return Mathf.Max(24f, Screen.width * SwipeFraction / Mathf.Clamp(Sensitivity, 0.4f, 2.5f)); }
        }

        private void QueueBuild(BuildKind kind)
        {
            if (kind != BuildKind.None && _builds.Count < MaxQueuedBuilds)
            {
                _builds.Enqueue(kind);
            }
        }

        private void ReadTouches(ref int shift)
        {
            if (Touchscreen.current == null)
            {
                return;
            }

            var touches = ETouch.activeTouches;
            for (int i = 0; i < touches.Count; i++)
            {
                ETouch t = touches[i];
                Vector2 pos = t.screenPosition;
                if (t.began)
                {
                    if (IsOverInteractiveUI(pos))
                    {
                        RememberUiTouch(t.touchId);
                        continue;
                    }

                    Begin(t.touchId, pos);
                }
                else if (_ignoreHeldPointers && t.isInProgress)
                {
                    RememberUiTouch(t.touchId);
                }

                if (t.ended)
                {
                    _uiTouchIds.Remove(t.touchId);
                }

                int g = Find(t.touchId);
                if (g < 0 || _uiTouchIds.Contains(t.touchId))
                {
                    continue;
                }

                if (t.ended)
                {
                    End(g, pos, ref shift);
                    continue;
                }

                Track(g, pos, ref shift);
            }

            if (touches.Count == 0)
            {
                _uiTouchIds.Clear();
                RemoveTouchGestures();
            }
        }

        private void ReadMouse(ref int shift)
        {
            Mouse mouse = Mouse.current;
            if (mouse == null || (Touchscreen.current != null && ETouch.activeTouches.Count > 0))
            {
                _mouseDown = false;
                return;
            }

            Vector2 pos = mouse.position.ReadValue();
            if (mouse.leftButton.wasPressedThisFrame && !_ignoreHeldPointers && !IsOverInteractiveUI(pos))
            {
                _mouseDown = true;
                Begin(-2, pos);
            }

            if (mouse.rightButton.wasPressedThisFrame && !IsOverInteractiveUI(pos))
            {
                QueueBuild(BuildKind.Bridge);
            }

            int g = Find(-2);
            if (!_mouseDown || g < 0)
            {
                return;
            }

            if (!mouse.leftButton.isPressed)
            {
                _mouseDown = false;
                End(g, pos, ref shift);
                return;
            }

            Track(g, pos, ref shift);
        }

        private void Begin(int id, Vector2 pos)
        {
            if (Find(id) >= 0 || _gestureCount >= MaxGestures)
            {
                return;
            }

            _gestures[_gestureCount++] = new Gesture
            {
                Id = id,
                Anchor = pos,
                Start = pos,
                StartTime = Time.unscaledTime,
            };
        }

        private void Track(int g, Vector2 pos, ref int shift)
        {
            Gesture s = _gestures[g];
            Vector2 d = pos - s.Anchor;
            float threshold = SwipeDistance;
            if (Mathf.Abs(d.x) >= threshold && Mathf.Abs(d.x) > Mathf.Abs(d.y) * 0.8f)
            {
                shift += d.x > 0f ? 1 : -1;
                s.Anchor = pos; // keep sliding for the next lane
                s.Moved = true;
            }
            else if (!s.Vertical && Mathf.Abs(d.y) >= threshold && Mathf.Abs(d.y) > Mathf.Abs(d.x) * 1.2f)
            {
                // One ramp per swipe; the same finger can still slide sideways afterwards.
                bool up = d.y > 0f != InvertY;
                QueueBuild(up ? BuildKind.RampUp : BuildKind.RampDown);
                s.Vertical = true;
                s.Moved = true;
                s.Anchor = pos;
            }
            else if ((pos - s.Start).sqrMagnitude > threshold * threshold * 0.25f)
            {
                s.Moved = true;
            }

            _gestures[g] = s;
        }

        private void End(int g, Vector2 pos, ref int shift)
        {
            Gesture s = _gestures[g];
            bool tap = !s.Moved && Time.unscaledTime - s.StartTime <= TapSeconds &&
                       (pos - s.Start).sqrMagnitude < SwipeDistance * SwipeDistance * 0.25f;
            if (tap)
            {
                float side = Screen.width * SideFraction;
                if (_mode == ControlMode.TapSides && pos.x < side)
                {
                    shift--;
                }
                else if (_mode == ControlMode.TapSides && pos.x > Screen.width - side)
                {
                    shift++;
                }
                else
                {
                    QueueBuild(BuildKind.Bridge);
                }
            }

            Remove(g);
        }

        private int Find(int id)
        {
            for (int i = 0; i < _gestureCount; i++)
            {
                if (_gestures[i].Id == id)
                {
                    return i;
                }
            }

            return -1;
        }

        private void Remove(int g)
        {
            _gestureCount--;
            if (g < _gestureCount)
            {
                _gestures[g] = _gestures[_gestureCount];
            }
        }

        private void RemoveTouchGestures()
        {
            for (int i = _gestureCount - 1; i >= 0; i--)
            {
                if (_gestures[i].Id != -2)
                {
                    Remove(i);
                }
            }
        }

        private void ReadKeyboardAndGamepad(ref int shift)
        {
            Keyboard kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.aKey.wasPressedThisFrame || kb.leftArrowKey.wasPressedThisFrame)
                {
                    shift--;
                }

                if (kb.dKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame)
                {
                    shift++;
                }

                if (kb.wKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame)
                {
                    QueueBuild(InvertY ? BuildKind.RampDown : BuildKind.RampUp);
                }

                if (kb.sKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame)
                {
                    QueueBuild(InvertY ? BuildKind.RampUp : BuildKind.RampDown);
                }

                if (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame)
                {
                    QueueBuild(BuildKind.Bridge);
                }
            }

            Gamepad pad = Gamepad.current;
            if (pad == null)
            {
                return;
            }

            if (pad.dpad.left.wasPressedThisFrame || pad.leftShoulder.wasPressedThisFrame)
            {
                shift--;
            }

            if (pad.dpad.right.wasPressedThisFrame || pad.rightShoulder.wasPressedThisFrame)
            {
                shift++;
            }

            if (pad.dpad.up.wasPressedThisFrame || pad.buttonNorth.wasPressedThisFrame)
            {
                QueueBuild(InvertY ? BuildKind.RampDown : BuildKind.RampUp);
            }

            if (pad.dpad.down.wasPressedThisFrame || pad.buttonEast.wasPressedThisFrame)
            {
                QueueBuild(InvertY ? BuildKind.RampUp : BuildKind.RampDown);
            }

            if (pad.buttonSouth.wasPressedThisFrame || pad.rightTrigger.wasPressedThisFrame)
            {
                QueueBuild(BuildKind.Bridge);
            }

            // A stick flick is one move; it must come back toward the centre before it counts again.
            Vector2 stick = pad.leftStick.ReadValue();
            if (!_stickLatchedX && Mathf.Abs(stick.x) >= StickFlick && Mathf.Abs(stick.x) > Mathf.Abs(stick.y))
            {
                _stickLatchedX = true;
                shift += stick.x > 0f ? 1 : -1;
            }
            else if (_stickLatchedX && Mathf.Abs(stick.x) <= StickRelease)
            {
                _stickLatchedX = false;
            }

            if (!_stickLatchedY && Mathf.Abs(stick.y) >= StickFlick && Mathf.Abs(stick.y) > Mathf.Abs(stick.x))
            {
                _stickLatchedY = true;
                bool up = stick.y > 0f != InvertY;
                QueueBuild(up ? BuildKind.RampUp : BuildKind.RampDown);
            }
            else if (_stickLatchedY && Mathf.Abs(stick.y) <= StickRelease)
            {
                _stickLatchedY = false;
            }
        }

        /// <summary>A touch that started on a button: it never steers.</summary>
        private void RememberUiTouch(int touchId)
        {
            if (!_uiTouchIds.Contains(touchId) && _uiTouchIds.Count < 16)
            {
                _uiTouchIds.Add(touchId);
            }
        }

        /// <summary>True if a screen position is over a button/slider/toggle (not just any UI graphic).</summary>
        private bool IsOverInteractiveUI(Vector2 screenPosition)
        {
            EventSystem es = EventSystem.current;
            if (es == null)
            {
                return false;
            }

            if (_pointerData == null || _pointerData.GetType() != typeof(PointerEventData))
            {
                _pointerData = new PointerEventData(es);
            }

            _pointerData.position = screenPosition;
            _uiHits.Clear();
            es.RaycastAll(_pointerData, _uiHits);
            for (int i = 0; i < _uiHits.Count; i++)
            {
                GameObject go = _uiHits[i].gameObject;
                if (go != null && go.GetComponentInParent<Selectable>() != null)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
