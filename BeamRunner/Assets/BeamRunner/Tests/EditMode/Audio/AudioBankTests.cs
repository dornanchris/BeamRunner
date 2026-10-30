using System;
using System.Collections.Generic;
using System.Diagnostics;
using BeamRunner.Core.Audio;
using NUnit.Framework;

namespace BeamRunner.Core.Tests.Audio
{
    /// <summary>
    /// Renders the whole procedural bank once (as the game does at boot) and checks every clip: sanity (no
    /// NaN/Inf, bounded, audible), loop seams, stem lengths, timing metadata, mix headroom and determinism.
    /// </summary>
    [TestFixture]
    public sealed class AudioBankTests
    {
        private const int SampleRate = AudioBank.DefaultSampleRate;
        private AudioBankData _bank;
        private double _renderMs;
        private readonly Dictionary<MusicMode, MusicArrangement> _separate = new Dictionary<MusicMode, MusicArrangement>();

        [OneTimeSetUp]
        public void RenderBank()
        {
            var sw = Stopwatch.StartNew();
            _bank = AudioBank.RenderAll(SampleRate, null, true);
            _renderMs = sw.Elapsed.TotalMilliseconds;
        }

        private static IEnumerable<SfxId> AllSfx()
        {
            foreach (SfxId id in Enum.GetValues(typeof(SfxId)))
            {
                yield return id;
            }
        }

        private static IEnumerable<LoopId> AllLoops()
        {
            foreach (LoopId id in Enum.GetValues(typeof(LoopId)))
            {
                yield return id;
            }
        }

        private static readonly MusicMode[] Arrangements = { MusicMode.Menu, MusicMode.Run, MusicMode.GameOver };

        /// <summary>The arrangement with every layer as its own stem (what the playback layout pre-mixes), cached.</summary>
        private MusicArrangement Separate(MusicMode mode)
        {
            if (!_separate.TryGetValue(mode, out MusicArrangement m))
            {
                m = AudioBank.RenderMusic(mode, SampleRate, AudioBank.DefaultSeed, true, true);
                _separate[mode] = m;
            }

            return m;
        }

        [TestCaseSource(nameof(AllSfx))]
        public void Sfx_RendersCleanAndAudible(SfxId id)
        {
            RenderedAudio a = _bank.GetSfx(id);
            Assert.IsNotNull(a, "missing " + id);
            Assert.AreEqual(SampleRate, a.SampleRate);
            Assert.IsFalse(a.IsLoop);
            Assert.Greater(a.FrameCount, SampleRate / 100, "too short");
            Assert.Less(a.DurationSeconds, 3.5, "too long");
            AudioAsserts.Finite(a);
            Assert.LessOrEqual(a.Peak(), 1.0f, "peak above full scale");
            Assert.Greater(a.Peak(), DspMath.DbToGain(-20f), "peak too low");
            Assert.Greater(a.Rms(), DspMath.DbToGain(-50f), "practically silent");
            AudioAsserts.EdgesQuiet(a);
        }

        /// <summary>
        /// Clips end at the last sample above −70 dB re peak plus a 20 ms fade, or at the canvas end: the fade must only
        /// ever shape an inaudible tail, never cut through the sound itself.
        /// </summary>
        [TestCaseSource(nameof(AllSfx))]
        public void Sfx_EndFadeOnlyTouchesTheInaudibleTail(SfxId id)
        {
            RenderedAudio a = _bank.GetSfx(id);
            int ch = a.Channels;
            int fadeFrames = SampleRate / 50;
            float tail = 0f;
            for (int i = (a.FrameCount - fadeFrames) * ch; i < a.Samples.Length; i++)
            {
                tail = Math.Max(tail, Math.Abs(a.Samples[i]));
            }

            Assert.LessOrEqual(tail, a.Peak() * DspMath.DbToGain(-50f), id + " is cut off while still audible");
        }

