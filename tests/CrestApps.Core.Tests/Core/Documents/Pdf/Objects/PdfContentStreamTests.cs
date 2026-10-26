using System.Text;
using CrestApps.Core.AI.Documents.Pdf.Objects;

namespace CrestApps.Core.Tests.Core.Documents.Pdf.Objects;

public sealed class PdfContentStreamTests
{
    [Fact]
    public void Read_ThenWrite_KeepsEveryOperatorAndOperand()
    {
        var content = Encoding.Latin1.GetBytes(
            "q 1 0 0 1 72 720.5 cm\n" +
            "BT /F1 12 Tf (Hello \\(world\\) \\101\\\n) Tj [(A) -120 <0041>] TJ ET\n" +
            "/OC /L1 BDC 0 0 1 rg EMC\n" +
            "/P << /MCID 3 /Alt (Logo) >> BDC % a comment\n EMC Q");

        var operators = PdfContentStream.Read(content);

        Assert.Equal(["q", "cm", "BT", "Tf", "Tj", "TJ", "ET", "BDC", "rg", "EMC", "BDC", "EMC", "Q"], operators.Select(op => op.Name));

        var shown = Assert.IsType<PdfString>(operators[4].Operands[0]);

        Assert.Equal("Hello (world) A", Encoding.Latin1.GetString(shown.Bytes));

        var array = Assert.IsType<PdfArray>(operators[5].Operands[0]);

        Assert.Equal(3, array.Elements.Count);
        Assert.Equal(-120, Assert.IsType<PdfInteger>(array.Elements[1]).Value);
        Assert.Equal([0x00, 0x41], Assert.IsType<PdfString>(array.Elements[2]).Bytes);
        Assert.Equal(720.5, Assert.IsType<PdfReal>(operators[1].Operands[5]).Value);
        Assert.Equal("/L1", Assert.IsType<PdfName>(operators[7].Operands[1]).Value);
        Assert.Equal(3, Assert.IsType<PdfDictionary>(operators[10].Operands[1]).Elements.GetInteger("/MCID"));

        // Written back and read again, the stream says the same.
        var again = PdfContentStream.Read(PdfContentStream.Write(operators));

        Assert.Equal(operators.Select(op => op.Name), again.Select(op => op.Name));
        Assert.Equal(shown.Bytes, Assert.IsType<PdfString>(again[4].Operands[0]).Bytes);
        Assert.Equal("Logo", Assert.IsType<PdfDictionary>(again[10].Operands[1]).Elements.GetString("/Alt"));
    }

    [Fact]
    public void Read_InlineImage_KeepsItsDataByteForByte()
    {
        byte[] data = [0x00, 0xFF, (byte)'E', (byte)'I', 0x10];
        var content = new List<byte>();

        content.AddRange(Encoding.ASCII.GetBytes("q BI /W 5 /H 1 /CS /G /BPC 8 ID "));
        content.AddRange(data);
        content.AddRange(Encoding.ASCII.GetBytes("\nEI Q"));

        var operators = PdfContentStream.Read([.. content]);

        Assert.Equal(["q", "BI", "Q"], operators.Select(op => op.Name));
        Assert.Equal(data, operators[1].InlineImageData);
        Assert.Equal(5, Assert.IsType<PdfDictionary>(operators[1].Operands[0]).Elements.GetInteger("/W"));

        var again = PdfContentStream.Read(PdfContentStream.Write(operators));

        Assert.Equal(data, again[1].InlineImageData);
    }

    [Fact]
    public void Read_StringThatNeverCloses_Throws()
    {
        Assert.Throws<FormatException>(() => PdfContentStream.Read(Encoding.ASCII.GetBytes("BT (never closed Tj ET")));
    }
}
