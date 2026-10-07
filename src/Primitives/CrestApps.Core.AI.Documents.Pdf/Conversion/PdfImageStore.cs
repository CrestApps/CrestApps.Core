using CrestApps.Core.AI.Documents.Pdf.Composition;

namespace CrestApps.Core.AI.Documents.Pdf.Conversion;

/// <summary>
/// Keeps the pictures of a converted file in the PDF workspace, within the limits the agent works to, and
/// counts the ones it could not keep.
/// </summary>
internal sealed class PdfImageStore
{
    private readonly Func<byte[], string, string, string, CancellationToken, Task<string>> _store;
    private readonly long _maxBytes;
    private readonly int _maxImages;
    private int _stored;
    private int _unsupported;
    private int _tooLarge;
    private int _overLimit;

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfImageStore"/> class.
    /// </summary>
    /// <param name="store">Stores a picture — its bytes, media type, file name and description — and returns the source a block names it by.</param>
    /// <param name="maxBytes">The largest picture kept.</param>
    /// <param name="maxImages">The most pictures kept.</param>
    public PdfImageStore(Func<byte[], string, string, string, CancellationToken, Task<string>> store, long maxBytes, int maxImages)
    {
        _store = store;
        _maxBytes = maxBytes;
        _maxImages = maxImages;
    }

    /// <summary>
    /// Gets what could not be kept, as sentences for the answer.
    /// </summary>
    public IEnumerable<string> Warnings
    {
        get
        {
            if (_unsupported > 0)
            {
                yield return $"{_unsupported} picture(s) are in a format the PDF cannot hold (only JPEG, PNG, GIF and BMP are), such as EMF or SVG drawings, and were left out.";
            }

            if (_tooLarge > 0)
            {
                yield return $"{_tooLarge} picture(s) were larger than {_maxBytes / (1024 * 1024)} MB and were left out.";
            }

            if (_overLimit > 0)
            {
                yield return $"Only the first {_maxImages} pictures were kept; {_overLimit} more were left out.";
            }
        }
    }

    /// <summary>
    /// Keeps a picture.
    /// </summary>
    /// <param name="bytes">The picture.</param>
    /// <param name="mediaType">Its media type.</param>
    /// <param name="fileName">Its file name.</param>
    /// <param name="description">What it shows, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The source a block names it by, or <see langword="null"/> when it was not kept.</returns>
    public async Task<string> StoreAsync(byte[] bytes, string mediaType, string fileName, string description, CancellationToken cancellationToken)
    {
        if (bytes is null || !PdfImageInfo.TryRead(bytes, out _, out _, out _))
        {
            _unsupported++;

            return null;
        }

        if (bytes.LongLength > _maxBytes)
        {
            _tooLarge++;

            return null;
        }

        if (_stored >= _maxImages)
        {
            _overLimit++;

            return null;
        }

        _stored++;

        return await _store(bytes, mediaType, fileName, description, cancellationToken);
    }
}
