using System;

namespace BeamRunner.Core.Audio
{
    /// <summary>
    /// Procedural sound design for every <see cref="SfxId"/>. Each builder layers oscillators, noise, FM bells,
    /// booms and debris on an <see cref="SfxKit"/> canvas and returns how it should be levelled (loudness or peak);
    /// the kit then trims, levels and limits. Positive stingers are voiced in C major / A minor to sit with the music.
    /// </summary>
    internal static class SfxRenderer
    {
        public static RenderedAudio Render(SfxId id, int sampleRate, int seed)
        {
            int s;
            unchecked
            {
                s = seed * 7919 + ((int)id + 1) * 104729;
            }

            var k = new SfxKit(sampleRate, Length(id), s);
            SfxLevel level = Build(id, k);
            return k.Finish("Sfx_" + id, level);
        }

        /// <summary>Canvas length in seconds (the rendered clip is trimmed to its audible end).</summary>
        private static float Length(SfxId id)
        {
            switch (id)
            {
                case SfxId.UiClick:
                case SfxId.UiBack:
                case SfxId.Shot:
                case SfxId.BlockHit:
                    return 0.12f;
                case SfxId.ShotPing:
                    return 0.3f;
                case SfxId.BlockBreak:
                    return 0.6f;
                case SfxId.UiToggle:
                    return 0.16f;
                case SfxId.UiError:
                    return 0.34f;
                case SfxId.CountdownTick:
                    return 0.45f;
                case SfxId.GatePass:
                    return 0.25f;
                case SfxId.GemPickup:
                    return 0.7f;
                case SfxId.MultiplierUp:
                    return 0.6f;
                case SfxId.NearMiss:
                    return 0.9f;
                case SfxId.PowerUpPickup:
                case SfxId.MagnetStart:
                case SfxId.ShieldBreak:
                case SfxId.ShieldUp:
                    return 1.4f;
                case SfxId.CountdownGo:
                case SfxId.OverdriveEnd:
                case SfxId.SlipstreamEnter:
                case SfxId.Milestone:
                case SfxId.GateSmash:
                    return 1.7f;
                case SfxId.Purchase:
                case SfxId.MissionComplete:
                case SfxId.OverdriveReady:
                    return 2.0f;
                case SfxId.Crash:
                case SfxId.Reward:
                case SfxId.Revive:
                case SfxId.RankUp:
                    return 2.6f;
                case SfxId.OverdriveStart:
                case SfxId.ZoneChange:
                case SfxId.NewRecord:
                    return 3.0f;
                default:
                    return 2.0f;
            }
        }

        private static SfxLevel Build(SfxId id, SfxKit k)
        {
            switch (id)
            {
                case SfxId.UiClick: return UiClick(k);
                case SfxId.UiBack: return UiBack(k);
                case SfxId.UiToggle: return UiToggle(k);
                case SfxId.UiError: return UiError(k);
                case SfxId.Purchase: return Purchase(k);
                case SfxId.Reward: return Reward(k);
                case SfxId.CountdownTick: return CountdownTick(k);
                case SfxId.CountdownGo: return CountdownGo(k);
                case SfxId.GemPickup: return GemPickup(k);
                case SfxId.PowerUpPickup: return PowerUpPickup(k);
                case SfxId.NearMiss: return NearMiss(k);
                case SfxId.GatePass: return GatePass(k);
                case SfxId.GateSmash: return GateSmash(k);
                case SfxId.ShieldUp: return ShieldUp(k);
                case SfxId.ShieldBreak: return ShieldBreak(k);
                case SfxId.MagnetStart: return MagnetStart(k);
                case SfxId.OverdriveReady: return OverdriveReady(k);
                case SfxId.OverdriveStart: return OverdriveStart(k);
                case SfxId.OverdriveEnd: return OverdriveEnd(k);
                case SfxId.SlipstreamEnter: return SlipstreamEnter(k);
                case SfxId.ZoneChange: return ZoneChange(k);
                case SfxId.Milestone: return Milestone(k);
                case SfxId.MultiplierUp: return MultiplierUp(k);
                case SfxId.Crash: return Crash(k);
                case SfxId.Revive: return Revive(k);
                case SfxId.NewRecord: return NewRecord(k);
                case SfxId.MissionComplete: return MissionComplete(k);
                case SfxId.RankUp: return RankUp(k);
                case SfxId.Shot: return Shot(k);
                case SfxId.BlockHit: return BlockHit(k);
                case SfxId.BlockBreak: return BlockBreak(k);
                case SfxId.ShotPing: return ShotPing(k);
                default:
                    k.Tone(0f, 0.05f, 1000f, 1000f, Wave.Sine, 1f);
                    return SfxLevel.Peak(-12f);
            }
        }

