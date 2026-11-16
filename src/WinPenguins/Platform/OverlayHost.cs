using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectComposition;
using Vortice.DXGI;
using WinPenguins.Assets;
using WinPenguins.Config;
using WinPenguins.Engine;

namespace WinPenguins.Platform;

public sealed class OverlayHost : IDisposable
{
    private readonly ID3D11Device _d3dDevice;
    private readonly ID3D11DeviceContext _d3dContext;
    private readonly IDXGIFactory2 _dxgiFactory;
    private readonly ID2D1Factory1 _d2dFactory;
    private readonly ID2D1Device _d2dDevice;
    private readonly ID2D1DeviceContext _d2dContext;
    private readonly IDCompositionDevice _dcompDevice;
    private GpuSpriteAtlas _gpuAtlas;

    private readonly List<DCompOverlayWindow> _windows = [];
    private readonly HashSet<IntPtr> _ownHandles = [];
    private readonly List<Screen> _activeScreens = [];

    private OverlayLayerMode _layerMode;
    private bool _primaryOnly;
    private bool _visible = true;
    private bool _desktopZOrderDirty = true;
    private long _lastZOrderCheckTick;
    private Func<int, int, bool>? _pointHitTest;

    public IReadOnlyList<Screen> ActiveScreens => _activeScreens;

    public event Action<int, int>? MouseDown;
    public event Action<int, int>? MouseMove;
    public event Action<int, int>? MouseUp;
    public event Action? CaptureLost;

    public OverlayHost(AppSettings settings, SpriteCatalog spriteCatalog)
    {
        (_d3dDevice, _d3dContext) = CreateD3D11Device();
        using IDXGIDevice dxgiDevice = _d3dDevice.QueryInterface<IDXGIDevice>();
        using IDXGIAdapter dxgiAdapter = dxgiDevice.GetAdapter();
        _dxgiFactory = dxgiAdapter.GetParent<IDXGIFactory2>();

        _d2dFactory = D2D1.D2D1CreateFactory<ID2D1Factory1>(FactoryType.SingleThreaded);
        _d2dDevice = _d2dFactory.CreateDevice(dxgiDevice);
        _d2dContext = _d2dDevice.CreateDeviceContext(DeviceContextOptions.None);
        _d2dContext.UnitMode = UnitMode.Pixels;
        _d2dContext.SetDpi(96f, 96f);

        _dcompDevice = DComp.DCompositionCreateDevice<IDCompositionDevice>(dxgiDevice);
        _gpuAtlas = new GpuSpriteAtlas(_d2dContext, spriteCatalog);

        Rebuild(settings);
    }

    public void Rebuild(AppSettings settings)
    {
        DisposeWindows();

        _layerMode = settings.LayerMode;
        _primaryOnly = settings.PrimaryMonitorOnly;

        Screen[] allScreens = Screen.AllScreens;
        if (_primaryOnly && Screen.PrimaryScreen != null)
        {
            _activeScreens.Add(Screen.PrimaryScreen);
        }
        else if (allScreens.Length > 0)
        {
            _activeScreens.AddRange(allScreens);
        }

        bool topmost = _layerMode == OverlayLayerMode.AboveWindows;
        foreach (Screen screen in _activeScreens)
        {
            var win = new DCompOverlayWindow(
                screen.Bounds,
                topmost,
                _d3dDevice,
                _dxgiFactory,
                _d2dContext,
                _dcompDevice);
            win.SetOverlayVisible(_visible);
            win.SetPointHitTest(_pointHitTest);
            win.MouseDownOnOverlay += OnWindowMouseDown;
            win.MouseMoveOnOverlay += OnWindowMouseMove;
            win.MouseUpOnOverlay += OnWindowMouseUp;
            win.MouseCaptureLost += OnWindowCaptureLost;
            _windows.Add(win);
            _ownHandles.Add(win.Handle);
        }

        ApplyLayerMode(_layerMode);
    }

    public void MarkDesktopZOrderDirty() => _desktopZOrderDirty = true;

