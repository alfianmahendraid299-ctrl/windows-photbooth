using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CvMat = OpenCvSharp.Mat;
using Cv2 = OpenCvSharp.Cv2;

namespace PhotoBooth;

/// <summary>
/// Menempelkan teks "Shot on ..." dan cap waktu di pojok kiri bawah foto. Cap waktu berada tepat di atas teks Shot on.
/// Teks dirender dengan WPF (huruf halus, mendukung Unicode) sehingga HARUS dipanggil dari thread UI.
/// </summary>
public static class Watermark
{
    public static void Apply(CvMat photo, string? shotOn, bool timestamp, DateTime when)
    {
        var lines = new List<(string Text, double Scale, bool Bold)>();
        if (timestamp) lines.Add((when.ToString("dd MMM yyyy  ·  HH:mm", CultureInfo.CurrentCulture), 0.78, false));
        if (!string.IsNullOrWhiteSpace(shotOn)) lines.Add((shotOn.Trim(), 1.0, true));
        if (lines.Count == 0 || photo.Empty()) return;

        double basePx = Math.Max(14.0, photo.Rows * 0.030);
        double pad = basePx * 0.35;

        var texts = new List<(string Text, Typeface Face, double Size)>();
        foreach (var l in lines)
        {
            var face = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal,
                                    l.Bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal);
            texts.Add((l.Text, face, basePx * l.Scale));
        }

        FormattedText Make(string text, Typeface face, double size, Brush brush)
            => new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, size, brush, 1.0);

        var measured = texts.Select(t => Make(t.Text, t.Face, t.Size, Brushes.White)).ToList();
        int iw = (int)Math.Ceiling(measured.Max(m => m.Width) + pad * 2 + 3);
        int ih = (int)Math.Ceiling(measured.Sum(m => m.Height) + pad * 2 + 3);

        var shadow = new SolidColorBrush(Color.FromArgb(170, 0, 0, 0));
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            double y = pad;
            for (int i = 0; i < texts.Count; i++)
            {
                var t = texts[i];
                dc.DrawText(Make(t.Text, t.Face, t.Size, shadow), new Point(pad + 1.5, y + 1.5));
                dc.DrawText(measured[i], new Point(pad, y));
                y += measured[i].Height;
            }
        }

        var rtb = new RenderTargetBitmap(iw, ih, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var px = new byte[iw * ih * 4];
        rtb.CopyPixels(px, iw * 4, 0);

        using var overlay = new CvMat(ih, iw, OpenCvSharp.MatType.CV_8UC4);
        Marshal.Copy(px, 0, overlay.Data, px.Length);

        int x0 = (int)(photo.Cols * 0.03);
        int y0 = photo.Rows - ih - (int)(photo.Rows * 0.035);
        BlendPremultiplied(photo, overlay, x0, y0);
    }

    /// <summary>frame(ROI) = overlay_premultiplied + frame * (1 - alpha)</summary>
    static void BlendPremultiplied(CvMat frame, CvMat bgra, int x0, int y0)
    {
        x0 = Math.Max(0, x0);
        y0 = Math.Max(0, y0);
        int w = Math.Min(bgra.Cols, frame.Cols - x0);
        int h = Math.Min(bgra.Rows, frame.Rows - y0);
        if (w < 2 || h < 2) return;

        Cv2.Split(bgra, out CvMat[] ch);
        try
        {
            using var pmFull = new CvMat();
            Cv2.Merge(new[] { ch[0], ch[1], ch[2] }, pmFull);
            using var a3Full = new CvMat();
            Cv2.Merge(new[] { ch[3], ch[3], ch[3] }, a3Full);

            using var pm = new CvMat(pmFull, new OpenCvSharp.Rect(0, 0, w, h));
            using var a3 = new CvMat(a3Full, new OpenCvSharp.Rect(0, 0, w, h));
            using var ia3 = new CvMat();
            Cv2.BitwiseNot(a3, ia3);

            using var roi = new CvMat(frame, new OpenCvSharp.Rect(x0, y0, w, h));
            using var tmp = new CvMat();
            Cv2.Multiply(roi, ia3, tmp, 1.0 / 255.0);
            Cv2.Add(tmp, pm, roi);
        }
        finally
        {
            foreach (var m in ch) m.Dispose();
        }
    }
}