        [Test]
        public void Sfx_LoudOnesAreLouderThanUiTicks()
        {
            Assert.Greater(_bank.GetSfx(SfxId.Crash).Rms(), _bank.GetSfx(SfxId.UiClick).Rms());
            Assert.Greater(_bank.GetSfx(SfxId.GateSmash).Rms(), _bank.GetSfx(SfxId.GatePass).Rms() * 4f);
            Assert.Greater(_bank.GetSfx(SfxId.Crash).Peak(), DspMath.DbToGain(-1.5f), "Crash should sit near 0 dBFS");
        }

        [TestCaseSource(nameof(AllLoops))]
        public void Loop_IsSeamlessAndClean(LoopId id)
        {
            RenderedAudio a = _bank.GetLoop(id);
            Assert.IsNotNull(a);
            Assert.IsTrue(a.IsLoop);
            AudioAsserts.Finite(a);
            Assert.LessOrEqual(a.Peak(), 1.0f);
            Assert.Greater(a.Rms(), DspMath.DbToGain(-40f));
            AudioAsserts.Seamless(a);
        }

        [TestCaseSource(nameof(Arrangements))]
        public void Music_StemsAreEqualLengthCleanAndSeamless(MusicMode mode)
        {
            MusicArrangement m = _bank.GetMusic(mode);
            Assert.IsNotNull(m);
            Assert.Greater(m.StemCount, 0);
            Assert.AreEqual(m.StemCount, m.StemIds.Count);
            foreach (RenderedAudio stem in m.Stems)
            {
                Assert.AreEqual(m.LoopFrames, stem.FrameCount, stem.Name + " length");
                Assert.AreEqual(m.SampleRate, stem.SampleRate, stem.Name + " rate");
                Assert.AreEqual(2, stem.Channels, stem.Name + " channels");
                Assert.IsTrue(stem.IsLoop);
                AudioAsserts.Finite(stem);
                Assert.LessOrEqual(stem.Peak(), 1.0f, stem.Name);
                Assert.Greater(stem.Rms(), DspMath.DbToGain(-45f), stem.Name + " is silent");
                AudioAsserts.Seamless(stem);
            }
        }

        [Test]
        public void Music_SeparateStemsHaveTheDocumentedLayers()
        {
            MusicArrangement run = Separate(MusicMode.Run);
            foreach (MusicStem s in new[] { MusicStem.Drums, MusicStem.Bass, MusicStem.Pads, MusicStem.Arp, MusicStem.Lead })
            {
                Assert.IsNotNull(run.GetStem(s), "Run " + s);
            }

            MusicArrangement menu = Separate(MusicMode.Menu);
            foreach (MusicStem s in new[] { MusicStem.Drums, MusicStem.Bass, MusicStem.Pads, MusicStem.Arp })
            {
                Assert.IsNotNull(menu.GetStem(s), "Menu " + s);
            }

            Assert.IsNull(menu.GetStem(MusicStem.Lead));
            Assert.IsNull(run.GetStem(MusicStem.Base));

            // The bass layers are mono content: shipped as their own stereo clips they would waste half their memory.
            Assert.IsTrue(AudioAsserts.IsMonoContent(run.GetStem(MusicStem.Bass)));
            Assert.IsTrue(AudioAsserts.IsMonoContent(menu.GetStem(MusicStem.Bass)));
            Assert.IsFalse(AudioAsserts.IsMonoContent(run.GetStem(MusicStem.Drums)));
            Assert.IsNotNull(_bank.GetMusic(MusicMode.GameOver).GetStem(MusicStem.Pads));
            Assert.IsNull(_bank.GetMusic(MusicMode.Silent));
        }

        [Test]
        public void MusicLayers_OnlyTheRunIntensityLayersAreFaded()
        {
            foreach (MusicMode mode in Enum.GetValues(typeof(MusicMode)))
            {
                foreach (MusicStem stem in Enum.GetValues(typeof(MusicStem)))
                {
                    bool expected = mode == MusicMode.Run && (stem == MusicStem.Arp || stem == MusicStem.Lead);
                    Assert.AreEqual(expected, MusicLayers.IsFaded(mode, stem), mode + " " + stem);
                }
            }
        }

