using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.AI.Realtime;
using CrestApps.Core.Templates.Rendering;
using Microsoft.Extensions.Logging;

namespace CrestApps.Core.AI.Services;

/// <summary>
/// Resolves personas from the persona templates known to <see cref="IAIProfileTemplateManager"/>, which merges
/// saved templates with file-based ones.
/// </summary>
public sealed class DefaultAIPersonaResolver : IAIPersonaResolver
{
    private readonly IAIProfileTemplateManager _templateManager;
    private readonly ITemplateEngine _templateEngine;
    private readonly IAIDeploymentStore _deploymentStore;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultAIPersonaResolver"/> class.
    /// </summary>
    /// <param name="templateManager">The manager that merges saved and file-based templates.</param>
    /// <param name="templateEngine">The engine that renders the persona instructions.</param>
    /// <param name="deploymentStore">The store used to compare the providers of two deployments.</param>
    /// <param name="logger">The logger.</param>
    public DefaultAIPersonaResolver(
        IAIProfileTemplateManager templateManager,
        ITemplateEngine templateEngine,
        IAIDeploymentStore deploymentStore,
        ILogger<DefaultAIPersonaResolver> logger)
    {
        _templateManager = templateManager;
        _templateEngine = templateEngine;
        _deploymentStore = deploymentStore;
        _logger = logger;
    }

    /// <summary>
    /// Lists every persona template, saved and file-based.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    public ValueTask<IEnumerable<AIProfileTemplate>> ListAsync(CancellationToken cancellationToken = default)
    {
        return _templateManager.GetAsync(AITemplateSources.Persona, cancellationToken);
    }

    /// <summary>
    /// Finds a persona template by its technical name.
    /// </summary>
    /// <param name="personaId">The technical name of the persona template.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async ValueTask<AIProfileTemplate> FindAsync(string personaId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(personaId))
        {
            return null;
        }

        var personas = await ListAsync(cancellationToken);

        return personas.FirstOrDefault(template => string.Equals(template.Name, personaId.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Resolves the persona linked to a resource through its <see cref="AIPersonaMetadata"/>.
    /// </summary>
    /// <param name="resource">The entity that may link to a persona.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task<AIPersona> ResolveAsync(ExtensibleEntity resource, CancellationToken cancellationToken = default)
    {
        if (resource is null ||
            !resource.TryGet<AIPersonaMetadata>(out var metadata) ||
            string.IsNullOrWhiteSpace(metadata.PersonaId))
        {
            return Task.FromResult<AIPersona>(null);
        }

        return ResolveAsync(metadata.PersonaId, metadata.Parameters, cancellationToken);
    }

    /// <summary>
    /// Resolves a persona by its technical name and renders its instructions.
    /// </summary>
    /// <param name="personaId">The technical name of the persona template.</param>
    /// <param name="parameters">The Liquid arguments used to render the instructions.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<AIPersona> ResolveAsync(string personaId, IDictionary<string, object> parameters = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(personaId))
        {
            return null;
        }

        var template = await FindAsync(personaId, cancellationToken);

        if (template is null)
        {
            // A persona deleted after profiles linked to it must not break their chats: they carry on as if no
            // persona was chosen, and the log says why the character went away.
            _logger.LogWarning("The AI persona '{PersonaId}' was not found; continuing without a persona.", personaId);

            return null;
        }

        var metadata = template.GetOrCreate<PersonaTemplateMetadata>();
        string instructions = null;

        if (!string.IsNullOrWhiteSpace(metadata.Instructions))
        {
            try
            {
                instructions = await _templateEngine.RenderAsync(metadata.Instructions, parameters, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A Liquid mistake in the persona should cost the persona, not the conversation.
                _logger.LogWarning(ex, "The instructions of the AI persona '{PersonaId}' could not be rendered; continuing without them.", template.Name);
            }
        }

        return new AIPersona
        {
            Id = template.Name,
            DisplayText = string.IsNullOrWhiteSpace(template.DisplayText) ? template.Name : template.DisplayText,
            Instructions = string.IsNullOrWhiteSpace(instructions) ? null : instructions.Trim(),
            AssistantName = NullIfWhiteSpace(metadata.AssistantName),
            Language = NullIfWhiteSpace(metadata.Language),
            VoiceName = NullIfWhiteSpace(metadata.VoiceName),
            VoiceDeploymentName = NullIfWhiteSpace(metadata.VoiceDeploymentName),
            AvatarUrl = NullIfWhiteSpace(metadata.AvatarUrl),
        };
    }

    /// <summary>
    /// Chooses the voice for a session.
    /// </summary>
    /// <param name="persona">The resolved persona, or <see langword="null"/>.</param>
    /// <param name="explicitVoice">A voice chosen explicitly, which always wins.</param>
    /// <param name="speakingDeploymentName">The name of the deployment that will speak.</param>
    /// <param name="defaultVoice">The fallback voice.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<string> SelectVoiceAsync(
        AIPersona persona,
        string explicitVoice,
        string speakingDeploymentName,
        string defaultVoice,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(explicitVoice))
        {
            return explicitVoice.Trim();
        }

        if (!string.IsNullOrWhiteSpace(persona?.VoiceName) &&
            await IsSameProviderAsync(persona.VoiceDeploymentName, speakingDeploymentName))
        {
            return persona.VoiceName;
        }

        return string.IsNullOrWhiteSpace(defaultVoice)
            ? null
            : defaultVoice.Trim();
    }

    private async Task<bool> IsSameProviderAsync(string voiceDeploymentName, string speakingDeploymentName)
    {
        // Nothing to compare against: trust the voice rather than silently dropping it.
        if (string.IsNullOrWhiteSpace(voiceDeploymentName) ||
            string.IsNullOrWhiteSpace(speakingDeploymentName) ||
            string.Equals(voiceDeploymentName, speakingDeploymentName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var voiceProvider = await GetSpeakingProviderAsync(voiceDeploymentName);
        var speakingProvider = await GetSpeakingProviderAsync(speakingDeploymentName);

        if (voiceProvider is null || speakingProvider is null)
        {
            return true;
        }

        if (string.Equals(voiceProvider, speakingProvider, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // A voice name only means something to the provider it came from: an OpenAI realtime voice sent to Azure
        // Speech fails the session, so the deployment default is used instead.
        _logger.LogWarning(
            "The persona voice was chosen on deployment '{VoiceDeployment}' ({VoiceProvider}) but deployment '{SpeakingDeployment}' ({SpeakingProvider}) will speak; using that deployment's default voice.",
            voiceDeploymentName,
            voiceProvider,
            speakingDeploymentName,
            speakingProvider);

        return false;
    }

    private async Task<string> GetSpeakingProviderAsync(string deploymentName)
    {
        var deployment = await _deploymentStore.FindByNameAsync(deploymentName);

        if (deployment is null)
        {
            return null;
        }

        // A cascaded realtime deployment does not speak itself; the text-to-speech deployment it names does, so
        // that deployment's provider owns the voice.
        if (deployment.TryGet<CascadedRealtimeMetadata>(out var cascade) && cascade.IsComplete())
        {
            var speaking = await _deploymentStore.FindByNameAsync(cascade.TextToSpeechDeploymentName);

            return speaking?.ClientName;
        }

        return deployment.ClientName;
    }

    private static string NullIfWhiteSpace(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }
}
