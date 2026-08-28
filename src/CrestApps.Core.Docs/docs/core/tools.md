---
sidebar_label: Custom Tools
sidebar_position: 8
title: Custom AI Tools
description: Register AI-callable functions using the fluent tool builder pattern.
---

# Custom AI Tools

> Register functions that the AI model can invoke during orchestration using the fluent builder pattern.

## Quick Start

```csharp
builder.Services
    .AddCoreAIServices()
    .AddCoreAIOrchestration()
    .AddCoreAITool<WeatherTool>("get-weather")
        .WithTitle("Get Weather")
        .WithDescription("Returns current weather for a location.")
        .WithCategory("Utilities")
        .Selectable();
```

## Problem & Solution

AI models can call functions (tools) to access external data or perform actions. The framework needs a way to:

- **Register** tools with metadata (name, description, category)
- **Classify** tools as system (auto-included) or selectable (user-assignable)
- **Scope** tools dynamically based on context (profile, session, available data)
- **Control access** to tools based on permissions or context

The tool builder pattern provides a fluent API for all of this.

## Tool Types

| Type | Registration | Visibility | Use Case |
|------|-------------|-----------|----------|
| **Selectable** | `.Selectable()` | Visible in UI for profile assignment | User-facing tools (calculator, search) |
| **Hidden** | `.Hidden()` | Not shown in the picker or generic public exports; usable only when a profile or agent names it explicitly | Private helper tools for specialized agents |
| **System** | Default (no `.Selectable()`) | Hidden, auto-included by orchestrator | Internal tools (RAG search, image gen) |

## Fluent Builder API

### `AddCoreAITool<TTool>(name)`

Returns an `AIToolBuilder<TTool>` for fluent configuration:

| Method | Description |
|--------|-------------|
| `.WithTitle(string)` | Display title in UI |
| `.WithDescription(string)` | Description shown to the AI model |
| `.WithCategory(string)` | UI grouping category |
| `.WithPurpose(string)` | Semantic purpose tag (see below) |
| `.WithDependency(string)` / `.WithDependencies(params string[])` | Registers dependency tools that should be added automatically |
| `.WithoutDependency(string)` / `.WithoutDependencies(params string[])` | Removes previously registered dependency tools |
| `.Hidden()` | Makes the tool private to explicitly named profiles or agents and excludes it from picker-style/public export surfaces |
| `.Selectable()` | Makes the tool visible in UI and assignable to profiles |

### Tool Purposes

Purpose tags allow the orchestrator to include tools automatically based on context:

| Constant | Value | When Auto-Included |
|----------|-------|-------------------|
| `AIToolPurposes.ContentGeneration` | `"ContentGeneration"` | When the model wants to generate images or charts |
| `AIToolPurposes.DocumentProcessing` | `"DocumentProcessing"` | When documents are attached to the session |
| `AIToolPurposes.DataSourceSearch` | `"DataSourceSearch"` | When data sources are configured |

## Tool Dependencies

Use dependencies when one tool expects another tool to be present in the same completion.

```csharp
builder.Services
    .AddCoreAITool<CreateContentItemTool>("create_content_item")
        .WithTitle("Create Content Item")
        .WithDescription("Creates a content item from validated structured input.")
        .WithDependency("get_content_item_schema")
        .Selectable();

builder.Services
    .AddCoreAITool<GetContentItemSchemaTool>("get_content_item_schema")
        .WithTitle("Get Content Item Schema")
        .WithDescription("Returns the schema and required fields for a content type.");
```

When `create_content_item` is selected, CrestApps.Core automatically expands the tool set to include `get_content_item_schema` when that dependency is registered. The same dependency expansion is also applied when built-in system-tool selection brings in a tool with registered dependencies.

Dependency behavior:

- missing dependencies are ignored safely
- shared and circular dependency graphs are deduplicated
- profiles and chat interactions only need to store the top-level tool name
- dependency tools can stay hidden system tools when they should not be assigned directly in the UI

## Implementing a Tool

Tools inherit from `AITool` (which extends `AIFunction` from `Microsoft.Extensions.AI`):

