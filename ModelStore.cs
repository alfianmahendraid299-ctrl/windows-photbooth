using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace PhotoBooth;

/// <summary>
/// Mencari model AI di dua tempat: folder Assets\Models di samping aplikasi (untuk distribusi offline),
/// lalu %LOCALAPPDATA%\PhotoBooth\models (hasil unduhan otomatis).
/// </summary>
public static class ModelStore
{
    // MediaPipe Face Landmarker (478 titik), konversi ONNX oleh pihak ketiga, lisensi Apache-2.0.
    public const string LandmarkFile = "face_landmarks.onnx";
    public const string LandmarkUrl =
        "https://huggingface.co/naklitechie/face-landmarks-onnx/resolve/main/face_landmarks.onnx";
    public const string LandmarkSha256 = "f38c3321ceffbc9e95103480ad38cc3f52e7e1bde2bcee7cd9355d0b9138ac0c";

    // YuNet (OpenCV Zoo, lisensi MIT) untuk mencari wajah pertama kali.
    public const string YuNetFile = "face_detection_yunet_2023mar.onnx";
    public const string YuNetUrl =
        "https://github.com/opencv/opencv_zoo/raw/main/models/face_detection_yunet/face_detection_yunet_2023mar.onnx";

    // MobileNetV2 (ONNX Model Zoo, Apache-2.0) untuk klasifikasi scene; unggahan ulang di Hugging Face (webml/models).
    public const string SceneFile = "mobilenetv2-12.onnx";
    public const string SceneUrl = "https://huggingface.co/webml/models/resolve/main/mobilenetv2-12.onnx";
    public const string SceneSha256 = "c0c3f76d93fa3fd6580652a45618618a220fced18babf65774ed169de0432ad5";

    static string DownloadDir => Path.Combine(AppSettings.Dir, "models");
    static string BundledDir => Path.Combine(AppContext.BaseDirectory, "Assets", "Models");

    public static string? Find(string file)
    {
        var bundled = Path.Combine(BundledDir, file);
        if (File.Exists(bundled)) return bundled;
        var downloaded = Path.Combine(DownloadDir, file);
        return File.Exists(downloaded) ? downloaded : null;
    }

    public static bool AllPresent => Find(LandmarkFile) != null && Find(YuNetFile) != null;
    public static bool ScenePresent => Find(SceneFile) != null;

    /// <summary>Mengunduh model yang belum ada. Mengembalikan pesan error, atau null bila sukses.</summary>
    public static async Task<string?> EnsureAsync(IProgress<string>? progress, bool needScene = false)
    {
        try
        {
            Directory.CreateDirectory(DownloadDir);
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("PhotoBooth/1.0");

            if (Find(YuNetFile) == null)
            {
                progress?.Report("Mengunduh model deteksi wajah…");
                var path = Path.Combine(DownloadDir, YuNetFile);
                await DownloadAsync(http, YuNetUrl, path);
                if (new FileInfo(path).Length < 100_000)
                {
                    File.Delete(path);
                    return "Berkas model deteksi wajah tidak valid.";
                }
            }

            if (Find(LandmarkFile) == null)
            {
                progress?.Report("Mengunduh model titik wajah (±5 MB)…");
                var path = Path.Combine(DownloadDir, LandmarkFile);
                await DownloadAsync(http, LandmarkUrl, path);
                if (!HashMatches(path, LandmarkSha256))
                {
                    File.Delete(path);
                    return "Hash model titik wajah tidak cocok, berkas dibuang demi keamanan.";
                }
            }
            if (needScene && Find(SceneFile) == null)
            {
                progress?.Report("Mengunduh model scene (±14 MB)…");
                var path = Path.Combine(DownloadDir, SceneFile);
                await DownloadAsync(http, SceneUrl, path);
                if (!HashMatches(path, SceneSha256))
                {
                    File.Delete(path);
                    return "Hash model scene tidak cocok, berkas dibuang demi keamanan.";
                }
            }
            return null;
        }
        catch (Exception ex)
        {
            return "Gagal mengunduh model: " + ex.Message;
        }
    }

    static async Task DownloadAsync(HttpClient http, string url, string target)
    {
        var tmp = target + ".part";
        using (var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
        {
            resp.EnsureSuccessStatusCode();
            await using var src = await resp.Content.ReadAsStreamAsync();
            await using var dst = File.Create(tmp);
            await src.CopyToAsync(dst);
        }
        if (File.Exists(target)) File.Delete(target);
        File.Move(tmp, target);
    }

    static bool HashMatches(string path, string expectedHex)
    {
        using var fs = File.OpenRead(path);
        var hash = Convert.ToHexString(SHA256.HashData(fs));
        return string.Equals(hash, expectedHex, StringComparison.OrdinalIgnoreCase);
    }
}
