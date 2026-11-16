namespace WinPenguins.Platform;

public static class PowerMonitor
{
    /// <summary>
    /// Returns true when Windows Battery Saver / Energy Saver is currently active.
    /// </summary>
    public static bool IsBatterySaverActive()
    {
        if (!NativeMethods.GetSystemPowerStatus(out NativeMethods.SYSTEM_POWER_STATUS status))
        {
            return false;
        }

        return status.SystemStatusFlag == 1;
    }
}
