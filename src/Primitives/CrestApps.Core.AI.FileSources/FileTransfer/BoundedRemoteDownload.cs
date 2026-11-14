namespace CrestApps.Core.AI.FileSources.FileTransfer;

/// <summary>
/// Downloads a remote file into memory through a limit, for the FTP and SFTP clients that otherwise buffered a file of any
/// size.
/// </summary>
public static class BoundedRemoteDownload
{
    /// <summary>
    /// Runs a download into a buffer that refuses to grow past the limit.
    /// </summary>
    /// <param name="download">Writes the file to the stream it is given, and returns whether the file was found.</param>
    /// <param name="maxBytes">The most bytes to buffer.</param>
    /// <returns>The buffered file, positioned at its start, or <see langword="null"/> when it was not found.</returns>
    /// <exception cref="RemoteFileTooLargeException">The file is larger than <paramref name="maxBytes"/>.</exception>
    public static async Task<MemoryStream> OpenAsync(Func<Stream, Task<bool>> download, long maxBytes)
    {
        ArgumentNullException.ThrowIfNull(download);

        var buffer = new MemoryStream();

        try
        {
            if (!await download(new LimitedStream(buffer, maxBytes)))
            {
                await buffer.DisposeAsync();

                return null;
            }
        }
        catch (Exception ex)
        {
            await buffer.DisposeAsync();

            // The transport libraries wrap what their output stream throws, so the limit is looked for in the causes.
            for (var current = ex; current is not null; current = current.InnerException)
            {
                if (current is RemoteFileTooLargeException tooLarge)
                {
                    throw new RemoteFileTooLargeException(tooLarge.MaxBytes);
                }
            }

            throw;
        }

        buffer.Position = 0;

        return buffer;
    }

    private sealed class LimitedStream : Stream
    {
        private readonly MemoryStream _inner;
        private readonly long _maxBytes;

        public LimitedStream(MemoryStream inner, long maxBytes)
        {
            _inner = inner;
            _maxBytes = maxBytes;
        }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => throw new NotSupportedException();
        }

        public override void Flush() => _inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            Reserve(count);
            _inner.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Reserve(buffer.Length);
            _inner.Write(buffer);
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Reserve(buffer.Length);

            return _inner.WriteAsync(buffer, cancellationToken);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Reserve(count);

            return _inner.WriteAsync(buffer, offset, count, cancellationToken);
        }

        private void Reserve(int count)
        {
            if (_inner.Length + count > _maxBytes)
            {
                throw new RemoteFileTooLargeException(_maxBytes);
            }
        }
    }
}
