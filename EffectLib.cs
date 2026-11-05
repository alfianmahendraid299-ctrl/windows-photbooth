using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using OpenCvSharp;

namespace PhotoBooth;

public enum EffectKind
{
    // Halaman 1: filter klasik
    Normal, BlackWhite, Sepia, Comic, Thermal, XRay, PopArt, Pencil, Painting,
    // Halaman 2: filter tambahan
    Neon, Cartoon, Vintage, Glitch, Pixel, Kaleido, Duotone, NightVision, Dreamy,
    // Halaman 3: AR aksesori & hewan
    Dog, Cat, Bunny, Nerd, Shades, Crown, PartyHat, Mustache, HeartEyes,
    // Halaman 4: AR wajah (warp & makeup)
    BigEyes, BigNose, Slim, Alien, Beauty, Lipstick, Glam, Freckles, Portrait,
    // Halaman 5: AR karakter
    Devil, Angel, Clown, FlowerCrown, Pirate, Santa, Cowboy, Rainbow, Sparkle,
    // Halaman 6: filter lensa (distorsi & gaya, tanpa wajah)
    FishEye, Swirl, Ripple, Funhouse, MirrorHalf, Matrix, Vhs, Halftone, Anaglyph,
    // Halaman 7: AR aksi & emosi
    Hulk, LaserEyes, Fire, Bats, Butterflies, LoveRain, Dizzy, Googly, Pinocchio,
    // Halaman 8: AR spesial
    Lightning, Frozen, Vampire, Zombie, Terminator, FireBreath, Visor, Ninja, Astronaut,
}

public sealed record EffectDef(EffectKind Kind, string Name, bool NeedsFace);

public static class EffectLib
{
    public const int PerPage = 9;

    public static readonly string[] PageTitles =
    {
        "Filter", "Filter Seni", "AR · Hewan & Aksesori", "AR · Wajah", "AR · Karakter",
        "Filter Lensa", "AR · Aksi & Emosi", "AR · Spesial",
    };

    public static readonly EffectDef[] All =
    {
        new(EffectKind.Normal, "Normal", false), new(EffectKind.BlackWhite, "Hitam Putih", false),
        new(EffectKind.Sepia, "Sepia", false), new(EffectKind.Comic, "Komik", false),
        new(EffectKind.Thermal, "Termal", false), new(EffectKind.XRay, "Sinar-X", false),
        new(EffectKind.PopArt, "Pop Art", false), new(EffectKind.Pencil, "Pensil", false),
        new(EffectKind.Painting, "Lukisan", false),

        new(EffectKind.Neon, "Neon", false), new(EffectKind.Cartoon, "Kartun", false),
        new(EffectKind.Vintage, "Vintage", false), new(EffectKind.Glitch, "Glitch", false),
        new(EffectKind.Pixel, "Piksel", false), new(EffectKind.Kaleido, "Kaleidoskop", false),
        new(EffectKind.Duotone, "Duotone", false), new(EffectKind.NightVision, "Night Vision", false),
        new(EffectKind.Dreamy, "Mimpi", false),

        new(EffectKind.Dog, "Anjing", true), new(EffectKind.Cat, "Kucing", true),
        new(EffectKind.Bunny, "Kelinci", true), new(EffectKind.Nerd, "Kacamata Bulat", true),
        new(EffectKind.Shades, "Kacamata Hitam", true), new(EffectKind.Crown, "Mahkota", true),
        new(EffectKind.PartyHat, "Topi Pesta", true), new(EffectKind.Mustache, "Kumis", true),
        new(EffectKind.HeartEyes, "Mata Hati", true),

        new(EffectKind.BigEyes, "Mata Besar", true), new(EffectKind.BigNose, "Hidung Besar", true),
        new(EffectKind.Slim, "Wajah Tirus", true), new(EffectKind.Alien, "Alien", true),
        new(EffectKind.Beauty, "Cantik", true), new(EffectKind.Lipstick, "Lipstik", true),
        new(EffectKind.Glam, "Glam", true), new(EffectKind.Freckles, "Bintik", true),
        new(EffectKind.Portrait, "Potret", true),

        new(EffectKind.Devil, "Setan", true), new(EffectKind.Angel, "Malaikat", true),
        new(EffectKind.Clown, "Badut", true), new(EffectKind.FlowerCrown, "Mahkota Bunga", true),
        new(EffectKind.Pirate, "Bajak Laut", true), new(EffectKind.Santa, "Sinterklas", true),
        new(EffectKind.Cowboy, "Koboi", true), new(EffectKind.Rainbow, "Pelangi", true),
        new(EffectKind.Sparkle, "Berkilau", true),

        new(EffectKind.FishEye, "Fish Eye", false), new(EffectKind.Swirl, "Pusaran", false),
        new(EffectKind.Ripple, "Riak Air", false), new(EffectKind.Funhouse, "Kaca Lucu", false),
        new(EffectKind.MirrorHalf, "Cermin", false), new(EffectKind.Matrix, "Matrix", false),
        new(EffectKind.Vhs, "VHS", false), new(EffectKind.Halftone, "Halftone", false),
        new(EffectKind.Anaglyph, "3D Merah-Biru", false),

        new(EffectKind.Hulk, "Hulk Marah", true), new(EffectKind.LaserEyes, "Mata Laser", true),
        new(EffectKind.Fire, "Api", true), new(EffectKind.Bats, "Kelelawar", true),
        new(EffectKind.Butterflies, "Kupu-kupu", true), new(EffectKind.LoveRain, "Hujan Hati", true),
        new(EffectKind.Dizzy, "Pusing", true), new(EffectKind.Googly, "Mata Goyang", true),
        new(EffectKind.Pinocchio, "Pinokio", true),

        new(EffectKind.Lightning, "Petir", true), new(EffectKind.Frozen, "Beku", true),
        new(EffectKind.Vampire, "Drakula", true), new(EffectKind.Zombie, "Zombi", true),
        new(EffectKind.Terminator, "Terminator", true), new(EffectKind.FireBreath, "Napas Api", true),
        new(EffectKind.Visor, "Visor Cyber", true), new(EffectKind.Ninja, "Ninja", true),
        new(EffectKind.Astronaut, "Astronot", true),
    };

