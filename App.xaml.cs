using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace PhotoBooth;

public partial class App : Application
{
    public static string LogPath => Path.Combine(AppSettings.Dir, "crash.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Error di thread UI: catat, beri tahu, dan lanjutkan bila memungkinkan
        DispatcherUnhandledException += (_, ev) =>
        {
            Log("UI", ev.Exception);
            ev.Handled = true;
            MessageBox.Show("Terjadi kesalahan:\n" + ev.Exception.Message +
                            "\n\nDetail dicatat di:\n" + LogPath,
                            "Photo Booth", MessageBoxButton.OK, MessageBoxImage.Warning);
        };

        // Error fatal di thread lain: tidak bisa dicegah, tapi dicatat untuk diagnosis
        AppDomain.CurrentDomain.UnhandledException += (_, ev) => Log("FATAL", ev.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, ev) =>
        {
            Log("Task", ev.Exception);
            ev.SetObserved();
        };
    }

    static void Log(string kind, Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(AppSettings.Dir);
            File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {kind}\n{ex}\n\n");
        }
        catch { }
    }
}
