using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json.Nodes;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Services;
using CrestApps.Core.Handlers;
using CrestApps.Core.Models;
using CrestApps.Core.Support;
using Microsoft.AspNetCore.Http;

namespace CrestApps.Core.Blazor.Web.Areas.AI.Handlers;

public sealed class AIMemoryEntryIndexingHandler : CatalogEntryHandlerBase<AIMemoryEntry>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TimeProvider _timeProvider;
    private readonly AIMemoryIndexingService _indexingService;
    private readonly ILogger<AIMemoryEntryIndexingHandler> _logger;

    public AIMemoryEntryIndexingHandler(
        IHttpContextAccessor httpContextAccessor,
        TimeProvider timeProvider,
        AIMemoryIndexingService indexingService,
        ILogger<AIMemoryEntryIndexingHandler> logger)
    {
        _httpContextAccessor = httpContextAccessor;
        _timeProvider = timeProvider;
        _indexingService = indexingService;
        _logger = logger;
    }

    public override Task InitializingAsync(InitializingContext<AIMemoryEntry> context, CancellationToken cancellationToken = default)
        => PopulateAsync(context.Model, context.Data);

    public override Task UpdatingAsync(UpdatingContext<AIMemoryEntry> context, CancellationToken cancellationToken = default)
        => PopulateAsync(context.Model, context.Data);

    public override Task InitializedAsync(InitializedContext<AIMemoryEntry> context, CancellationToken cancellationToken = default)
    {
        EnsureCreatedDefaults(context.Model);

        return Task.CompletedTask;
    }

    public override Task CreatingAsync(CreatingContext<AIMemoryEntry> context, CancellationToken cancellationToken = default)
    {
        EnsureCreatedDefaults(context.Model);

        return Task.CompletedTask;
    }

    public override Task ValidatingAsync(ValidatingContext<AIMemoryEntry> context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(context.Model.UserId))
        {
            context.Result.Fail(new ValidationResult("User ID is required.", [nameof(AIMemoryEntry.UserId)]));
        }

        if (string.IsNullOrWhiteSpace(context.Model.Name))
        {
            context.Result.Fail(new ValidationResult("Name is required.", [nameof(AIMemoryEntry.Name)]));
        }

        if (string.IsNullOrWhiteSpace(context.Model.Content))
        {
            context.Result.Fail(new ValidationResult("Content is required.", [nameof(AIMemoryEntry.Content)]));
        }

        return Task.CompletedTask;
    }

    public override async Task CreatedAsync(CreatedContext<AIMemoryEntry> context, CancellationToken cancellationToken = default)
    {
        await IndexAsync(context.Model);
    }

    public override async Task UpdatedAsync(UpdatedContext<AIMemoryEntry> context, CancellationToken cancellationToken = default)
    {
        await IndexAsync(context.Model);
    }

    public override async Task DeletedAsync(DeletedContext<AIMemoryEntry> context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(context.Model.ItemId))
        {
            return;
        }

        try
        {
            await _indexingService.DeleteAsync([context.Model.ItemId], cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove AI memory '{MemoryId}' from the configured memory index.", context.Model.ItemId);
        }
    }

    private async Task IndexAsync(AIMemoryEntry memory)
    {
        try
        {
            await _indexingService.IndexAsync(memory);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to index AI memory '{MemoryId}' into the configured memory index.", memory.ItemId);
        }
    }

    private void EnsureCreatedDefaults(AIMemoryEntry memory)
    {
        if (memory.CreatedUtc == default)
        {
            memory.CreatedUtc = _timeProvider.GetUtcNow().UtcDateTime;
        }

        if (memory.UpdatedUtc == default)
        {
            memory.UpdatedUtc = memory.CreatedUtc;
        }

        var user = _httpContextAccessor.HttpContext?.User;

        if (user != null && string.IsNullOrWhiteSpace(memory.UserId))
        {
            memory.UserId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.Identity?.Name;
        }
    }

    private static Task PopulateAsync(AIMemoryEntry memory, JsonNode data)
    {
        if (data is not JsonObject json)
        {
            return Task.CompletedTask;
        }

        json.TryUpdateTrimmedStringValue(nameof(AIMemoryEntry.UserId), value => memory.UserId = value);
        json.TryUpdateTrimmedStringValue(nameof(AIMemoryEntry.Name), value => memory.Name = value);
        json.TryUpdateTrimmedStringValue(nameof(AIMemoryEntry.Description), value => memory.Description = value);
        json.TryUpdateTrimmedStringValue(nameof(AIMemoryEntry.Content), value => memory.Content = value);

        if (json.TryGetDateTimeValue(nameof(AIMemoryEntry.CreatedUtc), out var createdUtc))
        {
            memory.CreatedUtc = createdUtc;
        }

        if (json.TryGetDateTimeValue(nameof(AIMemoryEntry.UpdatedUtc), out var updatedUtc))
        {
            memory.UpdatedUtc = updatedUtc;
        }

        return Task.CompletedTask;
    }
}
