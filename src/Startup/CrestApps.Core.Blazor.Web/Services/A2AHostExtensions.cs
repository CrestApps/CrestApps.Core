using System.Text.Json;
using System.Text.Json.Serialization;
using A2A;
using A2A.AspNetCore;
using CrestApps.Core.AI.A2A.Models;
using CrestApps.Core.AI.Completions;
using CrestApps.Core.AI.Deployments;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Profiles;
using CrestApps.Core.Builders;
using CrestApps.Core.Startup.Shared.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Blazor.Web.Services;

internal static class A2AHostExtensions
{
    private const string A2AHostPolicyName = "BlazorA2AHostPolicy";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly Action<ILogger, string, Exception> _failedToExecuteAgent =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(1001, nameof(FailedToExecuteAgent)),
            "Failed to execute agent '{AgentName}'.");

    public static IServiceCollection AddA2AHost(this IServiceCollection services)
    {
        services.AddScoped<IAuthorizationHandler, A2AHostAuthorizationHandler>();
        services.AddAuthentication()
            .AddScheme<A2AApiKeyAuthenticationOptions, A2AApiKeyAuthenticationHandler>(
                A2AApiKeyAuthenticationDefaults.AuthenticationScheme, _ => { });

        services.AddAuthorizationBuilder()
            .AddPolicy(A2AHostPolicyName, policy =>
            {
                policy.AddAuthenticationSchemes(
                    A2AApiKeyAuthenticationDefaults.AuthenticationScheme,
                    CookieAuthenticationDefaults.AuthenticationScheme);
                policy.AddRequirements(new A2AHostAuthorizationRequirement());
            });

        services.AddSingleton<IAgentHandler, BlazorA2AAgentHandler>();
        services.AddSingleton<ChannelEventNotifier>();
        services.AddSingleton<ITaskStore, InMemoryTaskStore>();
        services.AddSingleton<IA2ARequestHandler>(services =>
            new A2AServer(
                services.GetRequiredService<IAgentHandler>(),
                services.GetRequiredService<ITaskStore>(),
                services.GetRequiredService<ChannelEventNotifier>(),
                services.GetRequiredService<ILogger<A2AServer>>()));

        return services;
    }

    public static CrestAppsAISuiteBuilder AddA2AHost(this CrestAppsAISuiteBuilder builder)
    {
        builder.Services.AddA2AHost();

        return builder;
    }

    public static IEndpointRouteBuilder MapA2AHost(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/.well-known/agent-card.json", HandleWellKnownEndpointAsync);

        var requestHandler = endpoints.ServiceProvider.GetRequiredService<IA2ARequestHandler>();
        endpoints.MapA2A(requestHandler, "a2a")
            .RequireAuthorization(A2AHostPolicyName);

        return endpoints;
    }

    private static async Task HandleWellKnownEndpointAsync(HttpContext context)
    {
        var options = context.RequestServices.GetRequiredService<IOptionsMonitor<A2AHostOptions>>().CurrentValue;
        var profileManager = context.RequestServices.GetRequiredService<IAIProfileManager>();
        var profiles = await profileManager.GetAsync(AIProfileType.Agent);
        var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}";

        context.Response.ContentType = "application/json";

        if (options.ExposeAgentsAsSkill)
        {
            var card = BuildSkillModeCard($"{baseUrl}/a2a", profiles);
            ApplySecuritySchemes(card, options, baseUrl);
            await context.Response.WriteAsJsonAsync(card, _jsonOptions, context.RequestAborted);
        }
        else
        {
            var cards = new List<AgentCard>();

            if (profiles is not null)
            {
                foreach (var profile in profiles)
                {
                    var agentUrl = $"{baseUrl}/a2a?agent={Uri.EscapeDataString(profile.Name)}";
                    var card = BuildAgentCard(profile, agentUrl);
                    ApplySecuritySchemes(card, options, baseUrl);
                    cards.Add(card);
                }
            }

            await context.Response.WriteAsJsonAsync(cards, _jsonOptions, context.RequestAborted);
        }
    }

    private static async Task ProcessAgentRequestAsync(
        TaskUpdater updater,
        IHttpContextAccessor httpContextAccessor,
        RequestContext requestContext,
        AgentEventQueue eventQueue,
        CancellationToken cancellationToken)
    {
        var services = httpContextAccessor.HttpContext?.RequestServices;

        if (services is null)
        {
            await updater.FailAsync(
                CreateAgentMessage(requestContext.ContextId, "Request services are not available."),
                cancellationToken: cancellationToken);

            return;
        }

        var logger = services.GetRequiredService<ILogger<A2AServer>>();

        var prompt = requestContext.UserText;

        if (string.IsNullOrWhiteSpace(prompt))
        {
            await updater.FailAsync(
                CreateAgentMessage(requestContext.ContextId, "No text message was provided."),
                cancellationToken: cancellationToken);

            return;
        }

        var targetProfile = await ResolveTargetProfileAsync(
            services, httpContextAccessor, requestContext.Message);

        if (targetProfile is null)
        {
            await updater.FailAsync(
                CreateAgentMessage(requestContext.ContextId, "No agents are available to process this request."),
                cancellationToken: cancellationToken);

            return;
        }

        try
        {
            if (!requestContext.IsContinuation)
            {
                await updater.SubmitAsync(cancellationToken: cancellationToken);
            }

            await updater.StartWorkAsync(cancellationToken: cancellationToken);

            var completionService = services.GetRequiredService<IAICompletionService>();
            var contextBuilder = services.GetRequiredService<IAICompletionContextBuilder>();
            var deploymentManager = services.GetRequiredService<IAIDeploymentManager>();

            var context = await contextBuilder.BuildAsync(targetProfile, cancellationToken: cancellationToken);
            context.DisableTools = true;

            var deployment = await deploymentManager.ResolveOrDefaultAsync(AIDeploymentPurpose.Chat, deploymentName: context.ChatDeploymentName, cancellationToken: cancellationToken)
                ?? throw new InvalidOperationException($"Unable to resolve a chat deployment for profile '{targetProfile.Name}'.");

            var messages = new List<ChatMessage>
            {
                new(ChatRole.User, prompt),
            };

            var responseText = new System.Text.StringBuilder();
            var artifactId = Guid.NewGuid().ToString("N");
            var appendArtifact = false;

            await foreach (var update in completionService.CompleteStreamingAsync(
                deployment, messages, context, cancellationToken))
            {
                var chunk = update.Text;

                if (!string.IsNullOrEmpty(chunk))
                {
                    responseText.Append(chunk);

                    await updater.AddArtifactAsync(
                        [Part.FromText(chunk)],
                        artifactId: artifactId,
                        append: appendArtifact,
                        lastChunk: false,
                        cancellationToken: cancellationToken);

                    appendArtifact = true;
                }
            }

            var finalText = responseText.Length > 0
                ? responseText.ToString()
                : "The agent did not produce a response.";

            if (appendArtifact)
            {
                await eventQueue.EnqueueArtifactUpdateAsync(
                    new TaskArtifactUpdateEvent
                    {
                        TaskId = updater.TaskId,
                        ContextId = updater.ContextId,
                        Artifact = new Artifact
                        {
                            ArtifactId = artifactId,
                            Parts = [],
                        },
                        Append = true,
                        LastChunk = true,
                    },
                    cancellationToken);
            }

            await updater.CompleteAsync(
                CreateAgentMessage(requestContext.ContextId, finalText),
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await updater.CancelAsync(cancellationToken: CancellationToken.None);
        }
        catch (Exception ex)
        {
            FailedToExecuteAgent(logger, targetProfile.Name, ex);

            await updater.FailAsync(
                CreateAgentMessage(requestContext.ContextId, $"An error occurred while executing agent '{targetProfile.Name}'."),
                cancellationToken: CancellationToken.None);
        }
    }

    private static async Task<AIProfile> ResolveTargetProfileAsync(
        IServiceProvider services,
        IHttpContextAccessor httpContextAccessor,
        Message message)
    {
        var options = services.GetRequiredService<IOptionsMonitor<A2AHostOptions>>().CurrentValue;
        var profileManager = services.GetRequiredService<IAIProfileManager>();
        var profiles = await profileManager.GetAsync(AIProfileType.Agent);

        AIProfile targetProfile = null;

        if (!options.ExposeAgentsAsSkill)
        {
            var agentName = httpContextAccessor.HttpContext?.Request.Query["agent"].FirstOrDefault();

            if (!string.IsNullOrEmpty(agentName))
            {
                targetProfile = profiles?.FirstOrDefault(p =>
                    string.Equals(p.Name, agentName, StringComparison.OrdinalIgnoreCase));
            }
        }

        if (targetProfile is null &&
            message?.Metadata?.TryGetValue("agentName", out var agentNameElement) == true)
        {
            var metaAgentName = agentNameElement.GetString();

            if (!string.IsNullOrEmpty(metaAgentName))
            {
                targetProfile = profiles?.FirstOrDefault(p =>
                    string.Equals(p.Name, metaAgentName, StringComparison.OrdinalIgnoreCase));
            }
        }

        return targetProfile ?? profiles?.FirstOrDefault();
    }

    private static AgentCard BuildSkillModeCard(string agentUrl, IEnumerable<AIProfile> profiles)
    {
        var skills = new List<AgentSkill>();

        if (profiles is not null)
        {
            foreach (var profile in profiles)
            {
                skills.Add(new AgentSkill
                {
                    Id = profile.Name,
                    Name = profile.DisplayText ?? profile.Name,
                    Description = profile.Description,
                    Tags = ["agent"],
                });
            }
        }

        return new AgentCard
        {
            Name = "CrestApps Blazor A2A Host",
            Description = "Exposes AI Agent profiles via the Agent-to-Agent protocol.",
            Version = "1.0",
            SupportedInterfaces =
            [
                new AgentInterface
                {
                    Url = agentUrl,
                    ProtocolBinding = "JSONRPC",
                    ProtocolVersion = "1.0",
                },
            ],
            DefaultInputModes = ["text/plain"],
            DefaultOutputModes = ["text/plain"],
            Capabilities = new AgentCapabilities
            {
                Streaming = true,
            },
            Skills = skills,
        };
    }

    private static AgentCard BuildAgentCard(AIProfile profile, string agentUrl)
    {
        return new AgentCard
        {
            Name = profile.DisplayText ?? profile.Name,
            Description = profile.Description ?? $"AI Agent: {profile.DisplayText ?? profile.Name}",
            Version = "1.0",
            SupportedInterfaces =
            [
                new AgentInterface
                {
                    Url = agentUrl,
                    ProtocolBinding = "JSONRPC",
                    ProtocolVersion = "1.0",
                },
            ],
            DefaultInputModes = ["text/plain"],
            DefaultOutputModes = ["text/plain"],
            Capabilities = new AgentCapabilities
            {
                Streaming = true,
            },
            Skills =
            [
                new AgentSkill
                {
                    Id = profile.Name,
                    Name = profile.DisplayText ?? profile.Name,
                    Description = profile.Description,
                    Tags = ["agent"],
                },
            ],
        };
    }

    private static void ApplySecuritySchemes(AgentCard card, A2AHostOptions options, string baseUrl)
    {
        switch (options.AuthenticationType)
        {
            case A2AHostAuthenticationType.ApiKey:
                card.SecuritySchemes = new Dictionary<string, SecurityScheme>
                {
                    ["apiKey"] = new SecurityScheme
                    {
                        ApiKeySecurityScheme = new ApiKeySecurityScheme
                        {
                            Name = "Authorization",
                            Location = "header",
                            Description = "API key authentication. Send as 'Bearer {key}' or 'ApiKey {key}' in the Authorization header.",
                        },
                    },
                };

                card.SecurityRequirements =
                [
                    new SecurityRequirement
                    {
                        Schemes = new Dictionary<string, StringList>
                        {
                            ["apiKey"] = new(),
                        },
                    },
                ];
                break;

            case A2AHostAuthenticationType.OpenId:
                card.SecuritySchemes = new Dictionary<string, SecurityScheme>
                {
                    ["openId"] = new SecurityScheme
                    {
                        OpenIdConnectSecurityScheme = new OpenIdConnectSecurityScheme
                        {
                            OpenIdConnectUrl = $"{baseUrl}/.well-known/openid-configuration",
                            Description = "OpenID Connect authentication.",
                        },
                    },
                };

                card.SecurityRequirements =
                [
                    new SecurityRequirement
                    {
                        Schemes = new Dictionary<string, StringList>
                        {
                            ["openId"] = new(),
                        },
                    },
                ];
                break;
        }
    }

    private static AIProfile ResolveAgentProfile(IEnumerable<AIProfile> profiles, string agentName)
    {
        if (string.IsNullOrEmpty(agentName) || profiles is null)
        {
            return null;
        }

        return profiles.FirstOrDefault(p =>
            string.Equals(p.Name, agentName, StringComparison.OrdinalIgnoreCase));
    }

    private static Message CreateAgentMessage(string contextId, string text)
    {
        return new Message
        {
            Role = Role.Agent,
            MessageId = Guid.NewGuid().ToString(),
            ContextId = contextId,
            Parts = [Part.FromText(text)],
        };
    }

    private static void FailedToExecuteAgent(ILogger logger, string agentName, Exception exception)
    {
        _failedToExecuteAgent(logger, agentName, exception);
    }

    private sealed class BlazorA2AAgentHandler : IAgentHandler
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        /// <summary>
        /// Initializes a new instance of the <see cref="BlazorA2AAgentHandler"/> class.
        /// </summary>
        /// <param name="httpContextAccessor">The current HTTP context accessor.</param>
        public BlazorA2AAgentHandler(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        /// <summary>
        /// Executes the A2A agent request.
        /// </summary>
        /// <param name="context">The A2A request context.</param>
        /// <param name="eventQueue">The A2A event queue.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        public Task ExecuteAsync(
            RequestContext context,
            AgentEventQueue eventQueue,
            CancellationToken cancellationToken)
        {
            var updater = new TaskUpdater(eventQueue, context.TaskId, context.ContextId);

            return ProcessAgentRequestAsync(
                updater,
                _httpContextAccessor,
                context,
                eventQueue,
                cancellationToken);
        }
    }
}
