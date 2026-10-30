using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using BeamRunner.Core.Audio;

// BeamRunner audio preview: renders the procedural bank with the exact Unity Core/Audio sources and writes
// 16-bit WAVs + a level table (peak dBFS, RMS dBFS, approx. integrated LUFS, render time, loop seam jump).
//
//   dotnet run -c Release --project tools/audio-preview/Preview [-- outDir [sampleRate]]
internal static class Program
{
    // Outside the repository: the WAVs are large and never committed.
    private static readonly string DefaultOut = Path.Combine(Path.GetTempPath(), "beamrunner-audio-preview");

    private static int Main(string[] args)
    {
        string outDir = args.Length > 0 ? args[0] : DefaultOut;
        int sr = args.Length > 1 ? int.Parse(args[1]) : AudioBank.DefaultSampleRate;
        Directory.CreateDirectory(outDir);

        // Timings: one cold run (includes JIT, like first boot on device), then warm sequential and parallel runs.
        var sw = Stopwatch.StartNew();
        AudioBankData data = AudioBank.RenderAll(sr, null, parallel: false);
        double cold = sw.Elapsed.TotalMilliseconds;

        // Warm runs: per-item minimum over 3 sequential passes (the sandbox CPU is shared and noisy).
        var timeOf = new Dictionary<string, double>();
        double warmSeq = double.MaxValue;
        for (int pass = 0; pass < 3; pass++)
        {
            sw.Restart();
            double last = 0;
            foreach (AudioBankItem item in AudioBank.RenderStaged(sr, parallel: false))
            {
                double now = sw.Elapsed.TotalMilliseconds;
                string name = item.Kind == AudioBankItemKind.Music ? "Music " + item.Music.Mode
                    : item.Kind == AudioBankItemKind.Sfx ? "Sfx " + item.Sfx : "Loop " + item.Loop;
                timeOf[name] = Math.Min(timeOf.TryGetValue(name, out double prev) ? prev : double.MaxValue, now - last);
                last = now;
            }

            warmSeq = Math.Min(warmSeq, sw.Elapsed.TotalMilliseconds);
        }

        double warmPar = double.MaxValue;
        double menuPar = double.MaxValue;
        double essentialsPar = double.MaxValue;
        for (int pass = 0; pass < 3; pass++)
        {
            sw.Restart();
            foreach (AudioBankItem item in AudioBank.RenderStaged(sr, parallel: true))
            {
                if (item.Kind == AudioBankItemKind.Music && item.Music.Mode == MusicMode.Menu)
                {
                    menuPar = Math.Min(menuPar, sw.Elapsed.TotalMilliseconds);
                }

                if (item.Kind == AudioBankItemKind.Loop && (int)item.Loop == AudioBank.LoopCount - 1)
                {
                    essentialsPar = Math.Min(essentialsPar, sw.Elapsed.TotalMilliseconds);
                }
            }

            warmPar = Math.Min(warmPar, sw.Elapsed.TotalMilliseconds);
        }

        Console.WriteLine($"Sample rate {sr} Hz, cores {Environment.ProcessorCount}");
        Console.WriteLine($"Render total: cold(seq, incl. JIT) {cold:F0} ms | warm sequential {warmSeq:F0} ms | warm parallel {warmPar:F0} ms");
        Console.WriteLine($"Parallel staged: menu music ready {menuPar:F0} ms, essentials (menu+SFX+loops) {essentialsPar:F0} ms, all {warmPar:F0} ms");
        Console.WriteLine();

        Console.WriteLine($"Sum of per-item minima (sequential): {timeOf.Values.Sum():F0} ms");
        Console.WriteLine();
        Console.WriteLine($"{"Name",-34} {"Dur s",7} {"Ch",3} {"Peak dB",8} {"RMS dB",8} {"LUFS",7} {"MaxM",6} {"ms",7} {"Seam",8}");
        Console.WriteLine(new string('-', 90));

        long clipSamples = 0;
        foreach (MusicMode mode in new[] { MusicMode.Menu, MusicMode.Run, MusicMode.GameOver })
        {
            // Per-layer rows (what calibrate.py balances). The game loads the playback layout instead, in which the
            // always-on layers are pre-mixed into one Base stem (same mix, fewer clips): counted in the memory total.
            MusicArrangement arr = AudioBank.RenderMusic(mode, sr, AudioBank.DefaultSeed, true, true);
            foreach (RenderedAudio clip in data.GetMusic(mode).Stems)
            {
                clipSamples += clip.Samples.Length;
            }

            double ms = timeOf.TryGetValue("Music " + mode, out double t) ? t : 0;
            float[] mix = MixStems(arr, 2);
            Row($"MIX {mode} ({arr.Bpm:F2} BPM, {arr.BeatsPerLoop} beats)", mix, 2, sr, ms, double.NaN);
            WriteWav(Path.Combine(outDir, $"music_{mode}_mix.wav"), mix, 2, sr);
            foreach (RenderedAudio stem in arr.Stems)
            {
                Row("  " + stem.Name, stem.Samples, stem.Channels, sr, double.NaN, Seam(stem));
                WriteWav(Path.Combine(outDir, stem.Name + ".wav"), Repeat(stem.Samples, 2), stem.Channels, sr);
            }
        }

        Console.WriteLine();
        for (int i = 0; i < AudioBank.SfxCount; i++)
        {
            RenderedAudio a = data.GetSfx((SfxId)i);
            clipSamples += a.Samples.Length;
            Row(a.Name, a.Samples, a.Channels, sr, timeOf["Sfx " + (SfxId)i], double.NaN);
            WriteWav(Path.Combine(outDir, a.Name + ".wav"), a.Samples, a.Channels, sr);
        }

        Console.WriteLine();
        for (int i = 0; i < AudioBank.LoopCount; i++)
        {
            RenderedAudio a = data.GetLoop((LoopId)i);
            clipSamples += a.Samples.Length;
            Row(a.Name, a.Samples, a.Channels, sr, timeOf["Loop " + (LoopId)i], Seam(a));
            WriteWav(Path.Combine(outDir, a.Name + ".wav"), Repeat(a.Samples, 4), a.Channels, sr);
        }

        Console.WriteLine();
        Console.WriteLine($"AudioClip memory as the game loads it (float PCM): {clipSamples * 4 / 1048576.0:F1} MiB");
        Console.WriteLine($"WAVs written to {outDir}");
        return 0;
    }

