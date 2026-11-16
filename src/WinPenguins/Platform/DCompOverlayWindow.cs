using System.Drawing;
using Vortice;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.Direct3D11;
using Vortice.DirectComposition;
using Vortice.DXGI;
using Vortice.Mathematics;
using WinPenguins.Engine;

namespace WinPenguins.Platform;

public sealed class DCompOverlayWindow : NativeWindow, IDisposable
{
    private readonly Rectangle _bounds;
    private readonly ID2D1DeviceContext _d2dContext;
    private readonly IDXGISwapChain1 _swapChain;
    private readonly ID2D1Bitmap1 _targetBitmap;
    private readonly IDCompositionTarget _dcompTarget;
    private readonly IDCompositionVisual _dcompVisual;

    private readonly List<DrawCommand> _lastCommands = [];
    private readonly List<DrawCommand> _currentCommands = [];

    private bool _visible;
    private bool _topmost;
    private bool _hadContent = true;
    private byte _lastOpacity = 255;
    private bool _clickThrough = true;
    private bool _capturing;
    private Func<int, int, bool>? _pointHitTest;

    public Rectangle Bounds => _bounds;

    public event Action<int, int>? MouseDownOnOverlay;
    public event Action<int, int>? MouseMoveOnOverlay;
    public event Action<int, int>? MouseUpOnOverlay;
    public event Action? MouseCaptureLost;

    public DCompOverlayWindow(
        Rectangle bounds,
        bool topmost,
        ID3D11Device d3dDevice,
        IDXGIFactory2 dxgiFactory,
        ID2D1DeviceContext d2dContext,
        IDCompositionDevice dcompDevice)
    {
        _bounds = bounds;
        _topmost = topmost;
        _d2dContext = d2dContext;

        int exStyle = NativeMethods.WS_EX_NOREDIRECTIONBITMAP
            | NativeMethods.WS_EX_LAYERED
            | NativeMethods.WS_EX_TRANSPARENT
            | NativeMethods.WS_EX_TOOLWINDOW
            | NativeMethods.WS_EX_NOACTIVATE;
        if (topmost)
        {
            exStyle |= NativeMethods.WS_EX_TOPMOST;
        }

        CreateHandle(new CreateParams
        {
            Caption = "WinPenguinsOverlay",
            Style = NativeMethods.WS_POPUP,
            ExStyle = exStyle,
            X = bounds.X,
            Y = bounds.Y,
            Width = bounds.Width,
            Height = bounds.Height
        });

        // Initialize WS_EX_LAYERED state so kernel hit-testing passes clicks through
        // across processes while DirectComposition renders the premultiplied swapchain.
        NativeMethods.SetLayeredWindowAttributes(Handle, 0, 255, NativeMethods.LWA_ALPHA);

        // Enforce exact physical per-monitor pixel placement even across mixed-DPI displays.
        NativeMethods.SetWindowPos(
            Handle,
            topmost ? NativeMethods.HWND_TOPMOST : NativeMethods.HWND_NOTOPMOST,
            bounds.X,
            bounds.Y,
            bounds.Width,
            bounds.Height,
            NativeMethods.SWP_NOACTIVATE);

        var swapDesc = new SwapChainDescription1
        {
            Width = (uint)Math.Max(1, bounds.Width),
            Height = (uint)Math.Max(1, bounds.Height),
            Format = Format.B8G8R8A8_UNorm,
            Stereo = false,
            SampleDescription = new SampleDescription(1, 0),
            BufferUsage = Usage.RenderTargetOutput,
            BufferCount = 2,
            Scaling = Scaling.Stretch,
            SwapEffect = SwapEffect.FlipSequential,
            AlphaMode = Vortice.DXGI.AlphaMode.Premultiplied
        };

        _swapChain = dxgiFactory.CreateSwapChainForComposition(d3dDevice, swapDesc);
        using IDXGISurface backBuffer = _swapChain.GetBuffer<IDXGISurface>(0);
        var targetProps = new BitmapProperties1(
            new PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
            96f,
            96f,
            BitmapOptions.Target | BitmapOptions.CannotDraw);
        _targetBitmap = _d2dContext.CreateBitmapFromDxgiSurface(backBuffer, targetProps);

        dcompDevice.CreateTargetForHwnd(Handle, true, out _dcompTarget);
        _dcompVisual = dcompDevice.CreateVisual();
        _dcompVisual.SetContent(_swapChain);
        _dcompTarget.SetRoot(_dcompVisual);
        dcompDevice.Commit();

        // Clear initial surface once before showing.
        ClearAndPresent();
        SetOverlayVisible(true);
    }

