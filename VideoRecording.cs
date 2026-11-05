using System;
using System.Collections.Generic;
using System.IO;
using NAudio.Wave;

namespace PhotoBooth;

/// <summary>
/// Penulis AVI sederhana: video MJPEG + audio PCM 16-bit, dua aliran digabung (interleaved) dengan indeks idx1.
/// OpenCV VideoWriter tidak bisa merekam audio, jadi penggabungan dilakukan di sini; hasilnya lalu diubah
/// menjadi MP4 (H.264 + AAC) lewat Media Foundation bawaan Windows.
/// </summary>
public sealed class AviMjpegWriter : IDisposable
{
    readonly FileStream fs;
    readonly BinaryWriter bw;
    readonly object gate = new();
    readonly int width, height, fps;
    readonly bool hasAudio;
    readonly int audioRate, audioChannels;

    readonly List<(uint Id, int Offset, int Size)> index = new();
    long riffSizePos, avihFramesPos, vidLengthPos, audLengthPos, moviSizePos, moviFourCcPos;
    int videoFrames;
    long audioBytes;
    int maxChunk;
    bool closed;

    public int VideoFrames => videoFrames;

    public AviMjpegWriter(string path, int width, int height, int fps, bool hasAudio, int audioRate = 44100, int audioChannels = 1)
    {
        this.width = width;
        this.height = height;
        this.fps = fps;
        this.hasAudio = hasAudio;
        this.audioRate = audioRate;
        this.audioChannels = audioChannels;

        fs = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
        bw = new BinaryWriter(fs);
        WriteHeaders();
    }

    static uint Fcc(string s) => (uint)(s[0] | (s[1] << 8) | (s[2] << 16) | (s[3] << 24));

    void W32(uint v) => bw.Write(v);
    void W32(int v) => bw.Write(v);
    void W16(int v) => bw.Write((short)v);
    void Cc(string s) => W32(Fcc(s));

    void WriteHeaders()
    {
        Cc("RIFF");
        riffSizePos = fs.Position;
        W32(0);
        Cc("AVI ");

        int streams = hasAudio ? 2 : 1;
        int strlVideo = 4 + 64 + 48;                  // 'strl' + strh + strf
        int strlAudio = 4 + 64 + 24;
        int hdrlData = 4 + 64 + (8 + strlVideo) + (hasAudio ? 8 + strlAudio : 0);

        Cc("LIST");
        W32(hdrlData);
        Cc("hdrl");

        // ---- avih ----
        Cc("avih");
        W32(56);
        W32(1_000_000 / fps);                          // dwMicroSecPerFrame
        W32(0);                                        // dwMaxBytesPerSec
        W32(0);                                        // dwPaddingGranularity
        W32(0x10 | 0x100);                             // AVIF_HASINDEX | AVIF_ISINTERLEAVED
        avihFramesPos = fs.Position;
        W32(0);                                        // dwTotalFrames (diisi saat Close)
        W32(0);                                        // dwInitialFrames
        W32(streams);
        W32(1 << 20);                                  // dwSuggestedBufferSize
        W32(width);
        W32(height);
        W32(0); W32(0); W32(0); W32(0);                // reserved

        // ---- stream video ----
        Cc("LIST");
        W32(strlVideo);
        Cc("strl");
        Cc("strh");
        W32(56);
        Cc("vids");
        Cc("MJPG");
        W32(0);                                        // flags
        W16(0); W16(0);                                // priority, language
        W32(0);                                        // initial frames
        W32(1);                                        // scale
        W32(fps);                                      // rate
        W32(0);                                        // start
        vidLengthPos = fs.Position;
        W32(0);                                        // length (diisi saat Close)
        W32(1 << 20);                                  // suggested buffer
        W32(unchecked((int)0xFFFFFFFF));               // quality
        W32(0);                                        // sample size
        W16(0); W16(0); W16(width); W16(height);       // rcFrame
        Cc("strf");
        W32(40);
        W32(40);                                       // biSize
        W32(width);
        W32(height);
        W16(1);                                        // planes
        W16(24);                                       // bit count
        Cc("MJPG");                                    // biCompression
        W32(width * height * 3);
        W32(0); W32(0); W32(0); W32(0);

        // ---- stream audio ----
        if (hasAudio)
        {
            int blockAlign = audioChannels * 2;
            Cc("LIST");
            W32(strlAudio);
            Cc("strl");
            Cc("strh");
            W32(56);
            Cc("auds");
            W32(0);                                    // handler
            W32(0);                                    // flags
            W16(0); W16(0);
            W32(0);
            W32(1);                                    // scale
            W32(audioRate);                            // rate
            W32(0);
            audLengthPos = fs.Position;
            W32(0);                                    // length (sampel; diisi saat Close)
            W32(audioRate * blockAlign / 2);
            W32(unchecked((int)0xFFFFFFFF));
            W32(blockAlign);                           // sample size
            W16(0); W16(0); W16(0); W16(0);
            Cc("strf");
            W32(16);
            W16(1);                                    // PCM
            W16(audioChannels);
            W32(audioRate);
            W32(audioRate * blockAlign);
            W16(blockAlign);
            W16(16);
        }

        Cc("LIST");
        moviSizePos = fs.Position;
        W32(0);
        moviFourCcPos = fs.Position;
        Cc("movi");
    }

