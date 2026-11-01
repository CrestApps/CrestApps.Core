---
sidebar_label: WebMCP
sidebar_position: 4
title: WebMCP
description: Give AI tools and page tools to AI agents in the browser through WebMCP, and let the AI chat call the tools of the page it is used from.
---

# WebMCP

> Give your AI tools to AI agents in the browser, and let the AI chat work with the page the user has open.

[WebMCP](https://github.com/webmachinelearning/webmcp) is a proposed web standard that lets a web page give tools to an AI agent running in the browser, such as the browser's own assistant or an agent extension. The page registers each tool with the browser's `ModelContext` (`document.modelContext` in the current draft, `navigator.modelContext` in earlier browser previews): a name, a description, a JSON schema and a JavaScript function. The tool runs in the page, with the user's session, so no API key or OAuth setup is needed.

`CrestApps.Core.AI.WebMcp` connects your application to WebMCP in two ways:

| Part | What it does |
| --- | --- |
| **Server tools for browser agents** | Gives the AI tools and tool instances you select to browser agents. The page script registers one proxy per tool; calling it posts to your application, which runs the tool as the signed-in user. |
| **Page tools in the AI chat** | Lets the CrestApps AI chat call the WebMCP tools of the browser tab a message is sent from, such as reading the page and filling its forms, for the AI profiles that allow it. |

:::note[Browser support]
WebMCP is experimental. It is behind an origin trial in Chrome 149 and Edge 150, and behind the `enable-webmcp-testing` flag in `about:flags` for local work. The scripts give tools to the browser only when it has a `ModelContext`; the page's own list of tools works in every browser, so the AI chat's page tools do not depend on browser support.
:::

## Quick Start

```csharp
builder.Services.AddCrestAppsCore(crestApps => crestApps
    .AddAISuite(ai => ai
        .AddOpenAI()
        .AddWebMcp(webMcp => webMcp
            .AddPageTools<AIChatHub>()
        )
    )
);

builder.Services.Configure<WebMcpOptions>(options =>
{
    options.Tools = ["calculator"];
});

var app = builder.Build();

app.MapHub<AIChatHub>("/hubs/ai-chat");
app.MapWebMcpTools();
```

Without the builder:

```csharp
builder.Services
    .AddCoreAIWebMcp()
    .AddCoreAIWebMcpPageTools<AIChatHub>();
```

Then add the configuration block and the scripts to the pages of signed-in users, before the chat scripts:

```cshtml
@inject IAntiforgery Antiforgery

@if (User.Identity?.IsAuthenticated == true)
{
    var tokens = Antiforgery.GetAndStoreTokens(Context);
    var configuration = new Dictionary<string, object>
    {
        ["toolsUrl"] = Url.Content("~/api/ai/webmcp/tools"),
        ["invokeUrlTemplate"] = Url.Content("~/api/ai/webmcp/tools/") + "{name}/invoke",
        ["antiforgeryHeaderName"] = tokens.HeaderName,
        ["antiforgeryToken"] = tokens.RequestToken,
    };

    <script type="application/json" id="crestapps-webmcp-config">@Html.Raw(JsonSerializer.Serialize(configuration))</script>
    <script src="~/_content/CrestApps.AI.Resources/scripts/webmcp.js"></script>
    <script src="~/_content/CrestApps.AI.Resources/scripts/webmcp-page-tools.js"></script>
}
```

The MVC sample host does this in `Views/Shared/_WebMcp.cshtml`, and the Blazor sample host in `Components/App.razor`.

## Server tools for browser agents

### Choosing the tools

`WebMcpOptions` decides which tools browser agents may call. Nothing is given by default.

| Property | Default | What it does |
| --- | --- | --- |
| `ExposeAllTools` | `false` | Gives every selectable tool and tool instance. System tools and hidden tools are never given. |
| `Tools` | empty | The tool and tool instance names given when `ExposeAllTools` is `false`, matched without regard to case. |

The sample hosts edit these on their settings page, in the **WebMCP** card, and store them with `SiteSettingsConfigureStoredOptions<WebMcpOptions>`.

A tool is listed and callable for a user only when it is selectable, `WebMcpOptions` allows it, and `IAIToolAccessEvaluator` authorizes it for that user. The same tool catalog (`AddCoreAITool<T>()`) and tool instances the rest of the AI services use are the source; WebMCP adds no tools of its own.

### Who may use them

`IWebMcpAccessEvaluator` decides whether a user may use the endpoints at all. The default, `DefaultWebMcpAccessEvaluator`, allows every signed-in user. Register your own before `AddWebMcp` to require a role or permission:

```csharp
builder.Services.AddSingleton<IWebMcpAccessEvaluator, AdministratorsOnlyWebMcpAccessEvaluator>();
```

### Endpoints

`app.MapWebMcpTools(prefix)` maps two endpoints under the prefix (`api/ai/webmcp` by default) and returns the route group, so you can add rate limiting or other conventions.

| Method and route | What it does | Answers |
| --- | --- | --- |
| `GET {prefix}/tools` | Lists the tools the user may call: `{ "tools": [{ "name", "title", "description", "inputSchema" }] }` | `401` when not signed in, `403` when `IWebMcpAccessEvaluator` refuses |
| `POST {prefix}/tools/{name}/invoke` | Calls a tool with the JSON object in the body, inside an `AIInvocationScope`, and answers an MCP-style result: `{ "content": [{ "type": "text", "text" }], "isError" }` | `401`, `403`, `400` without a valid antiforgery token in the request header or when the body is not a JSON object or is larger than 256 KB, `404` when the tool is not listed for the user |

A tool that throws answers `isError: true` with a generic message; the exception goes to the log only. A tool the user may not call answers `404` whatever the reason, so the endpoint does not tell which tools exist.

### The page script

`webmcp.js` reads the configuration block and keeps the page's list of tools at `window.crestAppsWebMcp`:

| Member | What it does |
| --- | --- |
| `registerTool(tool)` | Adds a tool `{ name, title?, description, inputSchema?, annotations?, execute }` and gives it to the browser's `ModelContext`. A tool registered again under the same name replaces the first. |
| `unregisterTool(name)` | Removes a tool from the page and from the browser. |
| `getTools()` | Lists the tools without their functions. Server tools have `source: 'server'`, the others `source: 'page'`. |
| `executeTool(name, input)` | Runs a tool on the page. |
| `onToolsChanged(listener)` | Calls the listener when the list changes, and answers a function that stops it. |
| `isSupported()` | Says whether the browser supports WebMCP. |
| `connectChat(connection)` | Connects the page tools to a chat hub connection. `ai-chat.js` calls it. |
| `config` | The page configuration. |

Each server tool is a proxy with the `untrustedContentHint` annotation that posts to the invoke endpoint with the antiforgery token. Tools are unregistered from the browser with the `AbortSignal` passed to `registerTool`, and with `unregisterTool` where the browser offers it. The script never sets `exposedTo`, so tools are not given to cross-origin frames.

Add your own tools after the script has loaded:

```javascript
window.crestAppsWebMcp.registerTool({
    name: 'contoso_find_store',
    title: 'Find a store',
    description: 'Finds the Contoso store nearest to a postal code.',
    inputSchema: {
        type: 'object',
        properties: { postalCode: { type: 'string' } },
        required: ['postalCode'],
    },
    annotations: { readOnlyHint: true },
    execute: async ({ postalCode }) => ({
        content: [{ type: 'text', text: await findStore(postalCode) }],
    }),
});
```

## Page tools

`webmcp-page-tools.js` registers tools that run in the browser only:

| Tool | Read-only | What it does |
| --- | --- | --- |
| `page_get_info` | Yes | The page's title, address, description, first 20 `h1` and `h2` headings, and whether it is an admin page (the `isAdmin` setting). |
| `page_list_links` | Yes | Up to 200 same-origin links in `nav`, `header`, `aside` and `[role="navigation"]`, with their text. An optional `filter` keeps the links whose text contains it. |
| `page_navigate` | No | Opens a same-origin address. Other origins and script addresses are refused. |
| `page_list_form_fields` | Yes | Up to 200 form fields: name, id, label, type, value (cut to 2,000 characters), required and, for lists, the choices. Hidden, password, file, button and disabled fields and the antiforgery token are left out. |
| `page_set_form_field` | No | Puts a value into one listed field, found by name, id or label, and raises `input` and `change`. For a list, the value is a choice's value or text. It never submits the form. |

## Page tools in the AI chat

With `AddPageTools<THub>()`, the AI chat calls the page tools of the browser tab a message is sent from:

1. Before it sends a message, `ai-chat.js` sends the page's tools (page tools only, not server tools) to the hub with `SetPageTools`, when they changed since the last message. A tab with no tools sends nothing.
2. `AIChatHubCore.SetPageTools` hands them to `IWebMcpPageToolRegistry`, which keeps them for the connection. The registry checks each tool: at most 64, names AI providers accept (`^[a-zA-Z0-9_-]{1,64}$`), a description of at most 2,000 characters and a JSON object schema of at most 16 KB. `ClearPageTools` and a closed connection forget them.
3. `AIChatResponseHandler` puts the connection id in the completion context under `AICompletionContextKeys.ChatConnectionId`.
4. `WebMcpPageToolRegistryProvider`, an `IToolRegistryProvider`, adds one `WebMcpPageToolFunction` per page tool when the chat session's profile has `WebMcpProfileSettings.AllowPageTools` set.
5. When the model calls a page tool, `HubWebMcpPageToolClient<THub>` invokes `ExecutePageTool` on the connection with SignalR client results and waits for the `{ text, isError }` the tab returns, up to `WebMcpPageToolOptions.ToolCallTimeout` (60 seconds by default).

Only profiles that opt in get page tools:

```csharp
profile.AlterSettings<WebMcpProfileSettings>(settings => settings.AllowPageTools = true);
```

The sample hosts show this as **Allow page tools** in the profile editor.

The registry is in memory. A connection lives on one server and the completions of the messages it sends run on the same server, so it needs no shared store in a web farm. Client results need `HubOptions.MaximumParallelInvocationsPerClient` above 1, which `ConfigureCrestAppsChatHubOptions<THub>()` sets.

## Security

- Nothing is given by default: you select the tools, and only signed-in users get the script.
- The server checks the user, `IWebMcpAccessEvaluator`, `WebMcpOptions` and `IAIToolAccessEvaluator` on every list and every call. The script's list only decides what is registered in the browser.
- Calls need the antiforgery token, so another site cannot make the user's browser call a tool.
- A browser agent acts as the signed-in user. Give agents only the tools you would let the user run, and prefer read-only tools.
- Page tools never submit forms and never leave the site, so the user reviews and saves what an agent changes.
- Page tools only reach the completions of messages sent from the same connection, and only for profiles that allow them.
