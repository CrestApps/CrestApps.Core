using System.Security.Cryptography;
using System.Text.Json;
using CrestApps.Core.AI;
using CrestApps.Core.AI.Tooling.Instances.Documentation;
using CrestApps.Core.AI.Tooling;
using CrestApps.Core.AI.Tooling.Instances;
using CrestApps.Core.AI.Tooling.Parameters;
using CrestApps.Core.Mvc.Web.Areas.Tooling.ViewModels;
using CrestApps.Core.Startup.Shared.ViewModels;
using CrestApps.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Options;

namespace CrestApps.Core.Mvc.Web.Areas.Tooling.Controllers;

/// <summary>
/// Manages tool instances. Each instance is a preconfigured, model-invokable tool created from a registered
/// source; the built-in HTTP API request source carries its own endpoint, authentication, headers, and a
/// description used to disambiguate instances.
/// </summary>
[Area("Tooling")]
[Authorize(Policy = "Admin")]
public sealed class AIToolInstanceController : Controller
{
    private static readonly JsonSerializerOptions _indentedJsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly ISourceCatalog<AIToolInstance> _catalog;
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly TimeProvider _timeProvider;
    private readonly AIOptions _aiOptions;
    private readonly IEnumerable<IAIToolParameterContextResolver> _contextResolvers;

    /// <summary>
    /// Initializes a new instance of the <see cref="AIToolInstanceController"/> class.
    /// </summary>
    /// <param name="catalog">The tool instance catalog.</param>
    /// <param name="dataProtectionProvider">The data protection provider used to protect secrets.</param>
    /// <param name="timeProvider">The time provider used for timestamps.</param>
    /// <param name="aiOptions">The AI options used to enumerate registered tool instance sources.</param>
    /// <param name="contextResolvers">The resolvers whose keys are offered for context-filled parameters.</param>
    public AIToolInstanceController(
        ISourceCatalog<AIToolInstance> catalog,
        IDataProtectionProvider dataProtectionProvider,
        TimeProvider timeProvider,
        IOptions<AIOptions> aiOptions,
        IEnumerable<IAIToolParameterContextResolver> contextResolvers)
    {
        _catalog = catalog;
        _dataProtectionProvider = dataProtectionProvider;
        _timeProvider = timeProvider;
        _aiOptions = aiOptions.Value;
        _contextResolvers = contextResolvers;
    }