        private static float Hz(int midi)
        {
            return DspMath.MidiToHz(midi);
        }

        private static SynthPatch Brass()
        {
            return new SynthPatch
            {
                Saw = 0.6f,
                Saw2 = 0.6f,
                Detune = 9f,
                Cutoff = 650f,
                KeyTrack = 0.4f,
                EnvOctaves = 2.6f,
                FilterDecay = 0.35f,
                Q = 1.0f,
                Attack = 0.01f,
                Decay = 0.9f,
                Sustain = 0.7f,
                Release = 0.28f,
                Drive = 1.1f,
            };
        }

        private static SynthPatch Stab()
        {
            return new SynthPatch
            {
                Saw = 0.6f,
                Saw2 = 0.6f,
                Pulse = 0.3f,
                PulseWidth = 0.4f,
                Detune = 14f,
                Cutoff = 450f,
                KeyTrack = 0.2f,
                EnvOctaves = 3.6f,
                FilterDecay = 0.55f,
                Q = 1.2f,
                Attack = 0.003f,
                Decay = 1.4f,
                Sustain = 0.35f,
                Release = 0.7f,
                Drive = 1.4f,
            };
        }

        // ------------------------------------------------------------------ UI

        private static SfxLevel UiClick(SfxKit k)
        {
            k.Tone(0f, 0.032f, 2300f, 1500f, Wave.Sine, 1f, 0f, 0.0006f, 50f);
            k.Tone(0f, 0.03f, 880f, 600f, Wave.Triangle, 0.35f, 0f, 0.0008f, 45f);
            k.NoiseLayer(0f, 0.007f, FilterMode.HighPass, 6000f, 6000f, 0.7f, 0.3f, EnvShape.Perc, 0.0003f, 40f, 0f, 0f, 0f);
            return SfxLevel.Peak(-6f);
        }

        private static SfxLevel Shot(SfxKit k)
        {
            // A thin laser zap: a falling square-wave chirp over a sine an octave up, with a breath of noise.
            k.Tone(0f, 0.07f, 1700f, 520f, Wave.Square, 0.45f, 0f, 0.0006f, 40f);
            k.Tone(0f, 0.05f, 3400f, 1100f, Wave.Sine, 0.25f, 0f, 0.0005f, 45f);
            k.NoiseLayer(0f, 0.012f, FilterMode.HighPass, 5000f, 5000f, 0.7f, 0.15f, EnvShape.Perc, 0.0003f, 40f, 0f, 0f, 0f);
            return SfxLevel.Peak(-9f);
        }

        private static SfxLevel BlockHit(SfxKit k)
        {
            // A dull, glassy knock.
            k.Tone(0f, 0.05f, 540f, 360f, Wave.Triangle, 1f, 0f, 0.0006f, 45f);
            k.NoiseLayer(0f, 0.03f, FilterMode.BandPass, 2600f, 1800f, 1.2f, 0.45f, EnvShape.Perc, 0.0003f, 40f, 0f, 0f, 0f);
            return SfxLevel.Peak(-8f);
        }

        private static SfxLevel BlockBreak(SfxKit k)
        {
            // A small GateSmash: thump, noise burst and glass shards.
            k.Boom(0f, 0.3f, 150f, 52f, 0.05f, 0.8f, 1.6f);
            k.NoiseLayer(0f, 0.22f, FilterMode.LowPass, 7000f, 900f, 0.9f, 0.6f, EnvShape.Perc, 0.0005f, 40f, -0.2f, 0.2f, 0.6f, false, 2f);
            k.Shatter(0.004f, 0.2f, 18, 2400f, 8500f, 0.03f, 0.15f, 0.16f);
            k.Reverb(0.5f, 0.6f, 6000f, 0.15f);
            return SfxLevel.Lufs(-15f, -2f);
        }

        private static SfxLevel ShotPing(SfxKit k)
        {
            // Metallic ricochet: two inharmonic sine partials with a slight downward bend.
            k.Tone(0f, 0.22f, 2950f, 2780f, Wave.Sine, 0.6f, 0f, 0.0004f, 18f);
            k.Tone(0f, 0.16f, 4420f, 4200f, Wave.Sine, 0.3f, 0f, 0.0004f, 22f);
            k.NoiseLayer(0f, 0.01f, FilterMode.HighPass, 6000f, 6000f, 0.7f, 0.25f, EnvShape.Perc, 0.0003f, 40f, 0f, 0f, 0f);
            return SfxLevel.Peak(-12f);
        }

