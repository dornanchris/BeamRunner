using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BeamRunner.Core.Audio
{
    /// <summary>
    /// Entry point of the procedural audio bank. Everything is rendered deterministically from a seed into plain
    /// float PCM (no engine types), so it can run on a worker thread at boot and be unit-tested.
    /// <para>
    /// Staged order (<see cref="RenderStaged"/>): Menu music first (so the menu can start quickly), then every SFX,
    /// the engine/slipstream loops, Run music and finally the GameOver loop.
    /// </para>
    /// <para>
    /// Music comes in the playback layout: stems the game never fades on their own are pre-mixed into one
    /// <see cref="MusicStem.Base"/> clip (Menu: 1 clip, Run: Base + Arp + Lead, GameOver: 1 clip), which roughly halves
    /// the AudioClip memory of the bank without changing what is heard.
    /// </para>
    /// </summary>
    public static class AudioBank
    {
        public const int DefaultSampleRate = 44100;
        public const int DefaultSeed = 1986;

        /// <summary>Number of <see cref="SfxId"/> values (ids are contiguous from 0).</summary>
        public static readonly int SfxCount = CountEnum(typeof(SfxId));

        /// <summary>Number of <see cref="LoopId"/> values (ids are contiguous from 0).</summary>
        public static readonly int LoopCount = CountEnum(typeof(LoopId));

        // Relative render cost of each stage, used for progress reporting (measured with tools/audio-preview).
        private const float WeightMenu = 0.31f;
        private const float WeightSfx = 0.22f;
        private const float WeightLoops = 0.02f;
        private const float WeightRun = 0.33f;
        private const float WeightGameOver = 0.12f;

        /// <summary>
        /// Cumulative <see cref="AudioBankItem.Progress"/> once the essentials (menu music, every SFX and both loops)
        /// are rendered — i.e. everything the menu and UI need. Run and GameOver music follow.
        /// </summary>
        public const float EssentialsProgress = WeightMenu + WeightSfx + WeightLoops;

        private static int CountEnum(Type t)
        {
            int max = -1;
            foreach (object v in Enum.GetValues(t))
            {
                max = Math.Max(max, (int)v);
            }

            return max + 1;
        }

        private static int SafeRate(int sampleRate)
        {
            return Math.Max(8000, Math.Min(96000, sampleRate));
        }

        /// <summary>Renders one music arrangement (null for <see cref="MusicMode.Silent"/>).</summary>
        /// <param name="parallel">Render the stems concurrently on the thread pool.</param>
        /// <param name="separateStems">False (default, what the game loads): the playback layout, in which every always-on
        /// layer is pre-mixed into one <see cref="MusicStem.Base"/> stem and only the independently faded layers
        /// (<see cref="MusicLayers.IsFaded"/>) stay separate. True: every layer as its own stem (tools, calibration).</param>
        public static MusicArrangement RenderMusic(MusicMode mode, int sampleRate = DefaultSampleRate, int seed = DefaultSeed, bool parallel = false,
            bool separateStems = false)
        {
            return MusicRenderer.Render(mode, SafeRate(sampleRate), seed, parallel, separateStems);
        }

        /// <summary>Renders one sound effect (stereo, trimmed, peak-normalised, never above 0 dBFS).</summary>
        public static RenderedAudio RenderSfx(SfxId id, int sampleRate = DefaultSampleRate, int seed = DefaultSeed)
        {
            return SfxRenderer.Render(id, SafeRate(sampleRate), seed);
        }

        /// <summary>Renders one seamless ambience loop.</summary>
        public static RenderedAudio RenderLoop(LoopId id, int sampleRate = DefaultSampleRate, int seed = DefaultSeed)
        {
            return LoopRenderer.Render(id, SafeRate(sampleRate), seed);
        }

        /// <summary>
        /// Lazily renders the whole bank in priority order, yielding each finished item. Enumerate it on a worker
        /// thread; items are immutable and can be handed to the main thread.
        /// </summary>
        /// <param name="parallel">Use the thread pool for stems / SFX batches (big speed-up on multi-core phones).</param>
        public static IEnumerable<AudioBankItem> RenderStaged(int sampleRate = DefaultSampleRate, bool parallel = true, int seed = DefaultSeed)
        {
            int sr = SafeRate(sampleRate);
            float done = 0f;

            // Locals are nulled after each yield: iterator locals live as long as the enumeration, and holding
            // tens of MB of rendered PCM after the consumer has taken it would inflate peak memory.
            MusicArrangement menu = MusicRenderer.Render(MusicMode.Menu, sr, seed, parallel);
            done += WeightMenu;
            yield return AudioBankItem.ForMusic(menu, done);
            menu = null;

            int n = SfxCount;
            var sfx = new RenderedAudio[n];
            if (parallel)
            {
                Parallel.For(0, n, i => sfx[i] = SfxRenderer.Render((SfxId)i, sr, seed));
            }

            for (int i = 0; i < n; i++)
            {
                if (sfx[i] == null)
                {
                    sfx[i] = SfxRenderer.Render((SfxId)i, sr, seed);
                }

                done += WeightSfx / n;
                yield return AudioBankItem.ForSfx((SfxId)i, sfx[i], done);
                sfx[i] = null;
            }

            for (int i = 0; i < LoopCount; i++)
            {
                RenderedAudio loop = LoopRenderer.Render((LoopId)i, sr, seed);
                done += WeightLoops / LoopCount;
                yield return AudioBankItem.ForLoop((LoopId)i, loop, done);
                loop = null;
            }

            // The single-stem GameOver loop renders alongside the Run stems to keep every core busy.
            Task<MusicArrangement> overTask = parallel
                ? Task.Run(() => MusicRenderer.Render(MusicMode.GameOver, sr, seed, false))
                : null;
            MusicArrangement run = MusicRenderer.Render(MusicMode.Run, sr, seed, parallel);
            done += WeightRun;
            yield return AudioBankItem.ForMusic(run, done);
            run = null;

            MusicArrangement over = overTask != null ? overTask.Result : MusicRenderer.Render(MusicMode.GameOver, sr, seed, false);
            yield return AudioBankItem.ForMusic(over, 1f);
        }

        /// <summary>Renders everything synchronously (tools/tests), reporting 0..1 progress after each item.</summary>
        public static AudioBankData RenderAll(int sampleRate = DefaultSampleRate, Action<float> progress = null, bool parallel = true, int seed = DefaultSeed)
        {
            var data = new AudioBankData(SafeRate(sampleRate));
            foreach (AudioBankItem item in RenderStaged(sampleRate, parallel, seed))
            {
                data.Add(item);
                progress?.Invoke(item.Progress);
            }

            return data;
        }
    }
}
