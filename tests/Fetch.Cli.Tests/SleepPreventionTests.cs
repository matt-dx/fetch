using System.Runtime.InteropServices;
using Fetch.Cli.Platform;
using FluentAssertions;

namespace Fetch.Cli.Tests;

public class SleepPreventionTests
{
    [Fact]
    [Trait("Category", "Windows")]
    public void Prevent_ReturnsDisposable_ThatCanBeDisposed()
    {
        var guard = SleepPrevention.Prevent();
        guard.Should().NotBeNull();
        guard.Dispose();
    }

    [Fact]
    [Trait("Category", "Windows")]
    public void SetThreadExecutionState_ReturnsNonZero_OnWindows()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        var result = SleepPrevention.SetThreadExecutionState(0x80000000); // ES_CONTINUOUS
        result.Should().NotBe(0u, "SetThreadExecutionState should succeed on Windows");
    }

    [Fact]
    [Trait("Category", "Windows")]
    public void Prevent_OnNonWindows_ReturnsNoOp()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        var guard = SleepPrevention.Prevent();
        guard.GetType().Name.Should().Contain("NoOp");
        guard.Dispose();
    }
}