```csharp
public sealed class WeatherTool : AITool
{
    public const string TheName = "get-weather";

    // Tool parameters are defined as a record or class
    private sealed record WeatherInput(string Location, string Units = "celsius");

    protected override async ValueTask<object> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        var input = arguments.Deserialize<WeatherInput>();
        // Call weather API...
        return new { Temperature = 22, Condition = "Sunny", Location = input.Location };
    }
}
```

Register it:

```csharp
builder.Services
    .AddCoreAITool<WeatherTool>(WeatherTool.TheName)
        .WithTitle("Weather")
        .WithDescription("Gets current weather for a location.")
        .WithCategory("Utilities")
        .Selectable();
```

## Tool Access Control

Tool access is governed differently for the two ways the AI is invoked.

### AI Sessions — the profile is the authorization boundary

For an **AI Session**, the **AI Profile is the authorization boundary**. A profile only exposes the
tools its author selected — the profile-selected functions, the MCP connections attached to the
profile, and the context-driven system tools. Every one of those is curated by an operator when the
profile is configured. If a caller is allowed to run a session against a profile, they are allowed
to use the tools that profile exposes; the runtime does **not** apply a per-user gate. This matters
because a session may be **anonymous** — it runs the profile exactly as configured.

Access is therefore decided where the profile (and which tools it may contain) is configured — for
example, the profile's Capabilities tab in the consuming application — rather than at completion
time. Profile editors already list only selectable (non-system, non-hidden) tools; consuming
applications own that selection UI and any permission checks around it.

### Chat Interactions — `IAIToolAccessEvaluator` re-verifies listable tools

A **Chat Interaction** is different: there is no session. Each interaction persists its own settings
(model, prompt, and the selected tool names) and starts a chat from them. Because those saved tool
names are attacker-controllable, a caller who tampered with an interaction could reference a
selectable tool they were never granted. To close that gap, when a Chat Interaction sends a message
the completion pipeline re-checks every **listable** (user-selectable) tool against
`IAIToolAccessEvaluator`:

```csharp
public interface IAIToolAccessEvaluator
{
    Task<bool> IsAuthorizedAsync(ClaimsPrincipal user, string toolName);
}
```

Tools the caller is not authorized for are excluded from the request (and reported in a single
`Warning` log entry) rather than failing it. Only listable tools are checked — **system tools**
(auto-injected by the orchestrator) and **hidden/dependency tools** are never subject to the check.
A `null` caller (a trusted server-side invocation such as a background task) skips the check
entirely.

The default implementation permits every tool. Consuming applications that enforce per-user tool
permissions replace it with an authorization-aware implementation, using the same permission model
that backs the Chat Interaction tool-selection UI.

### `IUserAccessor`

The caller principal used by the completion pipeline (for auditing, rate limiting, and any
host-side policy) comes from `IUserAccessor`, not from `IHttpContextAccessor`:

```csharp
public interface IUserAccessor
{
    ClaimsPrincipal User { get; set; }
}
```

It follows the same shape as `IHttpContextAccessor`. The default implementation resolves the caller in two steps:

1. If a principal was assigned on the current asynchronous flow, that principal wins.
2. Otherwise it falls back to `HttpContext.User` for ordinary HTTP requests.

This indirection exists because `IHttpContextAccessor.HttpContext` is unreliable inside SignalR hub invocations. Long-lived transports such as WebSockets, backplane-delivered invocations, and hosted SignalR services all run hub methods outside the request that opened the connection, so the accessor is frequently `null` there. The built-in hubs therefore assign `Context.User` at the start of every invocation:

```csharp
userAccessor.User = Context.User;

await DoWorkAsync();
```

The principal is tracked with an `AsyncLocal<T>`, exactly as `HttpContextAccessor` tracks the current request, so the assignment is confined to the invocation that made it. Concurrent connections never observe one another's caller, and the value does not leak back to the caller of the method that assigned it.

:::info Null means "no caller"
`User` returns `null` only when there is no caller at all, such as a background task, a workflow, or a recipe running server-side. An unauthenticated caller is different: hubs and HTTP requests always provide a non-`null` `ClaimsPrincipal` with an unauthenticated identity.
:::

