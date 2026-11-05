using System;
using System.Threading.Tasks;
using Microsoft.Windows.AI.MachineLearning;

namespace PhotoBooth;

/// <summary>
/// Windows ML menyediakan ONNX Runtime bersama dan dapat mengunduh execution provider (EP) khusus
/// perangkat keras (Intel OpenVINO, Qualcomm QNN, AMD VitisAI, dll). EP perangkat keras butuh
/// Windows 11 24H2 (build 26100) atau lebih baru; di sistem lain panggilan ini gagal dengan aman
/// dan aplikasi tetap berjalan di GPU (DirectML) atau CPU.
/// </summary>
public static class AiRuntime
{
    static bool tried;

    public static string LastError { get; private set; } = "";

    public static async Task RegisterProvidersAsync()
    {
        if (tried) return;
        tried = true;
        try
        {
            var catalog = ExecutionProviderCatalog.GetDefault();
            await catalog.EnsureAndRegisterCertifiedAsync();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
    }
}