        private static SfxLevel UiBack(SfxKit k)
        {
            k.Tone(0f, 0.05f, 1300f, 760f, Wave.Sine, 1f, 0f, 0.0008f, 50f);
            k.Tone(0f, 0.05f, 650f, 400f, Wave.Triangle, 0.4f, 0f, 0.001f, 45f);
            k.NoiseLayer(0f, 0.008f, FilterMode.HighPass, 4000f, 4000f, 0.7f, 0.22f, EnvShape.Perc, 0.0003f, 40f, 0f, 0f, 0f);
            return SfxLevel.Peak(-7f);
        }

        private static SfxLevel UiToggle(SfxKit k)
        {
            k.Tone(0f, 0.035f, 1320f, 1300f, Wave.Triangle, 0.85f, -0.15f, 0.0008f, 50f);
            k.Tone(0.045f, 0.05f, 1980f, 1960f, Wave.Triangle, 1f, 0.15f, 0.0008f, 50f);
            k.Tone(0.045f, 0.03f, 3960f, 3900f, Wave.Sine, 0.15f, 0.15f, 0.0005f, 50f);
            return SfxLevel.Peak(-7f);
        }

        private static SfxLevel UiError(SfxKit k)
        {
            for (int i = 0; i < 2; i++)
            {
                float t = i * 0.15f;
                k.Tone(t, 0.11f, 150f, 141f, Wave.Square, 0.8f, 0f, 0.004f, 14f, 850f, 0.9f);
                k.Tone(t, 0.11f, 153f, 144f, Wave.Saw, 0.5f, 0f, 0.004f, 14f, 700f, 0.7f);
            }

            return SfxLevel.Peak(-6f);
        }

        private static SfxLevel Purchase(SfxKit k)
        {
            // Coin blip (B6 → E7), then a C-major chime run with a sparkle wash.
            k.Tone(0f, 0.06f, 1975.5f, 1975.5f, Wave.Pulse25, 0.28f, 0f, 0.001f, 18f, 6000f);
            k.Tone(0.06f, 0.3f, 2637f, 2637f, Wave.Pulse25, 0.28f, 0f, 0.001f, 50f, 7000f);
            int[] notes = { 84, 88, 91, 96 };
            for (int i = 0; i < notes.Length; i++)
            {
                k.Bell(0.05f + i * 0.065f, Hz(notes[i]), 0.5f - i * 0.05f, -0.6f + i * 0.4f, 0.9f, 2f, 0.45f, 0.3f);
            }

            k.NoiseLayer(0.05f, 0.6f, FilterMode.HighPass, 7000f, 9500f, 0.7f, 0.08f, EnvShape.Bump, 0.01f, 30f, -0.5f, 0.5f, 1f);
            k.Delay(0.11f, 0.3f, 0.25f, 6000f);
            k.Reverb(0.9f, 1.4f, 7000f, 0.35f);
            return SfxLevel.Lufs(-13f);
        }

        private static SfxLevel Reward(SfxKit k)
        {
            var pad = new PadPatch
            {
                Voices = 3,
                Detune = 12f,
                Width = 0.8f,
                Cutoff = 3000f,
                Q = 0.8f,
                EnvOctaves = 1f,
                Attack = 0.04f,
                Decay = 1f,
                Sustain = 0.8f,
                Release = 0.9f,
            };
            k.Chord(0f, 0.55f, new[] { 72, 76, 79, 86 }, 0.35f, pad);

            int[] bells = { 84, 91, 96, 100 };
            for (int i = 0; i < bells.Length; i++)
            {
                k.Bell(i * 0.07f, Hz(bells[i]), 0.45f, -0.5f + i * 0.33f, 1.1f, 2f, 0.45f, 0.3f);
            }

            k.Shatter(0.2f, 0.9f, 14, 2500f, 7500f, 0.12f, 0.35f, 0.07f);
            k.Delay(0.12f, 0.35f, 0.25f, 6000f);
            k.Reverb(1.1f, 1.8f, 7000f, 0.35f);
            return SfxLevel.Lufs(-12.5f);
        }

        // ------------------------------------------------------------------ race start

