using System.Text.Json;
using CrestApps.Core.AI.Mcp.Models;
using CrestApps.Core.Mvc.Web.Areas.Mcp.ViewModels;
using CrestApps.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ModelContextProtocol.Protocol;

namespace CrestApps.Core.Mvc.Web.Areas.Mcp.Controllers;

[Area("Mcp")]
[Authorize(Policy = "Admin")]
public sealed class McpPromptController : Controller
{
    private static readonly JsonSerializerOptions _indentedJsonOptions = new()
    {
        WriteIndented = true
    };
    private static readonly JsonSerializerOptions _messageJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
    private readonly INamedCatalog<McpPrompt> _catalog;
    private readonly TimeProvider _timeProvider;
    public McpPromptController(
        INamedCatalog<McpPrompt> catalog,
        TimeProvider timeProvider)
    {
        _catalog = catalog;
        _timeProvider = timeProvider;
    }

    public async Task<IActionResult> Index()
    {
        return View((await _catalog.GetAllAsync()).OrderBy(prompt => prompt.Name, StringComparer.OrdinalIgnoreCase).ToList());
    }

    public IActionResult Create()
    {
        return View(new McpPromptViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(McpPromptViewModel model)
    {
        var arguments = ParseArguments(model);
        var messages = ParseMessages(model);
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var prompt = new McpPrompt
        {
            ItemId = UniqueId.GenerateId(),
            CreatedUtc = _timeProvider.GetUtcNow().UtcDateTime,
        };
        Apply(model, prompt, arguments, messages);
        await _catalog.CreateAsync(prompt);

        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(string id)
    {
        var prompt = await _catalog.FindByIdAsync(id);
        if (prompt == null)
        {
            return NotFound();
        }

        return View(new McpPromptViewModel { ItemId = prompt.ItemId, Name = prompt.Name, Title = prompt.Prompt?.Title, Description = prompt.Prompt?.Description, Arguments = prompt.Prompt?.Arguments is { Count: > 0 } ? JsonSerializer.Serialize(prompt.Prompt.Arguments, _indentedJsonOptions) : "[]", Messages = prompt.Messages is { Count: > 0 } ? JsonSerializer.Serialize(prompt.Messages, _messageJsonOptions) : "[]", });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(McpPromptViewModel model)
    {
        var prompt = await _catalog.FindByIdAsync(model.ItemId);
        if (prompt == null)
        {
            return NotFound();
        }

        // Preserve the original name since it is readonly after creation.
        model.Name = prompt.Name;
        var arguments = ParseArguments(model);
        var messages = ParseMessages(model);
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        Apply(model, prompt, arguments, messages);
        await _catalog.UpdateAsync(prompt);

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id)
    {
        var prompt = await _catalog.FindByIdAsync(id);
        if (prompt == null)
        {
            return NotFound();
        }

        await _catalog.DeleteAsync(prompt);

        return RedirectToAction(nameof(Index));
    }

    private List<PromptArgument> ParseArguments(McpPromptViewModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Name))
        {
            ModelState.AddModelError(nameof(model.Name), "Name is required.");
        }

        if (string.IsNullOrWhiteSpace(model.Arguments))
        {
            return [];
        }

        try
        {
            var arguments = JsonSerializer.Deserialize<List<PromptArgument>>(model.Arguments) ?? [];
            for (var i = 0; i < arguments.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(arguments[i].Name))
                {
                    ModelState.AddModelError(nameof(model.Arguments), $"Argument {i + 1} requires a name.");
                }
            }

            return arguments.Where(argument => !string.IsNullOrWhiteSpace(argument.Name)).ToList();
        }
        catch (JsonException)
        {
            ModelState.AddModelError(nameof(model.Arguments), "Arguments must be valid JSON.");

            return [];
        }
    }

    private List<McpPromptMessage> ParseMessages(McpPromptViewModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Messages))
        {
            return [];
        }

        try
        {
            var messages = JsonSerializer.Deserialize<List<McpPromptMessage>>(model.Messages, _messageJsonOptions) ?? [];
            for (var i = 0; i < messages.Count; i++)
            {
                if (!IsValidRole(messages[i].Role))
                {
                    ModelState.AddModelError(nameof(model.Messages), $"Message {i + 1} must have a role of 'user' or 'assistant'.");
                }
            }

            return messages.Where(message => !string.IsNullOrWhiteSpace(message.Content)).ToList();
        }
        catch (JsonException)
        {
            ModelState.AddModelError(nameof(model.Messages), "Messages must be valid JSON.");

            return [];
        }
    }

    private static bool IsValidRole(string role)
        => string.Equals(role, "user", StringComparison.OrdinalIgnoreCase)
            || string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase);

    private static void Apply(McpPromptViewModel model, McpPrompt prompt, List<PromptArgument> arguments, List<McpPromptMessage> messages)
    {
        var name = model.Name.Trim();
        prompt.Name = name;
        prompt.Prompt = new Prompt
        {
            Name = name,
            Title = model.Title?.Trim(),
            Description = model.Description?.Trim(),
            Arguments = arguments,
        };
        prompt.Messages = messages;
    }
}
