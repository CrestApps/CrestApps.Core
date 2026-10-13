using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.AI.Animations.Storage;

/// <summary>
/// Keeps animations on the local disk under <see cref="AnimationOptions.BasePath"/>, one folder per conversation
/// and one per animation: <c>{referenceType}/{referenceId}/{animationId}/</c>.
/// </summary>
/// <remarks>
/// Every part of a path is checked before it is used, and the animation identifier and the file names are the
/// store's own, so nothing a request or the model supplies can reach outside the base folder.
/// <para>
/// Files are opened for reading with delete sharing: a player keeps the video open while it streams, and clearing
/// the conversation must still be able to remove it.
/// </para>
/// </remarks>
internal sealed partial class FileSystemAnimationStore : IAnimationStore
{
    private const string VideoFileName = "video.mp4";
    private const string PosterFileName = "poster.png";
    private const string SettingsFileName = "settings.json";
    private const string ScriptFileName = "code.js";

    private readonly string _basePath;
    private readonly ILogger<FileSystemAnimationStore> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileSystemAnimationStore"/> class.
    /// </summary>
    /// <param name="options">The animation options.</param>
    /// <param name="logger">The logger.</param>
    public FileSystemAnimationStore(
        IOptions<AnimationOptions> options,
        ILogger<FileSystemAnimationStore> logger)
    {
        _basePath = Path.GetFullPath(options.Value.BasePath);
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> SaveAsync(AnimationOwner owner, AnimationContent content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(content);

        var animationId = Guid.NewGuid().ToString("N");
        var folder = AnimationFolder(owner, animationId);

        Directory.CreateDirectory(folder);

        try
        {
            File.Move(content.VideoFilePath, Path.Combine(folder, VideoFileName));
            await File.WriteAllBytesAsync(Path.Combine(folder, PosterFileName), content.Poster, cancellationToken);

            if (content.SettingsJson is not null)
            {
                await File.WriteAllTextAsync(Path.Combine(folder, SettingsFileName), content.SettingsJson, cancellationToken);
            }

            if (content.Script is not null)
            {
                await File.WriteAllTextAsync(Path.Combine(folder, ScriptFileName), content.Script, cancellationToken);
            }
        }
        catch
        {
            // A half-written animation is worse than none: the endpoint would serve a poster for a video that
            // is not there.
            Directory.Delete(folder, recursive: true);

            throw;
        }

        return animationId;
    }

    /// <inheritdoc />
    public Task<Stream> OpenReadAsync(AnimationOwner owner, string animationId, AnimationAsset asset, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);

        if (!IsAnimationId(animationId) || !IsSafeSegment(owner.ReferenceType) || !IsSafeSegment(owner.ReferenceId))
        {
            return Task.FromResult<Stream>(null);
        }

        var fileName = asset == AnimationAsset.Poster ? PosterFileName : VideoFileName;
        var path = Path.Combine(AnimationFolder(owner, animationId), fileName);

        if (!File.Exists(path))
        {
            return Task.FromResult<Stream>(null);
        }

        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, bufferSize: 81_920, FileOptions.Asynchronous | FileOptions.SequentialScan);

        return Task.FromResult(stream);
    }

    /// <inheritdoc />
    public async Task<AnimationSource> GetLatestSourceAsync(AnimationOwner owner, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);

        if (!IsSafeSegment(owner.ReferenceType) || !IsSafeSegment(owner.ReferenceId))
        {
            return null;
        }

        var conversationFolder = new DirectoryInfo(Path.Combine(_basePath, owner.ReferenceType, owner.ReferenceId));

        if (!conversationFolder.Exists)
        {
            return null;
        }

        var latest = conversationFolder
            .EnumerateDirectories()
            .Where(folder => IsAnimationId(folder.Name))
            .OrderByDescending(folder => folder.CreationTimeUtc)
            .FirstOrDefault();

        if (latest is null)
        {
            return null;
        }

        var scriptPath = Path.Combine(latest.FullName, ScriptFileName);
        var settingsPath = Path.Combine(latest.FullName, SettingsFileName);

        return new AnimationSource(
            latest.Name,
            File.Exists(settingsPath) ? await File.ReadAllTextAsync(settingsPath, cancellationToken) : null,
            File.Exists(scriptPath) ? await File.ReadAllTextAsync(scriptPath, cancellationToken) : null);
    }

    /// <inheritdoc />
    public Task DeleteAllAsync(AnimationOwner owner, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);

        if (!IsSafeSegment(owner.ReferenceType) || !IsSafeSegment(owner.ReferenceId))
        {
            return Task.CompletedTask;
        }

        var folder = Path.Combine(_basePath, owner.ReferenceType, owner.ReferenceId);

        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Called while a conversation's history is cleared or the conversation is deleted. A file that cannot be
            // removed must not stop that, so it is reported and left behind.
            _logger.LogWarning(ex, "Could not delete the animations stored in '{Folder}'.", folder);
        }

        return Task.CompletedTask;
    }

    private string AnimationFolder(AnimationOwner owner, string animationId)
    {
        if (!IsSafeSegment(owner.ReferenceType) || !IsSafeSegment(owner.ReferenceId) || !IsAnimationId(animationId))
        {
            throw new ArgumentException($"'{owner.ReferenceType}/{owner.ReferenceId}/{animationId}' is not a valid animation location.", nameof(owner));
        }

        return Path.Combine(_basePath, owner.ReferenceType, owner.ReferenceId, animationId);
    }

    private static bool IsSafeSegment(string segment)
    {
        return !string.IsNullOrEmpty(segment) && segment != "." && segment != ".." && SafeSegmentPattern().IsMatch(segment);
    }

    private static bool IsAnimationId(string animationId)
    {
        return animationId?.Length == 32 && Guid.TryParseExact(animationId, "N", out _);
    }

    [GeneratedRegex("^[a-zA-Z0-9._-]+$")]
    private static partial Regex SafeSegmentPattern();
}
