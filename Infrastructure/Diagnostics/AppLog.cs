using System.Diagnostics;
using System.Text;

namespace PortablePlayer.Infrastructure.Diagnostics;

public static class AppLog
{
    private static readonly object Sync = new();
    private static string? _logPath;

    public static string CurrentLogPath
    {
        get
        {
            EnsureInitialized();
            return _logPath!;
        }
    }

    public static void Initialize(string appRoot)
    {
        try
        {
            var logDir = Path.Combine(appRoot, "logs");
            Directory.CreateDirectory(logDir);
            _logPath = Path.Combine(logDir, $"segmentplayer-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            WriteInternal("INFO", "Bootstrap", "Logger initialized.");
        }
        catch
        {
            // Logging should never crash the app.
        }
    }

    public static void Info(string scope, string message) => WriteInternal("INFO", scope, message);

    public static void Warn(string scope, string message) => WriteInternal("WARN", scope, message);

    public static void Error(string scope, string message, Exception? exception = null)
    {
        var payload = exception is null
            ? message
            : $"{message} | {exception.GetType().Name}: {exception.Message}{Environment.NewLine}{exception.StackTrace}";
        WriteInternal("ERROR", scope, payload);
    }

    private static void EnsureInitialized()
    {
        if (!string.IsNullOrWhiteSpace(_logPath))
        {
            return;
        }

        Initialize(AppContext.BaseDirectory);
    }

    private static void WriteInternal(string level, string scope, string message)
    {
        try
        {
            EnsureInitialized();
            var path = _logPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            var line = BuildLogLine(level, scope, message);
            lock (Sync)
            {
                File.AppendAllText(path, line, Encoding.UTF8);
            }
        }
        catch
        {
            // Logging should never crash the app.
        }
    }

    private static string BuildLogLine(string level, string scope, string message)
    {
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var tid = Environment.CurrentManagedThreadId;
        var pid = Environment.ProcessId;
        return $"[{now}] [{level}] [pid={pid},tid={tid}] [{scope}] {message}{Environment.NewLine}";
    }
}
