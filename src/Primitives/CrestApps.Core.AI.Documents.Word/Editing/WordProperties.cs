using System.Text.Json;
using CrestApps.Core.AI.Documents.Word.Workspace;
using DocumentFormat.OpenXml.ExtendedProperties;
using DocumentFormat.OpenXml.Packaging;

namespace CrestApps.Core.AI.Documents.Word.Editing;

/// <summary>
/// Reads and writes a document's properties: title, subject, author, keywords, description, category, company.
/// </summary>
internal static class WordProperties
{
    /// <summary>
    /// Writes the properties a model gave, changing only those it gave.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="properties">The properties object.</param>
    /// <param name="now">The time the document is recorded as modified.</param>
    /// <returns>The names of the properties that were set.</returns>
    public static List<string> Apply(WordprocessingDocument document, JsonElement properties, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(document);

        var changed = new List<string>();

        if (properties.ValueKind != JsonValueKind.Object)
        {
            return changed;
        }

        var package = document.PackageProperties;

        void Set(string name, Action<string> apply)
        {
            var value = WordJsonValues.GetString(properties, name);

            if (value is not null)
            {
                apply(value.Trim());
                changed.Add(name);
            }
        }

        Set("title", value => package.Title = value);
        Set("subject", value => package.Subject = value);
        Set("author", value => package.Creator = value);
        Set("keywords", value => package.Keywords = value);
        Set("description", value => package.Description = value);
        Set("category", value => package.Category = value);
        Set("company", value =>
        {
            var part = document.ExtendedFilePropertiesPart ?? document.AddExtendedFilePropertiesPart();

            part.Properties ??= new Properties();
            part.Properties.Company = new Company(value);
        });

        if (changed.Count > 0)
        {
            package.Modified = now;
            package.Created ??= now;
        }

        return changed;
    }

    /// <summary>
    /// Reads a document's properties.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <returns>The properties that have a value, by name.</returns>
    public static Dictionary<string, string> Read(WordprocessingDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var package = document.PackageProperties;
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["title"] = package.Title,
            ["subject"] = package.Subject,
            ["author"] = package.Creator,
            ["keywords"] = package.Keywords,
            ["description"] = package.Description,
            ["category"] = package.Category,
            ["last_modified_by"] = package.LastModifiedBy,
            ["created"] = package.Created?.ToString("u", System.Globalization.CultureInfo.InvariantCulture),
            ["modified"] = package.Modified?.ToString("u", System.Globalization.CultureInfo.InvariantCulture),
            ["company"] = document.ExtendedFilePropertiesPart?.Properties?.Company?.Text,
        };

        return values.Where(pair => !string.IsNullOrWhiteSpace(pair.Value)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }
}
