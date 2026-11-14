using System.Text;

namespace ClevoControl.Core;

/// <summary>
/// Small rolling log file (%APPDATA%\ClevoControl\clevocontrol.log, max ~1 MB + one backup).
/// Records start-up, sleep/resume, profile/fan changes and every error, so problems can be
/// diagnosed afterwards. Contains no personal data.
/// </summary>
public static class Log
{
    private const long MaxBytes = 1024 * 1024;
    private static readonly object Sync = new();

    public static string FilePath { get; } = Path.Combine(Settings.SettingsStore.DefaultDirectory, "clevocontrol.log");

    public static void Info(string message) => Write("INFO ", message);

    public static void Warn(string message) => Write("WARN ", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");

    // Unit tests run in "testhost" – they must not write into the user's real log.
    private static readonly bool Disabled = AppDomain.CurrentDomain.FriendlyName.StartsWith("testhost", StringComparison.OrdinalIgnoreCase);

    private static void Write(string level, string message)
    {
        if (Disabled) return;
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > MaxBytes)
                    File.Move(FilePath, FilePath + ".1", true);
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} [{Environment.CurrentManagedThreadId}] {message}{Environment.NewLine}", Encoding.UTF8);
            }
        }
        catch
        {
            // logging must never break the app
        }
    }
}