        private static SfxLevel CountdownTick(SfxKit k)
        {
            k.Tone(0f, 0.15f, 880f, 880f, Wave.Square, 0.6f, 0f, 0.001f, 30f, 3200f, 0.8f);
            k.Tone(0f, 0.15f, 1760f, 1760f, Wave.Sine, 0.35f, 0f, 0.001f, 36f);
            k.Reverb(0.5f, 0.5f, 6000f, 0.15f);
            return SfxLevel.Lufs(-16f, -2f);
        }

        private static SfxLevel CountdownGo(SfxKit k)
        {
            k.Tone(0f, 0.05f, 900f, 1760f, Wave.Sine, 0.3f, 0f, 0.002f, 6f);
            k.Tone(0.02f, 0.62f, 1760f, 1760f, Wave.Square, 0.5f, 0f, 0.002f, 26f, 4200f, 0.8f, 6f, 10f);
            k.Tone(0.02f, 0.62f, 880f, 880f, Wave.Saw, 0.35f, 0f, 0.002f, 26f, 3000f, 0.8f, 6f, 10f);
            k.Tone(0.02f, 0.5f, 3520f, 3520f, Wave.Sine, 0.1f, 0f, 0.002f, 30f);
            k.Delay(0.13f, 0.3f, 0.28f, 5000f);
            k.Reverb(0.9f, 1.2f, 6000f, 0.25f);
            return SfxLevel.Lufs(-11f);
        }

        // ------------------------------------------------------------------ pickups

        private static SfxLevel GemPickup(SfxKit k)
        {
            // Bright A5 ping; the game pitches this up the A-minor pentatonic for chained pickups.
            k.Bell(0f, 880f, 0.8f, 0f, 0.42f, 2f, 0.35f, 0.35f);
            k.Tone(0f, 0.2f, 1760f, 1760f, Wave.Sine, 0.28f, 0f, 0.0005f, 60f);
            k.Tone(0f, 0.012f, 7000f, 5200f, Wave.Sine, 0.08f, 0f, 0.0003f, 30f);
            k.Delay(0.055f, 0.25f, 0.22f, 7000f, 600f);
            return SfxLevel.Lufs(-15f, -2f);
        }

        private static SfxLevel PowerUpPickup(SfxKit k)
        {
            k.SweepTone(0f, 0.24f, 330f, 1320f, Wave.Saw, 0.35f, 0f, 900f, 5000f, 2f, 10f, 0.004f, 8f);
            k.Tone(0f, 0.24f, 660f, 2640f, Wave.Sine, 0.25f, 0f, 0.004f, 8f);
            k.Bell(0.19f, Hz(81), 0.45f, -0.4f, 0.65f, 2f, 0.4f, 0.3f);
            k.Bell(0.215f, Hz(88), 0.4f, 0.4f, 0.65f, 2f, 0.4f, 0.3f);
            k.Bell(0.24f, Hz(93), 0.35f, 0f, 0.7f, 2f, 0.4f, 0.3f);
            k.NoiseLayer(0f, 0.3f, FilterMode.BandPass, 800f, 6000f, 1f, 0.18f, EnvShape.Swell, 0.002f, 60f, -0.3f, 0.3f, 1f);
            k.Reverb(1f, 1.2f, 7000f, 0.3f);
            return SfxLevel.Lufs(-13f);
        }

        // ------------------------------------------------------------------ racing

        private static SfxLevel NearMiss(SfxKit k)
        {
            // Signature fly-by: rising-then-falling band-passed whoosh panned left → right, a Doppler-falling
            // tone, a glassy zing and a low air thump at the pass point.
            k.Whoosh(0f, 0.46f, 650f, 5200f, 900f, 0.1f, 1.3f, 1f, -0.85f, 0.85f, 0.55f, false, 0.3f);
            k.Whoosh(0.02f, 0.3f, 2500f, 9000f, 3500f, 0.08f, 0.9f, 0.35f, -0.6f, 0.9f, 1f);
            k.Tone(0f, 0.36f, 1250f, 360f, Wave.Triangle, 0.2f, 0f, 0.035f, 40f, 2800f, 0.8f, 0f, 0f, 0.3f);
            k.Tone(0.015f, 0.22f, 3400f, 2100f, Wave.Sine, 0.07f, 0.3f, 0.02f, 50f);
            k.Tone(0.06f, 0.18f, 120f, 58f, Wave.Sine, 0.4f, 0f, 0.012f, 40f);
            k.Reverb(0.6f, 0.7f, 6000f, 0.12f);
            return SfxLevel.Lufs(-12.5f, -1f, 3f);
        }

