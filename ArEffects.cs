using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using OpenCvSharp;

namespace PhotoBooth;

public enum Anchor { HeadTop, Eyes, EyeA, EyeB, Nose, Mouth, LowerLip, UpperLip, Chin, CheekA, CheekB }

/// <summary>
/// Penempatan satu stiker. W = lebar stiker relatif lebar wajah; (Px,Py) = titik pivot di gambar (0..1);
/// (Ox,Oy) = geser dalam satuan lebar wajah (X sepanjang garis mata, Y ke bawah); Rot dalam derajat.
/// </summary>
public sealed record StickerSpec(string Asset, Anchor A, float W, float Px, float Py,
                                 float Ox = 0, float Oy = 0, float Rot = 0,
                                 bool MouthOnly = false, bool Yaw = true, bool Pulse = false, float Bob = 0,
                                 int Frames = 1, float Fps = 12, float OrbitR = 0, float OrbitSpeed = 0,
                                 float OrbitPhase = 0, float OrbitTilt = 0.35f, float DriftY = 0,
                                 float DriftPeriod = 0, float Phase = 0, float Depth = 0.02f);

public static class ArEffects
{
    // Indeks landmark MediaPipe Face Mesh
    static readonly int[] FaceOval = { 10, 338, 297, 332, 284, 251, 389, 356, 454, 323, 361, 288, 397, 365, 379,
        378, 400, 377, 152, 148, 176, 149, 150, 136, 172, 58, 132, 93, 234, 127, 162, 21, 54, 103, 67, 109 };
    static readonly int[] LipsOuter = { 61, 146, 91, 181, 84, 17, 314, 405, 321, 375, 291, 409, 270, 269, 267, 0, 37, 39, 40, 185 };
    static readonly int[] LipsInner = { 78, 95, 88, 178, 87, 14, 317, 402, 318, 324, 308, 415, 310, 311, 312, 13, 82, 81, 80, 191 };
    static readonly int[] EyeRight = { 33, 7, 163, 144, 145, 153, 154, 155, 133, 173, 157, 158, 159, 160, 161, 246 };
    static readonly int[] EyeLeft = { 362, 382, 381, 380, 374, 373, 390, 249, 263, 466, 388, 387, 386, 385, 384, 398 };

    // ------------------------------------------------------------------ pintu masuk

    /// <summary>Haluskan kulit ringan (dipakai mode Potret pada Auto Scene).</summary>
    public static void ApplyBeauty(Mat frame, FaceData face)
    {
        if (face.P.Length >= 468) Beauty(frame, face);
    }

    public static Mat Apply(Mat src, EffectKind kind, FaceData? face)
    {
        var dst = src.Clone();
        if (face == null || face.P.Length < 468) return dst;

        double t = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        float w = face.FaceW;

        switch (kind)
        {
            case EffectKind.BigEyes:
                Bulge(dst, face.EyeA, w * 0.20f, 0.60f);
                Bulge(dst, face.EyeB, w * 0.20f, 0.60f);
                break;

            case EffectKind.BigNose:
                Bulge(dst, face.P[1], w * 0.28f, 0.65f);
                break;

            case EffectKind.Slim:
                Squeeze(dst, (face.P[1] + face.P[152]) * 0.5f, w * 0.80f, face.FaceH * 0.80f, 0.17f);
                break;

            case EffectKind.Alien:
                Bulge(dst, face.EyeA, w * 0.22f, 0.65f);
                Bulge(dst, face.EyeB, w * 0.22f, 0.65f);
                Bulge(dst, face.P[152], w * 0.30f, 0.35f);
                Squeeze(dst, (face.P[1] + face.P[152]) * 0.5f, w * 0.80f, face.FaceH * 0.80f, 0.12f);
                Tint(dst, face, new Scalar(70, 205, 95), 0.33);
                break;

            case EffectKind.Beauty:
                Beauty(dst, face);
                break;

            case EffectKind.Lipstick:
                Lipstick(dst, face, new Scalar(55, 35, 205), 0.55);
                break;

            case EffectKind.Glam:
                EyeShadow(dst, face, new Scalar(200, 70, 150), 0.40);
                Lipstick(dst, face, new Scalar(95, 40, 190), 0.55);
                SoftCircle(dst, face.P[50] + face.V * (w * 0.02f), w * 0.11f, new Scalar(150, 110, 245), 0.40);
                SoftCircle(dst, face.P[280] + face.V * (w * 0.02f), w * 0.11f, new Scalar(150, 110, 245), 0.40);
                break;

            case EffectKind.Freckles:
                Freckles(dst, face);
                SoftCircle(dst, face.P[50], w * 0.10f, new Scalar(140, 110, 240), 0.30);
                SoftCircle(dst, face.P[280], w * 0.10f, new Scalar(140, 110, 240), 0.30);
                break;

            case EffectKind.Portrait:
                Portrait(dst, face);
                break;

            case EffectKind.Hulk:
                Hulk(dst, face);
                break;

            case EffectKind.LaserEyes:
                LaserEyes(dst, face, t);
                break;

            case EffectKind.Googly:
                Googly(dst, face, t);
                break;

            case EffectKind.Lightning:
                Lightning(dst, face, t);
                break;

            case EffectKind.Frozen:
                Tint(dst, face, new Scalar(255, 205, 120), 0.40);
                SoftCircle(dst, face.P[50], w * 0.10f, new Scalar(255, 235, 210), 0.30);
                SoftCircle(dst, face.P[280], w * 0.10f, new Scalar(255, 235, 210), 0.30);
                Catalog(dst, face, kind, t);
                break;

            case EffectKind.Vampire:
                Tint(dst, face, new Scalar(238, 238, 250), 0.30);
                EyeShadow(dst, face, new Scalar(55, 25, 75), 0.45, 1.0f);
                Lipstick(dst, face, new Scalar(30, 20, 130), 0.50);
                SoftCircle(dst, face.EyeA, w * 0.035f, new Scalar(30, 30, 255), 0.65);
                SoftCircle(dst, face.EyeB, w * 0.035f, new Scalar(30, 30, 255), 0.65);
                Catalog(dst, face, kind, t);
                break;

            case EffectKind.Zombie:
                Tint(dst, face, new Scalar(115, 150, 125), 0.55);
                EyeShadow(dst, face, new Scalar(25, 25, 38), 0.70, 2.4f);
                Scar(dst, face);
                break;

            case EffectKind.Terminator:
                Terminator(dst, face, t);
                break;

            default:
                Catalog(dst, face, kind, t);
                break;
        }
        return dst;
    }

