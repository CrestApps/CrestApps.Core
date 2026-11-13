using System.Text;

namespace CrestApps.Core.AI.Http;

/// <summary>
/// Reads the body of an HTTP response without letting its size decide how much memory is used. A server, or a page a
/// remote document points at, can answer with a body of any size, and a small compressed body can inflate to gigabytes
/// when the client decompresses it, so every body that comes from outside is read through a limit.
/// </summary>
public static class BoundedHttpContent
{
    private const int BufferSize = 81920;

    /// <summary>
    /// Sends a GET request and reads the response body, which is refused when it is larger than the limit.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="url">The address.</param>
    /// <param name="maxBytes">The most bytes to read.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The body.</returns>
    /// <exception cref="HttpRequestException">The server answered with an error status.</exception>
    /// <exception cref="HttpContentTooLargeException">The body is larger than <paramref name="maxBytes"/>.</exception>
    public static async Task<byte[]> GetBytesAsync(HttpClient client, string url, long maxBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);

        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        response.EnsureSuccessStatusCode();

        return await ReadBytesAsync(response.Content, maxBytes, cancellationToken);
    }

    /// <summary>
    /// Sends a GET request and reads the response body as text, which is refused when it is larger than the limit.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="url">The address.</param>
    /// <param name="maxBytes">The most bytes to read.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The text, decoded with the response's charset (UTF-8 when it names none).</returns>
    /// <exception cref="HttpRequestException">The server answered with an error status.</exception>
    /// <exception cref="HttpContentTooLargeException">The body is larger than <paramref name="maxBytes"/>.</exception>
    public static async Task<string> GetStringAsync(HttpClient client, string url, long maxBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);

        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        response.EnsureSuccessStatusCode();

        return await ReadStringAsync(response.Content, maxBytes, cancellationToken);
    }

    /// <summary>
    /// Reads a response body, giving up as soon as it is known to be larger than the limit: from the declared
    /// <c>Content-Length</c> before any of it is read, or from the bytes read when a body does not declare one or declares
    /// less than it sends.
    /// </summary>
    /// <param name="content">The content.</param>
    /// <param name="maxBytes">The most bytes to read.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The body.</returns>
    /// <exception cref="HttpContentTooLargeException">The body is larger than <paramref name="maxBytes"/>.</exception>
    public static async Task<byte[]> ReadBytesAsync(HttpContent content, long maxBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (content.Headers.ContentLength > maxBytes)
        {
            throw new HttpContentTooLargeException(maxBytes);
        }

        var (bytes, truncated) = await ReadAsync(content, maxBytes, truncate: false, cancellationToken);

        return truncated ? throw new HttpContentTooLargeException(maxBytes) : bytes;
    }

    /// <summary>
    /// Reads a response body as text, which is refused when it is larger than the limit.
    /// </summary>
    /// <param name="content">The content.</param>
    /// <param name="maxBytes">The most bytes to read.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The text, decoded with the response's charset (UTF-8 when it names none).</returns>
    /// <exception cref="HttpContentTooLargeException">The body is larger than <paramref name="maxBytes"/>.</exception>
    public static async Task<string> ReadStringAsync(HttpContent content, long maxBytes, CancellationToken cancellationToken)
    {
        var bytes = await ReadBytesAsync(content, maxBytes, cancellationToken);

        return Decode(content, bytes);
    }

    /// <summary>
    /// Reads a response body as text, stopping at the limit and reporting that it did, for a caller that would cut the
    /// text anyway.
    /// </summary>
    /// <param name="content">The content.</param>
    /// <param name="maxBytes">The most bytes to read.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The text read, and whether the body was longer than what was read.</returns>
    public static async Task<(string Text, bool Truncated)> ReadStringTruncatedAsync(HttpContent content, long maxBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        var (bytes, truncated) = await ReadAsync(content, maxBytes, truncate: true, cancellationToken);

        return (Decode(content, bytes), truncated || content.Headers.ContentLength > maxBytes);
    }

    private static async Task<(byte[] Bytes, bool Truncated)> ReadAsync(HttpContent content, long maxBytes, bool truncate, CancellationToken cancellationToken)
    {
        await using var body = await content.ReadAsStreamAsync(cancellationToken);
        using var destination = new MemoryStream();
        var buffer = new byte[BufferSize];
        int read;

        while ((read = await body.ReadAsync(buffer, cancellationToken)) > 0)
        {
            var remaining = maxBytes - destination.Length;

            if (read > remaining)
            {
                if (truncate)
                {
                    destination.Write(buffer, 0, (int)remaining);
                }

                return (truncate ? destination.ToArray() : [], true);
            }

            destination.Write(buffer, 0, read);
        }

        return (destination.ToArray(), false);
    }

    private static string Decode(HttpContent content, byte[] bytes)
    {
        var encoding = Encoding.UTF8;
        var charset = content.Headers.ContentType?.CharSet;

        if (!string.IsNullOrWhiteSpace(charset))
        {
            try
            {
                encoding = Encoding.GetEncoding(charset.Trim('"'));
            }
            catch (ArgumentException)
            {
                // An unknown charset reads as UTF-8, as HttpContent does.
            }
        }

        // A byte order mark is not part of the text. HttpContent.ReadAsStringAsync drops it, and a caller that parses what
        // it reads (JSON, a page) fails on a leading U+FEFF.
        var preamble = encoding.GetPreamble();
        var skip = preamble.Length > 0 && bytes.AsSpan().StartsWith(preamble) ? preamble.Length : 0;

        return encoding.GetString(bytes, skip, bytes.Length - skip);
    }
}
