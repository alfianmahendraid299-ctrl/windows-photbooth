using System;
using System.IO;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;

namespace PhotoBooth;

/// <summary>
/// Mengubah rekaman AVI (MJPEG + PCM) menjadi MP4 (H.264 + AAC) memakai Windows.Media.Transcoding,
/// yaitu Media Foundation bawaan Windows (dengan akselerasi perangkat keras bila ada).
/// Bila gagal (mis. Windows edisi N tanpa Media Feature Pack), berkas AVI tetap dipakai.
/// </summary>
public static class Mp4Transcoder
{
    public static string LastError { get; private set; } = "";

    public static async Task<bool> ConvertAsync(string aviPath, string mp4Path, IProgress<double>? progress)
    {
        StorageFile? dst = null;
        try
        {
            var src = await StorageFile.GetFileFromPathAsync(aviPath);
            var dir = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(mp4Path)!);
            dst = await dir.CreateFileAsync(Path.GetFileName(mp4Path), CreationCollisionOption.ReplaceExisting);

            var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.Auto);
            var transcoder = new MediaTranscoder { HardwareAccelerationEnabled = true };

            var prepared = await transcoder.PrepareFileTranscodeAsync(src, dst, profile);
            if (!prepared.CanTranscode)
            {
                LastError = "Tidak bisa dikonversi: " + prepared.FailureReason;
                await TryDelete(dst);
                return false;
            }

            var op = prepared.TranscodeAsync();
            op.Progress = new AsyncActionProgressHandler<double>((_, percent) => progress?.Report(percent / 100.0));
            await op;
            return new FileInfo(mp4Path).Length > 1024;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            await TryDelete(dst);
            return false;
        }
    }

    static async Task TryDelete(StorageFile? f)
    {
        try { if (f != null) await f.DeleteAsync(); } catch { }
    }
}
