using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using CrestApps.Core.AI.Documents.Pdf.Analysis;
using CrestApps.Core.AI.Documents.Pdf.Tools;
using UglyToad.PdfPig;
using PdfObjects = CrestApps.Core.AI.Documents.Pdf.Objects;

namespace CrestApps.Core.Tests.Core.Documents.Pdf;

public sealed class PdfRedactionTests
{
    private const string StandardFontContent =
        "BT /F1 12 Tf 72 720 Td (Customer SSN: 123-45-6789 on file.) Tj 0 -16 Td [(Email: jane) -20 (.doe@example.com here)] TJ ET";

    [Fact]
    public async Task FindThenRedactCategories_RemovesTheValuesFromTheText()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("record.pdf", PdfToolFixtures.StandardFontPdf(StandardFontContent));

        var found = await host.InvokeAsync(new FindPdfSensitiveDataTool(), new { });

        Assert.Contains("us_ssn: •••-••-6789", found, StringComparison.Ordinal);
        Assert.Contains("email: j•••@example.com", found, StringComparison.Ordinal);
        Assert.DoesNotContain("123-45-6789", found, StringComparison.Ordinal);

        var redacted = await host.InvokeAsync(new RedactPdfTool(), new { categories = new[] { "all_sensitive" }, overlay_text = "X" });

        Assert.Contains("Verified", redacted, StringComparison.Ordinal);

        using var pdf = PdfDocument.Open(await host.ReadWorkingPdfAsync("record"));
        var text = string.Concat(pdf.GetPage(1).Letters.Select(letter => letter.Value));

        Assert.DoesNotContain("6789", text, StringComparison.Ordinal);
        Assert.DoesNotContain("example.com", text, StringComparison.Ordinal);

