using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using WinPenguins.Platform;

namespace WinPenguins.Diagnostics;

/// <summary>
/// Records deduplicated runtime exceptions, frame stalls, GPU recoveries, and handle diagnostics
/// into a rolling JSONL log under %LocalAppData%\WinPenguins so silent edge-case bugs stay visible.
/// </summary>
public sealed class DiagnosticsReporter
{
    public const int DefaultMaxEventsPerSession = 64;
    private const long MaxLogBytes = 512 * 1024;
    private const double SlowFrameThresholdMs = 100.0;
    private const double StallLogThresholdMs = 350.0;

    private static readonly Lock InstallLock = new();
    private static bool s_handlersInstalled;
    private static DiagnosticsReporter s_default = CreateDefault();

    private readonly Lock _sync = new();
    private readonly string _logPath;
    private readonly int _maxEventsPerSession;
    private readonly HashSet<string> _seenKeys = new(StringComparer.Ordinal);
    private readonly Queue<DiagnosticEntry> _recent = new();
    private int _recordedCount;
    private long _totalFrames;
    private long _slowFrames;
    private double _worstFrameMs;

    public DiagnosticsReporter(string logPath, int maxEventsPerSession = DefaultMaxEventsPerSession)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logPath);
        _logPath = logPath;
        _maxEventsPerSession = Math.Max(1, maxEventsPerSession);
    }

    public static DiagnosticsReporter Default => s_default;

    public string LogPath => _logPath;

    public int RecordedEventCount
    {
        get
        {
            lock (_sync)
            {
                return _recordedCount;
            }
        }
    }

    public (long TotalFrames, long SlowFrames, double WorstFrameMs) GetFrameStats()
    {
        lock (_sync)
        {
            return (_totalFrames, _slowFrames, _worstFrameMs);
        }
    }

    public IReadOnlyList<DiagnosticEntry> GetRecentEntries()
    {
        lock (_sync)
        {
            return [.. _recent];
        }
    }

    public static string GetDefaultLogPath()
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinPenguins");
        return Path.Combine(dir, "diagnostics.jsonl");
    }

    public static DiagnosticsReporter CreateDefault() => new(GetDefaultLogPath());

    public static void InstallProcessHandlers(DiagnosticsReporter? reporter = null)
    {
        lock (InstallLock)
        {
            if (reporter != null)
            {
                s_default = reporter;
            }

            if (s_handlersInstalled)
            {
                return;
            }

            s_handlersInstalled = true;
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) => s_default.RecordException("UIThread", e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                if (e.ExceptionObject is Exception ex)
                {
                    s_default.RecordException(e.IsTerminating ? "AppDomainFatal" : "AppDomain", ex);
                }
                else
                {
                    s_default.RecordAnomaly("AppDomain", Convert.ToString(e.ExceptionObject, CultureInfo.InvariantCulture) ?? "Unknown object");
                }
            };
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                s_default.RecordException("UnobservedTask", e.Exception);
                e.SetObserved();
            };
        }
    }

    public bool RecordException(string source, Exception ex, string? context = null)
    {
        ArgumentNullException.ThrowIfNull(ex);
        string firstFrame = ExtractFirstStackLine(ex.StackTrace);
        string key = $"{source}|{ex.GetType().FullName}|{firstFrame}";
        var entry = new DiagnosticEntry(
            DateTimeOffset.UtcNow,
            GetAppVersion(),
            source,
            ex.GetType().Name,
            Truncate(ex.Message, 320),
            Truncate(context ?? string.Empty, 160),
            Truncate(ex.StackTrace ?? string.Empty, 2000));

        return RecordCore(key, entry, deduplicate: true);
    }

    public bool RecordAnomaly(string kind, string detail, bool deduplicate = true, string? dedupKey = null)
    {
        string safeKind = string.IsNullOrWhiteSpace(kind) ? "Anomaly" : kind.Trim();
        string safeDetail = Truncate(detail ?? string.Empty, 400);
        string key = string.IsNullOrEmpty(dedupKey) ? $"{safeKind}|{safeDetail}" : dedupKey;
        var entry = new DiagnosticEntry(
            DateTimeOffset.UtcNow,
            GetAppVersion(),
            safeKind,
            safeKind,
            safeDetail,
            string.Empty,
            string.Empty);

        return RecordCore(key, entry, deduplicate);
    }

    public void RecordFrameTiming(double elapsedMs)
    {
        if (!double.IsFinite(elapsedMs) || elapsedMs < 0)
        {
            return;
        }

        bool reportStall = false;
        lock (_sync)
        {
            _totalFrames++;
            if (elapsedMs > _worstFrameMs)
            {
                _worstFrameMs = elapsedMs;
            }

            if (elapsedMs > SlowFrameThresholdMs)
            {
                _slowFrames++;
            }

            if (elapsedMs > StallLogThresholdMs)
            {
                reportStall = true;
            }
        }

        if (reportStall)
        {
            RecordAnomaly(
                "FrameStall",
                string.Create(CultureInfo.InvariantCulture, $"Render step took {elapsedMs:0.0} ms"),
                deduplicate: true,
                dedupKey: "FrameStall");
        }
    }

    public static (uint GdiObjects, uint UserObjects) QueryProcessHandleCounts()
    {
        try
        {
            IntPtr proc = NativeMethods.GetCurrentProcess();
            return (
                NativeMethods.GetGuiResources(proc, NativeMethods.GR_GDIOBJECTS),
                NativeMethods.GetGuiResources(proc, NativeMethods.GR_USEROBJECTS));
        }
        catch
        {
            return (0, 0);
        }
    }

    public string BuildDiagnosticSnapshot(string? runtimeStats = null)
    {
        var sb = new StringBuilder();
        (long totalFrames, long slowFrames, double worstFrameMs) = GetFrameStats();
        (uint gdi, uint user) = QueryProcessHandleCounts();

        sb.AppendLine(CultureInfo.InvariantCulture, $"WinPenguins {GetAppVersion()}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"OS: {Environment.OSVersion} | .NET: {Environment.Version} | Process64: {Environment.Is64BitProcess}");
        if (!string.IsNullOrWhiteSpace(runtimeStats))
        {
            sb.AppendLine(runtimeStats);
        }

        sb.AppendLine(CultureInfo.InvariantCulture, $"Handles: GDI={gdi}, USER={user}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Frames: total={totalFrames}, slow(>{SlowFrameThresholdMs:0}ms)={slowFrames}, worst={worstFrameMs:0.0}ms");

        Screen[] screens = Screen.AllScreens;
        sb.AppendLine(CultureInfo.InvariantCulture, $"Monitors ({screens.Length}):");
        for (int i = 0; i < screens.Length; i++)
        {
            Screen s = screens[i];
            sb.AppendLine(CultureInfo.InvariantCulture, $"  [{i}] {s.Bounds} work={s.WorkingArea} primary={s.Primary}");
        }

        IReadOnlyList<DiagnosticEntry> recent = GetRecentEntries();
        sb.AppendLine(CultureInfo.InvariantCulture, $"Recent session anomalies/errors ({recent.Count}):");
        for (int i = 0; i < recent.Count; i++)
        {
            DiagnosticEntry e = recent[i];
            sb.AppendLine(CultureInfo.InvariantCulture, $"  [{e.TimestampUtc:HH:mm:ss}Z] {e.Source}/{e.Kind}: {e.Message}");
        }

        if (recent.Count == 0)
        {
            sb.AppendLine("  (none)");
        }

        sb.AppendLine(CultureInfo.InvariantCulture, $"Log file: {_logPath}");
        return sb.ToString();
    }

    private bool RecordCore(string key, DiagnosticEntry entry, bool deduplicate)
    {
        string jsonLine;
        lock (_sync)
        {
            if (_recordedCount >= _maxEventsPerSession)
            {
                return false;
            }

            if (deduplicate && !_seenKeys.Add(key))
            {
                return false;
            }

            _recordedCount++;
            _recent.Enqueue(entry);
            while (_recent.Count > 20)
            {
                _recent.Dequeue();
            }

            jsonLine = JsonSerializer.Serialize(entry);
        }

        WriteLineSafe(jsonLine);
        return true;
    }

    private void WriteLineSafe(string jsonLine)
    {
        try
        {
            string? dir = Path.GetDirectoryName(_logPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            if (File.Exists(_logPath))
            {
                var info = new FileInfo(_logPath);
                if (info.Length > MaxLogBytes)
                {
                    string rotatedPath = _logPath + ".prev";
                    File.Move(_logPath, rotatedPath, overwrite: true);
                }
            }

            File.AppendAllText(_logPath, jsonLine + Environment.NewLine, Encoding.UTF8);
        }
        catch
        {
            // Diagnostics logging must never crash the application.
        }
    }

    private static string ExtractFirstStackLine(string? stack)
    {
        if (string.IsNullOrWhiteSpace(stack))
        {
            return string.Empty;
        }

        int nl = stack.IndexOf('\n', StringComparison.Ordinal);
        return (nl >= 0 ? stack[..nl] : stack).Trim();
    }

    private static string Truncate(string value, int maxLen) =>
        value.Length <= maxLen ? value : value[..maxLen];

    private static string GetAppVersion()
    {
        Version? v = Assembly.GetExecutingAssembly().GetName().Version;
        return v?.ToString() ?? "2.1.0";
    }
}

public sealed record DiagnosticEntry(
    DateTimeOffset TimestampUtc,
    string Version,
    string Source,
    string Kind,
    string Message,
    string Context,
    string Stack);
