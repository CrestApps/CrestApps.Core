using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CrestApps.Core.AI.Documents.OpenXml.Word;
using CrestApps.Core.AI.Documents.Word.Tools;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CrestApps.Core.Tests.Core.Documents.Word;

public sealed class WordPreviewSecurityTests
{
    private const string Payload = "FF0000\" onload=\"alert(1)";

    [Fact]
    public async Task PreviewWord_UploadWithMarkupInColors_DrawsNoInjectedMarkup()
    {
        using var host = new WordToolTestHost();

        await host.UploadAsync("hostile.docx", BuildHostileDocument());

        var answer = await host.InvokeAsync(new PreviewWordTool(), new { document = "hostile.docx" });
        var marker = Regex.Match(answer, @"\[fig:\d+\]").Value;

        Assert.False(string.IsNullOrEmpty(marker), answer);

        var (_, bytes) = await host.ReadMarkerAsync(marker);
        var svg = Encoding.UTF8.GetString(bytes);

        // The picture parses as one SVG element tree, and no attribute carries the payload.
        var root = XElement.Parse(svg);

        Assert.DoesNotContain(root.DescendantsAndSelf().SelectMany(element => element.Attributes()), attribute => attribute.Name.LocalName.StartsWith("on", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("alert", svg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Visible text", svg, StringComparison.Ordinal);
    }

    private static byte[] BuildHostileDocument()
    {
        using var package = WordPackage.Create(new WordDesign());

        package.MainPart.Document.DocumentBackground = new DocumentBackground { Color = Payload };
        package.Body.PrependChild(new Paragraph(
            new ParagraphProperties(new Shading { Val = ShadingPatternValues.Clear, Fill = Payload }),
            new Run(new RunProperties(new Color { Val = Payload }, new Shading { Val = ShadingPatternValues.Clear, Fill = Payload }), new Text("Visible text"))));

        return package.Save();
    }
}
