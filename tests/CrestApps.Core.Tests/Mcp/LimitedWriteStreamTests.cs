using CrestApps.Core.AI.Mcp.IO;

namespace CrestApps.Core.Tests.Mcp;

public sealed class LimitedWriteStreamTests
{
    [Fact]
    public void Write_WithinLimit_Succeeds()
    {
        using var inner = new MemoryStream();
        using var stream = new LimitedWriteStream(inner, maxBytes: 16);

        var payload = new byte[] { 1, 2, 3, 4 };
        stream.Write(payload, 0, payload.Length);
        stream.Write(payload.AsSpan());

        Assert.Equal(8, stream.BytesWritten);
        Assert.Equal(8, inner.Length);
    }

    [Fact]
    public void Write_ExceedingLimit_ThrowsResourceSizeLimitExceeded()
    {
        using var inner = new MemoryStream();
        using var stream = new LimitedWriteStream(inner, maxBytes: 4);

        stream.Write([1, 2, 3], 0, 3);

        var ex = Assert.Throws<ResourceSizeLimitExceededException>(() => stream.Write([4, 5], 0, 2));
        Assert.Equal(4, ex.MaxBytes);

        // BytesWritten reflects only successful writes.
        Assert.Equal(3, stream.BytesWritten);
    }

    [Fact]
    public void WriteSpan_ExceedingLimit_Throws()
    {
        using var inner = new MemoryStream();
        using var stream = new LimitedWriteStream(inner, maxBytes: 2);

        Assert.Throws<ResourceSizeLimitExceededException>(() => stream.Write(new byte[] { 1, 2, 3 }.AsSpan()));
        Assert.Equal(0, stream.BytesWritten);
    }

    [Fact]
    public async Task WriteAsync_WithinLimit_TracksBytesWritten()
    {
        using var inner = new MemoryStream();
        await using var stream = new LimitedWriteStream(inner, maxBytes: 10);
        var ct = TestContext.Current.CancellationToken;

        // Exercise both the byte[]-based and Memory<byte>-based async overloads.
#pragma warning disable CA1835 // Intentionally exercises the byte[] overload.
        await stream.WriteAsync(new byte[] { 1, 2, 3, 4 }, 0, 4, ct);
#pragma warning restore CA1835
        await stream.WriteAsync(new byte[] { 5, 6 }.AsMemory(), ct);

        Assert.Equal(6, stream.BytesWritten);
        Assert.Equal(6, inner.Length);
    }

    [Fact]
    public async Task WriteAsync_ExceedingLimit_Throws()
    {
        using var inner = new MemoryStream();
        await using var stream = new LimitedWriteStream(inner, maxBytes: 3);
        var ct = TestContext.Current.CancellationToken;

#pragma warning disable CA1835 // Intentionally exercises the byte[] overload.
        await Assert.ThrowsAsync<ResourceSizeLimitExceededException>(async () =>
            await stream.WriteAsync(new byte[] { 1, 2, 3, 4 }, 0, 4, ct));
#pragma warning restore CA1835

        await Assert.ThrowsAsync<ResourceSizeLimitExceededException>(async () =>
            await stream.WriteAsync(new byte[] { 1, 2, 3, 4 }.AsMemory(), ct).AsTask());
    }

    [Fact]
    public void Constructor_NullInner_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new LimitedWriteStream(null, 10));
    }

    [Fact]
    public void Constructor_NonPositiveMaxBytes_Throws()
    {
        using var inner = new MemoryStream();
        Assert.Throws<ArgumentOutOfRangeException>(() => new LimitedWriteStream(inner, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LimitedWriteStream(inner, -1));
    }

    [Fact]
    public void Capabilities_AreWriteOnly()
    {
        using var inner = new MemoryStream();
        using var stream = new LimitedWriteStream(inner, 10);

        Assert.True(stream.CanWrite);
        Assert.False(stream.CanRead);
        Assert.False(stream.CanSeek);
        Assert.Throws<NotSupportedException>(() => stream.Read(new byte[1], 0, 1));
        Assert.Throws<NotSupportedException>(() => stream.Seek(0, SeekOrigin.Begin));
        Assert.Throws<NotSupportedException>(() => stream.SetLength(5));
    }

    [Fact]
    public async Task FlushAndDispose_ProxyToInnerStream()
    {
        var inner = new TrackingStream();
        var ct = TestContext.Current.CancellationToken;

        var stream = new LimitedWriteStream(inner, 10);
        stream.Write([1, 2, 3], 0, 3);
        stream.Flush();

        Assert.True(inner.Flushed);

        inner.Flushed = false;
        await stream.FlushAsync(ct);
        Assert.True(inner.Flushed);

        await stream.DisposeAsync();

        // Inner remains accessible to the test (LimitedWriteStream does not own it),
        // but writes through the disposed wrapper should fail.
        Assert.Equal(3, inner.Length);
    }

    private sealed class TrackingStream : MemoryStream
    {
        public bool Flushed { get; set; }

        public override void Flush()
        {
            Flushed = true;
            base.Flush();
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            Flushed = true;
            return base.FlushAsync(cancellationToken);
        }
    }
}
