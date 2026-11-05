using System;
using System.IO;
using System.Text.Json;

namespace PhotoBooth;

public enum AiPreference { Auto, Npu, Gpu, Cpu }

public sealed class AppSettings
{
    public AiPreference Ai { get; set; } = AiPreference.Auto;
    public bool AiConsent { get; set; }
    public int TimerSeconds { get; set; } = 3;          // 0 = tanpa timer (langsung jepret)
    public bool WatermarkOn { get; set; }
    public string WatermarkText { get; set; } = "Shot on Photo Booth";
    public bool TimestampOn { get; set; }
    public int GridMode { get; set; }                   // 0 mati, 1 = 3x3, 2 = 4x4
    public bool AutoScene { get; set; }
    public bool PalmShoot { get; set; }
    public bool CheeseShoot { get; set; }
    public bool SoundShutter { get; set; } = true;
    public bool SoundTimer { get; set; } = true;
    public bool RecordAudio { get; set; } = true;
    public string? SaveFolder { get; set; }             // null = Pictures\Photo Booth
    public string? CamProfile { get; set; }      // mode kamera terakhir yang berhasil, contoh "DSHOW|MJPG|1280|720"
    public int CamProfileIndex { get; set; } = -1;
    public int CamProfileHeight { get; set; }
    public bool CamGuard { get; set; }           // true saat kamera sedang dibuka; jika masih true saat app dibuka lagi, berarti sebelumnya crash
    public int AdjBrightness { get; set; }        // -80..80
    public double AdjContrast { get; set; } = 1.0; // 0.6..1.6
    public double AdjExposure { get; set; }       // -1..1 (positif = lebih terang)
    public bool AutoLevel { get; set; } = true;   // redam gambar yang terlalu terang secara otomatis
    public int CameraHeight { get; set; }   // 0 = otomatis (maks. 1080p), atau 1080 / 720 / 480

    public static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhotoBooth");

    static string FilePath => Path.Combine(Dir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
        }
        catch { }
    }
}
