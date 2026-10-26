using System.Collections;

namespace CrestApps.Core.AI.Documents.Pdf.Objects;

/// <summary>
/// The entries of a <see cref="PdfDictionary"/>, keyed by name with its leading slash, as <c>/Type</c>, in
/// the order they were added.
/// </summary>
/// <remarks>
/// Reading an entry returns it as stored, so an entry stored elsewhere in the file is a
/// <see cref="PdfReference"/>; the typed getters follow references.
/// </remarks>
internal sealed class PdfDictionaryElements : IEnumerable<KeyValuePair<string, PdfItem>>
{
    private readonly PdfDictionary _owner;
    private readonly OrderedDictionary<string, PdfItem> _entries = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfDictionaryElements"/> class.
    /// </summary>
    /// <param name="owner">The dictionary the entries belong to.</param>
    public PdfDictionaryElements(PdfDictionary owner)
    {
        _owner = owner;
    }

    /// <summary>
    /// Gets the number of entries.
    /// </summary>
    public int Count => _entries.Count;

    /// <summary>
    /// Gets the keys, in order.
    /// </summary>
    public IEnumerable<string> Keys => _entries.Keys;

    /// <summary>
    /// Gets the values, in order, as stored.
    /// </summary>
    public IEnumerable<PdfItem> Values => _entries.Values;

    /// <summary>
    /// Gets or sets an entry as stored. Reading a missing key returns <see langword="null"/>, and setting
    /// <see langword="null"/> removes the entry.
    /// </summary>
    /// <param name="key">The key, with its slash.</param>
    /// <returns>The value.</returns>
    public PdfItem this[string key]
    {
        get
        {
            return _entries.TryGetValue(Normalize(key), out var value)
                ? value
                : null;
        }

        set
        {
            if (value is null)
            {
                _entries.Remove(Normalize(key));

                return;
            }

            _entries[Normalize(key)] = value;
        }
    }

    /// <summary>
    /// Returns whether the dictionary has an entry.
    /// </summary>
    /// <param name="key">The key, with its slash.</param>
    /// <returns><see langword="true"/> when the entry exists.</returns>
    public bool ContainsKey(string key)
    {
        return _entries.ContainsKey(Normalize(key));
    }

    /// <summary>
    /// Reads an entry as stored.
    /// </summary>
    /// <param name="key">The key, with its slash.</param>
    /// <param name="value">The value.</param>
    /// <returns><see langword="true"/> when the entry exists.</returns>
    public bool TryGetValue(string key, out PdfItem value)
    {
        return _entries.TryGetValue(Normalize(key), out value);
    }

    /// <summary>
    /// Adds or replaces an entry.
    /// </summary>
    /// <param name="key">The key, with its slash.</param>
    /// <param name="value">The value.</param>
    public void Add(string key, PdfItem value)
    {
        this[key] = value;
    }

    /// <summary>
    /// Removes an entry.
    /// </summary>
    /// <param name="key">The key, with its slash.</param>
    /// <returns><see langword="true"/> when the entry existed.</returns>
    public bool Remove(string key)
    {
        return _entries.Remove(Normalize(key));
    }

    /// <summary>
    /// Removes every entry.
    /// </summary>
    public void Clear()
    {
        _entries.Clear();
    }

    /// <summary>
    /// Reads an entry, following a reference to the object it names.
    /// </summary>
    /// <param name="key">The key, with its slash.</param>
    /// <returns>The value, or <see langword="null"/> when the entry is missing.</returns>
    public PdfItem GetResolved(string key)
    {
        return Resolve(this[key]);
    }

    /// <summary>
    /// Reads a name entry.
    /// </summary>
    /// <param name="key">The key, with its slash.</param>
    /// <returns>The name with its slash, or an empty string when the entry is missing or not a name.</returns>
    public string GetName(string key)
    {
        return GetResolved(key) is PdfName name
            ? name.Value
            : string.Empty;
    }

    /// <summary>
    /// Reads a text entry.
    /// </summary>
    /// <param name="key">The key, with its slash.</param>
    /// <returns>The text, or an empty string when the entry is missing or not a string.</returns>
    public string GetString(string key)
    {
        return GetResolved(key) is PdfString text
            ? text.Value
            : string.Empty;
    }

    /// <summary>
    /// Reads a whole number entry.
    /// </summary>
    /// <param name="key">The key, with its slash.</param>
    /// <returns>The number, or 0 when the entry is missing or not a number.</returns>
    public int GetInteger(string key)
    {
        return GetResolved(key) switch
        {
            PdfInteger number => (int)Math.Clamp(number.Value, int.MinValue, int.MaxValue),
            PdfReal number => (int)Math.Clamp(number.Value, int.MinValue, int.MaxValue),
            _ => 0,
        };
    }

    /// <summary>
    /// Reads a number entry.
    /// </summary>
    /// <param name="key">The key, with its slash.</param>
    /// <returns>The number, or 0 when the entry is missing or not a number.</returns>
    public double GetReal(string key)
    {
        return GetResolved(key) switch
        {
            PdfInteger number => number.Value,
            PdfReal number => number.Value,
            _ => 0,
        };
    }