    static readonly Dictionary<EffectKind, EffectDef> ByKind = All.ToDictionary(e => e.Kind);

    public static int PageCount => (All.Length + PerPage - 1) / PerPage;
    public static EffectDef Get(EffectKind k) => ByKind[k];
    public static EffectDef[] Page(int page) => All.Skip(page * PerPage).Take(PerPage).ToArray();
    public static bool PageHasFace(int page) => Page(page).Any(e => e.NeedsFace);

    /// <summary>Pintu masuk tunggal: filter biasa atau efek AR. Mengembalikan Mat baru (wajib Dispose).</summary>
    public static Mat Render(Mat src, EffectKind kind, FaceData? face)
        => Get(kind).NeedsFace ? ArEffects.Apply(src, kind, face) : Apply(src, kind);

    // ------------------------------------------------------------------ bahan bersama

    static readonly Mat SepiaKernel = Mat.FromArray(new float[,]
    {
        { 0.131f, 0.534f, 0.272f },
        { 0.168f, 0.686f, 0.349f },
        { 0.189f, 0.769f, 0.393f },
    });

    static readonly Mat Poster4 = BuildPosterLut(4);
    static readonly Mat Poster8 = BuildPosterLut(8);
    static readonly Mat DuoLut = BuildDuoLut();

    static Mat BuildPosterLut(int levels)
    {
        var lut = new byte[256];
        int step = 256 / levels;
        for (int i = 0; i < 256; i++)
            lut[i] = (byte)Math.Min(255, (i / step) * step + step / 2);
        return Mat.FromArray(lut);
    }

    // gradasi ungu tua -> oranye pastel (urutan BGR)
    static Mat BuildDuoLut()
    {
        var data = new byte[256 * 3];
        double[] c0 = { 95, 25, 55 }, c1 = { 120, 175, 255 };
        for (int i = 0; i < 256; i++)
        {
            double t = i / 255.0;
            for (int k = 0; k < 3; k++) data[i * 3 + k] = (byte)(c0[k] + (c1[k] - c0[k]) * t);
        }
        var m = new Mat(256, 1, MatType.CV_8UC3);
        Marshal.Copy(data, 0, m.Data, data.Length);
        return m;
    }

