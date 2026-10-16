using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json.Nodes;
using CrestApps.Core.AI.Models;
using CrestApps.Core.Handlers;
using CrestApps.Core.Models;
using CrestApps.Core.Services;
using CrestApps.Core.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Localization;

namespace CrestApps.Core.AI.Handlers;

/// <summary>
/// Fills, stamps and validates AI personas as they are created and updated.
/// </summary>
internal sealed class AIPersonaCatalogHandler : CatalogEntryHandlerBase<AIPersona>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TimeProvider _timeProvider;
    private readonly INamedCatalog<AIPersona> _personasCatalog;

    internal readonly IStringLocalizer S;

    public AIPersonaCatalogHandler(
        IHttpContextAccessor httpContextAccessor,
        TimeProvider timeProvider,
        INamedCatalog<AIPersona> personasCatalog,
        IStringLocalizer<AIPersonaCatalogHandler> stringLocalizer)
    {
        _httpContextAccessor = httpContextAccessor;
        _timeProvider = timeProvider;
        _personasCatalog = personasCatalog;
        S = stringLocalizer;
    }

    public override Task InitializingAsync(InitializingContext<AIPersona> context, CancellationToken cancellationToken = default)
    {
        Populate(context.Model, context.Data, isNew: true);

        return Task.CompletedTask;
    }

    public override Task UpdatingAsync(UpdatingContext<AIPersona> context, CancellationToken cancellationToken = default)
    {
        // The technical name is how profiles and interactions link to the persona, so an update never changes it.
        Populate(context.Model, context.Data, isNew: false);
        context.Model.ModifiedUtc = _timeProvider.GetUtcNow().UtcDateTime;

        return Task.CompletedTask;
    }

    public override Task InitializedAsync(InitializedContext<AIPersona> context, CancellationToken cancellationToken = default)
    {
        EnsureCreatedDefaults(context.Model);

        return Task.CompletedTask;
    }

    public override Task CreatingAsync(CreatingContext<AIPersona> context, CancellationToken cancellationToken = default)
    {
        EnsureCreatedDefaults(context.Model);

        return Task.CompletedTask;
    }

    public override async Task ValidatingAsync(ValidatingContext<AIPersona> context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(context.Model.Name))
        {
            context.Result.Fail(new ValidationResult(S["Persona name is required."], [nameof(AIPersona.Name)]));
        }
        else
        {
            var existing = await _personasCatalog.FindByNameAsync(context.Model.Name, cancellationToken);

            if (existing is not null && !string.Equals(existing.ItemId, context.Model.ItemId, StringComparison.Ordinal))
            {
                context.Result.Fail(new ValidationResult(S["A persona with this name already exists. The name must be unique."], [nameof(AIPersona.Name)]));
            }
        }

        if (string.IsNullOrWhiteSpace(context.Model.Instructions))
        {
            context.Result.Fail(new ValidationResult(S["Persona instructions are required."], [nameof(AIPersona.Instructions)]));
        }
    }

    private void EnsureCreatedDefaults(AIPersona persona)
    {
        if (persona.CreatedUtc == default)
        {
            persona.CreatedUtc = _timeProvider.GetUtcNow().UtcDateTime;
        }

        if (string.IsNullOrWhiteSpace(persona.DisplayText))
        {
            persona.DisplayText = persona.Name;
        }

        var user = _httpContextAccessor.HttpContext?.User;

        if (user is null)
        {
            return;
        }

        persona.OwnerId ??= user.FindFirstValue(ClaimTypes.NameIdentifier);
        persona.Author ??= user.Identity?.Name;
    }

    private static void Populate(AIPersona persona, JsonNode data, bool isNew)
    {
        if (data is not JsonObject json)
        {
            return;
        }

        if (isNew)
        {
            json.TryUpdateTrimmedStringValue(nameof(AIPersona.Name), value => persona.Name = value);
        }

        json.TryUpdateTrimmedStringValue(nameof(AIPersona.DisplayText), value => persona.DisplayText = value);
        json.TryUpdateTrimmedStringValue(nameof(AIPersona.Description), value => persona.Description = value);
        json.TryUpdateTrimmedStringValue(nameof(AIPersona.Instructions), value => persona.Instructions = value);
        json.TryUpdateTrimmedStringValue(nameof(AIPersona.AssistantName), value => persona.AssistantName = value);
        json.TryUpdateTrimmedStringValue(nameof(AIPersona.Language), value => persona.Language = value);
        json.TryUpdateTrimmedStringValue(nameof(AIPersona.VoiceName), value => persona.VoiceName = value);
        json.TryUpdateTrimmedStringValue(nameof(AIPersona.VoiceDeploymentName), value => persona.VoiceDeploymentName = value);
        json.TryUpdateTrimmedStringValue(nameof(AIPersona.AvatarUrl), value => persona.AvatarUrl = value);
        json.TryUpdateTrimmedStringValue(nameof(AIPersona.OwnerId), value => persona.OwnerId = value);
        json.TryUpdateTrimmedStringValue(nameof(AIPersona.Author), value => persona.Author = value);

        if (json.TryGetDateTimeValue(nameof(AIPersona.CreatedUtc), out var createdUtc))
        {
            persona.CreatedUtc = createdUtc;
        }
    }
}
