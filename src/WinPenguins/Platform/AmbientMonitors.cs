using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WinPenguins.Platform;

public static class FocusAndRecordingMonitor
{
    private const int QunsRunningD3dFullScreen = 3;
    private const int QunsPresentationMode = 4;
    private const int QunsQuietTime = 6;

    // Deliberately exclude QUNS_BUSY (2): Windows 11 frequently reports QUNS_BUSY on ordinary
    // multi-monitor desktops and Explorer sessions, which would spuriously blink or hide penguins.
    internal static bool IsBusyNotificationState(int quns) =>
        quns is QunsRunningD3dFullScreen or QunsPresentationMode or QunsQuietTime;

    public static bool IsPresentationOrFocusBusy()
    {
        try
        {
            if (NativeMethods.SHQueryUserNotificationState(out int quns) == 0)
            {
                return IsBusyNotificationState(quns);
            }
        }
        catch
        {
            // Ignore shell query issues safely.
        }

        return false;
    }

    public static bool IsAnyProcessRunning(IReadOnlySet<string> targetProcessNames)
    {
        if (targetProcessNames.Count == 0)
        {
            return false;
        }

        try
        {
            foreach (Process p in Process.GetProcesses())
            {
                try
                {
                    if (targetProcessNames.Contains(p.ProcessName))
                    {
                        return true;
                    }
                }
                finally
                {
                    p.Dispose();
                }
            }
        }
        catch
        {
            // Ignore process enumeration issues gracefully.
        }

        return false;
    }
}

public sealed class AudioMeterMonitor : IDisposable
{
    private const int ERender = 0;
    private const int EMultimedia = 1;
    private static readonly Guid IidIAudioMeterInformation = new("C02216F6-8C67-4B5B-9D00-D008E73E0064");

    private IAudioMeterInformation? _meter;
    private long _lastAcquireAttemptTick;
    private long _lastSampleTick;
    private float _cachedPeak;

    public float SamplePeak(bool enabled)
    {
        if (!enabled)
        {
            _cachedPeak = 0f;
            return 0f;
        }

        long now = Environment.TickCount64;
        if (now - _lastSampleTick < 30)
        {
            return _cachedPeak;
        }

        _lastSampleTick = now;

        if (_meter == null)
        {
            if (now - _lastAcquireAttemptTick < 3000)
            {
                return 0f;
            }

            _lastAcquireAttemptTick = now;
            TryAcquireMeter();
        }

        if (_meter == null)
        {
            return 0f;
        }

        try
        {
            int hr = _meter.GetPeakValue(out float peak);
            if (hr == 0)
            {
                _cachedPeak = Math.Clamp(peak, 0f, 1f);
                return _cachedPeak;
            }
        }
        catch
        {
            // Endpoint disconnected; re-acquire cleanly later.
        }

        ReleaseMeter();
        _cachedPeak = 0f;
        return 0f;
    }

    private void TryAcquireMeter()
    {
        object? enumeratorObj = null;
        IMMDevice? device = null;
        try
        {
            enumeratorObj = new MMDeviceEnumeratorCom();
            var enumerator = (IMMDeviceEnumerator)enumeratorObj;
            if (enumerator.GetDefaultAudioEndpoint(ERender, EMultimedia, out device) != 0 || device == null)
            {
                return;
            }

            Guid iid = IidIAudioMeterInformation;
            if (device.Activate(in iid, 1, IntPtr.Zero, out object meterObj) == 0)
            {
                _meter = meterObj as IAudioMeterInformation;
            }
        }
        catch
        {
            ReleaseMeter();
        }
        finally
        {
            if (device != null)
            {
                Marshal.ReleaseComObject(device);
            }
            if (enumeratorObj != null)
            {
                Marshal.ReleaseComObject(enumeratorObj);
            }
        }
    }

    private void ReleaseMeter()
    {
        if (_meter != null)
        {
            try
            {
                Marshal.ReleaseComObject(_meter);
            }
            catch
            {
                // Ignore COM release failures during teardown.
            }
            _meter = null;
        }
    }

    public void Dispose() => ReleaseMeter();

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private sealed class MMDeviceEnumeratorCom
    {
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(int dataFlow, uint dwStateMask, out IntPtr ppDevices);
        [PreserveSig]
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice? ppEndpoint);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(in Guid iid, uint dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
    }

    [ComImport]
    [Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioMeterInformation
    {
        [PreserveSig]
        int GetPeakValue(out float pfPeak);
    }
}
