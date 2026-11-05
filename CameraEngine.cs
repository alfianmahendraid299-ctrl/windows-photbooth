using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;

namespace PhotoBooth;

/// <summary>
/// Membaca kamera di thread terpisah, membalik (mirror) gambar seperti Photo Booth,
/// menerapkan efek, lalu mengirim frame ke UI. Juga menangani rekam video.
/// </summary>
public sealed class CameraEngine : IDisposable
{
    VideoCapture? cap;
    Thread? thread;
    volatile bool run;

    readonly object gate = new();    // melindungi 'last'
    readonly object wlock = new();   // melindungi 'writer'
    Mat? last;                       // frame terakhir (sudah mirror + efek)
    double measuredFps = 30;

    // rekaman video (AVI MJPEG + audio) dengan jeda
    AviMjpegWriter? avi;
    AudioRecorder? audioRec;
    readonly Stopwatch recClock = new();
    long recFrames;
    volatile bool recPaused;
    const int RecFps = 30;
    static readonly int[] JpegParams = { (int)ImwriteFlags.JpegQuality, 88 };

    public bool IsRecording => avi != null;
    public bool IsPaused => recPaused;
    public TimeSpan RecElapsed => recClock.Elapsed;

    // Auto filter AI berdasarkan scene + sensor telapak tangan
    public volatile bool AutoScene;
    public volatile bool PalmEnabled;
    public SceneClassifier Scene { get; } = new();
    readonly SceneGrader grader = new();
    readonly PalmDetector palm = new();
    public event Action<SceneKind>? SceneChanged;
    public event Action? PalmShot;
    volatile SceneKind currentScene = SceneKind.Normal;
    SceneKind candidateScene = SceneKind.Normal;
    int candidateCount;
    double lastSceneTick;
    int sceneBusy;
    int palmTick;

    public volatile EffectKind Effect = EffectKind.Normal;
    public volatile bool TilesActive;
    public volatile int TilePage;

    // Penyesuaian cahaya (perangkat lunak, berlaku untuk pratinjau, foto, dan video)
    public volatile int AdjBrightness;
    public volatile float AdjContrast = 1f;
    public volatile float AdjExposure;
    public volatile bool AutoLevel = true;
    double autoGamma = 1.0;
    Mat? lutMat;
    readonly byte[] lutBytes = new byte[256];

    /// <summary>Pelacak wajah untuk efek AR (NPU/GPU/CPU lewat Windows ML).</summary>
    public FaceTracker Tracker { get; } = new();

    /// <summary>0 = tidak perlu wajah, 1 = wajah terdeteksi, 2 = tidak ada wajah, 3 = AI belum siap.</summary>
    public event Action<int>? FaceState;
    int lastFaceState = -1;

    /// <summary>
    /// Frame siap tampil. Mat ini adalah buffer yang dipakai ulang: penerima WAJIB memanggil ReleaseFrame()
    /// setelah selesai menyalin (frame berikutnya dibuang selama belum dilepas, sehingga tanpa antrean/alokasi).
    /// </summary>
    public event Action<Mat>? FrameReady;
    Mat? uiMat;
    int uiBusy;
    public void ReleaseFrame(Mat frame)
    {
        if (ReferenceEquals(frame, uiMat)) Interlocked.Exchange(ref uiBusy, 0);
    }

    /// <summary>Profil mode yang dipakai, mis. "DSHOW|MJPG|1280|720" (disimpan agar tidak perlu uji lagi).</summary>
    public string Profile { get; private set; } = "";
    public string LastError { get; private set; } = "";

    // Kamera yang tidak bisa dilepas dengan aman (thread masih tertahan driver) dibiarkan hidup agar tidak crash.
    static readonly List<VideoCapture> Abandoned = new();

    // Hasil negosiasi mode kamera
    public int Width { get; private set; }
    public int Height { get; private set; }
    public double NegotiatedFps { get; private set; }
    public string Codec { get; private set; } = "";
    public double CurrentFps => measuredFps;

    static CameraEngine()
    {
        // Mempercepat pembukaan kamera lewat Media Foundation (lihat dokumentasi OpenCV)
        Environment.SetEnvironmentVariable("OPENCV_VIDEOIO_MSMF_ENABLE_HW_TRANSFORMS", "0");
    }
    public event Action<BitmapSource[]>? TilesReady;

