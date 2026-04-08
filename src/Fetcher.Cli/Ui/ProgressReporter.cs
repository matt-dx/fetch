using System.Collections.Concurrent;
using System.Diagnostics;
using Fetcher.Core.Orchestration;

namespace Fetcher.Cli.Ui;

public sealed class ProgressReporter : IDownloadProgress
{
    private readonly ConcurrentDictionary<int, long> _chunkBytes = new();
    private readonly ConcurrentDictionary<int, bool> _chunkCompleted = new();
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private long _totalBytesWritten;

    public DownloadPhase CurrentPhase { get; private set; } = DownloadPhase.Preparing;
    public long TotalSize { get; set; }
    public int TotalChunks { get; set; }

    public long TotalBytesWritten => Interlocked.Read(ref _totalBytesWritten);
    public int CompletedChunks => _chunkCompleted.Count;
    public TimeSpan Elapsed => _stopwatch.Elapsed;

    public double Progress => TotalSize > 0 ? (double)TotalBytesWritten / TotalSize : 0;

    public double BytesPerSecond
    {
        get
        {
            var seconds = Elapsed.TotalSeconds;
            return seconds > 0 ? TotalBytesWritten / seconds : 0;
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
        CurrentPhase = phase;
    }

    public void ReportError(int chunkIndex, Exception ex, int retryAttempt)
    {
        // Errors are available for UI to display if needed
        LastError = $"Chunk {chunkIndex}: {ex.Message} (retry {retryAttempt})";
    }

    public string? LastError { get; private set; }
}
