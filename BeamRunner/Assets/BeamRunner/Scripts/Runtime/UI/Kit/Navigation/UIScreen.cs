using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BeamRunner.UI
{
    /// <summary>How a <see cref="UIScreen"/> enters and leaves.</summary>
    public enum ScreenTransition
    {
        /// <summary>Fade plus a slight zoom (default).</summary>
        Zoom = 0,
        Fade = 1,
        SlideUp = 2,
        SlideLeft = 3,
        None = 4,
    }

    /// <summary>
    /// Base class for full screens (menu, hangar, settings, HUD, …). A screen is a stretched RectTransform with a
    /// CanvasGroup; it is deactivated while hidden and only blocks raycasts while visible.
    /// <para>Create with <see cref="Create{T}"/> (content built immediately) or <see cref="CreateDeferred{T}"/> (content
    /// built later: progressively by the <see cref="UIBuildQueue"/>, or on the spot by the first <see cref="Show"/> /
    /// <see cref="EnsureBuilt"/>). Override <see cref="Build"/> to construct the content once — or
    /// <see cref="BuildSteps"/> to split a big screen into steps the build queue can spread over frames — and the
    /// <c>On*</c> hooks to bind data. Navigate with <see cref="UIStack"/> or call <see cref="Show"/>/<see cref="Hide"/>
    /// directly (e.g. for the HUD).</para>
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class UIScreen : MonoBehaviour
    {
        private RectTransform _rect;
        private CanvasGroup _group;
        private bool _initialized;
        private bool _buildStarted;
        private bool _built;
        private bool _stepping;
        private IEnumerator _buildSteps;
        private bool _everShown;
        private bool _visible;
        private bool _transitioning;
        private Action _shownHandler;
        private Action _hiddenHandler;

        /// <summary>Entrance/exit style.</summary>
        public ScreenTransition Transition = ScreenTransition.Zoom;

        /// <summary>Show duration in seconds (hide is ~75% of it).</summary>
        public float TransitionDuration = 0.3f;

        /// <summary>Selectable focused when navigating with a gamepad/keyboard (optional).</summary>
        public GameObject DefaultSelection;

        public RectTransform Rect
        {
            get
            {
                EnsureInit();
                return _rect;
            }
        }

        public CanvasGroup Group
        {
            get
            {
                EnsureInit();
                return _group;
            }
        }

        public bool IsVisible
        {
            get { return _visible; }
        }

        public bool IsTransitioning
        {
            get { return _transitioning; }
        }

        /// <summary>True once the content has been built completely (see <see cref="EnsureBuilt"/>).</summary>
        public bool IsBuilt
        {
            get { return _built; }
        }

        /// <summary>The stack that currently holds this screen (null if shown directly).</summary>
        public UIStack Stack { get; internal set; }

        /// <summary>
        /// Creates a screen of type <typeparamref name="T"/> under <paramref name="parent"/> (usually
        /// <c>UIRoot.Instance.GetLayer(UILayer.Screens)</c>), runs <see cref="Build"/> and leaves it hidden.
        /// </summary>
        public static T Create<T>(Transform parent, string name = null) where T : UIScreen
        {
            RectTransform rt = UIFactory.CreateFill(parent, name ?? typeof(T).Name);
            rt.gameObject.AddComponent<CanvasGroup>();
            T screen = rt.gameObject.AddComponent<T>();
            screen.EnsureBuilt();
            screen.HideImmediate();
            return screen;
        }

        /// <summary>
        /// Creates a hidden, still empty screen of type <typeparamref name="T"/> under <paramref name="parent"/> (cheap).
        /// Its content is built later: step by step by <c>UIRoot.BuildQueue.Enqueue(screen)</c>, or all at once by the
        /// first <see cref="Show"/> / <see cref="EnsureBuilt"/> — whichever comes first. The content is built while the
        /// screen is inactive, so widget <c>Awake</c>/<c>OnEnable</c> run when it is first activated.
        /// </summary>
        public static T CreateDeferred<T>(Transform parent, string name = null) where T : UIScreen
        {
            RectTransform rt = UIFactory.CreateFill(parent, name ?? typeof(T).Name);
            rt.gameObject.AddComponent<CanvasGroup>();
            T screen = rt.gameObject.AddComponent<T>();
            screen.HideImmediate();
            return screen;
        }

        /// <summary>Builds the screen's content (called once, before the first show). Ignored when <see cref="BuildSteps"/> is overridden.</summary>
        protected virtual void Build()
        {
        }

        /// <summary>
        /// The content build as a sequence of steps: each <c>yield return null</c> marks a point where the
        /// <see cref="UIBuildQueue"/> may continue in a later frame (a built screen is never shown half-done: showing it, or
        /// <see cref="EnsureBuilt"/>, runs the remaining steps at once). The default runs <see cref="Build"/> in one step.
        /// Keep every step self-contained: fields assigned in later steps are still null until the build completes.
        /// </summary>
        protected virtual IEnumerator BuildSteps()
        {
            Build();
            yield break;
        }

        /// <summary>Called when the screen starts showing (bind data here).</summary>
        protected virtual void OnShow()
        {
        }

        /// <summary>Called when the entrance animation has finished (start staggered lists, focus, etc.).</summary>
        protected virtual void OnShown()
        {
        }

        /// <summary>Called when the screen starts hiding.</summary>
        protected virtual void OnHide()
        {
        }

        /// <summary>Called when the screen is fully hidden (and deactivated).</summary>
        protected virtual void OnHidden()
        {
        }

        /// <summary>Back/Escape/gamepad-B while this is the top screen. Return true if handled (the stack won't pop).</summary>
        public virtual bool OnBack()
        {
            return false;
        }

        public void Show(bool instant = false)
        {
            EnsureBuilt();
            _everShown = true;
            bool wasActive = gameObject.activeSelf;
            gameObject.SetActive(true);
            _visible = true;
            _group.blocksRaycasts = true;
            _group.interactable = true;
            UITween.Kill(_group);
            UITween.Kill(_rect);
            SafeCall(OnShow);

            if (instant || Transition == ScreenTransition.None || !Application.isPlaying)
            {
                ResetPose();
                _group.alpha = 1f;
                _transitioning = false;
                SafeCall(OnShown);
                return;
            }

            if (!wasActive)
            {
                _group.alpha = 0f;
                ApplyOffsetPose(1f);
            }

            float d = Mathf.Max(0.01f, TransitionDuration);
            _transitioning = true;
            UITween.Fade(_group, 1f, d * 0.8f).SetEase(UIEase.OutCubic).OnComplete(_shownHandler);
            switch (Transition)
            {
                case ScreenTransition.Zoom:
                    UITween.Scale(_rect, Vector3.one, d).SetEase(UIEase.OutCubic);
                    UITween.Move(_rect, Vector2.zero, d).SetEase(UIEase.OutCubic);
                    break;
                case ScreenTransition.SlideUp:
                case ScreenTransition.SlideLeft:
                    UITween.Move(_rect, Vector2.zero, d).SetEase(UIEase.OutQuart);
                    UITween.Scale(_rect, Vector3.one, d).SetEase(UIEase.OutCubic);
                    break;
                default:
                    ResetPose();
                    break;
            }
        }

        public void Hide(bool instant = false)
        {
            EnsureInit();
            _visible = false;
            _group.blocksRaycasts = false;
            _group.interactable = false;
            UITween.Kill(_group);
            UITween.Kill(_rect);
            if (!gameObject.activeSelf)
            {
                _transitioning = false;
                return;
            }

            SafeCall(OnHide);
            if (instant || Transition == ScreenTransition.None || !Application.isPlaying)
            {
                HideImmediate();
                SafeCall(OnHidden);
                return;
            }

            float d = Mathf.Max(0.01f, TransitionDuration * 0.75f);
            _transitioning = true;
            UITween.Fade(_group, 0f, d).SetEase(UIEase.InQuad).OnComplete(_hiddenHandler);
            switch (Transition)
            {
                case ScreenTransition.Zoom:
                    UITween.Scale(_rect, new Vector3(1.03f, 1.03f, 1f), d).SetEase(UIEase.InQuad);
                    break;
                case ScreenTransition.SlideUp:
                    UITween.Move(_rect, new Vector2(0f, -80f), d).SetEase(UIEase.InQuad);
                    break;
                case ScreenTransition.SlideLeft:
                    UITween.Move(_rect, new Vector2(-120f, 0f), d).SetEase(UIEase.InQuad);
                    break;
            }
        }

        /// <summary>
        /// Builds whatever is still missing of the content, right now (no-op when built, or when called from inside the
        /// build itself). <see cref="Show"/> calls it, so a deferred screen shown early is simply built on demand.
        /// </summary>
        public void EnsureBuilt()
        {
            EnsureInit();
            while (!_built && !_stepping)
            {
                BuildNextStep();
            }
        }

        /// <summary>
        /// Runs the next build step (the <see cref="UIBuildQueue"/> calls this under its frame budget). Returns true once
        /// the content is complete. An exception ends the build (logged); the screen then shows what was built, as before.
        /// </summary>
        internal bool BuildNextStep()
        {
            if (_built)
            {
                return true;
            }

            if (_stepping)
            {
                // Re-entered from inside a build step (e.g. Build calling Show): let the running step finish.
                return false;
            }

            EnsureInit();
            _stepping = true;
            bool more = false;
            try
            {
                if (!_buildStarted)
                {
                    _buildStarted = true;
                    _buildSteps = BuildSteps();
                }

                more = _buildSteps != null && _buildSteps.MoveNext();
            }
            catch (Exception e)
            {
                Debug.LogException(e, this);
                more = false;
            }
            finally
            {
                _stepping = false;
            }

            if (!more)
            {
                _built = true;
                (_buildSteps as IDisposable)?.Dispose();
                _buildSteps = null;
            }

            return _built;
        }

        /// <summary>
        /// Pays a never-shown screen's one-time activation costs ahead of its first show: activates it for a moment —
        /// invisible (alpha 0, no raycasts) — so every widget runs <c>Awake</c>/<c>OnEnable</c>, runs its layout once (font
        /// atlas glyphs, cached text measurements, sprites for pill radii), and deactivates it again in the same call (no
        /// frame renders it, no <c>Update</c> runs, no meshes are built). No-op for screens that are visible, active,
        /// unbuilt or were already shown.
        /// </summary>
        internal void Warm()
        {
            if (!_built || _everShown || _visible || !Application.isPlaying || gameObject.activeSelf)
            {
                return;
            }

            Transform parent = transform.parent;
            if (parent == null || !parent.gameObject.activeInHierarchy)
            {
                return;
            }

            EnsureInit();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;
            _group.interactable = false;
            try
            {
                gameObject.SetActive(true);
                WarmLayout();
            }
            catch (Exception e)
            {
                Debug.LogException(e, this);
            }
            finally
            {
                if (!_visible)
                {
                    gameObject.SetActive(false);
                }
            }
        }

        // Rebuilds the layout of every layout root in the (now active) screen — not the graphics, whose meshes the next
        // activation rebuilds anyway.
        private void WarmLayout()
        {
            ILayoutController[] controllers = GetComponentsInChildren<ILayoutController>(false);
            var done = new HashSet<Transform>();
            for (int i = 0; i < controllers.Length; i++)
            {
                var behaviour = controllers[i] as Behaviour;
                if (behaviour == null || !behaviour.isActiveAndEnabled)
                {
                    continue;
                }

                var rt = behaviour.transform as RectTransform;
                if (rt == null || !done.Add(rt) || IsDrivenByParentGroup(rt))
                {
                    continue;
                }

                LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
            }
        }

        private static bool IsDrivenByParentGroup(RectTransform rt)
        {
            Transform parent = rt.parent;
            var group = parent != null ? parent.GetComponent<ILayoutGroup>() as Behaviour : null;
            return group != null && group.isActiveAndEnabled;
        }

        private void EnsureInit()
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            _rect = (RectTransform)transform;
            _group = GetComponent<CanvasGroup>();
            if (_group == null)
            {
                _group = gameObject.AddComponent<CanvasGroup>();
            }

            _shownHandler = HandleShown;
            _hiddenHandler = HandleHidden;
        }

        private void HideImmediate()
        {
            EnsureInit();
            _visible = false;
            _transitioning = false;
            _group.alpha = 0f;
            _group.blocksRaycasts = false;
            _group.interactable = false;
            ResetPose();
            gameObject.SetActive(false);
        }

        private void HandleShown()
        {
            _transitioning = false;
            SafeCall(OnShown);
        }

        private void HandleHidden()
        {
            _transitioning = false;
            if (!_visible)
            {
                ResetPose();
                gameObject.SetActive(false);
                SafeCall(OnHidden);
            }
        }

        private void ResetPose()
        {
            _rect.localScale = Vector3.one;
            _rect.anchoredPosition = Vector2.zero;
        }

        private void ApplyOffsetPose(float amount)
        {
            switch (Transition)
            {
                case ScreenTransition.Zoom:
                    _rect.localScale = new Vector3(1f - 0.05f * amount, 1f - 0.05f * amount, 1f);
                    _rect.anchoredPosition = Vector2.zero;
                    break;
                case ScreenTransition.SlideUp:
                    _rect.localScale = Vector3.one;
                    _rect.anchoredPosition = new Vector2(0f, -120f * amount);
                    break;
                case ScreenTransition.SlideLeft:
                    _rect.localScale = Vector3.one;
                    _rect.anchoredPosition = new Vector2(160f * amount, 0f);
                    break;
                default:
                    ResetPose();
                    break;
            }
        }

        private void SafeCall(Action a)
        {
            try
            {
                a();
            }
            catch (Exception e)
            {
                Debug.LogException(e, this);
            }
        }
    }
}
