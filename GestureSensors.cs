using System;
using System.Linq;
using System.Threading.Tasks;
using OpenCvSharp;
using Windows.Media.SpeechRecognition;

namespace PhotoBooth;

/// <summary>
/// Deteksi telapak tangan terbuka tanpa model tambahan: segmentasi warna kulit (YCrCb), area wajah dikecualikan,
/// lalu jari dihitung dari cacat konveks kontur. Bersifat eksperimental: bekerja terbaik dengan pencahayaan cukup dan
/// latar yang tidak berwarna kulit. Telapak harus terbuka menghadap kamera dan diam sekitar 1 detik.
/// </summary>
public sealed class PalmDetector
{
    double stableSince = -1;
    double lastSeen = -10;
    double cooldownUntil;

    public bool Update(Mat bgr, Rect? face, double now)
    {
        if (now < cooldownUntil) return false;

        if (Detect(bgr, face))
        {
            lastSeen = now;
            if (stableSince < 0) stableSince = now;
            if (now - stableSince >= 1.0)
            {
                stableSince = -1;
                cooldownUntil = now + 6.0;
                return true;
            }
        }
        else if (now - lastSeen > 0.4)
        {
            stableSince = -1;
        }
        return false;
    }

    static bool Detect(Mat bgr, Rect? face)
    {
        double k = bgr.Cols > 320 ? 320.0 / bgr.Cols : 1.0;
        int w = Math.Max(32, (int)(bgr.Cols * k)), h = Math.Max(32, (int)(bgr.Rows * k));

        using var small = new Mat();
        Cv2.Resize(bgr, small, new Size(w, h));
        using var ycc = new Mat();
        Cv2.CvtColor(small, ycc, ColorConversionCodes.BGR2YCrCb);
        using var mask = new Mat();
        Cv2.InRange(ycc, new Scalar(0, 135, 77), new Scalar(255, 173, 127), mask);

        if (face is { } f)   // wajah + leher bukan telapak
        {
            int x = (int)((f.X - f.Width * 0.25) * k), y = (int)((f.Y - f.Height * 0.3) * k);
            int fw = (int)(f.Width * 1.5 * k), fh = (int)(f.Height * 2.4 * k);
            Cv2.Rectangle(mask, new Rect(Math.Max(0, x), Math.Max(0, y), Math.Max(1, fw), Math.Max(1, fh)),
                          Scalar.All(0), -1);
        }

        using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(5, 5));
        Cv2.MorphologyEx(mask, mask, MorphTypes.Open, kernel);
        Cv2.MorphologyEx(mask, mask, MorphTypes.Close, kernel);

        Cv2.FindContours(mask, out Point[][] contours, out _, RetrievalModes.External,
                         ContourApproximationModes.ApproxSimple);
        if (contours.Length == 0) return false;

        var contour = contours.OrderByDescending(c => Cv2.ContourArea(c)).First();
        double area = Cv2.ContourArea(contour);
        double ratio = area / (w * (double)h);
        if (ratio < 0.025 || ratio > 0.35 || contour.Length < 20) return false;

        var box = Cv2.BoundingRect(contour);
        double aspect = box.Width / (double)Math.Max(1, box.Height);
        if (aspect < 0.5 || aspect > 1.5) return false;

        var hull = Cv2.ConvexHull(contour);
        double hullArea = Cv2.ContourArea(hull);
        if (hullArea < 1) return false;
        double solidity = area / hullArea;           // telapak terbuka: tidak terlalu padat (ada sela jari)
        if (solidity < 0.45 || solidity > 0.88) return false;

        var hullIdx = Cv2.ConvexHullIndices(contour, false);
        if (hullIdx.Length < 4) return false;
        Vec4i[] defects;
        try { defects = Cv2.ConvexityDefects(contour, hullIdx); }
        catch { return false; }

        int gaps = 0;
        foreach (var d in defects)
        {
            double depth = d.Item3 / 256.0;
            if (depth < box.Height * 0.20) continue;
            var s = contour[d.Item0];
            var e = contour[d.Item1];
            var far = contour[d.Item2];
            double a = Dist(s, far), b = Dist(e, far), c = Dist(s, e);
            if (a < 1 || b < 1) continue;
            double angle = Math.Acos(Math.Clamp((a * a + b * b - c * c) / (2 * a * b), -1, 1));
            if (angle < 1.75) gaps++;                // sela antar jari: sudut tajam
        }
        return gaps is >= 3 and <= 4;                // 4-5 jari terbuka
    }

    static double Dist(Point p, Point q)
    {
        double dx = p.X - q.X, dy = p.Y - q.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}

/// <summary>Mendengarkan kata "cheese" lewat pengenalan suara Windows (tata bahasa daftar kata, berjalan lokal).</summary>
public sealed class CheeseListener : IDisposable
{
    SpeechRecognizer? rec;
    DateTime lastHeard = DateTime.MinValue;

    public event Action? Heard;
    public string Error { get; private set; } = "";
    public bool Running { get; private set; }

    public async Task<bool> StartAsync()
    {
        if (Running) return true;
        Error = "";
        try
        {
            SpeechRecognizer? r = null;
            try { r = new SpeechRecognizer(new Windows.Globalization.Language("en-US")); }
            catch { r = new SpeechRecognizer(); }   // bahasa ucapan bawaan sistem

            r.Constraints.Add(new SpeechRecognitionListConstraint(new[] { "cheese", "say cheese", "cheers" }, "cheese"));
            var compiled = await r.CompileConstraintsAsync();
            if (compiled.Status != SpeechRecognitionResultStatus.Success)
            {
                Error = "Pengenalan suara tidak siap: " + compiled.Status;
                r.Dispose();
                return false;
            }

            r.ContinuousRecognitionSession.ResultGenerated += (_, args) =>
            {
                if (args.Result.Confidence == SpeechRecognitionConfidence.Rejected) return;
                if (!args.Result.Text.Contains("chee", StringComparison.OrdinalIgnoreCase)) return;
                if ((DateTime.Now - lastHeard).TotalSeconds < 5) return;     // jeda antar pemicu
                lastHeard = DateTime.Now;
                Heard?.Invoke();
            };

            await r.ContinuousRecognitionSession.StartAsync();
            rec = r;
            Running = true;
            return true;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            return false;
        }
    }

    public async Task StopAsync()
    {
        Running = false;
        try
        {
            if (rec != null) await rec.ContinuousRecognitionSession.StopAsync();
        }
        catch { }
        rec?.Dispose();
        rec = null;
    }

    public void Dispose() => _ = StopAsync();
}
