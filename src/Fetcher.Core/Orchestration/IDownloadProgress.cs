namespace Fetcher.Core.Orchestration;

public enum DownloadPhase
{
    Preparing,
    Downloading,
    Assembling,
    Validating,
    Complete,
    Failed
}

public interface IDownloadProgress
{
    void ReportMetadata(long totalSize, int totalChunks);
    void ReportBytesWritten(int chunkIndex, long totalBytesWritten);
    void ReportChunkCompleted(int chunkIndex);
    void ReportChunkAssembled(int chunkIndex);
    void ReportPhaseChanged(DownloadPhase phase);
    void ReportError(int chunkIndex, Exception ex, int retryAttempt);
}

public sealed record DownloadResult
{
    public bool Success { get; init; }
    public string OutputPath { get; init; } = string.Empty;
    public long TotalBytes { get; init; }
    public TimeSpan DownloadDuration { get; init; }
    public TimeSpan AssemblyDuration { get; init; }
    public TimeSpan ValidationDuration { get; init; }
    public TimeSpan TotalDuration { get; init; }
    public Exception? Error { get; init; }
}
