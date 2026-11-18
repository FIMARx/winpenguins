using System.Drawing;
using System.Numerics;
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
    public event Action<int, int>? RightClickOnOverlay;
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
            // Triple-buffer flip composition so 120-240 Hz presents never contend with DWM reading layered menus/dialogs.
            BufferCount = 3,
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

    public static bool IsDeviceLostHResult(int hresult) => hresult is
        unchecked((int)0x887A0005) or // DXGI_ERROR_DEVICE_REMOVED
        unchecked((int)0x887A0006) or // DXGI_ERROR_DEVICE_HUNG
        unchecked((int)0x887A0007) or // DXGI_ERROR_DEVICE_RESET
        unchecked((int)0x8899000C);   // D2DERR_RECREATE_TARGET

    public bool RenderFrame(
        IReadOnlyList<Penguin> penguins,
        GpuSpriteAtlas atlas,
        byte opacity,
        bool forceRedraw = false,
        IReadOnlyList<DesktopToy>? toys = null,
        SnowSystem? snow = null,
        ID2D1SolidColorBrush? sharedBrush = null)
    {
        if (Handle == IntPtr.Zero)
        {
            return true;
        }

        PopulateCommands(penguins, _bounds, toys, snow, _currentCommands, out bool hasToysInMonitor, out bool hasSnowInMonitor);

        if (_currentCommands.Count == 0 && !hasToysInMonitor && !hasSnowInMonitor && !_hadContent && !forceRedraw)
        {
            return true;
        }

        if (!forceRedraw && !hasToysInMonitor && !hasSnowInMonitor && opacity == _lastOpacity && CommandsMatch(_lastCommands, _currentCommands))
        {
            return true;
        }

        try
        {
            _d2dContext.Target = _targetBitmap;
            _d2dContext.BeginDraw();
            RenderSceneCommands(
                _d2dContext,
                _bounds,
                _currentCommands,
                hasToysInMonitor,
                hasSnowInMonitor,
                atlas,
                opacity,
                toys,
                snow,
                sharedBrush);
            _d2dContext.EndDraw();
            _swapChain.Present(0, PresentFlags.None);
        }
        catch (SharpGen.Runtime.SharpGenException ex) when (IsDeviceLostHResult(ex.HResult))
        {
            return false;
        }
        finally
        {
            _d2dContext.Transform = Matrix3x2.Identity;
            _d2dContext.Target = null;
        }

        _hadContent = _currentCommands.Count > 0 || hasToysInMonitor || hasSnowInMonitor;
        _lastOpacity = opacity;
        _lastCommands.Clear();
        _lastCommands.AddRange(_currentCommands);
        return true;
    }

    internal static void PopulateCommands(
        IReadOnlyList<Penguin> penguins,
        Rectangle bounds,
        IReadOnlyList<DesktopToy>? toys,
        SnowSystem? snow,
        List<DrawCommand> commands,
        out bool hasToysInMonitor,
        out bool hasSnowInMonitor)
    {
        commands.Clear();
        for (int i = 0; i < penguins.Count; i++)
        {
            Penguin p = penguins[i];
            if (!p.Active)
            {
                continue;
            }

            Rectangle worldRect = p.Bounds;
            Rectangle inflated = Rectangle.Inflate(worldRect, 10, Math.Max(12, p.ShadowDistance <= 120 ? p.ShadowDistance + 10 : 12));
            if (!inflated.IntersectsWith(bounds))
            {
                continue;
            }

            byte emoteAlpha = p.Emote != PenguinEmote.None
                ? (byte)Math.Clamp((int)Math.Round(Math.Min(1.0, p.EmoteSecondsRemaining / 0.4) * 255.0), 0, 255)
                : (byte)0;

            bool jetActive = p.SubType == PenguinSubType.Jetpack &&
                (p.JetBoostSeconds > 0 || p.Animation is PenguinAnimationId.Climber or PenguinAnimationId.SantaClimber);

            commands.Add(new DrawCommand(
                p.Animation,
                p.Direction,
                p.FrameIndex,
                (float)(p.VisualX - bounds.X),
                (float)(p.VisualY - bounds.Y),
                worldRect.Width,
                worldRect.Height,
                (short)Math.Clamp(Math.Round(p.VisualRotationDeg * 10f), -3600, 3600),
                (short)Math.Clamp(Math.Round(p.VisualScaleX * 100f), 50, 200),
                (short)Math.Clamp(Math.Round(p.VisualScaleY * 100f), 50, 200),
                p.ShadowDistance <= 120 ? p.ShadowDistance : 999,
                p.IsSliding,
                p.Emote,
                emoteAlpha,
                p.SubType,
                jetActive));
        }

        hasToysInMonitor = false;
        if (toys != null)
        {
            for (int i = 0; i < toys.Count; i++)
            {
                if (toys[i].Active && toys[i].Bounds.IntersectsWith(bounds))
                {
                    hasToysInMonitor = true;
                    break;
                }
            }
        }

        hasSnowInMonitor = false;
        if (snow != null)
        {
            ReadOnlySpan<SnowParticle> flakes = snow.Particles;
            for (int i = 0; i < flakes.Length; i++)
            {
                if (flakes[i].Active && bounds.Contains((int)flakes[i].X, (int)flakes[i].Y))
                {
                    hasSnowInMonitor = true;
                    break;
                }
            }
        }
    }

    internal static void RenderSceneCommands(
        ID2D1DeviceContext d2dContext,
        Rectangle bounds,
        IReadOnlyList<DrawCommand> commands,
        bool hasToysInMonitor,
        bool hasSnowInMonitor,
        GpuSpriteAtlas atlas,
        byte opacity,
        IReadOnlyList<DesktopToy>? toys,
        SnowSystem? snow,
        ID2D1SolidColorBrush? sharedBrush)
    {
        d2dContext.Transform = Matrix3x2.Identity;
        d2dContext.Clear(new Color4(0f, 0f, 0f, 0f));

        float alpha = opacity / 255f;

        if (sharedBrush != null && hasToysInMonitor && toys != null)
        {
            RenderToys(d2dContext, bounds, toys, alpha, sharedBrush);
        }

        for (int i = 0; i < commands.Count; i++)
        {
            DrawCommand cmd = commands[i];
            if (sharedBrush != null && cmd.ShadowDist <= 120)
            {
                float factor = Math.Clamp(1f - cmd.ShadowDist / 130f, 0f, 1f);
                float shadowAlpha = 0.28f * factor * alpha;
                float rx = cmd.Width * (0.34f - 0.14f * (cmd.ShadowDist / 120f));
                float ry = Math.Max(2f, rx * 0.24f);
                float floorY = cmd.LocalY + cmd.Height + cmd.ShadowDist;
                sharedBrush.Color = new Color4(0f, 0f, 0f, shadowAlpha);
                d2dContext.FillEllipse(new Ellipse(new Vector2(cmd.LocalX + cmd.Width * 0.5f, floorY), rx, ry), sharedBrush);
            }

            AnimationSpec spec = atlas.GetSpec(cmd.Animation);
            (int texW, int texH) = atlas.GetTextureFrameSize(cmd.Animation);
            RawRectF srcRect = atlas.GetFrameSourceRect(cmd.Animation, (int)cmd.Direction, cmd.FrameIndex);
            var dstRect = new RawRectF(cmd.LocalX, cmd.LocalY, cmd.LocalX + cmd.Width, cmd.LocalY + cmd.Height);
            bool exactIntegerScale = cmd.Width >= texW
                && cmd.Height >= texH
                && cmd.Width % texW == 0
                && cmd.Height % texH == 0;
            bool classicPixelArt = texW <= 48 && texH <= 48 && spec.BaseWidth <= 48;
            var interpolation = (classicPixelArt || exactIntegerScale)
                ? Vortice.Direct2D1.BitmapInterpolationMode.NearestNeighbor
                : Vortice.Direct2D1.BitmapInterpolationMode.Linear;

            float rotDeg = cmd.RotTenths * 0.1f;
            float sx = cmd.ScaleXPct * 0.01f;
            float sy = cmd.ScaleYPct * 0.01f;
            bool hasTransform = Math.Abs(rotDeg) > 0.05f || Math.Abs(sx - 1f) > 0.005f || Math.Abs(sy - 1f) > 0.005f;
            if (hasTransform)
            {
                bool spinCentered = !cmd.IsSliding && Math.Abs(rotDeg) > 18f;
                Vector2 anchor = spinCentered
                    ? new Vector2(cmd.LocalX + cmd.Width * 0.5f, cmd.LocalY + cmd.Height * 0.5f)
                    : new Vector2(cmd.LocalX + cmd.Width * 0.5f, cmd.LocalY + cmd.Height);
                d2dContext.Transform = Matrix3x2.CreateScale(sx, sy, anchor) * Matrix3x2.CreateRotation(rotDeg * (MathF.PI / 180f), anchor);
            }

            d2dContext.DrawBitmap(
                atlas.GetSheetBitmap(cmd.Animation),
                dstRect,
                alpha,
                interpolation,
                srcRect);

            if (sharedBrush != null && cmd.SubType is not (PenguinSubType.Unspecified or PenguinSubType.Normal or PenguinSubType.Santa))
            {
                RenderSubTypeAccessory(d2dContext, cmd, alpha, sharedBrush);
            }

            if (hasTransform)
            {
                d2dContext.Transform = Matrix3x2.Identity;
            }

            if (sharedBrush != null && cmd.Emote != PenguinEmote.None && cmd.EmoteAlpha > 4)
            {
                RenderEmote(d2dContext, cmd, alpha * (cmd.EmoteAlpha / 255f), sharedBrush);
            }
        }

        if (sharedBrush != null && hasSnowInMonitor && snow != null)
        {
            RenderSnow(d2dContext, bounds, snow.Particles, alpha, sharedBrush);
        }
    }

    private static void RenderEmote(ID2D1DeviceContext d2dContext, DrawCommand cmd, float alpha, ID2D1SolidColorBrush brush)
    {
        bool isClimbing = cmd.Animation is PenguinAnimationId.Climber or PenguinAnimationId.SantaClimber;
        float cx = isClimbing
            ? cmd.LocalX + cmd.Width * (cmd.Direction == FacingDirection.Right ? 0.25f : 0.75f)
            : cmd.LocalX + cmd.Width * 0.5f;
        float topY = Math.Max(6f, cmd.LocalY - 9f - (1f - cmd.EmoteAlpha / 255f) * 5f);

        switch (cmd.Emote)
        {
            case PenguinEmote.Heart:
                brush.Color = new Color4(1.0f, 0.34f, 0.52f, alpha);
                d2dContext.FillEllipse(new Ellipse(new Vector2(cx - 2.3f, topY), 2.6f, 2.6f), brush);
                d2dContext.FillEllipse(new Ellipse(new Vector2(cx + 2.3f, topY), 2.6f, 2.6f), brush);
                d2dContext.Transform = Matrix3x2.CreateRotation(MathF.PI * 0.25f, new Vector2(cx, topY + 1.8f));
                d2dContext.FillRectangle(new RawRectF(cx - 2.7f, topY - 0.9f, cx + 2.7f, topY + 4.5f), brush);
                d2dContext.Transform = Matrix3x2.Identity;
                break;

            case PenguinEmote.Music:
                brush.Color = new Color4(0.32f, 0.85f, 1.0f, alpha);
                d2dContext.FillEllipse(new Ellipse(new Vector2(cx - 2.0f, topY + 3.5f), 2.8f, 2.1f), brush);
                d2dContext.DrawLine(new Vector2(cx + 0.4f, topY + 3.5f), new Vector2(cx + 0.4f, topY - 3.5f), brush, 1.6f);
                d2dContext.DrawLine(new Vector2(cx + 0.4f, topY - 3.5f), new Vector2(cx + 4.5f, topY - 1.8f), brush, 1.8f);
                break;

            case PenguinEmote.Alert:
                brush.Color = new Color4(1.0f, 0.78f, 0.18f, alpha);
                d2dContext.FillRoundedRectangle(new RoundedRectangle
                {
                    Rect = new RawRectF(cx - 1.3f, topY - 4.0f, cx + 1.3f, topY + 2.2f),
                    RadiusX = 1.2f,
                    RadiusY = 1.2f
                }, brush);
                d2dContext.FillEllipse(new Ellipse(new Vector2(cx, topY + 4.8f), 1.4f, 1.4f), brush);
                break;

            case PenguinEmote.Sleep:
                brush.Color = new Color4(0.72f, 0.78f, 1.0f, alpha);
                DrawTinyZ(d2dContext, cx - 2.5f, topY + 1.5f, 4.5f, brush);
                DrawTinyZ(d2dContext, cx + 3.0f, topY - 4.0f, 5.5f, brush);
                break;
        }
    }

    private static void DrawTinyZ(ID2D1DeviceContext d2dContext, float x, float y, float size, ID2D1SolidColorBrush brush)
    {
        d2dContext.DrawLine(new Vector2(x, y), new Vector2(x + size, y), brush, 1.4f);
        d2dContext.DrawLine(new Vector2(x + size, y), new Vector2(x, y + size), brush, 1.4f);
        d2dContext.DrawLine(new Vector2(x, y + size), new Vector2(x + size, y + size), brush, 1.4f);
    }

    private static void RenderSubTypeAccessory(ID2D1DeviceContext d2dContext, DrawCommand cmd, float alpha, ID2D1SolidColorBrush brush)
    {
        if (cmd.Animation is PenguinAnimationId.Explosion or PenguinAnimationId.Bomber)
        {
            return;
        }

        float lx = cmd.LocalX;
        float ly = cmd.LocalY;
        float w = cmd.Width;
        float h = cmd.Height;
        float cx = lx + w * 0.5f;
        float cy = ly + h * 0.5f;
        bool isClimbing = cmd.Animation is PenguinAnimationId.Climber or PenguinAnimationId.SantaClimber;
        float dirSign = cmd.Direction == FacingDirection.Right ? 1f : -1f;
        bool facingForward = isClimbing || cmd.Animation is PenguinAnimationId.Walker or PenguinAnimationId.SantaWalker;
        bool hideHat = cmd.Animation == PenguinAnimationId.Floater;

        Matrix3x2 savedTransform = d2dContext.Transform;
        bool hasClimbTransform = TryCreateClimbAccessoryTransform(
            cmd.Animation,
            cmd.Direction,
            cx,
            cy,
            h,
            out Matrix3x2 climbTransform);
        if (hasClimbTransform)
        {
            d2dContext.Transform = climbTransform * savedTransform;
        }

        try
        {
            switch (cmd.SubType)
            {
                case PenguinSubType.King when !hideHat:
                    brush.Color = new Color4(1.0f, 0.80f, 0.14f, alpha);
                    d2dContext.FillRoundedRectangle(new RoundedRectangle
                    {
                        Rect = new RawRectF(cx - w * 0.18f, ly + h * 0.10f, cx + w * 0.18f, ly + h * 0.20f),
                        RadiusX = 1.2f,
                        RadiusY = 1.2f
                    }, brush);
                    d2dContext.FillRectangle(new RawRectF(cx - w * 0.16f, ly + h * 0.02f, cx - w * 0.09f, ly + h * 0.12f), brush);
                    d2dContext.FillRectangle(new RawRectF(cx - w * 0.04f, ly + h * 0.00f, cx + w * 0.04f, ly + h * 0.12f), brush);
                    d2dContext.FillRectangle(new RawRectF(cx + w * 0.09f, ly + h * 0.02f, cx + w * 0.16f, ly + h * 0.12f), brush);
                    brush.Color = new Color4(0.96f, 0.22f, 0.34f, alpha);
                    d2dContext.FillEllipse(new Ellipse(new Vector2(cx, ly + h * 0.15f), Math.Max(1.3f, w * 0.036f), Math.Max(1.3f, h * 0.036f)), brush);
                    break;

                case PenguinSubType.Chef when !hideHat:
                    brush.Color = new Color4(0.84f, 0.88f, 0.94f, alpha);
                    d2dContext.FillRoundedRectangle(new RoundedRectangle
                    {
                        Rect = new RawRectF(cx - w * 0.17f, ly + h * 0.11f, cx + w * 0.17f, ly + h * 0.19f),
                        RadiusX = 1.2f,
                        RadiusY = 1.2f
                    }, brush);
                    brush.Color = new Color4(0.99f, 0.99f, 1.0f, alpha);
                    d2dContext.FillEllipse(new Ellipse(new Vector2(cx - w * 0.11f, ly + h * 0.07f), w * 0.10f, h * 0.09f), brush);
                    d2dContext.FillEllipse(new Ellipse(new Vector2(cx + w * 0.11f, ly + h * 0.07f), w * 0.10f, h * 0.09f), brush);
                    d2dContext.FillEllipse(new Ellipse(new Vector2(cx, ly + h * 0.03f), w * 0.12f, h * 0.10f), brush);
                    break;

                case PenguinSubType.DJ:
                    float eyeShift = facingForward ? dirSign * w * 0.08f : 0f;
                    brush.Color = new Color4(0.06f, 0.08f, 0.12f, 0.95f * alpha);
                    d2dContext.FillRoundedRectangle(new RoundedRectangle
                    {
                        Rect = new RawRectF(cx + eyeShift - w * 0.15f, ly + h * 0.27f, cx + eyeShift + w * 0.15f, ly + h * 0.35f),
                        RadiusX = 1.5f,
                        RadiusY = 1.5f
                    }, brush);
                    if (!hideHat)
                    {
                        float earX = cx - dirSign * w * 0.13f;
                        brush.Color = new Color4(0.18f, 0.86f, 1.0f, alpha);
                        d2dContext.DrawLine(new Vector2(cx - w * 0.15f, ly + h * 0.18f), new Vector2(cx + w * 0.15f, ly + h * 0.18f), brush, Math.Max(1.6f, w * 0.055f));
                        d2dContext.FillEllipse(new Ellipse(new Vector2(earX, ly + h * 0.30f), w * 0.08f, h * 0.09f), brush);
                        brush.Color = new Color4(1.0f, 0.28f, 0.72f, alpha);
                        d2dContext.FillEllipse(new Ellipse(new Vector2(earX, ly + h * 0.30f), w * 0.04f, h * 0.045f), brush);
                    }
                    break;

                case PenguinSubType.Jetpack:
                    float packX = cmd.Direction == FacingDirection.Right ? lx + w * 0.16f : lx + w * 0.68f;
                    float packW = w * 0.18f;
                    float packTop = ly + h * 0.44f;
                    float packBottom = ly + h * 0.72f;
                    brush.Color = new Color4(0.45f, 0.52f, 0.64f, alpha);
                    d2dContext.FillRoundedRectangle(new RoundedRectangle
                    {
                        Rect = new RawRectF(packX, packTop, packX + packW, packBottom),
                        RadiusX = 2f,
                        RadiusY = 2f
                    }, brush);
                    if (cmd.JetActive)
                    {
                        float jx = packX + packW * 0.5f;
                        brush.Color = new Color4(1.0f, 0.48f, 0.10f, 0.92f * alpha);
                        d2dContext.FillEllipse(new Ellipse(new Vector2(jx, packBottom + h * 0.10f), w * 0.08f, h * 0.12f), brush);
                        brush.Color = new Color4(0.42f, 0.92f, 1.0f, alpha);
                        d2dContext.FillEllipse(new Ellipse(new Vector2(jx, packBottom + h * 0.06f), w * 0.045f, h * 0.07f), brush);
                    }
                    break;

                case PenguinSubType.Baby:
                    brush.Color = new Color4(0.32f, 0.86f, 0.94f, alpha);
                    d2dContext.FillRoundedRectangle(new RoundedRectangle
                    {
                        Rect = new RawRectF(cx - w * 0.18f, ly + h * 0.48f, cx + w * 0.18f, ly + h * 0.56f),
                        RadiusX = 1.5f,
                        RadiusY = 1.5f
                    }, brush);
                    float tailX = cx - dirSign * w * 0.16f;
                    d2dContext.DrawLine(new Vector2(tailX, ly + h * 0.52f), new Vector2(tailX - dirSign * w * 0.10f, ly + h * 0.62f), brush, Math.Max(1.6f, w * 0.07f));
                    break;

                case PenguinSubType.Sleepy when !hideHat:
                    brush.Color = new Color4(0.38f, 0.44f, 0.84f, alpha);
                    float capTipX = cx - dirSign * w * 0.22f;
                    d2dContext.FillRoundedRectangle(new RoundedRectangle
                    {
                        Rect = new RawRectF(cx - w * 0.17f, ly + h * 0.09f, cx + w * 0.17f, ly + h * 0.20f),
                        RadiusX = 2.5f,
                        RadiusY = 2.5f
                    }, brush);
                    d2dContext.DrawLine(new Vector2(cx, ly + h * 0.12f), new Vector2(capTipX, ly + h * 0.24f), brush, Math.Max(2.2f, w * 0.08f));
                    brush.Color = new Color4(1.0f, 0.95f, 0.82f, alpha);
                    d2dContext.FillEllipse(new Ellipse(new Vector2(capTipX, ly + h * 0.25f), Math.Max(1.6f, w * 0.06f), Math.Max(1.6f, h * 0.06f)), brush);
                    break;
            }
        }
        finally
        {
            if (hasClimbTransform)
            {
                d2dContext.Transform = savedTransform;
            }
        }
    }

    internal static bool TryCreateClimbAccessoryTransform(
        PenguinAnimationId animation,
        FacingDirection direction,
        float cx,
        float cy,
        float height,
        out Matrix3x2 transform)
    {
        if (animation is not (PenguinAnimationId.Climber or PenguinAnimationId.SantaClimber))
        {
            transform = Matrix3x2.Identity;
            return false;
        }

        // Climber sprites lie horizontally with their crown away from the wall (-90° on right wall, +90° on left wall).
        float climbAngle = direction == FacingDirection.Right ? -MathF.PI * 0.5f : MathF.PI * 0.5f;
        transform =
            Matrix3x2.CreateRotation(climbAngle, new Vector2(cx, cy)) *
            Matrix3x2.CreateTranslation(0f, height * 0.04f);
        return true;
    }

    private static void RenderToys(ID2D1DeviceContext d2dContext, Rectangle bounds, IReadOnlyList<DesktopToy> toys, float alpha, ID2D1SolidColorBrush brush)
    {
        for (int i = 0; i < toys.Count; i++)
        {
            DesktopToy t = toys[i];
            if (!t.Active || !t.Bounds.IntersectsWith(bounds))
            {
                continue;
            }

            float lx = (float)(t.X - bounds.X);
            float ly = (float)(t.Y - bounds.Y);
            float w = t.Width;
            float h = t.Height;

            switch (t.Kind)
            {
                case ToyKind.Fish:
                    float bob = MathF.Sin((float)t.AgeSeconds * 4f) * (float)(1.5 * t.Scale);
                    // Tail fin
                    brush.Color = new Color4(0.95f, 0.45f, 0.10f, alpha);
                    d2dContext.FillEllipse(new Ellipse(new Vector2(lx + w * 0.16f, ly + h * 0.5f + bob), w * 0.16f, h * 0.34f), brush);
                    // Fish body
                    brush.Color = new Color4(1.0f, 0.62f, 0.14f, alpha);
                    d2dContext.FillEllipse(new Ellipse(new Vector2(lx + w * 0.56f, ly + h * 0.5f + bob), w * 0.40f, h * 0.42f), brush);
                    // Eye
                    brush.Color = new Color4(0.10f, 0.12f, 0.16f, alpha);
                    d2dContext.FillEllipse(new Ellipse(new Vector2(lx + w * 0.78f, ly + h * 0.42f + bob), Math.Max(1.5f, w * 0.08f), Math.Max(1.5f, w * 0.08f)), brush);
                    break;

                case ToyKind.Trampoline:
                    float squish = (float)(t.Pulse * h * 0.25);
                    brush.Color = new Color4(0.22f, 0.26f, 0.32f, alpha);
                    d2dContext.FillRectangle(new RawRectF(lx + w * 0.12f, ly + h * 0.45f, lx + w * 0.22f, ly + h), brush);
                    d2dContext.FillRectangle(new RawRectF(lx + w * 0.78f, ly + h * 0.45f, lx + w * 0.88f, ly + h), brush);
                    brush.Color = new Color4(0.18f, 0.72f, 0.98f, alpha);
                    d2dContext.FillRoundedRectangle(new RoundedRectangle
                    {
                        Rect = new RawRectF(lx, ly + squish, lx + w, ly + h * 0.55f + squish),
                        RadiusX = Math.Max(2f, h * 0.22f),
                        RadiusY = Math.Max(2f, h * 0.22f)
                    }, brush);
                    break;

                case ToyKind.PortalA:
                case ToyKind.PortalB:
                    bool isA = t.Kind == ToyKind.PortalA;
                    float ringPulse = 1f + 0.08f * MathF.Sin((float)t.AgeSeconds * 6f) + (float)t.Pulse * 0.18f;
                    var center = new Vector2(lx + w * 0.5f, ly + h * 0.5f);
                    float prx = w * 0.45f * ringPulse;
                    float pry = h * 0.46f * ringPulse;

                    brush.Color = isA
                        ? new Color4(0.16f, 0.65f, 1.0f, 0.26f * alpha)
                        : new Color4(1.0f, 0.52f, 0.12f, 0.26f * alpha);
                    d2dContext.FillEllipse(new Ellipse(center, prx, pry), brush);

                    brush.Color = isA
                        ? new Color4(0.32f, 0.85f, 1.0f, 0.92f * alpha)
                        : new Color4(1.0f, 0.70f, 0.22f, 0.92f * alpha);
                    d2dContext.DrawEllipse(new Ellipse(center, prx, pry), brush, Math.Max(2f, (float)(2.4 * t.Scale)));
                    break;
            }
        }
    }

    private static void RenderSnow(ID2D1DeviceContext d2dContext, Rectangle bounds, ReadOnlySpan<SnowParticle> flakes, float alpha, ID2D1SolidColorBrush brush)
    {
        for (int i = 0; i < flakes.Length; i++)
        {
            ref readonly SnowParticle f = ref flakes[i];
            if (!f.Active)
            {
                continue;
            }

            float lx = f.X - bounds.X;
            float ly = f.Y - bounds.Y;
            if (lx < -8 || ly < -8 || lx > bounds.Width + 8 || ly > bounds.Height + 8)
            {
                continue;
            }

            float fade = f.Settled ? Math.Clamp(f.SettledSecondsRemaining / 1.5f, 0f, 1f) * 0.72f : 0.85f;
            brush.Color = new Color4(0.95f, 0.98f, 1.0f, fade * alpha);
            float r = f.Size * 0.5f;
            d2dContext.FillEllipse(new Ellipse(new Vector2(lx, ly), r, f.Settled ? r * 0.55f : r), brush);
        }
    }

    private void ClearAndPresent()
    {
        try
        {
            _d2dContext.Target = _targetBitmap;
            _d2dContext.BeginDraw();
            _d2dContext.Clear(new Color4(0f, 0f, 0f, 0f));
            _d2dContext.EndDraw();
            _swapChain.Present(0, PresentFlags.None);
        }
        catch (SharpGen.Runtime.SharpGenException)
        {
            // Ignore clear errors during device reset.
        }
        finally
        {
            _d2dContext.Target = null;
        }
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

            case NativeMethods.WM_RBUTTONDOWN:
                if (!_clickThrough)
                {
                    m.Result = IntPtr.Zero;
                    return;
                }
                break;

            case NativeMethods.WM_RBUTTONUP:
                if (!_clickThrough)
                {
                    NativeMethods.GetCursorPos(out NativeMethods.POINT pt);
                    RightClickOnOverlay?.Invoke(pt.X, pt.Y);
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

    internal readonly record struct DrawCommand(
        PenguinAnimationId Animation,
        FacingDirection Direction,
        int FrameIndex,
        float LocalX,
        float LocalY,
        int Width,
        int Height,
        short RotTenths,
        short ScaleXPct,
        short ScaleYPct,
        int ShadowDist,
        bool IsSliding,
        PenguinEmote Emote,
        byte EmoteAlpha,
        PenguinSubType SubType,
        bool JetActive);
}
