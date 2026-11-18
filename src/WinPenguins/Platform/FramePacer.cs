using System.Diagnostics;

namespace WinPenguins.Platform;

public sealed class FramePacer : IDisposable
{
    private const int WmAppTick = NativeMethods.WM_APP + 1;

    private readonly Action<double> _onTick;
    private readonly MessageTargetWindow _msgWindow;
    private readonly ManualResetEventSlim _activeGate = new(initialState: true);
    private readonly IntPtr _highResTimer;
    private readonly Thread _thread;

    private volatile bool _disposed;
    private volatile int _targetFps;
    private int _tickPending;
    private long _lastTickTimestamp;

    public FramePacer(int targetFps, Action<double> onTick)
    {
        _targetFps = Math.Clamp(targetFps, 30, 240);
        _onTick = onTick;
        _msgWindow = new MessageTargetWindow(OnMessageTick);
        _lastTickTimestamp = Stopwatch.GetTimestamp();

        _highResTimer = NativeMethods.CreateWaitableTimerEx(
            IntPtr.Zero,
            null,
            NativeMethods.CREATE_WAITABLE_TIMER_HIGH_RESOLUTION,
            NativeMethods.TIMER_ALL_ACCESS);

        _thread = new Thread(PaceLoop)
        {
            IsBackground = true,
            Name = "WinPenguinsFramePacer"
        };
        _thread.Start();
    }

    public void SetTargetFps(int targetFps) =>
        _targetFps = Math.Clamp(targetFps, 30, 240);

    public void SetPaused(bool paused)
    {
        if (paused)
        {
            _activeGate.Reset();
        }
        else
        {
            _lastTickTimestamp = Stopwatch.GetTimestamp();
            _activeGate.Set();
        }
    }

    private void PaceLoop()
    {
        while (!_disposed)
        {
            try
            {
                _activeGate.Wait();
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            if (_disposed)
            {
                break;
            }

            int fps = _targetFps;
            WaitInterval(fps);

            if (_disposed)
            {
                break;
            }

            IntPtr hwnd = _msgWindow.Handle;
            if (hwnd != IntPtr.Zero && Interlocked.CompareExchange(ref _tickPending, 1, 0) == 0)
            {
                if (!NativeMethods.PostMessage(hwnd, WmAppTick, IntPtr.Zero, IntPtr.Zero))
                {
                    Interlocked.Exchange(ref _tickPending, 0);
                }
            }
        }
    }

    private void WaitInterval(int fps)
    {
        if (_highResTimer != IntPtr.Zero)
        {
            // Negative value specifies relative time in 100-nanosecond units.
            long dueTime100Ns = -(10_000_000L / Math.Max(1, fps));
            if (NativeMethods.SetWaitableTimer(_highResTimer, ref dueTime100Ns, 0, IntPtr.Zero, IntPtr.Zero, false))
            {
                _ = NativeMethods.WaitForSingleObject(_highResTimer, 100);
                return;
            }
        }

        Thread.Sleep(Math.Max(1, 1000 / Math.Max(1, fps)));
    }

    private void OnMessageTick()
    {
        Interlocked.Exchange(ref _tickPending, 0);
        if (_disposed || !_activeGate.IsSet)
        {
            return;
        }

        long now = Stopwatch.GetTimestamp();
        double elapsedMs = (now - _lastTickTimestamp) * 1000.0 / Stopwatch.Frequency;
        _lastTickTimestamp = now;
        elapsedMs = Math.Clamp(elapsedMs, 0.5, 100.0);

        _onTick(elapsedMs);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _activeGate.Set();
        _thread.Join(250);
        _msgWindow.Dispose();
        _activeGate.Dispose();

        if (_highResTimer != IntPtr.Zero)
        {
            NativeMethods.CloseHandle(_highResTimer);
        }
    }

    private sealed class MessageTargetWindow : NativeWindow, IDisposable
    {
        private readonly Action _onTickMessage;

        public MessageTargetWindow(Action onTickMessage)
        {
            _onTickMessage = onTickMessage;
            CreateHandle(new CreateParams
            {
                Caption = "WinPenguinsPacerMsg",
                Parent = NativeMethods.HWND_MESSAGE
            });
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmAppTick)
            {
                _onTickMessage();
                return;
            }

            base.WndProc(ref m);
        }

        public void Dispose()
        {
            if (Handle != IntPtr.Zero)
            {
                DestroyHandle();
            }
        }
    }
}
