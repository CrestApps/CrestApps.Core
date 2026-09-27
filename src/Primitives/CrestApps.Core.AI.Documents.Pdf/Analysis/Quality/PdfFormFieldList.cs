using System.Globalization;
using System.Text;
using PdfSharp.Pdf;

namespace CrestApps.Core.AI.Documents.Pdf.Analysis;

/// <summary>
/// One form field a person fills in: a terminal field of the form's field tree.
/// </summary>
/// <param name="Dictionary">The field dictionary, the one that carries the name and the tooltip.</param>
/// <param name="FullName">The field's full name, its ancestors' names joined with dots.</param>
/// <param name="FieldType">The field type without its slash, for example <c>Tx</c>, or <see langword="null"/>.</param>
internal sealed record PdfFormField(PdfDictionary Dictionary, string FullName, string FieldType)
{
    /// <summary>
    /// Gets the tooltip (<c>/TU</c>), the name assistive technology reads out, or <see langword="null"/>.
    /// </summary>
    public string Tooltip => PdfObjects.GetText(Dictionary, "/TU");
}

/// <summary>
/// Lists the fields of a PDF form.
/// </summary>
internal static class PdfFormFieldList
{
    private const int MaxFields = 5_000;

    /// <summary>
    /// Lists the terminal fields of a document's form.
    /// </summary>
    /// <param name="document">The document, opened with PDFsharp.</param>
    /// <returns>The fields, in the order the form lists them.</returns>
    public static List<PdfFormField> Read(PdfDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var fields = new List<PdfFormField>();
        var form = PdfObjects.GetDictionary(document.Internals.Catalog, "/AcroForm");

        if (form is null)
        {
            return fields;
        }

        var visited = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);

        foreach (var item in PdfObjects.Items(PdfObjects.GetArray(form, "/Fields")))
        {
            Walk(item as PdfDictionary, null, null, fields, visited, 0);
        }

        return fields;
    }

    /// <summary>
    /// Turns a field name into words a person would say, for example <c>first_name</c> or <c>firstName</c>
    /// into <c>First name</c>.
    /// </summary>
    /// <param name="fullName">The field's full name.</param>
    /// <returns>The words, or <see langword="null"/> when the name has none.</returns>
    public static string Humanize(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return null;
        }

        // Only the field's own part of the name, without the index an XFA-made form adds ("name[0]").
        var name = fullName.Split('.', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? fullName;
        var bracket = name.IndexOf('[', StringComparison.Ordinal);

        if (bracket > 0)
        {
            name = name[..bracket];
        }

        var builder = new StringBuilder(name.Length + 8);
        var previous = '\0';

        foreach (var character in name)
        {
            if (character is '_' or '-' or ' ' or '.')
            {
                AppendSpace(builder);
            }
            else if (char.IsUpper(character) && char.IsLower(previous))
            {
                AppendSpace(builder);
                builder.Append(char.ToLowerInvariant(character));
            }
            else if (char.IsDigit(character) && char.IsLetter(previous))
            {
                AppendSpace(builder);
                builder.Append(character);
            }
            else
            {
                builder.Append(char.ToLowerInvariant(character));
            }

            previous = character;
        }

        var words = builder.ToString().Trim();

        if (words.Length == 0)
        {
            return null;
        }

        return char.ToUpper(words[0], CultureInfo.InvariantCulture) + words[1..];
    }

    private static void AppendSpace(StringBuilder builder)
    {
        if (builder.Length > 0 && builder[^1] != ' ')
        {
            builder.Append(' ');
        }
    }

    private static void Walk(
        PdfDictionary field,
        string parentName,
        string inheritedType,
        List<PdfFormField> fields,
        HashSet<PdfDictionary> visited,
        int depth)
    {
        if (field is null || depth > 32 || fields.Count >= MaxFields || !visited.Add(field))
        {
            return;
        }

        var partial = PdfObjects.GetText(field, "/T");
        var fullName = parentName;

        if (!string.IsNullOrEmpty(partial))
        {
            fullName = string.IsNullOrEmpty(parentName)
                ? partial
                : parentName + "." + partial;
        }

        var type = PdfObjects.GetName(field, "/FT")?.TrimStart('/') ?? inheritedType;

        // Kids without a name of their own are the field's widgets; kids with one are further fields.
        var childFields = PdfObjects.Items(PdfObjects.GetArray(field, "/Kids"))
            .OfType<PdfDictionary>()
            .Where(kid => PdfObjects.Get(kid, "/T") is not null)
            .ToList();

        if (childFields.Count == 0)
        {
            if (!string.IsNullOrEmpty(partial))
            {
                fields.Add(new PdfFormField(field, fullName, type));
            }

            return;
        }

        foreach (var child in childFields)
        {
            Walk(child, fullName, type, fields, visited, depth + 1);
        }
    }
}
