using System;
using System.IO;
using System.Media;
using System.Threading.Tasks;

namespace PhotoBooth;

/// <summary>
/// Suara dibuat dengan kode (WAV 16-bit mono 44,1 kHz) sehingga tidak perlu berkas audio tambahan:
/// jepretan kamera (dua "klik" mekanis) dan bip hitung mundur.
/// </summary>
public static class SoundFx
{
    const int Rate = 44100;

    static readonly byte[] Shutter = BuildShutter();
    static readonly byte[] Tick = BuildTone(880, 0.09, 0.55);
    static readonly byte[] TickLast = BuildTone(1320, 0.18, 0.6);

    public static void PlayShutter() => Play(Shutter);
    public static void PlayTick(bool last = false) => Play(last ? TickLast : Tick);

    static void Play(byte[] wav)
    {
        // PlaySync di thread pool: aman dari penghentian dini oleh GC, tidak memblokir UI
        Task.Run(() =>
        {
            try
            {
                using var ms = new MemoryStream(wav);
                using var player = new SoundPlayer(ms);
                player.PlaySync();
            }
            catch { }
        });
    }

    static byte[] BuildTone(double freq, double seconds, double amp)
    {
        int n = (int)(Rate * seconds);
        var s = new short[n];
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)Rate;
            double env = Math.Min(1.0, Math.Min(t / 0.006, (seconds - t) / 0.03));   // fade in/out singkat
            s[i] = (short)(Math.Sin(2 * Math.PI * freq * t) * env * amp * short.MaxValue);
        }
        return ToWav(s);
    }

    /// <summary>Dua klik: bukaan rana lalu penutupan, masing-masing derau pendek yang meluruh + dentuman rendah.</summary>
    static byte[] BuildShutter()
    {
        double total = 0.20;
        int n = (int)(Rate * total);
        var buf = new double[n];
        var rnd = new Random(42);

        void Click(double start, double len, double tone, double gain)
        {
            int i0 = (int)(start * Rate), cnt = (int)(len * Rate);
            double lp = 0;
            for (int i = 0; i < cnt && i0 + i < n; i++)
            {
                double t = i / (double)Rate;
                double noise = rnd.NextDouble() * 2 - 1;
                lp += (noise - lp) * 0.35;                               // derau dilunakkan
                double thump = Math.Sin(2 * Math.PI * tone * t) * Math.Exp(-t / 0.012);
                buf[i0 + i] += (lp * Math.Exp(-t / 0.007) * 0.9 + thump * 0.6) * gain;
            }
        }

        Click(0.000, 0.060, 210, 1.0);
        Click(0.085, 0.080, 150, 0.8);

        var s = new short[n];
        for (int i = 0; i < n; i++)
            s[i] = (short)(Math.Clamp(buf[i] * 0.7, -1, 1) * short.MaxValue);
        return ToWav(s);
    }

    static byte[] ToWav(short[] samples)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        int dataLen = samples.Length * 2;
        bw.Write(new[] { 'R', 'I', 'F', 'F' });
        bw.Write(36 + dataLen);
        bw.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
        bw.Write(16);
        bw.Write((short)1);          // PCM
        bw.Write((short)1);          // mono
        bw.Write(Rate);
        bw.Write(Rate * 2);
        bw.Write((short)2);
        bw.Write((short)16);
        bw.Write(new[] { 'd', 'a', 't', 'a' });
        bw.Write(dataLen);
        foreach (var v in samples) bw.Write(v);
        bw.Flush();
        return ms.ToArray();
    }
}
