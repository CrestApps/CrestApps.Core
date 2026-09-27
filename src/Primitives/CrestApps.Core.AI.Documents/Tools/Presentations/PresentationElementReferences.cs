namespace CrestApps.Core.AI.Documents.Tools.Presentations;

/// <summary>
/// Reads the elements a tool call names.
/// </summary>
internal static class PresentationElementReferences
{
    /// <summary>
    /// Reads the <c>elements</c> argument — ids, names or roles, as an array or a comma-separated string — or
    /// the single <c>element</c>.
    /// </summary>
    /// <param name="arguments">The call's arguments.</param>
    /// <returns>The references.</returns>
    /// <exception cref="PresentationArgumentException">No element is named.</exception>
    public static List<string> Read(PresentationArguments arguments)
    {
        var references = arguments.Strings("elements", "element", "element_ids", "ids", "shapes")
            .Select(reference => reference.Trim())
            .Where(reference => reference.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (references.Count == 0)
        {
            throw new PresentationArgumentException("Name the elements by their #ids (from get_slide_content), names, or roles such as title or body.");
        }

        return references;
    }
}
