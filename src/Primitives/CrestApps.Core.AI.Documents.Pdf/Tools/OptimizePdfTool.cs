using System.Globalization;
using System.Text;
using CrestApps.Core.AI.Documents.Pdf.Editing;
using CrestApps.Core.AI.Documents.Pdf.Workspace;
using PdfSharp.Pdf;

namespace CrestApps.Core.AI.Documents.Pdf.Tools;

/// <summary>
/// Makes a PDF smaller without changing what it shows, and reports the size before and after.
/// </summary>
internal sealed class OptimizePdfTool : PdfToolBase
{
    /// <summary>
    /// The tool name.
    /// </summary>
    public const string TheName = PdfToolNames.OptimizePdf;

    private const string Schema = $$"""
        {
          "type": "object",
          "properties": {
            {{PdfToolSchemas.Pdf}},
            {{PdfToolSchemas.Password}},
            {{PdfToolSchemas.SaveAs}},
            "level": {
              "type": "string",
              "enum": ["standard", "maximum"],
              "description": "standard (default) removes thumbnails, merges duplicate objects and compresses uncompressed streams; maximum also recompresses every stream at the best level and removes metadata."
            },
            "remove_metadata": { "type": "boolean", "description": "Remove the title, author and other document information, XMP metadata and application data (keeping the producer, dates and any PDF/A identification). Defaults to false, true at level maximum." },
            "remove_thumbnails": { "type": "boolean", "description": "Remove embedded page thumbnails; viewers draw their own. Defaults to true." },
            "deduplicate": { "type": "boolean", "description": "Merge identical images, fonts and forms stored more than once. Defaults to true." },
            "compress_streams": { "type": "boolean", "description": "Compress streams stored without compression. Defaults to true." }
          },
          "required": [],
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="OptimizePdfTool"/> class.
    /// </summary>
    public OptimizePdfTool()
        : base(Schema)
    {
    }

    /// <summary>
    /// Gets the name.
    /// </summary>
    public override string Name => TheName;

    /// <summary>
    /// Gets the description.
    /// </summary>
    public override string Description => "Compresses and optimizes a PDF without changing what it shows: removes page thumbnails, merges duplicate images, fonts and forms, compresses uncompressed streams and, at level maximum, recompresses every stream and removes metadata. Reports the size before and after, and keeps the original when the result is not smaller. Images are not downsampled or re-encoded.";

    /// <summary>
    /// Optimizes the PDF.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="context">The PDF context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    protected override async Task<string> ExecuteAsync(PdfToolArguments arguments, PdfToolContext context, CancellationToken cancellationToken)
    {
        var level = (arguments.GetString("level") ?? "standard").Trim().ToLowerInvariant();

        if (level is not ("standard" or "maximum"))
        {
            throw new PdfToolException($"'{level}' is not a level. Use standard or maximum.");
        }

        var maximum = level == "maximum";
        var removeMetadata = arguments.GetBoolean("remove_metadata") ?? maximum;
        var removeThumbnails = arguments.GetBoolean("remove_thumbnails") ?? true;
        var deduplicate = arguments.GetBoolean("deduplicate") ?? true;
        var compress = arguments.GetBoolean("compress_streams") ?? true;
        var password = arguments.GetString("password");

        return await context.MutateAsync(async state =>
        {
            var target = context.FindPdf(state, arguments.Pdf());
            var bytes = await context.ReadPdfAsync(target, cancellationToken);

            using var document = PdfFiles.OpenForEditing(bytes, password);

            var signatures = PdfObjects.DescribeBrokenSignatures(document);
            var sync = PdfMetadataSync.Capture(document, keepOtherProperties: !removeMetadata);
            var done = new List<string>();

            if (removeThumbnails)
            {
                AddCount(done, PdfStreamOptimizer.RemoveThumbnails(document), "removed {0} page thumbnail(s)");
            }

            if (removeMetadata)
            {
                PdfStreamOptimizer.RemoveMetadata(document);
                done.Add("removed the document information, XMP metadata and application data, keeping the producer, the dates and any PDF/A or PDF/UA identification");
            }

            if (deduplicate)
            {
                AddCount(done, PdfStreamOptimizer.Deduplicate(document), "merged {0} duplicate object(s) (images, fonts or forms stored more than once)");
            }

            if (compress)
            {
                document.Options.NoCompression = false;
                document.Options.CompressContentStreams = true;
                document.Options.FlateEncodeMode = PdfFlateEncodeMode.BestCompression;
                AddCount(done, PdfStreamOptimizer.CompressUncompressed(document), "compressed {0} uncompressed stream(s)");
            }

            if (maximum)
            {
                AddCount(done, PdfStreamOptimizer.Recompress(document), "recompressed {0} stream(s) at the best level");
            }

            var saved = sync.Save(document, context.TimeProvider.GetUtcNow());
            var before = bytes.LongLength;
            var after = saved.LongLength;
            var answer = new StringBuilder();

            if (after >= before)
            {
                answer.AppendLine($"The optimized file would be {PdfPropertiesToolText.Size(after)}, not smaller than the {PdfPropertiesToolText.Size(before)} of {target.Describe()}, so nothing was saved and the PDF was left as it is.");
                answer.AppendLine(done.Count == 0
                    ? "It has no thumbnails, duplicate objects or uncompressed streams to remove."
                    : "Tried: " + string.Join("; ", done) + ".");
                answer.AppendLine("Images were not downsampled or re-encoded (no image codec is available here); a PDF made mostly of scans or photos cannot be made much smaller by this tool.");

                return answer.ToString().TrimEnd();
            }

            var working = await context.SaveWorkingFileAsync(state, target, arguments.GetString("save_as"), saved, $"Optimized ({level})", cancellationToken);
            var saving = (before - after) * 100d / before;

            answer.AppendLine(PdfPropertiesToolText.Saved(target, working));
            answer.AppendLine(string.Create(CultureInfo.InvariantCulture, $"Size: {PdfPropertiesToolText.Size(before)} → {PdfPropertiesToolText.Size(after)} (−{saving:0.0}%)."));

            if (done.Count > 0)
            {
                answer.AppendLine("Done: " + string.Join("; ", done) + ".");
            }

            answer.AppendLine("Images were not downsampled or re-encoded (no image codec is available here), so what they take is unchanged.");

            PdfPropertiesToolText.AppendNotes(answer, removeMetadata ? null : sync.DescribeConformance(), signatures, PdfProtection.DescribeDropped(bytes, password));

            return answer.ToString().TrimEnd();
        }, cancellationToken);
    }

    private static void AddCount(List<string> done, int count, string format)
    {
        if (count > 0)
        {
            done.Add(string.Format(CultureInfo.InvariantCulture, format, count));
        }
    }
}
