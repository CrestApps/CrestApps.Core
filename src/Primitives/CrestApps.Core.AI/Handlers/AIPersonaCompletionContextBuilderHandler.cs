using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;

namespace CrestApps.Core.AI.Handlers;

/// <summary>
/// Places the instructions of the persona a profile or chat interaction links to at the top of its system message,
/// and exposes the resolved persona under <see cref="AICompletionContextKeys.Persona"/>. It runs once the resource's
/// own handlers have built the system message, so the persona leads it and the business rules after it win a
/// conflict. Resolved on every request, so a persona edit applies from the next message.
/// </summary>
internal sealed class AIPersonaCompletionContextBuilderHandler : IAICompletionContextBuilderHandler
{
    private readonly IAIPersonaResolver _personaResolver;

    public AIPersonaCompletionContextBuilderHandler(IAIPersonaResolver personaResolver)
    {
        _personaResolver = personaResolver;
    }

    public Task BuildingAsync(AICompletionContextBuildingContext context)
    {
        return Task.CompletedTask;
    }

    public async Task BuiltAsync(AICompletionContextBuiltContext context)
    {
        if (context.Resource is not ExtensibleEntity resource)
        {
            return;
        }

        var persona = await _personaResolver.ResolveAsync(resource);

        if (persona is null)
        {
            return;
        }

        context.Context.SystemMessage = persona.PrependTo(context.Context.SystemMessage);
        context.Context.AdditionalProperties[AICompletionContextKeys.Persona] = persona;
    }
}
