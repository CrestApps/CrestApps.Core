using PdfSharp.Pdf;

namespace CrestApps.Core.AI.Documents.Pdf.Editing;

/// <summary>
/// Reads a PDF's optional content groups (layers) and changes which of them are shown when it opens.
/// </summary>
/// <remarks>
/// A layer's visibility on opening is set by the default configuration, <c>/OCProperties /D</c>: its
/// <c>/BaseState</c> applies to every group, and its <c>/ON</c> and <c>/OFF</c> arrays name the exceptions.
/// Showing or hiding a layer moves it between those arrays; the content drawn in the layer stays in the file.
/// </remarks>
internal static class PdfLayers
{
    /// <summary>
    /// Lists the layers of a document.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <returns>The layers, in the order the document declares them.</returns>
    public static List<PdfLayer> List(PdfDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var layers = new List<PdfLayer>();
        var properties = PdfObjects.GetDictionary(document.Internals.Catalog, "/OCProperties");
        var groups = PdfObjects.GetArray(properties, "/OCGs");

        if (groups is null)
        {
            return layers;
        }

        var configuration = PdfObjects.GetDictionary(properties, "/D");
        var hiddenByDefault = string.Equals(PdfObjects.GetName(configuration, "/BaseState"), "OFF", StringComparison.Ordinal);
        var on = PdfObjects.GetArray(configuration, "/ON");
        var off = PdfObjects.GetArray(configuration, "/OFF");
        var locked = PdfObjects.GetArray(configuration, "/Locked");
        var byGroup = new Dictionary<PdfDictionary, PdfLayer>();

        foreach (var item in groups.Elements)
        {
            if (PdfObjects.Resolve(item) is not PdfDictionary group || byGroup.ContainsKey(group))
            {
                continue;
            }

            var layer = new PdfLayer
            {
                Name = PdfObjects.GetText(group, "/Name") ?? "(unnamed)",
                Visible = !Contains(off, group) && (Contains(on, group) || !hiddenByDefault),
                Locked = Contains(locked, group),
                Intent = string.Join(", ", PdfObjects.Items(group.Elements["/Intent"]).Select(intent => PdfObjects.ToText(PdfObjects.Resolve(intent))).Where(intent => intent is not null)),
                Group = group,
            };

            byGroup[group] = layer;
            layers.Add(layer);
        }

        for (var index = 0; index < document.PageCount; index++)
        {
            var used = new HashSet<PdfDictionary>();
            var page = document.Pages[index];

            CollectFromResources(PdfObjects.GetDictionary(page, "/Resources"), used, new HashSet<PdfDictionary>(), 0);

            var annotations = PdfObjects.GetArray(page, "/Annots");

            if (annotations is not null)
            {
                foreach (var annotation in annotations.Elements)
                {
                    CollectFromMembership(PdfObjects.Resolve(annotation) is PdfDictionary dictionary ? dictionary.Elements["/OC"] : null, used);
                }
            }

            foreach (var group in used)
            {
                if (byGroup.TryGetValue(group, out var layer))
                {
                    layer.Pages.Add(index + 1);
                }
            }
        }

        return layers;
    }

    /// <summary>
    /// Sets whether layers are shown when the document opens.
    /// </summary>
    /// <param name="document">The document, opened for editing.</param>
    /// <param name="layers">The layers, as <see cref="List"/> returned them for this document.</param>
    /// <param name="visible">Whether they are shown.</param>
    public static void SetVisibility(PdfDocument document, IEnumerable<PdfLayer> layers, bool visible)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(layers);

        var properties = PdfObjects.GetDictionary(document.Internals.Catalog, "/OCProperties")
            ?? throw new InvalidOperationException("The document has no optional content.");

        var configuration = PdfObjects.GetDictionary(properties, "/D");

        if (configuration is null)
        {
            configuration = new PdfDictionary(document);
            properties.Elements["/D"] = configuration;
        }

        var on = EnsureArray(document, configuration, "/ON");
        var off = EnsureArray(document, configuration, "/OFF");

        foreach (var layer in layers)
        {
            RemoveAll(on, layer.Group);
            RemoveAll(off, layer.Group);

            PdfItem item = layer.Group.Reference is null
                ? layer.Group
                : layer.Group.Reference;

            (visible ? on : off).Elements.Add(item);
        }
    }

    private static void CollectFromResources(PdfDictionary resources, HashSet<PdfDictionary> used, HashSet<PdfDictionary> visited, int depth)
    {
        if (resources is null || depth > 4 || !visited.Add(resources))
        {
            return;
        }

        var properties = PdfObjects.GetDictionary(resources, "/Properties");

        if (properties is not null)
        {
            foreach (var entry in properties.Elements)
            {
                CollectFromMembership(entry.Value, used);
            }
        }

        var objects = PdfObjects.GetDictionary(resources, "/XObject");

        if (objects is null)
        {
            return;
        }

        foreach (var entry in objects.Elements)
        {
            if (PdfObjects.Resolve(entry.Value) is not PdfDictionary drawn)
            {
                continue;
            }

            CollectFromMembership(drawn.Elements["/OC"], used);

            if (string.Equals(PdfObjects.GetName(drawn, "/Subtype"), "Form", StringComparison.Ordinal))
            {
                CollectFromResources(PdfObjects.GetDictionary(drawn, "/Resources"), used, visited, depth + 1);
            }
        }
    }

    private static void CollectFromMembership(PdfItem item, HashSet<PdfDictionary> used)
    {
        if (PdfObjects.Resolve(item) is not PdfDictionary dictionary)
        {
            return;
        }

        var type = PdfObjects.GetName(dictionary, "/Type");

        if (string.Equals(type, "OCG", StringComparison.Ordinal))
        {
            used.Add(dictionary);

            return;
        }

        if (!string.Equals(type, "OCMD", StringComparison.Ordinal))
        {
            return;
        }

        foreach (var member in PdfObjects.Items(dictionary.Elements["/OCGs"]))
        {
            if (PdfObjects.Resolve(member) is PdfDictionary group)
            {
                used.Add(group);
            }
        }
    }

    private static bool Contains(PdfArray array, PdfDictionary group)
    {
        return array is not null && array.Elements.Any(item => ReferenceEquals(PdfObjects.Resolve(item), group));
    }

    private static void RemoveAll(PdfArray array, PdfDictionary group)
    {
        for (var index = array.Elements.Count - 1; index >= 0; index--)
        {
            if (ReferenceEquals(PdfObjects.Resolve(array.Elements[index]), group))
            {
                array.Elements.RemoveAt(index);
            }
        }
    }

    private static PdfArray EnsureArray(PdfDocument document, PdfDictionary dictionary, string key)
    {
        var array = PdfObjects.GetArray(dictionary, key);

        if (array is null)
        {
            array = new PdfArray(document);
            dictionary.Elements[key] = array;
        }

        return array;
    }
}