        private static SfxLevel GatePass(SfxKit k)
        {
            k.Whoosh(0f, 0.14f, 900f, 2600f, 1400f, 0.04f, 1f, 1f, -0.25f, 0.25f, 0.5f, true, 0.2f);
            return SfxLevel.Lufs(-28f, -8f);
        }

        private static SfxLevel GateSmash(SfxKit k)
        {
            k.Boom(0f, 0.55f, 95f, 38f, 0.12f, 0.9f, 2.2f);
            k.NoiseLayer(0f, 0.35f, FilterMode.LowPass, 9000f, 1200f, 0.9f, 0.7f, EnvShape.Perc, 0.0005f, 50f, -0.2f, 0.2f, 0.8f, false, 2.5f);
            k.NoiseLayer(0f, 0.06f, FilterMode.HighPass, 3000f, 3000f, 0.7f, 0.5f, EnvShape.Perc, 0.0003f, 30f, 0f, 0f, 1f);
            k.Shatter(0.005f, 0.35f, 34, 2500f, 9500f, 0.04f, 0.22f, 0.13f);
            float[] metal = { 1150f, 1737f, 2415f, 3310f };
            for (int i = 0; i < metal.Length; i++)
            {
                k.Tone(0f, 0.6f, metal[i], metal[i] * 0.995f, Wave.Sine, 0.07f, (i & 1) == 0 ? -0.4f : 0.4f, 0.0005f, 60f);
            }

            k.Crackle(0.05f, 0.6f, 30, 1500f, 7000f, 0.25f);
            k.Reverb(0.8f, 1f, 6000f, 0.2f);
            return SfxLevel.Lufs(-11f, -1f, 3f);
        }

        private static SfxLevel ShieldUp(SfxKit k)
        {
            k.SweepTone(0f, 0.55f, 220f, 440f, Wave.Saw, 0.5f, 0f, 400f, 4000f, 3f, 12f, 0.02f, 20f, 0f, 0f, 0.35f);
            k.Tone(0f, 0.5f, 440f, 880f, Wave.Sine, 0.2f, 0f, 0.02f, 20f, 0f, 0.7f, 0f, 0f, 0.35f);
            k.Bell(0.3f, Hz(88), 0.3f, -0.3f, 0.7f, 2f, 0.35f, 0.3f);
            k.Bell(0.36f, Hz(93), 0.28f, 0.3f, 0.8f, 2f, 0.35f, 0.3f);
            k.Tone(0.3f, 0.6f, 1760f, 1760f, Wave.Sine, 0.06f, 0f, 0.05f, 30f, 0f, 0.7f, 18f, 30f);
            k.Chorus(12f, 4f, 0.8f, 0.5f);
            k.Reverb(1f, 1.2f, 6000f, 0.3f);
            return SfxLevel.Lufs(-13f);
        }

        private static SfxLevel ShieldBreak(SfxKit k)
        {
            k.NoiseLayer(0f, 0.04f, FilterMode.HighPass, 2500f, 2500f, 0.7f, 0.7f, EnvShape.Perc, 0.0003f, 30f, 0f, 0f, 1f);
            k.Shatter(0f, 0.28f, 26, 3000f, 10000f, 0.03f, 0.18f, 0.14f);
            k.SweepTone(0f, 0.45f, 950f, 170f, Wave.Square, 0.3f, 0f, 3500f, 500f, 1.5f, 15f, 0.002f, 30f);
            k.Boom(0f, 0.25f, 90f, 42f, 0.08f, 0.6f, 1.5f);
            k.Reverb(0.8f, 1f, 6500f, 0.22f);
            return SfxLevel.Lufs(-12f, -1f, 3f);
        }

        private static SfxLevel MagnetStart(SfxKit k)
        {
            k.SweepTone(0f, 0.65f, 110f, 220f, Wave.Saw, 0.45f, 0f, 300f, 1600f, 4f, 10f, 0.03f, 30f, 9f, 1.2f);
            k.Tone(0f, 0.6f, 440f, 880f, Wave.Sine, 0.22f, 0f, 0.03f, 30f, 0f, 0.7f, 14f, 60f);
            k.Bell(0.45f, Hz(88), 0.2f, 0.3f, 0.45f, 2f, 0.3f, 0.3f);
            k.Reverb(0.8f, 0.9f, 6000f, 0.2f);
            return SfxLevel.Lufs(-14f, -1f, 1f);
        }

