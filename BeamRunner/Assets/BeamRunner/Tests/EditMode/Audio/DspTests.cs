using System;
using BeamRunner.Core.Audio;
using NUnit.Framework;

namespace BeamRunner.Core.Tests.Audio
{
    /// <summary>Unit tests for the DSP building blocks.</summary>
    [TestFixture]
    public sealed class DspTests
    {
        private const float Sr = 44100f;

        [Test]
        public void SineTable_MatchesMathSin()
        {
            for (int i = 0; i < 1000; i++)
            {
                float p = i / 1000f;
                Assert.That(DspMath.Sin01(p), Is.EqualTo((float)Math.Sin(p * 2.0 * Math.PI)).Within(1e-3f));
            }
        }

        [Test]
        public void Oscillators_StayBoundedAndDcFree()
        {
            var o = new Osc(0f);
            float inc = 440f / Sr;
            double sum = 0;
            float max = 0f;
            int n = 44100;
            for (int i = 0; i < n; i++)
            {
                float v = o.Saw(inc);
                sum += v;
                max = Math.Max(max, Math.Abs(v));
            }

            Assert.LessOrEqual(max, 1.05f);
            Assert.Less(Math.Abs(sum / n), 0.01);

            var p = new Osc(0f);
            sum = 0;
            for (int i = 0; i < n; i++)
            {
                sum += p.Pulse(inc, 0.25f);
            }

            Assert.Less(Math.Abs(sum / n), 0.01, "pulse should be DC-free");
        }

        [Test]
        public void Noise_IsDeterministicAndCentred()
        {
            var a = new Noise(42u);
            var b = new Noise(42u);
            double sum = 0;
            for (int i = 0; i < 10000; i++)
            {
                float x = a.White();
                Assert.AreEqual(x, b.White());
                Assert.That(x, Is.InRange(-1f, 1f));
                sum += x;
            }

            Assert.Less(Math.Abs(sum / 10000), 0.03);
        }

        [Test]
        public void Svf_IsStableUnderFastModulation()
        {
            var f = new Svf();
            var n = new Noise(7u);
            float max = 0f;
            for (int i = 0; i < 200000; i++)
            {
                float cut = 50f * (float)Math.Pow(2.0, 8.0 * (0.5 + 0.5 * Math.Sin(i * 0.01)));
                f.Set(cut, 8f, Sr);
                float y = f.Process(n.White());
                Assert.IsFalse(float.IsNaN(y) || float.IsInfinity(y));
                max = Math.Max(max, Math.Abs(f.High) + Math.Abs(y));
            }

            Assert.Less(max, 100f);
        }

        [Test]
        public void Biquad_LowPassAttenuatesHighs()
        {
            var lp = Biquad.LowPass(500.0, 0.707, Sr);
            var o = new Osc(0f);
            float max = 0f;
            for (int i = 0; i < 44100; i++)
            {
                float y = lp.Process(o.Sine(8000f / Sr));
                if (i > 4410)
                {
                    max = Math.Max(max, Math.Abs(y));
                }
            }

            Assert.Less(max, 0.02f);
        }

        [Test]
        public void Reverb_TailDecaysAndStaysFinite()
        {
            var b = new StereoBuffer((int)(Sr * 8));
            b.L[100] = 1f;
            b.R[100] = 1f;
            var rv = new Reverb(Sr, 1.2f, 1.5f, 6000f, 10f);
            rv.Process(b.L, b.R, b.L, b.R, 0, b.Frames, 1f, 1f);
            float early = 0f;
            float late = 0f;
            for (int i = 0; i < b.Frames; i++)
            {
                Assert.IsFalse(float.IsNaN(b.L[i]) || float.IsNaN(b.R[i]));
                if (i > 1000 && i < Sr * 0.5f)
                {
                    early = Math.Max(early, Math.Abs(b.L[i]));
                }

                if (i > Sr * 6f)
                {
                    late = Math.Max(late, Math.Abs(b.L[i]));
                }
            }

            Assert.Greater(early, 1e-3f, "reverb produced no tail");
            Assert.Less(late, early * 1e-3f, "tail did not decay by 60 dB");
        }

        [Test]
        public void PingPongDelay_AlternatesSides()
        {
            var b = new StereoBuffer(4000);
            b.L[0] = 1f;
            b.R[0] = 1f;
            var d = new PingPongDelay(1000, 0.5f, 20000f, 5f, Sr);
            d.Process(b.L, b.R, b.L, b.R, 0, b.Frames, 1f, 1f);
            Assert.Greater(Math.Abs(b.L[1000]), 0.5f, "first echo on the left");
            Assert.Less(Math.Abs(b.R[1000]), 1e-3f);
            Assert.Greater(Math.Abs(b.R[2000]), 0.2f, "second echo on the right");
        }

        [Test]
        public void Limiter_NeverExceedsCeiling()
        {
            var b = new StereoBuffer(20000);
            var o = new Osc(0f);
            for (int i = 0; i < b.Frames; i++)
            {
                float v = o.Saw(110f / Sr) * (i % 5000 < 100 ? 4f : 0.5f);
                b.L[i] = v;
                b.R[i] = -v;
            }

            var lim = new Limiter(-1f, 0.1f, Sr);
            lim.Process(b.L, b.R, 0, b.Frames);
            Assert.LessOrEqual(b.Peak(b.Frames), DspMath.DbToGain(-1f) + 1e-5f);
        }

        [Test]
        public void SoftClip_IsMonotonicAndBounded()
        {
            float prev = -2f;
            for (int i = -400; i <= 400; i++)
            {
                float x = i / 50f;
                float y = DspMath.SoftClip(x, 0.7f);
                Assert.LessOrEqual(Math.Abs(y), 1f);
                Assert.GreaterOrEqual(y, prev);
                prev = y;
            }

            Assert.AreEqual(0.5f, DspMath.SoftClip(0.5f, 0.7f));
        }

        [Test]
        public void FoldTail_WrapsTailOntoStart()
        {
            var b = new StereoBuffer(150);
            for (int i = 0; i < 150; i++)
            {
                b.L[i] = i >= 100 ? 1f : 0f;
            }

            b.FoldTail(100);
            Assert.AreEqual(1f, b.L[0]);
            Assert.AreEqual(1f, b.L[49]);
            Assert.AreEqual(0f, b.L[50]);
            Assert.AreEqual(0f, b.L[120]);
        }

        [Test]
        public void MusicClock_LoopIsWholeFramesAndRateIsLoopPeriodic()
        {
            var c = new MusicClock(44100, 116.0, 32);
            Assert.AreEqual((int)Math.Round(32 * 60.0 / 116.0 * 44100), c.LoopFrames);
            Assert.AreEqual(0, c.Frame(0));
            Assert.AreEqual(c.LoopFrames, c.Frame(32));
            float rate = c.LoopRate(3);
            Assert.That(rate * c.LoopFrames / 44100.0, Is.EqualTo(3.0).Within(1e-4));
        }

        [Test]
        public void Loudness_MeasuresFullScaleSineNearMinus3Lufs()
        {
            // A 1 kHz full-scale sine in both channels reads about +0.0 LUFS per BS.1770 (−3.01 per channel + 3.01).
            var b = new StereoBuffer(44100);
            var o = new Osc(0f);
            for (int i = 0; i < b.Frames; i++)
            {
                float v = o.Sine(1000f / Sr);
                b.L[i] = v;
                b.R[i] = v;
            }

            float lufs = Loudness.MaxMomentary(b, b.Frames, Sr);
            Assert.That(lufs, Is.EqualTo(0f).Within(0.5f));
        }
    }
}
