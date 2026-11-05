using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.ML.OnnxRuntime;
using OpenCvSharp;

namespace PhotoBooth;

public enum SceneKind { Normal, Food, Landscape, Sky, Night, Portrait, Pet }

public static class SceneInfo
{
    public static string Label(SceneKind k) => k switch
    {
        SceneKind.Food => "Makanan",
        SceneKind.Landscape => "Pemandangan",
        SceneKind.Sky => "Langit",
        SceneKind.Night => "Malam",
        SceneKind.Portrait => "Potret",
        SceneKind.Pet => "Hewan",
        _ => "Normal",
    };

    /// <summary>Keputusan akhir dari probabilitas kelas ImageNet + heuristik cahaya/warna langit + ada tidaknya wajah.</summary>
    public static SceneKind Decide(double luma, double blueTop, double food, double landscape, double pet, bool face)
    {
        if (luma < 55) return SceneKind.Night;
        if (face) return SceneKind.Portrait;
        if (food >= 0.30) return SceneKind.Food;
        if (pet >= 0.35) return SceneKind.Pet;
        if (landscape >= 0.30) return SceneKind.Landscape;
        if (blueTop >= 0.45) return SceneKind.Sky;
        return SceneKind.Normal;
    }

    /// <summary>Rata-rata luminansi (0..255) dan porsi piksel biru di separuh atas gambar (indikasi langit).</summary>
    public static (double Luma, double BlueTop) Analyze(Mat bgrSmall)
    {
        var m = Cv2.Mean(bgrSmall);
        double luma = 0.114 * m.Val0 + 0.587 * m.Val1 + 0.299 * m.Val2;

        int topRows = Math.Max(2, (int)(bgrSmall.Rows * 0.45));
        using var top = new Mat(bgrSmall, new Rect(0, 0, bgrSmall.Cols, topRows));
        using var hsv = new Mat();
        Cv2.CvtColor(top, hsv, ColorConversionCodes.BGR2HSV);
        using var mask = new Mat();
        Cv2.InRange(hsv, new Scalar(95, 45, 100), new Scalar(130, 255, 255), mask);
        double blue = Cv2.CountNonZero(mask) / (double)(mask.Rows * mask.Cols);
        return (luma, blue);
    }
}

/// <summary>
/// Klasifikasi gambar MobileNetV2 (ImageNet, 1000 kelas) lewat ONNX Runtime milik Windows ML, pada NPU / GPU / CPU.
/// Kelas-kelas ImageNet dikelompokkan menjadi: makanan, pemandangan, hewan peliharaan.
/// </summary>
public sealed class SceneClassifier : IDisposable
{
    const int Size = 224;

    readonly object sync = new();
    InferenceSession? session;
    string inputName = "", outputName = "";
    readonly float[] hwc = new float[Size * Size * 3];
    readonly float[] chw = new float[Size * Size * 3];

    public bool IsReady => session != null;
    public string BackendLabel { get; private set; } = "";
    public string LastError { get; private set; } = "";

    static readonly HashSet<int> FoodIdx = BuildFood();
    static readonly HashSet<int> LandIdx = new() { 970, 972, 973, 974, 975, 976, 977, 978, 979, 980 };

    static HashSet<int> BuildFood()
    {
        var s = new HashSet<int>();
        for (int i = 923; i <= 969; i++) s.Add(i);                 // plate ... eggnog (makanan & minuman)
        foreach (int i in new[] { 532, 567, 809, 868, 909 }) s.Add(i);   // dining table, frying pan, soup bowl, tray, wok
        return s;
    }

    static string Short(string ep) => ep.Replace("ExecutionProvider", "", StringComparison.OrdinalIgnoreCase);

    public bool Initialize(string modelPath, AiPreference pref)
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
                s = new InferenceSession(modelPath, opts);

                string inName = s.InputMetadata.Keys.First();
                string outName = s.OutputMetadata.Keys.First();
                var warm = new float[Size * Size * 3];
                RunRaw(s, inName, outName, warm);   // pemanasan / uji perangkat

