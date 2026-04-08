using System.Runtime.InteropServices;

namespace Fetcher.Cli.Platform;

public static class SleepPrevention
{
    private const uint ES_CONTINUOUS = 0x80000000;
    private const uint ES_SYSTEM_REQUIRED = 0x00000001;

    public static IDisposable Prevent()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new NoOpDisposable();

        SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED);
        return new SleepGuard();
    }

    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint esFlags);

    private sealed class SleepGuard : IDisposable
    {
        public void Dispose() => SetThreadExecutionState(ES_CONTINUOUS);
    }

    private sealed class NoOpDisposable : IDisposable
    {
        public void Dispose() { }
    }
}
