namespace CrestApps.Core.Support;

public static class StreamExtensions
{
    public static byte[] ReadAllBytes(this Stream instream)
    {
        ArgumentNullException.ThrowIfNull(instream);

        if (instream is MemoryStream stream)
        {
            return stream.ToArray();
        }

        if (instream.CanSeek)
        {
            instream.Position = 0;
        }

        using var memoryStream = new MemoryStream();
        instream.CopyTo(memoryStream);

        return memoryStream.ToArray();
    }
}
