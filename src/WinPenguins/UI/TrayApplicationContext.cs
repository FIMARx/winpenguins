using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using Microsoft.Win32;
using WinPenguins.Assets;
using WinPenguins.Config;
using WinPenguins.Engine;
using WinPenguins.Platform;

namespace WinPenguins.UI;

public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly Icon _appIcon;
    private readonly Font _optionsBoldFont;
    private readonly ContextMenuStrip _trayMenu;
    private readonly NotifyIcon _trayIcon;
    private readonly FramePacer _framePacer;
    private readonly WindowTracker _windowTracker;
    private readonly OverlayHost _overlayHost;
    private readonly SoundPlayerService _soundService;
    private readonly GlobalHotkeyWindow _hotkeyWindow;
    private readonly System.Windows.Forms.Timer _batteryCheckTimer;
    private readonly CollisionWorld _world;
    private readonly PenguinSimulation _simulation;
    private readonly List<(int X, int Y, long TimestampMs)> _dragSamples = [];

    private readonly ToolStripMenuItem _pauseMenuItem;
    private readonly ToolStripMenuItem _aboveWindowsMenuItem;

    private SpriteCatalog _spriteCatalog;
    private AppSettings _settings;
    private IReadOnlyList<double> _cachedMonitorScales = [];
    private IReadOnlySet<string> _ignoredProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private double _activeScale;
    private bool _paused;
    private bool _pausedByUser;
    private bool _pausedByBatterySaver;
    private bool _exiting;
    private bool _hiddenForFullscreen;
    private bool _screenshotPending;
    private long _lastMonitorCheckTick;
    private Form? _activeDialog;

    private Penguin? _grabbedPenguin;
    private Point _grabStartPoint;
    private bool _grabMovedSignificantly;

    public TrayApplicationContext()
    {
        _appIcon = LoadEmbeddedIcon();
        _settings = SettingsStore.Load();
        _ignoredProcesses = BuildIgnoredSet(_settings);
        _soundService = new SoundPlayerService();
        _windowTracker = new WindowTracker();

        _activeScale = ResolveEffectiveScale(_settings);
        _spriteCatalog = SpriteCatalog.LoadTheme(_settings.ThemeName, 1.0);
        _overlayHost = new OverlayHost(_settings, _spriteCatalog);

        _cachedMonitorScales = _overlayHost.GetMonitorDpiScales(_settings);
        _world = new CollisionWorld(GetMonitorBounds(_overlayHost.ActiveScreens), monitorScales: _cachedMonitorScales);
        _simulation = new PenguinSimulation(_world, _activeScale, specs: _spriteCatalog.GetSpecsMap());
        _overlayHost.SetPointHitTest((x, y) =>
            !_paused && !_hiddenForFullscreen && !_exiting && _settings.InteractiveMouse && _simulation.HitTest(x, y) is not null);
        _overlayHost.MouseDown += OnOverlayMouseDown;
        _overlayHost.MouseMove += OnOverlayMouseMove;
        _overlayHost.MouseUp += OnOverlayMouseUp;
        _overlayHost.CaptureLost += OnOverlayCaptureLost;
        _simulation.SoundRequested += kind => _soundService.PlayEvent(kind, _settings);
        _simulation.EnsureCount(_settings.PenguinCount);

        _windowTracker.ForegroundOrZOrderChanged += OnForegroundOrZOrderChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        _hotkeyWindow = new GlobalHotkeyWindow();
        _hotkeyWindow.HotkeyPressed += TogglePause;
        string? startupHotkeyWarning = null;
        if (!_hotkeyWindow.UpdateBinding(_settings.EnablePauseHotkey, _settings.PauseHotkey, out string? hotkeyErr) &&
            hotkeyErr is not null)
        {
            startupHotkeyWarning = hotkeyErr;
        }

        _batteryCheckTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        _batteryCheckTimer.Tick += (_, _) => UpdateBatterySaverState();

        _pauseMenuItem = new ToolStripMenuItem("Pause", null, (_, _) => TogglePause());
        _aboveWindowsMenuItem = new ToolStripMenuItem("Walk Above Windows", null, (_, _) => ToggleLayerMode())
        {
            Checked = _settings.LayerMode == OverlayLayerMode.AboveWindows
        };
        _trayMenu = new ContextMenuStrip();
        _optionsBoldFont = new Font(_trayMenu.Font, FontStyle.Bold);
        var optionsItem = new ToolStripMenuItem("Options...", null, (_, _) => ShowOptions())
        {
            Font = _optionsBoldFont
        };

        _trayMenu.Items.Add(_pauseMenuItem);
        _trayMenu.Items.Add(_aboveWindowsMenuItem);
        _trayMenu.Items.Add(new ToolStripMenuItem("Scatter Penguins", null, (_, _) => _simulation.ScatterAll()));
        _trayMenu.Items.Add(new ToolStripMenuItem("Explode All (Bombers)", null, (_, _) => _simulation.ExplodeAllToBombers()));
        _trayMenu.Items.Add(new ToolStripSeparator());
        _trayMenu.Items.Add(optionsItem);
        _trayMenu.Items.Add(new ToolStripMenuItem("Screenshot...", null, (_, _) => ScheduleScreenshot()));
        _trayMenu.Items.Add(new ToolStripMenuItem("About WinPenguins...", null, (_, _) => ShowAbout()));
        _trayMenu.Items.Add(new ToolStripSeparator());
        _trayMenu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => BeginExit()));

        _trayIcon = new NotifyIcon
        {
            Icon = _appIcon,
            Text = "WinPenguins",
            ContextMenuStrip = _trayMenu,
            Visible = true
        };
        if (startupHotkeyWarning is not null)
        {
            _trayIcon.ShowBalloonTip(4000, "WinPenguins Hotkey", startupHotkeyWarning, ToolTipIcon.Warning);
        }
        _trayIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                ShowOptions();
            }
        };

        UpdatePauseMenuAndTooltip();
        _aboveWindowsMenuItem.Checked = _settings.LayerMode == OverlayLayerMode.AboveWindows;
        UpdateBatteryTimerState();
        UpdateBatterySaverState();
        StepAndRender(forceRedraw: true, deltaMs: 8.333);
        _framePacer = new FramePacer(_settings.TargetFps, deltaMs => StepAndRender(forceRedraw: false, deltaMs: deltaMs));
        _framePacer.SetPaused(_paused);
    }

    private void StepAndRender(bool forceRedraw, double deltaMs)
    {
        long now = Environment.TickCount64;
        if (forceRedraw || (now - _lastMonitorCheckTick) >= 500)
        {
            _lastMonitorCheckTick = now;
            if (!_overlayHost.MatchesMonitorConfig(_settings))
            {
                RebuildMonitors();
                forceRedraw = true;
            }
        }

        WindowSnapshot snapshot = _windowTracker.GetSnapshot(
            _overlayHost.ActiveScreens,
            _settings.IgnoreMaximizedWindows,
            _ignoredProcesses);

        bool shouldHide = _settings.HideInFullscreen && snapshot.FullscreenAppActive && !_exiting;
        if (shouldHide != _hiddenForFullscreen)
        {
            _hiddenForFullscreen = shouldHide;
            _overlayHost.SetVisible(!shouldHide && !_paused);
            if (!shouldHide && !_paused)
            {
                forceRedraw = true;
            }
        }

        if (_hiddenForFullscreen || _paused)
        {
            return;
        }

        _world.Update(GetMonitorBounds(_overlayHost.ActiveScreens), snapshot.SolidWindows, _cachedMonitorScales);

        Point? cursor = null;
        if (_settings.CursorAvoidance && NativeMethods.GetCursorPos(out NativeMethods.POINT pt))
        {
            cursor = new Point(pt.X, pt.Y);
        }

        if (_exiting)
        {
            if (deltaMs > 0)
            {
                StepExiting(deltaMs, cursor);
            }
            else
            {
                _overlayHost.RenderAll(_simulation.Penguins, _settings.Opacity, forceRedraw: true);
            }
            return;
        }

        if (deltaMs > 0)
        {
            _simulation.Step(_settings, deltaMs, cursor);
        }

        _overlayHost.EnsureDesktopZOrderIfNeeded();
        _overlayHost.UpdateInteractiveHover(
            _settings.InteractiveMouse,
            _grabbedPenguin is not null,
            (hx, hy) => _simulation.HitTest(hx, hy) is not null);
        _overlayHost.RenderAll(_simulation.Penguins, _settings.Opacity, forceRedraw);
    }

    private void StepExiting(double deltaMs, Point? cursor)
    {
        var exitingSettings = _settings.Clone();
        exitingSettings.PenguinCount = 0;
        _simulation.Step(exitingSettings, deltaMs, cursor);
        _overlayHost.RenderAll(_simulation.Penguins, _settings.Opacity, forceRedraw: true);

        if (_simulation.Penguins.Count == 0)
        {
            ExitThread();
        }
    }

    private void TogglePause()
    {
        _pausedByUser = !_pausedByUser;
        ApplyPausedState();
    }

    private void UpdateBatterySaverState()
    {
        bool active = _settings.PauseOnBatterySaver && PowerMonitor.IsBatterySaverActive();
        if (active != _pausedByBatterySaver)
        {
            _pausedByBatterySaver = active;
            ApplyPausedState();
        }
    }

    private void UpdateBatteryTimerState()
    {
        _batteryCheckTimer.Enabled = _settings.PauseOnBatterySaver;
    }

    private void ApplyPausedState()
    {
        bool effectivelyPaused = _pausedByUser || _pausedByBatterySaver;
        UpdatePauseMenuAndTooltip();

        if (effectivelyPaused == _paused)
        {
            return;
        }

        _paused = effectivelyPaused;
        if (_paused && _grabbedPenguin is not null)
        {
            _simulation.ReleaseGrab(_grabbedPenguin, 0, 0, wasClick: false);
            _grabbedPenguin = null;
            _dragSamples.Clear();
        }

        _overlayHost.SetVisible(!_paused && !_hiddenForFullscreen);
        _framePacer.SetPaused(_paused);
        if (!_paused)
        {
            StepAndRender(forceRedraw: true, deltaMs: 0);
        }
    }

    private void UpdatePauseMenuAndTooltip()
    {
        string baseLabel = _pausedByUser ? "Resume" : "Pause";
        _pauseMenuItem.Text = _settings.EnablePauseHotkey && !string.IsNullOrWhiteSpace(_settings.PauseHotkey)
            ? $"{baseLabel} ({_settings.PauseHotkey})"
            : baseLabel;

        _trayIcon.Text = (_pausedByUser, _pausedByBatterySaver) switch
        {
            (true, _) => "WinPenguins (Paused)",
            (false, true) => "WinPenguins (Paused - Battery Saver)",
            _ => "WinPenguins"
        };
    }

    private void ToggleLayerMode()
    {
        AppSettings next = _settings.Clone();
        next.LayerMode = next.LayerMode == OverlayLayerMode.AboveWindows
            ? OverlayLayerMode.DesktopOnly
            : OverlayLayerMode.AboveWindows;
        ApplySettings(next);
        SettingsStore.Save(_settings);
    }

    private void ShowOptions()
    {
        if (_activeDialog != null)
        {
            _activeDialog.Activate();
            return;
        }

        using var dlg = new OptionsForm(_appIcon, _settings, _soundService, ApplySettings);
        _activeDialog = dlg;
        if (dlg.ShowDialog() == DialogResult.OK)
        {
            SettingsStore.Save(_settings);
        }
        _activeDialog = null;
    }

    private void ShowAbout()
    {
        if (_activeDialog != null)
        {
            _activeDialog.Activate();
            return;
        }

        using var dlg = new AboutForm(_appIcon);
        _activeDialog = dlg;
        dlg.ShowDialog();
        _activeDialog = null;
    }

    private void ScheduleScreenshot()
    {
        if (_activeDialog != null || _screenshotPending)
        {
            _activeDialog?.Activate();
            return;
        }

        _screenshotPending = true;
        var delayTimer = new System.Windows.Forms.Timer { Interval = 200 };
        delayTimer.Tick += (_, _) =>
        {
            delayTimer.Stop();
            delayTimer.Dispose();
            _screenshotPending = false;
            StepAndRender(forceRedraw: true, deltaMs: 0);
            ScreenCaptureService.CaptureAndSaveInteractive();
        };
        delayTimer.Start();
    }

    private void ApplySettings(AppSettings updated)
    {
        updated.Normalize();
        bool monitorChange = updated.PrimaryMonitorOnly != _settings.PrimaryMonitorOnly;
        bool layerChange = updated.LayerMode != _settings.LayerMode;
        bool ignoreMaxChange = updated.IgnoreMaximizedWindows != _settings.IgnoreMaximizedWindows;
        bool ignoredProcChange = !string.Equals(updated.IgnoredProcesses, _settings.IgnoredProcesses, StringComparison.OrdinalIgnoreCase);
        bool themeChange = !string.Equals(updated.ThemeName, _settings.ThemeName, StringComparison.OrdinalIgnoreCase);
        bool scaleChange = updated.ScaleMode != _settings.ScaleMode;
        bool hotkeyChange = updated.EnablePauseHotkey != _settings.EnablePauseHotkey ||
            !string.Equals(updated.PauseHotkey, _settings.PauseHotkey, StringComparison.OrdinalIgnoreCase);

        _settings = updated;
        _framePacer.SetTargetFps(_settings.TargetFps);

        if (ignoredProcChange)
        {
            _ignoredProcesses = BuildIgnoredSet(_settings);
            _windowTracker.MarkDirty(urgent: true);
        }
        else if (ignoreMaxChange)
        {
            _windowTracker.MarkDirty(urgent: true);
        }

        if (themeChange)
        {
            SpriteCatalog nextCatalog = SpriteCatalog.LoadTheme(_settings.ThemeName, 1.0);
            _overlayHost.ReloadCatalog(nextCatalog);
            _simulation.UpdateSpecs(nextCatalog.GetSpecsMap());
            _spriteCatalog = nextCatalog;
        }

        if (monitorChange)
        {
            _overlayHost.Rebuild(_settings);
            _cachedMonitorScales = _overlayHost.GetMonitorDpiScales(_settings);
            _world.Update(GetMonitorBounds(_overlayHost.ActiveScreens), _world.Windows, _cachedMonitorScales);
        }
        else
        {
            if (scaleChange)
            {
                _cachedMonitorScales = _overlayHost.GetMonitorDpiScales(_settings);
                _world.Update(GetMonitorBounds(_overlayHost.ActiveScreens), _world.Windows, _cachedMonitorScales);
            }

            if (layerChange)
            {
                _overlayHost.ApplyLayerMode(_settings.LayerMode);
            }
        }

        double newScale = ResolveEffectiveScale(_settings);
        if (Math.Abs(newScale - _activeScale) > 0.001)
        {
            _activeScale = newScale;
            _simulation.SetScale(_activeScale);
        }

        if (hotkeyChange)
        {
            if (!_hotkeyWindow.UpdateBinding(_settings.EnablePauseHotkey, _settings.PauseHotkey, out string? err) &&
                err is not null)
            {
                _trayIcon.ShowBalloonTip(3500, "WinPenguins Hotkey", err, ToolTipIcon.Warning);
            }
        }

        UpdatePauseMenuAndTooltip();
        _aboveWindowsMenuItem.Checked = _settings.LayerMode == OverlayLayerMode.AboveWindows;
        UpdateBatteryTimerState();
        UpdateBatterySaverState();

        _simulation.EnsureCount(_settings.PenguinCount);
        StepAndRender(forceRedraw: true, deltaMs: 0);
    }

    private void RebuildMonitors()
    {
        _overlayHost.Rebuild(_settings);
        _cachedMonitorScales = _overlayHost.GetMonitorDpiScales(_settings);
        _world.Update(GetMonitorBounds(_overlayHost.ActiveScreens), _world.Windows, _cachedMonitorScales);

        double newScale = ResolveEffectiveScale(_settings);
        if (Math.Abs(newScale - _activeScale) > 0.001)
        {
            _activeScale = newScale;
            _simulation.SetScale(_activeScale);
            StepAndRender(forceRedraw: true, deltaMs: 0);
        }
    }

    private void BeginExit()
    {
        if (_exiting || _paused || _hiddenForFullscreen || _simulation.Penguins.Count == 0)
        {
            ExitThread();
            return;
        }

        _exiting = true;
        _simulation.ExplodeAllToBombers();
    }

    private void OnForegroundOrZOrderChanged()
    {
        if (_settings.LayerMode == OverlayLayerMode.DesktopOnly)
        {
            _overlayHost.MarkDesktopZOrderDirty();
            _overlayHost.EnsureDesktopZOrderIfNeeded();
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        _windowTracker.MarkDirty(urgent: true);
        RebuildMonitors();
    }

    private void OnOverlayMouseDown(int screenX, int screenY)
    {
        if (!_settings.InteractiveMouse || _paused || _hiddenForFullscreen || _exiting)
        {
            return;
        }

        Penguin? hit = _simulation.HitTest(screenX, screenY);
        if (hit is null)
        {
            return;
        }

        _grabbedPenguin = hit;
        _grabStartPoint = new Point(screenX, screenY);
        _grabMovedSignificantly = false;
        _dragSamples.Clear();
        _dragSamples.Add((screenX, screenY, Stopwatch.GetTimestamp() * 1000L / Stopwatch.Frequency));
        _simulation.BeginGrab(hit);
    }

    private void OnOverlayMouseMove(int screenX, int screenY)
    {
        if (_grabbedPenguin is null)
        {
            return;
        }

        if (Math.Abs(screenX - _grabStartPoint.X) > 4 || Math.Abs(screenY - _grabStartPoint.Y) > 4)
        {
            _grabMovedSignificantly = true;
        }

        long nowMs = Stopwatch.GetTimestamp() * 1000L / Stopwatch.Frequency;
        _dragSamples.Add((screenX, screenY, nowMs));
        if (_dragSamples.Count > 8)
        {
            _dragSamples.RemoveAt(0);
        }

        _simulation.MoveGrab(_grabbedPenguin, screenX, screenY);
        StepAndRender(forceRedraw: true, deltaMs: 0);
    }

    private void OnOverlayMouseUp(int screenX, int screenY)
    {
        if (_grabbedPenguin is null)
        {
            return;
        }

        Penguin released = _grabbedPenguin;
        _grabbedPenguin = null;

        long nowMs = Stopwatch.GetTimestamp() * 1000L / Stopwatch.Frequency;
        _dragSamples.Add((screenX, screenY, nowMs));

        bool wasClick = !_grabMovedSignificantly;
        (double vx, double vy) = ComputeThrowVelocity(_dragSamples, nowMs);
        _dragSamples.Clear();

        _simulation.ReleaseGrab(released, vx, vy, wasClick);
        _overlayHost.UpdateInteractiveHover(
            _settings.InteractiveMouse,
            isDragging: false,
            (hx, hy) => _simulation.HitTest(hx, hy) is not null);
        StepAndRender(forceRedraw: true, deltaMs: 0);
    }

    private void OnOverlayCaptureLost()
    {
        if (_grabbedPenguin is null)
        {
            return;
        }

        Penguin released = _grabbedPenguin;
        _grabbedPenguin = null;
        _dragSamples.Clear();
        _simulation.ReleaseGrab(released, 0, 0, wasClick: false);
    }

    private static (double Vx, double Vy) ComputeThrowVelocity(
        List<(int X, int Y, long TimestampMs)> samples,
        long nowMs)
    {
        if (samples.Count < 2)
        {
            return (0, 0);
        }

        (int x1, int y1, long t1) = samples[^1];
        (int x0, int y0, long t0) = samples[0];

        // Prefer samples within the last ~100ms for responsive throw velocity.
        for (int i = samples.Count - 2; i >= 0; i--)
        {
            if (nowMs - samples[i].TimestampMs <= 110)
            {
                x0 = samples[i].X;
                y0 = samples[i].Y;
                t0 = samples[i].TimestampMs;
            }
            else
            {
                break;
            }
        }

        long dt = Math.Max(8, t1 - t0);
        return ((x1 - x0) * 1000.0 / dt, (y1 - y0) * 1000.0 / dt);
    }

    private static HashSet<string> BuildIgnoredSet(AppSettings settings) =>
        new(settings.ParseIgnoredProcessNames(), StringComparer.OrdinalIgnoreCase);

    private static double ResolveEffectiveScale(AppSettings settings)
    {
        double dpiScale = NativeMethods.GetDpiForSystem() / 96.0;
        if (dpiScale <= 0)
        {
            dpiScale = 1.0;
        }

        return settings.ResolveScaleFactor(dpiScale);
    }

    private static IReadOnlyList<Rectangle> GetMonitorBounds(IReadOnlyList<Screen> screens)
    {
        var rects = new Rectangle[screens.Count];
        for (int i = 0; i < screens.Count; i++)
        {
            rects[i] = screens[i].Bounds;
        }

        return rects;
    }

    private static Icon LoadEmbeddedIcon()
    {
        using Stream? stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("WinPenguins.Assets.winpenguins.ico");
        return stream != null ? new Icon(stream) : SystemIcons.Application;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            _windowTracker.ForegroundOrZOrderChanged -= OnForegroundOrZOrderChanged;
            _overlayHost.MouseDown -= OnOverlayMouseDown;
            _overlayHost.MouseMove -= OnOverlayMouseMove;
            _overlayHost.MouseUp -= OnOverlayMouseUp;
            _overlayHost.CaptureLost -= OnOverlayCaptureLost;
            _batteryCheckTimer.Stop();
            _batteryCheckTimer.Dispose();
            _hotkeyWindow.HotkeyPressed -= TogglePause;
            _hotkeyWindow.Dispose();
            _framePacer.Dispose();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayMenu.Dispose();
            _optionsBoldFont.Dispose();
            _overlayHost.Dispose();
            _windowTracker.Dispose();
            _soundService.Dispose();
            _appIcon.Dispose();
        }

        base.Dispose(disposing);
    }
}