                InferenceSession? old;
                lock (sync)
                {
                    old = session;
                    session = s;
                    inputName = inName;
                    outputName = outName;
                    BackendLabel = label;
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

    static float[] RunRaw(InferenceSession s, string inName, string outName, float[] chwData)
    {
        using var input = OrtValue.CreateTensorValueFromMemory(chwData, new long[] { 1, 3, Size, Size });
        var inputs = new Dictionary<string, OrtValue> { [inName] = input };
        using var run = new RunOptions();
        using var results = s.Run(run, inputs, new[] { outName });
        return results[0].GetTensorDataAsSpan<float>().ToArray();
    }

    /// <summary>bgr224 harus berukuran 224x224 BGR 8-bit. Mengembalikan peluang (makanan, pemandangan, hewan).</summary>
    public (double Food, double Landscape, double Pet)? Classify(Mat bgr224)
    {
        lock (sync)
        {
            if (session == null) return null;
            try
            {
                using var rgb = new Mat();
                Cv2.CvtColor(bgr224, rgb, ColorConversionCodes.BGR2RGB);
                using var f32 = new Mat();
                rgb.ConvertTo(f32, MatType.CV_32FC3, 1.0 / 255.0);
                Marshal.Copy(f32.Data, hwc, 0, hwc.Length);

                int plane = Size * Size;
                for (int i = 0; i < plane; i++)
                {
                    chw[i] = (hwc[i * 3] - 0.485f) / 0.229f;
                    chw[plane + i] = (hwc[i * 3 + 1] - 0.456f) / 0.224f;
                    chw[2 * plane + i] = (hwc[i * 3 + 2] - 0.406f) / 0.225f;
                }

                var logits = RunRaw(session, inputName, outputName, chw);
                if (logits.Length < 1000) return null;

                // softmax stabil
                float max = float.MinValue;
                for (int i = 0; i < 1000; i++) max = Math.Max(max, logits[i]);
                double sum = 0;
                var p = new double[1000];
                for (int i = 0; i < 1000; i++) { p[i] = Math.Exp(logits[i] - max); sum += p[i]; }

                double food = 0, land = 0, pet = 0;
                for (int i = 0; i < 1000; i++)
                {
                    double v = p[i] / sum;
                    if (FoodIdx.Contains(i)) food += v;
                    else if (LandIdx.Contains(i)) land += v;
                    else if ((i >= 151 && i <= 268) || (i >= 281 && i <= 285)) pet += v;
                }
                return (food, land, pet);
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return null;
            }
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            session?.Dispose();
            session = null;
        }
    }
}

/// <summary>
/// Penata warna otomatis ala "AI camera": tiap scene punya parameter saturasi, keseimbangan warna, gamma, dan kontras.
/// Parameter berpindah halus (tanpa lompatan) saat scene berganti.
/// </summary>
public sealed class SceneGrader
{
    struct Grade
    {
        public double Sat, R, G, B, Gamma, Contrast;
        public static Grade Identity => new() { Sat = 1, R = 1, G = 1, B = 1, Gamma = 1, Contrast = 1 };
    }

    static Grade Target(SceneKind k) => k switch
    {
        SceneKind.Food      => new Grade { Sat = 1.30, R = 1.05, G = 1.00, B = 0.95, Gamma = 0.95, Contrast = 1.06 },
        SceneKind.Landscape => new Grade { Sat = 1.35, R = 1.00, G = 1.02, B = 1.02, Gamma = 1.00, Contrast = 1.10 },
        SceneKind.Sky       => new Grade { Sat = 1.35, R = 0.97, G = 1.00, B = 1.08, Gamma = 1.05, Contrast = 1.12 },
        SceneKind.Night     => new Grade { Sat = 1.10, R = 1.00, G = 1.00, B = 1.00, Gamma = 0.65, Contrast = 1.05 },
        SceneKind.Portrait  => new Grade { Sat = 1.05, R = 1.03, G = 1.00, B = 0.98, Gamma = 0.97, Contrast = 1.00 },
        SceneKind.Pet       => new Grade { Sat = 1.12, R = 1.00, G = 1.00, B = 1.00, Gamma = 1.00, Contrast = 1.05 },
        _ => Grade.Identity,
    };

    Grade cur = Grade.Identity;
    Mat? lut;
    readonly byte[] lutBytes = new byte[256];

    static double L(double a, double b, double t) => a + (b - a) * t;

    public void Apply(Mat frame, SceneKind scene)
    {
        var t = Target(scene);
        const double k = 0.08;   // kecepatan perpindahan per frame
        cur = new Grade
        {
            Sat = L(cur.Sat, t.Sat, k), R = L(cur.R, t.R, k), G = L(cur.G, t.G, k), B = L(cur.B, t.B, k),
            Gamma = L(cur.Gamma, t.Gamma, k), Contrast = L(cur.Contrast, t.Contrast, k),
        };

        bool neutral = Math.Abs(cur.Sat - 1) < 0.01 && Math.Abs(cur.R - 1) < 0.01 && Math.Abs(cur.G - 1) < 0.01 &&
                       Math.Abs(cur.B - 1) < 0.01 && Math.Abs(cur.Gamma - 1) < 0.01 && Math.Abs(cur.Contrast - 1) < 0.01;
        if (neutral) return;

        // saturasi + keseimbangan warna dalam satu transformasi matriks (urutan B,G,R)
        double[] w = { 0.114, 0.587, 0.299 };
        double[] gain = { cur.B, cur.G, cur.R };
        var m = new float[3, 3];
        for (int c = 0; c < 3; c++)
            for (int j = 0; j < 3; j++)
                m[c, j] = (float)(gain[c] * ((1 - cur.Sat) * w[j] + (j == c ? cur.Sat : 0)));

        using var kernel = Mat.FromArray(m);
        using var tmp = new Mat();
        Cv2.Transform(frame, tmp, kernel);

        // gamma + kontras lewat LUT
        for (int i = 0; i < 256; i++)
        {
            double v = Math.Pow(i / 255.0, cur.Gamma);
            v = (v - 0.5) * cur.Contrast + 0.5;
            lutBytes[i] = (byte)Math.Clamp(Math.Round(v * 255.0), 0, 255);
        }
        lut ??= new Mat(256, 1, MatType.CV_8UC1);
        Marshal.Copy(lutBytes, 0, lut.Data, 256);
        Cv2.LUT(tmp, lut, frame);
    }
}
