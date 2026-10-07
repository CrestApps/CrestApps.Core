using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using CrestApps.Core.AI.Ingestion;

namespace CrestApps.Core.AI.Documents.Presentations;

/// <summary>
/// The decks one conversation is working on, kept in the document file store so they survive between turns.
/// </summary>
/// <remarks>
/// Each deck is stored as the presentation package itself, so an uploaded deck keeps everything it came with
/// and an export is the package as it stands. The upload is never touched: the workspace works on a copy.
/// Earlier versions are kept beside it so a change can be undone, and the record of the decks carries the
/// house style and data links that make follow-up requests short.
/// </remarks>
internal sealed class PresentationWorkspace
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = false };

    private readonly IDocumentFileStore _store;
    private readonly string _folder;
    private readonly PresentationWorkspaceOptions _options;
    private readonly TimeProvider _timeProvider;

    private PresentationWorkspace(
        IDocumentFileStore store,
        string folder,
        PresentationWorkspaceOptions options,
        TimeProvider timeProvider,
        PresentationWorkspaceState state)
    {
        _store = store;
        _folder = folder;
        _options = options;
        _timeProvider = timeProvider;
        State = state;
    }

    /// <summary>
    /// Gets the record of the workspace's decks.
    /// </summary>
    public PresentationWorkspaceState State { get; }

    /// <summary>
    /// Gets the deck tools act on when none is named, or <see langword="null"/> when there are no decks.
    /// </summary>
    public PresentationDeckState ActiveDeck =>
        State.Decks.FirstOrDefault(deck => deck.Id == State.ActiveDeckId) ??
        (State.Decks.Count > 0 ? State.Decks[^1] : null);

    /// <summary>
    /// Takes the workspace's lock, so two tool calls in one turn cannot both read a deck, change it, and
    /// overwrite each other's change.
    /// </summary>
    /// <param name="folder">The workspace folder.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A handle that releases the lock when disposed.</returns>
    public static async Task<IDisposable> AcquireAsync(string folder, CancellationToken cancellationToken)
    {
        var semaphore = _locks.GetOrAdd(folder, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);

        return new Releaser(semaphore);
    }

    /// <summary>
    /// Loads a conversation's workspace, or starts an empty one.
    /// </summary>
    /// <param name="store">The document file store.</param>
    /// <param name="folder">The workspace folder.</param>
    /// <param name="options">The workspace options.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <returns>The workspace.</returns>
    public static async Task<PresentationWorkspace> LoadAsync(
        IDocumentFileStore store,
        string folder,
        PresentationWorkspaceOptions options,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrEmpty(folder);

        PresentationWorkspaceState state = null;

        await using (var stream = await store.GetFileAsync(PresentationWorkspaceStorage.Combine(folder, PresentationWorkspaceStorage.StateFileName)))
        {
            if (stream is not null)
            {
                try
                {
                    state = await JsonSerializer.DeserializeAsync<PresentationWorkspaceState>(stream, _jsonOptions);
                }
                catch (JsonException)
                {
                    // A record that cannot be read is treated as an empty workspace: the uploads are imported
                    // again, which is better than refusing every presentation request in the conversation.
                    state = null;
                }
            }
        }

        return new PresentationWorkspace(store, folder, options ?? new PresentationWorkspaceOptions(), timeProvider ?? TimeProvider.System, state ?? new PresentationWorkspaceState());
    }

    /// <summary>
    /// Finds a deck by identifier or name, or returns the active deck when no reference is given.
    /// </summary>
    /// <param name="reference">The deck's identifier or name.</param>
    /// <returns>The deck, or <see langword="null"/> when none matches.</returns>
    public PresentationDeckState FindDeck(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return ActiveDeck;
        }

        var trimmed = reference.Trim();

        return State.Decks.FirstOrDefault(deck => string.Equals(deck.Id, trimmed, StringComparison.OrdinalIgnoreCase))
            ?? State.Decks.FirstOrDefault(deck => string.Equals(deck.Name, trimmed, StringComparison.OrdinalIgnoreCase))
            ?? State.Decks.FirstOrDefault(deck => string.Equals(deck.FileName, trimmed, StringComparison.OrdinalIgnoreCase))
            ?? State.Decks.FirstOrDefault(deck => string.Equals(deck.SourceFileName, trimmed, StringComparison.OrdinalIgnoreCase))
            ?? State.Decks.FirstOrDefault(deck => deck.Name.Contains(trimmed, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Reads a deck's current package.
    /// </summary>
    /// <param name="deck">The deck.</param>
    /// <returns>The package, or <see langword="null"/> when its file is missing.</returns>
    public Task<byte[]> ReadAsync(PresentationDeckState deck)
    {
        ArgumentNullException.ThrowIfNull(deck);

        return ReadFileAsync(CurrentPath(deck));
    }

    /// <summary>
    /// Reads an earlier version of a deck.
    /// </summary>
    /// <param name="deck">The deck.</param>
    /// <param name="revision">The revision number.</param>
    /// <returns>The package, or <see langword="null"/> when that version is not kept.</returns>
    public Task<byte[]> ReadRevisionAsync(PresentationDeckState deck, int revision)
    {
        ArgumentNullException.ThrowIfNull(deck);

        if (revision == deck.Revision)
        {
            return ReadAsync(deck);
        }

        var entry = deck.History.FirstOrDefault(candidate => candidate.Revision == revision);

        return entry is null ? Task.FromResult<byte[]>(null) : ReadFileAsync(entry.Path);
    }

    /// <summary>
    /// Adds a deck and makes it the active one.
    /// </summary>
    /// <param name="name">The deck's name.</param>
    /// <param name="package">The package.</param>
    /// <param name="sourceDocumentId">The upload it came from, when it was imported.</param>
    /// <param name="sourceFileName">The name of that upload.</param>
    /// <returns>The deck.</returns>
    public async Task<PresentationDeckState> AddAsync(string name, byte[] package, string sourceDocumentId, string sourceFileName)
    {
        ArgumentNullException.ThrowIfNull(package);

        if (State.Decks.Count >= Math.Max(1, _options.MaxDecks))
        {
            throw new PresentationEditException($"This conversation already holds {State.Decks.Count.ToString(CultureInfo.InvariantCulture)} decks, the most it can. Work on one of them, or remove one first.");
        }

        var number = State.NextDeckNumber++;
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var displayName = UniqueName(string.IsNullOrWhiteSpace(name) ? "Presentation " + number.ToString(CultureInfo.InvariantCulture) : name.Trim());

        var deck = new PresentationDeckState
        {
            Id = "deck-" + number.ToString(CultureInfo.InvariantCulture),
            Name = displayName,
            FileName = ToFileName(displayName),
            SourceDocumentId = sourceDocumentId,
            SourceFileName = sourceFileName,
            CreatedUtc = now,
            ModifiedUtc = now,
            LastChange = sourceDocumentId is null ? "created" : "imported from " + sourceFileName,
        };

        await WriteFileAsync(CurrentPath(deck), package);

        State.Decks.Add(deck);
        State.ActiveDeckId = deck.Id;
        await SaveStateAsync();

        return deck;
    }

    /// <summary>
    /// Saves a new version of a deck, keeping the version it replaces for undo.
    /// </summary>
    /// <param name="deck">The deck.</param>
    /// <param name="package">The new package.</param>
    /// <param name="description">What the change did.</param>
    public async Task SaveAsync(PresentationDeckState deck, byte[] package, string description)
    {
        ArgumentNullException.ThrowIfNull(deck);
        ArgumentNullException.ThrowIfNull(package);

        if (_options.MaxRevisions > 0)
        {
            var current = await ReadAsync(deck);

            if (current is not null && current.Length <= _options.MaxRevisionPackageBytes)
            {
                var path = PresentationWorkspaceStorage.Combine(_folder, deck.Id + ".r" + deck.Revision.ToString(CultureInfo.InvariantCulture) + ".pptx");
                await WriteFileAsync(path, current);
                deck.History.Add(new PresentationDeckRevision { Revision = deck.Revision, Path = path, Description = description });
            }

            while (deck.History.Count > _options.MaxRevisions)
            {
                await _store.DeleteFileAsync(deck.History[0].Path);
                deck.History.RemoveAt(0);
            }
        }

        await WriteFileAsync(CurrentPath(deck), package);

        deck.Revision++;
        deck.ModifiedUtc = _timeProvider.GetUtcNow().UtcDateTime;
        deck.LastChange = description;
        State.ActiveDeckId = deck.Id;

        await SaveStateAsync();
    }

    /// <summary>
    /// Puts a deck back the way it was before its most recent changes.
    /// </summary>
    /// <param name="deck">The deck.</param>
    /// <param name="steps">How many changes to undo.</param>
    /// <returns>The descriptions of the changes undone, most recent first; empty when there was nothing to undo.</returns>
    public async Task<List<string>> UndoAsync(PresentationDeckState deck, int steps)
    {
        ArgumentNullException.ThrowIfNull(deck);

        var undone = new List<string>();
        var count = Math.Clamp(steps, 1, deck.History.Count);

        if (deck.History.Count == 0)
        {
            return undone;
        }

        var target = deck.History[^count];
        var package = await ReadFileAsync(target.Path);

        if (package is null)
        {
            return undone;
        }

        for (var index = deck.History.Count - 1; index >= deck.History.Count - count; index--)
        {
            undone.Add(deck.History[index].Description);
            await _store.DeleteFileAsync(deck.History[index].Path);
        }

        deck.History.RemoveRange(deck.History.Count - count, count);
        await WriteFileAsync(CurrentPath(deck), package);

        // The revision still moves forward, so anything cached against the undone version is not reused.
        deck.Revision++;
        deck.ModifiedUtc = _timeProvider.GetUtcNow().UtcDateTime;
        deck.LastChange = "undid: " + string.Join("; ", undone);
        await SaveStateAsync();

        return undone;
    }

    /// <summary>
    /// Removes a deck and every version of it.
    /// </summary>
    /// <param name="deck">The deck.</param>
    public async Task RemoveAsync(PresentationDeckState deck)
    {
        ArgumentNullException.ThrowIfNull(deck);

        await DeleteDeckFilesAsync(deck);
        State.Decks.Remove(deck);

        if (!string.IsNullOrEmpty(deck.SourceDocumentId) && !State.DismissedDocumentIds.Contains(deck.SourceDocumentId))
        {
            State.DismissedDocumentIds.Add(deck.SourceDocumentId);
        }

        if (State.ActiveDeckId == deck.Id)
        {
            State.ActiveDeckId = State.Decks.Count > 0 ? State.Decks[^1].Id : null;
        }

        await SaveStateAsync();
    }

    /// <summary>
    /// Removes a deck imported from an upload that no longer exists.
    /// </summary>
    /// <param name="documentId">The upload's identifier.</param>
    /// <returns><see langword="true"/> when the workspace changed: a deck was removed, or the upload is now dismissed.</returns>
    public async Task<bool> RemoveImportedAsync(string documentId)
    {
        // Recorded even when nothing was imported yet, so an upload removed while a tool call was loading the
        // conversation's documents is not imported after all.
        var removed = !string.IsNullOrEmpty(documentId) && !State.DismissedDocumentIds.Contains(documentId);

        if (removed)
        {
            State.DismissedDocumentIds.Add(documentId);
        }

        foreach (var deck in State.Decks.Where(deck => deck.SourceDocumentId == documentId).ToList())
        {
            await DeleteDeckFilesAsync(deck);
            State.Decks.Remove(deck);
            removed = true;
        }

        if (removed)
        {
            if (State.Decks.All(deck => deck.Id != State.ActiveDeckId))
            {
                State.ActiveDeckId = State.Decks.Count > 0 ? State.Decks[^1].Id : null;
            }

            await SaveStateAsync();
        }

        return removed;
    }

    /// <summary>
    /// Deletes every file of the workspace.
    /// </summary>
    public async Task DeleteAllAsync()
    {
        foreach (var deck in State.Decks.ToList())
        {
            await DeleteDeckFilesAsync(deck);
        }

        State.Decks.Clear();
        await _store.DeleteFileAsync(PresentationWorkspaceStorage.Combine(_folder, PresentationWorkspaceStorage.StateFileName));
    }

    /// <summary>
    /// Writes the record of the workspace's decks.
    /// </summary>
    public async Task SaveStateAsync()
    {
        using var stream = new MemoryStream();
        await JsonSerializer.SerializeAsync(stream, State, _jsonOptions);
        stream.Position = 0;

        await _store.SaveFileAsync(PresentationWorkspaceStorage.Combine(_folder, PresentationWorkspaceStorage.StateFileName), stream);
    }

    /// <summary>
    /// Renames a deck, which also renames the file it exports as.
    /// </summary>
    /// <param name="deck">The deck.</param>
    /// <param name="name">The new name.</param>
    public void Rename(PresentationDeckState deck, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || string.Equals(deck.Name, name.Trim(), StringComparison.Ordinal))
        {
            return;
        }

        deck.Name = UniqueName(name.Trim(), deck);
        deck.FileName = ToFileName(deck.Name);
    }

    private async Task DeleteDeckFilesAsync(PresentationDeckState deck)
    {
        await _store.DeleteFileAsync(CurrentPath(deck));

        foreach (var revision in deck.History)
        {
            await _store.DeleteFileAsync(revision.Path);
        }
    }

    private string CurrentPath(PresentationDeckState deck)
    {
        return PresentationWorkspaceStorage.Combine(_folder, deck.Id + ".pptx");
    }

    private async Task<byte[]> ReadFileAsync(string path)
    {
        await using var stream = await _store.GetFileAsync(path);

        if (stream is null)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);

        return buffer.ToArray();
    }

    private async Task WriteFileAsync(string path, byte[] package)
    {
        using var stream = new MemoryStream(package, writable: false);
        await _store.SaveFileAsync(path, stream);
    }

    private string UniqueName(string name, PresentationDeckState except = null)
    {
        var candidate = name.Length > 80 ? name[..80].Trim() : name;
        var baseName = candidate;
        var suffix = 2;

        while (State.Decks.Any(deck => !ReferenceEquals(deck, except) && string.Equals(deck.Name, candidate, StringComparison.OrdinalIgnoreCase)))
        {
            candidate = baseName + " (" + suffix++.ToString(CultureInfo.InvariantCulture) + ")";
        }

        return candidate;
    }

    private static string ToFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim().Trim('.');

        return (string.IsNullOrEmpty(cleaned) ? "presentation" : cleaned) + ".pptx";
    }

    private sealed class Releaser : IDisposable
    {
        private SemaphoreSlim _semaphore;

        public Releaser(SemaphoreSlim semaphore)
        {
            _semaphore = semaphore;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _semaphore, null)?.Release();
        }
    }
}
