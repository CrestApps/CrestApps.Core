namespace CrestApps.Core.AI.Models;

/// <summary>
/// Runtime options for AI Data Source retrieval behavior.
/// </summary>
public sealed class AIDataSourceOptions
{
    private readonly Dictionary<string, Dictionary<string, DataSourceFieldMapping>> _fieldMappings = new(StringComparer.OrdinalIgnoreCase);

    public const int MinStrictness = 1;

    public const int MaxStrictness = 5;

    public const int MinTopNDocuments = 3;

    public const int MaxTopNDocuments = 20;

    /// <summary>
    /// The minimum score demanded at maximum strictness, which anchors the whole strictness scale.
    /// </summary>
    /// <remarks>
    /// Cosine similarity does not use the whole zero-to-one range. For a modern embedding model, text that
    /// genuinely answers a question typically scores in the low tenths against a short query, and only a
    /// near-verbatim restatement approaches the high end. Anchoring the narrowest level here keeps every
    /// level inside the range real results occupy.
    /// </remarks>
    public const float DefaultStrictestMinimumScore = 0.4f;

    /// <summary>
    /// Gets or sets the default Strictness.
    /// </summary>
    public int DefaultStrictness { get; set; } = 3;

    /// <summary>
    /// Gets or sets the default Top N Documents.
    /// </summary>
    public int DefaultTopNDocuments { get; set; } = 5;

    /// <summary>
    /// Adds field mapping.
    /// </summary>
    /// <param name="providerName">The provider name.</param>
    /// <param name="indexProfileType">The index profile type.</param>
    /// <param name="configure">The action used to configure.</param>
    public void AddFieldMapping(string providerName, string indexProfileType, Action<DataSourceFieldMapping> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(indexProfileType);
        ArgumentNullException.ThrowIfNull(configure);
        if (!_fieldMappings.TryGetValue(providerName, out var providerMappings))
        {
            providerMappings = new Dictionary<string, DataSourceFieldMapping>(StringComparer.OrdinalIgnoreCase);
            _fieldMappings[providerName] = providerMappings;
        }

        if (!providerMappings.TryGetValue(indexProfileType, out var mapping))
        {
            mapping = new DataSourceFieldMapping();
            providerMappings[indexProfileType] = mapping;
        }

        configure(mapping);
    }

    /// <summary>
    /// Gets field mapping.
    /// </summary>
    /// <param name="providerName">The provider name.</param>
    /// <param name="indexProfileType">The index profile type.</param>
    public DataSourceFieldMapping GetFieldMapping(string providerName, string indexProfileType)
    {
        if (string.IsNullOrWhiteSpace(providerName) || string.IsNullOrWhiteSpace(indexProfileType))
        {
            return null;
        }

        return _fieldMappings.TryGetValue(providerName, out var providerMappings) && providerMappings.TryGetValue(indexProfileType, out var mapping) ? mapping : null;
    }

    /// <summary>
    /// Gets top n documents.
    /// </summary>
    /// <param name="topN">The top n.</param>
    public int GetTopNDocuments(int? topN)
    {
        if (topN >= MinTopNDocuments && topN <= MaxTopNDocuments)
        {
            return topN.Value;
        }

        if (DefaultTopNDocuments >= MinTopNDocuments && DefaultTopNDocuments <= MaxTopNDocuments)
        {
            return DefaultTopNDocuments;
        }

        return 5;
    }

    /// <summary>
    /// Gets strictness.
    /// </summary>
    /// <param name="strictness">The strictness.</param>
    public int GetStrictness(int? strictness)
    {
        if (strictness >= MinStrictness && strictness <= MaxStrictness)
        {
            return strictness.Value;
        }

        if (DefaultStrictness >= MinStrictness && DefaultStrictness <= MaxStrictness)
        {
            return DefaultStrictness;
        }

        return 3;
    }

    /// <summary>
    /// Gets or sets the minimum score demanded at the narrowest strictness level.
    /// </summary>
    /// <remarks>
    /// This is the top of the scale that <see cref="GetMinimumScore"/> interpolates towards, and it is the
    /// one number to move when a corpus or an embedding model behaves differently from the default
    /// assumption. Raising it towards 1 restores the old behaviour of demanding near-identical text.
    /// </remarks>
    public float StrictestMinimumScore { get; set; } = DefaultStrictestMinimumScore;

    /// <summary>
    /// Gets the minimum vector-search score required for a result to pass the current strictness level.
    /// Strictness 1 keeps the broadest recall, while 5 applies the narrowest filter.
    /// </summary>
    /// <param name="strictness">The configured strictness override.</param>
    /// <remarks>
    /// The levels are spread evenly from no floor at all up to <see cref="StrictestMinimumScore"/>, so the
    /// default level sits inside the range real matches occupy rather than above it.
    /// <para>
    /// The previous mapping divided by the number of levels, demanding 0.4 at the default level and 0.8 at
    /// the narrowest. Cosine similarity does not use the whole zero-to-one range: measured against a real
    /// ingested corpus, the best-scoring result for any query — including one naming exactly what the top
    /// hit depicted — reached only about 0.27. Every level from the default upwards was therefore
    /// unreachable, and retrieval returned nothing for every query while reporting an ordinary empty
    /// result. Ranking was never the problem; the floor was.
    /// </para>
    /// </remarks>
    public float GetMinimumScore(int? strictness)
    {
        var resolvedStrictness = GetStrictness(strictness);
        var ceiling = Math.Clamp(StrictestMinimumScore, 0f, 1f);

        // MaxStrictness - 1 rather than MaxStrictness, so the narrowest level lands exactly on the ceiling
        // instead of short of it.
        return Math.Max(0f, (resolvedStrictness - 1f) / (MaxStrictness - 1f) * ceiling);
    }
}
