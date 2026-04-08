using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fetcher.Core.Models;

public sealed class DownloadManifest
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public Guid Id { get; init; } = Guid.NewGuid();
    public Uri BlobUri { get; init; } = null!;
    public long TotalSize { get; init; }
    public byte[] ContentHash { get; init; } = [];
    public int ChunkSizeBytes { get; init; }
    public List<ChunkState> Chunks { get; init; } = [];
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    [JsonIgnore]
    public long TotalBytesWritten => Chunks.Sum(c => c.BytesWritten);

    [JsonIgnore]
    public double Progress => TotalSize > 0 ? (double)TotalBytesWritten / TotalSize : 0;

    public async Task SaveAsync(string path, CancellationToken ct = default)
    {
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, this, JsonOptions, ct);
    }

    public static async Task<DownloadManifest?> LoadAsync(string path, CancellationToken ct = default)
    {
        if (!File.Exists(path))
            return null;

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<DownloadManifest>(stream, JsonOptions, ct);
    }

    public static string GetManifestPath(string outputFilePath)
        => $"{outputFilePath}.fetcher-manifest.json";

    public void AssignTempFilePaths(string outputFilePath)
    {
        foreach (var chunk in Chunks)
            chunk.TempFilePath = $"{outputFilePath}.{chunk.Index:D6}";
    }

    public void SyncBytesWrittenFromDisk()
    {
        foreach (var chunk in Chunks)
        {
            if (chunk.IsComplete)
                continue;

            if (File.Exists(chunk.TempFilePath))
            {
                var fileLength = new FileInfo(chunk.TempFilePath).Length;
                chunk.BytesWritten = fileLength;
            }
        }
    }
}