    /// <summary>Mencari webcam yang tersedia (indeks 0..max-1). Panggil saat kamera belum dipakai.</summary>
    public static List<(int Index, string Name)> Probe(int max = 6)
    {
        var list = new List<(int Index, string Name)>();
        for (int i = 0; i < max; i++)
        {
            try
            {
                using var c = new VideoCapture(i, VideoCaptureAPIs.DSHOW);
                if (!c.IsOpened()) continue;
                list.Add((i, $"Kamera {list.Count + 1} (indeks {i})"));
            }
            catch { }
        }
        return list;
    }

    sealed record Negotiated(VideoCapture Cap, int W, int H, double Fps, string Codec, string Profile)
    {
        public double Score => W * (double)H * Math.Min(1.0, Fps / 24.0);
    }

    /// <summary>
    /// Menyalakan kamera. Prinsip: resolusi TIDAK PERNAH diubah pada kamera yang sudah menyala (DirectShow bisa
    /// membangun ulang grafnya, membuat lampu berkedip dan sebagian driver crash). Setiap percobaan membuka kamera
    /// baru dengan semua properti diatur sebelum frame pertama. Mode yang berhasil disimpan sehingga peluncuran
    /// berikutnya cukup membuka kamera satu kali. safeMode = satu percobaan saja (DirectShow, MJPG, 720p).
    /// maxHeight = 0 berarti otomatis (hingga 1080p).
    /// </summary>
    public bool Start(int index = 0, int maxHeight = 0, string? savedProfile = null, bool safeMode = false)
    {
        Stop();

        Negotiated? chosen = null;
        try
        {
            if (safeMode)
            {
                chosen = OpenProfile(index, VideoCaptureAPIs.DSHOW, true, 1280, 720, 10);
            }
            else
            {
                if (savedProfile != null && ParseProfile(savedProfile, out var api, out var mjpg, out var pw, out var ph)
                    && (maxHeight == 0 || ph <= maxHeight))
                {
                    chosen = OpenProfile(index, api, mjpg, pw, ph, 15);
                    if (chosen == null) Thread.Sleep(400);
                }
                chosen ??= Negotiate(index, maxHeight);
            }
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            chosen = null;
        }
        if (chosen == null) return false;

        cap = chosen.Cap;
        Width = chosen.W;
        Height = chosen.H;
        NegotiatedFps = chosen.Fps;
        Codec = chosen.Codec;
        Profile = chosen.Profile;
        measuredFps = chosen.Fps > 1 ? chosen.Fps : 30;

        Interlocked.Exchange(ref uiBusy, 0);
        uiMat = new Mat();          // buffer baru per sesi: aman dari sisa tampilan sesi sebelumnya

        run = true;
        thread = new Thread(Loop) { IsBackground = true, Name = "CameraLoop", Priority = ThreadPriority.AboveNormal };
        thread.Start();
        return true;
    }

    static bool ParseProfile(string text, out VideoCaptureAPIs api, out bool mjpg, out int w, out int h)
    {
        api = VideoCaptureAPIs.DSHOW; mjpg = true; w = h = 0;
        var p = text.Split('|');
        if (p.Length != 4 || !int.TryParse(p[2], out w) || !int.TryParse(p[3], out h)) return false;
        api = p[0] == "MSMF" ? VideoCaptureAPIs.MSMF : VideoCaptureAPIs.DSHOW;
        mjpg = p[1] == "MJPG";
        return w >= 160 && h >= 120;
    }

