using System.Globalization;
using CrestApps.Core.AI.Documents.Pdf;
using CrestApps.Core.AI.Documents.Pdf.Composition;
using CrestApps.Core.AI.Documents.Pdf.Intelligence;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Tests.Core.Documents.Pdf.Intelligence;

/// <summary>
/// Builds the PDFs the intelligence tool tests work on, in code, and the hosts they run in.
/// </summary>
internal static class PdfFixtures
{
    private static readonly PdfDocumentComposer _composer = new(Options.Create(new PdfCompositionOptions()), TimeProvider.System);

    /// <summary>
    /// Builds a text PDF, one page per entry, each page made of the given paragraphs.
    /// </summary>
    /// <param name="pages">The paragraphs of each page.</param>
    /// <returns>The PDF.</returns>
    public static async Task<byte[]> TextAsync(params string[][] pages)
    {
        var blocks = new List<PdfBlockDefinition>();

        for (var index = 0; index < pages.Length; index++)
        {
            if (index > 0)
            {
                blocks.Add(new PdfBlockDefinition { Id = "break" + index.ToString(CultureInfo.InvariantCulture), Type = "page_break" });
            }

            foreach (var paragraph in pages[index])
            {
                blocks.Add(new PdfBlockDefinition { Id = "b" + blocks.Count.ToString(CultureInfo.InvariantCulture), Type = "paragraph", Text = paragraph });
            }
        }

        return await ComposeAsync(blocks, new Dictionary<string, PdfImageData>());
    }

    /// <summary>
    /// Builds an image-only PDF, as a scanner makes: one picture per page and no text.
    /// </summary>
    /// <param name="images">The PNG of each page.</param>
    /// <returns>The PDF.</returns>
    public static async Task<byte[]> ScannedAsync(params byte[][] images)
    {
        var blocks = new List<PdfBlockDefinition>();
        var sources = new Dictionary<string, PdfImageData>(StringComparer.Ordinal);

        for (var index = 0; index < images.Length; index++)
        {
            if (index > 0)
            {
                blocks.Add(new PdfBlockDefinition { Id = "break" + index.ToString(CultureInfo.InvariantCulture), Type = "page_break" });
            }

            var name = "scan" + index.ToString(CultureInfo.InvariantCulture) + ".png";

            sources[name] = new PdfImageData(images[index], "image/png", name);
            blocks.Add(new PdfBlockDefinition { Id = "img" + index.ToString(CultureInfo.InvariantCulture), Type = "image", Image = new PdfImageDefinition { Source = name, WidthPercent = 80 } });
        }

        return await ComposeAsync(blocks, sources);
    }

    /// <summary>
    /// Builds a one-page PDF with a paragraph and a captioned figure.
    /// </summary>
    /// <param name="text">The paragraph.</param>
    /// <param name="image">The figure's PNG.</param>
    /// <param name="caption">The figure's caption.</param>
    /// <returns>The PDF.</returns>
    public static async Task<byte[]> FigureAsync(string text, byte[] image, string caption)
    {
        var blocks = new List<PdfBlockDefinition>
        {
            new() { Id = "b1", Type = "paragraph", Text = text },
            new() { Id = "b2", Type = "image", Image = new PdfImageDefinition { Source = "chart.png", Caption = caption, WidthPercent = 60 } },
        };

        return await ComposeAsync(blocks, new Dictionary<string, PdfImageData>
        {
            ["chart.png"] = new PdfImageData(image, "image/png", "chart.png"),
        });
    }

    /// <summary>
    /// Creates a tool host whose PDF tools use the given chat clients as their models.
    /// </summary>
    /// <param name="text">The text model, or <see langword="null"/> for none.</param>
    /// <param name="vision">The vision model, or <see langword="null"/> for none.</param>
    /// <param name="configure">Changes the agent's limits.</param>
    /// <returns>The host.</returns>
    public static PdfToolTestHost Host(IChatClient text = null, IChatClient vision = null, Action<PdfAgentOptions> configure = null)
    {
        return new PdfToolTestHost(configure: services =>
        {
            if (text is not null)
            {
                services.AddKeyedSingleton(PdfModelClient.TextClientKey, text);
            }

            if (vision is not null)
            {
                services.AddKeyedSingleton(PdfModelClient.VisionClientKey, vision);
            }

            if (configure is not null)
            {
                services.Configure(configure);
            }
        });
    }

    private static async Task<byte[]> ComposeAsync(List<PdfBlockDefinition> blocks, Dictionary<string, PdfImageData> images)
    {
        var definition = new PdfDocumentDefinition
        {
            Sections =
            [
                new PdfSectionDefinition { Id = "s1", Blocks = blocks },
            ],
        };

        var result = await _composer.ComposeAsync(definition, new DictionaryImageSource(images), TestContext.Current.CancellationToken);

        return result.Bytes;
    }

    /// <summary>
    /// Resolves pictures from a dictionary.
    /// </summary>
    private sealed class DictionaryImageSource : IPdfImageSource
    {
        private readonly Dictionary<string, PdfImageData> _images;

        /// <summary>
        /// Initializes a new instance of the <see cref="DictionaryImageSource"/> class.
        /// </summary>
        /// <param name="images">The pictures by source name.</param>
        public DictionaryImageSource(Dictionary<string, PdfImageData> images)
        {
            _images = images;
        }

        /// <summary>
        /// Resolves a picture.
        /// </summary>
        /// <param name="source">The source name.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The picture, or <see langword="null"/>.</returns>
        public Task<PdfImageData> ResolveAsync(string source, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_images.GetValueOrDefault(source));
        }
    }
}
