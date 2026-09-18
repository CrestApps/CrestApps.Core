using System.Reflection;

namespace CrestApps.Core.Tests.Core.Ingestion;

/// <summary>
/// Guards the shape the ingestion split was made for: document processing and file sources both sit on the
/// ingestion package, and neither sits on the other.
/// </summary>
/// <remarks>
/// A reference is easy to reintroduce by reaching for one convenient type, and nothing else in the suite
/// would fail when it happens: the assemblies still build and every behavioural test still passes. These
/// assertions read the compiled references, so the regression shows up as a failing test rather than as a
/// host that has to register the chat, tabular and tool services to read a folder.
/// </remarks>
public class PackageBoundaryTests
{
    private const string DocumentsAssembly = "CrestApps.Core.AI.Documents";
    private const string FileSourcesAssembly = "CrestApps.Core.AI.FileSources";
    private const string IngestionAssembly = "CrestApps.Core.AI.Ingestion";

    private static readonly Assembly _documents = typeof(AI.Documents.ServiceCollectionExtensions).Assembly;
    private static readonly Assembly _fileSources = typeof(AI.FileSources.ServiceCollectionExtensions).Assembly;
    private static readonly Assembly _ingestion = typeof(AI.Ingestion.ServiceCollectionExtensions).Assembly;
    private static readonly Assembly _ingestionPdf = typeof(AI.Ingestion.Pdf.PdfIngestionServiceCollectionExtensions).Assembly;

    private static string[] ReferencesOf(Assembly assembly)
        => [.. assembly.GetReferencedAssemblies().Select(name => name.Name)];

    [Fact]
    public void FileSources_DoesNotReferenceDocuments()
    {
        Assert.DoesNotContain(DocumentsAssembly, ReferencesOf(_fileSources));
    }

    [Fact]
    public void Documents_DoesNotReferenceFileSources()
    {
        Assert.DoesNotContain(FileSourcesAssembly, ReferencesOf(_documents));
    }

    [Fact]
    public void FileSources_ReferencesIngestion()
    {
        Assert.Contains(IngestionAssembly, ReferencesOf(_fileSources));
    }

    [Fact]
    public void Documents_ReferencesIngestion()
    {
        Assert.Contains(IngestionAssembly, ReferencesOf(_documents));
    }

    [Fact]
    public void PdfReader_DoesNotReferenceDocuments()
    {
        // Reading a PDF into a knowledge base is ingestion; writing one is a generated file. Keeping the
        // reader off Documents is what lets a file source read PDFs without the chat and tabular stack.
        Assert.DoesNotContain(DocumentsAssembly, ReferencesOf(_ingestionPdf));
    }

    [Fact]
    public void Ingestion_ReferencesNeitherConsumer()
    {
        // The shared package is the bottom of this trio. A reference in either direction here would make the
        // split cosmetic, because taking the ingestion package would take its consumer along with it.
        var references = ReferencesOf(_ingestion);

        Assert.DoesNotContain(DocumentsAssembly, references);
        Assert.DoesNotContain(FileSourcesAssembly, references);
    }
}