    /// <summary>Urutan percobaan: hentikan begitu ada mode yang cukup tinggi dan lancar.</summary>
    Negotiated? Negotiate(int index, int maxHeight)
    {
        var attempts = new List<(VideoCaptureAPIs Api, bool Mjpg, int W, int H)>();
        if (maxHeight == 0 || maxHeight >= 1080) attempts.Add((VideoCaptureAPIs.DSHOW, true, 1920, 1080));
        if (maxHeight == 0 || maxHeight >= 720)
        {
            attempts.Add((VideoCaptureAPIs.DSHOW, true, 1280, 720));
            attempts.Add((VideoCaptureAPIs.MSMF, false, 1280, 720));
        }
        attempts.Add((VideoCaptureAPIs.DSHOW, true, 640, 480));
        attempts.Add((VideoCaptureAPIs.DSHOW, false, 640, 480));

        var clock = Stopwatch.StartNew();
        (VideoCaptureAPIs Api, bool Mjpg, int W, int H)? bestParams = null;
        double bestScore = 0;

        foreach (var at in attempts)
        {
            if (clock.Elapsed.TotalSeconds > 12) break;

            var r = OpenProfile(index, at.Api, at.Mjpg, at.W, at.H, 12);
            if (r == null) { Thread.Sleep(400); continue; }

            // cukup bagus: tinggi minimal setara permintaan (maks. 720p) dan lancar
            if (r.Fps >= 22 && r.H >= Math.Min(at.H, 720) * 0.9)
                return r;

            if (r.Score > bestScore) { bestScore = r.Score; bestParams = at; }
            r.Cap.Dispose();
            Thread.Sleep(400);
        }

        // tidak ada yang "cukup bagus": buka ulang yang terbaik satu kali
        if (bestParams is { } b)
            return OpenProfile(index, b.Api, b.Mjpg, b.W, b.H, 8);
        return null;
    }

    /// <summary>Membuka kamera baru, mengatur format/resolusi/fps SEBELUM frame pertama, lalu mengukur fps nyata.</summary>
    static Negotiated? OpenProfile(int index, VideoCaptureAPIs api, bool mjpg, int w, int h, double minFps)
    {
        VideoCapture? c = null;
        try
        {
            c = new VideoCapture(index, api);
            if (!c.IsOpened()) { c.Dispose(); return null; }

            if (mjpg) c.Set(VideoCaptureProperties.FourCC, VideoWriter.FourCC('M', 'J', 'P', 'G'));
            c.Set(VideoCaptureProperties.FrameWidth, w);
            c.Set(VideoCaptureProperties.FrameHeight, h);
            c.Set(VideoCaptureProperties.Fps, 30);

            if (!MeasureMode(c, out int aw, out int ah, out double fps) || fps < minFps)
            {
                c.Dispose();
                return null;
            }

            string codec = (mjpg ? "MJPG" : "RAW") + (api == VideoCaptureAPIs.MSMF ? "/MF" : "");
            string profile = $"{(api == VideoCaptureAPIs.MSMF ? "MSMF" : "DSHOW")}|{(mjpg ? "MJPG" : "RAW")}|{w}|{h}";
            return new Negotiated(c, aw, ah, fps, codec, profile);
        }
        catch
        {
            c?.Dispose();
            return null;
        }
    }

    /// <summary>Membaca beberapa frame untuk memastikan mode aktif dan mengukur fps sebenarnya.</summary>
    static bool MeasureMode(VideoCapture c, out int w, out int h, out double fps)
    {
        w = h = 0;
        fps = 0;
        using var m = new Mat();

        int got = 0;
        for (int i = 0; i < 4; i++)
            if (c.Read(m) && !m.Empty()) got++;
        if (got == 0) return false;

        var sw = Stopwatch.StartNew();
        int n = 0;
        while (n < 10 && sw.ElapsedMilliseconds < 1500)
        {
            if (!c.Read(m) || m.Empty()) break;
            n++;
        }
        if (n < 3) return false;

        w = m.Width;
        h = m.Height;
        fps = n / Math.Max(0.001, sw.Elapsed.TotalSeconds);
        return w > 0 && h > 0;
    }

    /// <summary>Mematikan kamera (bisa dinyalakan lagi dengan Start).</summary>
    public void Stop()
    {
        run = false;
        bool exited = thread == null || thread.Join(6000);
        thread = null;
        StopRecording();

        if (cap != null)
        {
            if (exited)
            {
                cap.Release();
                cap.Dispose();
            }
            else
            {
                lock (Abandoned) Abandoned.Add(cap);   // thread masih tertahan di driver: jangan dilepas (hindari crash)
            }
            cap = null;
        }

        Interlocked.Exchange(ref uiBusy, 0);
        lock (gate)
        {
            last?.Dispose();
            last = null;
        }
    }

