using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using CrestApps.Core.AI.Documents.Pdf;

namespace CrestApps.Core.Tests.Architecture;

/// <summary>
/// The PDF libraries this repository and its packages use must stay MIT, Apache-2.0 or BSD licensed, so that
/// nobody who installs them is pulled into AGPL or commercial terms. PDFsharp and MigraDoc are AGPL-3.0-only
/// from 6.5.0 and check for a license at run time, iText is AGPL, and QuestPDF and the others are commercial or
/// exclude some users.
/// <para>
/// This fails when one of them is referenced by a project, given a version in <c>Directory.Packages.props</c>,
/// or reaches a build through another package.
/// </para>
/// </summary>
public sealed class PdfLibraryLicenseTests
{
    // Package ids, compared by prefix and case-insensitively, of PDF libraries whose license is not permissive:
    // "PDFsharp" also matches its forks, such as PdfSharpCore, and "itext" matches iTextSharp and iText 7.
    private static readonly string[] _deniedPackages =
    [
        "PDFsharp",
        "MigraDoc",
        "itext",
        "QuestPDF",
        "Aspose.PDF",
        "Syncfusion.Pdf",
        "Syncfusion.HtmlToPdf",
        "IronPdf",
        "Spire.PDF",
        "FreeSpire.PDF",
        "GemBox.Pdf",
        "SelectPdf",
        "EO.Pdf",
    ];

    private static readonly string[] _projectFolders = ["src", "tests", "benchmarks"];

    [Fact]
    public void NoProject_ReferencesAPdfLibraryWithANonPermissiveLicense()
    {
        // Arrange
        var repositoryRoot = FindRepositoryRoot();
        var violations = new List<string>();

        // Act
        foreach (var project in Projects(repositoryRoot))
        {
            foreach (var reference in XDocument.Load(project).Descendants("PackageReference"))
            {
                var id = (string)reference.Attribute("Include") ?? (string)reference.Attribute("Update");

                if (IsDenied(id))
                {
                    violations.Add($"{Relative(repositoryRoot, project)} references {id}.");
                }
            }
        }

        // Assert
        Assert.True(violations.Count == 0, BuildMessage(violations));
    }

    [Fact]
    public void CentralPackageVersions_HoldNoNonPermissivePdfLibrary()
    {
        // Arrange
        var repositoryRoot = FindRepositoryRoot();
        var violations = new List<string>();

        // Act
        foreach (var package in XDocument.Load(Path.Combine(repositoryRoot, "Directory.Packages.props")).Descendants("PackageVersion"))
        {
            var id = (string)package.Attribute("Include");

            if (IsDenied(id))
            {
                violations.Add($"Directory.Packages.props gives {id} the version {(string)package.Attribute("Version")}.");
            }
        }

        // Assert
        Assert.True(violations.Count == 0, BuildMessage(violations));
    }

    [Fact]
    public void ResolvedPackages_HoldNoNonPermissivePdfLibrary()
    {
        // Arrange
        var dependencies = Path.Combine(AppContext.BaseDirectory, "CrestApps.Core.Tests.deps.json");
        var violations = new List<string>();

        using var document = JsonDocument.Parse(File.ReadAllText(dependencies));

        // Act
        // The test project references the packages under test, so its dependency graph holds every package they bring in.
        foreach (var library in document.RootElement.GetProperty("libraries").EnumerateObject())
        {
            if (library.Value.GetProperty("type").GetString() == "package" && IsDenied(library.Name))
            {
                violations.Add($"{library.Name.Replace('/', ' ')} reaches the test build.");
            }
        }

        // Assert
        Assert.True(violations.Count == 0, BuildMessage(violations));
    }

    [Fact]
    public void RestoredProjects_HoldNoNonPermissivePdfLibrary()
    {
        // Arrange
        var repositoryRoot = FindRepositoryRoot();
        var violations = new List<string>();

        // Act
        // A package the test project does not reach, such as one only a sample host uses, still shows in the
        // graph each project restored.
        foreach (var project in Projects(repositoryRoot))
        {
            var assets = Path.Combine(Path.GetDirectoryName(project), "obj", "project.assets.json");

            if (!File.Exists(assets))
            {
                continue;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(assets));

            if (!document.RootElement.TryGetProperty("libraries", out var libraries))
            {
                continue;
            }

            foreach (var library in libraries.EnumerateObject())
            {
                if (IsDenied(library.Name))
                {
                    violations.Add($"{library.Name.Replace('/', ' ')} reaches {Relative(repositoryRoot, project)}.");
                }
            }
        }

        // Assert
        Assert.True(violations.Count == 0, BuildMessage(violations));
    }

    [Fact]
    public void PdfPackage_ReferencesNoPdfSharpOrMigraDocAssembly()
    {
        // Act
        var references = typeof(PdfCompositionOptions).Assembly.GetReferencedAssemblies();

        // Assert
        Assert.DoesNotContain(references, reference =>
            reference.Name.StartsWith("PdfSharp", StringComparison.OrdinalIgnoreCase) ||
            reference.Name.StartsWith("MigraDoc", StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> Projects(string repositoryRoot)
    {
        return _projectFolders
            .Select(folder => Path.Combine(repositoryRoot, folder))
            .Where(Directory.Exists)
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*.csproj", SearchOption.AllDirectories));
    }

    private static string Relative(string repositoryRoot, string path)
    {
        return Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/');
    }

    private static bool IsDenied(string id)
    {
        return !string.IsNullOrEmpty(id) &&
            _deniedPackages.Any(denied => id.StartsWith(denied, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildMessage(List<string> violations)
    {
        var builder = new StringBuilder()
            .AppendLine("A PDF library whose license is not MIT, Apache-2.0 or BSD is used. Use PdfPig and the package's own PDF writer instead:");

        foreach (var violation in violations)
        {
            builder.Append("  - ").AppendLine(violation);
        }

        return builder.ToString();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Directory.Packages.props"))
                && Directory.Exists(Path.Combine(directory.FullName, "tests", "CrestApps.Core.Tests")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate the repository root from the test assembly location.");
    }
}
