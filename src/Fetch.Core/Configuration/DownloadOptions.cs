namespace Fetch.Core.Configuration;

public sealed record DownloadOptions
{
    public required Uri BlobUri { get; init; }
    public string LocalPath { get; init; } = Directory.GetCurrentDirectory();
    public string? AccountKey { get; init; }
    public int MaxConcurrency { get; init; } = Math.Min(Environment.ProcessorCount * 4, 32);
    public int MaxChunkSizeBytes { get; init; } = 256 * 1024 * 1024; // 256 MB cap
    public int BufferSizeBytes { get; init; } = 81920;               // 80 KB (.NET default)
    public int MaxRetriesPerChunk { get; init; } = 5;
    public TimeSpan RetryBaseDelay { get; init; } = TimeSpan.FromSeconds(2);
    public bool WriteDebugManifest { get; init; } = false;
    public bool WaitForDownload { get; init; } = false;
}