    void Loop()
    {
        var camera = cap!;
        using var raw = new Mat();
        var sw = Stopwatch.StartNew();
        long prev = 0;
        int n = 0;

        var ui = uiMat!;
        while (run)
        {
          try
          {
            if (!camera.Read(raw) || raw.Empty())
            {
                Thread.Sleep(10);
                continue;
            }

            // Estimasi FPS (rata-rata bergerak) untuk rekaman video
            long now = sw.ElapsedMilliseconds;
            if (prev != 0 && now > prev)
            {
                double fps = 1000.0 / (now - prev);
                measuredFps = measuredFps * 0.9 + fps * 0.1;
            }
            prev = now;

            using var mirrored = new Mat();
            Cv2.Flip(raw, mirrored, FlipMode.Y);
            ApplyAdjust(mirrored);

            var kind = Effect;
            var def = EffectLib.Get(kind);
            int page = TilePage;
            bool tilesOn = TilesActive;
            double nowSec = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

            // --- Auto filter AI berdasarkan scene (hanya saat efek Normal) ---
            if (!AutoScene && currentScene != SceneKind.Normal)
            {
                currentScene = SceneKind.Normal;
                candidateScene = SceneKind.Normal;
                candidateCount = 0;
                SceneChanged?.Invoke(SceneKind.Normal);
            }
            if (AutoScene)
            {
                if (Scene.IsReady && nowSec - lastSceneTick > 0.6 && Interlocked.CompareExchange(ref sceneBusy, 1, 0) == 0)
                {
                    lastSceneTick = nowSec;
                    int side = Math.Min(mirrored.Cols, mirrored.Rows);
                    using var crop = new Mat(mirrored, new Rect((mirrored.Cols - side) / 2, (mirrored.Rows - side) / 2, side, side));
                    var sm224 = new Mat();
                    Cv2.Resize(crop, sm224, new Size(224, 224), 0, 0, InterpolationFlags.Area);
                    var (luma, blue) = SceneInfo.Analyze(sm224);
                    bool hasFace = Tracker.IsReady && Tracker.TryFaceBox(mirrored, out _);

                    Task.Run(() =>
                    {
                        try
                        {
                            double fd = 0, ld = 0, pt = 0;
                            var r = Scene.Classify(sm224);
                            if (r is { } v) { fd = v.Food; ld = v.Landscape; pt = v.Pet; }
                            VoteScene(SceneInfo.Decide(luma, blue, fd, ld, pt, hasFace));
                        }
                        catch { }
                        finally
                        {
                            sm224.Dispose();
                            Interlocked.Exchange(ref sceneBusy, 0);
                        }
                    });
                }

                if (kind == EffectKind.Normal)
                {
                    grader.Apply(mirrored, currentScene);
                    if (currentScene == SceneKind.Portrait && Tracker.IsReady)
                    {
                        var pf = Tracker.Track(mirrored);
                        if (pf != null) ArEffects.ApplyBeauty(mirrored, pf);
                    }
                }
            }

            // --- Sensor telapak tangan (palm shoot) ---
            if (PalmEnabled && Tracker.IsReady && !IsRecording && (palmTick++ % 4 == 0))
            {
                Rect? fb = Tracker.TryFaceBox(mirrored, out var box) ? box : null;
                if (palm.Update(mirrored, fb, nowSec)) PalmShot?.Invoke();
            }

            // Wajah dilacak hanya bila diperlukan: efek aktif butuh wajah, atau panel efek menampilkan halaman AR.
            FaceData? face = null;
            bool needTrack = def.NeedsFace || (tilesOn && EffectLib.PageHasFace(page));
            if (needTrack && Tracker.IsReady) face = Tracker.Track(mirrored);

            int state = !def.NeedsFace ? 0 : !Tracker.IsReady ? 3 : face == null ? 2 : 1;
            if (state != lastFaceState)
            {
                lastFaceState = state;
                FaceState?.Invoke(state);
            }

            // Efek Normal tidak perlu salinan tambahan
            Mat processed = kind == EffectKind.Normal ? mirrored : EffectLib.Render(mirrored, kind, face);
            try
            {
                lock (gate)
                {
                    if (last == null || last.Rows != processed.Rows || last.Cols != processed.Cols || last.Type() != processed.Type())
                    {
                        last?.Dispose();
                        last = new Mat();
                    }
                    processed.CopyTo(last);   // memakai ulang buffer, tanpa alokasi baru
                }

                WriteRecordingFrame(processed);

                // Kirim ke UI hanya bila frame sebelumnya sudah dilepas (tanpa antrean => latensi rendah, gerak mulus)
                if (Interlocked.CompareExchange(ref uiBusy, 1, 0) == 0)
                {
                    processed.CopyTo(ui);
                    if (FrameReady != null) FrameReady.Invoke(ui);
                    else Interlocked.Exchange(ref uiBusy, 0);
                }
            }
            finally
            {
                if (!ReferenceEquals(processed, mirrored)) processed.Dispose();
            }

            // Pratinjau panel efek (9 tile halaman aktif); efek AR memakai wajah yang sama, diskalakan
            if (tilesOn && (n++ % 3 == 0))
            {
                int w = 224;
                int h = Math.Max(1, w * mirrored.Rows / Math.Max(1, mirrored.Cols));
                float k = w / (float)mirrored.Cols;
                using var small = new Mat();
                Cv2.Resize(mirrored, small, new Size(w, h));
                var faceSmall = face?.Scaled(k);

                var items = EffectLib.Page(page);
                var arr = new BitmapSource[items.Length];
                for (int i = 0; i < arr.Length; i++)
                {
                    using var t = EffectLib.Render(small, items[i].Kind, items[i].NeedsFace ? faceSmall : null);
                    arr[i] = ToBmp(t);
                }
                TilesReady?.Invoke(arr);
            }
          }
          catch (Exception ex)
          {
              LastError = ex.Message;   // satu frame bermasalah tidak boleh mematikan aplikasi
              Thread.Sleep(5);
          }
        }
    }

