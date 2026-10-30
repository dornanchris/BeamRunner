using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BeamRunner.UI
{
    /// <summary>What a <see cref="Tween"/> animates.</summary>
    public enum TweenKind
    {
        Value = 0,
        Delay,
        Fade,
        Scale,
        Move,
        Color,
        Alpha,
        RendererAlpha,
        Fill,
        Size,
        Rotate,
        Punch,
        Shake,
    }

    /// <summary>
    /// A pooled tween. The instance returned by a <see cref="UITween"/> helper is only valid until the tween completes
    /// or is killed (it is then recycled): configure it immediately (fluent calls) and keep its <see cref="Id"/>, not the
    /// object, if you need to refer to it later (<see cref="UITween.KillId"/>, <see cref="UITween.IsAlive"/>).
    /// </summary>
    public sealed class Tween
    {
        internal int IdValue;
        internal TweenKind Kind;
        internal Object Target;
        internal bool HasTarget;
        internal Vector4 From;
        internal Vector4 To;
        internal bool FromSet;
        internal float Duration;
        internal float DelayLeft;
        internal float Elapsed;
        internal UIEase EaseType;
        internal Action<float> OnValue;
        internal Action Completed;
        internal int Loops;
        internal bool Yoyo;
        internal bool Forward;
        internal bool Alive;
        internal bool Started;

        /// <summary>Unique id of this use of the tween (ids are never reused).</summary>
        public int Id
        {
            get { return IdValue; }
        }

        public bool IsAlive
        {
            get { return Alive; }
        }

        public Tween SetEase(UIEase ease)
        {
            EaseType = ease;
            return this;
        }

        public Tween SetDelay(float seconds)
        {
            DelayLeft = seconds > 0f ? seconds : 0f;
            return this;
        }

        /// <summary>Called once when the tween finishes normally (not when killed, nor when its target was destroyed).</summary>
        public Tween OnComplete(Action callback)
        {
            Completed = callback;
            return this;
        }

        /// <summary>Repeats the tween; <paramref name="loops"/> = -1 repeats forever. Yoyo plays alternate cycles backwards.</summary>
        public Tween SetLoops(int loops, bool yoyo = false)
        {
            Loops = loops;
            Yoyo = yoyo;
            return this;
        }

        /// <summary>Binds a Value/Delay tween to an object: it dies with the object and <see cref="UITween.Kill"/> finds it.</summary>
        public Tween SetTarget(Object owner)
        {
            Target = owner;
            HasTarget = owner != null;
            return this;
        }

        public void Kill(bool complete = false)
        {
            UITween.KillTween(this, complete);
        }

        internal void Reset()
        {
            IdValue = 0;
            Kind = TweenKind.Value;
            Target = null;
            HasTarget = false;
            From = Vector4.zero;
            To = Vector4.zero;
            FromSet = false;
            Duration = 0f;
            DelayLeft = 0f;
            Elapsed = 0f;
            EaseType = UIEase.OutCubic;
            OnValue = null;
            Completed = null;
            Loops = 0;
            Yoyo = false;
            Forward = true;
            Alive = false;
            Started = false;
        }
    }

    /// <summary>
    /// Lightweight tween engine for UI. Runs on a hidden DontDestroyOnLoad runner using <b>unscaled time</b>, so menus
    /// animate while the game is paused or in slow motion. Tweens are pooled (no steady-state allocations), starting a
    /// tween on a target overwrites a running tween of the same channel on that target, and tweens whose target has
    /// been destroyed stop silently.
    /// </summary>
    public static class UITween
    {
        private static readonly List<Tween> Active = new List<Tween>(128);
        private static readonly List<Tween> Pending = new List<Tween>(32);
        private static readonly Stack<Tween> Pool = new Stack<Tween>(128);
        private static int _nextId;
        private static UITweenRunner _runner;
        private static bool _quitting;
        private static bool _hooked;

        /// <summary>Number of live tweens.</summary>
        public static int Count
        {
            get { return Active.Count + Pending.Count; }
        }

        // ------------------------------------------------------------------ helpers

        public static Tween Fade(CanvasGroup group, float to, float duration)
        {
            Tween t = Create(TweenKind.Fade, group, duration);
            t.To.x = to;
            return t;
        }

        /// <summary>Fade that starts from <paramref name="from"/> (applied immediately, even during a delay).</summary>
        public static Tween Fade(CanvasGroup group, float from, float to, float duration)
        {
            if (group != null)
            {
                group.alpha = from;
            }

            Tween t = Fade(group, to, duration);
            t.From.x = from;
            t.FromSet = true;
            return t;
        }

        public static Tween Scale(Transform target, Vector3 to, float duration)
        {
            Tween t = Create(TweenKind.Scale, target, duration);
            t.To = to;
            return t;
        }

        public static Tween Scale(Transform target, float to, float duration)
        {
            return Scale(target, new Vector3(to, to, 1f), duration);
        }

        public static Tween Scale(Transform target, Vector3 from, Vector3 to, float duration)
        {
            if (target != null)
            {
                target.localScale = from;
            }

            Tween t = Scale(target, to, duration);
            t.From = from;
            t.FromSet = true;
            return t;
        }

        public static Tween Scale(Transform target, float from, float to, float duration)
        {
            return Scale(target, new Vector3(from, from, 1f), new Vector3(to, to, 1f), duration);
        }

        public static Tween Move(RectTransform target, Vector2 to, float duration)
        {
            Tween t = Create(TweenKind.Move, target, duration);
            t.To = to;
            return t;
        }

        public static Tween Move(RectTransform target, Vector2 from, Vector2 to, float duration)
        {
            if (target != null)
            {
                target.anchoredPosition = from;
            }

            Tween t = Move(target, to, duration);
            t.From = from;
            t.FromSet = true;
            return t;
        }

        public static Tween Color(Graphic target, UnityEngine.Color to, float duration)
        {
            Tween t = Create(TweenKind.Color, target, duration);
            t.To = to;
            return t;
        }

        public static Tween Color(Graphic target, UnityEngine.Color from, UnityEngine.Color to, float duration)
        {
            if (target != null)
            {
                target.color = from;
            }

            Tween t = Color(target, to, duration);
            t.From = from;
            t.FromSet = true;
            return t;
        }

        /// <summary>Animates only the alpha of <see cref="Graphic.color"/>.</summary>
        public static Tween Alpha(Graphic target, float to, float duration)
        {
            Tween t = Create(TweenKind.Alpha, target, duration);
            t.To.x = to;
            return t;
        }

        /// <summary>Animates the CanvasRenderer alpha (no mesh rebuild; ideal for glows and pulses).</summary>
        public static Tween RendererAlpha(Graphic target, float to, float duration)
        {
            Tween t = Create(TweenKind.RendererAlpha, target, duration);
            t.To.x = to;
            return t;
        }

        public static Tween Fill(Image target, float to, float duration)
        {
            Tween t = Create(TweenKind.Fill, target, duration);
            t.To.x = to;
            return t;
        }

        public static Tween Size(RectTransform target, Vector2 to, float duration)
        {
            Tween t = Create(TweenKind.Size, target, duration);
            t.To = to;
            return t;
        }

        /// <summary>Rotates around Z to <paramref name="toDegrees"/> (no wrapping).</summary>
        public static Tween Rotate(Transform target, float toDegrees, float duration)
        {
            Tween t = Create(TweenKind.Rotate, target, duration);
            t.To.x = toDegrees;
            return t;
        }

        /// <summary>Scale punch around <see cref="Vector3.one"/>: grows by <paramref name="amount"/>, springs back.</summary>
        public static Tween Punch(Transform target, float amount = 0.15f, float duration = 0.4f)
        {
            return Punch(target, amount, duration, Vector3.one);
        }

        /// <summary>Scale punch around an explicit resting scale.</summary>
        public static Tween Punch(Transform target, float amount, float duration, Vector3 restScale)
        {
            Tween t = Create(TweenKind.Punch, target, duration);
            t.From = restScale;
            t.FromSet = true;
            t.To.x = amount;
            t.EaseType = UIEase.Linear;
            return t;
        }

        /// <summary>Random positional shake that decays to the start position.</summary>
        public static Tween Shake(RectTransform target, float strength = 14f, float duration = 0.35f)
        {
            Tween t = Create(TweenKind.Shake, target, duration);
            t.To.x = UITheme.ReducedMotion ? strength * 0.3f : strength;
            t.EaseType = UIEase.Linear;
            return t;
        }

        /// <summary>Tweens a float and reports it every frame.</summary>
        public static Tween Value(float from, float to, float duration, Action<float> onUpdate)
        {
            Tween t = Create(TweenKind.Value, null, duration);
            t.From.x = from;
            t.To.x = to;
            t.FromSet = true;
            t.OnValue = onUpdate;
            return t;
        }

        /// <summary>Value tween owned by <paramref name="owner"/> (stops when the owner is destroyed).</summary>
        public static Tween Value(Object owner, float from, float to, float duration, Action<float> onUpdate)
        {
            return Value(from, to, duration, onUpdate).SetTarget(owner);
        }

        /// <summary>Calls <paramref name="callback"/> after <paramref name="seconds"/> of unscaled time.</summary>
        public static Tween Delay(float seconds, Action callback)
        {
            Tween t = Create(TweenKind.Delay, null, seconds);
            t.Completed = callback;
            return t;
        }

        public static Tween Delay(Object owner, float seconds, Action callback)
        {
            return Delay(seconds, callback).SetTarget(owner);
        }

        // ------------------------------------------------------------------ control

        /// <summary>Stops every tween on <paramref name="target"/> (optionally jumping to the end and completing).</summary>
        public static void Kill(Object target, bool complete = false)
        {
            if (ReferenceEquals(target, null))
            {
                return;
            }

            KillIn(Active, target, complete);
            KillIn(Pending, target, complete);
        }

        public static void KillId(int id, bool complete = false)
        {
            if (id == 0)
            {
                return;
            }

            Tween t = Find(id);
            if (t != null)
            {
                KillTween(t, complete);
            }
        }

        public static bool IsAlive(int id)
        {
            return id != 0 && Find(id) != null;
        }

        public static bool IsTweening(Object target)
        {
            if (ReferenceEquals(target, null))
            {
                return false;
            }

            for (int i = 0; i < Active.Count; i++)
            {
                Tween t = Active[i];
                if (t.Alive && ReferenceEquals(t.Target, target))
                {
                    return true;
                }
            }

            for (int i = 0; i < Pending.Count; i++)
            {
                Tween t = Pending[i];
                if (t.Alive && ReferenceEquals(t.Target, target))
                {
                    return true;
                }
            }

            return false;
        }

        public static void KillAll()
        {
            for (int i = 0; i < Active.Count; i++)
            {
                Active[i].Alive = false;
            }

            for (int i = 0; i < Pending.Count; i++)
            {
                Pending[i].Alive = false;
            }
        }

        internal static void KillTween(Tween t, bool complete)
        {
            if (t == null || !t.Alive)
            {
                return;
            }

            t.Alive = false;
            if (!complete)
            {
                return;
            }

            if (!t.HasTarget || t.Target != null)
            {
                if (!t.Started && !t.FromSet)
                {
                    CaptureFrom(t);
                }

                // Punch/Shake use the raw progress (1 = back at rest); everything else jumps to its end value.
                Apply(t, 1f, 1f);
                Invoke(t.Completed);
            }
        }

        // ------------------------------------------------------------------ engine

        private static Tween Create(TweenKind kind, Object target, float duration)
        {
            EnsureRunner();
            if (!ReferenceEquals(target, null) && kind != TweenKind.Value && kind != TweenKind.Delay)
            {
                KillChannel(target, Channel(kind));
            }

            Tween t = Pool.Count > 0 ? Pool.Pop() : new Tween();
            t.Reset();
            t.IdValue = ++_nextId;
            t.Kind = kind;
            t.Target = target;
            t.HasTarget = !ReferenceEquals(target, null);
            t.Duration = duration > 0f ? duration : 0f;
            t.Alive = true;
            Pending.Add(t);
            return t;
        }

        internal static void Tick(float dt)
        {
            if (Pending.Count > 0)
            {
                for (int i = 0; i < Pending.Count; i++)
                {
                    Active.Add(Pending[i]);
                }

                Pending.Clear();
            }

            int count = Active.Count;
            for (int i = 0; i < count; i++)
            {
                Tween t = Active[i];
                if (t.Alive)
                {
                    Step(t, dt);
                }
            }

            int write = 0;
            for (int i = 0; i < Active.Count; i++)
            {
                Tween t = Active[i];
                if (t.Alive)
                {
                    Active[write++] = t;
                }
                else
                {
                    t.Reset();
                    Pool.Push(t);
                }
            }

            if (write < Active.Count)
            {
                Active.RemoveRange(write, Active.Count - write);
            }
        }

        private static void Step(Tween t, float dt)
        {
            if (t.HasTarget && t.Target == null)
            {
                t.Alive = false;
                return;
            }

            if (t.DelayLeft > 0f)
            {
                t.DelayLeft -= dt;
                if (t.DelayLeft > 0f)
                {
                    return;
                }

                dt = -t.DelayLeft;
                t.DelayLeft = 0f;
            }

            if (!t.Started)
            {
                t.Started = true;
                if (!t.FromSet)
                {
                    CaptureFrom(t);
                }
            }

            t.Elapsed += dt;
            float p = t.Duration > 0f ? t.Elapsed / t.Duration : 1f;
            bool finished = false;
            if (p >= 1f)
            {
                p = 1f;
                if (t.Loops != 0)
                {
                    if (t.Loops > 0)
                    {
                        t.Loops--;
                    }

                    ApplyProgress(t, 1f);
                    t.Elapsed = t.Duration > 0f ? t.Elapsed % t.Duration : 0f;
                    if (t.Yoyo)
                    {
                        t.Forward = !t.Forward;
                    }

                    return;
                }

                finished = true;
            }

            ApplyProgress(t, p);
            if (finished)
            {
                t.Alive = false;
                Invoke(t.Completed);
            }
        }

        private static void ApplyProgress(Tween t, float p)
        {
            float dirP = t.Forward ? p : 1f - p;
            float e = UIEaseFunctions.Evaluate(t.EaseType, dirP);
            Apply(t, e, dirP);
        }

        private static void CaptureFrom(Tween t)
        {
            switch (t.Kind)
            {
                case TweenKind.Fade:
                    t.From.x = ((CanvasGroup)t.Target).alpha;
                    break;
                case TweenKind.Scale:
                case TweenKind.Punch:
                    t.From = ((Transform)t.Target).localScale;
                    break;
                case TweenKind.Move:
                case TweenKind.Shake:
                    t.From = ((RectTransform)t.Target).anchoredPosition;
                    break;
                case TweenKind.Color:
                    t.From = ((Graphic)t.Target).color;
                    break;
                case TweenKind.Alpha:
                    t.From.x = ((Graphic)t.Target).color.a;
                    break;
                case TweenKind.RendererAlpha:
                    t.From.x = ((Graphic)t.Target).canvasRenderer.GetAlpha();
                    break;
                case TweenKind.Fill:
                    t.From.x = ((Image)t.Target).fillAmount;
                    break;
                case TweenKind.Size:
                    t.From = ((RectTransform)t.Target).sizeDelta;
                    break;
                case TweenKind.Rotate:
                    t.From.x = ((Transform)t.Target).localEulerAngles.z;
                    if (t.From.x > 180f)
                    {
                        t.From.x -= 360f;
                    }

                    break;
            }

            t.FromSet = true;
        }

        private static void Apply(Tween t, float e, float rawP)
        {
            switch (t.Kind)
            {
                case TweenKind.Value:
                    if (t.OnValue != null)
                    {
                        try
                        {
                            t.OnValue(Mathf.LerpUnclamped(t.From.x, t.To.x, e));
                        }
                        catch (Exception ex)
                        {
                            t.Alive = false;
                            Debug.LogException(ex);
                        }
                    }

                    break;
                case TweenKind.Delay:
                    break;
                case TweenKind.Fade:
                    ((CanvasGroup)t.Target).alpha = Mathf.Clamp01(Mathf.LerpUnclamped(t.From.x, t.To.x, e));
                    break;
                case TweenKind.Scale:
                    ((Transform)t.Target).localScale = Vector3.LerpUnclamped(t.From, t.To, e);
                    break;
                case TweenKind.Move:
                    ((RectTransform)t.Target).anchoredPosition = Vector2.LerpUnclamped(t.From, t.To, e);
                    break;
                case TweenKind.Color:
                    ((Graphic)t.Target).color = UnityEngine.Color.Lerp(t.From, t.To, e);
                    break;
                case TweenKind.Alpha:
                {
                    Graphic g = (Graphic)t.Target;
                    UnityEngine.Color c = g.color;
                    c.a = Mathf.Clamp01(Mathf.LerpUnclamped(t.From.x, t.To.x, e));
                    g.color = c;
                    break;
                }

                case TweenKind.RendererAlpha:
                    ((Graphic)t.Target).canvasRenderer.SetAlpha(Mathf.Clamp01(Mathf.LerpUnclamped(t.From.x, t.To.x, e)));
                    break;
                case TweenKind.Fill:
                    ((Image)t.Target).fillAmount = Mathf.Clamp01(Mathf.LerpUnclamped(t.From.x, t.To.x, e));
                    break;
                case TweenKind.Size:
                    ((RectTransform)t.Target).sizeDelta = Vector2.LerpUnclamped(t.From, t.To, e);
                    break;
                case TweenKind.Rotate:
                    ((Transform)t.Target).localEulerAngles = new Vector3(0f, 0f, Mathf.LerpUnclamped(t.From.x, t.To.x, e));
                    break;
                case TweenKind.Punch:
                {
                    float k = 1f + t.To.x * UIEaseFunctions.Punch(rawP);
                    ((Transform)t.Target).localScale = new Vector3(t.From.x * k, t.From.y * k, t.From.z);
                    break;
                }

                case TweenKind.Shake:
                {
                    Vector2 basePos = new Vector2(t.From.x, t.From.y);
                    if (rawP >= 1f)
                    {
                        ((RectTransform)t.Target).anchoredPosition = basePos;
                    }
                    else
                    {
                        float s = t.To.x * (1f - rawP);
                        ((RectTransform)t.Target).anchoredPosition = basePos + UnityEngine.Random.insideUnitCircle * s;
                    }

                    break;
                }
            }
        }

        private static int Channel(TweenKind kind)
        {
            switch (kind)
            {
                case TweenKind.Punch:
                    return (int)TweenKind.Scale;
                case TweenKind.Shake:
                    return (int)TweenKind.Move;
                case TweenKind.Alpha:
                    return (int)TweenKind.Color;
                default:
                    return (int)kind;
            }
        }

        private static void KillChannel(Object target, int channel)
        {
            for (int i = 0; i < Active.Count; i++)
            {
                Tween t = Active[i];
                if (t.Alive && ReferenceEquals(t.Target, target) && t.Kind != TweenKind.Value && t.Kind != TweenKind.Delay && Channel(t.Kind) == channel)
                {
                    t.Alive = false;
                }
            }

            for (int i = 0; i < Pending.Count; i++)
            {
                Tween t = Pending[i];
                if (t.Alive && ReferenceEquals(t.Target, target) && t.Kind != TweenKind.Value && t.Kind != TweenKind.Delay && Channel(t.Kind) == channel)
                {
                    t.Alive = false;
                }
            }
        }

        private static void KillIn(List<Tween> list, Object target, bool complete)
        {
            for (int i = 0; i < list.Count; i++)
            {
                Tween t = list[i];
                if (t.Alive && ReferenceEquals(t.Target, target))
                {
                    KillTween(t, complete);
                }
            }
        }

        private static Tween Find(int id)
        {
            for (int i = 0; i < Active.Count; i++)
            {
                if (Active[i].IdValue == id && Active[i].Alive)
                {
                    return Active[i];
                }
            }

            for (int i = 0; i < Pending.Count; i++)
            {
                if (Pending[i].IdValue == id && Pending[i].Alive)
                {
                    return Pending[i];
                }
            }

            return null;
        }

        private static void Invoke(Action a)
        {
            if (a == null)
            {
                return;
            }

            try
            {
                a();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private static void EnsureRunner()
        {
            if (_runner != null || _quitting || !Application.isPlaying)
            {
                return;
            }

            if (!_hooked)
            {
                _hooked = true;
                Application.quitting += OnQuitting;
            }

            var go = new GameObject("[FieldUI.Tween]");
            go.hideFlags = HideFlags.HideInHierarchy;
            Object.DontDestroyOnLoad(go);
            _runner = go.AddComponent<UITweenRunner>();
        }

        private static void OnQuitting()
        {
            _quitting = true;
        }

        internal static void RunnerDestroyed(UITweenRunner runner)
        {
            if (_runner == runner)
            {
                _runner = null;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Active.Clear();
            Pending.Clear();
            Pool.Clear();
            _runner = null;
            _quitting = false;
            _hooked = false;
            _nextId = 0;
        }
    }

    /// <summary>Drives <see cref="UITween"/> with unscaled time. Created automatically.</summary>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public sealed class UITweenRunner : MonoBehaviour
    {
        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (dt > 0.1f)
            {
                dt = 0.1f;
            }

            UITween.Tick(dt);
        }

        private void OnDestroy()
        {
            UITween.RunnerDestroyed(this);
        }
    }
}
