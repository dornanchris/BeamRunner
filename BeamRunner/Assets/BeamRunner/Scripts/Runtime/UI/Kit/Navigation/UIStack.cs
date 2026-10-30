using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace BeamRunner.UI
{
    /// <summary>
    /// Screen navigation stack with back handling (Android back / Escape, gamepad B). Back goes to the open dialog
    /// first, then to the top screen's <see cref="UIScreen.OnBack"/>, then pops; at the root it invokes
    /// <see cref="BackAtRoot"/>. Also tracks whether the player is navigating with keys/gamepad and focuses the top
    /// screen's <see cref="UIScreen.DefaultSelection"/> when they start doing so. Lives on the <see cref="UIRoot"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UIStack : MonoBehaviour
    {
        private readonly List<UIScreen> _screens = new List<UIScreen>(8);
        private UIRoot _root;
        private bool _navigating;

        /// <summary>Invoked when back is pressed on the root screen and nothing handled it (e.g. open a quit dialog).</summary>
        public Action BackAtRoot;

        /// <summary>Disable to ignore back input (e.g. during a run, where pause handles Escape itself).</summary>
        public bool BackEnabled = true;

        /// <summary>Raised after the top screen changed (argument: new top, may be null).</summary>
        public event Action<UIScreen> Changed;

        public UIScreen Top
        {
            get { return _screens.Count > 0 ? _screens[_screens.Count - 1] : null; }
        }

        public int Count
        {
            get { return _screens.Count; }
        }

        /// <summary>True while the player uses keyboard/gamepad navigation (buttons show focus highlights).</summary>
        public bool IsNavigating
        {
            get { return _navigating; }
        }

        internal void Init(UIRoot root)
        {
            _root = root;
        }

        public bool Contains(UIScreen screen)
        {
            return _screens.Contains(screen);
        }

        /// <summary>Hides the current top (kept in the stack) and shows <paramref name="screen"/> on top.</summary>
        public void Push(UIScreen screen, bool instant = false)
        {
            if (screen == null)
            {
                return;
            }

            UIScreen top = Top;
            if (top == screen)
            {
                if (!screen.IsVisible)
                {
                    screen.Show(instant);
                }

                return;
            }

            _screens.Remove(screen);
            if (top != null)
            {
                top.Hide(instant);
            }

            _screens.Add(screen);
            screen.Stack = this;
            screen.Show(instant);
            AfterChange();
        }

        /// <summary>Hides and removes the top screen, showing the one below. Returns the popped screen.</summary>
        public UIScreen Pop(bool instant = false)
        {
            if (_screens.Count == 0)
            {
                return null;
            }

            UIScreen top = _screens[_screens.Count - 1];
            _screens.RemoveAt(_screens.Count - 1);
            top.Hide(instant);
            top.Stack = null;
            UIScreen next = Top;
            if (next != null)
            {
                next.Show(instant);
            }

            AfterChange();
            return top;
        }

        /// <summary>Replaces the top screen with <paramref name="screen"/>.</summary>
        public void Replace(UIScreen screen, bool instant = false)
        {
            if (screen == null)
            {
                return;
            }

            if (_screens.Count > 0)
            {
                UIScreen top = _screens[_screens.Count - 1];
                _screens.RemoveAt(_screens.Count - 1);
                if (top != screen)
                {
                    top.Hide(instant);
                    top.Stack = null;
                }
            }

            _screens.Remove(screen);
            _screens.Add(screen);
            screen.Stack = this;
            screen.Show(instant);
            AfterChange();
        }

        /// <summary>Clears the stack (hiding everything) and shows <paramref name="screen"/> as the only entry.</summary>
        public void SetRoot(UIScreen screen, bool instant = false)
        {
            for (int i = _screens.Count - 1; i >= 0; i--)
            {
                UIScreen s = _screens[i];
                if (s != screen && s != null)
                {
                    s.Hide(instant);
                    s.Stack = null;
                }
            }

            _screens.Clear();
            if (screen != null)
            {
                _screens.Add(screen);
                screen.Stack = this;
                screen.Show(instant);
            }

            AfterChange();
        }

        /// <summary>Pops until <paramref name="screen"/> is on top (no-op if it isn't in the stack).</summary>
        public void PopTo(UIScreen screen, bool instant = false)
        {
            int index = _screens.IndexOf(screen);
            if (index < 0 || index == _screens.Count - 1)
            {
                return;
            }

            for (int i = _screens.Count - 1; i > index; i--)
            {
                _screens[i].Hide(instant);
                _screens[i].Stack = null;
                _screens.RemoveAt(i);
            }

            screen.Show(instant);
            AfterChange();
        }

        /// <summary>Hides every screen and empties the stack (e.g. when a run starts).</summary>
        public void Clear(bool instant = false)
        {
            SetRoot(null, instant);
        }

        /// <summary>Performs a back action. Returns true if something handled it.</summary>
        public bool HandleBack()
        {
            if (Dialogs.HandleBack())
            {
                return true;
            }

            UIScreen top = Top;
            if (top != null)
            {
                bool handled;
                try
                {
                    handled = top.OnBack();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    handled = true;
                }

                if (handled)
                {
                    return true;
                }

                if (_screens.Count > 1)
                {
                    UIFactory.PlayClickFeedback();
                    Pop();
                    return true;
                }
            }

            if (BackAtRoot != null)
            {
                UIFactory.SafeInvoke(BackAtRoot);
                return true;
            }

            return false;
        }

        private void AfterChange()
        {
            for (int i = _screens.Count - 1; i >= 0; i--)
            {
                if (_screens[i] == null)
                {
                    _screens.RemoveAt(i);
                }
            }

            if (_navigating)
            {
                FocusDefault(true);
            }

            Action<UIScreen> handler = Changed;
            if (handler != null)
            {
                try
                {
                    handler(Top);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        private void Update()
        {
            bool back = false;
            bool nav = false;
            bool pointer = false;

            Keyboard kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.escapeKey.wasPressedThisFrame)
                {
                    back = true;
                }

                if (kb.upArrowKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame ||
                    kb.leftArrowKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame || kb.tabKey.wasPressedThisFrame)
                {
                    nav = true;
                }
            }

            Gamepad gp = Gamepad.current;
            if (gp != null)
            {
                if (gp.buttonEast.wasPressedThisFrame)
                {
                    back = true;
                }

                if (gp.buttonSouth.wasPressedThisFrame || gp.leftStick.ReadValue().sqrMagnitude > 0.3f || gp.dpad.ReadValue().sqrMagnitude > 0.3f)
                {
                    nav = true;
                }
            }

            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                pointer = true;
            }

            Touchscreen touch = Touchscreen.current;
            if (touch != null && touch.primaryTouch.press.wasPressedThisFrame)
            {
                pointer = true;
            }

            if (pointer)
            {
                _navigating = false;
            }
            else if (nav)
            {
                // Entering navigation mode, or the selection was lost (e.g. a dialog closed): focus the default.
                _navigating = true;
                FocusDefault(false);
            }

            if (back && BackEnabled && (_root == null || _root.Fader == null || !_root.Fader.IsBusy))
            {
                HandleBack();
            }
        }

        private void FocusDefault(bool force)
        {
            EventSystem es = EventSystem.current;
            if (es == null)
            {
                return;
            }

            GameObject current = es.currentSelectedGameObject;
            if (!force && current != null && current.activeInHierarchy)
            {
                return;
            }

            GameObject target = Dialogs.CurrentDefaultSelection;
            if (target == null && Top != null)
            {
                target = Top.DefaultSelection;
            }

            if (target != null && target.activeInHierarchy)
            {
                es.SetSelectedGameObject(target);
            }
        }
    }
}