    /// <summary>
    /// Koreksi cahaya lewat satu tabel LUT: gamma (eksposur, termasuk koreksi otomatis untuk gambar terlalu terang),
    /// kontras, lalu kecerahan. Dilewati bila semuanya netral.
    /// </summary>
    void ApplyAdjust(Mat frame)
    {
        double gammaAuto = 1.0;
        if (AutoLevel)
        {
            using var small = new Mat();
            Cv2.Resize(frame, small, new Size(64, 36), 0, 0, InterpolationFlags.Area);
            var m = Cv2.Mean(small);
            double lum = 0.114 * m.Val0 + 0.587 * m.Val1 + 0.299 * m.Val2;
            // hanya meredam: bila rata-rata terlalu terang, cari gamma agar rata-rata mendekati ±118
            double target = lum > 125
                ? Math.Clamp(Math.Log(118.0 / 255.0) / Math.Log(Math.Max(lum, 1.0) / 255.0), 1.0, 1.5)
                : 1.0;
            autoGamma += (target - autoGamma) * 0.06;     // halus, tanpa berkedip
            gammaAuto = autoGamma;
        }
        else
        {
            autoGamma = 1.0;
        }

        double gamma = Math.Pow(2.0, -AdjExposure) * gammaAuto;
        double contrast = AdjContrast;
        int bright = AdjBrightness;
        if (Math.Abs(gamma - 1.0) < 0.01 && Math.Abs(contrast - 1.0) < 0.01 && bright == 0) return;

        for (int i = 0; i < 256; i++)
        {
            double v = Math.Pow(i / 255.0, gamma);
            v = (v - 0.5) * contrast + 0.5;
            v = v * 255.0 + bright;
            lutBytes[i] = (byte)Math.Clamp(Math.Round(v), 0, 255);
        }
        lutMat ??= new Mat(256, 1, MatType.CV_8UC1);
        Marshal.Copy(lutBytes, 0, lutMat.Data, 256);

        using var tmp = new Mat();
        Cv2.LUT(frame, lutMat, tmp);
        tmp.CopyTo(frame);
    }

    /// <summary>Salinan frame terakhir (pemanggil wajib Dispose).</summary>
    public Mat? Snapshot()
    {
        lock (gate) return last?.Clone();
    }