        private static SfxLevel OverdriveReady(SfxKit k)
        {
            k.NoiseLayer(0f, 0.4f, FilterMode.BandPass, 2000f, 7000f, 1.2f, 0.2f, EnvShape.Swell, 0.002f, 60f, -0.5f, 0.5f, 1f);
            int[] notes = { 88, 93, 96, 100 };
            for (int i = 0; i < notes.Length; i++)
            {
                k.Bell(0.3f + i * 0.075f, Hz(notes[i]), 0.35f - i * 0.03f, -0.6f + i * 0.4f, 0.9f, 2f, 0.35f, 0.3f);
            }

            k.Tone(0.3f, 1f, Hz(81), Hz(81), Wave.Triangle, 0.12f, 0f, 0.05f, 30f, 0f, 0.7f, 5f, 8f);
            k.Delay(0.16f, 0.4f, 0.35f, 5000f);
            k.Reverb(1.2f, 2f, 7000f, 0.4f);
            return SfxLevel.Lufs(-15f, -2f);
        }

        private static SfxLevel OverdriveStart(SfxKit k)
        {
            const float Hit = 0.36f;

            // Riser.
            k.NoiseLayer(0f, Hit, FilterMode.HighPass, 800f, 7000f, 0.8f, 0.45f, EnvShape.Swell, 0.002f, 60f, -0.4f, 0.4f, 0.8f);
            k.SweepTone(0f, Hit, 180f, 720f, Wave.Saw, 0.3f, 0f, 400f, 5000f, 2f, 12f, Hit - 0.01f, 0f);

            // Impact: punch, air burst, detuned Am stab and a long sub drop.
            k.Boom(Hit, 0.6f, 140f, 45f, 0.09f, 0.9f, 2.2f);
            k.Tone(Hit, 1.4f, 78f, 28f, Wave.Sine, 0.55f, 0f, 0.005f, 40f, 0f, 0.7f, 0f, 0f, 1.2f);
            k.NoiseLayer(Hit, 0.5f, FilterMode.LowPass, 7000f, 700f, 0.8f, 0.6f, EnvShape.Perc, 0.001f, 50f, 0f, 0f, 1f, false, 1.5f);
            SynthPatch stab = Stab();
            int[] chord = { 45, 52, 57, 60, 64 };
            for (int i = 0; i < chord.Length; i++)
            {
                k.Synth(Hit, 0.28f, Hz(chord[i]), 0.2f, -0.5f + i * 0.25f, stab);
            }

            k.Reverb(1.4f, 2f, 5000f, 0.3f);
            return SfxLevel.Lufs(-10f, -0.5f, 4f);
        }

        private static SfxLevel OverdriveEnd(SfxKit k)
        {
            int[] chord = { 57, 64, 69 };
            for (int i = 0; i < chord.Length; i++)
            {
                float f = Hz(chord[i]);
                k.SweepTone(0f, 0.8f, f, f * 0.4f, Wave.Saw, 0.38f - i * 0.04f, -0.3f + i * 0.3f, 3500f, 250f, 1.5f, 12f, 0.003f, 18f);
            }

            k.NoiseLayer(0f, 0.6f, FilterMode.LowPass, 5000f, 300f, 0.7f, 0.2f, EnvShape.Perc, 0.002f, 30f, 0f, 0f, 0.8f);
            k.Boom(0f, 0.3f, 80f, 40f, 0.2f, 0.3f, 1f);
            k.Reverb(1f, 1.2f, 5000f, 0.2f);
            return SfxLevel.Lufs(-15f, -1f, 2f);
        }

        private static SfxLevel SlipstreamEnter(SfxKit k)
        {
            k.Whoosh(0f, 1f, 500f, 3200f, 1500f, 0.4f, 1.4f, 0.9f, -0.5f, 0.5f, 1f, true, 0.2f);
            k.Tone(0.15f, 0.8f, Hz(88), Hz(88), Wave.Sine, 0.08f, -0.3f, 0.3f, 30f);
            k.Tone(0.2f, 0.75f, Hz(95), Hz(95), Wave.Sine, 0.06f, 0.3f, 0.3f, 30f);
            k.Reverb(1.2f, 1.6f, 7000f, 0.3f);
            return SfxLevel.Lufs(-16f, -2f);
        }

