using System;

namespace BeamRunner.Core.Audio
{
    /// <summary>
    /// Base class for an arrangement. A song renders each stem's dry voices + effects + sidechain into a
    /// buffer of <c>LoopFrames + tail</c>; <see cref="MusicRenderer"/> then folds the tail and finalises it.
    /// Implementations must be stateless (stems render in parallel).
    /// </summary>
    internal abstract class Song
    {
        public abstract MusicMode Mode { get; }

        public abstract double Bpm { get; }

        public abstract int Bars { get; }

        public abstract MusicStem[] Stems { get; }

        /// <summary>Extra render time after the loop for releases/reverb/delay (folded back onto the start).</summary>
        public abstract float TailSeconds(MusicStem stem);

        /// <summary>Final linear gain applied to a stem after folding (the mix balance).</summary>
        public abstract float StemGain(MusicStem stem);

        /// <summary>Renders one stem (voices, effects and ducking) into <paramref name="buffer"/>.</summary>
        public abstract void Render(MusicStem stem, StereoBuffer buffer, MusicClock clock, int seed);

        // ------------------------------------------------------------------ shared helpers

        /// <summary>
        /// Builds a loop-periodic sidechain gain curve. Because kick positions are known, the dip starts
        /// ~3 ms <em>before</em> each kick (so note attacks that coincide with the kick don't stack on its transient),
        /// holds briefly, then recovers along an S-curve over <paramref name="releaseBeats"/>. Kicks wrap around
        /// the loop, so the curve is continuous at the seam.
        /// </summary>
        protected static float[] Sidechain(MusicClock clock, double[] kickBeats, float depth, double releaseBeats, int totalFrames)
        {
            int loop = clock.LoopFrames;
            var curve = new float[Math.Max(totalFrames, loop)];
            int pre = Math.Max(1, (int)(0.003 * clock.SampleRate));
            int hold = (int)(0.012 * clock.SampleRate);
            int rel = Math.Max(1, clock.Frames(releaseBeats));
            int n = kickBeats.Length;
            for (int f = 0; f < loop; f++)
            {
                curve[f] = 1f;
            }

            float invPre = 1f / pre;
            for (int k = 0; k < n; k++)
            {
                int kf = clock.Frame(kickBeats[k]);
                int next = k + 1 < n ? clock.Frame(kickBeats[k + 1]) : clock.Frame(kickBeats[0]) + loop;

                // Recover fully before the next kick's pre-dip so consecutive dips never jump.
                int relK = Math.Max(1, Math.Min(rel, next - kf - pre - hold - 1));
                float invRel = 1f / relK;
                int endF = kf + hold + relK;
                for (int f = kf - pre; f < endF; f++)
                {
                    int t = f - kf;
                    float env;
                    if (t < 0)
                    {
                        env = DspMath.Smooth((t + pre) * invPre);
                    }
                    else if (t < hold)
                    {
                        env = 1f;
                    }
                    else
                    {
                        env = 1f - DspMath.Smooth((t - hold) * invRel);
                    }

                    int idx = f < 0 ? f + loop : (f >= loop ? f - loop : f);
                    curve[idx] = 1f - depth * env;
                }
            }

            // Repeat the loop-periodic curve over the tail.
            for (int f = loop; f < totalFrames; f += loop)
            {
                Array.Copy(curve, 0, curve, f, Math.Min(loop, totalFrames - f));
            }

            return curve;
        }

        /// <summary>
        /// Renders a chord progression as pad chords (one shared voice per chord) at half the sample rate and
        /// upsamples it into <paramref name="b"/>. The LFO completes <paramref name="lfoCycles"/> cycles per loop.
        /// </summary>
        protected static void RenderChordsHalfRate(StereoBuffer b, MusicClock c, int[][] chords, double beatsPerChord, double gateBeats,
            float gain, PadPatch patch, float topOctaveGain, int seed, int lfoCycles)
        {
            float hsr = c.SampleRate * 0.5f;
            var half = new StereoBuffer(b.Frames / 2 + 2);
            patch.LfoRate = lfoCycles * 2f / c.LoopFrames;
            for (int ch = 0; ch < chords.Length; ch++)
            {
                int[] notes = chords[ch];
                int count = notes.Length + (topOctaveGain > 0f ? 1 : 0);
                var freqs = new float[count];
                var gains = new float[count];
                for (int i = 0; i < notes.Length; i++)
                {
                    freqs[i] = Hz(notes[i]);
                    gains[i] = 1f;
                }

                if (topOctaveGain > 0f)
                {
                    freqs[count - 1] = Hz(notes[notes.Length - 1] + 12);
                    gains[count - 1] = topOctaveGain;
                }

                int start = c.Frame(ch * beatsPerChord) / 2;
                int gate = c.Frames(gateBeats) / 2;
                Voices.PadChord(half, start, gate, freqs, gains, gain, patch, NoteSeed(seed, ch, 0), hsr);
            }

            Voices.Upsample2x(half, b);
        }

        protected static float Hz(int midi)
        {
            return DspMath.MidiToHz(midi);
        }

        /// <summary>Deterministic per-note seed.</summary>
        protected static uint NoteSeed(int seed, int a, int b)
        {
            unchecked
            {
                return (uint)(seed * 73856093) ^ (uint)(a * 19349663) ^ (uint)(b * 83492791);
            }
        }
    }
}