        /// <summary>
        /// What the game loads: one clip per independently faded layer plus one Base clip holding every always-on
        /// layer (Menu 4 stems → 1 clip, Run 5 → 3).
        /// </summary>
        [Test]
        public void Music_PlaybackLayoutKeepsOnlyFadedLayersSeparate()
        {
            CollectionAssert.AreEqual(new[] { MusicStem.Base }, _bank.GetMusic(MusicMode.Menu).StemIds);
            CollectionAssert.AreEqual(new[] { MusicStem.Base, MusicStem.Arp, MusicStem.Lead }, _bank.GetMusic(MusicMode.Run).StemIds);
            CollectionAssert.AreEqual(new[] { MusicStem.Pads }, _bank.GetMusic(MusicMode.GameOver).StemIds);
            foreach (MusicMode mode in Arrangements)
            {
                MusicArrangement m = _bank.GetMusic(mode);
                foreach (MusicStem id in m.StemIds)
                {
                    Assert.IsTrue(id == MusicStem.Base || MusicLayers.IsFaded(mode, id) || m.StemCount == 1,
                        mode + " " + id + " is always on and should be pre-mixed into Base");
                }
            }
        }

        /// <summary>
        /// Null test of the pre-mix: Base is the sum of the separately rendered always-on layers (to float rounding) and
        /// the faded layers are untouched, so the mixer output is the same at every layer gain.
        /// </summary>
        [TestCase(MusicMode.Menu)]
        [TestCase(MusicMode.Run)]
        public void Music_BaseIsTheSumOfTheAlwaysOnLayers(MusicMode mode)
        {
            MusicArrangement play = _bank.GetMusic(mode);
            MusicArrangement sep = Separate(mode);
            Assert.AreEqual(sep.LoopFrames, play.LoopFrames);
            Assert.AreEqual(sep.SampleRate, play.SampleRate);
            Assert.AreEqual(sep.BeatsPerLoop, play.BeatsPerLoop);

            RenderedAudio mixed = play.GetStem(MusicStem.Base);
            var sum = new float[mixed.Samples.Length];
            int layers = 0;
            for (int k = 0; k < sep.StemCount; k++)
            {
                RenderedAudio stem = sep.Stems[k];
                if (MusicLayers.IsFaded(mode, sep.StemIds[k]))
                {
                    CollectionAssert.AreEqual(stem.Samples, play.GetStem(sep.StemIds[k]).Samples, stem.Name + " must be passed through");
                    continue;
                }

                layers++;
                for (int i = 0; i < sum.Length; i++)
                {
                    sum[i] += stem.Samples[i];
                }
            }

            Assert.GreaterOrEqual(layers, 2);
            float maxDiff = 0f;
            for (int i = 0; i < sum.Length; i++)
            {
                maxDiff = Math.Max(maxDiff, Math.Abs(sum[i] - mixed.Samples[i]));
            }

            Assert.LessOrEqual(maxDiff, 1e-6f, "Base differs from the sum of its layers");
        }

        [TestCaseSource(nameof(Arrangements))]
        public void Music_TimingMetadataIsConsistent(MusicMode mode)
        {
            MusicArrangement m = _bank.GetMusic(mode);
            Assert.AreEqual(0, m.BeatsPerLoop % m.BeatsPerBar);
            Assert.That(m.Bpm, Is.InRange(60.0, 140.0));
            Assert.That(m.LoopSeconds, Is.EqualTo(m.BeatsPerLoop * 60.0 / m.Bpm).Within(1e-9));
            Assert.That(m.BeatPhaseAt(0), Is.EqualTo(0.0).Within(1e-9));
            long oneAndAHalfBeats = (long)Math.Round(1.5 * m.LoopFrames / m.BeatsPerLoop);
            Assert.That(m.BeatPhaseAt(oneAndAHalfBeats), Is.EqualTo(0.5).Within(1e-3));
            Assert.That(m.BeatPhaseAt(m.LoopFrames), Is.EqualTo(0.0).Within(1e-9));
        }

        [Test]
        public void Music_RunTempoIsInTheSynthwaveRange()
        {
            Assert.That(_bank.GetMusic(MusicMode.Run).Bpm, Is.InRange(112.0, 118.0));
        }