    /// <summary>
    /// Reads a boolean entry.
    /// </summary>
    /// <param name="key">The key, with its slash.</param>
    /// <returns>The value, or <see langword="false"/> when the entry is missing or not a boolean.</returns>
    public bool GetBoolean(string key)
    {
        return GetResolved(key) is PdfBoolean { Value: true };
    }

    /// <summary>
    /// Reads a dictionary entry, following a reference.
    /// </summary>
    /// <param name="key">The key, with its slash.</param>
    /// <returns>The dictionary, or <see langword="null"/>.</returns>
    public PdfDictionary GetDictionary(string key)
    {
        return GetResolved(key) as PdfDictionary;
    }

    /// <summary>
    /// Reads an array entry, following a reference.
    /// </summary>
    /// <param name="key">The key, with its slash.</param>
    /// <returns>The array, or <see langword="null"/>.</returns>
    public PdfArray GetArray(string key)
    {
        return GetResolved(key) as PdfArray;
    }

    /// <summary>
    /// Reads an entry that refers to an object stored elsewhere.
    /// </summary>
    /// <param name="key">The key, with its slash.</param>
    /// <returns>The reference, or <see langword="null"/> when the entry is missing or stored directly.</returns>
    public PdfReference GetReference(string key)
    {
        return this[key] as PdfReference;
    }

    /// <summary>
    /// Reads an array or dictionary entry, following a reference.
    /// </summary>
    /// <param name="key">The key, with its slash.</param>
    /// <returns>The object, or <see langword="null"/>.</returns>
    public PdfObject GetObject(string key)
    {
        return GetResolved(key) as PdfObject;
    }

    /// <summary>
    /// Reads a date entry.
    /// </summary>
    /// <param name="key">The key, with its slash.</param>
    /// <returns>The date, or <see langword="null"/> when the entry is missing or not a date.</returns>
    public DateTimeOffset? GetDateTime(string key)
    {
        return GetResolved(key) is PdfString text && PdfTextEncoding.TryParseDate(text.Value, out var value)
            ? value
            : null;
    }

    /// <summary>
    /// Sets a name entry.
    /// </summary>
    /// <param name="key">The key, with its slash.</param>
    /// <param name="value">The name, with or without its slash.</param>
    public void SetName(string key, string value)
    {
        this[key] = new PdfName(value);
    }

    /// <summary>
    /// Sets a text entry.
    /// </summary>
    /// <param name="key">The key, with its slash.</param>
    /// <param name="value">The text.</param>
    public void SetString(string key, string value)
    {
        this[key] = new PdfString(value);
    }

    /// <summary>
    /// Sets a whole number entry.
    /// </summary>
    /// <param name="key">The key, with its slash.</param>
    /// <param name="value">The number.</param>
    public void SetInteger(string key, long value)
    {
        this[key] = new PdfInteger(value);
    }

    /// <summary>
    /// Sets a number entry.
    /// </summary>
    /// <param name="key">The key, with its slash.</param>
    /// <param name="value">The number.</param>
    public void SetReal(string key, double value)
    {
        this[key] = new PdfReal(value);
    }

    /// <summary>
    /// Sets a boolean entry.
    /// </summary>
    /// <param name="key">The key, with its slash.</param>
    /// <param name="value">The value.</param>
    public void SetBoolean(string key, bool value)
    {
        this[key] = value
            ? PdfBoolean.True
            : PdfBoolean.False;
    }

    /// <summary>
    /// Sets a date entry.
    /// </summary>
    /// <param name="key">The key, with its slash.</param>
    /// <param name="value">The date.</param>
    public void SetDateTime(string key, DateTimeOffset value)
    {
        this[key] = new PdfString(PdfTextEncoding.FormatDate(value));
    }

    /// <summary>
    /// Sets an entry to a reference to an object, storing the object on its own in the document first when
    /// it is not already.
    /// </summary>
    /// <param name="key">The key, with its slash.</param>
    /// <param name="value">The object.</param>
    public void SetReference(string key, PdfObject value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.Reference is null)
        {
            var document = value.Owner ?? _owner.Owner ?? throw new InvalidOperationException("The object belongs to no document, so it cannot be stored on its own.");

            document.AddObject(value);
        }

        this[key] = value.Reference;
    }

    /// <inheritdoc/>
    public IEnumerator<KeyValuePair<string, PdfItem>> GetEnumerator()
    {
        return _entries.GetEnumerator();
    }

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    private static PdfItem Resolve(PdfItem item)
    {
        for (var depth = 0; depth < 16 && item is PdfReference reference; depth++)
        {
            item = reference.Value;
        }

        return item is PdfReference or PdfNull
            ? null
            : item;
    }

    private static string Normalize(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        return key[0] == '/'
            ? key
            : "/" + key;
    }
}
