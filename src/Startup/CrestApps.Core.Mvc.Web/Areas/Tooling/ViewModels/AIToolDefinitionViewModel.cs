using System.ComponentModel.DataAnnotations;
using CrestApps.Core.AI.Tooling.Sources;

namespace CrestApps.Core.Mvc.Web.Areas.Tooling.ViewModels;

/// <summary>
/// The view model used to create and edit an <see cref="CrestApps.Core.AI.Tooling.AIToolDefinition"/>
/// configured from the built-in HTTP API request definition.
/// </summary>
public sealed class AIToolDefinitionViewModel
{
    /// <summary>
    /// Gets or sets the identifier of the instance being edited.
    /// </summary>
    public string ItemId { get; set; }

    /// <summary>
    /// Gets or sets the tool definition name (the catalog source).
    /// </summary>
    public string Source { get; set; }

    /// <summary>
    /// Gets or sets the human-readable name shown in management surfaces.
    /// </summary>
    [Required]
    public string DisplayText { get; set; }

    /// <summary>
    /// Gets or sets the description shown to the AI model so it can distinguish this instance from
    /// other instances of the same definition.
    /// </summary>
    [Required]
    public string Description { get; set; }

    /// <summary>
    /// Gets or sets the base URL the request targets.
    /// </summary>
    public string BaseUrl { get; set; }

    /// <summary>
    /// Gets or sets the HTTP method to use.
    /// </summary>
    public string HttpMethod { get; set; } = "GET";

    /// <summary>
    /// Gets or sets the authentication strategy applied to the request.
    /// </summary>
    public HttpApiRequestAuthenticationType AuthenticationType { get; set; }

    /// <summary>
    /// Gets or sets the header name used for API key authentication.
    /// </summary>
    public string ApiKeyHeaderName { get; set; } = "X-Api-Key";

    /// <summary>
    /// Gets or sets the API key value used for API key authentication.
    /// </summary>
    public string ApiKey { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a protected API key is already stored.
    /// </summary>
    public bool HasApiKey { get; set; }

    /// <summary>
    /// Gets or sets the bearer token used for bearer authentication.
    /// </summary>
    public string BearerToken { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a protected bearer token is already stored.
    /// </summary>
    public bool HasBearerToken { get; set; }

    /// <summary>
    /// Gets or sets the username used for basic authentication.
    /// </summary>
    public string BasicUsername { get; set; }

    /// <summary>
    /// Gets or sets the password used for basic authentication.
    /// </summary>
    public string BasicPassword { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a protected basic password is already stored.
    /// </summary>
    public bool HasBasicPassword { get; set; }

    /// <summary>
    /// Gets or sets the static headers, as a JSON object, always added to the request.
    /// </summary>
    public string DefaultHeaders { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the AI model may supply a relative path.
    /// </summary>
    public bool AllowModelProvidedPath { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the AI model may supply query string parameters.
    /// </summary>
    public bool AllowModelProvidedQuery { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the AI model may supply a request body.
    /// </summary>
    public bool AllowModelProvidedBody { get; set; } = true;

    /// <summary>
    /// Gets or sets an optional per-request timeout in seconds.
    /// </summary>
    public int? TimeoutSeconds { get; set; }
}