    /// <summary>Mulai merekam video (AVI MJPEG; ditambah audio mikrofon bila diminta dan tersedia).</summary>
    public bool StartRecording(string aviPath, bool withAudio, out bool audioOk)
    {
        audioOk = false;
        int w, h;
        lock (gate)
        {
            if (last == null) return false;
            w = last.Cols;
            h = last.Rows;
        }

        try
        {
            AudioRecorder? ar = null;
            AviMjpegWriter? target = null;
            if (withAudio)
            {
                ar = new AudioRecorder();
                audioOk = ar.Start((buf, n) => target?.WriteAudio(buf, n));
                if (!audioOk) { ar.Dispose(); ar = null; }
            }

            var wr = new AviMjpegWriter(aviPath, w, h, RecFps, audioOk);
            target = wr;
            lock (wlock)
            {
                avi = wr;
                audioRec = ar;
                recPaused = false;
                recFrames = 0;
                recClock.Restart();
            }
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return false;
        }
    }

    public void PauseRecording()
    {
        lock (wlock)
        {
            if (avi == null || recPaused) return;
            recPaused = true;
            recClock.Stop();
            audioRec?.Pause();
        }
    }

    public void ResumeRecording()
    {
        lock (wlock)
        {
            if (avi == null || !recPaused) return;
            recClock.Start();
            audioRec?.Resume();
            recPaused = false;
        }
    }

    public void StopRecording()
    {
        AviMjpegWriter? w;
        AudioRecorder? a;
        lock (wlock)
        {
            w = avi;
            a = audioRec;
            avi = null;
            audioRec = null;
            recPaused = false;
            recClock.Stop();
        }
        a?.Dispose();
        w?.Dispose();
    }

    /// <summary>
    /// Menulis frame sesuai jam rekaman (jam berhenti saat jeda), diulang bila kamera lebih lambat dari 30 fps,
    /// sehingga durasi video selalu sama dengan durasi audio.
    /// </summary>
    void WriteRecordingFrame(Mat frame)
    {
        lock (wlock)
        {
            if (avi == null || recPaused) return;
            long expected = (long)(recClock.Elapsed.TotalSeconds * RecFps) + 1;
            if (recFrames >= expected) return;
            if (!Cv2.ImEncode(".jpg", frame, out byte[] jpg, JpegParams)) return;
            long dup = Math.Min(4, expected - recFrames);
            for (long i = 0; i < dup; i++)
            {
                avi.WriteVideo(jpg);
                recFrames++;
            }
        }
    }

    void VoteScene(SceneKind sc)
    {
        if (sc == candidateScene) candidateCount++;
        else { candidateScene = sc; candidateCount = 1; }

        if (candidateCount >= 3 && sc != currentScene)   // 3 hasil berturut-turut sama = scene berganti
        {
            currentScene = sc;
            SceneChanged?.Invoke(sc);
        }
    }

    // ---------- Helper statis ----------

    public static BitmapSource ToBmp(Mat m)
    {
        var b = BitmapSourceConverter.ToBitmapSource(m);
        b.Freeze();
        return b;
    }

    /// <summary>Gabungkan 4 foto jadi grid 2x2 dengan bingkai putih (mode "4-up").</summary>
    public static Mat Compose2x2(IList<Mat> shots)
    {
        var tiles = shots.Select(s =>
        {
            var m = new Mat();
            Cv2.CopyMakeBorder(s, m, 6, 6, 6, 6, BorderTypes.Constant, Scalar.White);
            return m;
        }).ToList();

        using var top = new Mat();
        using var bottom = new Mat();
        Cv2.HConcat(tiles[0], tiles[1], top);
        Cv2.HConcat(tiles[2], tiles[3], bottom);

        var result = new Mat();
        Cv2.VConcat(top, bottom, result);

        foreach (var t in tiles) t.Dispose();
        return result;
    }

    /// <summary>Frame pertama sebuah video, untuk thumbnail.</summary>
    public static BitmapSource? VideoFirstFrame(string path)
    {
        try
        {
            using var vc = new VideoCapture(path);
            using var m = new Mat();
            if (vc.IsOpened() && vc.Read(m) && !m.Empty()) return ToBmp(m);
        }
        catch { }
        return null;
    }

    public void Dispose()
    {
        Stop();
        Tracker.Dispose();
        Scene.Dispose();
    }
}