    static readonly Dictionary<(int, int), Mat> Vignettes = new();

    static Mat Vignette(int w, int h)
    {
        if (Vignettes.TryGetValue((w, h), out var cached)) return cached;
        if (Vignettes.Count > 6) { foreach (var v in Vignettes.Values) v.Dispose(); Vignettes.Clear(); }

        var data = new byte[w * h * 3];
        double cx = (w - 1) / 2.0, cy = (h - 1) / 2.0, maxD = Math.Sqrt(cx * cx + cy * cy);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double d = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / maxD;
                double v = Math.Clamp(1.0 - 0.80 * d * d, 0.22, 1.0);
                byte b = (byte)(v * 255);
                int i = (y * w + x) * 3;
                data[i] = b; data[i + 1] = b; data[i + 2] = b;
            }
        var m = new Mat(h, w, MatType.CV_8UC3);
        Marshal.Copy(data, 0, m.Data, data.Length);
        Vignettes[(w, h)] = m;
        return m;
    }

    static byte[] noiseBuf = Array.Empty<byte>();

    /// <summary>Butiran film: derau seragam ditambahkan (img berubah di tempat).</summary>
    static void AddGrain(Mat img, double strength)
    {
        int len = img.Rows * img.Cols;
        if (noiseBuf.Length != len) noiseBuf = new byte[len];
        Random.Shared.NextBytes(noiseBuf);
        using var n = new Mat(img.Rows, img.Cols, MatType.CV_8UC1);
        Marshal.Copy(noiseBuf, 0, n.Data, len);

        if (img.Channels() == 3)
        {
            using var n3 = new Mat();
            Cv2.Merge(new[] { n, n, n }, n3);
            Cv2.AddWeighted(img, 1.0, n3, strength, -128 * strength, img);
        }
        else
        {
            Cv2.AddWeighted(img, 1.0, n, strength, -128 * strength, img);
        }
    }

    static Mat ShiftX(Mat ch, int dx)
    {
        var o = new Mat(ch.Rows, ch.Cols, ch.Type(), Scalar.All(0));
        int cw = ch.Cols - Math.Abs(dx);
        if (cw <= 0) return o;
        using var from = new Mat(ch, new Rect(dx < 0 ? -dx : 0, 0, cw, ch.Rows));
        using var to = new Mat(o, new Rect(dx > 0 ? dx : 0, 0, cw, ch.Rows));
        from.CopyTo(to);
        return o;
    }


    static double Now => System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;

    static readonly Dictionary<(int, int), Mat> ScanMasks = new();

    /// <summary>Garis pindai TV: baris genap terang, baris ganjil sedikit gelap.</summary>
    static Mat ScanMask(int w, int h)
    {
        if (ScanMasks.TryGetValue((w, h), out var cached)) return cached;
        if (ScanMasks.Count > 6) { foreach (var v in ScanMasks.Values) v.Dispose(); ScanMasks.Clear(); }
        var data = new byte[w * h * 3];
        for (int y = 0; y < h; y++)
        {
            byte b = (y % 3 == 0) ? (byte)205 : (byte)255;
            Array.Fill(data, b, y * w * 3, w * 3);
        }
        var m = new Mat(h, w, MatType.CV_8UC3);
        Marshal.Copy(data, 0, m.Data, data.Length);
        ScanMasks[(w, h)] = m;
        return m;
    }

    static readonly Dictionary<(EffectKind, int, int), (Mat X, Mat Y)> MapCache = new();

    /// <summary>
    /// Distorsi seluruh frame: peta sumber dihitung di resolusi 1/4 lalu diperbesar (hemat), kemudian Remap.
    /// cacheKey != null berarti peta tidak berubah terhadap waktu dan disimpan.
    /// </summary>
    static void WarpWith(Mat src, Mat dst, Func<float, float, (float X, float Y)> map, EffectKind? cacheKey)
    {
        int w = src.Cols, h = src.Rows;
        if (cacheKey != null && MapCache.TryGetValue((cacheKey.Value, w, h), out var cached))
        {
            Cv2.Remap(src, dst, cached.X, cached.Y, InterpolationFlags.Linear, BorderTypes.Replicate);
            return;
        }

        int cw = Math.Max(8, w / 4), ch = Math.Max(8, h / 4);
        var mx = new float[cw * ch];
        var my = new float[cw * ch];
        for (int y = 0; y < ch; y++)
            for (int x = 0; x < cw; x++)
            {
                float px = x * (w - 1f) / (cw - 1f), py = y * (h - 1f) / (ch - 1f);
                var (sx, sy) = map(px, py);
                int i = y * cw + x;
                mx[i] = sx;
                my[i] = sy;
            }

        using var cX = new Mat(ch, cw, MatType.CV_32FC1);
        using var cY = new Mat(ch, cw, MatType.CV_32FC1);
        Marshal.Copy(mx, 0, cX.Data, mx.Length);
        Marshal.Copy(my, 0, cY.Data, my.Length);

        var fx = new Mat();
        var fy = new Mat();
        Cv2.Resize(cX, fx, new Size(w, h), 0, 0, InterpolationFlags.Linear);
        Cv2.Resize(cY, fy, new Size(w, h), 0, 0, InterpolationFlags.Linear);
        Cv2.Remap(src, dst, fx, fy, InterpolationFlags.Linear, BorderTypes.Replicate);

        if (cacheKey != null)
        {
            if (MapCache.Count > 8)
            {
                foreach (var v in MapCache.Values) { v.X.Dispose(); v.Y.Dispose(); }
                MapCache.Clear();
            }
            MapCache[(cacheKey.Value, w, h)] = (fx, fy);
        }
        else
        {
            fx.Dispose();
            fy.Dispose();
        }
    }

    static Mat? matrixTex;
    static bool matrixTried;
    static readonly Dictionary<int, Mat> MatrixTall = new();

    /// <summary>Tekstur hujan kode, diskalakan ke lebar frame dan disambung vertikal agar bisa digulir tanpa putus.</summary>
    static Mat? MatrixLayer(int w)
    {
        if (MatrixTall.TryGetValue(w, out var cached)) return cached;
        if (!matrixTried)
        {
            matrixTried = true;
            try
            {
                var path = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Stickers", "matrix.png");
                if (System.IO.File.Exists(path))
                    matrixTex = Cv2.ImDecode(System.IO.File.ReadAllBytes(path), ImreadModes.Color);
            }
            catch { matrixTex = null; }
        }
        if (matrixTex == null || matrixTex.Empty()) return null;

        if (MatrixTall.Count > 4) { foreach (var v in MatrixTall.Values) v.Dispose(); MatrixTall.Clear(); }
        int ht = Math.Max(8, (int)(matrixTex.Rows * (w / (double)matrixTex.Cols)));
        using var scaled = new Mat();
        Cv2.Resize(matrixTex, scaled, new Size(w, ht));
        var tall = new Mat();
        Cv2.VConcat(scaled, scaled, tall);
        MatrixTall[w] = tall;
        return tall;
    }

    // ------------------------------------------------------------------ filter (tanpa wajah)

    /// <summary>Semua filter menerima &amp; mengembalikan Mat BGR 8-bit dengan ukuran sama.</summary>
    public static Mat Apply(Mat src, EffectKind kind)
    {
        var dst = new Mat();
        switch (kind)
        {
            case EffectKind.BlackWhite:
            {
                using var g = new Mat();
                Cv2.CvtColor(src, g, ColorConversionCodes.BGR2GRAY);
                Cv2.CvtColor(g, dst, ColorConversionCodes.GRAY2BGR);
                break;
            }

            case EffectKind.Sepia:
                Cv2.Transform(src, dst, SepiaKernel);
                break;

            case EffectKind.Comic:
            {
                using var gray = new Mat();
                Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);
                Cv2.MedianBlur(gray, gray, 7);
                using var edges = new Mat();
                Cv2.AdaptiveThreshold(gray, edges, 255, AdaptiveThresholdTypes.MeanC,
                                      ThresholdTypes.Binary, 9, 6);
                using var edges3 = new Mat();
                Cv2.CvtColor(edges, edges3, ColorConversionCodes.GRAY2BGR);
                using var smooth = new Mat();
                Cv2.BilateralFilter(src, smooth, 9, 70, 70);
                Cv2.BitwiseAnd(smooth, edges3, dst);
                break;
            }

            case EffectKind.Thermal:
            {
                using var g = new Mat();
                Cv2.CvtColor(src, g, ColorConversionCodes.BGR2GRAY);
                Cv2.ApplyColorMap(g, dst, ColormapTypes.Jet);
                break;
            }

            case EffectKind.XRay:
            {
                using var g = new Mat();
                Cv2.CvtColor(src, g, ColorConversionCodes.BGR2GRAY);
                Cv2.BitwiseNot(g, g);
                Cv2.ApplyColorMap(g, dst, ColormapTypes.Bone);
                break;
            }

            case EffectKind.PopArt:
            {
                using var poster = new Mat();
                Cv2.LUT(src, Poster4, poster);
                Cv2.ConvertScaleAbs(poster, dst, 1.25, -15);
                break;
            }

            case EffectKind.Pencil:
            {
                using var gray = new Mat();
                using var color = new Mat();
                Cv2.PencilSketch(src, gray, color, 60, 0.07f, 0.03f);
                Cv2.CvtColor(gray, dst, ColorConversionCodes.GRAY2BGR);
                break;
            }

            case EffectKind.Painting:
            {
                if (src.Width > 640)
                {
                    using var half = new Mat();
                    Cv2.Resize(src, half, new Size(src.Width / 2, src.Height / 2));
                    using var styl = new Mat();
                    Cv2.Stylization(half, styl, 60, 0.45f);
                    Cv2.Resize(styl, dst, new Size(src.Width, src.Height));
                }
                else
                {
                    Cv2.Stylization(src, dst, 60, 0.45f);
                }
                break;
            }

            case EffectKind.Neon:
            {
                using var g = new Mat();
                Cv2.CvtColor(src, g, ColorConversionCodes.BGR2GRAY);
                Cv2.GaussianBlur(g, g, new Size(5, 5), 0);
                using var edges = new Mat();
                Cv2.Canny(g, edges, 50, 130);
                using var glow = new Mat();
                Cv2.GaussianBlur(edges, glow, new Size(0, 0), Math.Max(1.5, src.Width / 320.0));
                using var neon = new Mat();
                Cv2.AddWeighted(edges, 1.0, glow, 1.6, 0, neon);
                using var b = new Mat();
                using var gg = new Mat();
                using var r = new Mat();
                neon.ConvertTo(b, MatType.CV_8UC1, 1.0);
                neon.ConvertTo(gg, MatType.CV_8UC1, 0.35);
                neon.ConvertTo(r, MatType.CV_8UC1, 0.9);
                using var neon3 = new Mat();
                Cv2.Merge(new[] { b, gg, r }, neon3);
                using var dark = new Mat();
                src.ConvertTo(dark, MatType.CV_8UC3, 0.22);
                Cv2.Add(dark, neon3, dst);
                break;
            }

            case EffectKind.Cartoon:
            {
                using var half = new Mat();
                Cv2.Resize(src, half, new Size(Math.Max(8, src.Width / 2), Math.Max(8, src.Height / 2)));
                using var s1 = new Mat();
                Cv2.BilateralFilter(half, s1, 7, 50, 7);
                using var s2 = new Mat();
                Cv2.BilateralFilter(s1, s2, 7, 50, 7);
                using var up = new Mat();
                Cv2.Resize(s2, up, new Size(src.Width, src.Height));
                using var q = new Mat();
                Cv2.LUT(up, Poster8, q);

                using var g = new Mat();
                Cv2.CvtColor(src, g, ColorConversionCodes.BGR2GRAY);
                Cv2.GaussianBlur(g, g, new Size(3, 3), 0);
                using var edges = new Mat();
                Cv2.Canny(g, edges, 60, 140);
                using var k = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(2, 2));
                Cv2.Dilate(edges, edges, k);
                Cv2.BitwiseNot(edges, edges);
                using var mask3 = new Mat();
                Cv2.CvtColor(edges, mask3, ColorConversionCodes.GRAY2BGR);
                Cv2.BitwiseAnd(q, mask3, dst);
                break;
            }

            case EffectKind.Vintage:
            {
                using var sep = new Mat();
                Cv2.Transform(src, sep, SepiaKernel);
                using var mix = new Mat();
                Cv2.AddWeighted(src, 0.35, sep, 0.65, 0, mix);
                Cv2.ConvertScaleAbs(mix, mix, 0.92, 14);
                Cv2.Multiply(mix, Vignette(src.Width, src.Height), dst, 1.0 / 255.0);
                AddGrain(dst, 0.14);
                break;
            }

            case EffectKind.Glitch:
            {
                int w = src.Width, h = src.Height;
                int dx = Math.Max(2, w / 90);
                Cv2.Split(src, out Mat[] ch);
                using (var b2 = ShiftX(ch[0], -dx))
                using (var r2 = ShiftX(ch[2], dx))
                    Cv2.Merge(new[] { b2, ch[1], r2 }, dst);
                foreach (var m in ch) m.Dispose();

                var rnd = Random.Shared;
                for (int i = 0; i < 3; i++)
                {
                    if (rnd.Next(3) == 0) continue;
                    int bh = rnd.Next(Math.Max(2, h / 40), Math.Max(3, h / 12));
                    int by = rnd.Next(0, Math.Max(1, h - bh));
                    int off = rnd.Next(-w / 10, w / 10);
                    if (Math.Abs(off) < 4) continue;

                    int cw = w - Math.Abs(off);
                    using var band = new Mat(dst, new Rect(0, by, w, bh)).Clone();
                    using var from = new Mat(band, new Rect(off < 0 ? -off : 0, 0, cw, bh));
                    using var to = new Mat(dst, new Rect(off > 0 ? off : 0, by, cw, bh));
                    from.CopyTo(to);
                }
                break;
            }

            case EffectKind.Pixel:
            {
                int bs = Math.Max(3, src.Width / 90);
                using var small = new Mat();
                Cv2.Resize(src, small, new Size(Math.Max(4, src.Width / bs), Math.Max(4, src.Height / bs)),
                           0, 0, InterpolationFlags.Area);
                Cv2.Resize(small, dst, new Size(src.Width, src.Height), 0, 0, InterpolationFlags.Nearest);
                break;
            }

            case EffectKind.Kaleido:
            {
                src.CopyTo(dst);
                int hw = src.Width / 2, hh = src.Height / 2;
                using var q = new Mat(src, new Rect(0, 0, hw, hh));
                using var fx = new Mat();
                using var fy = new Mat();
                using var fxy = new Mat();
                Cv2.Flip(q, fx, FlipMode.Y);
                Cv2.Flip(q, fy, FlipMode.X);
                Cv2.Flip(q, fxy, FlipMode.XY);
                using (var r1 = new Mat(dst, new Rect(hw, 0, hw, hh))) fx.CopyTo(r1);
                using (var r2 = new Mat(dst, new Rect(0, hh, hw, hh))) fy.CopyTo(r2);
                using (var r3 = new Mat(dst, new Rect(hw, hh, hw, hh))) fxy.CopyTo(r3);
                break;
            }

            case EffectKind.Duotone:
            {
                using var g = new Mat();
                Cv2.CvtColor(src, g, ColorConversionCodes.BGR2GRAY);
                using var g3 = new Mat();
                Cv2.CvtColor(g, g3, ColorConversionCodes.GRAY2BGR);
                Cv2.LUT(g3, DuoLut, dst);
                break;
            }

            case EffectKind.NightVision:
            {
                using var g = new Mat();
                Cv2.CvtColor(src, g, ColorConversionCodes.BGR2GRAY);
                Cv2.ConvertScaleAbs(g, g, 1.7, 25);
                AddGrain(g, 0.30);
                using var b = new Mat();
                using var r = new Mat();
                g.ConvertTo(b, MatType.CV_8UC1, 0.25);
                g.ConvertTo(r, MatType.CV_8UC1, 0.35);
                using var green = new Mat();
                Cv2.Merge(new[] { b, g, r }, green);
                Cv2.Multiply(green, Vignette(src.Width, src.Height), dst, 1.0 / 255.0);
                break;
            }

            case EffectKind.Dreamy:
            {
                using var small = new Mat();
                Cv2.Resize(src, small, new Size(Math.Max(8, src.Width / 4), Math.Max(8, src.Height / 4)));
                Cv2.GaussianBlur(small, small, new Size(0, 0), 5);
                using var blur = new Mat();
                Cv2.Resize(small, blur, new Size(src.Width, src.Height));
                Cv2.AddWeighted(src, 0.78, blur, 0.45, 12, dst);
                break;
            }


            case EffectKind.FishEye:
                WarpWith(src, dst, (px, py) =>
                {
                    float cx = (src.Cols - 1) / 2f, cy = (src.Rows - 1) / 2f;
                    float nx = (px - cx) / cx, ny = (py - cy) / cy;
                    float r = MathF.Sqrt(nx * nx + ny * ny) / 1.41421356f;
                    if (r < 1e-4f) return (px, py);
                    float sc = MathF.Pow(r, 0.30f);
                    return (cx + (px - cx) * sc, cy + (py - cy) * sc);
                }, EffectKind.FishEye);
                break;

            case EffectKind.Swirl:
            {
                double t = Now;
                float amp = (float)(2.4 * Math.Sin(t * 0.9));
                WarpWith(src, dst, (px, py) =>
                {
                    float cx = (src.Cols - 1) / 2f, cy = (src.Rows - 1) / 2f;
                    float dx = px - cx, dy = py - cy;
                    float rad = Math.Min(src.Cols, src.Rows) * 0.55f;
                    float r = MathF.Sqrt(dx * dx + dy * dy);
                    if (r >= rad) return (px, py);
                    float k = 1f - r / rad;
                    float a = amp * k * k;
                    float c = MathF.Cos(a), s = MathF.Sin(a);
                    return (cx + dx * c - dy * s, cy + dx * s + dy * c);
                }, null);
                break;
            }

            case EffectKind.Ripple:
            {
                double t = Now;
                WarpWith(src, dst, (px, py) =>
                {
                    float cx = (src.Cols - 1) / 2f, cy = (src.Rows - 1) / 2f;
                    float dx = px - cx, dy = py - cy;
                    float r = MathF.Sqrt(dx * dx + dy * dy);
                    if (r < 1e-3f) return (px, py);
                    float amp = src.Rows * 0.012f;
                    float wave = MathF.Sin(r / (src.Rows * 0.05f) - (float)t * 6f) * amp * MathF.Exp(-r / (src.Rows * 0.9f));
                    return (px + dx / r * wave, py + dy / r * wave);
                }, null);
                break;
            }

            case EffectKind.Funhouse:
            {
                double t = Now;
                WarpWith(src, dst, (px, py) =>
                    (px + MathF.Sin(py / (src.Rows * 0.12f) + (float)t * 2f) * src.Cols * 0.015f,
                     py + MathF.Sin(px / (src.Cols * 0.25f) + (float)t * 1.3f) * src.Rows * 0.010f), null);
                break;
            }

            case EffectKind.MirrorHalf:
            {
                src.CopyTo(dst);
                int hw = src.Width / 2;
                using var left = new Mat(src, new Rect(0, 0, hw, src.Height));
                using var flipped = new Mat();
                Cv2.Flip(left, flipped, FlipMode.Y);
                using var to = new Mat(dst, new Rect(src.Width - hw, 0, hw, src.Height));
                flipped.CopyTo(to);
                break;
            }

            case EffectKind.Matrix:
            {
                using var g = new Mat();
                Cv2.CvtColor(src, g, ColorConversionCodes.BGR2GRAY);
                using var b = new Mat();
                using var gg = new Mat();
                using var r = new Mat();
                g.ConvertTo(b, MatType.CV_8UC1, 0.10);
                g.ConvertTo(gg, MatType.CV_8UC1, 0.50);
                g.ConvertTo(r, MatType.CV_8UC1, 0.12);
                using var dark = new Mat();
                Cv2.Merge(new[] { b, gg, r }, dark);

                var tall = MatrixLayer(src.Width);
                int ht = tall == null ? 0 : tall.Rows / 2;
                if (tall != null && ht >= src.Height)
                {
                    int y0 = (int)(Now * src.Height * 0.45) % ht;
                    using var roi = new Mat(tall, new Rect(0, y0, src.Width, src.Height));
                    Cv2.Add(dark, roi, dst);
                }
                else
                {
                    dark.CopyTo(dst);
                }
                break;
            }

            case EffectKind.Vhs:
            {
                int w = src.Width, h = src.Height;
                double t = Now;
                int dx = Math.Max(2, w / 180);
                Cv2.Split(src, out Mat[] ch);
                using (var b2 = ShiftX(ch[0], -dx))
                using (var r2 = ShiftX(ch[2], dx))
                    Cv2.Merge(new[] { b2, ch[1], r2 }, dst);
                foreach (var m in ch) m.Dispose();

                Cv2.Multiply(dst, ScanMask(w, h), dst, 1.0 / 255.0);

                int bh = Math.Max(6, h / 14);
                int by = (int)((t * 0.22 % 1.0) * (h - bh));
                int off = (int)(w * 0.03 * Math.Sin(t * 20) + w * 0.02);
                if (Math.Abs(off) > 3)
                {
                    int cw = w - Math.Abs(off);
                    using var band = new Mat(dst, new Rect(0, by, w, bh)).Clone();
                    using var from = new Mat(band, new Rect(off < 0 ? -off : 0, 0, cw, bh));
                    using var to = new Mat(dst, new Rect(off > 0 ? off : 0, by, cw, bh));
                    from.CopyTo(to);
                }
                AddGrain(dst, 0.12);

                double fs = Math.Max(0.45, w / 1000.0);
                int th = Math.Max(1, w / 500);
                Cv2.PutText(dst, "PLAY >", new Point((int)(w * 0.05), (int)(h * 0.12)),
                            HersheyFonts.HersheySimplex, fs * 1.6, new Scalar(255, 255, 255), th, LineTypes.AntiAlias);
                var ts = TimeSpan.FromSeconds(t % 3600);
                Cv2.PutText(dst, $"{ts.Minutes:00}:{ts.Seconds:00}", new Point((int)(w * 0.80), (int)(h * 0.93)),
                            HersheyFonts.HersheySimplex, fs * 1.3, new Scalar(255, 255, 255), th, LineTypes.AntiAlias);
                break;
            }

            case EffectKind.Halftone:
            {
                int w = src.Width, h = src.Height;
                int cell = Math.Max(5, w / 110);
                int cols = Math.Max(1, w / cell), rows = Math.Max(1, h / cell);
                using var small = new Mat();
                Cv2.Resize(src, small, new Size(cols, rows), 0, 0, InterpolationFlags.Area);
                var px = new byte[cols * rows * 3];
                Marshal.Copy(small.Data, px, 0, px.Length);

                dst.Dispose();
                dst = new Mat(h, w, MatType.CV_8UC3, new Scalar(245, 245, 245));
                for (int r = 0; r < rows; r++)
                    for (int c = 0; c < cols; c++)
                    {
                        int i = (r * cols + c) * 3;
                        double lum = (0.114 * px[i] + 0.587 * px[i + 1] + 0.299 * px[i + 2]) / 255.0;
                        int rad = (int)(cell * 0.62 * Math.Sqrt(1.0 - lum));
                        if (rad < 1) continue;
                        Cv2.Circle(dst, new Point(c * cell + cell / 2, r * cell + cell / 2), rad,
                                   new Scalar(Math.Min(255, px[i] * 1.1), Math.Min(255, px[i + 1] * 1.1), Math.Min(255, px[i + 2] * 1.1)),
                                   -1, LineTypes.Link8);
                    }
                break;
            }

            case EffectKind.Anaglyph:
            {
                int dx = Math.Max(3, src.Width / 80);
                Cv2.Split(src, out Mat[] ch);
                using (var b2 = ShiftX(ch[0], dx))
                using (var g2 = ShiftX(ch[1], dx))
                using (var r2 = ShiftX(ch[2], -dx))
                    Cv2.Merge(new[] { b2, g2, r2 }, dst);
                foreach (var m in ch) m.Dispose();
                break;
            }

            default:   // Normal dan efek AR yang dipanggil tanpa wajah
                src.CopyTo(dst);
                break;
        }
        return dst;
    }
}
