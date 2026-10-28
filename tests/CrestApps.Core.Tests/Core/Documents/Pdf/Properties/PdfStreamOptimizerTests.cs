using CrestApps.Core.AI.Documents.Pdf.Editing;
using CrestApps.Core.AI.Documents.Pdf.Objects;

namespace CrestApps.Core.Tests.Core.Documents.Pdf.Properties;

public sealed class PdfStreamOptimizerTests
{
    [Fact]
    public void Deduplicate_MergesOnlyStreamsWhoseDictionariesAgreeToo()
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        byte[] pixels = [.. Enumerable.Range(0, 200).Select(value => (byte)value)];

        // Same bytes, but one is ten pixels wide and the other twenty: they are different pictures.
        var narrow = Image(document, pixels, width: 10, height: 20);
        var wide = Image(document, pixels, width: 20, height: 10);
        var copy = Image(document, pixels, width: 10, height: 20);
        var objects = new PdfDictionary(document);

        objects.Elements["/Im1"] = narrow.Reference;
        objects.Elements["/Im2"] = wide.Reference;
        objects.Elements["/Im3"] = copy.Reference;
        page.Resources.Elements["/XObject"] = objects;

        var merged = PdfStreamOptimizer.Deduplicate(document);

        Assert.Equal(1, merged);
        Assert.Same(narrow.Reference, objects.Elements["/Im3"]);
        Assert.Same(wide.Reference, objects.Elements["/Im2"]);
        Assert.NotSame(narrow.Reference, objects.Elements["/Im2"]);
    }

    private static PdfDictionary Image(PdfDocument document, byte[] pixels, int width, int height)
    {
        var image = new PdfDictionary(document);

        image.Elements.SetName("/Type", "/XObject");
        image.Elements.SetName("/Subtype", "/Image");
        image.Elements.SetInteger("/Width", width);
        image.Elements.SetInteger("/Height", height);
        image.Elements.SetName("/ColorSpace", "/DeviceGray");
        image.Elements.SetInteger("/BitsPerComponent", 8);
        image.CreateStream(pixels);
        document.AddObject(image);

        return image;
    }
}
