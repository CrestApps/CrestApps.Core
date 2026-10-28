using System.Text;
using CrestApps.Core.AI.Documents.Pdf.Composition;
using CrestApps.Core.AI.Documents.Pdf.Graphics;
using CrestApps.Core.AI.Documents.Pdf.Graphics.Fonts;
using CrestApps.Core.AI.Documents.Pdf.Graphics.Images;
using CrestApps.Core.AI.Documents.Pdf.Objects;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Graphics.Operations.SpecialGraphicsState;
using UglyToad.PdfPig.Writer;
using PigDocument = UglyToad.PdfPig.PdfDocument;

namespace CrestApps.Core.Tests.Core.Documents.Pdf.Graphics;

public sealed class PdfCanvasTests
{
    private static readonly PdfBrush _black = new(new PdfColor(0, 0, 0));

    [Fact]
    public void DrawText_EmbedsTheFontAndReadsBack()
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        var canvas = PdfCanvas.ForPage(page);
        var font = PdfFontLibrary.Get("Arial");

        canvas.DrawText("Grüße, café — naïve", font, 14, 72, 100, _black);
        canvas.Commit();

        using var pig = PigDocument.Open(document.Save());
        var letters = pig.GetPage(1).Letters;

        Assert.Equal("Grüße, café — naïve", string.Concat(letters.Select(letter => letter.Value)));

        // The text sits an inch from the left and 100 points from the top.
        Assert.Equal(72, letters[0].StartBaseLine.X, 0);
        Assert.Equal(PdfPage.A4Height - 100, letters[0].StartBaseLine.Y, 0);

        if (font.IsEmbedded)
        {
            Assert.Contains("+", letters[0].FontName, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void DrawText_WritesMoreThanTwoHundredFiftyFiveCharactersWithOneFont()
    {
        var font = PdfFontLibrary.Get("Arial");
        // Characters that share a glyph, such as the hyphen and the soft hyphen, read back as the first one.
        var characters = Enumerable.Range(0x20, 0x500)
            .Where(codePoint => !char.IsControl((char)codePoint) && !char.IsWhiteSpace((char)codePoint) && font.HasGlyph(codePoint))
            .Select(codePoint => (char)codePoint)
            .DistinctBy(character => font.Encode(character.ToString())[0].Code)
            .Take(400)
            .ToArray();

        Assert.SkipWhen(characters.Length < 300, "The installed font has too few glyphs for this test.");

        using var document = new PdfDocument();
        var page = document.AddPage();
        var canvas = PdfCanvas.ForPage(page);

        for (var line = 0; line * 40 < characters.Length; line++)
        {
            canvas.DrawText(new string([.. characters.Skip(line * 40).Take(40)]), font, 9, 36, 40 + (line * 14), _black);
        }

        canvas.Commit();

        using var pig = PigDocument.Open(document.Save());

        Assert.Equal(new string(characters), string.Concat(pig.GetPage(1).Letters.Select(letter => letter.Value)));
    }

    [Fact]
    public void DrawText_WithoutAFont_UsesAStandardFontReadersHave()
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        var canvas = PdfCanvas.ForPage(page);

        canvas.DrawText("Plain text, déjà vu", PdfFont.FromStandard("Arial", bold: true, italic: false), 12, 72, 72, _black);
        canvas.Commit();

        using var pig = PigDocument.Open(document.Save());
        var page1 = pig.GetPage(1);

        Assert.Equal("Plain text, déjà vu", string.Concat(page1.Letters.Select(letter => letter.Value)));
        Assert.Contains("Helvetica-Bold", page1.Letters[0].FontName, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("png")]
    [InlineData("translucent-png")]
    [InlineData("gif")]
    [InlineData("bmp")]
    public void DrawImage_PlacesEachSupportedFormat(string format)
    {
        var bytes = format switch
        {
            "png" => PdfTestImages.SolidPng(8, 4, 10, 20, 30),
            "translucent-png" => PdfTestImages.TranslucentPng(8, 4, 10, 20, 30, 128),
            "gif" => PdfTestImages.TransparentGif(),
            _ => PdfTestImages.SmallBmp(),
        };

        var image = PdfImage.TryLoad(bytes);

        Assert.NotNull(image);

        using var document = new PdfDocument();
        var page = document.AddPage();
        var canvas = PdfCanvas.ForPage(page);

        canvas.DrawImage(image, 100, 100, 80, 40);
        canvas.Commit();

        using var pig = PigDocument.Open(document.Save());
        var placed = Assert.Single(pig.GetPage(1).GetImages());

        Assert.Equal(image.Width, placed.WidthInSamples);
        Assert.Equal(80, placed.BoundingBox.Width, 1);
        Assert.Equal(PdfPage.A4Height - 140, placed.BoundingBox.Bottom, 1);
    }

    [Fact]
    public void Opacity_IsWrittenAsAGraphicsState()
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        var canvas = PdfCanvas.ForPage(page);

        canvas.DrawRectangle(10, 10, 100, 50, new PdfBrush(new PdfColor(255, 0, 0), 0.25));
        canvas.Commit();

        using var reopened = PdfDocument.Open(document.Save());
        var states = reopened.Pages[0].Resources.Elements.GetDictionary("/ExtGState");
        var state = states.Elements.GetDictionary(states.Elements.Keys.Single());

        Assert.Equal(0.25, state.Elements.GetReal("/ca"), 3);
    }

    [Fact]
    public void ForPage_DrawsOverExistingContentWithoutShiftingIt()
    {
        var builder = new PdfDocumentBuilder();
        var helvetica = builder.AddStandard14Font(Standard14Font.Helvetica);
        var source = builder.AddPage(PageSize.A4);

        // Content that leaves the coordinates scaled: anything drawn after it in the same stream would be too.
        source.CurrentStream.Operations.Add(new ModifyCurrentTransformationMatrix([2, 0, 0, 2, 0, 0]));
        source.AddText("Original", 12, new PdfPoint(36, 360), helvetica);

        using var document = PdfDocument.Open(builder.Build());
        var canvas = PdfCanvas.ForPage(document.Pages[0]);

        canvas.DrawText("Stamp", PdfFontLibrary.Get("Arial"), 12, 72, 72, _black);
        canvas.Commit();

        using var pig = PigDocument.Open(document.Save());
        var words = pig.GetPage(1).GetWords().ToList();
        var stamp = words.Single(word => word.Text == "Stamp");

        Assert.Contains(words, word => word.Text == "Original");
        Assert.Equal(72, stamp.BoundingBox.Left, 0);
        Assert.Equal(PdfPage.A4Height - 72, stamp.Letters[0].StartBaseLine.Y, 0);
    }

    [Fact]
    public void ForForm_DrawsIntoAFormObjectAPageShows()
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        var formCanvas = PdfCanvas.ForForm(document, 200, 50, out var form);

        formCanvas.DrawText("In a form", PdfFontLibrary.Get("Arial"), 12, 4, 30, _black);
        formCanvas.Commit();

        var canvas = PdfCanvas.ForPage(page);

        canvas.DrawForm(form, 100, 200, 200, 50);
        canvas.Commit();

        using var pig = PigDocument.Open(document.Save());
        var text = string.Concat(pig.GetPage(1).Letters.Select(letter => letter.Value));

        Assert.Equal("In a form", text);
        Assert.Contains("/Subtype /Form", Encoding.Latin1.GetString(document.Save()), StringComparison.Ordinal);
    }
}