    // ------------------------------------------------------------------ stiker

    static readonly Dictionary<string, Mat?> cache = new();

    /// <summary>Memuat PNG, lalu mengalikan warna dengan alpha (premultiplied) agar tepi hasil warp bersih.</summary>
    static Mat? Sticker(string name)
    {
        lock (cache)
        {
            if (cache.TryGetValue(name, out var cached)) return cached;
            Mat? result = null;
            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Stickers", name + ".png");
                if (File.Exists(path))
                {
                    using var raw = Cv2.ImDecode(File.ReadAllBytes(path), ImreadModes.Unchanged);
                    if (!raw.Empty() && raw.Channels() == 4)
                    {
                        Cv2.Split(raw, out Mat[] ch);
                        for (int i = 0; i < 3; i++) Cv2.Multiply(ch[i], ch[3], ch[i], 1.0 / 255.0);
                        result = new Mat();
                        Cv2.Merge(ch, result);
                        foreach (var m in ch) m.Dispose();
                    }
                }
            }
            catch { result = null; }
            cache[name] = result;
            return result;
        }
    }

    static Vector2 AnchorPoint(FaceData f, Anchor a) => a switch
    {
        Anchor.HeadTop => f.P[10],
        Anchor.Eyes => (f.EyeA + f.EyeB) * 0.5f,
        Anchor.EyeA => f.EyeA,
        Anchor.EyeB => f.EyeB,
        Anchor.Nose => f.P[1],
        Anchor.Mouth => f.Mid(13, 14),
        Anchor.LowerLip => f.P[14],
        Anchor.UpperLip => f.P[0],
        Anchor.Chin => f.P[152],
        Anchor.CheekA => f.P[50],
        Anchor.CheekB => f.P[280],
        _ => f.P[1],
    };

    static void DrawStickers(Mat frame, FaceData f, StickerSpec[] specs, double t)
    {
        foreach (var sp in specs)
        {
            if (sp.MouthOnly && f.MouthOpen < 0.22f) continue;

            string name = sp.Asset;
            if (sp.Frames > 1)   // stiker beranimasi: ganti bingkai berdasarkan waktu
            {
                int fi = (int)Math.Floor(t * sp.Fps + sp.Phase * sp.Frames) % sp.Frames;
                if (fi < 0) fi += sp.Frames;
                name = sp.Asset + "_" + fi;
            }
            var st = Sticker(name);
            if (st == null) continue;

            double ox = sp.Ox;
            double oy = sp.Oy + (sp.Bob != 0 ? sp.Bob * Math.Sin(t * 3) : 0.0);
            double widthMul = 1.0;
            double depth = sp.Depth;

            if (sp.OrbitR > 0)   // mengorbit kepala; yang "di depan" tampak lebih besar & lebih dekat
            {
                double a = sp.OrbitSpeed * t + sp.OrbitPhase;
                ox += sp.OrbitR * Math.Cos(a);
                oy += sp.OrbitR * sp.OrbitTilt * Math.Sin(a);
                double zf = Math.Sin(a);
                widthMul *= 1.0 + 0.25 * zf;
                depth += 0.35 * sp.OrbitR * zf;
            }
            if (sp.DriftPeriod > 0)   // melayang naik lalu menghilang (hujan hati)
            {
                double fr = (t / sp.DriftPeriod + sp.Phase) % 1.0;
                if (fr < 0) fr += 1.0;
                oy -= sp.DriftY * fr;
                widthMul *= Math.Sin(Math.PI * fr);
                if (widthMul < 0.03) continue;
            }
            if (sp.Pulse) widthMul *= 1.0 + 0.08 * Math.Sin(t * 7);

            var anchor = AnchorPoint(f, sp.A) + f.U3xy * (float)(ox * f.FaceW) + f.V3xy * (float)(oy * f.FaceW);
            Overlay(frame, st, f, anchor, sp.Px, sp.Py, sp.W * f.FaceW * widthMul,
                    sp.Rot * Math.PI / 180.0, (float)(depth * f.FaceW));
        }
    }

    /// <summary>
    /// Menempel stiker pada bidang wajah 3D: sudut gambar diproyeksikan lewat sumbu U/V/N wajah sehingga
    /// stiker ikut miring, menoleh, dan mengangguk. Transformasi perspektif, alpha blending hanya di area terkena.
    /// </summary>
    static void Overlay(Mat frame, Mat st, FaceData f, Vector2 anchor, double px, double py,
                        double widthPx, double rot, float depthPx)
    {
        double s = widthPx / st.Cols;
        double ppx = px * st.Cols, ppy = py * st.Rows;
        double cr = Math.Cos(rot), sr = Math.Sin(rot);
        var u = f.U3xy;
        var v = f.V3xy;
        var n = f.N3xy;

        double[] sxs = { 0, st.Cols, st.Cols, 0 };
        double[] sys = { 0, 0, st.Rows, st.Rows };
        var src = new Point2f[4];
        var dstPts = new Vector2[4];
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        for (int i = 0; i < 4; i++)
        {
            double lx = (sxs[i] - ppx) * s, ly = (sys[i] - ppy) * s;
            double rx = lx * cr - ly * sr, ry = lx * sr + ly * cr;
            var p = anchor + u * (float)rx + v * (float)ry + n * depthPx;
            dstPts[i] = p;
            src[i] = new Point2f((float)sxs[i], (float)sys[i]);
            minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
            minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
        }

        // lewati bila bidang nyaris menjadi garis (kepala menoleh terlalu jauh)
        double area = 0;
        for (int i = 0; i < 4; i++)
        {
            var a = dstPts[i];
            var b = dstPts[(i + 1) % 4];
            area += a.X * b.Y - b.X * a.Y;
        }
        if (Math.Abs(area) * 0.5 < 16) return;

        int x0 = Math.Max(0, (int)Math.Floor(minX));
        int y0 = Math.Max(0, (int)Math.Floor(minY));
        int x1 = Math.Min(frame.Cols, (int)Math.Ceiling(maxX));
        int y1 = Math.Min(frame.Rows, (int)Math.Ceiling(maxY));
        int w = x1 - x0, h = y1 - y0;
        if (w < 2 || h < 2 || (long)w * h > 6_000_000) return;

        var dst = new Point2f[4];
        for (int i = 0; i < 4; i++) dst[i] = new Point2f(dstPts[i].X - x0, dstPts[i].Y - y0);

        using var m = Cv2.GetPerspectiveTransform(src, dst);
        using var warped = new Mat();
        Cv2.WarpPerspective(st, warped, m, new Size(w, h), InterpolationFlags.Linear,
                            BorderTypes.Constant, Scalar.All(0));

        Cv2.Split(warped, out Mat[] ch);
        try
        {
            using var pm = new Mat();
            Cv2.Merge(new[] { ch[0], ch[1], ch[2] }, pm);
            using var a3 = new Mat();
            Cv2.Merge(new[] { ch[3], ch[3], ch[3] }, a3);
            using var ia3 = new Mat();
            Cv2.BitwiseNot(a3, ia3);

            using var roi = new Mat(frame, new Rect(x0, y0, w, h));
            using var tmp = new Mat();
            Cv2.Multiply(roi, ia3, tmp, 1.0 / 255.0);
            Cv2.Add(tmp, pm, roi);
        }
        finally
        {
            foreach (var x in ch) x.Dispose();
        }
    }

    // ------------------------------------------------------------------ warp wajah

    /// <summary>strength > 0 memperbesar area bulat; strength &lt; 0 mengecilkan.</summary>
    static void Bulge(Mat frame, Vector2 c, float r, float strength)
    {
        if (!Region(frame, c, r, r, out int x0, out int y0, out int w, out int h)) return;

        var mx = new float[w * h];
        var my = new float[w * h];
        float r2 = r * r;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float px = x0 + x, py = y0 + y;
                float dx = px - c.X, dy = py - c.Y;
                float d2 = (dx * dx + dy * dy) / r2;
                float sc = 1f;
                if (d2 < 1f) { float k = 1f - d2; sc = 1f - strength * k * k; }
                int i = y * w + x;
                mx[i] = c.X + dx * sc - x0;
                my[i] = c.Y + dy * sc - y0;
            }
        RemapRegion(frame, x0, y0, w, h, mx, my);
    }

    /// <summary>Menekan isi secara horizontal di dalam elips (k > 0): wajah tampak lebih ramping.</summary>
    static void Squeeze(Mat frame, Vector2 c, float rx, float ry, float k)
    {
        if (!Region(frame, c, rx, ry, out int x0, out int y0, out int w, out int h)) return;

        var mx = new float[w * h];
        var my = new float[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float px = x0 + x, py = y0 + y;
                float nx = (px - c.X) / rx, ny = (py - c.Y) / ry;
                float d2 = nx * nx + ny * ny;
                float sx = 1f;
                if (d2 < 1f) { float t = 1f - d2; sx = 1f + k * t * t; }
                int i = y * w + x;
                mx[i] = c.X + (px - c.X) * sx - x0;
                my[i] = py - y0;
            }
        RemapRegion(frame, x0, y0, w, h, mx, my);
    }

    static bool Region(Mat frame, Vector2 c, float rx, float ry, out int x0, out int y0, out int w, out int h)
    {
        x0 = Math.Max(0, (int)(c.X - rx));
        y0 = Math.Max(0, (int)(c.Y - ry));
        int x1 = Math.Min(frame.Cols, (int)(c.X + rx) + 1);
        int y1 = Math.Min(frame.Rows, (int)(c.Y + ry) + 1);
        w = x1 - x0;
        h = y1 - y0;
        return w >= 8 && h >= 8 && (long)w * h < 2_000_000;
    }

    static void RemapRegion(Mat frame, int x0, int y0, int w, int h, float[] mx, float[] my)
    {
        using var mapX = new Mat(h, w, MatType.CV_32FC1);
        using var mapY = new Mat(h, w, MatType.CV_32FC1);
        Marshal.Copy(mx, 0, mapX.Data, mx.Length);
        Marshal.Copy(my, 0, mapY.Data, my.Length);
        using var roi = new Mat(frame, new Rect(x0, y0, w, h));
        using var copy = roi.Clone();
        Cv2.Remap(copy, roi, mapX, mapY, InterpolationFlags.Linear, BorderTypes.Replicate);
    }

    // ------------------------------------------------------------------ makeup & kecantikan

    static Point[] Pts(FaceData f, int[] idx, int ox = 0, int oy = 0)
    {
        var r = new Point[idx.Length];
        for (int i = 0; i < idx.Length; i++)
            r[i] = new Point((int)MathF.Round(f.P[idx[i]].X) - ox, (int)MathF.Round(f.P[idx[i]].Y) - oy);
        return r;
    }

    static bool Bounds(Mat frame, Point[] pts, int pad, out Rect r)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (var p in pts)
        {
            minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
            minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
        }
        int x0 = Math.Max(0, minX - pad), y0 = Math.Max(0, minY - pad);
        int x1 = Math.Min(frame.Cols, maxX + pad), y1 = Math.Min(frame.Rows, maxY + pad);
        r = new Rect(x0, y0, x1 - x0, y1 - y0);
        return r.Width >= 4 && r.Height >= 4;
    }

    /// <summary>frame(ROI) = frame*(1-a*m) + color*(a*m)</summary>
    static void BlendColor(Mat frame, Rect r, Mat mask8, Scalar color, double alpha)
    {
        using var ms = new Mat();
        mask8.ConvertTo(ms, MatType.CV_8UC1, alpha);
        using var m3 = new Mat();
        Cv2.Merge(new[] { ms, ms, ms }, m3);
        using var inv = new Mat();
        Cv2.BitwiseNot(m3, inv);
        using var col = new Mat(r.Height, r.Width, MatType.CV_8UC3, color);
        using var roi = new Mat(frame, r);
        using var a = new Mat();
        using var b = new Mat();
        Cv2.Multiply(roi, inv, a, 1.0 / 255.0);
        Cv2.Multiply(col, m3, b, 1.0 / 255.0);
        Cv2.Add(a, b, roi);
    }

    static void Lipstick(Mat frame, FaceData f, Scalar color, double alpha)
    {
        var outer = Pts(f, LipsOuter);
        if (!Bounds(frame, outer, 6, out var r)) return;
        using var mask = new Mat(r.Height, r.Width, MatType.CV_8UC1, Scalar.All(0));
        Cv2.FillPoly(mask, new[] { Pts(f, LipsOuter, r.X, r.Y) }, Scalar.All(255));
        Cv2.FillPoly(mask, new[] { Pts(f, LipsInner, r.X, r.Y) }, Scalar.All(0));
        Cv2.GaussianBlur(mask, mask, new Size(0, 0), 1.6);
        BlendColor(frame, r, mask, color, alpha);
    }

    static void EyeShadow(Mat frame, FaceData f, Scalar color, double alpha, float grow = 1f)
    {
        foreach (var idx in new[] { EyeRight, EyeLeft })
        {
            if (!Bounds(frame, Pts(f, idx), (int)(f.FaceW * 0.07 * grow), out var r)) continue;
            using var mask = new Mat(r.Height, r.Width, MatType.CV_8UC1, Scalar.All(0));
            var poly = Pts(f, idx, r.X, r.Y);
            Cv2.FillPoly(mask, new[] { poly }, Scalar.All(255));
            int k = Math.Max(3, (int)(f.FaceW * 0.05 * grow)) | 1;
            using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(k, k));
            Cv2.Dilate(mask, mask, kernel);
            Cv2.FillPoly(mask, new[] { poly }, Scalar.All(0));   // bola mata tidak diwarnai
            Cv2.GaussianBlur(mask, mask, new Size(0, 0), Math.Max(1.5, f.FaceW * 0.012));
            BlendColor(frame, r, mask, color, alpha);
        }
    }

    static void SoftCircle(Mat frame, Vector2 c, float rad, Scalar color, double alpha)
    {
        int pad = (int)(rad * 1.6f);
        int x0 = Math.Max(0, (int)c.X - pad), y0 = Math.Max(0, (int)c.Y - pad);
        int x1 = Math.Min(frame.Cols, (int)c.X + pad), y1 = Math.Min(frame.Rows, (int)c.Y + pad);
        var r = new Rect(x0, y0, x1 - x0, y1 - y0);
        if (r.Width < 4 || r.Height < 4) return;

        using var mask = new Mat(r.Height, r.Width, MatType.CV_8UC1, Scalar.All(0));
        Cv2.Circle(mask, new Point((int)c.X - x0, (int)c.Y - y0), (int)rad, Scalar.All(255), -1);
        Cv2.GaussianBlur(mask, mask, new Size(0, 0), Math.Max(1.0, rad * 0.55));
        BlendColor(frame, r, mask, color, alpha);
    }

    static readonly float[][] FreckleSpots =
    {
        new[] { -0.20f, 0.00f }, new[] { -0.14f, -0.05f }, new[] { -0.25f, -0.06f }, new[] { -0.09f, 0.03f },
        new[] { -0.19f, 0.06f }, new[] { -0.30f, 0.01f }, new[] { -0.12f, -0.10f },
    };

    static void Freckles(Mat frame, FaceData f)
    {
        var origin = f.P[1];
        foreach (var s in FreckleSpots)
            foreach (int side in new[] { 1, -1 })
            {
                var p = origin + f.U * (s[0] * side * f.FaceW) + f.V * (s[1] * f.FaceW);
                SoftCircle(frame, p, f.FaceW * 0.014f, new Scalar(45, 80, 135), 0.60);
            }
    }

    static void Tint(Mat frame, FaceData f, Scalar color, double alpha)
    {
        var oval = Pts(f, FaceOval);
        if (!Bounds(frame, oval, 4, out var r)) return;
        using var mask = new Mat(r.Height, r.Width, MatType.CV_8UC1, Scalar.All(0));
        Cv2.FillPoly(mask, new[] { Pts(f, FaceOval, r.X, r.Y) }, Scalar.All(255));
        Cv2.GaussianBlur(mask, mask, new Size(0, 0), Math.Max(2.0, f.FaceW * 0.03));
        BlendColor(frame, r, mask, color, alpha);
    }

    /// <summary>Haluskan kulit (bilateral) hanya di dalam wajah, kecuali mata dan bibir.</summary>
    static void Beauty(Mat frame, FaceData f)
    {
        var oval = Pts(f, FaceOval);
        if (!Bounds(frame, oval, 4, out var r)) return;

        using var roi = new Mat(frame, r);
        using var small = new Mat();
        Cv2.Resize(roi, small, new Size(Math.Max(8, r.Width / 2), Math.Max(8, r.Height / 2)));
        using var sm = new Mat();
        Cv2.BilateralFilter(small, sm, 7, 40, 7);
        using var big = new Mat();
        Cv2.Resize(sm, big, new Size(r.Width, r.Height));
        Cv2.ConvertScaleAbs(big, big, 1.04, 7);   // sedikit lebih cerah

        using var mask = new Mat(r.Height, r.Width, MatType.CV_8UC1, Scalar.All(0));
        Cv2.FillPoly(mask, new[] { Pts(f, FaceOval, r.X, r.Y) }, Scalar.All(255));
        Cv2.FillPoly(mask, new[] { Pts(f, EyeRight, r.X, r.Y) }, Scalar.All(0));
        Cv2.FillPoly(mask, new[] { Pts(f, EyeLeft, r.X, r.Y) }, Scalar.All(0));
        Cv2.FillPoly(mask, new[] { Pts(f, LipsOuter, r.X, r.Y) }, Scalar.All(0));
        Cv2.GaussianBlur(mask, mask, new Size(0, 0), Math.Max(2.0, f.FaceW * 0.025));

        using var m3 = new Mat();
        Cv2.Merge(new[] { mask, mask, mask }, m3);
        using var inv = new Mat();
        Cv2.BitwiseNot(m3, inv);
        using var a = new Mat();
        using var b = new Mat();
        Cv2.Multiply(big, m3, a, 1.0 / 255.0);
        Cv2.Multiply(roi, inv, b, 1.0 / 255.0);
        Cv2.Add(a, b, roi);
    }

    /// <summary>Pendekatan "mode potret": latar dikaburkan di luar elips kepala-bahu (tanpa segmentasi sungguhan).</summary>
    static void Portrait(Mat frame, FaceData f)
    {
        int sw = Math.Max(8, frame.Cols / 4), sh = Math.Max(8, frame.Rows / 4);
        double k = sw / (double)frame.Cols;

        using var small = new Mat();
        Cv2.Resize(frame, small, new Size(sw, sh));
        using var blurSmall = new Mat();
        Cv2.GaussianBlur(small, blurSmall, new Size(0, 0), 4.0);
        using var blur = new Mat();
        Cv2.Resize(blurSmall, blur, new Size(frame.Cols, frame.Rows));

        using var mask = new Mat(sh, sw, MatType.CV_8UC1, Scalar.All(0));
        var center = f.Mid(10, 152) + f.V * (f.FaceH * 0.55f);
        Cv2.Ellipse(mask, new Point((int)(center.X * k), (int)(center.Y * k)),
                    new Size((int)(f.FaceW * 1.35 * k), (int)(f.FaceH * 1.9 * k)),
                    f.Roll * 180.0 / Math.PI, 0, 360, Scalar.All(255), -1);
        Cv2.GaussianBlur(mask, mask, new Size(0, 0), Math.Max(2.0, f.FaceW * 0.18 * k));
        using var maskFull = new Mat();
        Cv2.Resize(mask, maskFull, new Size(frame.Cols, frame.Rows));

        using var m3 = new Mat();
        Cv2.Merge(new[] { maskFull, maskFull, maskFull }, m3);
        using var inv = new Mat();
        Cv2.BitwiseNot(m3, inv);
        using var a = new Mat();
        using var b = new Mat();
        Cv2.Multiply(frame, m3, a, 1.0 / 255.0);
        Cv2.Multiply(blur, inv, b, 1.0 / 255.0);
        Cv2.Add(a, b, frame);
    }

    // ------------------------------------------------------------------ efek aksi & spesial

    static void Catalog(Mat frame, FaceData f, EffectKind kind, double t)
    {
        if (ArCatalog.Stickers.TryGetValue(kind, out var specs)) DrawStickers(frame, f, specs, t);
    }

    static Vector2 Loc(FaceData f, Vector2 origin, float u, float v)
        => origin + f.U3xy * (u * f.FaceW) + f.V3xy * (v * f.FaceW);

    static Vector2 Rotate(Vector2 v, double a)
        => new((float)(v.X * Math.Cos(a) - v.Y * Math.Sin(a)), (float)(v.X * Math.Sin(a) + v.Y * Math.Cos(a)));

    /// <summary>Menggambar goresan tebal berujung halus lalu mencampurnya ke frame.</summary>
    static void DrawStrokes(Mat frame, List<Vector2[]> strokes, float thickness, Scalar color, double alpha, double blur)
    {
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var s in strokes)
            foreach (var p in s)
            {
                minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
                minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
            }
        if (minX > maxX) return;

        int pad = (int)(thickness * 2) + 6;
        int x0 = Math.Max(0, (int)minX - pad), y0 = Math.Max(0, (int)minY - pad);
        int x1 = Math.Min(frame.Cols, (int)maxX + pad), y1 = Math.Min(frame.Rows, (int)maxY + pad);
        var r = new Rect(x0, y0, x1 - x0, y1 - y0);
        if (r.Width < 4 || r.Height < 4) return;

        using var mask = new Mat(r.Height, r.Width, MatType.CV_8UC1, Scalar.All(0));
        int th = Math.Max(1, (int)Math.Round(thickness));
        foreach (var s in strokes)
            for (int i = 0; i < s.Length - 1; i++)
                Cv2.Line(mask,
                         new Point((int)(s[i].X - r.X), (int)(s[i].Y - r.Y)),
                         new Point((int)(s[i + 1].X - r.X), (int)(s[i + 1].Y - r.Y)),
                         Scalar.All(255), th, LineTypes.AntiAlias);
        if (blur > 0) Cv2.GaussianBlur(mask, mask, new Size(0, 0), blur);
        BlendColor(frame, r, mask, color, alpha);
    }

    /// <summary>Hulk: kulit hijau, rahang melebar, alis menukik marah, dan urat di dahi.</summary>
    static void Hulk(Mat frame, FaceData f)
    {
        float w = f.FaceW;
        Squeeze(frame, f.P[1] + f.V3xy * (w * 0.15f), w * 0.85f, f.FaceH * 0.75f, -0.10f);   // rahang melebar
        Tint(frame, f, new Scalar(40, 175, 55), 0.62);

        var brows = new List<Vector2[]>();
        foreach (bool left in new[] { true, false })
        {
            int outer = left ? 70 : 300, mid = left ? 105 : 334, inner = left ? 107 : 336;
            var po = Loc(f, f.P[outer], 0f, -0.015f);
            var pm = Loc(f, f.P[mid], 0f, 0.012f);
            var pi = Loc(f, f.P[inner], left ? 0.02f : -0.02f, 0.05f);   // ujung dalam turun = marah
            brows.Add(new[] { po, pm, pi });
        }
        DrawStrokes(frame, brows, w * 0.05f, new Scalar(20, 45, 20), 0.85, 1.2);

        var o = f.P[9];
        var veins = new List<Vector2[]>();
        foreach (int sd in new[] { -1, 1 })
        {
            veins.Add(new[] { Loc(f, o, sd * 0.05f, -0.12f), Loc(f, o, sd * 0.10f, -0.18f), Loc(f, o, sd * 0.08f, -0.24f), Loc(f, o, sd * 0.15f, -0.30f) });
            veins.Add(new[] { Loc(f, o, sd * 0.16f, -0.14f), Loc(f, o, sd * 0.22f, -0.19f), Loc(f, o, sd * 0.20f, -0.26f) });
        }
        veins.Add(new[] { Loc(f, o, 0f, -0.14f), Loc(f, o, 0.02f, -0.21f), Loc(f, o, -0.01f, -0.28f) });
        DrawStrokes(frame, veins, w * 0.012f, new Scalar(25, 100, 30), 0.70, 0.8);
    }

    /// <summary>Laser dari kedua mata; makin kuat saat mulut dibuka. Digambar di resolusi 1/4 lalu ditambahkan (glow).</summary>
    static void LaserEyes(Mat frame, FaceData f, double t)
    {
        int sw = Math.Max(16, frame.Cols / 4), sh = Math.Max(16, frame.Rows / 4);
        double k = sw / (double)frame.Cols;
        using var layer = new Mat(sh, sw, MatType.CV_8UC3, Scalar.All(0));

        double flick = 0.82 + 0.18 * Math.Sin(t * 38);
        double power = 0.65 + 0.35 * Math.Clamp(f.MouthOpen * 3.0, 0.0, 1.0);
        double len = Math.Sqrt((double)frame.Cols * frame.Cols + (double)frame.Rows * frame.Rows);

        for (int side = -1; side <= 1; side += 2)
        {
            var eye = side < 0 ? f.EyeA : f.EyeB;
            var dir = f.U3xy * side + f.V3xy * 0.30f;           // keluar & sedikit ke bawah menurut sumbu wajah
            dir = Vector2.Normalize(Rotate(dir, 0.03 * Math.Sin(t * 17 + side)));
            var end = eye + dir * (float)len;

            var p0 = new Point((int)(eye.X * k), (int)(eye.Y * k));
            var p1 = new Point((int)(end.X * k), (int)(end.Y * k));
            int w0 = Math.Max(2, (int)(f.FaceW * k * 0.085 * power * flick));

            Cv2.Line(layer, p0, p1, new Scalar(0, 0, 190), w0 * 2, LineTypes.AntiAlias);
            Cv2.Line(layer, p0, p1, new Scalar(30, 50, 255), w0, LineTypes.AntiAlias);
            Cv2.Line(layer, p0, p1, new Scalar(190, 225, 255), Math.Max(1, w0 / 3), LineTypes.AntiAlias);
            Cv2.Circle(layer, p0, Math.Max(2, (int)(f.FaceW * k * 0.07 * power)), new Scalar(60, 60, 255), -1, LineTypes.AntiAlias);
            Cv2.Circle(layer, p0, Math.Max(1, (int)(f.FaceW * k * 0.035)), new Scalar(225, 240, 255), -1, LineTypes.AntiAlias);
        }

        Cv2.GaussianBlur(layer, layer, new Size(0, 0), 2.0);
        using var up = new Mat();
        Cv2.Resize(layer, up, new Size(frame.Cols, frame.Rows));
        Cv2.Add(frame, up, frame);
    }

    // Mata goyang: pupil bergerak seperti benda pegas (gravitasi + guncangan kepala)
    static readonly Vector2[] gPupil = new Vector2[2];
    static readonly Vector2[] gVel = new Vector2[2];
    static readonly Vector2[] gPrev = new Vector2[2];
    static double gPrevT;
    static bool gInit;

    static void Googly(Mat frame, FaceData f, double t)
    {
        float R = f.FaceW * 0.12f;
        float dt = (float)Math.Clamp(t - gPrevT, 0.005, 0.1);
        gPrevT = t;
        var eyes = new[] { f.EyeA, f.EyeB };
        var grav = f.V3xy * 0.45f;

        for (int i = 0; i < 2; i++)
        {
            if (!gInit) gPrev[i] = eyes[i];
            var disp = (eyes[i] - gPrev[i]) / R;
            gPrev[i] = eyes[i];

            gVel[i] += (grav * 22f - gPupil[i] * 9f) * dt - gVel[i] * 3.5f * dt - disp * 2.2f;
            gPupil[i] += gVel[i] * dt;
            if (gPupil[i].Length() > 0.5f)
            {
                gPupil[i] = Vector2.Normalize(gPupil[i]) * 0.5f;
                gVel[i] *= -0.35f;
            }

            var c = new Point((int)eyes[i].X, (int)eyes[i].Y);
            int r = (int)R;
            Cv2.Circle(frame, c, r, new Scalar(250, 250, 250), -1, LineTypes.AntiAlias);
            Cv2.Circle(frame, c, r, new Scalar(25, 25, 25), Math.Max(2, r / 8), LineTypes.AntiAlias);
            var pc = new Point((int)(eyes[i].X + gPupil[i].X * R), (int)(eyes[i].Y + gPupil[i].Y * R));
            Cv2.Circle(frame, pc, Math.Max(2, r / 2), new Scalar(15, 15, 15), -1, LineTypes.AntiAlias);
            Cv2.Circle(frame, new Point(pc.X - r / 8, pc.Y - r / 8), Math.Max(1, r / 9), new Scalar(255, 255, 255), -1, LineTypes.AntiAlias);
        }
        gInit = true;
    }

    /// <summary>Petir bercabang di sekeliling kepala; bentuknya berganti ~14 kali per detik.</summary>
    static void Lightning(Mat frame, FaceData f, double t)
    {
        int sw = Math.Max(16, frame.Cols / 4), sh = Math.Max(16, frame.Rows / 4);
        double k = sw / (double)frame.Cols;
        using var layer = new Mat(sh, sw, MatType.CV_8UC3, Scalar.All(0));
        var rnd = new Random((int)(t * 14));
        var c = f.Mid(10, 152);

        for (int b = 0; b < 6; b++)
        {
            double a = rnd.NextDouble() * Math.PI * 2;
            var start = c + new Vector2((float)Math.Cos(a) * f.FaceW * 0.55f, (float)Math.Sin(a) * f.FaceH * 0.62f);
            var dir = Vector2.Normalize(start - c);
            float len = f.FaceW * (0.5f + (float)rnd.NextDouble() * 0.7f);

            var pts = new List<Vector2> { start, start + dir * len };
            float off = len * 0.25f;
            for (int d = 0; d < 3; d++)
            {
                var next = new List<Vector2>();
                for (int i = 0; i < pts.Count - 1; i++)
                {
                    var mid = (pts[i] + pts[i + 1]) * 0.5f;
                    var seg = pts[i + 1] - pts[i];
                    var perp = Vector2.Normalize(new Vector2(-seg.Y, seg.X));
                    mid += perp * ((float)rnd.NextDouble() - 0.5f) * 2f * off;
                    next.Add(pts[i]);
                    next.Add(mid);
                }
                next.Add(pts[^1]);
                pts = next;
                off *= 0.5f;
            }

            for (int i = 0; i < pts.Count - 1; i++)
            {
                var p0 = new Point((int)(pts[i].X * k), (int)(pts[i].Y * k));
                var p1 = new Point((int)(pts[i + 1].X * k), (int)(pts[i + 1].Y * k));
                Cv2.Line(layer, p0, p1, new Scalar(255, 200, 90), 3, LineTypes.AntiAlias);
                Cv2.Line(layer, p0, p1, new Scalar(255, 255, 255), 1, LineTypes.AntiAlias);
            }
        }

        Cv2.GaussianBlur(layer, layer, new Size(0, 0), 1.6);
        using var up = new Mat();
        Cv2.Resize(layer, up, new Size(frame.Cols, frame.Rows));
        Cv2.Add(frame, up, frame);
    }

    static void Scar(Mat frame, FaceData f)
    {
        var a = Loc(f, f.P[50], -0.04f, 0.0f);
        var b = Loc(f, f.P[50], 0.14f, -0.14f);
        var strokes = new List<Vector2[]> { new[] { a, b } };
        var dir = Vector2.Normalize(b - a);
        var n = new Vector2(-dir.Y, dir.X);
        for (int i = 0; i < 5; i++)
        {
            var p = Vector2.Lerp(a, b, (i + 0.5f) / 5f);
            strokes.Add(new[] { p - n * (f.FaceW * 0.035f), p + n * (f.FaceW * 0.035f) });
        }
        DrawStrokes(frame, strokes, f.FaceW * 0.012f, new Scalar(35, 35, 130), 0.85, 0.6);
    }

    static Mat? termK;

    /// <summary>Penglihatan Terminator: gambar merah monokrom, garis pindai, kotak target, dan teks data.</summary>
    static void Terminator(Mat frame, FaceData f, double t)
    {
        termK ??= Mat.FromArray(new float[,]
        {
            { 0.114f * 0.12f, 0.587f * 0.12f, 0.299f * 0.12f },
            { 0.114f * 0.10f, 0.587f * 0.10f, 0.299f * 0.10f },
            { 0.114f * 1.15f, 0.587f * 1.15f, 0.299f * 1.15f },
        });
        using (var tmp = new Mat())
        {
            Cv2.Transform(frame, tmp, termK);
            tmp.CopyTo(frame);
        }

        for (int y = 0; y < frame.Rows; y += 4)
            Cv2.Line(frame, new Point(0, y), new Point(frame.Cols, y), Scalar.All(0), 1);

        int bar = Math.Max(6, frame.Rows / 28);
        int by = (int)((t * 0.35 % 1.0) * (frame.Rows - bar));
        using (var strip = new Mat(frame, new Rect(0, by, frame.Cols, bar)))
        using (var red = new Mat(strip.Rows, strip.Cols, MatType.CV_8UC3, new Scalar(0, 0, 255)))
            Cv2.AddWeighted(strip, 0.6, red, 0.4, 0, strip);

        if (!Bounds(frame, Pts(f, FaceOval), (int)(f.FaceW * 0.10), out var r)) return;
        int L = Math.Max(10, r.Width / 5);
        int th = Math.Max(2, frame.Cols / 400);
        var col = new Scalar(40, 40, 255);

        void Corner(int x, int y, int dx, int dy)
        {
            Cv2.Line(frame, new Point(x, y), new Point(x + dx * L, y), col, th);
            Cv2.Line(frame, new Point(x, y), new Point(x, y + dy * L), col, th);
        }
        Corner(r.X, r.Y, 1, 1);
        Corner(r.Right, r.Y, -1, 1);
        Corner(r.X, r.Bottom, 1, -1);
        Corner(r.Right, r.Bottom, -1, -1);

        var n = f.P[1];
        int cr = (int)(f.FaceW * 0.06f);
        Cv2.Circle(frame, new Point((int)n.X, (int)n.Y), cr, col, th, LineTypes.AntiAlias);
        Cv2.Line(frame, new Point((int)n.X - cr * 2, (int)n.Y), new Point((int)n.X - cr, (int)n.Y), col, th);
        Cv2.Line(frame, new Point((int)n.X + cr, (int)n.Y), new Point((int)n.X + cr * 2, (int)n.Y), col, th);
        Cv2.Line(frame, new Point((int)n.X, (int)n.Y - cr * 2), new Point((int)n.X, (int)n.Y - cr), col, th);
        Cv2.Line(frame, new Point((int)n.X, (int)n.Y + cr), new Point((int)n.X, (int)n.Y + cr * 2), col, th);

        double fs = Math.Max(0.45, frame.Cols / 900.0);
        int tt = Math.Max(1, th - 1);
        if (((int)(t * 2)) % 2 == 0)
            Cv2.PutText(frame, "TARGET LOCKED", new Point(r.X, Math.Max(18, r.Y - 8)),
                        HersheyFonts.HersheySimplex, fs, col, tt, LineTypes.AntiAlias);

        int lx = Math.Min(frame.Cols - 10, r.Right + 10);
        int ly = r.Y + (int)(fs * 28);
        string[] lines = { "HUMAN", $"MATCH {96 + (int)(3 * Math.Sin(t * 5))}%", $"ID {((int)(t * 7) % 900) + 100}", "THREAT: LOW" };
        foreach (var line in lines)
        {
            if (lx < frame.Cols * 0.72) Cv2.PutText(frame, line, new Point(lx, ly), HersheyFonts.HersheySimplex, fs * 0.8, col, tt, LineTypes.AntiAlias);
            ly += (int)(fs * 26);
        }
    }
}
