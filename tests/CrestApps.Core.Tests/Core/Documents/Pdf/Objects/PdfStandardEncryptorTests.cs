using CrestApps.Core.AI.Documents.Pdf.Objects;
using CrestApps.Core.Tests.Core.Documents.Pdf.Reading;
using UglyToad.PdfPig.Exceptions;
using UglyToad.PdfPig.Tokens;
using PigDocument = UglyToad.PdfPig.PdfDocument;
using PigParsingOptions = UglyToad.PdfPig.ParsingOptions;

namespace CrestApps.Core.Tests.Core.Documents.Pdf.Objects;

public sealed class PdfStandardEncryptorTests
{
    [Theory]
    [InlineData("aes256", 6)]
    [InlineData("aes128", 4)]
    [InlineData("rc4_128", 3)]
    public async Task Write_Encrypted_OpensOnlyWithAPasswordAndTellsTheOwnerApart(string encryption, int revision)
    {
        var method = encryption switch
        {
            "aes128" => PdfEncryptionMethod.Aes128,
            "rc4_128" => PdfEncryptionMethod.Rc4With128Bits,
            _ => PdfEncryptionMethod.Aes256,
        };

        using var source = PdfDocument.Open(await PdfReadingFixtures.ParagraphsAsync("Quarterly figures for the board."));
        source.Info.Title = "Board pack";

        var bytes = PdfObjectWriter.Write(source, new PdfStandardEncryptor(method, "open-sesame", "owner-secret"));

        Assert.ThrowsAny<PdfDocumentEncryptedException>(() => PigDocument.Open(bytes).Dispose());

        using (var pig = PigDocument.Open(bytes, new PigParsingOptions { Password = "open-sesame" }))
        {
            Assert.True(pig.IsEncrypted);
            Assert.Equal("Board pack", pig.Information.Title);
            Assert.Equal(["Quarterly", "figures", "for", "the", "board."], pig.GetPage(1).GetWords().Select(word => word.Text));
            Assert.Equal(revision, ReadRevision(pig));
        }

        using (var reader = PdfDocument.Open(bytes, "open-sesame"))
        {
            Assert.False(reader.HasOwnerAccess);
        }

        using var owner = PdfDocument.Open(bytes, "owner-secret");

        Assert.True(owner.HasOwnerAccess);
        Assert.Equal("Board pack", owner.Info.Title);
    }

    [Fact]
    public async Task Write_WithoutAUserPassword_OpensForAnyoneButOnlyTheOwnerMayChangeIt()
    {
        using var source = PdfDocument.Open(await PdfReadingFixtures.ParagraphsAsync("Read-only notice."));

        var bytes = PdfObjectWriter.Write(source, new PdfStandardEncryptor(PdfEncryptionMethod.Aes256, null, "owner-secret", PdfStandardEncryptor.AllPermissions & ~0x10));

        using (var pig = PigDocument.Open(bytes))
        {
            Assert.True(pig.IsEncrypted);
            Assert.Equal(["Read-only", "notice."], pig.GetPage(1).GetWords().Select(word => word.Text));
        }

        using (var anyone = PdfDocument.Open(bytes))
        {
            Assert.False(anyone.HasOwnerAccess);
        }

        using var owner = PdfDocument.Open(bytes, "owner-secret");

        Assert.True(owner.HasOwnerAccess);
    }

    private static int? ReadRevision(PigDocument pig)
    {
        var token = pig.Structure.Trailer.EncryptionToken;

        if (token is IndirectReferenceToken reference)
        {
            token = pig.Structure.GetObject(reference.Data).Data;
        }

        return token is DictionaryToken dictionary && dictionary.Data["R"] is NumericToken number
            ? number.Int
            : null;
    }
}
