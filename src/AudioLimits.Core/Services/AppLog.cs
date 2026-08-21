namespace AudioLimits.Core.Services;

public static class AppLog
{
    private static readonly object Gate = new();
    private static string? _logPath;
    private const long MaxBytes = 512 * 1024;

    public static string LogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AudioLimits",
        "logs");

    public static void Initialize()
    {
        try
        {
            var dir = LogDirectory;
            Directory.CreateDirectory(dir);
            _logPath = Path.Combine(dir, "AudioLimits.log");
            RotateIfNeeded();
            Info("Audio Limits starting");
        }
        catch
        {
            _logPath = null;
        }
    }

    public static void Info(string message) => Write("INFO", message, null);
    public static void Warn(string message) => Write("WARN", message, null);
    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        try
        {
            lock (Gate)
            {
                if (_logPath is null)
                    return;

                RotateIfNeeded();
                var line = $"{DateTimeOffset.Now:O} [{level}] {message}";
                if (ex is not null)
                    line += Environment.NewLine + ex;
                File.AppendAllText(_logPath, line + Environment.NewLine);
            }
        }
        catch
        {
            // Logging must never take the application down.
        }
    }

    private static void RotateIfNeeded()
    {
        if (_logPath is null || !File.Exists(_logPath))
            return;
        if (new FileInfo(_logPath).Length < MaxBytes)
            return;

        var backup = _logPath + ".1";
        File.Move(_logPath, backup, true);
    }
}
