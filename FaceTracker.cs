using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.ML.OnnxRuntime;
using OpenCvSharp;

namespace PhotoBooth;

/// <summary>Hasil pelacakan satu wajah: 478 titik (piksel frame) beserta besaran turunan.</summary>
public sealed class FaceData
{
    public Vector2[] P = Array.Empty<Vector2>();
    public float[] Zraw = Array.Empty<float>();   // kedalaman mentah dari model
    public Vector2 EyeA, EyeB;      // pusat iris; A = yang lebih kiri di layar
    public Vector2 U, V;            // sumbu wajah: U sepanjang garis mata, V ke bawah
    public float Roll;              // radian
    public float FaceW, FaceH;
    public float Yaw;               // kira-kira -1..1 (menoleh)
    public float MouthOpen;         // jarak bibir dalam / lebar mulut

    // Sumbu wajah 3D (satuan): U ke kanan wajah, V ke bawah, N menghadap kamera. Dipakai agar stiker ikut menoleh/mengangguk.
    public Vector3 U3 = Vector3.UnitX, V3 = Vector3.UnitY, N3 = -Vector3.UnitZ;
    public Vector2 U3xy => new(U3.X, U3.Y);
    public Vector2 V3xy => new(V3.X, V3.Y);
    public Vector2 N3xy => new(N3.X, N3.Y);

    public static FaceData From(Vector2[] p, float[]? z = null)
    {
        var f = new FaceData { P = p };
        var a = p.Length > 468 ? p[468] : (p[33] + p[133]) * 0.5f;
        var b = p.Length > 473 ? p[473] : (p[362] + p[263]) * 0.5f;
        if (a.X > b.X) (a, b) = (b, a);
        f.EyeA = a;
        f.EyeB = b;

        var u = b - a;
        if (u.LengthSquared() < 1e-3f) u = new Vector2(1, 0);
        u = Vector2.Normalize(u);
        f.U = u;
        f.V = new Vector2(-u.Y, u.X);
        f.Roll = MathF.Atan2(u.Y, u.X);

        f.FaceW = Math.Max(1f, Vector2.Distance(p[234], p[454]));
        f.FaceH = Math.Max(1f, Vector2.Distance(p[10], p[152]));

        var cheekMid = (p[234] + p[454]) * 0.5f;
        float lateral = Vector2.Dot(p[1] - cheekMid, u) / f.FaceW;
        f.Yaw = Math.Clamp(lateral * 3f, -1f, 1f);

        f.MouthOpen = Vector2.Distance(p[13], p[14]) / Math.Max(1f, Vector2.Distance(p[61], p[291]));

        // --- orientasi 3D dari kedalaman landmark ---
        float[] zr = (z != null && z.Length == p.Length) ? z : new float[p.Length];
        f.Zraw = zr;
        // Hidung selalu lebih dekat ke kamera daripada pipi: dari situ konvensi tanda kedalaman dideteksi otomatis.
        float noseDepth = zr[1] - 0.5f * (zr[234] + zr[454]);
        float sign = noseDepth <= 0 ? 1f : -1f;
        Vector3 Pt(int i) => new(p[i].X, p[i].Y, sign * zr[i]);

        var a3 = Pt(454) - Pt(234);
        float axy = new Vector2(a3.X, a3.Y).Length();
        a3.Z = Math.Clamp(a3.Z, -0.9f * axy, 0.9f * axy);
        var u3 = Vector3.Normalize(a3);

        var b3 = Pt(152) - Pt(10);
        float bxy = new Vector2(b3.X, b3.Y).Length();
        b3.Z = Math.Clamp(b3.Z, -0.8f * bxy, 0.8f * bxy);
        b3 -= Vector3.Dot(b3, u3) * u3;
        var v3 = Vector3.Normalize(b3);

        f.U3 = u3;
        f.V3 = v3;
        f.N3 = -Vector3.Cross(u3, v3);
        f.Yaw = Math.Clamp(u3.Z, -1f, 1f);
        return f;
    }

    public FaceData Scaled(float k)
    {
        var q = new Vector2[P.Length];
        var zz = new float[Zraw.Length];
        for (int i = 0; i < q.Length; i++) q[i] = P[i] * k;
        for (int i = 0; i < zz.Length; i++) zz[i] = Zraw[i] * k;
        return From(q, zz);
    }

