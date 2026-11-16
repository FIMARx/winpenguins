using System.Drawing;

namespace WinPenguins.Platform;

public sealed class WindowSnapshot
{
    public IReadOnlyList<Rectangle> SolidWindows { get; }
    public bool FullscreenAppActive { get; }

    public WindowSnapshot(IReadOnlyList<Rectangle> solidWindows, bool fullscreenAppActive)
    {
        SolidWindows = solidWindows;
        FullscreenAppActive = fullscreenAppActive;
    }
}

public sealed unsafe class WindowTracker : IDisposable
{
    private static readonly HashSet<string> IgnoredWindowClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman",
        "WorkerW",
        "EdgeUiInputTopWndClass",
        "EdgeUiInputWndClass",
        "Shell_InputSwitchTopLevelWindow",
        "ForegroundStaging",
        "ApplicationManager_DesktopShellWindow",
        "Ghost",
        "Windows.UI.Core.CoreWindow",
        "SysShadow",
        "Shell_LightDismissOverlay"
    };

    private static readonly HashSet<string> TaskbarClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd"
    };

    private readonly uint _ownProcessId = (uint)Environment.ProcessId;
    private readonly NativeMethods.WinEventDelegate _winEventCallback;
    private readonly IntPtr _systemHook;
    private readonly Dictionary<uint, string> _processNameCache = [];

    private volatile bool _dirty = true;
    private int _refreshRunning;
    private long _lastRefreshTick;
    private WindowSnapshot _latest = new(Array.Empty<Rectangle>(), false);

    public event Action? ForegroundOrZOrderChanged;

    public WindowTracker()
    {
        _winEventCallback = OnWinEvent;
        // Hook only system-level window state transitions (foreground/move-size/minimize),
        // avoiding noisy EVENT_OBJECT_* accessibility/UIAutomation event storms.
        _systemHook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            NativeMethods.EVENT_SYSTEM_MINIMIZEEND,
            IntPtr.Zero,
            _winEventCallback,
            0,
            0,
            NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);
    }

    public void MarkDirty(bool urgent = true)
    {
        _dirty = true;
        if (urgent)
        {
            Volatile.Write(ref _lastRefreshTick, Math.Min(Volatile.Read(ref _lastRefreshTick), Environment.TickCount64 - 220));
        }
    }

    public WindowSnapshot GetSnapshot(
        IReadOnlyList<Screen> targetScreens,
        bool ignoreMaximizedWindows,
        IReadOnlySet<string>? ignoredProcessNames = null)
    {
        long now = Environment.TickCount64;
        long elapsed = now - Volatile.Read(ref _lastRefreshTick);
        bool shouldScan = _dirty ? elapsed >= 40 : elapsed >= 250;

        if (shouldScan && Interlocked.CompareExchange(ref _refreshRunning, 1, 0) == 0)
        {
            _dirty = false;
            Rectangle[] screenBounds = new Rectangle[targetScreens.Count];
            Rectangle[] workAreas = new Rectangle[targetScreens.Count];
            for (int i = 0; i < targetScreens.Count; i++)
            {
                screenBounds[i] = targetScreens[i].Bounds;
                workAreas[i] = targetScreens[i].WorkingArea;
            }

            if (Volatile.Read(ref _lastRefreshTick) == 0)
            {
                RunScan(screenBounds, workAreas, ignoreMaximizedWindows, ignoredProcessNames);
                Interlocked.Exchange(ref _refreshRunning, 0);
            }
            else
            {
                _ = Task.Run(() =>
                {
                    try
                    {
                        RunScan(screenBounds, workAreas, ignoreMaximizedWindows, ignoredProcessNames);
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _refreshRunning, 0);
                    }
                });
            }
        }

        return Volatile.Read(ref _latest);
    }

    private void RunScan(
        Rectangle[] screenBounds,
        Rectangle[] workAreas,
        bool ignoreMaximizedWindows,
        IReadOnlySet<string>? ignoredProcessNames)
    {
        var solidWindows = new List<Rectangle>(32);
        var occludedMonitors = new HashSet<int>();
        bool fullscreenActive = false;
        IntPtr foregroundHwnd = NativeMethods.GetForegroundWindow();

        NativeMethods.EnumWindows((hwnd, lParam) =>
        {
            _ = NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == _ownProcessId)
            {
                return true;
            }

            if (!NativeMethods.IsWindowVisible(hwnd) || NativeMethods.IsIconic(hwnd))
            {
                return true;
            }

            if (IsCloaked(hwnd))
            {
                return true;
            }

            string className = GetClassName(hwnd);
            if (IgnoredWindowClasses.Contains(className))
            {
                return true;
            }

            bool isTaskbar = TaskbarClasses.Contains(className);
            if (!isTaskbar && ignoredProcessNames is { Count: > 0 })
            {
                string procName = GetProcessNameCached(pid);
                if (procName.Length > 0 && ignoredProcessNames.Contains(procName))
                {
                    return true;
                }
            }
            nint exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
            bool isTransparent = (exStyle & NativeMethods.WS_EX_TRANSPARENT) != 0;
            bool isToolWindow = (exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0;
            bool isNoActivate = (exStyle & NativeMethods.WS_EX_NOACTIVATE) != 0;
            bool isLayered = (exStyle & NativeMethods.WS_EX_LAYERED) != 0;

            if (!isTaskbar && (isTransparent || (isToolWindow && isNoActivate)))
            {
                return true;
            }

            if (!isTaskbar && isLayered &&
                NativeMethods.GetLayeredWindowAttributes(hwnd, out _, out byte alpha, out uint flags) &&
                (flags & NativeMethods.LWA_ALPHA) != 0 && alpha == 0)
            {
                return true;
            }

            if (!TryGetVisibleBounds(hwnd, out Rectangle rect) || rect.Width <= 2 || rect.Height <= 2)
            {
                return true;
            }

            bool isTopmost = (exStyle & NativeMethods.WS_EX_TOPMOST) != 0;
            bool isMaximized = !isTaskbar && !isToolWindow && NativeMethods.IsZoomed(hwnd);
            bool isForegroundWindow = hwnd == foregroundHwnd
                || (foregroundHwnd != IntPtr.Zero && NativeMethods.GetWindow(hwnd, NativeMethods.GW_OWNER) == foregroundHwnd);

            bool intersectsAnyMonitor = false;
            bool allIntersectedMonitorsOccluded = true;

            for (int m = 0; m < screenBounds.Length; m++)
            {
                if (!rect.IntersectsWith(screenBounds[m]))
                {
                    continue;
                }

                intersectsAnyMonitor = true;

                if (!isTaskbar && isForegroundWindow && CoversMonitorFullscreen(rect, screenBounds[m]))
                {
                    fullscreenActive = true;
                }

                if (isMaximized && !isTopmost && CoversWorkArea(rect, workAreas[m]))
                {
                    if (ignoreMaximizedWindows)
                    {
                        occludedMonitors.Add(m);
                        return true;
                    }
                }

                if (!occludedMonitors.Contains(m))
                {
                    allIntersectedMonitorsOccluded = false;
                }
            }

            if (intersectsAnyMonitor && !isTaskbar && !isTopmost && allIntersectedMonitorsOccluded)
            {
                return true;
            }

            solidWindows.Add(rect);
            return true;
        }, IntPtr.Zero);

        Volatile.Write(ref _latest, new WindowSnapshot(solidWindows, fullscreenActive));
        Volatile.Write(ref _lastRefreshTick, Environment.TickCount64);
    }

    public static IntPtr FindDesktopHostWindow()
    {
        IntPtr progman = NativeMethods.FindWindow("Progman", "Program Manager");
        if (progman != IntPtr.Zero)
        {
            IntPtr defView = NativeMethods.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (defView != IntPtr.Zero)
            {
                return progman;
            }
        }

        IntPtr workerHost = IntPtr.Zero;
        NativeMethods.EnumWindows((hwnd, lParam) =>
        {
            if (GetClassName(hwnd).Equals("WorkerW", StringComparison.Ordinal))
            {
                IntPtr defView = NativeMethods.FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (defView != IntPtr.Zero)
                {
                    workerHost = hwnd;
                    return false;
                }
            }

            return true;
        }, IntPtr.Zero);

        return workerHost != IntPtr.Zero ? workerHost : progman;
    }

    private void OnWinEvent(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime)
    {
        if (idObject != NativeMethods.OBJID_WINDOW || idChild != NativeMethods.CHILDID_SELF)
        {
            return;
        }

        switch (eventType)
        {
            case NativeMethods.EVENT_SYSTEM_FOREGROUND:
                _dirty = true;
                ForegroundOrZOrderChanged?.Invoke();
                break;

            case NativeMethods.EVENT_SYSTEM_MOVESIZEEND:
            case NativeMethods.EVENT_SYSTEM_MINIMIZESTART:
            case NativeMethods.EVENT_SYSTEM_MINIMIZEEND:
                _dirty = true;
                break;
        }
    }

    private static bool IsCloaked(IntPtr hwnd)
    {
        int hr = NativeMethods.DwmGetWindowAttribute(
            hwnd,
            NativeMethods.DWMWA_CLOAKED,
            out int cloaked,
            sizeof(int));
        return hr == 0 && cloaked != 0;
    }

    private static bool TryGetVisibleBounds(IntPtr hwnd, out Rectangle bounds)
    {
        int hr = NativeMethods.DwmGetWindowAttribute(
            hwnd,
            NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS,
            out NativeMethods.RECT rect,
            sizeof(NativeMethods.RECT));

        if (hr != 0)
        {
            if (!NativeMethods.GetWindowRect(hwnd, out rect))
            {
                bounds = Rectangle.Empty;
                return false;
            }
        }

        bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        return true;
    }

    private static string GetClassName(IntPtr hwnd)
    {
        char* buffer = stackalloc char[256];
        int len = NativeMethods.GetClassName(hwnd, buffer, 256);
        return len > 0 ? new string(buffer, 0, len) : string.Empty;
    }

    private string GetProcessNameCached(uint pid)
    {
        if (_processNameCache.TryGetValue(pid, out string? cached))
        {
            return cached;
        }

        if (_processNameCache.Count > 512)
        {
            _processNameCache.Clear();
        }

        string resolved = QueryProcessName(pid);
        _processNameCache[pid] = resolved;
        return resolved;
    }

    private static string QueryProcessName(uint pid)
    {
        if (pid == 0)
        {
            return string.Empty;
        }

        IntPtr hProcess = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (hProcess == IntPtr.Zero)
        {
            return string.Empty;
        }

        try
        {
            char* buffer = stackalloc char[512];
            uint size = 512;
            if (!NativeMethods.QueryFullProcessImageName(hProcess, 0, buffer, ref size) || size == 0)
            {
                return string.Empty;
            }

            string fullPath = new(buffer, 0, (int)size);
            return Path.GetFileNameWithoutExtension(fullPath);
        }
        finally
        {
            NativeMethods.CloseHandle(hProcess);
        }
    }

    private static bool CoversMonitorFullscreen(Rectangle windowRect, Rectangle monitorBounds)
    {
        Rectangle clipped = Rectangle.Intersect(windowRect, monitorBounds);
        return clipped.Width >= monitorBounds.Width - 2 && clipped.Height >= monitorBounds.Height - 2;
    }

    private static bool CoversWorkArea(Rectangle windowRect, Rectangle workArea)
    {
        Rectangle clipped = Rectangle.Intersect(windowRect, workArea);
        return clipped.Width >= (int)(workArea.Width * 0.96)
            && clipped.Height >= (int)(workArea.Height * 0.96);
    }

    public void Dispose()
    {
        if (_systemHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_systemHook);
        }
    }
}