        [TestCaseSource(nameof(Arrangements))]
        public void Music_FullMixHasHeadroom(MusicMode mode)
        {
            MusicArrangement m = _bank.GetMusic(mode);
            int n = m.LoopFrames * 2;
            float peak = 0f;
            for (int i = 0; i < n; i++)
            {
                float sum = 0f;
                foreach (RenderedAudio s in m.Stems)
                {
                    sum += s.Samples[i];
                }

                peak = Math.Max(peak, Math.Abs(sum));
            }

            Assert.LessOrEqual(peak, DspMath.DbToGain(-0.5f), mode + " full mix clips");
            Assert.Greater(peak, DspMath.DbToGain(-12f), mode + " full mix far too quiet");
        }

        [Test]
        public void Rendering_IsDeterministic()
        {
            RenderedAudio a = AudioBank.RenderSfx(SfxId.NearMiss, SampleRate);
            RenderedAudio b = AudioBank.RenderSfx(SfxId.NearMiss, SampleRate);
            CollectionAssert.AreEqual(a.Samples, b.Samples);
            CollectionAssert.AreEqual(_bank.GetSfx(SfxId.Crash).Samples, AudioBank.RenderSfx(SfxId.Crash, SampleRate).Samples);

            // Parallel (bank) and sequential renders of the same arrangement must match bit for bit.
            MusicArrangement seq = AudioBank.RenderMusic(MusicMode.GameOver, SampleRate, AudioBank.DefaultSeed, false);
            CollectionAssert.AreEqual(_bank.GetMusic(MusicMode.GameOver).Stems[0].Samples, seq.Stems[0].Samples);

            // Also with a pre-mixed Base stem (summed in song order whatever order the parallel stems finished in).
            MusicArrangement runSeq = AudioBank.RenderMusic(MusicMode.Run, SampleRate, AudioBank.DefaultSeed, false);
            MusicArrangement runPar = _bank.GetMusic(MusicMode.Run);
            CollectionAssert.AreEqual(runPar.StemIds, runSeq.StemIds);
            for (int i = 0; i < runPar.StemCount; i++)
            {
                CollectionAssert.AreEqual(runPar.Stems[i].Samples, runSeq.Stems[i].Samples, runPar.Stems[i].Name);
            }
        }

        [Test]
        public void Rendering_DifferentSeedsDiffer()
        {
            RenderedAudio a = AudioBank.RenderSfx(SfxId.Crash, SampleRate, 1);
            RenderedAudio b = AudioBank.RenderSfx(SfxId.Crash, SampleRate, 2);
            CollectionAssert.AreNotEqual(a.Samples, b.Samples);
        }

        [Test]
        public void Rendering_WorksAtLowerSampleRates()
        {
            RenderedAudio a = AudioBank.RenderSfx(SfxId.OverdriveStart, 32000);
            Assert.AreEqual(32000, a.SampleRate);
            AudioAsserts.Finite(a);
            Assert.LessOrEqual(a.Peak(), 1.0f);
            RenderedAudio loop = AudioBank.RenderLoop(LoopId.Engine, 22050);
            AudioAsserts.Seamless(loop);
        }

        /// <summary>
        /// The Unity side may pick 32 kHz (low-memory devices) and IL2CPP builds skip array bounds checks in the DSP
        /// loops, so every clip must render in range at other rates too (these tests run with bounds checks on).
        /// </summary>
        [TestCase(32000)]
        [TestCase(48000)]
        public void Rendering_WholeBankAtOtherSampleRates(int rate)
        {
            AudioBankData bank = AudioBank.RenderAll(rate, null, true);
            foreach (SfxId id in AllSfx())
            {
                RenderedAudio a = bank.GetSfx(id);
                Assert.AreEqual(rate, a.SampleRate);
                AudioAsserts.Finite(a);
                Assert.LessOrEqual(a.Peak(), 1f, id.ToString());
            }

            foreach (LoopId id in AllLoops())
            {
                AudioAsserts.Finite(bank.GetLoop(id));
                AudioAsserts.Seamless(bank.GetLoop(id));
            }

            foreach (MusicMode mode in Arrangements)
            {
                MusicArrangement m = bank.GetMusic(mode);
                Assert.AreEqual(rate, m.SampleRate);
                foreach (RenderedAudio stem in m.Stems)
                {
                    // Every stem of an arrangement: same rate and frame count, hence the same loop length in seconds
                    // (PlayScheduled starts them on one DSP sample and they must wrap together forever).
                    Assert.AreEqual(m.LoopFrames, stem.FrameCount, stem.Name);
                    Assert.AreEqual(rate, stem.SampleRate, stem.Name);
                    Assert.AreEqual(2, stem.Channels, stem.Name);
                    AudioAsserts.Finite(stem);
                    Assert.LessOrEqual(stem.Peak(), 1f, stem.Name);
                    AudioAsserts.Seamless(stem);
                }
            }
        }

