using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Media;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using OpenCvSharp.WpfExtensions;
using System.Windows.Threading;
using CvMat = OpenCvSharp.Mat;

namespace PhotoBooth;

public record ThumbItem(string Path, BitmapSource Thumb, bool IsVideo);

public partial class MainWindow : Window
{
    enum Mode { Four, Single, Video }

    readonly CameraEngine engine = new();
    readonly ObservableCollection<ThumbItem> items = new();
    static readonly string DefaultFolder = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Photo Booth");
    string folder => string.IsNullOrWhiteSpace(settings.SaveFolder) ? DefaultFolder : settings.SaveFolder!;
    readonly Image[] tileImages = new Image[EffectLib.PerPage];
    readonly TextBlock[] tileLabels = new TextBlock[EffectLib.PerPage];
    readonly AppSettings settings = AppSettings.Load();
    int tilePage;
    bool aiBusy;

    // pulau galeri & timer
    int timerSeconds = 3;
    bool adjReady, adjOpen;
    DispatcherTimer? adjSaveTimer;
    bool startSafe;   // true bila peluncuran sebelumnya crash saat membuka kamera
    bool islandOpen, islandPinned;
    int hoverCount, islandToken, viewerToken;
    DispatcherTimer? islandTimer;

    bool busy;
    bool recording;
    bool suppress;
    Mode mode = Mode.Four;

    const double SegW = 96;      // lebar satu segmen slider mode
    bool effectsOpen;
    bool draggingMode;
    bool dragMoved;
    double downX;

    List<(int Index, string Name)> cameras = new();
    int currentCam = -1;
    CancellationTokenSource? cts;

    string recPath = "";
    DateTime recStart;
    DispatcherTimer? recTimer;

    public MainWindow()
    {
        InitializeComponent();
    }

    // ------------------------------------------------------------ startup / shutdown

    async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(folder);
        BuildTiles();
        Thumbs.ItemsSource = items;
        timerSeconds = settings.TimerSeconds is 0 or 3 or 5 or 10 ? settings.TimerSeconds : 3;
        startSafe = settings.CamGuard;      // flag masih menyala = sebelumnya crash saat menegosiasikan kamera
        if (startSafe) settings.CamProfile = null;
        items.CollectionChanged += (_, _) => UpdateGallery();
        UpdateGallery();
        UpdateTimerButton();
        BuildDots();
        UpdatePageUi();
        SetModeIndex(0, false);

        AdjBright.Value = settings.AdjBrightness;
        AdjContrast.Value = Math.Clamp(settings.AdjContrast, 0.6, 1.6);
        AdjExposure.Value = Math.Clamp(settings.AdjExposure, -1.0, 1.0);
        adjReady = true;
        ApplyAdjustUi(false);

        engine.SceneChanged += OnSceneChanged;
        engine.PalmShot += OnPalmShot;
        LoadSettingsUi();
        if (settings.CheeseShoot) _ = UpdateCheeseAsync();
        if (settings.AiConsent && (settings.AutoScene || settings.PalmShoot)) _ = EnsureAiAsync();
        engine.FaceState += OnFaceState;
        if (!settings.AiConsent) AiChipText.Text = "AI · belum aktif";

        engine.FrameReady += OnFrame;
        engine.TilesReady += OnTiles;

        cameras = await Task.Run(() => CameraEngine.Probe());
        if (cameras.Count > 0)
            await StartCameraAsync(cameras[0].Index);
        else
            NoCamera.Visibility = Visibility.Visible;

