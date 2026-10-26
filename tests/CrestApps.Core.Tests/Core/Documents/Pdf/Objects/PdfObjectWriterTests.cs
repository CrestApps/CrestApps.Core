using System.Text;
using CrestApps.Core.AI.Documents.Pdf.Objects;
using CrestApps.Core.Tests.Core.Documents.Pdf.Reading;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using PigDocument = UglyToad.PdfPig.PdfDocument;

namespace CrestApps.Core.Tests.Core.Documents.Pdf.Objects;

public sealed class PdfObjectWriterTests
{
    [Fact]
    public void Save_NewDocument_IsReadByPdfPig()
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        var font = HelveticaFont(document);

        page.Resources.Elements["/Font"] = new PdfDictionary(document);
        page.Resources.Elements.GetDictionary("/Font").Elements.SetReference("/F1", font);
        page.Contents.AppendContent().CreateStream(Encoding.ASCII.GetBytes("BT /F1 12 Tf 72 720 Td (Hello \\(world\\)) Tj ET"));
        document.Info.Title = "Grüße – a test";
        document.Language = "de-DE";

        var bytes = document.Save();

        using var pig = PigDocument.Open(bytes);

        Assert.Equal(1, pig.NumberOfPages);
        Assert.Equal("Hello (world)", pig.GetPage(1).Text);
        Assert.Equal("Grüße – a test", pig.Information.Title);
        Assert.Equal(PdfPage.A4Width, pig.GetPage(1).Width, 1);
    }

    [Fact]
    public void Open_ChangeAndSave_KeepsContentAndWritesTheChange()
    {
        var source = PigBuiltPdf("First page", "Second page", "Third page");

        using var document = PdfDocument.Open(source);

        Assert.Equal(3, document.PageCount);

        document.Pages.MovePage(2, 0);
        document.Pages.RemoveAt(2);
        document.Pages[0].Rotate = 90;
        document.Info.Author = "Tester";

        using var pig = PigDocument.Open(document.Save());

        Assert.Equal(2, pig.NumberOfPages);
        Assert.Equal("Third page", pig.GetPage(1).Text);
        Assert.Equal("First page", pig.GetPage(2).Text);
        Assert.Equal(90, pig.GetPage(1).Rotation.Value);
        Assert.Equal("Tester", pig.Information.Author);
    }

    [Fact]
    public void Pages_FromAnotherDocument_AreCopiedWithTheirResources()
    {
        using var first = PdfDocument.Open(PigBuiltPdf("Alpha"));
        using var second = PdfDocument.Open(PigBuiltPdf("Beta", "Gamma"));

        first.Pages.Add(second.Pages[1]);
        first.Pages.Insert(0, second.Pages[0]);

        using var pig = PigDocument.Open(first.Save());

        Assert.Equal(["Beta", "Alpha", "Gamma"], pig.GetPages().Select(page => page.Text));
    }

    [Fact]
    public void Open_NestedPageTree_GivesEveryPageWhatItInherits()
    {
        var file = new StringBuilder();
        var offsets = new List<int>();

        void Add(string body)
        {
            offsets.Add(file.Length);
            file.Append(offsets.Count).Append(" 0 obj\n").Append(body).Append("\nendobj\n");
        }

        file.Append("%PDF-1.4\n");
        Add("<< /Type /Catalog /Pages 2 0 R >>");
        Add("<< /Type /Pages /Kids [3 0 R] /Count 2 /MediaBox [0 0 300 400] /Rotate 180 >>");
        Add("<< /Type /Pages /Parent 2 0 R /Kids [4 0 R 5 0 R] /Count 2 /Resources << /Font << /F1 6 0 R >> >> >>");
        Add("<< /Type /Page /Parent 3 0 R /Contents 7 0 R >>");
        Add("<< /Type /Page /Parent 3 0 R /MediaBox [0 0 500 200] /Contents 7 0 R >>");
        Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
        Add("<< /Length 33 >>\nstream\nBT /F1 10 Tf 20 20 Td (Kid) Tj ET\nendstream");

        var xref = file.Length;

        file.Append("xref\n0 ").Append(offsets.Count + 1).Append("\n0000000000 65535 f\r\n");

        foreach (var offset in offsets)
        {
            file.Append(offset.ToString("D10")).Append(" 00000 n\r\n");
        }

        file.Append("trailer\n<< /Size ").Append(offsets.Count + 1).Append(" /Root 1 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF\n");

        using var document = PdfDocument.Open(Encoding.ASCII.GetBytes(file.ToString()));

        Assert.Equal(2, document.PageCount);
        Assert.Equal(300, document.Pages[0].Width);
        Assert.Equal(500, document.Pages[1].Width);
        Assert.Equal(180, document.Pages[1].Rotate);
        Assert.NotNull(document.Pages[1].Resources.Elements.GetDictionary("/Font")?.Elements.GetDictionary("/F1"));

        using var pig = PigDocument.Open(document.Save());

        Assert.Equal("Kid", pig.GetPage(2).Text);
        Assert.Equal(400, pig.GetPage(1).Height, 0);
    }

    [Fact]
    public void Strings_RoundTripTextAndBytes()
    {
        using var document = new PdfDocument();
        var dictionary = new PdfDictionary(document);

        dictionary.Elements.SetString("/Plain", "Plain (text) \\ here");
        dictionary.Elements.SetString("/Unicode", "日本語 – ✓");
        dictionary.Elements["/Binary"] = new PdfString([0x00, 0xFF, 0x10], isHex: true);
        dictionary.Elements.SetDateTime("/Date", new DateTimeOffset(2026, 10, 26, 14, 30, 5, TimeSpan.FromHours(-5)));
        document.Catalog.Elements.SetReference("/Test", dictionary);

        using var reopened = PdfDocument.Open(document.Save());
        var read = reopened.Catalog.Elements.GetDictionary("/Test");

        Assert.Equal("Plain (text) \\ here", read.Elements.GetString("/Plain"));
        Assert.Equal("日本語 – ✓", read.Elements.GetString("/Unicode"));
        Assert.Equal(new byte[] { 0x00, 0xFF, 0x10 }, ((PdfString)read.Elements["/Binary"]).Bytes);
        Assert.Equal(new DateTimeOffset(2026, 10, 26, 14, 30, 5, TimeSpan.FromHours(-5)), read.Elements.GetDateTime("/Date"));
    }

    [Fact]
    public void Streams_AreDecodedAndRecompressed()
    {
        using var document = new PdfDocument();
        var stream = new PdfDictionary(document);
        var content = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("compressible ", 200)));

        stream.CreateStream([]).SetCompressed(content);
        document.Catalog.Elements.SetReference("/Data", stream);

        using var reopened = PdfDocument.Open(document.Save());
        var read = reopened.Catalog.Elements.GetDictionary("/Data");

        Assert.True(read.Stream.Length < content.Length);
        Assert.Equal(content, read.Stream.UnfilteredValue);
    }

    [Fact]
    public void Open_KeepsTheBytesOfLiteralStrings()
    {
        // A binary literal string, as /O, /U and many /ID entries are written, and a title using a
        // PDFDocEncoding code PdfPig reads as another character.
        var file = MinimalPdf("<< /Type /Catalog /Pages 2 0 R /Binary (\\200\\201\\377\\000\\001\\(x) >>", "<< /Title (Dash \\205 here) >>");

        using var document = PdfDocument.Open(file);

        Assert.Equal(new byte[] { 0x80, 0x81, 0xFF, 0x00, 0x01, (byte)'(', (byte)'x' }, ((PdfString)document.Catalog.Elements["/Binary"]).Bytes);
        Assert.Equal("Dash – here", document.Info.Title);

        using var reopened = PdfDocument.Open(document.Save());

        Assert.Equal(new byte[] { 0x80, 0x81, 0xFF, 0x00, 0x01, (byte)'(', (byte)'x' }, ((PdfString)reopened.Catalog.Elements["/Binary"]).Bytes);
        Assert.Equal("Dash – here", reopened.Info.Title);
    }

    [Fact]
    public void Open_EncryptedFile_ReadsItDecrypted()
    {
        var protectedFile = PdfReadingFixtures.Protect(PigBuiltPdf("Secret text"), "user", "owner");

        using var document = PdfDocument.Open(protectedFile, "user");

        Assert.True(document.IsEncrypted);
        // The content is readable once decrypted: PdfPig's writer stores "Secret" as hexadecimal.
        Assert.Contains("<536563726574> Tj", Encoding.Latin1.GetString(document.Pages[0].Contents.ReadAll()), StringComparison.Ordinal);

        using var pig = PigDocument.Open(document.Save());

        Assert.False(pig.IsEncrypted);
        Assert.Equal("Secret text", pig.GetPage(1).Text);
    }

    [Fact]
    public void Save_DropsObjectsNothingUses()
    {
        using var document = PdfDocument.Open(PigBuiltPdf("Kept", "Dropped"));

        document.Pages.RemoveAt(1);

        var bytes = document.Save();

        Assert.DoesNotContain("Dropped", Encoding.Latin1.GetString(bytes), StringComparison.Ordinal);
    }

    private static byte[] MinimalPdf(string catalog, string info)
    {
        var file = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();

        void Add(string body)
        {
            offsets.Add(file.Length);
            file.Append(offsets.Count).Append(" 0 obj\n").Append(body).Append("\nendobj\n");
        }

        Add(catalog);
        Add("<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        Add("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] >>");
        Add(info);

        var xref = file.Length;

        file.Append("xref\n0 ").Append(offsets.Count + 1).Append("\n0000000000 65535 f\r\n");

        foreach (var offset in offsets)
        {
            file.Append(offset.ToString("D10")).Append(" 00000 n\r\n");
        }

        file.Append("trailer\n<< /Size ").Append(offsets.Count + 1).Append(" /Root 1 0 R /Info 4 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF\n");

        return Encoding.Latin1.GetBytes(file.ToString());
    }

    private static PdfDictionary HelveticaFont(PdfDocument document)
    {
        var font = new PdfDictionary(document);

        font.Elements.SetName("/Type", "/Font");
        font.Elements.SetName("/Subtype", "/Type1");
        font.Elements.SetName("/BaseFont", "/Helvetica");
        font.Elements.SetName("/Encoding", "/WinAnsiEncoding");

        return font;
    }

    private static byte[] PigBuiltPdf(params string[] pages)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);

        foreach (var text in pages)
        {
            builder.AddPage(PageSize.A4).AddText(text, 12, new PdfPoint(72, 720), font);
        }

        return builder.Build();
    }
}
