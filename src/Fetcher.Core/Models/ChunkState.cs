using System.Text.Json.Serialization;

namespace Fetcher.Core.Models;

public sealed class ChunkState
{
    public int Index { get; init; }

    [JsonIgnore]
    public string TempFilePath { get; set; } = string.Empty;

    public long Offset { get; init; }
    public long Length { get; init; }
    public long BytesWritten { get; set; }

    public bool IsComplete => BytesWritten >= Length;
    public long BytesRemaining => Length - BytesWritten;
    public long ResumeOffset => Offset + BytesWritten;
}