    void WriteChunk(string id, byte[] data, int len)
    {
        int offset = (int)(fs.Position - moviFourCcPos);
        Cc(id);
        W32(len);
        bw.Write(data, 0, len);
        if ((len & 1) == 1) bw.Write((byte)0);
        index.Add((Fcc(id), offset, len));
        if (len > maxChunk) maxChunk = len;
    }

    public void WriteVideo(byte[] jpeg)
    {
        lock (gate)
        {
            if (closed) return;
            WriteChunk("00dc", jpeg, jpeg.Length);
            videoFrames++;
        }
    }

    public void WriteAudio(byte[] pcm, int count)
    {
        if (!hasAudio) return;
        lock (gate)
        {
            if (closed || count <= 0) return;
            WriteChunk("01wb", pcm, count);
            audioBytes += count;
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (closed) return;
            closed = true;

            long moviEnd = fs.Position;
            Cc("idx1");
            W32(index.Count * 16);
            foreach (var e in index)
            {
                W32(e.Id);
                W32(e.Id == Fcc("00dc") ? 0x10 : 0);    // keyframe
                W32(e.Offset);
                W32(e.Size);
            }
            long end = fs.Position;

            fs.Position = riffSizePos;
            W32((int)(end - 8));
            fs.Position = moviSizePos;
            W32((int)(moviEnd - moviFourCcPos));
            fs.Position = avihFramesPos;
            W32(videoFrames);
            fs.Position = vidLengthPos;
            W32(videoFrames);
            if (hasAudio)
            {
                fs.Position = audLengthPos;
                W32((int)(audioBytes / (audioChannels * 2)));
            }

            bw.Flush();
            bw.Dispose();
            fs.Dispose();
        }
    }
}

/// <summary>Merekam mikrofon bawaan (44,1 kHz, mono, 16-bit) lewat NAudio. Mendukung jeda.</summary>
public sealed class AudioRecorder : IDisposable
{
    public const int Rate = 44100;
    public const int Channels = 1;

    WaveInEvent? wave;
    volatile bool paused;

    /// <summary>Mengembalikan false bila tidak ada mikrofon / akses ditolak.</summary>
    public bool Start(Action<byte[], int> sink)
    {
        try
        {
            wave = new WaveInEvent
            {
                WaveFormat = new WaveFormat(Rate, 16, Channels),
                BufferMilliseconds = 40,
            };
            wave.DataAvailable += (_, e) =>
            {
                if (!paused && e.BytesRecorded > 0) sink(e.Buffer, e.BytesRecorded);
            };
            wave.StartRecording();
            return true;
        }
        catch
        {
            wave?.Dispose();
            wave = null;
            return false;
        }
    }

    public void Pause() => paused = true;
    public void Resume() => paused = false;

    public void Dispose()
    {
        try { wave?.StopRecording(); } catch { }
        wave?.Dispose();
        wave = null;
    }
}
