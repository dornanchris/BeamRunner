using System;
using System.Threading.Tasks;

namespace BeamRunner.Core.Audio
{
    /// <summary>
    /// Renders arrangements into seamless, equal-length stems: each stem is rendered as loop + tail, the tail
    /// is faded and folded back onto the start (so releases/reverb/delay wrap around), then gain-staged and
    /// passed through a safety limiter that is pre-warmed on the loop end so its state is continuous at the seam.
    /// In the playback layout the always-on stems are then summed into one <see cref="MusicStem.Base"/> stem
    /// (<see cref="MusicLayers"/>).
    /// </summary>
    internal static class MusicRenderer
    {
        /// <summary>Frames per work item when the Base mix is summed on the thread pool.</summary>
        private const int SumBlockFrames = 65536;

        public static Song GetSong(MusicMode mode)
        {
            switch (mode)
            {
                case MusicMode.Menu:
                    return new MenuSong();
                case MusicMode.Run:
                    return new RunSong();
                case MusicMode.GameOver:
                    return new GameOverSong();
                default:
                    return null;
            }
        }

        /// <param name="separateStems">Keep every layer as its own stem (tools, mix calibration) instead of the playback
        /// layout, where the always-on layers are pre-mixed into one <see cref="MusicStem.Base"/> stem.</param>
        public static MusicArrangement Render(MusicMode mode, int sampleRate, int seed, bool parallel, bool separateStems = false)
        {
            Song song = GetSong(mode);
            if (song == null)
            {
                return null;
            }

            var clock = new MusicClock(sampleRate, song.Bpm, song.Bars * 4);
            MusicStem[] ids = song.Stems;
            int alwaysOn = 0;
            for (int i = 0; i < ids.Length; i++)
            {
                if (!MusicLayers.IsFaded(mode, ids[i]))
                {
                    alwaysOn++;
                }
            }

            // Always-on stems of the playback layout stay planar until they are summed into the Base stem, always in
            // song order (so parallel and sequential renders give the same bits).
            bool premix = !separateStems && alwaysOn >= 2;
            int loop = clock.LoopFrames;
            var stems = new RenderedAudio[ids.Length];
            var planar = premix ? new StereoBuffer[ids.Length] : null;
            float[] mixed = null;
            Action<int> render = i =>
            {
                StereoBuffer buf = RenderStem(song, ids[i], clock, seed);
                if (premix && !MusicLayers.IsFaded(mode, ids[i]))
                {
                    planar[i] = buf;
                }
                else
                {
                    stems[i] = buf.ToRendered(StemName(mode, ids[i]), loop, sampleRate, true, false);
                }
            };

            if (parallel && ids.Length > 1)
            {
                Parallel.For(0, ids.Length, render);
                for (int i = 0; premix && i < ids.Length; i++)
                {
                    mixed = AddInterleaved(mixed, planar[i], loop, true);
                }
            }
            else
            {
                for (int i = 0; i < ids.Length; i++)
                {
                    render(i);
                    if (premix)
                    {
                        // Sum right away so only one planar stem is alive at a time (reload / WebGL path).
                        mixed = AddInterleaved(mixed, planar[i], loop, false);
                        planar[i] = null;
                    }
                }
            }

            if (!premix)
            {
                var idCopy = (MusicStem[])ids.Clone();
                return new MusicArrangement(mode, sampleRate, clock.LoopFrames, clock.BeatsPerLoop, 4, idCopy, stems);
            }

            // Base first, then the independently faded layers in song order.
            var outIds = new MusicStem[ids.Length - alwaysOn + 1];
            var outStems = new RenderedAudio[outIds.Length];
            outIds[0] = MusicStem.Base;
            outStems[0] = new RenderedAudio(StemName(mode, MusicStem.Base), mixed, 2, sampleRate, true);
            for (int i = 0, k = 1; i < ids.Length; i++)
            {
                if (stems[i] != null)
                {
                    outIds[k] = ids[i];
                    outStems[k] = stems[i];
                    k++;
                }
            }

            return new MusicArrangement(mode, sampleRate, clock.LoopFrames, clock.BeatsPerLoop, 4, outIds, outStems);
        }

        private static string StemName(MusicMode mode, MusicStem stem)
        {
            return "Music_" + mode + "_" + stem;
        }

        /// <summary>
        /// Adds the first <paramref name="frames"/> of a planar stem into the interleaved Base mix (allocated on the first
        /// stem; null stems are skipped). The game plays the always-on stems with identical gain and duck filter from the
        /// same DSP sample, so their sum is exactly what the mixer would produce (to float rounding) from one clip instead
        /// of several. Each stem is limited already and the full mix keeps its headroom (tested), so it stays within ±1.
        /// </summary>
        /// <param name="parallel">Split the frames into blocks on the thread pool (every sample still gets the same
        /// operations, so the result does not depend on it).</param>
        private static float[] AddInterleaved(float[] mix, StereoBuffer stem, int frames, bool parallel)
        {
            if (stem == null)
            {
                return mix;
            }

            if (stem.Frames < frames)
            {
                throw new InvalidOperationException("Stem shorter than the loop.");
            }

            bool first = mix == null;
            float[] dst = first ? new float[frames * 2] : mix;
            float[] l = stem.L;
            float[] r = stem.R;
            Action<int> block = b =>
            {
                int start = b * SumBlockFrames;
                int end = Math.Min(frames, start + SumBlockFrames);
                if (first)
                {
                    for (int j = start, o = start * 2; j < end; j++, o += 2)
                    {
                        dst[o] = l[j];
                        dst[o + 1] = r[j];
                    }
                }
                else
                {
                    for (int j = start, o = start * 2; j < end; j++, o += 2)
                    {
                        dst[o] += l[j];
                        dst[o + 1] += r[j];
                    }
                }
            };

            int blocks = (frames + SumBlockFrames - 1) / SumBlockFrames;
            if (parallel && blocks > 1)
            {
                Parallel.For(0, blocks, block);
            }
            else
            {
                for (int b = 0; b < blocks; b++)
                {
                    block(b);
                }
            }

            return dst;
        }

        /// <summary>Renders one stem into a planar buffer; the first <see cref="MusicClock.LoopFrames"/> frames are the loop.</summary>
        private static StereoBuffer RenderStem(Song song, MusicStem stem, MusicClock clock, int seed)
        {
            int sr = clock.SampleRate;
            int loop = clock.LoopFrames;
            int tail = (int)(song.TailSeconds(stem) * sr);
            var buf = new StereoBuffer(loop + tail);
            unchecked
            {
                song.Render(stem, buf, clock, seed * 31 + (int)song.Mode * 1009 + (int)stem * 7919);
            }

            // Fade the far end of the tail so nothing is truncated audibly, then wrap it onto the start.
            int fade = Math.Min(tail, (int)(0.4f * sr));
            if (fade > 0)
            {
                buf.FadeOut(buf.Frames - fade, fade);
            }

            buf.FoldTail(loop);
            buf.Scale(song.StemGain(stem), 0, loop);
            var limiter = new Limiter(-1f, 0.12f, sr);
            limiter.ProcessLoop(buf.L, buf.R, loop, Math.Min(loop, sr));
            return buf;
        }
    }
}
