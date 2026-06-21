using System.Text;
using System.Text.Json;

namespace NeoOrder.OneGate.Services;

/// <summary>
/// Global crash/exception capture. Hooks every unhandled-exception source (managed + native),
/// writes a diagnostic report to a local file synchronously when a crash happens (so it survives
/// the process dying), and best-effort uploads pending reports to the OneGate API on next launch.
/// No third-party SDK; reports contain only diagnostic fields, never wallet/account data.
/// </summary>
public static class CrashReporter
{
    const string ReportEndpoint = "api/app/crash";
    const int MaxStoredReports = 50;

    static readonly object fileLock = new();
    static string FilePath => Path.Combine(FileSystem.AppDataDirectory, "crash-reports.jsonl");

    /// <summary>Register the unhandled-exception handlers. Call as early as possible in startup.</summary>
    public static void Initialize()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Persist(e.ExceptionObject as Exception, "AppDomain");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Persist(e.Exception, "TaskScheduler");
            e.SetObserved();
        };
#if ANDROID
        Android.Runtime.AndroidEnvironment.UnhandledExceptionRaiser += (_, e) => Persist(e.Exception, "Android");
#elif IOS || MACCATALYST
        ObjCRuntime.Runtime.MarshalManagedException += (_, e) => Persist(e.Exception, "Apple");
#endif
    }

    static void Persist(Exception? exception, string source)
    {
        if (exception is null) return;
        try
        {
            CrashReport report = new()
            {
                Timestamp = DateTimeOffset.UtcNow,
                Source = source,
                Type = exception.GetType().FullName ?? nameof(Exception),
                Message = exception.Message,
                StackTrace = exception.ToString(),
                AppVersion = AppInfo.VersionString,
                Platform = $"{DeviceInfo.Platform} {DeviceInfo.VersionString}",
                Device = $"{DeviceInfo.Manufacturer} {DeviceInfo.Model}",
            };
            string line = JsonSerializer.Serialize(report, SharedOptions.JsonSerializerOptions);
            lock (fileLock)
                File.AppendAllText(FilePath, line + Environment.NewLine);
        }
        catch
        {
            // A crash handler must never throw.
        }
    }

    /// <summary>Upload pending crash reports to the OneGate API; clear them on success.</summary>
    public static async Task FlushAsync(HttpClient httpClient)
    {
        string[] lines;
        try
        {
            lock (fileLock)
            {
                if (!File.Exists(FilePath)) return;
                lines = File.ReadAllLines(FilePath);
            }
        }
        catch
        {
            return;
        }

        lines = [.. lines.Where(l => !string.IsNullOrWhiteSpace(l))];
        if (lines.Length == 0) { TryDelete(); return; }
        if (lines.Length > MaxStoredReports)
            lines = lines[^MaxStoredReports..];

        try
        {
            string payload = "[" + string.Join(",", lines) + "]";
            using StringContent content = new(payload, Encoding.UTF8, "application/json");
            HttpResponseMessage response = await httpClient.PostAsync(ReportEndpoint, content);
            if (response.IsSuccessStatusCode)
                TryDelete();
            else
                Persist(lines); // keep bounded for the next attempt
        }
        catch
        {
            Persist(lines); // endpoint unavailable/offline; keep for next launch
        }
    }

    static void Persist(string[] lines)
    {
        try
        {
            lock (fileLock)
                File.WriteAllText(FilePath, string.Join(Environment.NewLine, lines) + Environment.NewLine);
        }
        catch
        {
        }
    }

    static void TryDelete()
    {
        try
        {
            lock (fileLock)
                if (File.Exists(FilePath)) File.Delete(FilePath);
        }
        catch
        {
        }
    }
}

public sealed class CrashReport
{
    public DateTimeOffset Timestamp { get; set; }
    public required string Source { get; set; }
    public required string Type { get; set; }
    public required string Message { get; set; }
    public required string StackTrace { get; set; }
    public required string AppVersion { get; set; }
    public required string Platform { get; set; }
    public required string Device { get; set; }
}