    public Vector2 Mid(int a, int b) => (P[a] + P[b]) * 0.5f;
}

/// <summary>
/// Pipeline ala MediaPipe: YuNet (OpenCV) mencari wajah saat pelacakan hilang, lalu model Face Mesh
/// (ONNX) dijalankan lewat ONNX Runtime milik Windows ML pada NPU / GPU / CPU. Frame berikutnya memakai
/// kotak dari landmark sebelumnya sehingga deteksi hanya berjalan saat wajah baru muncul.
/// </summary>
public sealed class FaceTracker : IDisposable
{
    const int InSize = 256;

    sealed record ModelIo(string Input, string Landmarks, string Presence, bool Nchw);

    readonly object sync = new();
    InferenceSession? session;
    ModelIo? io;
    OpenCvSharp.Dnn.Net? yunet;
    string yunetPath = "";

    readonly float[] hwc = new float[InSize * InSize * 3];
    readonly float[] chw = new float[InSize * InSize * 3];

    bool hasRoi;
    double rcx, rcy, rsize, rangle;
    Vector2[]? smooth;
    float[]? smoothZ;
    float[] lastZ = Array.Empty<float>();
    float lastAlpha = 1f;

    public bool IsReady => session != null;
    public string BackendLabel { get; private set; } = "";
    public double LastInferenceMs { get; private set; }
    public string LastError { get; private set; } = "";

    // ------------------------------------------------------------------ inisialisasi

