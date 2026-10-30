using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace BeamRunner.UI
{
    /// <summary>
    /// Progressive UI construction: runs queued work — deferred screens (<see cref="UIScreen.CreateDeferred{T}"/>) step by
    /// step, pool warm-ups, icon uploads — a few milliseconds per frame, in order, so a large UI is built behind a splash
    /// instead of in one long first frame. Lives on the <see cref="UIRoot"/> (<c>UIRoot.BuildQueue</c>).
    /// <para>Nothing ever waits for the queue: showing a screen that is not built yet builds the rest on the spot
    /// (<see cref="UIScreen.EnsureBuilt"/>) and <see cref="UIIcons.Get"/> paints an icon that has not been prewarmed, so the
    /// queue only decides <i>when</i> the work happens. Enqueue what is needed soonest first.</para>
    /// <para>The queue never runs in the frame it was created nor the next one, so a UI created at boot gets its first
    /// frame (the splash) on screen before any queued work starts. At least one step runs per frame while work is
    /// pending; a single step (one <see cref="UIScreen.BuildSteps"/> step, one job) is never split.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UIBuildQueue : MonoBehaviour
    {
        /// <summary>Default per-frame work budget in milliseconds.</summary>
        public const float DefaultBudgetMs = 4f;

        private struct Item
        {
            public UIScreen Screen;
            public bool Warm;
            public bool Warmed;
            public Action Job;
        }

        private readonly List<Item> _items = new List<Item>(24);
        private readonly Stopwatch _clock = new Stopwatch();
        private int _head;
        private int _enqueued;
        private int _completed;
        private int _createdFrame;
        private bool _icons;

        /// <summary>Work per frame, in milliseconds (at least one step runs per frame while work is pending).</summary>
        public float BudgetMs = DefaultBudgetMs;

        /// <summary>When true, no queued work runs (showing a screen still builds it on demand).</summary>
        public bool Paused;

        /// <summary>Items (screens, jobs) not finished yet.</summary>
        public int Pending
        {
            get { return _items.Count - _head; }
        }

        /// <summary>True when every queued item is done and, if requested, every icon is uploaded.</summary>
        public bool IsIdle
        {
            get { return Pending == 0 && (!_icons || UIIcons.IsPrewarmed); }
        }

        /// <summary>Share of the enqueued items that are done (0..1; 1 when nothing was enqueued).</summary>
        public float Progress
        {
            get { return _enqueued == 0 ? 1f : Mathf.Clamp01((float)_completed / _enqueued); }
        }

        /// <summary>
        /// Queues the remaining build of <paramref name="screen"/> (one <see cref="UIScreen.BuildSteps"/> step per slot).
        /// <paramref name="warm"/> then also pays its one-time activation costs (<c>Awake</c>, first layout, font glyphs)
        /// in a separate step, while it is still hidden.
        /// </summary>
        public void Enqueue(UIScreen screen, bool warm = true)
        {
            if (screen == null)
            {
                return;
            }

            _items.Add(new Item { Screen = screen, Warm = warm });
            _enqueued++;
        }

        /// <summary>Queues a one-step job (e.g. pre-creating pooled views). Exceptions are logged.</summary>
        public void Enqueue(Action job)
        {
            if (job == null)
            {
                return;
            }

            _items.Add(new Item { Job = job });
            _enqueued++;
        }

        /// <summary>
        /// Starts painting every icon off the main thread (<see cref="UIIcons.BeginPrewarm"/>) and uploads the results with
        /// the budget queued items leave over.
        /// </summary>
        public void PrewarmIcons()
        {
            UIIcons.BeginPrewarm();
            _icons = true;
        }

        /// <summary>Runs everything that is left right now.</summary>
        public void Flush()
        {
            while (_head < _items.Count)
            {
                RunStep();
            }

            if (_icons)
            {
                UIIcons.PumpPrewarm(double.MaxValue);
            }
        }

        private void Awake()
        {
            _createdFrame = Time.frameCount;
        }

        private void Update()
        {
            // Keep the frame the queue was created in (and the next one, for UIs created during scene load, whose Awake
            // runs before the first frame's Update) free of queued work: the splash has to reach the screen first.
            if (Paused || Time.frameCount <= _createdFrame + 1)
            {
                return;
            }

            if (_head >= _items.Count && (!_icons || UIIcons.IsPrewarmed))
            {
                return;
            }

            double budget = Mathf.Max(0.25f, BudgetMs);
            _clock.Reset();
            _clock.Start();
            bool ranOne = false;
            while (_head < _items.Count)
            {
                if (ranOne && _clock.Elapsed.TotalMilliseconds >= budget)
                {
                    break;
                }

                RunStep();
                ranOne = true;
            }

            if (_icons && !UIIcons.IsPrewarmed)
            {
                double left = budget - _clock.Elapsed.TotalMilliseconds;
                if (!ranOne || left > 0.25)
                {
                    UIIcons.PumpPrewarm(ranOne ? left : budget);
                }
            }

            _clock.Stop();
            if (_head >= _items.Count)
            {
                // Everything done: drop the references (screens may be destroyed later).
                _items.Clear();
                _head = 0;
            }
        }

        private void RunStep()
        {
            Item item = _items[_head];
            bool done;
            try
            {
                done = Step(ref item);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogException(e, this);
                done = true;
            }

            if (done)
            {
                _items[_head] = default;
                _head++;
                _completed++;
            }
            else
            {
                _items[_head] = item;
            }
        }

        private static bool Step(ref Item item)
        {
            if (item.Job != null)
            {
                item.Job();
                return true;
            }

            UIScreen screen = item.Screen;
            if (screen == null)
            {
                // Destroyed while queued.
                return true;
            }

            if (!screen.IsBuilt)
            {
                return screen.BuildNextStep() && !item.Warm;
            }

            if (item.Warm && !item.Warmed)
            {
                item.Warmed = true;
                screen.Warm();
            }

            return true;
        }
    }
}