Custom hosts that invoke completions outside of an HTTP request or a hub should assign the caller themselves so host-side policy sees the right principal.

## Custom Tool Registry Provider

Supply tools from an external source (database, API, etc.):

```csharp
public sealed class MyToolRegistryProvider : IToolRegistryProvider
{
    public async ValueTask<IEnumerable<AIToolMetadataEntry>> GetToolsAsync(
        AICompletionContext context,
        CancellationToken cancellationToken = default)
    {
        // Return tools dynamically based on context
    }
}

// Register
builder.Services.AddScoped<IToolRegistryProvider, MyToolRegistryProvider>();
```

## Complex Tool Example

A tool that queries an external API with nested object parameters, async operations, and error handling:

```csharp
public sealed class OrderLookupTool : AITool
{
    public const string TheName = "lookup-order";

    private sealed record OrderQuery(
        string OrderId,
        CustomerFilter Customer = null,
        bool IncludeLineItems = true);

    private sealed record CustomerFilter(
        string Email = null,
        string Phone = null);

    protected override async ValueTask<object> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        var logger = arguments.Services.GetRequiredService<ILogger<OrderLookupTool>>();
        var httpClientFactory = arguments.Services.GetRequiredService<IHttpClientFactory>();

        // Deserialize nested parameters
        var query = arguments.Deserialize<OrderQuery>();

        if (string.IsNullOrEmpty(query?.OrderId) && query?.Customer is null)
        {
            logger.LogWarning("AI tool '{ToolName}' requires an order ID or customer filter.", Name);
            return "Please provide either an order ID or customer information (email or phone).";
        }

        try
        {
            var client = httpClientFactory.CreateClient("OrderApi");

            HttpResponseMessage response;

            if (!string.IsNullOrEmpty(query.OrderId))
            {
                response = await client.GetAsync(
                    $"/api/orders/{Uri.EscapeDataString(query.OrderId)}?includeItems={query.IncludeLineItems}",
                    cancellationToken);
            }
            else
            {
                var searchParams = new Dictionary<string, string>();

                if (!string.IsNullOrEmpty(query.Customer?.Email))
                {
                    searchParams["email"] = query.Customer.Email;
                }

                if (!string.IsNullOrEmpty(query.Customer?.Phone))
                {
                    searchParams["phone"] = query.Customer.Phone;
                }

                var queryString = string.Join("&",
                    searchParams.Select(kvp => $"{kvp.Key}={Uri.EscapeDataString(kvp.Value)}"));

                response = await client.GetAsync(
                    $"/api/orders/search?{queryString}",
                    cancellationToken);
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Order API returned {StatusCode} for tool '{ToolName}'.",
                    response.StatusCode, Name);

                return response.StatusCode == System.Net.HttpStatusCode.NotFound
                    ? "No order found matching the provided criteria."
                    : "Unable to look up the order at this time. Please try again later.";
            }

            var order = await response.Content.ReadFromJsonAsync<JsonElement>(
                cancellationToken: cancellationToken);

            return order;
        }
        catch (TaskCanceledException)
        {
            logger.LogWarning("Order API timed out for tool '{ToolName}'.", Name);
            return "The order lookup timed out. Please try again.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception in tool '{ToolName}'.", Name);
            return "An error occurred while looking up the order.";
        }
    }
}
```

Register it:

```csharp
builder.Services
    .AddCoreAITool<OrderLookupTool>(OrderLookupTool.TheName)
        .WithTitle("Order Lookup")
        .WithDescription("Looks up an order by ID or customer email/phone. Returns order details and line items.")
        .WithCategory("Commerce")
        .Selectable();
```

## Tool Error Handling

When `InvokeCoreAsync` throws an exception, the orchestrator catches it and returns an error message to the AI model so the conversation can continue. However, **best practice is to never throw from a tool**. Instead, catch exceptions and return a descriptive error string:

| Pattern | Behavior | Recommended |
|---------|----------|-------------|
| Return error string | Model sees the message and can respond to the user | ✅ Yes |
| Throw exception | Orchestrator catches, logs, returns generic error to model | ❌ Avoid |
| Return `null` | Model sees an empty result | ❌ Avoid |