        private static SfxLevel ZoneChange(SfxKit k)
        {
            const float Hit = 0.6f;
            k.NoiseLayer(0f, Hit, FilterMode.HighPass, 500f, 9000f, 0.9f, 0.5f, EnvShape.Swell, 0.002f, 60f, -0.8f, 0.8f, 0.8f);
            k.SweepTone(0f, Hit, 110f, 440f, Wave.Saw, 0.2f, 0f, 300f, 3000f, 2f, 15f, Hit - 0.05f, 0f);
            SynthPatch stab = Stab();
            int[] chord = { 57, 60, 64, 67, 71 };
            for (int i = 0; i < chord.Length; i++)
            {
                k.Synth(Hit, 0.3f, Hz(chord[i]), 0.2f, -0.6f + i * 0.3f, stab);
            }

            k.Boom(Hit, 0.6f, 90f, 40f, 0.1f, 0.5f, 1.5f);
            k.NoiseLayer(Hit, 0.25f, FilterMode.LowPass, 6000f, 1500f, 0.7f, 0.3f, EnvShape.Perc, 0.001f, 50f, 0f, 0f, 1f);
            k.Delay(0.19f, 0.4f, 0.3f, 4500f);
            k.Reverb(1.5f, 2.6f, 5500f, 0.4f);
            return SfxLevel.Lufs(-12.5f, -1f, 2f);
        }

        private static SfxLevel Milestone(SfxKit k)
        {
            k.Bell(0f, Hz(88), 0.55f, -0.2f, 0.8f, 2f, 0.45f, 0.3f);
            k.Bell(0.1f, Hz(93), 0.6f, 0.2f, 1f, 2f, 0.45f, 0.3f);
            k.Tone(0.1f, 0.5f, Hz(81), Hz(81), Wave.Triangle, 0.12f, 0f, 0.01f, 40f);
            k.Shatter(0.1f, 0.4f, 8, 4000f, 9000f, 0.05f, 0.2f, 0.06f);
            k.Delay(0.12f, 0.3f, 0.25f, 6000f);
            k.Reverb(1f, 1.3f, 7000f, 0.3f);
            return SfxLevel.Lufs(-14f, -2f);
        }

        private static SfxLevel MultiplierUp(SfxKit k)
        {
            k.Tone(0f, 0.14f, 520f, 1040f, Wave.Pulse25, 0.45f, 0f, 0.002f, 12f, 5000f, 0.7f, 0f, 0f, 0.1f);
            k.Tone(0f, 0.14f, 1040f, 2080f, Wave.Sine, 0.25f, 0f, 0.002f, 12f, 0f, 0.7f, 0f, 0f, 0.1f);
            k.Tone(0.1f, 0.2f, 1040f, 1040f, Wave.Sine, 0.3f, 0f, 0.001f, 50f);
            k.Delay(0.09f, 0.25f, 0.2f, 6000f);
            return SfxLevel.Lufs(-17f, -2f);
        }

        private static SfxLevel Crash(SfxKit k)
        {
            k.NoiseLayer(0f, 0.9f, FilterMode.LowPass, 9000f, 350f, 0.8f, 0.9f, EnvShape.Perc, 0.0005f, 55f, 0f, 0f, 1f, false, 2.5f);
            k.NoiseLayer(0f, 0.05f, FilterMode.HighPass, 2000f, 2000f, 0.7f, 0.6f, EnvShape.Perc, 0.0003f, 30f, 0f, 0f, 1f);
            k.Boom(0f, 1.3f, 150f, 28f, 0.9f, 1f, 4f, 1500f);
            k.Crackle(0.12f, 1.3f, 90, 800f, 6000f, 0.35f);
            k.Shatter(0.05f, 0.9f, 12, 2000f, 6500f, 0.08f, 0.3f, 0.07f);
            k.Reverb(0.9f, 1.3f, 5000f, 0.2f);
            return SfxLevel.Lufs(-8.5f, -0.3f, 4f);
        }

        private static SfxLevel Revive(SfxKit k)
        {
            const float Hit = 0.7f;
            var swell = new PadPatch
            {
                Voices = 3,
                Detune = 14f,
                Width = 0.9f,
                Cutoff = 2300f,
                Q = 0.8f,
                EnvOctaves = 1.5f,
                Attack = Hit,
                Decay = 1f,
                Sustain = 1f,
                Release = 0.02f,
            };
            k.Chord(0f, Hit, new[] { 69, 72, 76, 81 }, 0.35f, swell);

            k.NoiseLayer(0f, Hit, FilterMode.HighPass, 1500f, 6000f, 0.8f, 0.3f, EnvShape.Swell, 0.002f, 60f, -0.5f, 0.5f, 1f);
            int[] bells = { 81, 88, 93, 96 };
            for (int i = 0; i < bells.Length; i++)
            {
                k.Bell(Hit + i * 0.05f, Hz(bells[i]), 0.4f - i * 0.04f, -0.45f + i * 0.3f, 1.1f, 2f, 0.4f, 0.3f);
            }

            k.Boom(Hit, 0.4f, 110f, 50f, 0.1f, 0.4f, 1.2f);
            k.Delay(0.14f, 0.35f, 0.25f, 6000f);
            k.Reverb(1.3f, 2f, 7000f, 0.35f);
            return SfxLevel.Lufs(-12f);
        }