    private static float[] MixStems(MusicArrangement arr, int loops)
    {
        int n = arr.LoopFrames * 2;
        var mix = new float[n * loops];
        foreach (RenderedAudio s in arr.Stems)
        {
            for (int l = 0; l < loops; l++)
            {
                for (int i = 0; i < n; i++)
                {
                    mix[l * n + i] += s.Samples[i];
                }
            }
        }

        return mix;
    }

    private static float[] Repeat(float[] s, int times)
    {
        var r = new float[s.Length * times];
        for (int t = 0; t < times; t++)
        {
            Array.Copy(s, 0, r, t * s.Length, s.Length);
        }

        return r;
    }

    /// <summary>Loop seam jump relative to the 99.9th percentile of adjacent-sample steps (≤ ~1 is seamless).</summary>
    private static double Seam(RenderedAudio a)
    {
        int ch = a.Channels;
        int frames = a.FrameCount;
        var steps = new List<float>(frames);
        for (int i = 1; i < frames; i++)
        {
            for (int c = 0; c < ch; c++)
            {
                steps.Add(Math.Abs(a.Samples[i * ch + c] - a.Samples[(i - 1) * ch + c]));
            }
        }

        steps.Sort();
        float p999 = steps[(int)(steps.Count * 0.999)];
        float seam = 0f;
        for (int c = 0; c < ch; c++)
        {
            seam = Math.Max(seam, Math.Abs(a.Samples[c] - a.Samples[(frames - 1) * ch + c]));
        }

        return seam / Math.Max(1e-9, p999);
    }

    private static void Row(string name, float[] s, int ch, int sr, double ms, double seam)
    {
        double peak = 0, sum = 0;
        foreach (float v in s)
        {
            peak = Math.Max(peak, Math.Abs(v));
            sum += v * (double)v;
        }

        double rms = Math.Sqrt(sum / s.Length);
        double lufs = Lufs(s, ch, sr, out double maxM);
        string msStr = double.IsNaN(ms) ? "" : ms.ToString("F1");
        string seamStr = double.IsNaN(seam) ? "" : seam.ToString("F2");
        Console.WriteLine($"{name,-34} {s.Length / (double)ch / sr,7:F2} {ch,3} {Db(peak),8:F1} {Db(rms),8:F1} {lufs,7:F1} {maxM,6:F1} {msStr,7} {seamStr,8}");
    }

    private static double Db(double g)
    {
        return g <= 1e-12 ? -240 : 20 * Math.Log10(g);
    }

