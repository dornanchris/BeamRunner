using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace BeamRunner.UI
{
    /// <summary>
    /// Procedural SDF icons: white, anti-aliased, 128 px with mipmaps, drawn on a common 24-unit grid with a shared
    /// stroke weight (see <see cref="IconPainter"/>). Tint with <c>Image.color</c>; use <c>preserveAspect</c>.
    /// Icons are generated lazily on first use and cached. <see cref="BeginPrewarm"/> paints them all ahead of time on a
    /// worker thread (the pixel work is engine-free), leaving only the texture upload to the main thread
    /// (<see cref="PumpPrewarm"/>, or on first use).
    /// </summary>
    public static class UIIcons
    {
        /// <summary>Texture size of every icon.</summary>
        public const int Size = 128;

        private const int Free = 0;
        private const int Taken = 1;
        private const int Painted = 2;
        private const int Consumed = 3;

        // Painted first: what the menu, the daily popup and the settings/missions/records overlays show on arrival.
        private static readonly IconId[] Priority =
        {
            IconId.Gem, IconId.Plus, IconId.Ad, IconId.Gift, IconId.Settings, IconId.ChevronDown, IconId.Play, IconId.Hangar,
            IconId.Missions, IconId.Trophy, IconId.Close, IconId.Crown, IconId.Check, IconId.Back, IconId.Music,
            IconId.SoundOn, IconId.Vibration, IconId.Gamepad, IconId.Speed, IconId.Restore, IconId.Bolt, IconId.Star,
            IconId.Stats, IconId.Shield, IconId.Info, IconId.Cart, IconId.Heart, IconId.Warning,
        };

        /// <summary>One prewarm run. Worker and main thread coordinate through <see cref="State"/> (per icon, by id).</summary>
        private sealed class PrewarmJob
        {
            public IconId[] Order;
            public Color32[][] Pixels;
            public int[] State;
            public int Next;
            public bool Threaded;
            public volatile bool WorkerDone;
            public volatile bool Cancelled;
        }

        private static readonly System.Diagnostics.Stopwatch PumpClock = new System.Diagnostics.Stopwatch();
        private static PrewarmJob _job;

        /// <summary>True once <see cref="BeginPrewarm"/> ran and every icon has been uploaded.</summary>
        public static bool IsPrewarmed
        {
            get { return _job != null && _job.Next >= _job.Order.Length; }
        }

        /// <summary>Sprite for <paramref name="id"/> (null for <see cref="IconId.None"/>).</summary>
        public static Sprite Get(IconId id)
        {
            if (id == IconId.None)
            {
                return null;
            }

            return UISprites.IconSprite(id, Size);
        }

        /// <summary>Generates icons ahead of time, synchronously (e.g. during a loading fade) to avoid a hitch on first display.</summary>
        public static void Prewarm(params IconId[] ids)
        {
            if (ids == null)
            {
                return;
            }

            for (int i = 0; i < ids.Length; i++)
            {
                Get(ids[i]);
            }
        }

        /// <summary>Generates every icon, synchronously.</summary>
        public static void PrewarmAll()
        {
            var all = (IconId[])Enum.GetValues(typeof(IconId));
            for (int i = 0; i < all.Length; i++)
            {
                Get(all[i]);
            }
        }

        /// <summary>
        /// Starts painting every icon on a worker thread (idempotent). Painted icons are uploaded by
        /// <see cref="PumpPrewarm"/> or instantly by the first <see cref="Get"/>; an icon requested before the worker
        /// reached it is painted on the spot (and skipped by the worker). Without threads (WebGL) the pump paints them
        /// on the main thread instead, one per slot.
        /// </summary>
        public static void BeginPrewarm()
        {
            if (_job != null)
            {
                return;
            }

            var all = (IconId[])Enum.GetValues(typeof(IconId));
            int max = 0;
            for (int i = 0; i < all.Length; i++)
            {
                max = Mathf.Max(max, (int)all[i]);
            }

            var job = new PrewarmJob
            {
                Order = BuildOrder(all, max),
                Pixels = new Color32[max + 1][],
                State = new int[max + 1],
            };

            _job = job;
            if (Application.platform == RuntimePlatform.WebGLPlayer)
            {
                job.WorkerDone = true;
                return;
            }

            try
            {
                // LongRunning = a dedicated thread: at boot the thread pool is saturated by the audio synthesis.
                Task.Factory.StartNew(() => PaintAll(job), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                job.Threaded = true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("UIIcons: painting on the main thread (" + e.Message + ")");
                job.WorkerDone = true;
            }
        }

        /// <summary>
        /// Main thread: uploads icons the worker has finished (in priority order) until <paramref name="budgetMs"/> is
        /// used up. Returns true when every icon is available.
        /// </summary>
        public static bool PumpPrewarm(double budgetMs)
        {
            PrewarmJob job = _job;
            if (job == null)
            {
                return true;
            }

            PumpClock.Reset();
            PumpClock.Start();
            while (job.Next < job.Order.Length)
            {
                IconId id = job.Order[job.Next];
                int i = (int)id;
                if (!UISprites.HasIconSprite(id, Size))
                {
                    int state = Volatile.Read(ref job.State[i]);
                    bool workerPending = job.Threaded && !job.WorkerDone && (state == Free || state == Taken);
                    if (workerPending)
                    {
                        // The worker paints in this order: nothing further along is ready either.
                        break;
                    }

                    if (PumpClock.Elapsed.TotalMilliseconds >= budgetMs)
                    {
                        break;
                    }

                    // Painted → upload; otherwise (no thread, worker failed or quit) paint it here.
                    try
                    {
                        Get(id);
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                    }
                }
                else
                {
                    job.Pixels[i] = null;
                }

                job.Next++;
            }

            PumpClock.Stop();
            bool done = job.Next >= job.Order.Length;
            if (done && job.WorkerDone)
            {
                // Drop pixels the worker finished after the main thread had already painted that icon itself.
                Array.Clear(job.Pixels, 0, job.Pixels.Length);
            }

            return done;
        }

        /// <summary>
        /// Main thread, on a cache miss: the worker's pixels for <paramref name="id"/> if it has finished them (null
        /// otherwise — the caller paints; an icon the worker has not started is claimed so it doesn't paint it too).
        /// </summary>
        internal static Color32[] TakePainted(IconId id)
        {
            PrewarmJob job = _job;
            int i = (int)id;
            if (job == null || i < 0 || i >= job.State.Length)
            {
                return null;
            }

            int state = Interlocked.CompareExchange(ref job.State[i], Taken, Free);
            if (state != Painted)
            {
                return null;
            }

            Color32[] px = job.Pixels[i];
            job.Pixels[i] = null;
            Volatile.Write(ref job.State[i], Consumed);
            return px;
        }

        private static void PaintAll(PrewarmJob job)
        {
            try
            {
                for (int n = 0; n < job.Order.Length && !job.Cancelled; n++)
                {
                    int i = (int)job.Order[n];
                    if (Interlocked.CompareExchange(ref job.State[i], Taken, Free) != Free)
                    {
                        // Already requested on the main thread (painted there).
                        continue;
                    }

                    try
                    {
                        float[] coverage = IconPainter.Paint(job.Order[n], Size);
                        job.Pixels[i] = UISprites.ToPixels(coverage, Size, Size);
                        Volatile.Write(ref job.State[i], Painted);
                    }
                    catch (Exception)
                    {
                        // Leave it to the main thread (Get paints it synchronously and reports any error there).
                        Volatile.Write(ref job.State[i], Consumed);
                    }
                }
            }
            finally
            {
                job.WorkerDone = true;
            }
        }

        private static IconId[] BuildOrder(IconId[] all, int max)
        {
            var seen = new bool[max + 1];
            var order = new System.Collections.Generic.List<IconId>(all.Length);
            for (int i = 0; i < Priority.Length; i++)
            {
                Add(Priority[i]);
            }

            for (int i = 0; i < all.Length; i++)
            {
                Add(all[i]);
            }

            return order.ToArray();

            void Add(IconId id)
            {
                int index = (int)id;
                if (id != IconId.None && index >= 0 && index <= max && !seen[index])
                {
                    seen[index] = true;
                    order.Add(id);
                }
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            if (_job != null)
            {
                _job.Cancelled = true;
            }

            _job = null;
        }
    }
}
