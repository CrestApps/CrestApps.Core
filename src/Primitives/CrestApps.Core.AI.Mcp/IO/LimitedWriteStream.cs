namespace CrestApps.Core.AI.Mcp.IO;

/// <summary>
/// Write-only stream wrapper that enforces a maximum byte budget on the underlying stream.
/// Throws <see cref="ResourceSizeLimitExceededException"/> when a write would exceed
/// <see cref="MaxBytes"/>, allowing callers to abort streaming downloads of untrusted
/// remote files before they exhaust process memory.
/// </summary>
public sealed class LimitedWriteStream : Stream
{
    private readonly Stream _inner;
    private long _written;

    /// <summary>Initializes a new instance of the <see cref="LimitedWriteStream"/> class.</summary>
    /// <param name="inner">The underlying writable stream that receives the data.</param>
    /// <param name="maxBytes">The maximum number of bytes that may be written before an exception is thrown. Must be positive.</param>
    public LimitedWriteStream(Stream inner, long maxBytes)
    {
        ArgumentNullException.ThrowIfNull(inner);

        if (maxBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytes), maxBytes, "The byte budget must be positive.");
        }

        _inner = inner;
        MaxBytes = maxBytes;
    }

    /// <summary>Gets the maximum number of bytes that may be written to the stream.</summary>
    public long MaxBytes { get; }

    /// <summary>Gets the number of bytes that have been written so far.</summary>
    public long BytesWritten => _written;

    /// <inheritdoc/>
    public override bool CanRead => false;

    /// <inheritdoc/>
    public override bool CanSeek => false;

    /// <inheritdoc/>
    public override bool CanWrite => true;

    /// <inheritdoc/>
    public override long Length => _inner.Length;

    /// <inheritdoc/>
    public override long Position
    {
        get => _inner.Position;
        set => throw new NotSupportedException();
    }

    /// <inheritdoc/>
    public override void Flush() => _inner.Flush();

    /// <inheritdoc/>
    public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count)
    {
        EnsureCapacity(count);
        _inner.Write(buffer, offset, count);
        _written += count;
    }

    /// <inheritdoc/>
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        EnsureCapacity(buffer.Length);
        _inner.Write(buffer);
        _written += buffer.Length;
    }

    /// <inheritdoc/>
    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        EnsureCapacity(count);
        await _inner.WriteAsync(buffer.AsMemory(offset, count), cancellationToken);
        _written += count;
    }

    /// <inheritdoc/>
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        EnsureCapacity(buffer.Length);
        await _inner.WriteAsync(buffer, cancellationToken);
        _written += buffer.Length;
    }

    private void EnsureCapacity(int additionalBytes)
    {
        var projected = _written + additionalBytes;

        if (projected > MaxBytes)
        {
            throw new ResourceSizeLimitExceededException(MaxBytes);
        }
    }
}
