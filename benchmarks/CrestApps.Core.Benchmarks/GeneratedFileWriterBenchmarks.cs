using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using CrestApps.Core.AI.Documents.Generation;
using CrestApps.Core.AI.Documents.OpenXml.Services;
using CrestApps.Core.AI.Documents.Pdf.Services;

namespace CrestApps.Core.Benchmarks;

/// <summary>
/// Measures generated document writing for allocation-sensitive payloads.
/// This class must remain unsealed because BenchmarkDotNet generates a derived benchmark type.
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
public class GeneratedFileWriterBenchmarks
{
    private GeneratedFileContent _content;
    private IGeneratedFileWriter _writer;
    private MemoryStream _destination;

    /// <summary>
    /// Gets or sets the generated document format.
    /// </summary>
    [Params("Word", "Pdf")]
    public string Format { get; set; }

    /// <summary>
    /// Gets or sets the number of body lines.
    /// </summary>
    [Params(100, 1000)]
    public int LineCount { get; set; }

    /// <summary>
    /// Creates the writer and representative document content.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _writer = Format == "Word"
            ? new WordGeneratedFileWriter()
            : new PdfGeneratedFileWriter();

        _content = new GeneratedFileContent
        {
            Title = "Performance report",
            Text = CreateBodyText(LineCount),
        };

        _destination = new MemoryStream();
    }

    /// <summary>
    /// Resets the destination before each measured write.
    /// </summary>
    [IterationSetup]
    public void ResetDestination()
    {
        _destination.SetLength(0);
        _destination.Position = 0;
    }

    /// <summary>
    /// Writes the generated document.
    /// </summary>
    /// <returns>A task representing the write operation.</returns>
    [Benchmark]
    public Task WriteAsync()
    {
        return _writer.WriteAsync(_content, _destination);
    }

    /// <summary>
    /// Creates representative generated document text.
    /// </summary>
    /// <param name="lineCount">The number of body lines.</param>
    /// <returns>The generated body text.</returns>
    private static string CreateBodyText(int lineCount)
    {
        var builder = new StringBuilder(lineCount * 64);

        for (var i = 0; i < lineCount; i++)
        {
            builder.Append("Line ");
            builder.Append(i);
            builder.Append(": representative generated document content.\r\n");
        }

        return builder.ToString();
    }
}
