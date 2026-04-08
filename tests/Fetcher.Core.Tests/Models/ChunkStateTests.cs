using Fetcher.Core.Models;
using FluentAssertions;

namespace Fetcher.Core.Tests.Models;

public class ChunkStateTests
{
    [Fact]
    public void IsComplete_WhenBytesWrittenEqualsLength_ReturnsTrue()
    {
        var chunk = new ChunkState { Length = 1000, BytesWritten = 1000 };
        chunk.IsComplete.Should().BeTrue();
    }

    [Fact]
    public void IsComplete_WhenBytesWrittenExceedsLength_ReturnsTrue()
    {
        var chunk = new ChunkState { Length = 1000, BytesWritten = 1500 };
        chunk.IsComplete.Should().BeTrue();
    }

    [Fact]
    public void IsComplete_WhenBytesWrittenLessThanLength_ReturnsFalse()
    {
        var chunk = new ChunkState { Length = 1000, BytesWritten = 500 };
        chunk.IsComplete.Should().BeFalse();
    }

    [Fact]
    public void BytesRemaining_ReturnsCorrectValue()
    {
        var chunk = new ChunkState { Length = 1000, BytesWritten = 300 };
        chunk.BytesRemaining.Should().Be(700);
    }

    [Fact]
    public void BytesRemaining_WhenComplete_ReturnsZero()
    {
        var chunk = new ChunkState { Length = 1000, BytesWritten = 1000 };
        chunk.BytesRemaining.Should().Be(0);
    }

    [Fact]
    public void ResumeOffset_ReturnsOffsetPlusBytesWritten()
    {
        var chunk = new ChunkState { Offset = 5000, Length = 1000, BytesWritten = 300 };
        chunk.ResumeOffset.Should().Be(5300);
    }

    [Fact]
    public void ResumeOffset_WhenNoBytesWritten_ReturnsOffset()
    {
        var chunk = new ChunkState { Offset = 5000, Length = 1000, BytesWritten = 0 };
        chunk.ResumeOffset.Should().Be(5000);
    }
}