    /// <summary>
    /// Lists all configured tool instances.
    /// </summary>
    public async Task<IActionResult> Index()
    {
        var items = (await _catalog.GetAllAsync())
            .OrderBy(instance => instance.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return View(items);
    }

    /// <summary>
    /// Renders the create form.
    /// </summary>
    public IActionResult Create()
    {
        var sources = BuildSourceList();

        var model = new AIToolInstanceViewModel
        {
            Source = sources.FirstOrDefault()?.Value ?? HttpApiRequestToolConstants.SourceName,
            Sources = sources,
            DefaultHeaders = "{}",
        };

        PopulateParameterMetadata(model);

        return View(model);
    }

    /// <summary>
    /// Handles the create form submission.
    /// </summary>
    /// <param name="model">The submitted view model.</param>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(AIToolInstanceViewModel model)
    {
        await ValidateAsync(model, false, null);

        if (!ModelState.IsValid)
        {
            model.Sources = BuildSourceList();
            PopulateParameterMetadata(model);

            return View(model);
        }

        var instance = new AIToolInstance
        {
            ItemId = UniqueId.GenerateId(),
            Source = model.Source,
            CreatedUtc = _timeProvider.GetUtcNow().UtcDateTime,
        };

        Apply(model, instance, isNew: true);

        await _catalog.CreateAsync(instance);

        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Renders the edit form.
    /// </summary>
    /// <param name="id">The instance identifier.</param>
    public async Task<IActionResult> Edit(string id)
    {
        var instance = await _catalog.FindByIdAsync(id);

        if (instance == null)
        {
            return NotFound();
        }

        return View(ToViewModel(instance));
    }

    /// <summary>
    /// Handles the edit form submission.
    /// </summary>
    /// <param name="model">The submitted view model.</param>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(AIToolInstanceViewModel model)
    {
        var instance = await _catalog.FindByIdAsync(model.ItemId);

        if (instance == null)
        {
            return NotFound();
        }

        model.Source = instance.Source;

        await ValidateAsync(model, true, instance);

        if (!ModelState.IsValid)
        {
            model.Sources = BuildSourceList();
            PopulateParameterMetadata(model);

            return View(model);
        }

        Apply(model, instance, isNew: false);

        await _catalog.UpdateAsync(instance);

        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Deletes a tool instance.
    /// </summary>
    /// <param name="id">The instance identifier.</param>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id)
    {
        var instance = await _catalog.FindByIdAsync(id);

        if (instance == null)
        {
            return NotFound();
        }

        await _catalog.DeleteAsync(instance);

        return RedirectToAction(nameof(Index));
    }

    private void ValidateParameters(AIToolInstanceViewModel model, AIToolInstance existingInstance)
    {
        if (model.Parameters is not { Count: > 0 })
        {
            return;
        }

        AIToolInstanceParameterCapabilities capabilities = null;

        if (!string.IsNullOrEmpty(model.Source) &&
            _aiOptions.ToolInstanceSources.TryGetValue(model.Source, out var entry))
        {
            capabilities = entry.Parameters;
        }

        // The stored parameters are supplied so a secret the user left blank — meaning "keep what is
        // stored" — is not mistaken for a missing value.
        var parameters = AIToolInstanceParameterViewModel.ToParameters(
            model.Parameters,
            existingInstance is null ? null : AIToolParameterBinder.GetParameters(existingInstance));

        foreach (var (index, error) in AIToolParameterValidator.Validate(parameters, capabilities))
        {
            // Anchoring the error to the row lets the editor render it next to the offending parameter.
            var key = index >= 0
                ? $"{nameof(model.Parameters)}[{index}].{nameof(AIToolInstanceParameterViewModel.Name)}"
                : nameof(model.Parameters);

            ModelState.AddModelError(key, error);
        }
    }

    private void ApplyParameters(AIToolInstanceViewModel model, AIToolInstance instance)
    {
        var parameterProtector = _dataProtectionProvider.CreateProtector(HttpApiRequestToolConstants.DataProtectionPurpose);

        var parameters = AIToolInstanceParameterViewModel.ToParameters(
            model.Parameters,
            AIToolParameterBinder.GetParameters(instance),
            parameterProtector.Protect);

        instance.Put(new AIToolInstanceParametersMetadata { Parameters = parameters });
    }

    private void PopulateParameterMetadata(AIToolInstanceViewModel model)
    {
        model.Parameters ??= [];

        // Every capable source is sent to the view, not just the selected one: the create form lets the
        // source be switched client-side, so the placement options for whichever source the user lands on
        // have to already be on the page.
        model.ParameterCapabilities = _aiOptions.ToolInstanceSources
            .Where(source => source.Value.Parameters is { Supported: true })
            .ToDictionary(source => source.Key, source => source.Value.Parameters, StringComparer.OrdinalIgnoreCase);

        model.ParameterCapableSources = model.ParameterCapabilities.Keys.ToList();

        model.ContextKeys = _contextResolvers
            .SelectMany(resolver => resolver.SupportedKeys)
            .ToList();
    }

    private List<SelectListItem> BuildSourceList()
    {
        return _aiOptions.ToolInstanceSources
            .OrderBy(entry => entry.Value.DisplayName?.Value ?? entry.Key, StringComparer.OrdinalIgnoreCase)
            .Select(entry => new SelectListItem
            {
                Value = entry.Key,
                Text = entry.Value.DisplayName?.Value ?? entry.Key,
            })
            .ToList();
    }

    private async Task ValidateAsync(AIToolInstanceViewModel model, bool isEditing, AIToolInstance existingInstance)
    {
        if (string.IsNullOrWhiteSpace(model.Source))
        {
            ModelState.AddModelError(nameof(model.Source), "A source is required.");
        }
        else if (!_aiOptions.ToolInstanceSources.ContainsKey(model.Source))
        {
            ModelState.AddModelError(nameof(model.Source), "The selected source is not registered.");
        }

        if (string.IsNullOrWhiteSpace(model.Name))
        {
            ModelState.AddModelError(nameof(model.Name), "A unique name is required.");
        }
        else
        {
            await ValidateUniqueNameAsync(model.Name, model.ItemId);
        }

        if (string.IsNullOrWhiteSpace(model.Description))
        {
            ModelState.AddModelError(nameof(model.Description), "A description is required so the AI model can tell instances apart.");
        }

        ValidateParameters(model, existingInstance);

        if (string.Equals(model.Source, DocumentationToolConstants.SitemapSourceName, StringComparison.OrdinalIgnoreCase))
        {
            ValidateSitemap(model);

            return;
        }

        if (string.Equals(model.Source, DocumentationToolConstants.SearchIndexSourceName, StringComparison.OrdinalIgnoreCase))
        {
            ValidateSearchIndex(model);

            return;
        }

        if (string.Equals(model.Source, DocumentationToolConstants.AlgoliaSourceName, StringComparison.OrdinalIgnoreCase))
        {
            ValidateAlgolia(model, isEditing, existingInstance);

            return;
        }

        if (!string.Equals(model.Source, HttpApiRequestToolConstants.SourceName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(model.BaseUrl))
        {
            ModelState.AddModelError(nameof(model.BaseUrl), "Base URL is required.");
        }
        else if (!Uri.TryCreate(model.BaseUrl, UriKind.Absolute, out _))
        {
            ModelState.AddModelError(nameof(model.BaseUrl), "Base URL must be a valid absolute URL.");
        }

        if (string.IsNullOrWhiteSpace(model.HttpMethod))
        {
            ModelState.AddModelError(nameof(model.HttpMethod), "HTTP method is required.");
        }

        switch (model.AuthenticationType)
        {
            case HttpApiRequestAuthenticationType.ApiKey:
                if (string.IsNullOrWhiteSpace(model.ApiKeyHeaderName))
                {
                    ModelState.AddModelError(nameof(model.ApiKeyHeaderName), "API key header name is required.");
                }

                if ((!isEditing || !model.HasApiKey) && string.IsNullOrWhiteSpace(model.ApiKey))
                {
                    ModelState.AddModelError(nameof(model.ApiKey), "API key is required.");
                }

                break;
            case HttpApiRequestAuthenticationType.Bearer:
                if ((!isEditing || !model.HasBearerToken) && string.IsNullOrWhiteSpace(model.BearerToken))
                {
                    ModelState.AddModelError(nameof(model.BearerToken), "Bearer token is required.");
                }

                break;
            case HttpApiRequestAuthenticationType.Basic:
                if (string.IsNullOrWhiteSpace(model.Username))
                {
                    ModelState.AddModelError(nameof(model.Username), "Username is required.");
                }

                if ((!isEditing || !model.HasPassword) && string.IsNullOrWhiteSpace(model.Password))
                {
                    ModelState.AddModelError(nameof(model.Password), "Password is required.");
                }

                break;
            case HttpApiRequestAuthenticationType.OAuth2:
                if (string.IsNullOrWhiteSpace(model.TokenEndpoint))
                {
                    ModelState.AddModelError(nameof(model.TokenEndpoint), "Token endpoint is required.");
                }
                else if (!Uri.TryCreate(model.TokenEndpoint, UriKind.Absolute, out _))
                {
                    ModelState.AddModelError(nameof(model.TokenEndpoint), "Token endpoint must be a valid absolute URL.");
                }

                if (string.IsNullOrWhiteSpace(model.ClientId))
                {
                    ModelState.AddModelError(nameof(model.ClientId), "Client ID is required.");
                }

                if ((!isEditing || !model.HasClientSecret) && string.IsNullOrWhiteSpace(model.ClientSecret))
                {
                    ModelState.AddModelError(nameof(model.ClientSecret), "Client secret is required.");
                }

                if (!string.IsNullOrWhiteSpace(model.Username) &&
                    (!isEditing || !model.HasPassword) &&
                    string.IsNullOrWhiteSpace(model.Password))
                {
                    ModelState.AddModelError(nameof(model.Password), "Password is required when a username is provided.");
                }

                break;
        }

        if (!string.IsNullOrWhiteSpace(model.DefaultHeaders))
        {
            try
            {
                _ = JsonSerializer.Deserialize<Dictionary<string, string>>(model.DefaultHeaders);
            }
            catch (JsonException)
            {
                ModelState.AddModelError(nameof(model.DefaultHeaders), "Default headers must be a valid JSON object.");
            }
        }

        if (model.TimeoutSeconds is < 1 or > 600)
        {
            ModelState.AddModelError(nameof(model.TimeoutSeconds), "Timeout must be between 1 and 600 seconds.");
        }
    }

    private async Task ValidateUniqueNameAsync(string name, string currentItemId)
    {
        var existing = await _catalog.GetAllAsync();

        var duplicate = existing.Any(entry =>
            !string.Equals(entry.ItemId, currentItemId, StringComparison.Ordinal) &&
            string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase));

        if (duplicate)
        {
            ModelState.AddModelError(nameof(AIToolInstanceViewModel.Name), "A tool instance with this name already exists. The name must be unique.");
        }
    }

    private void ValidateSitemap(AIToolInstanceViewModel model)
    {
        ValidateAbsoluteUrl(model.SitemapBaseUrl, nameof(model.SitemapBaseUrl), "Base URL", required: true);
        ValidateAbsoluteUrl(model.SitemapUrl, nameof(model.SitemapUrl), "Sitemap URL", required: false);
    }

    private void ValidateSearchIndex(AIToolInstanceViewModel model)
    {
        ValidateAbsoluteUrl(model.SearchIndexBaseUrl, nameof(model.SearchIndexBaseUrl), "Base URL", required: true);
        ValidateAbsoluteUrl(model.SearchIndexUrl, nameof(model.SearchIndexUrl), "Search index URL", required: false);
    }

    private void ValidateAlgolia(AIToolInstanceViewModel model, bool isEditing, AIToolInstance existingInstance)
    {
        model.HasAlgoliaApiKey = isEditing &&
            existingInstance?.TryGet<AlgoliaDocumentationToolSettings>(out var settings) == true &&
            !string.IsNullOrEmpty(settings.ApiKey);

        if (string.IsNullOrWhiteSpace(model.AlgoliaApplicationId))
        {
            ModelState.AddModelError(nameof(model.AlgoliaApplicationId), "Application ID is required.");
        }

        if (!model.HasAlgoliaApiKey && string.IsNullOrWhiteSpace(model.AlgoliaApiKey))
        {
            ModelState.AddModelError(nameof(model.AlgoliaApiKey), "Search-only API key is required.");
        }

        if (string.IsNullOrWhiteSpace(model.AlgoliaIndexName))
        {
            ModelState.AddModelError(nameof(model.AlgoliaIndexName), "Index name is required.");
        }
    }

    private void ValidateAbsoluteUrl(string value, string key, string label, bool required)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (required)
            {
                ModelState.AddModelError(key, $"{label} is required.");
            }

            return;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out _))
        {
            ModelState.AddModelError(key, $"{label} must be a valid absolute URL.");
        }
    }

