using System.Runtime.ExceptionServices;

namespace WinPenguins.Tests;

internal static class StaRunner
{
    public static void Run(Action action, int timeoutSeconds = 45)
    {
        ExceptionDispatchInfo? captured = null;
        using var done = new ManualResetEventSlim(false);

        var thread = new Thread(() =>
        {
            SynchronizationContext? previous = SynchronizationContext.Current;
            using var winFormsCtx = new WindowsFormsSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(winFormsCtx);
            try
            {
                action();
            }
            catch (Exception ex)
            {
                captured = ExceptionDispatchInfo.Capture(ex);
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previous);
                done.Set();
            }
        });

        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        if (!done.Wait(TimeSpan.FromSeconds(timeoutSeconds)))
        {
            throw new TimeoutException($"STA UI test timed out after {timeoutSeconds}s.");
        }

        captured?.Throw();
    }
}
