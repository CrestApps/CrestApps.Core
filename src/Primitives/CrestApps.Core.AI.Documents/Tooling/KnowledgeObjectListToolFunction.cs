using System.Text;
using System.Text.Json;
using CrestApps.Core.AI.DataSources;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Orchestration;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.Infrastructure.Indexing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrestApps.Core.AI.Documents.Tooling;

/// <summary>
/// Enumerates what a data source holds, rather than ranking it against a query.
/// </summary>
/// <remarks>
/// "What tables are in this issue?" and "show me every figure in this article" are questions about a set, and
/// a set cannot be answered by similarity: a search returns the rows that scored best against a sentence,
/// which is neither all of them nor a statement that it was all of them. The store knows the whole set, keyed
/// by the document each object belongs to and by what kind of thing it is, so this reads it directly.
/// <para>
/// It is the other half of <see cref="KnowledgeObjectToolFunction"/>: this one says what is there, that one
/// hands over any one of them in full.
/// </para>
/// </remarks>
public sealed class KnowledgeObjectListToolFunction : AIFunction
{
    private const string ParentRelation = "parent";
    private const string SiblingsRelation = "siblings";

    private static readonly string[] _anyKind = [];
    private static readonly string[] _figureKinds = [KnowledgeObjectTypes.Figure, KnowledgeObjectTypes.Chart];
    private static readonly string[] _chartKinds = [KnowledgeObjectTypes.Chart];
    private static readonly string[] _tableKinds = [KnowledgeObjectTypes.Table];
    private static readonly string[] _articleKinds = [KnowledgeObjectTypes.Article];
    private static readonly string[] _textKinds = [KnowledgeObjectTypes.Text];
    private static readonly string[] _documentKinds = [KnowledgeObjectTypes.Document];

    private static readonly JsonElement _jsonSchema = JsonSerializer.Deserialize<JsonElement>(
    """
    {
      "type": "object",
      "properties": {
        "kind": {
          "type": "string",
          "description": "Which kind of object to list: 'figure' (which includes charts), 'chart', 'table', 'article', 'text' or 'document'. Omit it to list every kind."
        },
        "parent_id": {
          "type": "string",
          "description": "List only what is inside this article or document, given exactly as its identifier - for example 'article:0123456789abcdef:1'. Omit it to list across the whole data source."
        },
        "id": {
          "type": "string",
          "description": "The object to walk from. Only used together with 'relation'."
        },
        "relation": {
          "type": "string",
          "description": "Walk from 'id' instead of listing: 'parent' returns the object it hangs off, 'siblings' returns everything under the same parent, this object included. 'kind' is ignored for 'parent'."
        },
        "limit": {
          "type": "integer",
          "description": "The most objects to return. It is capped by the tool's own maximum, and the answer says outright when more matched than were returned."
        }
      },
      "additionalProperties": false
    }
    """);

    private readonly string _name;
    private readonly string _description;
    private readonly KnowledgeObjectListToolSettings _settings;

    /// <summary>
    /// Initializes a new instance of the <see cref="KnowledgeObjectListToolFunction"/> class.
    /// </summary>
    /// <param name="name">The function name exposed to the AI model.</param>
    /// <param name="description">The description exposed to the AI model.</param>
    /// <param name="settings">The configured instance settings.</param>
    public KnowledgeObjectListToolFunction(string name, string description, KnowledgeObjectListToolSettings settings)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(settings);