    private void Apply(AIToolInstanceViewModel model, AIToolInstance instance, bool isNew)
    {
        if (isNew)
        {
            instance.Name = model.Name.Trim();
        }

        instance.Description = model.Description.Trim();
        instance.ModifiedUtc = _timeProvider.GetUtcNow().UtcDateTime;

        ApplyParameters(model, instance);

        if (string.Equals(model.Source, DocumentationToolConstants.SitemapSourceName, StringComparison.OrdinalIgnoreCase))
        {
            instance.Put(new SitemapDocumentationToolSettings
            {
                BaseUrl = model.SitemapBaseUrl?.Trim(),
                SitemapUrl = string.IsNullOrWhiteSpace(model.SitemapUrl) ? null : model.SitemapUrl.Trim(),
                MaxResults = model.SitemapMaxResults,
                MaxPages = model.SitemapMaxPages,
            });

            return;
        }

        if (string.Equals(model.Source, DocumentationToolConstants.SearchIndexSourceName, StringComparison.OrdinalIgnoreCase))
        {
            instance.Put(new SearchIndexDocumentationToolSettings
            {
                BaseUrl = model.SearchIndexBaseUrl?.Trim(),
                IndexUrl = string.IsNullOrWhiteSpace(model.SearchIndexUrl) ? null : model.SearchIndexUrl.Trim(),
                MaxResults = model.SearchIndexMaxResults,
            });

            return;
        }

        if (string.Equals(model.Source, DocumentationToolConstants.AlgoliaSourceName, StringComparison.OrdinalIgnoreCase))
        {
            var algoliaProtector = _dataProtectionProvider.CreateProtector(DocumentationToolConstants.AlgoliaDataProtectionPurpose);
            var existingAlgoliaSettings = instance.GetOrCreate<AlgoliaDocumentationToolSettings>();

            instance.Put(new AlgoliaDocumentationToolSettings
            {
                ApplicationId = model.AlgoliaApplicationId?.Trim(),
                ApiKey = ProtectOrReuseProtected(model.AlgoliaApiKey?.Trim(), existingAlgoliaSettings.ApiKey, algoliaProtector),
                IndexName = model.AlgoliaIndexName?.Trim(),
                MaxResults = model.AlgoliaMaxResults,
            });

            return;
        }

        var protector = _dataProtectionProvider.CreateProtector(HttpApiRequestToolConstants.DataProtectionPurpose);
        var existing = instance.GetOrCreate<HttpApiRequestToolSettings>();

        var settings = new HttpApiRequestToolSettings
        {
            BaseUrl = model.BaseUrl?.Trim(),
            PathTemplate = string.IsNullOrWhiteSpace(model.PathTemplate) ? null : model.PathTemplate.Trim(),
            HttpMethod = string.IsNullOrWhiteSpace(model.HttpMethod) ? "GET" : model.HttpMethod.Trim().ToUpperInvariant(),
            AuthenticationType = model.AuthenticationType,
            AllowModelProvidedPath = model.AllowModelProvidedPath,
            AllowModelProvidedQuery = model.AllowModelProvidedQuery,
            AllowModelProvidedBody = model.AllowModelProvidedBody,
            TimeoutSeconds = model.TimeoutSeconds,
            DefaultHeaders = string.IsNullOrWhiteSpace(model.DefaultHeaders)
                ? []
                : JsonSerializer.Deserialize<Dictionary<string, string>>(model.DefaultHeaders) ?? [],
        };

        switch (model.AuthenticationType)
        {
            case HttpApiRequestAuthenticationType.ApiKey:
                settings.ApiKeyHeaderName = model.ApiKeyHeaderName?.Trim();
                settings.ApiKey = ProtectOrReuse(model.ApiKey, existing.ApiKey, protector);
                break;
            case HttpApiRequestAuthenticationType.Bearer:
                settings.BearerToken = ProtectOrReuse(model.BearerToken, existing.BearerToken, protector);
                break;
            case HttpApiRequestAuthenticationType.Basic:
                settings.Username = model.Username?.Trim();
                settings.Password = ProtectOrReuse(model.Password, existing.Password, protector);
                break;
            case HttpApiRequestAuthenticationType.OAuth2:
                settings.TokenEndpoint = model.TokenEndpoint?.Trim();
                settings.ClientId = model.ClientId?.Trim();
                settings.ClientSecret = ProtectOrReuse(model.ClientSecret, existing.ClientSecret, protector);
                settings.Username = model.Username?.Trim();
                settings.Password = ProtectOrReuse(model.Password, existing.Password, protector);
                settings.Scope = model.Scope?.Trim();
                break;
        }

        instance.Put(settings);
    }