    /// <summary>ITU-R BS.1770 integrated loudness (K-weighting, 400 ms blocks, abs/rel gating).</summary>
    private static double Lufs(float[] s, int ch, int sr, out double maxMomentary)
    {
        int frames = s.Length / ch;
        var weighted = new double[ch][];
        for (int c = 0; c < ch; c++)
        {
            var x = new double[frames];
            for (int i = 0; i < frames; i++)
            {
                x[i] = s[i * ch + c];
            }

            x = Shelf(x, sr);
            x = HighPass(x, sr);
            weighted[c] = x;
        }

        int block = (int)(0.4 * sr);
        int hop = block / 4;
        if (frames < block)
        {
            // Short clip: treat it as one 400 ms window (zero-padded), like a meter would.
            double sumShort = 0;
            for (int c = 0; c < ch; c++)
            {
                double sum = 0;
                for (int i = 0; i < frames; i++)
                {
                    sum += weighted[c][i] * weighted[c][i];
                }

                sumShort += sum / block;
            }

            double l = -0.691 + 10 * Math.Log10(sumShort * (ch == 1 ? 2 : 1) + 1e-20);
            maxMomentary = l;
            return l;
        }

        var z = new List<double>();
        for (int start = 0; start + block <= frames; start += hop)
        {
            double sumAll = 0;
            for (int c = 0; c < ch; c++)
            {
                double sum = 0;
                for (int i = start; i < start + block; i++)
                {
                    sum += weighted[c][i] * weighted[c][i];
                }

                sumAll += sum / block;
            }

            if (ch == 1)
            {
                sumAll *= 2; // mono played on both speakers
            }

            z.Add(sumAll);
        }

        maxMomentary = z.Count == 0 ? -70 : -0.691 + 10 * Math.Log10(z.Max() + 1e-20);
        var abs = z.Where(v => -0.691 + 10 * Math.Log10(v + 1e-20) > -70).ToList();
        if (abs.Count == 0)
        {
            return -70;
        }

        double rel = -0.691 + 10 * Math.Log10(abs.Average()) - 10;
        var gated = abs.Where(v => -0.691 + 10 * Math.Log10(v) > rel).ToList();
        return -0.691 + 10 * Math.Log10(gated.Average());
    }

    private static double[] Shelf(double[] x, int sr)
    {
        double g = 4.0, q = 1 / Math.Sqrt(2), fc = 1500.0;
        double a = Math.Pow(10, g / 40), w = 2 * Math.PI * fc / sr, al = Math.Sin(w) / (2 * q), c = Math.Cos(w);
        double b0 = a * ((a + 1) + (a - 1) * c + 2 * Math.Sqrt(a) * al);
        double b1 = -2 * a * ((a - 1) + (a + 1) * c);
        double b2 = a * ((a + 1) + (a - 1) * c - 2 * Math.Sqrt(a) * al);
        double a0 = (a + 1) - (a - 1) * c + 2 * Math.Sqrt(a) * al;
        double a1 = 2 * ((a - 1) - (a + 1) * c);
        double a2 = (a + 1) - (a - 1) * c - 2 * Math.Sqrt(a) * al;
        return Filter(x, b0 / a0, b1 / a0, b2 / a0, a1 / a0, a2 / a0);
    }

    private static double[] HighPass(double[] x, int sr)
    {
        double q = 0.5, fc = 38.0;
        double w = 2 * Math.PI * fc / sr, al = Math.Sin(w) / (2 * q), c = Math.Cos(w);
        double a0 = 1 + al;
        return Filter(x, (1 + c) / 2 / a0, -(1 + c) / a0, (1 + c) / 2 / a0, -2 * c / a0, (1 - al) / a0);
    }

    private static double[] Filter(double[] x, double b0, double b1, double b2, double a1, double a2)
    {
        var y = new double[x.Length];
        double z1 = 0, z2 = 0;
        for (int i = 0; i < x.Length; i++)
        {
            double v = b0 * x[i] + z1;
            z1 = b1 * x[i] - a1 * v + z2;
            z2 = b2 * x[i] - a2 * v;
            y[i] = v;
        }

        return y;
    }

    private static void WriteWav(string path, float[] samples, int channels, int sr)
    {
        using var fs = new FileStream(path, FileMode.Create);
        using var w = new BinaryWriter(fs);
        int dataBytes = samples.Length * 2;
        w.Write("RIFF"u8.ToArray());
        w.Write(36 + dataBytes);
        w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray());
        w.Write(16);
        w.Write((short)1);
        w.Write((short)channels);
        w.Write(sr);
        w.Write(sr * channels * 2);
        w.Write((short)(channels * 2));
        w.Write((short)16);
        w.Write("data"u8.ToArray());
        w.Write(dataBytes);
        var rnd = new Random(1);
        foreach (float f in samples)
        {
            double d = f * 32767.0 + (rnd.NextDouble() - rnd.NextDouble()); // TPDF dither
            w.Write((short)Math.Max(-32768, Math.Min(32767, Math.Round(d))));
        }
    }
}