        _name = name;
        _description = string.IsNullOrWhiteSpace(description) ? name : description;
        _settings = settings;
    }

    /// <inheritdoc />
    public override string Name => _name;

    /// <inheritdoc />
    public override string Description => _description;

    /// <inheritdoc />
    public override JsonElement JsonSchema => _jsonSchema;

    /// <inheritdoc />
    public override IReadOnlyDictionary<string, object> AdditionalProperties { get; } = new Dictionary<string, object>
    {
        ["Strict"] = false,
    };

    /// <summary>
    /// Lists the requested objects.
    /// </summary>
    /// <param name="arguments">The arguments supplied by the AI model.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The listing, or a message explaining why it could not be produced.</returns>
    protected override async ValueTask<object> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var services = arguments.Services;
        var logger = services?.GetService<ILoggerFactory>()?.CreateLogger<KnowledgeObjectListToolFunction>()
            ?? NullLogger<KnowledgeObjectListToolFunction>.Instance;

        if (services is null)
        {
            return "No services are available to list objects.";
        }

        if (string.IsNullOrEmpty(_settings.DataSourceId))
        {
            logger.LogWarning("AI tool '{ToolName}' failed: no data source is configured for this instance.", _name);

            return "No data source is configured for this tool. Configure one in the tool instance settings.";
        }

        var store = services.GetService<IKnowledgeObjectStore>();

        if (store is null)
        {
            return "Knowledge objects are not available in this application.";
        }

        var kind = ReadString(arguments, "kind");

        if (!TryResolveKinds(kind, out var kinds, out var kindLabel))
        {
            return $"'{kind}' is not a kind of knowledge object. Use one of: figure, chart, table, article, text, document - or omit it to list every kind.";
        }

        var relation = ReadString(arguments, "relation");

        if (relation is not null &&
            !relation.Equals(ParentRelation, StringComparison.OrdinalIgnoreCase) &&
            !relation.Equals(SiblingsRelation, StringComparison.OrdinalIgnoreCase))
        {
            return $"'{relation}' is not a relation. Use 'parent' or 'siblings', or omit it to list instead of walking.";
        }

        var limit = ResolveLimit(ReadInt(arguments, "limit"));

        Listing listing;

        try
        {
            listing = relation is null
                ? await BuildListingAsync(store, ReadString(arguments, "parent_id"), kinds, kindLabel, limit, cancellationToken)
                : await BuildWalkAsync(store, ReadString(arguments, "id"), relation, kinds, kindLabel, limit, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AI tool '{ToolName}' failed to list objects in data source '{DataSourceId}'.", _name, _settings.DataSourceId);

            return "An error occurred while listing the objects.";
        }

        if (listing.Failure is not null)
        {
            return listing.Failure;
        }

        return Render(listing, services, logger);
    }

    /// <summary>
    /// Lists every object of the requested kinds, across the data source or inside one article or document.
    /// </summary>
    /// <param name="store">The knowledge object store.</param>
    /// <param name="parentId">The article or document to list inside, or <see langword="null"/> for the whole data source.</param>
    /// <param name="kinds">The object types to keep, or empty for every kind.</param>
    /// <param name="kindLabel">How to name those kinds in the answer.</param>
    /// <param name="limit">The most objects to return.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The listing.</returns>
    private async Task<Listing> BuildListingAsync(
        IKnowledgeObjectStore store,
        string parentId,
        string[] kinds,
        string kindLabel,
        int limit,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(parentId))
        {
            // One more than the bound is asked for so truncation is a fact rather than a guess: if the extra
            // object comes back, more matched than fit, and the answer says so. Counting the whole set first
            // would be a second query over the same rows to print a number nobody asked for.
            var fetched = await store.GetByObjectTypesAsync(_settings.DataSourceId, kinds, limit + 1, cancellationToken);
            var truncated = fetched.Count > limit;
            var page = fetched.Where(IsInDataSource).Take(limit).Where(IsListable).ToList();

            SortForReading(page);

            return new Listing
            {
                Header = $"{kindLabel} in this data source",
                Objects = page,
                IsTruncated = truncated,
                TruncationNote = truncated
                    ? $"More than {limit} objects match, so this is not the whole set. The {limit} listed were taken in identifier order. Narrow the listing with 'parent_id' or 'kind'."
                    : null,
            };
        }

        if (!KnowledgeObjectIdentifiers.IsKnown(parentId))
        {
            return Failed($"'{parentId}' is not a knowledge object identifier. Use one exactly as a search reported it.");
        }

        var anchor = await FindAsync(store, parentId, cancellationToken);

        if (anchor is null)
        {
            return Failed($"No object with identifier '{parentId}' was found in this data source.");
        }

        // Everything in the document the anchor belongs to, which the store answers from an indexed column,
        // and then the part of it that hangs below the anchor. Reading one document is what widening a hit
        // to its article already costs; reading the data source to filter it in memory is the thing this
        // must never become.
        var family = (await store.GetByRootIdAsync(_settings.DataSourceId, RootOf(anchor), cancellationToken))
            .Where(IsInDataSource)
            .ToList();

        var matches = DescendantsOf(family, anchor.CanonicalId)
            .Where(entry => IsListable(entry) && IsWanted(entry, kinds))
            .ToList();

        SortForReading(matches);

        var bounded = matches.Count > limit;

        if (bounded)
        {
            matches.RemoveRange(limit, matches.Count - limit);
        }

        return new Listing
        {
            Header = $"{kindLabel} inside '{parentId}'",
            Objects = matches,
            IsTruncated = bounded,
            TruncationNote = bounded
                ? $"More than {limit} objects match, so this is not the whole set. The first {limit} are listed, in document order. Narrow the listing with 'kind'."
                : null,
        };
    }

    /// <summary>
    /// Walks from one object to its parent or to the objects beside it.
    /// </summary>
    /// <param name="store">The knowledge object store.</param>
    /// <param name="anchorId">The object to walk from.</param>
    /// <param name="relation">Which way to walk.</param>
    /// <param name="kinds">The object types to keep, or empty for every kind.</param>
    /// <param name="kindLabel">How to name those kinds in the answer.</param>
    /// <param name="limit">The most objects to return.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The listing.</returns>
    private async Task<Listing> BuildWalkAsync(
        IKnowledgeObjectStore store,
        string anchorId,
        string relation,
        string[] kinds,
        string kindLabel,
        int limit,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(anchorId))
        {
            return Failed($"Walking to a '{relation}' needs an 'id' argument naming the object to walk from.");
        }

        if (!KnowledgeObjectIdentifiers.IsKnown(anchorId))
        {
            return Failed($"'{anchorId}' is not a knowledge object identifier. Use one exactly as a search reported it.");
        }

        var anchor = await FindAsync(store, anchorId, cancellationToken);

        if (anchor is null)
        {
            return Failed($"No object with identifier '{anchorId}' was found in this data source.");
        }

        if (relation.Equals(ParentRelation, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(anchor.ParentId))
            {
                return Failed($"'{anchorId}' has no parent. It sits at the top of document '{RootOf(anchor)}'.");
            }

            var parent = await FindAsync(store, anchor.ParentId, cancellationToken);

            if (parent is null)
            {
                // Said as what it is. A parent identifier that resolves to nothing is a broken document, not
                // an empty one, and reporting it as "no parent" would hide a half-deleted ingest forever.
                return Failed($"'{anchorId}' names '{anchor.ParentId}' as its parent, but no such object is in this data source.");
            }

            return new Listing
            {
                Header = $"Parent of '{anchorId}'",
                Objects = [parent],
            };
        }

        var family = (await store.GetByRootIdAsync(_settings.DataSourceId, RootOf(anchor), cancellationToken))
            .Where(IsInDataSource)
            .ToList();

        var siblings = family
            .Where(entry => string.Equals(entry.ParentId, anchor.ParentId, StringComparison.Ordinal) && IsListable(entry) && IsWanted(entry, kinds))
            .ToList();

        SortForReading(siblings);

        var bounded = siblings.Count > limit;

        if (bounded)
        {
            siblings.RemoveRange(limit, siblings.Count - limit);
        }

        var header = string.IsNullOrWhiteSpace(anchor.ParentId)
            ? $"{kindLabel} at the top of document '{RootOf(anchor)}', alongside '{anchorId}'"
            : $"{kindLabel} directly under '{anchor.ParentId}', alongside '{anchorId}'";

        return new Listing
        {
            Header = header,
            Objects = siblings,
            IsTruncated = bounded,
            TruncationNote = bounded
                ? $"More than {limit} objects match, so this is not the whole set. The first {limit} are listed, in document order. Narrow the listing with 'kind'."
                : null,
        };
    }

    /// <summary>
    /// Turns the listing into what the model reads and what the host can show.
    /// </summary>
    /// <param name="listing">The listing.</param>
    /// <param name="services">The request services.</param>
    /// <param name="logger">The logger.</param>
    /// <returns>The result.</returns>
    private KnowledgeObjectListToolResult Render(Listing listing, IServiceProvider services, ILogger logger)
    {
        var invocationContext = AIInvocationScope.Current;
        var nextLabel = FigureReferenceMarker.NextIndex(invocationContext);
        var entries = new List<KnowledgeObjectListEntry>(listing.Objects.Count);
        var unshowable = new List<string>();

        string firstLabel = null;

        foreach (var entry in listing.Objects)
        {
            var isFigure = entry.ObjectType is KnowledgeObjectTypes.Figure or KnowledgeObjectTypes.Chart;
            var link = isFigure ? ResolveLink(services, entry, logger) : null;

            string label = null;

            if (isFigure)
            {
                if (invocationContext is not null && !string.IsNullOrWhiteSpace(link))
                {
                    // Registered under the very marker printed below, so what the model is shown and what the
                    // host looks up cannot drift apart. This is the mechanism a [doc:n] citation already
                    // uses, with the client substituting a picture for the marker.
                    label = FigureReferenceMarker.Format(nextLabel);

                    invocationContext.ToolReferences[label] = new AICompletionReference
                    {
                        Text = string.IsNullOrWhiteSpace(entry.Title) ? label : entry.Title,
                        Title = entry.Title,
                        Link = link,
                        IsImage = true,
                        Index = nextLabel,
                        ReferenceId = entry.CanonicalId,
                        ReferenceType = AIDataSourceSourceTypes.File,
                        DataSourceId = _settings.DataSourceId,
                    };

                    firstLabel ??= label;
                    nextLabel++;
                }
                else
                {
                    unshowable.Add(entry.CanonicalId);
                }
            }

            entries.Add(new KnowledgeObjectListEntry
            {
                Id = entry.CanonicalId,
                ObjectType = entry.ObjectType,
                Title = entry.Title,
                ParentId = entry.ParentId,
                PageStart = entry.PageStart,
                Label = label,
                Link = link,
            });
        }

        return new KnowledgeObjectListToolResult
        {
            Text = BuildText(listing, entries, firstLabel, unshowable),
            Entries = entries,
            IsTruncated = listing.IsTruncated,
        };
    }

    /// <summary>
    /// Renders the listing as the lines a model reads.
    /// </summary>
    /// <param name="listing">The listing.</param>
    /// <param name="entries">The rendered entries, in the order they are printed.</param>
    /// <param name="firstLabel">The first figure marker printed, or <see langword="null"/> when none was.</param>
    /// <param name="unshowable">The identifiers of the figures the host has no address for.</param>
    /// <returns>The text.</returns>
    private static string BuildText(
        Listing listing,
        List<KnowledgeObjectListEntry> entries,
        string firstLabel,
        List<string> unshowable)
    {
        var builder = new StringBuilder();

        if (entries.Count == 0)
        {
            // Said as a fact about the data source rather than as silence. "None" and "the listing failed"
            // are different answers, and a caller that cannot tell them apart reports the second as the
            // first.
            builder.Append(listing.Header).AppendLine(": none. The data source was read and holds no such object.");

            return builder.ToString().TrimEnd();
        }

        builder.Append(listing.Header).Append(": ").Append(entries.Count).AppendLine(entries.Count == 1 ? " object." : " objects.");

        if (listing.TruncationNote is not null)
        {
            builder.AppendLine(listing.TruncationNote);
        }

        builder.AppendLine();

        for (var index = 0; index < entries.Count; index++)
        {
            // The entries were built in lockstep with the objects, so entry i was rendered from object i.
            // The object is still needed here for the detail its kind adds, which the entry does not carry.
            var entry = entries[index];
            var source = listing.Objects[index];

            if (entry.Label is not null)
            {
                builder.Append(entry.Label).Append(' ');
            }

            builder.Append(entry.Id);

            if (!string.IsNullOrWhiteSpace(entry.Title))
            {
                builder.Append(" - ").Append(entry.Title);
            }

            if (entry.PageStart.HasValue)
            {
                builder.Append(" (p. ").Append(entry.PageStart.Value).Append(')');
            }

            AppendKindDetail(builder, source);

            builder.AppendLine();
        }

        if (firstLabel is not null)
        {
            // The label is the whole instruction. Naming an address here would undo the point of having one,
            // because a model told to show a picture writes whatever address it was shown - near enough to
            // look right, wrong often enough to 404.
            builder.AppendLine();
            builder.Append("To show a figure in the answer, write its label exactly as printed above, on a line of its own - a line containing only ");
            builder.Append(firstLabel);
            builder.AppendLine(". Never write a URL for a figure: an address you write yourself will not resolve.");
        }

        if (unshowable.Count > 0)
        {
            // Named outright, because told only that a picture exists and given no way to show it, a model
            // asked to show one writes a plausible URL of its own invention and the reader gets a broken
            // image that looks like a real citation.
            builder.AppendLine();

            if (firstLabel is null)
            {
                builder.AppendLine("None of these figures has an address this host can serve. Describe them, or read one by identifier to get the picture; never invent a URL for one.");
            }
            else
            {
                builder.Append("These figures have no address this host can serve and cannot be shown: ");
                builder.Append(string.Join(", ", unshowable));
                builder.AppendLine(". Describe them; never invent a URL for one.");
            }
        }

        builder.AppendLine();
        builder.AppendLine("Read any one of these in full by passing its identifier to the knowledge object tool.");

        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// Appends what one kind of object adds to its line beyond a title and a page.
    /// </summary>
    /// <param name="builder">The builder.</param>
    /// <param name="entry">The object.</param>
    private static void AppendKindDetail(StringBuilder builder, KnowledgeObject entry)
    {
        if (entry.ObjectType == KnowledgeObjectTypes.Table &&
            entry.TryGet<TableDetails>(out var table) &&
            table.Columns is { Count: > 0 })
        {
            builder.Append(" - columns: ").Append(string.Join(", ", table.Columns));

            return;
        }

        if (entry.ObjectType is not (KnowledgeObjectTypes.Figure or KnowledgeObjectTypes.Chart))
        {
            return;
        }

        var confidence = entry.TryGet<ChartDetails>(out var chart) ? chart.ValueConfidence : null;

        if (string.Equals(confidence, ChartValueConfidence.Descriptive, StringComparison.OrdinalIgnoreCase))
        {
            // Said outright, because a number read off a picture by eye looks exactly like a number lifted
            // from the file's own geometry and only one of them is true.
            builder.Append("   values: descriptive - not machine-readable");
        }
        else if (!string.IsNullOrWhiteSpace(confidence))
        {
            builder.Append("   values: ").Append(confidence);
        }
        else if (entry.ObjectType == KnowledgeObjectTypes.Chart)
        {
            // Silence is not a claim of exactness. A chart whose numbers were lifted from the file's own
            // geometry says so explicitly, so if nothing says so they are treated as read off by eye.
            builder.Append("   values: unconfirmed - do not quote as exact");
        }
    }

    /// <summary>
    /// Resolves the address a person can open for a figure, through the same link resolver citations use.
    /// </summary>
    /// <param name="services">The request services.</param>
    /// <param name="entry">The figure.</param>
    /// <param name="logger">The logger.</param>
    /// <returns>The link, or <see langword="null"/> when the host exposes none.</returns>
    private static string ResolveLink(IServiceProvider services, KnowledgeObject entry, ILogger logger)
    {
        var resolver = services.GetKeyedService<IAIReferenceLinkResolver>(AIDataSourceSourceTypes.File);

        if (resolver is null)
        {
            return null;
        }

        try
        {
            return resolver.ResolveLink(entry.CanonicalId, new Dictionary<string, object>
            {
                ["Title"] = entry.Title,
                ["DataSourceId"] = entry.Source,
            });
        }
        catch (Exception ex)
        {
            // A link that cannot be built costs the picture's address, never the listing.
            logger.LogWarning(ex, "Failed to build a figure link for '{CanonicalId}'.", entry.CanonicalId);

            return null;
        }
    }

    /// <summary>
    /// Collects everything that hangs below one object, however many levels down.
    /// </summary>
    /// <param name="family">Every object in the document, which is the only place a descendant can be.</param>
    /// <param name="anchorId">The object to walk down from.</param>
    /// <returns>The descendants.</returns>
    /// <remarks>
    /// "Every figure in this article" means every figure the article contains, and a figure hangs off the
    /// article while a chunk of text hangs off the article too - so direct children alone would answer a
    /// containment question with one level of it.
    /// </remarks>
    private static List<KnowledgeObject> DescendantsOf(List<KnowledgeObject> family, string anchorId)
    {
        var byParent = new Dictionary<string, List<KnowledgeObject>>(StringComparer.Ordinal);

        foreach (var entry in family)
        {
            if (string.IsNullOrEmpty(entry.ParentId))
            {
                continue;
            }

            if (!byParent.TryGetValue(entry.ParentId, out var children))
            {
                children = [];
                byParent[entry.ParentId] = children;
            }

            children.Add(entry);
        }

        var found = new List<KnowledgeObject>();
        var seen = new HashSet<string>(StringComparer.Ordinal)
        {
            anchorId,
        };

        var pending = new Queue<string>();
        pending.Enqueue(anchorId);

        while (pending.Count > 0)
        {
            if (!byParent.TryGetValue(pending.Dequeue(), out var children))
            {
                continue;
            }

            foreach (var child in children)
            {
                // A parent chain that loops back on itself would otherwise walk forever. Nothing writes one,
                // which is exactly why nothing would notice one.
                if (!seen.Add(child.CanonicalId))
                {
                    continue;
                }

                found.Add(child);
                pending.Enqueue(child.CanonicalId);
            }
        }

        return found;
    }

    /// <summary>
    /// Puts the objects in the order a reader would meet them in the document.
    /// </summary>
    /// <param name="objects">The objects, sorted in place.</param>
    private static void SortForReading(List<KnowledgeObject> objects)
    {
        objects.Sort(static (left, right) =>
        {
            // An object with no page sorts last rather than first: a page number is the strongest ordering
            // there is, and letting a missing one win would scatter the pages that do have numbers.
            var byPage = (left.PageStart ?? int.MaxValue).CompareTo(right.PageStart ?? int.MaxValue);

            if (byPage != 0)
            {
                return byPage;
            }

            var byOrdinal = left.Ordinal.CompareTo(right.Ordinal);

            return byOrdinal != 0 ? byOrdinal : string.CompareOrdinal(left.CanonicalId, right.CanonicalId);
        });
    }

    /// <summary>
    /// Finds one object and checks it belongs to the configured data source.
    /// </summary>
    /// <param name="store">The knowledge object store.</param>
    /// <param name="canonicalId">The identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The object, or <see langword="null"/> when this data source has none.</returns>
    private async Task<KnowledgeObject> FindAsync(IKnowledgeObjectStore store, string canonicalId, CancellationToken cancellationToken)
    {
        var entry = await store.FindByCanonicalIdAsync(_settings.DataSourceId, canonicalId, cancellationToken);

        // The identifier says nothing about which data source it belongs to, so the answer has to. An object
        // from another data source reads as not found, never as a result.
        return IsInDataSource(entry) ? entry : null;
    }

    private bool IsInDataSource(KnowledgeObject entry)
    {
        return entry is not null && string.Equals(entry.Source, _settings.DataSourceId, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Determines whether an object may appear in a listing at all.
    /// </summary>
    /// <param name="entry">The object.</param>
    /// <returns><see langword="true"/> when it may be listed.</returns>
    /// <remarks>
    /// An excluded object is kept so its document stays complete and kept out of the index so it can never be
    /// returned as an answer. Enumerating one here would put it back in front of a model by another route,
    /// which is the whole thing the status exists to prevent.
    /// </remarks>
    private static bool IsListable(KnowledgeObject entry)
    {
        return !string.Equals(entry.Status, KnowledgeObjectStatus.Excluded, StringComparison.Ordinal);
    }

    private static bool IsWanted(KnowledgeObject entry, string[] kinds)
    {
        if (kinds.Length == 0)
        {
            return true;
        }

        foreach (var kind in kinds)
        {
            if (string.Equals(entry.ObjectType, kind, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string RootOf(KnowledgeObject entry)
    {
        // A document is its own root, and an object written before the root was carried says nothing; in
        // both cases the object's own identifier is the document to read.
        return string.IsNullOrWhiteSpace(entry.RootId) ? entry.CanonicalId : entry.RootId;
    }

    /// <summary>
    /// Works out which object types a requested kind covers.
    /// </summary>
    /// <param name="kind">The requested kind, or <see langword="null"/> for every kind.</param>
    /// <param name="kinds">The object types, or empty for every kind.</param>
    /// <param name="label">How to name those kinds in the answer.</param>
    /// <returns><see langword="true"/> when the kind is one this store holds.</returns>
    private static bool TryResolveKinds(string kind, out string[] kinds, out string label)
    {
        if (string.IsNullOrWhiteSpace(kind) ||
            kind.Equals("all", StringComparison.OrdinalIgnoreCase) ||
            kind.Equals("any", StringComparison.OrdinalIgnoreCase))
        {
            kinds = _anyKind;
            label = "Objects";

            return true;
        }

        // A chart is a figure that happens to carry series values, and the rest of this codebase treats the
        // pair as one thing wherever it shows pictures. Answering "list the figures" with everything except
        // the charts would be a listing that is wrong about the set it claims to describe.
        if (kind.Equals(KnowledgeObjectTypes.Figure, StringComparison.OrdinalIgnoreCase))
        {
            kinds = _figureKinds;
            label = "Figures";

            return true;
        }

        if (kind.Equals(KnowledgeObjectTypes.Chart, StringComparison.OrdinalIgnoreCase))
        {
            kinds = _chartKinds;
            label = "Charts";

            return true;
        }

        if (kind.Equals(KnowledgeObjectTypes.Table, StringComparison.OrdinalIgnoreCase))
        {
            kinds = _tableKinds;
            label = "Tables";

            return true;
        }

        if (kind.Equals(KnowledgeObjectTypes.Article, StringComparison.OrdinalIgnoreCase))
        {
            kinds = _articleKinds;
            label = "Articles";

            return true;
        }

        if (kind.Equals(KnowledgeObjectTypes.Text, StringComparison.OrdinalIgnoreCase))
        {
            kinds = _textKinds;
            label = "Text chunks";

            return true;
        }

        if (kind.Equals(KnowledgeObjectTypes.Document, StringComparison.OrdinalIgnoreCase))
        {
            kinds = _documentKinds;
            label = "Documents";

            return true;
        }

        kinds = null;
        label = null;

        return false;
    }

    /// <summary>
    /// Works out how many objects this call may return.
    /// </summary>
    /// <param name="requested">What the model asked for, or <see langword="null"/>.</param>
    /// <returns>The bound.</returns>
    private int ResolveLimit(int? requested)
    {
        var configured = _settings.MaxResults < 1
            ? KnowledgeObjectListToolConstants.DefaultMaxResults
            : Math.Min(_settings.MaxResults, KnowledgeObjectListToolConstants.MaxAllowedResults);

        if (requested is null || requested.Value < 1)
        {
            return configured;
        }

        return Math.Min(requested.Value, configured);
    }

    private static Listing Failed(string message)
    {
        return new Listing
        {
            Failure = message,
        };
    }

    private static string ReadString(AIFunctionArguments arguments, string name)
    {
        if (!arguments.TryGetValue(name, out var raw) || raw is null)
        {
            return null;
        }

        var value = raw switch
        {
            string text => text.Trim(),
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString()?.Trim(),
            JsonElement { ValueKind: JsonValueKind.Null } => null,
            _ => raw.ToString()?.Trim(),
        };

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static int? ReadInt(AIFunctionArguments arguments, string name)
    {
        if (!arguments.TryGetValue(name, out var raw) || raw is null)
        {
            return null;
        }

        return raw switch
        {
            int value => value,
            long value => (int)Math.Clamp(value, int.MinValue, int.MaxValue),
            JsonElement { ValueKind: JsonValueKind.Number } element when element.TryGetInt32(out var value) => value,
            JsonElement { ValueKind: JsonValueKind.String } element when int.TryParse(element.GetString(), out var value) => value,
            string text when int.TryParse(text, out var value) => value,
            _ => null,
        };
    }

    /// <summary>
    /// What one call worked out, before it is rendered: either the objects to list, or the one thing to say
    /// instead of listing them.
    /// </summary>
    private sealed class Listing
    {
        /// <summary>
        /// Gets the message to answer with instead of a listing, or <see langword="null"/> when there is one.
        /// </summary>
        public string Failure { get; init; }

        /// <summary>
        /// Gets the line that says what set these objects are.
        /// </summary>
        public string Header { get; init; }

        /// <summary>
        /// Gets the objects, in the order they are printed.
        /// </summary>
        public List<KnowledgeObject> Objects { get; init; } = [];

        /// <summary>
        /// Gets a value indicating whether more objects matched than were returned.
        /// </summary>
        public bool IsTruncated { get; init; }

        /// <summary>
        /// Gets the sentence that says the listing is short of the whole set, or <see langword="null"/> when
        /// it is not.
        /// </summary>
        public string TruncationNote { get; init; }
    }
}