    private static string ProtectOrReuse(string newValue, string existingValue, IDataProtector protector)
    {
        return string.IsNullOrWhiteSpace(newValue) ? existingValue : protector.Protect(newValue);
    }

    private static string ProtectOrReuseProtected(string newValue, string existingValue, IDataProtector protector)
    {
        if (!string.IsNullOrWhiteSpace(newValue))
        {
            return protector.Protect(newValue);
        }

        if (string.IsNullOrWhiteSpace(existingValue))
        {
            return existingValue;
        }

        try
        {
            protector.Unprotect(existingValue);

            return existingValue;
        }
        catch (CryptographicException)
        {
            return protector.Protect(existingValue);
        }
    }

    private AIToolInstanceViewModel ToViewModel(AIToolInstance instance)
    {
        var model = new AIToolInstanceViewModel
        {
            ItemId = instance.ItemId,
            Source = instance.Source,
            Name = instance.Name,
            Description = instance.Description,
            DefaultHeaders = "{}",
        };

        if (instance.TryGet<HttpApiRequestToolSettings>(out var settings))
        {
            model.BaseUrl = settings.BaseUrl;
            model.PathTemplate = settings.PathTemplate;
            model.HttpMethod = string.IsNullOrWhiteSpace(settings.HttpMethod) ? "GET" : settings.HttpMethod;
            model.AuthenticationType = settings.AuthenticationType;
            model.ApiKeyHeaderName = string.IsNullOrWhiteSpace(settings.ApiKeyHeaderName) ? "X-Api-Key" : settings.ApiKeyHeaderName;
            model.HasApiKey = !string.IsNullOrEmpty(settings.ApiKey);
            model.HasBearerToken = !string.IsNullOrEmpty(settings.BearerToken);
            model.Username = settings.Username;
            model.HasPassword = !string.IsNullOrEmpty(settings.Password);
            model.TokenEndpoint = settings.TokenEndpoint;
            model.ClientId = settings.ClientId;
            model.HasClientSecret = !string.IsNullOrEmpty(settings.ClientSecret);
            model.Scope = settings.Scope;
            model.AllowModelProvidedPath = settings.AllowModelProvidedPath;
            model.AllowModelProvidedQuery = settings.AllowModelProvidedQuery;
            model.AllowModelProvidedBody = settings.AllowModelProvidedBody;
            model.TimeoutSeconds = settings.TimeoutSeconds;
            model.DefaultHeaders = settings.DefaultHeaders is { Count: > 0 }
                ? JsonSerializer.Serialize(settings.DefaultHeaders, _indentedJsonOptions)
                : "{}";
        }

        model.Parameters = AIToolInstanceParameterViewModel.FromParameters(AIToolParameterBinder.GetParameters(instance));

        PopulateParameterMetadata(model);

        if (instance.TryGet<SitemapDocumentationToolSettings>(out var sitemapSettings))
        {
            model.SitemapBaseUrl = sitemapSettings.BaseUrl;
            model.SitemapUrl = sitemapSettings.SitemapUrl;
            model.SitemapMaxResults = sitemapSettings.MaxResults;
            model.SitemapMaxPages = sitemapSettings.MaxPages;
        }

        if (instance.TryGet<SearchIndexDocumentationToolSettings>(out var searchIndexSettings))
        {
            model.SearchIndexBaseUrl = searchIndexSettings.BaseUrl;
            model.SearchIndexUrl = searchIndexSettings.IndexUrl;
            model.SearchIndexMaxResults = searchIndexSettings.MaxResults;
        }

        if (instance.TryGet<AlgoliaDocumentationToolSettings>(out var algoliaSettings))
        {
            model.AlgoliaApplicationId = algoliaSettings.ApplicationId;
            model.HasAlgoliaApiKey = !string.IsNullOrEmpty(algoliaSettings.ApiKey);
            model.AlgoliaIndexName = algoliaSettings.IndexName;
            model.AlgoliaMaxResults = algoliaSettings.MaxResults;
        }

        return model;
    }
}