    public void ApplyLayerMode(OverlayLayerMode mode)
    {
        _layerMode = mode;
        _desktopZOrderDirty = true;
        bool topmost = mode == OverlayLayerMode.AboveWindows;
        IntPtr insertAfter = topmost ? IntPtr.Zero : ResolveDesktopInsertAfter();

        for (int i = 0; i < _windows.Count; i++)
        {
            _windows[i].SetLayerZOrder(topmost, insertAfter);
        }

        _lastZOrderCheckTick = Environment.TickCount64;
        _desktopZOrderDirty = false;
    }

    public void EnsureDesktopZOrderIfNeeded()
    {
        if (_layerMode != OverlayLayerMode.DesktopOnly || _windows.Count == 0)
        {
            return;
        }

        long now = Environment.TickCount64;
        if (!_desktopZOrderDirty && (now - _lastZOrderCheckTick) < 500)
        {
            return;
        }

        _desktopZOrderDirty = false;
        _lastZOrderCheckTick = now;

        IntPtr insertAfter = ResolveDesktopInsertAfter();
        for (int i = 0; i < _windows.Count; i++)
        {
            _windows[i].SetLayerZOrder(false, insertAfter);
        }
    }

    public void SetVisible(bool visible)
    {
        _visible = visible;
        for (int i = 0; i < _windows.Count; i++)
        {
            _windows[i].SetOverlayVisible(visible);
        }

        if (!visible)
        {
            for (int i = 0; i < _windows.Count; i++)
            {
                _windows[i].SetClickThrough(true);
            }
        }
    }

    public void ReloadCatalog(SpriteCatalog spriteCatalog)
    {
        var nextAtlas = new GpuSpriteAtlas(_d2dContext, spriteCatalog);
        GpuSpriteAtlas previous = _gpuAtlas;
        _gpuAtlas = nextAtlas;
        previous.Dispose();
    }

    public void SetPointHitTest(Func<int, int, bool>? hitTest)
    {
        _pointHitTest = hitTest;
        for (int i = 0; i < _windows.Count; i++)
        {
            _windows[i].SetPointHitTest(hitTest);
        }
    }

    public void UpdateInteractiveHover(bool enabled, bool isDragging, Func<int, int, bool> hitTest)
    {
        if (!_visible || !enabled)
        {
            for (int i = 0; i < _windows.Count; i++)
            {
                _windows[i].SetClickThrough(true);
            }

            return;
        }

        if (isDragging)
        {
            return;
        }

        if (!NativeMethods.GetCursorPos(out NativeMethods.POINT pt))
        {
            for (int i = 0; i < _windows.Count; i++)
            {
                _windows[i].SetClickThrough(true);
            }

            return;
        }

        bool hit = hitTest(pt.X, pt.Y);
        for (int i = 0; i < _windows.Count; i++)
        {
            DCompOverlayWindow win = _windows[i];
            bool shouldIntercept = hit && win.Bounds.Contains(pt.X, pt.Y);
            win.SetClickThrough(!shouldIntercept);
        }
    }

