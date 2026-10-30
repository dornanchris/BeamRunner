using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace BeamRunner.Services
{
    /// <summary>
    /// Hidden, persistent pump for the services layer: marshals callbacks onto the Unity main thread,
    /// runs one-shot timers and ticks watchdogs. All state is static, so if the component is ever destroyed
    /// (for example together with its host object) a fresh one is created on demand and nothing queued is lost.
    /// </summary>
    /// <remarks>
    /// Two clocks are available: real time (<see cref="Time.realtimeSinceStartup"/>, keeps running while the app
    /// is suspended, used for retry back-off) and <see cref="ActiveTime"/> (advances only while the player loop
    /// runs, so a full-screen ad that suspends the app never trips a watchdog).
    /// </remarks>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public sealed class ServiceRunner : MonoBehaviour
    {
        private const float MaxActiveStep = 0.25f;
        private const string RunnerObjectName = "[BeamRunner.Services]";

        private struct Timer
        {
            public int Id;
            public double Due;
            public bool UsesActiveTime;
            public Action Action;
        }

        private static readonly object s_queueLock = new object();
        private static readonly List<Action> s_queue = new List<Action>();
        private static readonly List<Action> s_drain = new List<Action>();
        private static readonly List<Timer> s_timers = new List<Timer>();
        private static readonly List<Timer> s_dueTimers = new List<Timer>();

        private static ServiceRunner s_instance;
        private static int s_mainThreadId = -1;
        private static int s_nextTimerId;
        private static double s_activeTime;
        private static bool s_quitting;

        /// <summary>Raised once per frame on the main thread, after queued callbacks and timers ran.</summary>
        public static event Action Ticked;

        /// <summary>Forwarded <c>OnApplicationFocus</c>.</summary>
        public static event Action<bool> FocusChanged;

        /// <summary>Forwarded <c>OnApplicationPause</c>.</summary>
        public static event Action<bool> PauseChanged;

        /// <summary>Seconds of unscaled time during which the player loop was actually running.</summary>
        public static double ActiveTime => s_activeTime;

        /// <summary>True when called from the Unity main thread.</summary>
        public static bool IsMainThread =>
            s_mainThreadId < 0 || Thread.CurrentThread.ManagedThreadId == s_mainThreadId;

        /// <summary>True while the application is shutting down (no new objects are created then).</summary>
        public static bool IsQuitting => s_quitting;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            // Supports "Enter Play Mode Options" with domain reload disabled.
            lock (s_queueLock)
            {
                s_queue.Clear();
            }

            s_drain.Clear();
            s_timers.Clear();
            s_dueTimers.Clear();
            s_instance = null;
            s_nextTimerId = 0;
            s_activeTime = 0.0;
            s_quitting = false;
            Ticked = null;
            FocusChanged = null;
            PauseChanged = null;
            s_mainThreadId = Thread.CurrentThread.ManagedThreadId;
            Application.quitting -= OnQuitting;
            Application.quitting += OnQuitting;
        }

        private static void OnQuitting()
        {
            s_quitting = true;
        }

        /// <summary>
        /// Returns the live runner, creating it if needed. Main thread only. When <paramref name="host"/> is an
        /// active object the runner is attached to it (it should live for the whole session, like the bootstrap
        /// object); otherwise a hidden <c>DontDestroyOnLoad</c> object is created.
        /// </summary>
        public static ServiceRunner Ensure(MonoBehaviour host = null)
        {
            if (s_instance != null)
            {
                return s_instance;
            }

            if (s_quitting || !IsMainThread)
            {
                return null;
            }

            if (s_mainThreadId < 0)
            {
                s_mainThreadId = Thread.CurrentThread.ManagedThreadId;
            }

            try
            {
                GameObject go;
                if (host != null && host.gameObject.activeInHierarchy)
                {
                    go = host.gameObject;
                    ServiceRunner existing = go.GetComponent<ServiceRunner>();
                    if (existing != null)
                    {
                        s_instance = existing;
                        return existing;
                    }
                }
                else
                {
                    go = new GameObject(RunnerObjectName);
                    go.hideFlags = HideFlags.HideInHierarchy;
                    DontDestroyOnLoad(go);
                }

                s_instance = go.AddComponent<ServiceRunner>();
            }
            catch (Exception e)
            {
                ServiceLog.Exception("ServiceRunner could not be created", e);
                s_instance = null;
            }

            return s_instance;
        }

        /// <summary>
        /// Queues <paramref name="action"/> to run on the main thread during the next frame. Thread-safe; never
        /// runs the action synchronously, so it is safe to call from inside SDK callbacks.
        /// </summary>
        public static void Post(Action action)
        {
            if (action == null)
            {
                return;
            }

            lock (s_queueLock)
            {
                s_queue.Add(action);
            }

            if (s_instance == null && IsMainThread)
            {
                Ensure();
            }
        }

        /// <summary>
        /// Runs <paramref name="action"/> on the main thread after <paramref name="seconds"/>. Main thread only.
        /// With <paramref name="useActiveTime"/> the delay only counts time during which the app was running.
        /// Returns an id for <see cref="Cancel"/>.
        /// </summary>
        public static int Delay(float seconds, Action action, bool useActiveTime = false)
        {
            if (action == null)
            {
                return 0;
            }

            Ensure();
            double now = useActiveTime ? s_activeTime : Time.realtimeSinceStartup;
            int id = ++s_nextTimerId;
            if (id <= 0)
            {
                s_nextTimerId = 1;
                id = 1;
            }

            s_timers.Add(new Timer
            {
                Id = id,
                Due = now + Math.Max(0f, seconds),
                UsesActiveTime = useActiveTime,
                Action = action,
            });
            return id;
        }

        /// <summary>Cancels a pending <see cref="Delay"/>. Unknown or already-run ids are ignored.</summary>
        public static void Cancel(int timerId)
        {
            if (timerId <= 0)
            {
                return;
            }

            for (int i = s_timers.Count - 1; i >= 0; i--)
            {
                if (s_timers[i].Id == timerId)
                {
                    s_timers.RemoveAt(i);
                    return;
                }
            }
        }

        private void Awake()
        {
            if (s_instance != null && s_instance != this)
            {
                Destroy(this);
                return;
            }

            s_instance = this;
            if (s_mainThreadId < 0)
            {
                s_mainThreadId = Thread.CurrentThread.ManagedThreadId;
            }
        }

        private void Update()
        {
            if (s_instance != this)
            {
                return;
            }

            float step = Time.unscaledDeltaTime;
            if (step > MaxActiveStep)
            {
                step = MaxActiveStep;
            }

            if (step > 0f)
            {
                s_activeTime += step;
            }

            DrainQueue();
            RunTimers();

            Action ticked = Ticked;
            if (ticked != null)
            {
                try
                {
                    ticked();
                }
                catch (Exception e)
                {
                    ServiceLog.Exception("Service tick failed", e);
                }
            }
        }

        private static void DrainQueue()
        {
            lock (s_queueLock)
            {
                if (s_queue.Count == 0)
                {
                    return;
                }

                s_drain.AddRange(s_queue);
                s_queue.Clear();
            }

            for (int i = 0; i < s_drain.Count; i++)
            {
                try
                {
                    s_drain[i]();
                }
                catch (Exception e)
                {
                    ServiceLog.Exception("Service callback failed", e);
                }
            }

            s_drain.Clear();
        }

        private static void RunTimers()
        {
            if (s_timers.Count == 0)
            {
                return;
            }

            double realNow = Time.realtimeSinceStartup;
            for (int i = s_timers.Count - 1; i >= 0; i--)
            {
                Timer timer = s_timers[i];
                double now = timer.UsesActiveTime ? s_activeTime : realNow;
                if (now >= timer.Due)
                {
                    s_dueTimers.Add(timer);
                    s_timers.RemoveAt(i);
                }
            }

            // Collected newest-first; run in scheduling order.
            for (int i = s_dueTimers.Count - 1; i >= 0; i--)
            {
                try
                {
                    s_dueTimers[i].Action();
                }
                catch (Exception e)
                {
                    ServiceLog.Exception("Service timer failed", e);
                }
            }

            s_dueTimers.Clear();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (s_instance != this)
            {
                return;
            }

            Action<bool> handler = FocusChanged;
            if (handler == null)
            {
                return;
            }

            try
            {
                handler(hasFocus);
            }
            catch (Exception e)
            {
                ServiceLog.Exception("Focus handler failed", e);
            }
        }

        private void OnApplicationPause(bool paused)
        {
            if (s_instance != this)
            {
                return;
            }

            Action<bool> handler = PauseChanged;
            if (handler == null)
            {
                return;
            }

            try
            {
                handler(paused);
            }
            catch (Exception e)
            {
                ServiceLog.Exception("Pause handler failed", e);
            }
        }

        private void OnDestroy()
        {
            if (s_instance == this)
            {
                s_instance = null;
            }
        }
    }
}
