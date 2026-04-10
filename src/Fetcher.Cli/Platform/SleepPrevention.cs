using System.Runtime.InteropServices;

namespace Fetcher.Cli.Platform;

public static class SleepPrevention
{
    private const uint ES_CONTINUOUS = 0x80000000;
    private const uint ES_SYSTEM_REQUIRED = 0x00000001;
    private const uint ES_DISPLAY_REQUIRED = 0x00000002;

    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(30);

    public static IDisposable Prevent()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new NoOpDisposable();

        return new SleepGuard();
    }

    [DllImport("kernel32.dll")]
    internal static extern uint SetThreadExecutionState(uint esFlags);

    private sealed class SleepGuard : IDisposable
    {
        private readonly Thread _thread;
        private readonly ManualResetEventSlim _stopSignal = new(false);

        public SleepGuard()
        {
            _thread = new Thread(KeepAwakeLoop)
            {
                IsBackground = true,
                Name = "SleepPrevention"
            };
            _thread.Start();
        }

        private void KeepAwakeLoop()
        {
            const uint flags = ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED;

            try
            {
                SetThreadExecutionState(flags);

                while (!_stopSignal.Wait(RefreshInterval))
                {
                    SetThreadExecutionState(flags);
                }
            }
            catch (ObjectDisposedException)
            {
                // Dispose() was called and the signal was disposed before we
                // could observe it — fall through to clear the execution state.
            }

            SetThreadExecutionState(ES_CONTINUOUS);
        }

        public void Dispose()
        {
            _stopSignal.Set();
            if (_thread.Join(TimeSpan.FromSeconds(5)))
            {
                _stopSignal.Dispose();
            }
            // If Join timed out the background thread is still running.
            // It will observe the signalled event on its next iteration and
            // exit cleanly.  We intentionally leak the ManualResetEventSlim
            // rather than disposing it out from under the thread.
        }
    }

    private sealed class NoOpDisposable : IDisposable
    {
        public void Dispose() { }
    }
}