        await LoadThumbsAsync();
    }

    void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (recording) engine.StopRecording();
        cheese?.Dispose();
        settings.CamGuard = false;
        settings.Save();
        engine.Dispose();
    }

    // ------------------------------------------------------------ frame dari kamera

    WriteableBitmap? previewBmp;

    /// <summary>Menyalin frame ke satu WriteableBitmap yang dipakai ulang (tanpa membuat bitmap baru tiap frame).</summary>
    void OnFrame(CvMat frame)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                int w = frame.Width, h = frame.Height;
                if (previewBmp == null || previewBmp.PixelWidth != w || previewBmp.PixelHeight != h)
                    previewBmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgr24, null);
                if (!ReferenceEquals(Preview.Source, previewBmp)) Preview.Source = previewBmp;
                WriteableBitmapConverter.ToWriteableBitmap(frame, previewBmp);
            }
            catch { }
            finally { engine.ReleaseFrame(frame); }
        }), DispatcherPriority.Render);
    }

    void OnTiles(BitmapSource[] arr)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!effectsOpen) return;
            for (int i = 0; i < arr.Length && i < tileImages.Length; i++)
                tileImages[i].Source = arr[i];
        }));
    }

    // ------------------------------------------------------------ pilih kamera

    async Task<bool> StartCameraAsync(int index)
    {
        int maxH = settings.CameraHeight;
        string? saved = settings.CamProfileIndex == index && settings.CamProfileHeight == maxH ? settings.CamProfile : null;
        bool safe = startSafe;

        settings.CamGuard = true;      // bila proses mati di tengah jalan, peluncuran berikutnya memakai mode aman
        settings.Save();
        bool ok = await Task.Run(() => engine.Start(index, maxH, saved, safe));
        settings.CamGuard = false;

        if (ok)
        {
            settings.CamProfile = engine.Profile;
            settings.CamProfileIndex = index;
            settings.CamProfileHeight = maxH;
            startSafe = false;
        }
        else if (saved != null)
        {
            settings.CamProfile = null;
        }
        settings.Save();

        NoCamera.Visibility = ok ? Visibility.Collapsed : Visibility.Visible;
        currentCam = ok ? index : -1;
        if (ok) UpdateGrid();
        if (ok) ShowToast((safe ? "Mode aman · " : "") + $"{engine.Width}×{engine.Height} · {engine.NegotiatedFps:0} fps · {engine.Codec}");
        return ok;
    }

    void Camera_Click(object sender, RoutedEventArgs e)
    {
        if (busy || recording) return;

        var menu = new ContextMenu();
        foreach (var cam in cameras)
        {
            var mi = new MenuItem { Header = cam.Name, IsChecked = cam.Index == currentCam, Tag = cam.Index };
            mi.Click += CameraItem_Click;
            menu.Items.Add(mi);
        }
        if (cameras.Count == 0)
            menu.Items.Add(new MenuItem { Header = "(kamera tidak ditemukan)", IsEnabled = false });

        menu.Items.Add(new Separator());
        var res = new MenuItem { Header = "Resolusi kamera" };
        foreach (var (label, h) in new[] { ("Otomatis (maksimal perangkat, hingga 1080p)", 0), ("1080p", 1080), ("720p", 720), ("480p", 480) })
        {
            var item = new MenuItem { Header = label, IsChecked = settings.CameraHeight == h, Tag = h };
            item.Click += CameraRes_Click;
            res.Items.Add(item);
        }
        menu.Items.Add(res);
        var renego = new MenuItem { Header = "Cari ulang mode kamera terbaik" };
        renego.Click += async (_, _) =>
        {
            if (busy || recording || currentCam < 0) return;
            busy = true;
            try
            {
                settings.CamProfile = null;
                Preview.Source = null;
                await StartCameraAsync(currentCam);
            }
            finally { busy = false; }
        };
        menu.Items.Add(renego);
        var refresh = new MenuItem { Header = "Segarkan daftar kamera" };
        refresh.Click += RefreshCameras_Click;
        menu.Items.Add(refresh);

        menu.PlacementTarget = (UIElement)sender;
        menu.Placement = PlacementMode.Top;
        menu.IsOpen = true;
    }

    async void CameraItem_Click(object sender, RoutedEventArgs e)
    {
        if (busy || recording) return;
        if (sender is not MenuItem { Tag: int idx } || idx == currentCam) return;

        busy = true;
        try
        {
            ShowEffects(false);
            HideViewer();
            Preview.Source = null;
            if (await StartCameraAsync(idx))
                ShowToast(cameras.First(c => c.Index == idx).Name);
            else
                MessageBox.Show("Kamera tidak bisa dibuka. Mungkin sedang dipakai aplikasi lain.",
                                "Photo Booth", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { busy = false; }
    }

    async void CameraRes_Click(object sender, RoutedEventArgs e)
    {
        if (busy || recording || sender is not MenuItem { Tag: int h }) return;
        settings.CameraHeight = h;
        settings.CamProfile = null;
        settings.Save();
        if (currentCam < 0) return;

        busy = true;
        try
        {
            Preview.Source = null;
            await StartCameraAsync(currentCam);
        }
        finally { busy = false; }
    }

    async void RefreshCameras_Click(object sender, RoutedEventArgs e)
    {
        if (busy || recording) return;

        busy = true;
        try
        {
            int previous = currentCam;
            Preview.Source = null;
            cameras = await Task.Run(() =>
            {
                engine.Stop(); // kamera harus dilepas dulu agar bisa dipindai ulang
                return CameraEngine.Probe();
            });

            if (cameras.Count == 0)
            {
                currentCam = -1;
                NoCamera.Visibility = Visibility.Visible;
                return;
            }

            int target = cameras.Any(c => c.Index == previous) ? previous : cameras[0].Index;
            await StartCameraAsync(target);
        }
        finally { busy = false; }
    }

    // ------------------------------------------------------------ panel efek

    void BuildTiles()
    {
        for (int i = 0; i < EffectLib.PerPage; i++)
        {
            var img = new Image { Stretch = Stretch.UniformToFill };
            tileImages[i] = img;

            var label = new TextBlock
            {
                Foreground = Brushes.White,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Padding = new Thickness(12, 4, 12, 4),
                Background = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 10),
            };
            tileLabels[i] = label;

            var grid = new Grid();
            grid.Children.Add(img);
            grid.Children.Add(label);
            grid.SizeChanged += (s2, e2) =>
                grid.Clip = new RectangleGeometry(new Rect(0, 0, grid.ActualWidth, grid.ActualHeight), 16, 16);

            var tile = new Border
            {
                Margin = new Thickness(6),
                Background = Brushes.Black,
                CornerRadius = new CornerRadius(18),
                BorderThickness = new Thickness(1.5),
                BorderBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)),
                Cursor = Cursors.Hand,
                Child = grid,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(1, 1),
            };
            tile.MouseLeftButtonUp += Tile_Click;
            tile.MouseEnter += (s2, e2) => ScaleTile(tile, 1.03);
            tile.MouseLeave += (s2, e2) => ScaleTile(tile, 1.0);
            TilesGrid.Children.Add(tile);
        }
    }

    // ------------------------------------------------------------ halaman efek

    void BuildDots()
    {
        PageDots.Children.Clear();
        for (int i = 0; i < EffectLib.PageCount; i++)
            PageDots.Children.Add(new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, Margin = new Thickness(4, 0, 4, 0) });
    }

    void UpdatePageUi()
    {
        var items = EffectLib.Page(tilePage);
        for (int i = 0; i < EffectLib.PerPage; i++)
        {
            var tile = (Border)TilesGrid.Children[i];
            bool has = i < items.Length;
            tile.Visibility = has ? Visibility.Visible : Visibility.Hidden;
            tile.Tag = has ? items[i].Kind : null;
            tileLabels[i].Text = has ? items[i].Name : "";
            tileImages[i].Source = null;
        }

        PageTitle.Text = $"{EffectLib.PageTitles[tilePage]}   ({tilePage + 1}/{EffectLib.PageCount})";
        for (int i = 0; i < PageDots.Children.Count; i++)
            ((System.Windows.Shapes.Ellipse)PageDots.Children[i]).Fill =
                i == tilePage ? Brushes.White : new SolidColorBrush(Color.FromArgb(90, 255, 255, 255));

        engine.TilePage = tilePage;
        UpdateTileSelection();
    }

    void ChangePage(int delta)
    {
        tilePage = (tilePage + delta + EffectLib.PageCount) % EffectLib.PageCount;
        UpdatePageUi();
        TilesGrid.BeginAnimation(OpacityProperty, new DoubleAnimation(0.25, 1, TimeSpan.FromMilliseconds(260)));
        if (EffectLib.PageHasFace(tilePage)) _ = EnsureAiAsync();
    }

    void PageNav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string t } && int.TryParse(t, out int d)) ChangePage(d);
    }

    static void ScaleTile(Border tile, double to)
    {
        var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(160));
        tile.RenderTransform.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        tile.RenderTransform.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }

    void UpdateTileSelection()
    {
        foreach (Border t in TilesGrid.Children)
        {
            bool sel = t.Tag is EffectKind k && k == engine.Effect;
            t.BorderBrush = sel ? Brushes.White : new SolidColorBrush(Color.FromArgb(70, 255, 255, 255));
            t.BorderThickness = new Thickness(sel ? 3 : 1.5);
        }
    }

    void Tile_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border { Tag: EffectKind k })
        {
            engine.Effect = k;
            ShowEffects(false);
            var def = EffectLib.Get(k);
            ShowToast(def.Name);
            if (def.NeedsFace) _ = EnsureAiAsync();
        }
    }

    void ShowEffects(bool show)
    {
        if (show == effectsOpen) return;
        effectsOpen = show;
        engine.TilesActive = show;

        if (show)
        {
            HideViewer();
            UpdatePageUi();
            if (EffectLib.PageHasFace(tilePage)) _ = EnsureAiAsync();
            TilesHost.Visibility = Visibility.Visible;
            TilesHost.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240)));

            var pop = new DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(340))
            {
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 }
            };
            TilesScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            TilesScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        }
        else
        {
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(170));
            fade.Completed += (_, _) =>
            {
                if (!effectsOpen) TilesHost.Visibility = Visibility.Collapsed;
            };
            TilesHost.BeginAnimation(OpacityProperty, fade);
        }
    }

    void Effects_Click(object sender, RoutedEventArgs e) => ShowEffects(!effectsOpen);

    // ------------------------------------------------------------ slider mode (4 Foto / Foto / Video)

    int ModeIndexFromX(double x) => (int)Math.Clamp(Math.Floor(x / SegW), 0, 2);

    void SetModeIndex(int idx, bool animate = true)
    {
        if (recording) return;
        idx = Math.Clamp(idx, 0, 2);
        mode = (Mode)idx;

        double target = idx * SegW;
        if (animate)
        {
            var move = new DoubleAnimation(target, TimeSpan.FromMilliseconds(420))
            {
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 }
            };
            ThumbShift.BeginAnimation(TranslateTransform.XProperty, move);
        }
        else
        {
            ThumbShift.BeginAnimation(TranslateTransform.XProperty, null);
            ThumbShift.X = target;
        }
        UpdateModeLabels();
        UpdateTimerButton();
    }

    void UpdateModeLabels()
    {
        var labels = new[] { ModeLbl0, ModeLbl1, ModeLbl2 };
        for (int i = 0; i < labels.Length; i++)
        {
            bool on = (int)mode == i;
            labels[i].Foreground = on ? Brushes.White : new SolidColorBrush(Color.FromArgb(153, 255, 255, 255));
        }
    }

    void Mode_Down(object sender, MouseButtonEventArgs e)
    {
        if (recording || busy) return;
        draggingMode = true;
        dragMoved = false;
        downX = e.GetPosition(ModeSlider).X;
        ModeSlider.CaptureMouse();
        SetModeIndex(ModeIndexFromX(downX)); // klik: thumb meluncur ke segmen itu
    }

    void Mode_Move(object sender, MouseEventArgs e)
    {
        if (!draggingMode) return;
        double x = e.GetPosition(ModeSlider).X;

        if (!dragMoved)
        {
            if (Math.Abs(x - downX) < 6) return;
            dragMoved = true;
            double cur = ThumbShift.X;
            ThumbShift.BeginAnimation(TranslateTransform.XProperty, null);
            ThumbShift.X = cur;
        }
        ThumbShift.X = Math.Clamp(x - SegW / 2, 0, 2 * SegW); // thumb mengikuti kursor
    }

    void Mode_Up(object sender, MouseButtonEventArgs e)
    {
        if (!draggingMode) return;
        draggingMode = false;
        ModeSlider.ReleaseMouseCapture();
        if (dragMoved) SetModeIndex(ModeIndexFromX(e.GetPosition(ModeSlider).X)); // lepas: snap
    }

    async void Shutter_Click(object sender, RoutedEventArgs e)
    {
        if (mode == Mode.Video)
        {
            await ToggleRecordingAsync();
            return;
        }
        if (busy) return;
        await TakePhotoAsync();
    }

    async Task TakePhotoAsync(int? countdownOverride = null)
    {
        busy = true;
        ShowEffects(false);
        HideViewer();

        cts = new CancellationTokenSource();
        var token = cts.Token;
        var shots = new List<CvMat>();
        try
        {
            int total = mode == Mode.Four ? 4 : 1;

            for (int i = 0; i < total; i++)
            {
                if (total > 1)
                {
                    ShotLabel.Text = $"Foto {i + 1} dari {total}";
                    ShotLabel.Visibility = Visibility.Visible;
                }

                // Setiap foto punya hitung mundur sendiri
                // timer pilihan hanya untuk mode Foto (0 = langsung jepret); 4 Foto selalu 3 detik per foto
                int count = countdownOverride ?? (mode == Mode.Single ? timerSeconds : 3);
                for (int sec = count; sec >= 1; sec--)
                {
                    PopCountdown(sec.ToString());
                    if (settings.SoundTimer) SoundFx.PlayTick(sec == 1);
                    await Task.Delay(1000, token);
                }
                Countdown.Visibility = Visibility.Collapsed;

                FlashScreen();
                var snap = engine.Snapshot();
                if (snap != null) shots.Add(snap);

                if (i < total - 1) await Task.Delay(700, token); // jeda singkat setelah kilatan
            }

            if (shots.Count == 0) return;

            string path = System.IO.Path.Combine(folder, $"Photo_{DateTime.Now:yyyyMMdd_HHmmss}.jpg");
            using var final = shots.Count == 4 ? CameraEngine.Compose2x2(shots) : shots[0].Clone();
            Watermark.Apply(final, settings.WatermarkOn ? settings.WatermarkText : null, settings.TimestampOn, DateTime.Now);
            SaveJpeg(CameraEngine.ToBmp(final), path);

            var thumb = MakeThumb(path);
            if (thumb != null) { items.Insert(0, thumb); ShowIsland(true); }
        }
        catch (OperationCanceledException)
        {
            // dibatalkan dengan Esc: foto yang belum lengkap dibuang
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Photo Booth", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            foreach (var s in shots) s.Dispose();
            cts?.Dispose();
            cts = null;
            Countdown.Visibility = Visibility.Collapsed;
            ShotLabel.Visibility = Visibility.Collapsed;
            busy = false;
        }
    }

    void FlashScreen()
    {
        if (settings.SoundShutter) SoundFx.PlayShutter();
        Flash.Opacity = 1;
        Flash.BeginAnimation(OpacityProperty,
            new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(450)));
    }

    // ------------------------------------------------------------ rekam video

    async Task ToggleRecordingAsync()
    {
        if (busy) return;

        if (!recording)
        {
            ShowEffects(false);
            HideViewer();

            // direkam sebagai AVI (video + suara) lalu diubah ke MP4 saat selesai
            recPath = System.IO.Path.Combine(folder, $"Video_{DateTime.Now:yyyyMMdd_HHmmss}.avi");
            if (!engine.StartRecording(recPath, settings.RecordAudio, out bool audioOk))
            {
                MessageBox.Show("Gagal memulai rekaman.", "Photo Booth", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (settings.RecordAudio && !audioOk) ShowToast("Mikrofon tidak tersedia: video direkam tanpa suara");

            recording = true;
            Shutter.Tag = "rec";
            PauseBtn.Visibility = Visibility.Visible;
            UpdatePauseUi(false);
            RecText.Text = "● 00:00";
            RecText.Visibility = Visibility.Visible;
            StartRecPulse();

            recTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            recTimer.Tick += (_, _) =>
            {
                var t = engine.RecElapsed;   // jam berhenti saat jeda
                RecText.Text = $"{(engine.IsPaused ? "⏸" : "●")} {(int)t.TotalMinutes:00}:{t.Seconds:00}";
            };
            recTimer.Start();
        }
        else
        {
            engine.StopRecording();
            recording = false;
            recTimer?.Stop();
            Shutter.Tag = null;
            PauseBtn.Visibility = Visibility.Collapsed;
            RecText.BeginAnimation(OpacityProperty, null);
            RecText.Opacity = 1;
            RecText.Visibility = Visibility.Collapsed;

            await FinishVideoAsync(recPath);
        }
    }

    void StartRecPulse()
    {
        RecText.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.35, TimeSpan.FromMilliseconds(700))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        });
    }

    void Pause_Click(object sender, RoutedEventArgs e)
    {
        if (!recording) return;
        if (engine.IsPaused) engine.ResumeRecording();
        else engine.PauseRecording();
        UpdatePauseUi(engine.IsPaused);
    }

    void UpdatePauseUi(bool paused)
    {
        PauseBtn.Content = paused ? "\uE768" : "\uE769";   // putar / jeda (Segoe MDL2)
        PauseBtn.ToolTip = paused ? "Lanjutkan rekaman" : "Jeda rekaman";
        if (paused)
        {
            RecText.BeginAnimation(OpacityProperty, null);
            RecText.Opacity = 1;
        }
        else if (recording)
        {
            StartRecPulse();
        }
    }

    /// <summary>Mengubah AVI menjadi MP4 (H.264 + AAC) lewat Media Foundation; bila gagal, AVI tetap disimpan.</summary>
    async Task FinishVideoAsync(string aviPath)
    {
        busy = true;
        string finalPath = aviPath;
        try
        {
            ShowToast("Memproses video ke MP4…");
            string mp4 = System.IO.Path.ChangeExtension(aviPath, ".mp4");
            int lastBucket = -1;
            var progress = new Progress<double>(p =>
            {
                int pct = (int)(p * 100);
                if (pct / 10 != lastBucket) { lastBucket = pct / 10; ShowToast($"Memproses video… {pct}%"); }
            });

            bool ok = await Mp4Transcoder.ConvertAsync(aviPath, mp4, progress);
            if (ok)
            {
                try { File.Delete(aviPath); } catch { }
                finalPath = mp4;
                ShowToast("Video MP4 tersimpan");
            }
            else
            {
                ShowToast("Konversi MP4 gagal, video disimpan sebagai AVI");
            }
        }
        finally { busy = false; }

        var thumb = MakeThumb(finalPath);
        if (thumb != null) { items.Insert(0, thumb); ShowIsland(true); }
    }

    // ------------------------------------------------------------ thumbnail & galeri

    static BitmapImage LoadImage(string path, int decodeWidth)
    {
        using var fs = File.OpenRead(path);
        var bi = new BitmapImage();
        bi.BeginInit();
        bi.CacheOption = BitmapCacheOption.OnLoad;
        if (decodeWidth > 0) bi.DecodePixelWidth = decodeWidth;
        bi.StreamSource = fs;
        bi.EndInit();
        bi.Freeze();
        return bi;
    }

    static ThumbItem? MakeThumb(string path)
    {
        try
        {
            bool video = path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
                         path.EndsWith(".avi", StringComparison.OrdinalIgnoreCase);
            BitmapSource? bmp = video ? CameraEngine.VideoFirstFrame(path) : LoadImage(path, 224);
            return bmp == null ? null : new ThumbItem(path, bmp, video);
        }
        catch
        {
            return null;
        }
    }

    async Task LoadThumbsAsync()
    {
        string dir = folder;
        var list = await Task.Run(() =>
            (Directory.Exists(dir) ? Directory.GetFiles(dir) : Array.Empty<string>())
                .Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".avi", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(File.GetCreationTime)
                .Take(200)
                .Select(MakeThumb)
                .Where(t => t != null)
                .Cast<ThumbItem>()
                .ToList());

        foreach (var t in list) items.Add(t);
    }

    static void SaveJpeg(BitmapSource bmp, string path)
    {
        var enc = new JpegBitmapEncoder { QualityLevel = 95 };
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    void Thumbs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (suppress || Thumbs.SelectedItem is not ThumbItem t) return;

        if (t.IsVideo)
        {
            try { Process.Start(new ProcessStartInfo(t.Path) { UseShellExecute = true }); } catch { }
            suppress = true;
            Thumbs.SelectedItem = null;
            suppress = false;
            return;
        }

        ShowEffects(false);
        try
        {
            Viewer.Source = LoadImage(t.Path, 0);
            viewerToken++;
            FadeIn(Viewer);
            FadeIn(ViewerClose);
        }
        catch { }
    }

    void Thumb_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem lbi)
        {
            suppress = true;
            lbi.IsSelected = true;
            suppress = false;
        }
    }

    void HideViewer()
    {
        suppress = true;
        Thumbs.SelectedItem = null;
        suppress = false;

        if (Viewer.Visibility != Visibility.Visible)
        {
            ViewerClose.Visibility = Visibility.Collapsed;
            return;
        }

        int token = ++viewerToken;
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(240));
        fade.Completed += (_, _) =>
        {
            if (token != viewerToken) return;   // sudah ada pratinjau baru
            Viewer.Visibility = Visibility.Collapsed;
            Viewer.Source = null;
            ViewerClose.Visibility = Visibility.Collapsed;
        };
        Viewer.BeginAnimation(OpacityProperty, fade);
        ViewerClose.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(180)));
    }

    void ViewerClose_Click(object sender, RoutedEventArgs e) => HideViewer();

    // ------------------------------------------------------------ menu konteks

    void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (Thumbs.SelectedItem is not ThumbItem t) return;
        try
        {
            if (t.IsVideo) Clipboard.SetFileDropList(new StringCollection { t.Path });
            else Clipboard.SetImage(LoadImage(t.Path, 0));
        }
        catch (Exception ex) { MessageBox.Show(ex.Message); }
    }

    void ShowInFolder_Click(object sender, RoutedEventArgs e)
    {
        if (Thumbs.SelectedItem is not ThumbItem t) return;
        Process.Start("explorer.exe", $"/select,\"{t.Path}\"");
    }

    void Delete_Click(object sender, RoutedEventArgs e) => DeleteSelected();

    void DeleteSelected()
    {
        if (Thumbs.SelectedItem is not ThumbItem t) return;

        var ask = MessageBox.Show($"Hapus {System.IO.Path.GetFileName(t.Path)}?", "Photo Booth",
                                  MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (ask != MessageBoxResult.Yes) return;

        try { File.Delete(t.Path); }
        catch (Exception ex) { MessageBox.Show(ex.Message); return; }

        HideViewer();
        items.Remove(t);
    }

    void Folder_Click(object sender, RoutedEventArgs e)
        => Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });

    // ------------------------------------------------------------ keyboard

    void Window_KeyDown(object sender, KeyEventArgs e)
    {
        // saat mengetik (mis. teks watermark), tombol tidak boleh memicu jepret/hapus
        if (Keyboard.FocusedElement is TextBox && e.Key != Key.Escape) return;

        switch (e.Key)
        {
            case Key.Space:
            case Key.Enter:
                Shutter_Click(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.Escape:
                if (cts != null) cts.Cancel();
                else if (effectsOpen) ShowEffects(false);
                else if (Viewer.Visibility == Visibility.Visible) HideViewer();
                else if (settingsOpen) ToggleSettings();
                else if (adjOpen) ToggleAdjust();
                else if (islandOpen) HideIsland();
                e.Handled = true;
                break;
            case Key.Left when effectsOpen:
                ChangePage(-1);
                e.Handled = true;
                break;
            case Key.Right when effectsOpen:
                ChangePage(1);
                e.Handled = true;
                break;
            case Key.Delete:
                DeleteSelected();
                e.Handled = true;
                break;
        }
    }

    // ------------------------------------------------------------ jendela tanpa bingkai (tombol ala Mac)

    static Brush Hex(string color) => (Brush)new BrushConverter().ConvertFromString(color)!;

    void Close_Click(object sender, RoutedEventArgs e) => Close();

    void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    void Maximize_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    // Simbol ×, –, dan panah muncul saat kursor berada di atas kelompok tombol (seperti macOS)
    void Lights_MouseEnter(object sender, MouseEventArgs e) => SetLightGlyphs("on");
    void Lights_MouseLeave(object sender, MouseEventArgs e) => SetLightGlyphs("off");

    void SetLightGlyphs(string value)
    {
        BtnClose.Tag = value;
        BtnMin.Tag = value;
        BtnMax.Tag = value;
    }

    // Tombol berubah abu-abu saat jendela tidak aktif, seperti macOS
    void Window_Activated(object? sender, EventArgs e)
    {
        BtnClose.Background = Hex("#FF5F57");
        BtnMin.Background = Hex("#FEBC2E");
        BtnMax.Background = Hex("#28C840");
    }

    void Window_Deactivated(object? sender, EventArgs e)
    {
        var gray = Hex("#66808080");
        BtnClose.Background = gray;
        BtnMin.Background = gray;
        BtnMax.Background = gray;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (PresentationSource.FromVisual(this) is HwndSource src)
            src.AddHook(WndProc);
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_GETMINMAXINFO)
        {
            FitMaximizedToWorkArea(hwnd, lParam);
            handled = true;
        }
        return IntPtr.Zero;
    }

    void FitMaximizedToWorkArea(IntPtr hwnd, IntPtr lParam)
    {
        var mmi = Marshal.PtrToStructure<NativeMethods.MINMAXINFO>(lParam);

        IntPtr monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (monitor != IntPtr.Zero)
        {
            var info = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
            if (NativeMethods.GetMonitorInfo(monitor, ref info))
            {
                var work = info.rcWork;
                var full = info.rcMonitor;
                mmi.ptMaxPosition.X = Math.Abs(work.Left - full.Left);
                mmi.ptMaxPosition.Y = Math.Abs(work.Top - full.Top);
                mmi.ptMaxSize.X = Math.Abs(work.Right - work.Left);
                mmi.ptMaxSize.Y = Math.Abs(work.Bottom - work.Top);
                mmi.ptMaxTrackSize.X = mmi.ptMaxSize.X;
                mmi.ptMaxTrackSize.Y = mmi.ptMaxSize.Y;
            }
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        mmi.ptMinTrackSize.X = (int)(MinWidth * dpi.DpiScaleX);
        mmi.ptMinTrackSize.Y = (int)(MinHeight * dpi.DpiScaleY);

        Marshal.StructureToPtr(mmi, lParam, true);
    }

    // ------------------------------------------------------------ animasi & helper UI

    static void FadeIn(UIElement el, double ms = 240)
    {
        el.Visibility = Visibility.Visible;
        el.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(ms)));
    }

    // ------------------------------------------------------------ pulau galeri (gaya Dynamic Island)

    void UpdateGallery()
    {
        IslandTitle.Text = items.Count == 0 ? "Belum ada foto" : $"Galeri · {items.Count} item";
        if (items.Count > 0)
        {
            GalleryThumb.Fill = new ImageBrush(items[0].Thumb) { Stretch = Stretch.UniformToFill };
            GalleryThumb.Visibility = Visibility.Visible;
            GalleryGlyph.Visibility = Visibility.Collapsed;
        }
        else
        {
            GalleryThumb.Visibility = Visibility.Collapsed;
            GalleryGlyph.Visibility = Visibility.Visible;
        }
    }

    void UpdateIslandChrome()
    {
        IslandPin.Visibility = islandPinned ? Visibility.Visible : Visibility.Collapsed;
        IslandClose.Visibility = islandPinned ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Memunculkan pulau: melebar dari arah tombol Galeri dengan efek pegas + fade in.</summary>
    void ShowIsland(bool autoHide = false)
    {
        UpdateGallery();
        islandToken++;
        islandTimer?.Stop();

        if (!islandOpen)
        {
            islandOpen = true;
            ThumbsCard.Opacity = 0;
            ThumbsCard.Visibility = Visibility.Visible;
            ThumbsCard.UpdateLayout();

            try   // titik asal animasi = posisi tombol Galeri
            {
                var pt = GalleryBtn.TranslatePoint(new Point(GalleryBtn.ActualWidth / 2, 0), ThumbsCard);
                double fx = ThumbsCard.ActualWidth > 0 ? Math.Clamp(pt.X / ThumbsCard.ActualWidth, 0, 1) : 0.5;
                ThumbsCard.RenderTransformOrigin = new Point(fx, 1);
            }
            catch { ThumbsCard.RenderTransformOrigin = new Point(0.5, 1); }

            var spring = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.45 };
            ThumbsCard.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
            IslandScale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(0.35, 1, TimeSpan.FromMilliseconds(440)) { EasingFunction = spring });
            IslandScale.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(0.55, 1, TimeSpan.FromMilliseconds(440)) { EasingFunction = spring });
            IslandShift.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(380))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
        else
        {
            // sedang memudar? kembalikan ke penuh
            ThumbsCard.BeginAnimation(OpacityProperty, new DoubleAnimation(ThumbsCard.Opacity, 1, TimeSpan.FromMilliseconds(140)));
            IslandScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(160)));
            IslandScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(160)));
            IslandShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(160)));
        }

        UpdateIslandChrome();
        if (autoHide) StartIslandTimer();
    }

    void HideIsland()
    {
        if (!islandOpen) return;
        islandOpen = false;
        islandPinned = false;
        islandTimer?.Stop();
        UpdateIslandChrome();

        int token = ++islandToken;
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(240));
        fade.Completed += (_, _) =>
        {
            if (token == islandToken) ThumbsCard.Visibility = Visibility.Collapsed;
        };
        ThumbsCard.BeginAnimation(OpacityProperty, fade);

        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        IslandScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.6, TimeSpan.FromMilliseconds(240)) { EasingFunction = ease });
        IslandScale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.75, TimeSpan.FromMilliseconds(240)) { EasingFunction = ease });
        IslandShift.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(10, TimeSpan.FromMilliseconds(240)) { EasingFunction = ease });
    }

    /// <summary>Hitung 5 detik sejak kursor tidak berada di tombol Galeri maupun pulau; disematkan = tidak pernah hilang sendiri.</summary>
    void StartIslandTimer()
    {
        if (islandPinned || hoverCount > 0) return;
        if (islandTimer == null)
        {
            islandTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            islandTimer.Tick += (_, _) =>
            {
                islandTimer!.Stop();
                if (!islandPinned && hoverCount == 0) HideIsland();
            };
        }
        islandTimer.Stop();
        islandTimer.Start();
    }

    void Gallery_MouseEnter(object sender, MouseEventArgs e)
    {
        hoverCount++;
        islandTimer?.Stop();
        ShowIsland();
    }

    void Gallery_MouseLeave(object sender, MouseEventArgs e)
    {
        hoverCount = Math.Max(0, hoverCount - 1);
        StartIslandTimer();
    }

    void Island_MouseEnter(object sender, MouseEventArgs e)
    {
        hoverCount++;
        islandTimer?.Stop();
    }

    void Island_MouseLeave(object sender, MouseEventArgs e)
    {
        hoverCount = Math.Max(0, hoverCount - 1);
        StartIslandTimer();
    }

    // Klik tombol Galeri: sematkan pulau (tetap tampil); klik lagi: sembunyikan.
    void Gallery_Click(object sender, RoutedEventArgs e)
    {
        if (islandPinned) { HideIsland(); return; }
        islandPinned = true;
        islandTimer?.Stop();
        ShowIsland();
        UpdateIslandChrome();
    }

    // Klik di pulau juga menyematkannya
    void Island_Click(object sender, MouseButtonEventArgs e)
    {
        if (islandPinned) return;
        islandPinned = true;
        islandTimer?.Stop();
        UpdateIslandChrome();
    }

    void IslandClose_Click(object sender, RoutedEventArgs e) => HideIsland();

    // ------------------------------------------------------------ timer (hanya mode Foto)

    void UpdateTimerButton()
    {
        bool on = mode == Mode.Single;
        TimerBadge.Text = timerSeconds == 0 ? "Off" : timerSeconds + "s";
        TimerBtn.Opacity = on ? 1.0 : 0.4;
        TimerBtn.ToolTip = on ? (timerSeconds == 0 ? "Tanpa timer (langsung jepret)" : $"Timer: {timerSeconds} detik") : "Timer hanya berlaku di mode Foto";
    }

    void Timer_Click(object sender, RoutedEventArgs e)
    {
        if (busy || recording) return;
        if (mode != Mode.Single)
        {
            ShowToast("Timer hanya berlaku di mode Foto");
            return;
        }

        var menu = new ContextMenu();
        foreach (int sec in new[] { 0, 3, 5, 10 })
        {
            var mi = new MenuItem { Header = sec == 0 ? "Tanpa timer (langsung jepret)" : $"{sec} detik", IsChecked = sec == timerSeconds, Tag = sec };
            mi.Click += TimerItem_Click;
            menu.Items.Add(mi);
        }
        menu.PlacementTarget = (UIElement)sender;
        menu.Placement = PlacementMode.Top;
        menu.IsOpen = true;
    }

    void TimerItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: int sec }) return;
        timerSeconds = sec;
        settings.TimerSeconds = sec;
        settings.Save();
        UpdateTimerButton();
        ShowToast(sec == 0 ? "Tanpa timer" : $"Timer {sec} detik");
    }

    void ShowToast(string text)
    {
        ToastText.Text = text;
        var a = new DoubleAnimationUsingKeyFrames();
        a.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        a.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(180))));
        a.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1700))));
        a.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2100))));
        Toast.BeginAnimation(OpacityProperty, a);
    }

    // Angka hitung mundur muncul membesar lalu memudar
    void PopCountdown(string text)
    {
        Countdown.Text = text;
        Countdown.Visibility = Visibility.Visible;

        var scale = new DoubleAnimation(1.6, 1, TimeSpan.FromMilliseconds(450))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        CdScale.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
        CdScale.BeginAnimation(ScaleTransform.ScaleYProperty, scale);

        var fade = new DoubleAnimationUsingKeyFrames();
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(150))));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(650))));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(950))));
        Countdown.BeginAnimation(OpacityProperty, fade);
    }

    // Scroll thumbnail dengan roda mouse (horizontal)
    void Thumbs_Wheel(object sender, MouseWheelEventArgs e)
    {
        var sv = FindChild<ScrollViewer>(Thumbs);
        if (sv == null) return;
        sv.ScrollToHorizontalOffset(sv.HorizontalOffset - e.Delta);
        e.Handled = true;
    }

    static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t) return t;
            var found = FindChild<T>(child);
            if (found != null) return found;
        }
        return null;
    }

    // ------------------------------------------------------------ AI (NPU / GPU / CPU)

    void SetAiChip(string text) => AiChipText.Text = text;

    void OnFaceState(int state)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            string? msg = state switch
            {
                2 => "Arahkan wajahmu ke kamera",
                3 => settings.AiConsent ? "AI sedang disiapkan…" : "Ketuk chip AI di kanan atas untuk mengaktifkan efek AR",
                _ => null,
            };

            if (msg == null)
            {
                if (FaceHint.Visibility == Visibility.Visible)
                {
                    var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180));
                    fade.Completed += (_, _) => { if (FaceHint.Opacity <= 0.01) FaceHint.Visibility = Visibility.Collapsed; };
                    FaceHint.BeginAnimation(OpacityProperty, fade);
                }
                return;
            }
            FaceHintText.Text = msg;
            if (FaceHint.Visibility != Visibility.Visible || FaceHint.Opacity < 0.99) FadeIn(FaceHint, 200);
        }));
    }

    /// <summary>Menyiapkan AI saat pertama dibutuhkan: izin pengguna, akselerator Windows ML, model, lalu sesi inferensi.</summary>
    async Task EnsureAiAsync(bool forScene = false)
    {
        bool needScene = settings.AutoScene || forScene;
        if (aiBusy) return;
        if (engine.Tracker.IsReady && (!needScene || engine.Scene.IsReady)) return;

        if (!settings.AiConsent)
        {
            var ask = MessageBox.Show(this,
                "Efek AR memakai kecerdasan buatan yang berjalan di perangkat ini (NPU / GPU / CPU).\n\n" +
                "Pertama kali, aplikasi akan mengunduh:\n" +
                "  • model pelacak wajah (±5 MB)\n" +
                "  • komponen akselerator AI dari Microsoft (Windows ML), bila perangkatmu mendukung\n\n" +
                "Pemrosesan berjalan lokal; gambar kamera tidak dikirim ke mana pun.\n\nLanjutkan?",
                "Photo Booth", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (ask != MessageBoxResult.Yes) { SetAiChip("AI · dinonaktifkan"); return; }
            settings.AiConsent = true;
            settings.Save();
        }

        aiBusy = true;
        try
        {
            SetAiChip("AI · menyiapkan…");
            await AiRuntime.RegisterProvidersAsync();

            var err = await ModelStore.EnsureAsync(new Progress<string>(m => SetAiChip("AI · " + m)), needScene);
            if (err != null)
            {
                SetAiChip("AI · gagal");
                MessageBox.Show(this, err + "\n\nKamu juga bisa menaruh berkas model secara manual di:\n" +
                                System.IO.Path.Combine(AppSettings.Dir, "models"),
                                "Photo Booth", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            await InitTrackerAsync();
        }
        finally { aiBusy = false; }
    }

    async Task InitTrackerAsync()
    {
        var lm = ModelStore.Find(ModelStore.LandmarkFile);
        var yn = ModelStore.Find(ModelStore.YuNetFile);
        if (lm == null || yn == null) { SetAiChip("AI · model tidak ada"); return; }

        SetAiChip("AI · memuat model… (NPU pertama kali bisa lama)");
        bool ok = await Task.Run(() => engine.Tracker.Initialize(lm, yn, settings.Ai));
        if (ok)
        {
            SetAiChip($"AI · {engine.Tracker.BackendLabel} · {engine.Tracker.LastInferenceMs:0} ms");
            ShowToast("AI aktif: " + engine.Tracker.BackendLabel);

            if (settings.AutoScene)   // klasifikasi scene memakai perangkat yang sama (NPU / GPU / CPU)
            {
                var sp = ModelStore.Find(ModelStore.SceneFile);
                if (sp != null)
                {
                    bool sok = await Task.Run(() => engine.Scene.Initialize(sp, settings.Ai));
                    if (!sok) ShowToast("Scene AI gagal: " + engine.Scene.LastError);
                }
            }
        }
        else
        {
            SetAiChip("AI · gagal");
            MessageBox.Show(this, "Model AI tidak bisa dijalankan.\n\n" + engine.Tracker.LastError,
                            "Photo Booth", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    void AiChip_Click(object sender, MouseButtonEventArgs e)
    {
        if (aiBusy) return;

        var menu = new ContextMenu();
        void Add(string header, AiPreference pref)
        {
            var mi = new MenuItem { Header = header, IsChecked = settings.Ai == pref, Tag = pref };
            mi.Click += AiPref_Click;
            menu.Items.Add(mi);
        }
        Add("Otomatis (NPU → GPU → CPU)", AiPreference.Auto);
        Add("Utamakan NPU", AiPreference.Npu);
        Add("Utamakan GPU", AiPreference.Gpu);
        Add("Hanya CPU", AiPreference.Cpu);

        if (!engine.Tracker.IsReady)
        {
            menu.Items.Add(new Separator());
            var prep = new MenuItem { Header = "Siapkan AI sekarang" };
            prep.Click += async (_, _) => await EnsureAiAsync();
            menu.Items.Add(prep);
        }

        menu.PlacementTarget = (UIElement)sender;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    async void AiPref_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: AiPreference pref }) return;
        settings.Ai = pref;
        settings.Save();

        if (!settings.AiConsent || !ModelStore.AllPresent) { await EnsureAiAsync(); return; }
        if (aiBusy) return;
        aiBusy = true;
        try { await InitTrackerAsync(); }
        finally { aiBusy = false; }
    }

    // ------------------------------------------------------------ penyesuaian cahaya

    void ApplyAdjustUi(bool save)
    {
        engine.AdjBrightness = (int)Math.Round(AdjBright.Value);
        engine.AdjContrast = (float)AdjContrast.Value;
        engine.AdjExposure = (float)AdjExposure.Value;
        engine.AutoLevel = settings.AutoLevel;

        AdjBrightVal.Text = engine.AdjBrightness.ToString("+0;-0;0");
        AdjContrastVal.Text = AdjContrast.Value.ToString("0.00");
        AdjExposureVal.Text = AdjExposure.Value.ToString("+0.0;-0.0;0.0");
        AutoLevelBtn.Content = "Otomatis: " + (settings.AutoLevel ? "Hidup" : "Mati");

        if (!save) return;
        settings.AdjBrightness = engine.AdjBrightness;
        settings.AdjContrast = AdjContrast.Value;
        settings.AdjExposure = AdjExposure.Value;
        if (adjSaveTimer == null)
        {
            adjSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            adjSaveTimer.Tick += (_, _) => { adjSaveTimer!.Stop(); settings.Save(); };
        }
        adjSaveTimer.Stop();
        adjSaveTimer.Start();   // simpan setelah slider berhenti digeser
    }

    void Adj_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (adjReady) ApplyAdjustUi(true);
    }

    void AutoLevel_Click(object sender, RoutedEventArgs e)
    {
        settings.AutoLevel = !settings.AutoLevel;
        ApplyAdjustUi(true);
    }

    void AdjReset_Click(object sender, RoutedEventArgs e)
    {
        adjReady = false;
        AdjBright.Value = 0;
        AdjContrast.Value = 1;
        AdjExposure.Value = 0;
        settings.AutoLevel = true;
        adjReady = true;
        ApplyAdjustUi(true);
    }

    void Adjust_Click(object sender, RoutedEventArgs e) => ToggleAdjust();

    void ToggleAdjust()
    {
        if (!adjOpen)
        {
            adjOpen = true;
            FadeIn(AdjustCard, 240);
            return;
        }

        adjOpen = false;
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(200));
        fade.Completed += (_, _) =>
        {
            if (!adjOpen) AdjustCard.Visibility = Visibility.Collapsed;
        };
        AdjustCard.BeginAnimation(OpacityProperty, fade);
    }

    // ------------------------------------------------------------ pengaturan

    bool settingsReady, settingsOpen;
    CheeseListener? cheese;

    void LoadSettingsUi()
    {
        settingsReady = false;
        SwWatermark.IsChecked = settings.WatermarkOn;
        SwTimestamp.IsChecked = settings.TimestampOn;
        SwScene.IsChecked = settings.AutoScene;
        SwPalm.IsChecked = settings.PalmShoot;
        SwCheese.IsChecked = settings.CheeseShoot;
        SwShutter.IsChecked = settings.SoundShutter;
        SwTimerSound.IsChecked = settings.SoundTimer;
        SwAudio.IsChecked = settings.RecordAudio;
        WatermarkBox.Text = settings.WatermarkText;
        WatermarkBox.IsEnabled = settings.WatermarkOn;
        GridBtn.Content = GridLabel();
        FolderText.Text = "Folder: " + folder;
        engine.AutoScene = settings.AutoScene;
        engine.PalmEnabled = settings.PalmShoot;
        settingsReady = true;
        UpdateGrid();
    }

    string GridLabel() => settings.GridMode switch { 1 => "Grid 3×3", 2 => "Grid 4×4", _ => "Grid: Mati" };

    void Settings_Click(object sender, RoutedEventArgs e) => ToggleSettings();
    void SettingsClose_Click(object sender, RoutedEventArgs e) => ToggleSettings();

    void ToggleSettings()
    {
        if (!settingsOpen)
        {
            settingsOpen = true;
            FadeIn(SettingsCard, 240);
            return;
        }

        settingsOpen = false;
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(200));
        fade.Completed += (_, _) =>
        {
            if (!settingsOpen) SettingsCard.Visibility = Visibility.Collapsed;
        };
        SettingsCard.BeginAnimation(OpacityProperty, fade);
    }

    void Setting_Toggled(object sender, RoutedEventArgs e)
    {
        if (!settingsReady || sender is not ToggleButton { Tag: string tag } tb) return;
        bool on = tb.IsChecked == true;

        switch (tag)
        {
            case "watermark":
                settings.WatermarkOn = on;
                WatermarkBox.IsEnabled = on;
                break;
            case "timestamp":
                settings.TimestampOn = on;
                break;
            case "scene":
                settings.AutoScene = on;
                engine.AutoScene = on;
                if (on) _ = EnsureAiAsync(true);
                break;
            case "palm":
                settings.PalmShoot = on;
                engine.PalmEnabled = on;
                if (on)
                {
                    ShowToast("Tunjukkan telapak terbuka ke kamera ±1 detik");
                    _ = EnsureAiAsync();
                }
                break;
            case "cheese":
                settings.CheeseShoot = on;
                _ = UpdateCheeseAsync();
                break;
            case "shutter":
                settings.SoundShutter = on;
                if (on) SoundFx.PlayShutter();
                break;
            case "timersound":
                settings.SoundTimer = on;
                if (on) SoundFx.PlayTick();
                break;
            case "audio":
                settings.RecordAudio = on;
                break;
        }
        settings.Save();
    }

    void WatermarkBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!settingsReady) return;
        settings.WatermarkText = WatermarkBox.Text;
        settings.Save();
    }

    void Grid_Click(object sender, RoutedEventArgs e)
    {
        settings.GridMode = (settings.GridMode + 1) % 3;
        settings.Save();
        GridBtn.Content = GridLabel();
        UpdateGrid();
    }

    void GridCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateGrid();

    /// <summary>Garis bantu 3x3 / 4x4 tepat di atas area gambar kamera (hanya tampilan, tidak ikut tersimpan).</summary>
    void UpdateGrid()
    {
        if (GridCanvas == null) return;
        GridCanvas.Children.Clear();
        int n = settings.GridMode == 1 ? 3 : settings.GridMode == 2 ? 4 : 0;
        if (n == 0 || GridCanvas.ActualWidth < 10 || engine.Width <= 0 || engine.Height <= 0) return;

        double cw = GridCanvas.ActualWidth, ch = GridCanvas.ActualHeight;
        double sc = Math.Min(cw / engine.Width, ch / engine.Height);
        double w = engine.Width * sc, h = engine.Height * sc;
        double x0 = (cw - w) / 2, y0 = (ch - h) / 2;
        var brush = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255));

        for (int i = 1; i < n; i++)
        {
            double x = x0 + w * i / n, y = y0 + h * i / n;
            GridCanvas.Children.Add(new System.Windows.Shapes.Line { X1 = x, X2 = x, Y1 = y0, Y2 = y0 + h, Stroke = brush, StrokeThickness = 1 });
            GridCanvas.Children.Add(new System.Windows.Shapes.Line { X1 = x0, X2 = x0 + w, Y1 = y, Y2 = y, Stroke = brush, StrokeThickness = 1 });
        }
    }

    // ---- folder hasil ----

    async void PickFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Pilih folder untuk foto & video",
            InitialDirectory = Directory.Exists(folder) ? folder : DefaultFolder,
        };
        if (dlg.ShowDialog(this) != true) return;
        settings.SaveFolder = dlg.FolderName;
        await ApplyFolderChangeAsync();
    }

    async void ResetFolder_Click(object sender, RoutedEventArgs e)
    {
        settings.SaveFolder = null;
        await ApplyFolderChangeAsync();
    }

    async Task ApplyFolderChangeAsync()
    {
        settings.Save();
        try { Directory.CreateDirectory(folder); }
        catch (Exception ex) { MessageBox.Show(this, "Folder tidak bisa dipakai:\n" + ex.Message); }
        FolderText.Text = "Folder: " + folder;
        HideViewer();
        items.Clear();
        await LoadThumbsAsync();
        ShowToast("Folder hasil diganti");
    }

    // ---- cheese shoot (suara) & palm shoot (gestur) ----

    async Task UpdateCheeseAsync()
    {
        if (settings.CheeseShoot)
        {
            cheese ??= new CheeseListener();
            cheese.Heard -= OnCheese;
            cheese.Heard += OnCheese;
            if (await cheese.StartAsync())
            {
                ShowToast("Ucapkan \"cheese\" untuk memotret");
                return;
            }

            settings.CheeseShoot = false;
            settings.Save();
            settingsReady = false;
            SwCheese.IsChecked = false;
            settingsReady = true;
            MessageBox.Show(this,
                "Cheese shoot tidak bisa diaktifkan.\n\n" + cheese.Error +
                "\n\nPastikan mikrofon aktif, \"Online speech recognition\" menyala di Settings › Privacy › Speech, " +
                "dan paket bahasa English (US) terpasang.",
                "Photo Booth", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        else if (cheese != null)
        {
            await cheese.StopAsync();
        }
    }

    void OnCheese() => Dispatcher.BeginInvoke(new Action(() => TriggerRemoteShot("Cheese!", 0)));

    void OnPalmShot() => Dispatcher.BeginInvoke(new Action(() => TriggerRemoteShot("Telapak terdeteksi", 3)));

    async void TriggerRemoteShot(string why, int countdown)
    {
        if (busy || recording || mode == Mode.Video) return;
        ShowToast(why);
        await TakePhotoAsync(countdown);
    }

    // ---- label scene ----

    void OnSceneChanged(SceneKind kind)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (kind == SceneKind.Normal)
            {
                if (SceneChip.Visibility != Visibility.Visible) return;
                var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
                fade.Completed += (_, _) => { if (SceneChip.Opacity <= 0.01) SceneChip.Visibility = Visibility.Collapsed; };
                SceneChip.BeginAnimation(OpacityProperty, fade);
                return;
            }
            SceneChipText.Text = "AI · " + SceneInfo.Label(kind);
            FadeIn(SceneChip, 240);
        }));
    }
}
