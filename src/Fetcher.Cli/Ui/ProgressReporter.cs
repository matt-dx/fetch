using System.Collections.Concurrent;
using System.Diagnostics;
using Fetcher.Core.Orchestration;

namespace Fetcher.Cli.Ui;

public sealed class ProgressReporter : IDownloadProgress
{
    private readonly ConcurrentDictionary<int, long> _chunkBytes = new();
    private readonly ConcurrentDictionary<int, bool> _chunkCompleted = new();
    private readonly Stopwatch _stopwatch = new();
    private long _totalBytesWritten;
    private long _resumedBytes;

    public DownloadPhase CurrentPhase { get; private set; } = DownloadPhase.Preparing;
    public long TotalSize { get; set; }
    public int TotalChunks { get; set; }

    public long TotalBytesWritten => Interlocked.Read(ref _totalBytesWritten);
    public long SessionBytesWritten => TotalBytesWritten - Interlocked.Read(ref _resumedBytes);
    public int CompletedChunks => _chunkCompleted.Count;
    public TimeSpan Elapsed => _stopwatch.Elapsed;

    public double Progress => TotalSize > 0 ? (double)TotalBytesWritten / TotalSize : 0;

    public double BytesPerSecond
    {
        get
        {
            var seconds = Elapsed.TotalSeconds;
            return seconds > 0 ? SessionBytesWritten / seconds : 0;
        }
    }

    public TimeSpan EstimatedTimeRemaining
    {
        get
        {
            var bps = BytesPerSecond;
            if (bps <= 0) return TimeSpan.Zero;
            var remaining = TotalSize - TotalBytesWritten;
            return TimeSpan.FromSeconds(remaining / bps);
        }
    }

    /// <summary>
    /// Locks in the current total as the resumed baseline and starts the session timer.
    /// Call once after seeding existing chunk state, before downloading begins.
    /// </summary>
    public void StartSession()
    {
        Interlocked.Exchange(ref _resumedBytes, Interlocked.Read(ref _totalBytesWritten));
        _stopwatch.Restart();
    }

    public void ReportMetadata(long totalSize, int totalChunks)
    {
        TotalSize = totalSize;
        TotalChunks = totalChunks;
    }

    public void ReportBytesWritten(int chunkIndex, long totalBytesWritten)
    {
        long delta = totalBytesWritten;
        _chunkBytes.AddOrUpdate(
            chunkIndex,
            totalBytesWritten,
            (_, old) => { delta = totalBytesWritten - old; return totalBytesWritten; });
        Interlocked.Add(ref _totalBytesWritten, delta);
    }

    public void ReportChunkCompleted(int chunkIndex)
    {
        _chunkCompleted.TryAdd(chunkIndex, true);
    }

    public void ReportPhaseChanged(DownloadPhase phase)
    {
        if (phase == DownloadPhase.Downloading && !_stopwatch.IsRunning)
            StartSession();

        CurrentPhase = phase;
    }

    public void ReportError(int chunkIndex, Exception ex, int retryAttempt)
    {
        // Errors are available for UI to display if needed
        LastError = $"Chunk {chunkIndex}: {ex.Message} (retry {retryAttempt})";
    }

    public string? LastError { get; private set; }
}
