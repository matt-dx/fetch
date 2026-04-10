using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fetch.Core.Models;

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
        // Write to a temp file first, then atomically replace the target.
        // This prevents an empty/corrupt manifest if the process is killed mid-write.
        var tempPath = path + ".tmp";
        try
        {
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, this, JsonOptions, ct);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            try { File.Delete(tempPath); } catch { }
            throw;
        }
    }

    public static async Task<DownloadManifest?> LoadAsync(string path, CancellationToken ct = default)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<DownloadManifest>(stream, JsonOptions, ct);
        }
        catch (JsonException)
        {
            // Corrupt or empty manifest (e.g., process killed during write) — treat as absent
            return null;
        }
    }

    public static string GetManifestPath(string outputFilePath)
        => $"{outputFilePath}.fetch-manifest.json";

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