```csharp
// ✅ Good: Return a user-friendly error message
protected override async ValueTask<object> InvokeCoreAsync(
    AIFunctionArguments arguments, CancellationToken cancellationToken)
{
    try
    {
        // ... tool logic
        return new { result = "success", data = someData };
    }
    catch (HttpRequestException ex)
    {
        logger.LogError(ex, "External API call failed in tool '{ToolName}'.", Name);
        return "The external service is temporarily unavailable. Please try again later.";
    }
}

// ❌ Avoid: Letting exceptions propagate
protected override async ValueTask<object> InvokeCoreAsync(
    AIFunctionArguments arguments, CancellationToken cancellationToken)
{
    // This will crash and return a generic error to the model
    var result = await httpClient.GetStringAsync("/api/data", cancellationToken);
    return result;
}
```

:::tip
Use guard clauses with `ILogger` at each validation point. This creates a clear audit trail and gives the AI model actionable error messages that it can relay to the user.
:::

## Tool Return Types

`InvokeCoreAsync` returns `ValueTask<object>`. The framework serializes the return value to JSON before passing it to the AI model. Here's how different return types are handled:

| Return Type | Serialization | Example |
|------------|---------------|---------|
| `string` | Passed as-is | `return "The weather is sunny."` |
| Anonymous object | Serialized to JSON | `return new { temp = 22, condition = "Sunny" }` |
| Record/class | Serialized to JSON | `return new WeatherResult { Temp = 22 }` |
| `JsonElement` | Passed as raw JSON | `return JsonDocument.Parse(apiResponse).RootElement` |
| Primitive (int, bool) | Converted to string | `return 42` → `"42"` |
| `null` | Empty result | Avoid — return an error string instead |

For complex return types, use explicit JSON serialization for maximum control:

```csharp
protected override async ValueTask<object> InvokeCoreAsync(
    AIFunctionArguments arguments, CancellationToken cancellationToken)
{
    var result = await FetchDataAsync(cancellationToken);

    // Explicit serialization with custom options
    return JsonSerializer.Serialize(new
    {
        result.Id,
        result.Name,
        result.Description,
        result.CreatedUtc,
        ItemCount = result.Items.Count,
    });
}
```

:::info
Keep return values concise. Large JSON payloads consume tokens and may hit model context limits. Return only the fields the AI model needs to formulate a response.
:::

## Testing Tools

Unit test tools by creating a mock `AIFunctionArguments` with the required services:

```csharp
public sealed class WeatherToolTests
{
    [Fact]
    public async Task InvokeAsync_WithValidLocation_ShouldReturnWeatherData()
    {
        // Arrange
        var mockHandler = new MockHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { temp = 22, condition = "Sunny" }),
            });

        var httpClient = new HttpClient(mockHandler)
        {
            BaseAddress = new Uri("https://api.weather.test/"),
        };

        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient("WeatherApi").Returns(httpClient);

        var services = new ServiceCollection()
            .AddSingleton(httpClientFactory)
            .AddLogging()
            .BuildServiceProvider();

        var tool = new WeatherTool();

        var arguments = new AIFunctionArguments(
            new Dictionary<string, object>
            {
                ["Location"] = "Seattle",
                ["Units"] = "celsius",
            })
        {
            Services = services,
        };

        // Act
        var result = await tool.InvokeAsync(arguments);

        // Assert
        Assert.NotNull(result);
        var json = result.ToString();
        Assert.Contains("22", json);
        Assert.Contains("Sunny", json);
    }

    [Fact]
    public async Task InvokeAsync_WithMissingLocation_ShouldReturnErrorMessage()
    {
        // Arrange
        var services = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();

        var tool = new WeatherTool();

        var arguments = new AIFunctionArguments(
            new Dictionary<string, object>())
        {
            Services = services,
        };

        // Act
        var result = await tool.InvokeAsync(arguments);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("location", result.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private sealed class MockHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(response);
    }
}
```

:::tip
Test tools in isolation from the AI model. Focus on:
1. **Valid input** → correct API call and return value
2. **Missing/invalid input** → descriptive error string (no exceptions)
3. **External service failure** → graceful error message
4. **Cancellation** → respects `CancellationToken`
:::
