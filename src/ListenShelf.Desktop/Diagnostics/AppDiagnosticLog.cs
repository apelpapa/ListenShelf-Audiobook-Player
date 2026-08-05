using System.Runtime.InteropServices;
using ListenShelf.Infrastructure.Storage;

namespace ListenShelf.Desktop.Diagnostics;

public sealed class AppDiagnosticLog
{
    private const long MaximumLogBytes = 2L * 1024L * 1024L;
    private readonly object _syncRoot = new();

    private AppDiagnosticLog(string? logFilePath)
    {
        LogFilePath = logFilePath;
    }

    public string? LogFilePath { get; }

    public static AppDiagnosticLog CreateDefault()
    {
        try
        {
            var paths = ListenShelfPaths.CreateDefault();
            Directory.CreateDirectory(paths.LogDirectoryPath);
            var logPath = Path.Combine(paths.LogDirectoryPath, "listenshelf.log");
            RotateIfNeeded(logPath);
            return new AppDiagnosticLog(logPath);
        }
        catch
        {
            return new AppDiagnosticLog(logFilePath: null);
        }
    }

    public void WriteSessionStart()
    {
        Write(
            "INFO",
            $"Starting ListenShelf {GetApplicationVersion()} on {RuntimeInformation.OSDescription}; architecture {RuntimeInformation.ProcessArchitecture}; .NET {Environment.Version}.");
    }

    public void WriteInfo(string message) => Write("INFO", message);

    public void WriteError(string context, Exception exception) =>
        Write("ERROR", $"{context}{Environment.NewLine}{exception}");

    private void Write(string level, string message)
    {
        if (LogFilePath is null)
        {
            return;
        }

        lock (_syncRoot)
        {
            try
            {
                File.AppendAllText(
                    LogFilePath,
                    $"{DateTimeOffset.UtcNow:O} [{level}] {message}{Environment.NewLine}");
            }
            catch
            {
                // Logging is diagnostic only and must never prevent startup or playback.
            }
        }
    }

    private static void RotateIfNeeded(string logPath)
    {
        if (!File.Exists(logPath) || new FileInfo(logPath).Length < MaximumLogBytes)
        {
            return;
        }

        var previousPath = Path.Combine(
            Path.GetDirectoryName(logPath)!,
            "listenshelf.previous.log");
        File.Move(logPath, previousPath, overwrite: true);
    }

    private static string GetApplicationVersion() =>
        typeof(AppDiagnosticLog).Assembly.GetName().Version?.ToString()
        ?? "unknown";
}
