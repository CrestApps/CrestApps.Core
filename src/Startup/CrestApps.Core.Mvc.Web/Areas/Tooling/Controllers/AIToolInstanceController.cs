using System.Text.Json;
using CrestApps.Core.AI.Tooling;
using CrestApps.Core.AI.Tooling.Instances;
using CrestApps.Core.Mvc.Web.Areas.Tooling.ViewModels;
using CrestApps.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;

namespace CrestApps.Core.Mvc.Web.Areas.Tooling.Controllers;

/// <summary>
/// Manages HTTP API request tool instances. Each instance is a preconfigured, model-invokable tool that
/// carries its own endpoint, authentication, headers, and a description used to disambiguate instances.
/// </summary>
[Area("Tooling")]
[Authorize(Policy = "Admin")]
public sealed class AIToolInstanceController : Controller
{
    private static readonly JsonSerializerOptions _indentedJsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly ICatalog<AIToolInstance> _catalog;
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="AIToolInstanceController"/> class.
    /// </summary>
    /// <param name="catalog">The tool instance catalog.</param>
    /// <param name="dataProtectionProvider">The data protection provider used to protect secrets.</param>
    /// <param name="timeProvider">The time provider used for timestamps.</param>
    public AIToolInstanceController(
        ICatalog<AIToolInstance> catalog,
        IDataProtectionProvider dataProtectionProvider,
        TimeProvider timeProvider)
    {
        _catalog = catalog;
        _dataProtectionProvider = dataProtectionProvider;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Lists all configured tool instances.
    /// </summary>
    public async Task<IActionResult> Index()
    {
        var items = (await _catalog.GetAllAsync())
            .OrderBy(instance => instance.DisplayText, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return View(items);
    }

    /// <summary>
    /// Renders the create form.
    /// </summary>
    public IActionResult Create()
    {
        return View(new AIToolInstanceViewModel
        {
            Source = HttpApiRequestToolConstants.DefinitionName,
            DefaultHeaders = "{}",
        });
    }

    /// <summary>
    /// Handles the create form submission.
    /// </summary>
    /// <param name="model">The submitted view model.</param>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(AIToolInstanceViewModel model)
    {
        Validate(model, false);

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var instance = new AIToolInstance
        {
            ItemId = UniqueId.GenerateId(),
            Source = HttpApiRequestToolConstants.DefinitionName,
            CreatedUtc = _timeProvider.GetUtcNow().UtcDateTime,
        };

        Apply(model, instance);

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

        Validate(model, true);

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        Apply(model, instance);

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

    private void Validate(AIToolInstanceViewModel model, bool isEditing)
    {
        if (string.IsNullOrWhiteSpace(model.DisplayText))
        {
            ModelState.AddModelError(nameof(model.DisplayText), "Display text is required.");
        }

        if (string.IsNullOrWhiteSpace(model.Description))
        {
            ModelState.AddModelError(nameof(model.Description), "A description is required so the AI model can tell instances apart.");
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
                if (string.IsNullOrWhiteSpace(model.BasicUsername))
                {
                    ModelState.AddModelError(nameof(model.BasicUsername), "Username is required.");
                }

                if ((!isEditing || !model.HasBasicPassword) && string.IsNullOrWhiteSpace(model.BasicPassword))
                {
                    ModelState.AddModelError(nameof(model.BasicPassword), "Password is required.");
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

    private void Apply(AIToolInstanceViewModel model, AIToolInstance instance)
    {
        instance.DisplayText = model.DisplayText.Trim();
        instance.Description = model.Description.Trim();

        var protector = _dataProtectionProvider.CreateProtector(HttpApiRequestToolConstants.DataProtectionPurpose);
        var existing = instance.TryGet<HttpApiRequestToolSettings>(out var stored) ? stored : new HttpApiRequestToolSettings();

        var settings = new HttpApiRequestToolSettings
        {
            BaseUrl = model.BaseUrl?.Trim(),
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
                settings.BasicUsername = model.BasicUsername?.Trim();
                settings.BasicPassword = ProtectOrReuse(model.BasicPassword, existing.BasicPassword, protector);
                break;
        }

        instance.Put(settings);
    }

    private static string ProtectOrReuse(string newValue, string existingValue, IDataProtector protector)
    {
        return string.IsNullOrWhiteSpace(newValue) ? existingValue : protector.Protect(newValue);
    }

    private static AIToolInstanceViewModel ToViewModel(AIToolInstance instance)
    {
        var model = new AIToolInstanceViewModel
        {
            ItemId = instance.ItemId,
            Source = instance.Source,
            DisplayText = instance.DisplayText,
            Description = instance.Description,
            DefaultHeaders = "{}",
        };

        if (instance.TryGet<HttpApiRequestToolSettings>(out var settings))
        {
            model.BaseUrl = settings.BaseUrl;
            model.HttpMethod = string.IsNullOrWhiteSpace(settings.HttpMethod) ? "GET" : settings.HttpMethod;
            model.AuthenticationType = settings.AuthenticationType;
            model.ApiKeyHeaderName = string.IsNullOrWhiteSpace(settings.ApiKeyHeaderName) ? "X-Api-Key" : settings.ApiKeyHeaderName;
            model.HasApiKey = !string.IsNullOrEmpty(settings.ApiKey);
            model.HasBearerToken = !string.IsNullOrEmpty(settings.BearerToken);
            model.BasicUsername = settings.BasicUsername;
            model.HasBasicPassword = !string.IsNullOrEmpty(settings.BasicPassword);
            model.AllowModelProvidedPath = settings.AllowModelProvidedPath;
            model.AllowModelProvidedQuery = settings.AllowModelProvidedQuery;
            model.AllowModelProvidedBody = settings.AllowModelProvidedBody;
            model.TimeoutSeconds = settings.TimeoutSeconds;
            model.DefaultHeaders = settings.DefaultHeaders is { Count: > 0 }
                ? JsonSerializer.Serialize(settings.DefaultHeaders, _indentedJsonOptions)
                : "{}";
        }

        return model;
    }
}