    public void SetOverlayVisible(bool visible)
    {
        if (Handle == IntPtr.Zero || _visible == visible)
        {
            return;
        }

        _visible = visible;
        NativeMethods.ShowWindow(
            Handle,
            visible ? NativeMethods.SW_SHOWNOACTIVATE : NativeMethods.SW_HIDE);
    }

    public void SetPointHitTest(Func<int, int, bool>? pointHitTest) => _pointHitTest = pointHitTest;

    public void SetClickThrough(bool clickThrough)
    {
        if (Handle == IntPtr.Zero || _clickThrough == clickThrough)
        {
            return;
        }

        _clickThrough = clickThrough;
        nint exStyle = NativeMethods.GetWindowLongPtr(Handle, NativeMethods.GWL_EXSTYLE);
        nint updated = clickThrough
            ? exStyle | NativeMethods.WS_EX_TRANSPARENT
            : exStyle & ~NativeMethods.WS_EX_TRANSPARENT;

        if (updated != exStyle)
        {
            NativeMethods.SetWindowLongPtr(Handle, NativeMethods.GWL_EXSTYLE, updated);
        }
    }

    public void SetLayerZOrder(bool topmost, IntPtr insertAfterWhenNotTopmost)
    {
        if (Handle == IntPtr.Zero)
        {
            return;
        }

        if (topmost != _topmost)
        {
            _topmost = topmost;
            NativeMethods.SetWindowPos(
                Handle,
                topmost ? NativeMethods.HWND_TOPMOST : NativeMethods.HWND_NOTOPMOST,
                0,
                0,
                0,
                0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        }

        if (!topmost)
        {
            NativeMethods.SetWindowPos(
                Handle,
                insertAfterWhenNotTopmost,
                0,
                0,
                0,
                0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        }
    }

    public void RenderFrame(
        IReadOnlyList<Penguin> penguins,
        GpuSpriteAtlas atlas,
        byte opacity,
        bool forceRedraw = false)
    {
        if (Handle == IntPtr.Zero)
        {
            return;
        }

        _currentCommands.Clear();
        for (int i = 0; i < penguins.Count; i++)
        {
            Penguin p = penguins[i];
            if (!p.Active)
            {
                continue;
            }

            Rectangle worldRect = p.Bounds;
            if (!worldRect.IntersectsWith(_bounds))
            {
                continue;
            }

            _currentCommands.Add(new DrawCommand(
                p.Animation,
                p.Direction,
                p.FrameIndex,
                (float)(p.VisualX - _bounds.X),
                (float)(p.VisualY - _bounds.Y),
                worldRect.Width,
                worldRect.Height));
        }

        if (_currentCommands.Count == 0 && !_hadContent && !forceRedraw)
        {
            return;
        }

        if (!forceRedraw && opacity == _lastOpacity && CommandsMatch(_lastCommands, _currentCommands))
        {
            return;
        }

        _d2dContext.Target = _targetBitmap;
        _d2dContext.BeginDraw();
        _d2dContext.Clear(new Color4(0f, 0f, 0f, 0f));

        float alpha = opacity / 255f;
        for (int i = 0; i < _currentCommands.Count; i++)
        {
            DrawCommand cmd = _currentCommands[i];
            AnimationSpec spec = atlas.GetSpec(cmd.Animation);
            RawRectF srcRect = atlas.GetFrameSourceRect(cmd.Animation, (int)cmd.Direction, cmd.FrameIndex);
            var dstRect = new RawRectF(cmd.LocalX, cmd.LocalY, cmd.LocalX + cmd.Width, cmd.LocalY + cmd.Height);
            bool exactIntegerScale = cmd.Width >= spec.BaseWidth
                && cmd.Height >= spec.BaseHeight
                && cmd.Width % spec.BaseWidth == 0
                && cmd.Height % spec.BaseHeight == 0;
            bool pixelArt = spec.BaseWidth <= 48 && spec.BaseHeight <= 48;
            var interpolation = (pixelArt || exactIntegerScale)
                ? Vortice.Direct2D1.BitmapInterpolationMode.NearestNeighbor
                : Vortice.Direct2D1.BitmapInterpolationMode.Linear;

            _d2dContext.DrawBitmap(
                atlas.GetSheetBitmap(cmd.Animation),
                dstRect,
                alpha,
                interpolation,
                srcRect);
        }

        _d2dContext.EndDraw();
        _d2dContext.Target = null;
        _swapChain.Present(0, PresentFlags.None);

        _hadContent = _currentCommands.Count > 0;
        _lastOpacity = opacity;
        _lastCommands.Clear();
        _lastCommands.AddRange(_currentCommands);
    }

    private void ClearAndPresent()
    {
        _d2dContext.Target = _targetBitmap;
        _d2dContext.BeginDraw();
        _d2dContext.Clear(new Color4(0f, 0f, 0f, 0f));
        _d2dContext.EndDraw();
        _d2dContext.Target = null;
        _swapChain.Present(0, PresentFlags.None);
        _hadContent = false;
    }

    private static bool CommandsMatch(List<DrawCommand> a, List<DrawCommand> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (int i = 0; i < a.Count; i++)
        {
            if (a[i] != b[i])
            {
                return false;
            }
        }

        return true;
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case NativeMethods.WM_NCHITTEST:
            {
                if (_capturing)
                {
                    m.Result = (IntPtr)NativeMethods.HTCLIENT;
                    return;
                }

                if (_clickThrough)
                {
                    m.Result = (IntPtr)NativeMethods.HTTRANSPARENT;
                    return;
                }

                if (_pointHitTest is not null)
                {
                    long lp = (long)m.LParam;
                    int sx = unchecked((short)(lp & 0xFFFF));
                    int sy = unchecked((short)((lp >> 16) & 0xFFFF));
                    if (!_pointHitTest(sx, sy))
                    {
                        m.Result = (IntPtr)NativeMethods.HTTRANSPARENT;
                        return;
                    }
                }

                m.Result = (IntPtr)NativeMethods.HTCLIENT;
                return;
            }

            case NativeMethods.WM_MOUSEACTIVATE:
                m.Result = (IntPtr)NativeMethods.MA_NOACTIVATE;
                return;

            case NativeMethods.WM_SETCURSOR:
                if ((_capturing || !_clickThrough) &&
                    unchecked((short)((long)m.LParam & 0xFFFF)) == NativeMethods.HTCLIENT)
                {
                    IntPtr hand = NativeMethods.LoadCursor(IntPtr.Zero, NativeMethods.IDC_HAND);
                    if (hand != IntPtr.Zero)
                    {
                        NativeMethods.SetCursor(hand);
                        m.Result = (IntPtr)1;
                        return;
                    }
                }
                break;

            case NativeMethods.WM_LBUTTONDOWN:
                if (!_clickThrough)
                {
                    NativeMethods.GetCursorPos(out NativeMethods.POINT pt);
                    _capturing = true;
                    NativeMethods.SetCapture(Handle);
                    MouseDownOnOverlay?.Invoke(pt.X, pt.Y);
                    m.Result = IntPtr.Zero;
                    return;
                }
                break;

            case NativeMethods.WM_MOUSEMOVE:
                if (_capturing)
                {
                    NativeMethods.GetCursorPos(out NativeMethods.POINT pt);
                    MouseMoveOnOverlay?.Invoke(pt.X, pt.Y);
                    m.Result = IntPtr.Zero;
                    return;
                }
                break;

            case NativeMethods.WM_LBUTTONUP:
                if (_capturing)
                {
                    _capturing = false;
                    NativeMethods.ReleaseCapture();
                    NativeMethods.GetCursorPos(out NativeMethods.POINT pt);
                    MouseUpOnOverlay?.Invoke(pt.X, pt.Y);
                    m.Result = IntPtr.Zero;
                    return;
                }
                break;

            case NativeMethods.WM_CAPTURECHANGED:
                if (_capturing)
                {
                    _capturing = false;
                    MouseCaptureLost?.Invoke();
                }
                break;

            case NativeMethods.WM_DPICHANGED:
                // Overlay windows already use physical per-monitor pixel bounds from Screen.AllScreens.
                m.Result = IntPtr.Zero;
                return;
        }

        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (_capturing)
        {
            _capturing = false;
            NativeMethods.ReleaseCapture();
        }

        if (Handle != IntPtr.Zero)
        {
            DestroyHandle();
        }

        _dcompTarget.Dispose();
        _dcompVisual.Dispose();
        _targetBitmap.Dispose();
        _swapChain.Dispose();
    }

    private readonly record struct DrawCommand(
        PenguinAnimationId Animation,
        FacingDirection Direction,
        int FrameIndex,
        float LocalX,
        float LocalY,
        int Width,
        int Height);
}