        private static SfxLevel NewRecord(SfxKit k)
        {
            SynthPatch brass = Brass();
            int[] arp = { 72, 76, 79, 84 };
            for (int i = 0; i < arp.Length; i++)
            {
                k.Synth(i * 0.1f, 0.09f, Hz(arp[i]), 0.35f, -0.3f + i * 0.2f, brass);
            }

            const float Hit = 0.42f;
            int[] chord = { 60, 67, 72, 76, 79, 84 };
            for (int i = 0; i < chord.Length; i++)
            {
                k.Synth(Hit, 1f, Hz(chord[i]), 0.2f, -0.5f + i * 0.2f, brass);
            }

            k.Bell(Hit, Hz(96), 0.3f, 0.3f, 1.2f, 2f, 0.4f, 0.3f);
            k.Bell(Hit + 0.08f, Hz(100), 0.25f, -0.3f, 1.2f, 2f, 0.4f, 0.3f);
            Voices.Snare(k.Buffer, k.Frame(Hit), 0.35f, k.SampleRate, k.Rng.NextUInt());
            k.Boom(Hit, 0.4f, 100f, 45f, 0.1f, 0.4f, 1.2f);
            k.Delay(0.2f, 0.3f, 0.2f, 5000f);
            k.Reverb(1.3f, 2.2f, 6000f, 0.35f);
            return SfxLevel.Lufs(-12f, -1f, 2f);
        }

        private static SfxLevel MissionComplete(SfxKit k)
        {
            k.Bell(0f, Hz(79), 0.5f, -0.3f, 1f, 2f, 0.45f, 0.3f);
            k.Bell(0.13f, Hz(84), 0.5f, 0.1f, 1.1f, 2f, 0.45f, 0.3f);
            k.Bell(0.13f, Hz(88), 0.35f, 0.35f, 1.1f, 2f, 0.45f, 0.3f);
            k.Tone(0.13f, 0.8f, Hz(72), Hz(72), Wave.Triangle, 0.12f, 0f, 0.01f, 40f);
            k.NoiseLayer(0.1f, 0.5f, FilterMode.HighPass, 6000f, 9000f, 0.7f, 0.05f, EnvShape.Bump, 0.002f, 60f, -0.4f, 0.4f, 1f);
            k.Reverb(1.1f, 1.6f, 7000f, 0.3f);
            return SfxLevel.Lufs(-14f, -2f);
        }

        private static SfxLevel RankUp(SfxKit k)
        {
            SynthPatch brass = Brass();
            int[] arp = { 72, 76, 79, 84, 88, 91 };
            for (int i = 0; i < arp.Length; i++)
            {
                k.Synth(i * 0.055f, 0.07f, Hz(arp[i]), 0.3f, -0.5f + i * 0.2f, brass);
            }

            const float Hit = 0.36f;
            int[] chord = { 72, 76, 79, 84 };
            for (int i = 0; i < chord.Length; i++)
            {
                k.Synth(Hit, 0.6f, Hz(chord[i]), 0.2f, -0.3f + i * 0.2f, brass);
            }

            k.Bell(Hit, Hz(96), 0.3f, -0.3f, 1.1f, 2f, 0.4f, 0.3f);
            k.Bell(Hit + 0.06f, Hz(103), 0.25f, 0.3f, 1.1f, 2f, 0.4f, 0.3f);
            k.NoiseLayer(0f, Hit, FilterMode.HighPass, 1000f, 8000f, 0.8f, 0.25f, EnvShape.Swell, 0.002f, 60f, -0.5f, 0.5f, 1f);
            k.Delay(0.15f, 0.35f, 0.25f, 5500f);
            k.Reverb(1.3f, 2f, 6500f, 0.35f);
            return SfxLevel.Lufs(-12.5f, -1f, 1f);
        }
    }
}