        [Test]
        public void Rendering_StagedOrderAndProgress()
        {
            var kinds = new List<AudioBankItemKind>();
            float last = 0f;
            MusicMode first = MusicMode.Silent;
            foreach (AudioBankItem item in AudioBank.RenderStaged(22050, false))
            {
                Assert.GreaterOrEqual(item.Progress, last);
                last = item.Progress;
                if (item.Kind == AudioBankItemKind.Music && first == MusicMode.Silent)
                {
                    first = item.Music.Mode;
                }

                if (item.Kind == AudioBankItemKind.Music)
                {
                    foreach (RenderedAudio stem in item.Music.Stems)
                    {
                        Assert.AreEqual(22050, stem.SampleRate, stem.Name);
                        Assert.AreEqual(item.Music.LoopFrames, stem.FrameCount, stem.Name);
                    }
                }

                kinds.Add(item.Kind);
            }

            Assert.AreEqual(MusicMode.Menu, first, "menu music must be rendered first");
            Assert.AreEqual(1f, last);
            Assert.AreEqual(3 + AudioBank.SfxCount + AudioBank.LoopCount, kinds.Count);
        }

        private IEnumerable<RenderedAudio> AllClips()
        {
            foreach (SfxId id in AllSfx())
            {
                yield return _bank.GetSfx(id);
            }

            foreach (LoopId id in AllLoops())
            {
                yield return _bank.GetLoop(id);
            }

            foreach (MusicMode mode in Arrangements)
            {
                foreach (RenderedAudio stem in _bank.GetMusic(mode).Stems)
                {
                    yield return stem;
                }
            }
        }

        /// <summary>
        /// AudioClip.Create clips hold float PCM (4 bytes per sample): the whole bank at 44.1 kHz was 74.1 MiB with every
        /// music layer as its own clip and is 42.8 MiB with the always-on layers pre-mixed (32 kHz: ×0.73).
        /// </summary>
        [Test]
        public void Bank_FitsTheClipMemoryBudget()
        {
            long bytes = 0;
            long music = 0;
            foreach (RenderedAudio a in AllClips())
            {
                bytes += a.Samples.Length * 4L;
                if (a.IsLoop && a.Name.StartsWith("Music_", StringComparison.Ordinal))
                {
                    music += a.Samples.Length * 4L;
                }
            }

            Assert.Less(bytes / 1048576.0, 45.0, "bank clip memory (MiB)");
            Assert.Less(music / 1048576.0, 29.0, "music clip memory (MiB)");
        }

        /// <summary>A big clip whose channels are identical wastes half its memory (and should be looked at).</summary>
        [Test]
        public void Bank_NoLargeClipIsMonoContentStoredAsStereo()
        {
            foreach (RenderedAudio a in AllClips())
            {
                if (a.Samples.Length * 4L >= 512 * 1024)
                {
                    Assert.IsFalse(AudioAsserts.IsMonoContent(a), a.Name + " has identical channels");
                }
            }
        }

        [Test]
        public void Rendering_TimeBudgetSanity()
        {
            // Generous: the editor's Mono JIT and CI machines are slower than a release build. Measured in the
            // audio-preview tool the whole bank takes well under a second on a desktop core.
            Assert.Less(_renderMs, 30000.0, "bank render took " + _renderMs + " ms");
        }
    }
}