    /// <summary>Membuat sesi inferensi. Urutan Auto: NPU, GPU (DirectML dulu), lalu CPU. Aman dipanggil dari thread lain.</summary>
    public bool Initialize(string landmarkPath, string yunetModelPath, AiPreference pref)
    {
        LastError = "";
        var env = OrtEnv.Instance();

        List<OrtEpDevice> devs;
        try { devs = env.GetEpDevices().ToList(); }
        catch { devs = new List<OrtEpDevice>(); }

        var cands = new List<(string Label, OrtEpDevice? Dev)>();
        if (pref is AiPreference.Auto or AiPreference.Npu)
            foreach (var d in devs.Where(d => d.HardwareDevice.Type == OrtHardwareDeviceType.NPU))
                cands.Add(("NPU · " + Short(d.EpName), d));

        if (pref is AiPreference.Auto or AiPreference.Gpu)
            foreach (var d in devs.Where(d => d.HardwareDevice.Type == OrtHardwareDeviceType.GPU)
                                  .OrderBy(d => d.EpName.Contains("Dml", StringComparison.OrdinalIgnoreCase) ? 0 : 1))
                cands.Add(("GPU · " + Short(d.EpName), d));

        cands.Add(("CPU", null));

        foreach (var (label, dev) in cands)
        {
            InferenceSession? s = null;
            try
            {
                using var opts = new SessionOptions();
                if (dev != null)
                {
                    opts.EnableMemoryPattern = false;
                    opts.AppendExecutionProvider(env, new[] { dev }, new Dictionary<string, string>());
                }

                s = new InferenceSession(landmarkPath, opts);
                var mio = Inspect(s);
                if (mio == null)
                {
                    LastError = "Model titik wajah tidak cocok (keluaran 1434 nilai tidak ditemukan).";
                    s.Dispose();
                    continue;
                }

                var zeros = new float[InSize * InSize * 3];
                RunModel(s, mio, zeros, out _, out _);          // pemanasan (kompilasi pertama bisa lama di NPU)
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < 3; i++) RunModel(s, mio, zeros, out _, out _);
                double ms = sw.Elapsed.TotalMilliseconds / 3;

                InferenceSession? old;
                lock (sync)
                {
                    old = session;
                    session = s;
                    io = mio;
                    yunetPath = yunetModelPath;
                    yunet?.Dispose();
                    yunet = null;
                    hasRoi = false;
                    smooth = null; smoothZ = null;
                    BackendLabel = label;
                    LastInferenceMs = ms;
                }
                old?.Dispose();
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                s?.Dispose();
            }
        }
        return false;
    }

    static string Short(string ep) => ep.Replace("ExecutionProvider", "", StringComparison.OrdinalIgnoreCase);

    static ModelIo? Inspect(InferenceSession s)
    {
        string inName = s.InputMetadata.Keys.First();
        var dims = s.InputMetadata[inName].Dimensions;
        bool nchw = dims.Length == 4 && dims[1] == 3;

        string lm = "";
        foreach (var kv in s.OutputMetadata)
        {
            long n = 1;
            foreach (var d in kv.Value.Dimensions) n *= d > 0 ? d : 1;
            if (n == 1434 && lm == "") lm = kv.Key;
        }
        if (lm == "") return null;

        string pr = "";
        if (s.OutputMetadata.ContainsKey("Identity_1") && lm != "Identity_1") pr = "Identity_1";
        else
            foreach (var kv in s.OutputMetadata)
            {
                long n = 1;
                foreach (var d in kv.Value.Dimensions) n *= d > 0 ? d : 1;
                if (n == 1 && kv.Key != lm) { pr = kv.Key; break; }
            }
        return pr == "" ? null : new ModelIo(inName, lm, pr, nchw);
    }

    static void RunModel(InferenceSession s, ModelIo m, float[] data, out float[] landmarks, out float presenceLogit)
    {
        long[] shape = m.Nchw ? new long[] { 1, 3, InSize, InSize } : new long[] { 1, InSize, InSize, 3 };
        using var input = OrtValue.CreateTensorValueFromMemory(data, shape);
        var inputs = new Dictionary<string, OrtValue> { [m.Input] = input };
        using var run = new RunOptions();
        using var results = s.Run(run, inputs, new[] { m.Landmarks, m.Presence });
        landmarks = results[0].GetTensorDataAsSpan<float>().ToArray();
        presenceLogit = results[1].GetTensorDataAsSpan<float>()[0];
    }

    // ------------------------------------------------------------------ pelacakan

    /// <summary>Melacak satu wajah pada frame BGR. Mengembalikan null bila tidak ada wajah.</summary>
    public FaceData? Track(Mat bgr)
    {
        lock (sync)
        {
            if (session == null || io == null) return null;
            try
            {
                bool fresh = false;
                if (!hasRoi)
                {
                    if (!Detect(bgr)) { smooth = null; smoothZ = null; return null; }
                    fresh = true;
                }

                var pts = RunLandmarks(bgr);
                if (pts == null) { hasRoi = false; smooth = null; smoothZ = null; return null; }
                UpdateRoi(pts);

                if (fresh)   // putaran kedua dengan kotak yang sudah dihaluskan dari landmark
                {
                    var again = RunLandmarks(bgr);
                    if (again != null) { pts = again; UpdateRoi(pts); }
                }
                return FaceData.From(Smooth(pts), SmoothZ(lastZ));
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                hasRoi = false;
                smooth = null; smoothZ = null;
                return null;
            }
        }
    }

    /// <summary>Kotak wajah terbesar (piksel frame) dari detektor YuNet, tanpa mengubah status pelacakan.</summary>
    public bool TryFaceBox(Mat bgr, out Rect box)
    {
        box = default;
        lock (sync)
        {
            if (session == null) return false;
            var (a, b, c, d, h) = (rcx, rcy, rsize, rangle, hasRoi);
            bool ok = false;
            try
            {
                ok = Detect(bgr);
                if (ok)
                {
                    int side = (int)(rsize / 1.6);
                    box = new Rect((int)(rcx - side / 2.0), (int)(rcy - side / 2.0), side, side);
                }
            }
            catch { ok = false; }
            finally { (rcx, rcy, rsize, rangle, hasRoi) = (a, b, c, d, h); }
            return ok;
        }
    }

    static readonly string[] YuNetOutputs =
    {
        "cls_8", "cls_16", "cls_32", "obj_8", "obj_16", "obj_32",
        "bbox_8", "bbox_16", "bbox_32", "kps_8", "kps_16", "kps_32",
    };

    static float[] ReadFloats(Mat m)
    {
        var a = new float[(int)m.Total()];
        Marshal.Copy(m.Data, a, 0, a.Length);
        return a;
    }

    /// <summary>
    /// Mencari wajah dengan YuNet lewat modul DNN OpenCV. Keluaran 3 skala (stride 8/16/32) didekode manual,
    /// sama seperti implementasi resmi OpenCV (FaceDetectorYN), lalu wajah terbesar dipilih.
    /// </summary>
    bool Detect(Mat bgr)
    {
        if (!System.IO.File.Exists(yunetPath)) return false;
        yunet ??= OpenCvSharp.Dnn.CvDnn.ReadNetFromOnnx(yunetPath);

        double k = Math.Min(1.0, 640.0 / bgr.Cols);
        int w = (int)(Math.Ceiling(bgr.Cols * k / 32.0) * 32);
        int h = (int)(Math.Ceiling(bgr.Rows * k / 32.0) * 32);
        w = Math.Max(64, w);
        h = Math.Max(64, h);

        using var resized = new Mat();
        Cv2.Resize(bgr, resized, new Size(w, h));
        using var blob = OpenCvSharp.Dnn.CvDnn.BlobFromImage(resized, 1.0, new Size(w, h), new Scalar(), false, false);
        yunet.SetInput(blob);

        var outs = new Mat[YuNetOutputs.Length];
        for (int i = 0; i < outs.Length; i++) outs[i] = new Mat();
        try
        {
            yunet.Forward(outs, YuNetOutputs);

            int[] strides = { 8, 16, 32 };
            double bestArea = 0;
            double bx = 0, by = 0, bw = 0, bh = 0, rex = 0, rey = 0, lex = 0, ley = 0;
            bool found = false;

            for (int i = 0; i < 3; i++)
            {
                int stride = strides[i];
                int cols = w / stride, rows = h / stride;
                var cls = ReadFloats(outs[i]);
                var obj = ReadFloats(outs[i + 3]);
                var box = ReadFloats(outs[i + 6]);
                var kps = ReadFloats(outs[i + 9]);
                if (cls.Length < rows * cols || box.Length < rows * cols * 4 || kps.Length < rows * cols * 10) continue;

                for (int r = 0; r < rows; r++)
                    for (int c = 0; c < cols; c++)
                    {
                        int idx = r * cols + c;
                        float cs = Math.Clamp(cls[idx], 0f, 1f);
                        float os = Math.Clamp(obj[idx], 0f, 1f);
                        float score = MathF.Sqrt(cs * os);
                        if (score < 0.7f) continue;

                        double cx = (c + box[idx * 4]) * stride;
                        double cy = (r + box[idx * 4 + 1]) * stride;
                        double fw = Math.Exp(box[idx * 4 + 2]) * stride;
                        double fh = Math.Exp(box[idx * 4 + 3]) * stride;
                        double area = fw * fh;
                        if (area <= bestArea) continue;

                        bestArea = area;
                        found = true;
                        bx = cx - fw / 2; by = cy - fh / 2; bw = fw; bh = fh;
                        rex = (kps[idx * 10] + c) * stride;      rey = (kps[idx * 10 + 1] + r) * stride;
                        lex = (kps[idx * 10 + 2] + c) * stride;  ley = (kps[idx * 10 + 3] + r) * stride;
                    }
            }
            if (!found) return false;

            // kembali ke piksel frame asli
            double sx = bgr.Cols / (double)w, sy = bgr.Rows / (double)h;
            rcx = (bx + bw / 2) * sx;
            rcy = (by + bh / 2) * sy;
            rsize = Math.Max(bw * sx, bh * sy) * 1.6;
            rangle = Math.Atan2((ley - rey) * sy, (lex - rex) * sx);
            hasRoi = true;
            return true;
        }
        finally
        {
            foreach (var m in outs) m.Dispose();
        }
    }

    /// <summary>Memotong wajah (diputar agar mata rata), menjalankan model, lalu memetakan balik ke piksel frame.</summary>
    Vector2[]? RunLandmarks(Mat bgr)
    {
        double s = InSize / rsize;
        double phi = -rangle;
        double c = Math.Cos(phi), sn = Math.Sin(phi);
        double a = s * c, b = -s * sn, cc = s * sn, d = s * c;
        double tx = InSize / 2.0 - (a * rcx + b * rcy);
        double ty = InSize / 2.0 - (cc * rcx + d * rcy);

        using var m = Mat.FromArray(new double[,] { { a, b, tx }, { cc, d, ty } });
        using var crop = new Mat();
        Cv2.WarpAffine(bgr, crop, m, new Size(InSize, InSize), InterpolationFlags.Linear,
                       BorderTypes.Constant, Scalar.All(0));

        using var rgb = new Mat();
        Cv2.CvtColor(crop, rgb, ColorConversionCodes.BGR2RGB);
        using var f32 = new Mat();
        rgb.ConvertTo(f32, MatType.CV_32FC3, 1.0 / 255.0);
        Marshal.Copy(f32.Data, hwc, 0, hwc.Length);

        float[] input = hwc;
        if (io!.Nchw)
        {
            int plane = InSize * InSize;
            for (int i = 0; i < plane; i++)
            {
                chw[i] = hwc[i * 3];
                chw[plane + i] = hwc[i * 3 + 1];
                chw[2 * plane + i] = hwc[i * 3 + 2];
            }
            input = chw;
        }

        RunModel(session!, io, input, out var lm, out var logit);
        if (1.0 / (1.0 + Math.Exp(-logit)) < 0.5) return null;

        double det = a * d - b * cc;
        int n = lm.Length / 3;
        var pts = new Vector2[n];
        var zs = new float[n];
        for (int i = 0; i < n; i++)
        {
            zs[i] = (float)(lm[i * 3 + 2] / s);
            double px = lm[i * 3] - tx, py = lm[i * 3 + 1] - ty;
            pts[i] = new Vector2((float)((d * px - b * py) / det), (float)((-cc * px + a * py) / det));
        }
        lastZ = zs;
        return n >= 468 ? pts : null;
    }

    void UpdateRoi(Vector2[] p)
    {
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var v in p)
        {
            minX = Math.Min(minX, v.X); maxX = Math.Max(maxX, v.X);
            minY = Math.Min(minY, v.Y); maxY = Math.Max(maxY, v.Y);
        }
        rcx = (minX + maxX) / 2;
        rcy = (minY + maxY) / 2;
        rsize = Math.Max(maxX - minX, maxY - minY) * 1.5;
        rangle = Math.Atan2(p[263].Y - p[33].Y, p[263].X - p[33].X);
        hasRoi = rsize > 20;
    }

    static readonly int[] KeyPoints = { 1, 152, 33, 263, 10, 61, 291 };

    /// <summary>Penghalusan adaptif: gerakan kecil diredam kuat (anti getar), gerakan cepat tetap responsif.</summary>
    Vector2[] Smooth(Vector2[] raw)
    {
        if (smooth == null || smooth.Length != raw.Length)
        {
            smooth = (Vector2[])raw.Clone();
            return (Vector2[])smooth.Clone();
        }

        float faceW = Math.Max(1f, Vector2.Distance(raw[234], raw[454]));
        float disp = 0;
        foreach (int k in KeyPoints) disp += Vector2.Distance(raw[k], smooth[k]);
        disp /= KeyPoints.Length * faceW;

        float alpha = Math.Clamp(0.30f + disp * 10f, 0.30f, 0.95f);
        lastAlpha = alpha;
        for (int i = 0; i < raw.Length; i++) smooth[i] = Vector2.Lerp(smooth[i], raw[i], alpha);
        return (Vector2[])smooth.Clone();
    }

    float[] SmoothZ(float[] z)
    {
        if (smoothZ == null || smoothZ.Length != z.Length)
        {
            smoothZ = (float[])z.Clone();
            return (float[])smoothZ.Clone();
        }
        for (int i = 0; i < z.Length; i++) smoothZ[i] += (z[i] - smoothZ[i]) * lastAlpha;
        return (float[])smoothZ.Clone();
    }

    public void Reset()
    {
        lock (sync) { hasRoi = false; smooth = null; smoothZ = null; }
    }

    public void Dispose()
    {
        lock (sync)
        {
            session?.Dispose();
            session = null;
            yunet?.Dispose();
            yunet = null;
        }
    }
}