    public IReadOnlyList<double> GetMonitorDpiScales(AppSettings settings)
    {
        if (_activeScreens.Count == 0)
        {
            return Array.Empty<double>();
        }

        var scales = new double[_activeScreens.Count];
        for (int i = 0; i < _activeScreens.Count; i++)
        {
            Rectangle b = _activeScreens[i].Bounds;
            var pt = new NativeMethods.POINT
            {
                X = b.Left + b.Width / 2,
                Y = b.Top + b.Height / 2
            };

            double dpiScale = 1.0;
            IntPtr hMonitor = NativeMethods.MonitorFromPoint(pt, NativeMethods.MONITOR_DEFAULTTONEAREST);
            if (hMonitor != IntPtr.Zero &&
                NativeMethods.GetDpiForMonitor(hMonitor, NativeMethods.MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0 &&
                dpiX > 0)
            {
                dpiScale = dpiX / 96.0;
            }

            scales[i] = settings.ResolveScaleFactor(dpiScale);
        }

        return scales;
    }

    private void OnWindowMouseDown(int x, int y) => MouseDown?.Invoke(x, y);
    private void OnWindowMouseMove(int x, int y) => MouseMove?.Invoke(x, y);
    private void OnWindowMouseUp(int x, int y) => MouseUp?.Invoke(x, y);
    private void OnWindowCaptureLost() => CaptureLost?.Invoke();

    public void RenderAll(IReadOnlyList<Penguin> penguins, byte opacity, bool forceRedraw = false)
    {
        if (!_visible)
        {
            return;
        }

        for (int i = 0; i < _windows.Count; i++)
        {
            _windows[i].RenderFrame(penguins, _gpuAtlas, opacity, forceRedraw);
        }
    }

    public bool MatchesMonitorConfig(AppSettings settings)
    {
        if (settings.PrimaryMonitorOnly != _primaryOnly)
        {
            return false;
        }

        Screen[] expected = settings.PrimaryMonitorOnly && Screen.PrimaryScreen != null
            ? [Screen.PrimaryScreen]
            : Screen.AllScreens;

        if (expected.Length != _activeScreens.Count)
        {
            return false;
        }

        for (int i = 0; i < expected.Length; i++)
        {
            if (expected[i].Bounds != _activeScreens[i].Bounds)
            {
                return false;
            }
        }

        return true;
    }

    private IntPtr ResolveDesktopInsertAfter()
    {
        IntPtr desktopHost = WindowTracker.FindDesktopHostWindow();
        if (desktopHost == IntPtr.Zero)
        {
            return NativeMethods.HWND_NOTOPMOST;
        }

        IntPtr prev = NativeMethods.GetWindow(desktopHost, NativeMethods.GW_HWNDPREV);
        while (prev != IntPtr.Zero && _ownHandles.Contains(prev))
        {
            prev = NativeMethods.GetWindow(prev, NativeMethods.GW_HWNDPREV);
        }

        if (prev != IntPtr.Zero &&
            (NativeMethods.GetWindowLongPtr(prev, NativeMethods.GWL_EXSTYLE) & NativeMethods.WS_EX_TOPMOST) != 0)
        {
            return NativeMethods.HWND_TOP;
        }

        return prev != IntPtr.Zero ? prev : NativeMethods.HWND_TOP;
    }

    private static (ID3D11Device Device, ID3D11DeviceContext Context) CreateD3D11Device()
    {
        Vortice.Direct3D.FeatureLevel[] levels =
        [
            Vortice.Direct3D.FeatureLevel.Level_11_1,
            Vortice.Direct3D.FeatureLevel.Level_11_0,
            Vortice.Direct3D.FeatureLevel.Level_10_1,
            Vortice.Direct3D.FeatureLevel.Level_10_0
        ];

        try
        {
            ID3D11Device hwDevice = D3D11.D3D11CreateDevice(
                DriverType.Hardware,
                DeviceCreationFlags.BgraSupport,
                levels);
            return (hwDevice, hwDevice.ImmediateContext);
        }
        catch
        {
            // Fall back to WARP if hardware adapter is unavailable.
        }

        ID3D11Device warpDevice = D3D11.D3D11CreateDevice(
            DriverType.Warp,
            DeviceCreationFlags.BgraSupport,
            levels);
        return (warpDevice, warpDevice.ImmediateContext);
    }

    private void DisposeWindows()
    {
        for (int i = 0; i < _windows.Count; i++)
        {
            DCompOverlayWindow win = _windows[i];
            win.MouseDownOnOverlay -= OnWindowMouseDown;
            win.MouseMoveOnOverlay -= OnWindowMouseMove;
            win.MouseUpOnOverlay -= OnWindowMouseUp;
            win.MouseCaptureLost -= OnWindowCaptureLost;
            win.Dispose();
        }

        _windows.Clear();
        _ownHandles.Clear();
        _activeScreens.Clear();
    }

    public void Dispose()
    {
        DisposeWindows();
        _gpuAtlas.Dispose();
        _dcompDevice.Dispose();
        _d2dContext.Dispose();
        _d2dDevice.Dispose();
        _d2dFactory.Dispose();
        _dxgiFactory.Dispose();
        _d3dContext.Dispose();
        _d3dDevice.Dispose();
    }
}
