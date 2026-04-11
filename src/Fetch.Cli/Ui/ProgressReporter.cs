using System.Collections.Concurrent;
using System.Diagnostics;
using Fetch.Core.Orchestration;

namespace Fetch.Cli.Ui;

public sealed record ChunkProgress(int Index, long BytesWritten, long TotalLength);

public sealed class ProgressReporter : IDownloadProgress
{
    private readonly ConcurrentDictionary<int, long> _chunkBytes = new();
    private readonly ConcurrentDictionary<int, long> _chunkLengths = new();
    private readonly ConcurrentDictionary<int, bool> _chunkCompleted = new();
    private readonly ConcurrentDictionary<int, bool> _chunkAssembled = new();
    private readonly Stopwatch _stopwatch = new();
    private long _totalBytesWritten;
    private long _resumedBytes;

    public DownloadPhase CurrentPhase { get; private set; } = DownloadPhase.Preparing;
    public long TotalSize { get; set; }
    public int TotalChunks { get; set; }
    public string? FileName { get; set; }

    // Download tracking
    public long TotalBytesWritten => Interlocked.Read(ref _totalBytesWritten);
    public long SessionBytesWritten => TotalBytesWritten - Interlocked.Read(ref _resumedBytes);
    public int CompletedChunks => _chunkCompleted.Count;
    public TimeSpan Elapsed => _stopwatch.Elapsed;

    public double DownloadProgress => TotalSize > 0 ? (double)TotalBytesWritten / TotalSize : 0;

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

    // Assembly tracking
    public int AssembledChunks => _chunkAssembled.Count;
    public double AssemblyProgress => TotalChunks > 0 ? (double)AssembledChunks / TotalChunks : 0;

    // Per-chunk detail view
    public IReadOnlyList<ChunkProgress> GetActiveChunks()
    {
        return _chunkBytes
            .Where(kvp => !_chunkCompleted.ContainsKey(kvp.Key))
            .Select(kvp => new ChunkProgress(
                kvp.Key,
                kvp.Value,
                _chunkLengths.GetValueOrDefault(kvp.Key, 0)))
            .OrderBy(c => c.Index)
            .ToList();
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

    public void ReportChunkInfo(int chunkIndex, long length)
    {
        _chunkLengths[chunkIndex] = length;
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

    public void ReportChunkAssembled(int chunkIndex)
    {
        _chunkAssembled.TryAdd(chunkIndex, true);
    }

    public void ReportPhaseChanged(DownloadPhase phase)
    {
        if (phase == DownloadPhase.Downloading && !_stopwatch.IsRunning)
            StartSession();

        CurrentPhase = phase;
    }

    public void ReportError(int chunkIndex, Exception ex, int retryAttempt)
    {
        LastError = $"Chunk {chunkIndex}: {ex.Message} (retry {retryAttempt})";
    }

    public string? LastError { get; private set; }

    /// <summary>
    /// Resets all state so the reporter can be reused for the next download in a queue.
    /// </summary>
    public void Reset()
    {
        _chunkBytes.Clear();
        _chunkLengths.Clear();
        _chunkCompleted.Clear();
        _chunkAssembled.Clear();
        _stopwatch.Reset();
        Interlocked.Exchange(ref _totalBytesWritten, 0);
        Interlocked.Exchange(ref _resumedBytes, 0);
        CurrentPhase = DownloadPhase.Preparing;
        TotalSize = 0;
        TotalChunks = 0;
        FileName = null;
        LastError = null;
    }
}