        // The words around the removed values are untouched.
        Assert.Contains("CustomerSSN:", text.Replace(" ", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("onfile.", text.Replace(" ", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("here", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RedactPhrase_InCompositeFontDocument_KeepsNeighboursInPlace()
    {
        using var host = new PdfToolTestHost();
        var original = await PdfToolFixtures.SimplePdfAsync("The merger with Northwind closes in March according to plan.");
        await host.UploadAsync("memo.pdf", original);

        double BeforeX(PdfDocument document, string word)
        {
            return document.GetPage(1).GetWords().First(candidate => candidate.Text.StartsWith(word, StringComparison.Ordinal)).BoundingBox.Left;
        }

        double originalPosition;

        using (var before = PdfDocument.Open(original))
        {
            originalPosition = BeforeX(before, "closes");
        }

        var redacted = await host.InvokeAsync(new RedactPdfTool(), new { texts = new[] { "Northwind" } });

        Assert.Contains("Verified", redacted, StringComparison.Ordinal);

        using var after = PdfDocument.Open(await host.ReadWorkingPdfAsync("memo"));
        var words = after.GetPage(1).GetWords().Select(word => word.Text).ToList();

        Assert.DoesNotContain(words, word => word.Contains("Northwind", StringComparison.Ordinal));
        Assert.Contains("closes", words);
        Assert.Equal(originalPosition, BeforeX(after, "closes"), 1);
    }

    [Fact]
    public async Task RedactAreaAndWholePage_RemovesEverythingThere()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("two.pdf", await PdfToolFixtures.SimplePdfAsync("Keep this first line", "Secret second page"));

        var result = await host.InvokeAsync(new RedactPdfTool(), new { whole_pages = "2" });

        Assert.Contains("page 2", result, StringComparison.Ordinal);

        using var pdf = PdfDocument.Open(await host.ReadWorkingPdfAsync("two"));

        Assert.DoesNotContain(pdf.GetPage(2).Letters, letter => !string.IsNullOrWhiteSpace(letter.Value));
        Assert.Contains("Keep", string.Join(' ', pdf.GetPage(1).GetWords().Select(word => word.Text)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RedactPdf_NothingFound_ChangesNothing()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("clean.pdf", await PdfToolFixtures.SimplePdfAsync("Nothing to see"));

        var result = await host.InvokeAsync(new RedactPdfTool(), new { texts = new[] { "absent phrase" } });

        Assert.Contains("nothing was redacted", result, StringComparison.Ordinal);
        Assert.Empty((await host.LoadWorkspaceAsync()).Documents);
    }

    [Fact]
    public async Task RedactFlattenedForm_LeavesTheValuesNowhereInTheFile()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("signup.pdf", await PdfToolFixtures.SimplePdfAsync("Signup form"));

        await host.InvokeAsync(new EditPdfFormTool(), new
        {
            changes = new object[]
            {
                new { action = "add", type = "text", name = "email", page = 1, x = 150, y = 150 },
                new { action = "add", type = "text", name = "phone", page = 1, x = 150, y = 180 },
            },
        });
        await host.InvokeAsync(new FillPdfFormTool(), new { values = new Dictionary<string, object> { ["email"] = "jane.doe@example.com", ["phone"] = "555-0100" } });
        await host.InvokeAsync(new FlattenPdfTool(), new { scope = "forms" });

        Assert.Contains("555-0100", ReadEveryStream(await host.ReadWorkingPdfAsync("signup")), StringComparison.Ordinal);

        var redacted = await host.InvokeAsync(new RedactPdfTool(), new { texts = new[] { "jane.doe@example.com", "555-0100" } });

        Assert.Contains("Verified", redacted, StringComparison.Ordinal);

        // Each flattened field is a form XObject. The page now draws a rewritten copy of it, and the original,
        // which still holds the value, must not be written to the file beside it.
        var file = ReadEveryStream(await host.ReadWorkingPdfAsync("signup"));

        Assert.DoesNotContain("jane.doe", file, StringComparison.Ordinal);
        Assert.DoesNotContain("555-0100", file, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RedactPage_RemovesItsImageFromTheFile()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("photo.pdf", ImagePdf(pages: 1, shareResources: false));

        await host.InvokeAsync(new RedactPdfTool(), new { whole_pages = "1" });

        var bytes = await host.ReadWorkingPdfAsync("photo");

        using var pdf = PdfDocument.Open(bytes);

        Assert.Empty(pdf.GetPage(1).GetImages());
        Assert.DoesNotMatch(@"/Subtype\s*/Image\b", Encoding.Latin1.GetString(bytes));
    }

    [Fact]
    public async Task RedactPage_KeepsTheImageOfAPageSharingItsResources()
    {
        using var host = new PdfToolTestHost();
        await host.UploadAsync("photos.pdf", ImagePdf(pages: 2, shareResources: true));

        await host.InvokeAsync(new RedactPdfTool(), new { whole_pages = "1" });

        using var pdf = PdfDocument.Open(await host.ReadWorkingPdfAsync("photos"));

        // The image comes off the redacted page's own copy of the resources, not off the dictionary both pages use.
        Assert.Empty(pdf.GetPage(1).GetImages());
        Assert.Single(pdf.GetPage(2).GetImages());
    }

    [Theory]
    [InlineData("4242 4242 4242 4242", true)]
    [InlineData("4242 4242 4242 4241", false)]
    public void PatternLibrary_ChecksCardNumbers(string value, bool valid)
    {
        Assert.Equal(valid, PdfPatternLibrary.IsLuhnValid(value));
    }

    [Theory]
    [InlineData("GB82 WEST 1234 5698 7654 32", true)]
    [InlineData("GB82 WEST 1234 5698 7654 33", false)]
    public void PatternLibrary_ChecksIbans(string value, bool valid)
    {
        Assert.Equal(valid, PdfPatternLibrary.IsIbanValid(value));
    }

    [Fact]
    public void PatternLibrary_FindsAndMasksValues()
    {
        var matches = PdfPatternLibrary.Find("Call 555-123-4567 or mail ops@contoso.com; SSN 000-12-3456 is not valid, 219-09-9999 is.");

        Assert.Contains(matches, match => match.Kind == PdfPatternLibrary.Phone);
        Assert.Contains(matches, match => match.Kind == PdfPatternLibrary.Email && match.Value == "ops@contoso.com");
        Assert.DoesNotContain(matches, match => match.Value == "000-12-3456" && match.Kind == PdfPatternLibrary.UsSocialSecurityNumber);
        Assert.Contains(matches, match => match.Value == "219-09-9999" && match.Kind == PdfPatternLibrary.UsSocialSecurityNumber);
        Assert.Equal("•••-••-9999", PdfPatternLibrary.Mask(PdfPatternLibrary.UsSocialSecurityNumber, "219-09-9999"));
    }

    [Theory]
    [InlineData("Signed on 2026-03-15 by the buyer.", "2026-03-15")]
    [InlineData("Signed on 15.03.2026 by the buyer.", "15.03.2026")]
    public void PatternLibrary_ReadsNumericDatesAsDates(string text, string date)
    {
        var match = Assert.Single(PdfPatternLibrary.Find(text, [PdfPatternLibrary.Date, PdfPatternLibrary.Phone]));

        Assert.Equal(PdfPatternLibrary.Date, match.Kind);
        Assert.Equal(date, match.Value);
    }

    /// <summary>
    /// Builds pages that each draw the same small picture, either with resources of their own or all sharing one
    /// resources dictionary, as some generators write them.
    /// </summary>
    private static byte[] ImagePdf(int pages, bool shareResources)
    {
        using var document = new PdfObjects.PdfDocument();
        var image = new PdfObjects.PdfDictionary(document);

        image.Elements.SetName("/Type", "/XObject");
        image.Elements.SetName("/Subtype", "/Image");
        image.Elements.SetInteger("/Width", 2);
        image.Elements.SetInteger("/Height", 2);
        image.Elements.SetName("/ColorSpace", "/DeviceGray");
        image.Elements.SetInteger("/BitsPerComponent", 8);
        image.CreateStream([0, 255, 255, 0]);
        document.AddObject(image);

        PdfObjects.PdfDictionary shared = null;

        for (var index = 0; index < pages; index++)
        {
            var page = document.AddPage();
            page.MediaBox = new PdfObjects.PdfRectangle(0, 0, 612, 792);

            if (!shareResources || shared is null)
            {
                var xobjects = new PdfObjects.PdfDictionary(document);
                var resources = new PdfObjects.PdfDictionary(document);

                xobjects.Elements["/Im1"] = image.Reference;
                resources.Elements["/XObject"] = xobjects;
                document.AddObject(resources);
                shared = resources;
            }

            page.Elements["/Resources"] = shared.Reference;
            page.Contents.AppendContent().Stream.SetCompressed(Encoding.ASCII.GetBytes("q 100 0 0 100 72 600 cm /Im1 Do Q"));
        }

        return document.Save();
    }

    /// <summary>
    /// Reads every stream in a file, inflated where it is compressed, so a test can tell whether a value is
    /// anywhere in the file and not only on its pages.
    /// </summary>
    private static string ReadEveryStream(byte[] bytes)
    {
        var file = Encoding.Latin1.GetString(bytes);
        var text = new StringBuilder(file);

        foreach (Match match in Regex.Matches(file, @"(?<!end)stream\r?\n"))
        {
            var start = match.Index + match.Length;
            var end = file.IndexOf("endstream", start, StringComparison.Ordinal);

            try
            {
                using var zlib = new ZLibStream(new MemoryStream(bytes, start, end - start), CompressionMode.Decompress);
                using var inflated = new MemoryStream();

                zlib.CopyTo(inflated);
                text.Append(Encoding.Latin1.GetString(inflated.ToArray()));
            }
            catch (InvalidDataException)
            {
                // Not compressed: its bytes are already in the file's text.
            }
        }

        return text.ToString();
    }
}
