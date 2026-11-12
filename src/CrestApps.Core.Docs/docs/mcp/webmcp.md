---
sidebar_label: WebMCP
sidebar_position: 4
title: WebMCP
description: Register your AI tools and page tools with the standard WebMCP API, and let the AI chat discover and run every WebMCP tool the page it is used from exposes.
---

# WebMCP

> Give your AI tools to AI agents in the browser, and let the AI chat use every tool the page the user has open exposes.

[WebMCP](https://webmachinelearning.github.io/webmcp/) is a proposed web standard of the W3C Web Machine Learning Community Group. It lets a web page give tools to an AI agent in the browser, such as the browser's own assistant, an agent extension, or a chat on the page. The page registers each tool with `document.modelContext.registerTool()`: a name, a description, a JSON schema, annotations and a JavaScript `execute` function. An agent lists the tools with `getTools()` and runs one with `executeTool()`. The tool runs in the page, with the user's session, so no API key or OAuth setup is needed.

CrestApps uses only that standard API, in both directions:

| Part | What it does |
| --- | --- |
| **Your AI tools as WebMCP tools** | Registers the AI tools and tool instances you select with `document.modelContext`. Calling one posts to your application, which runs the tool as the signed-in user. Any WebMCP agent can find and run them. |
| **Page tools** | Registers tools that read the page, list its links, open another page of the site, and read and fill its forms. |
| **The AI chat as a WebMCP agent** | Lets the CrestApps AI chat discover and run **every** WebMCP tool the page exposes, whoever registered it: your page tools, tools your own scripts register, a third-party script's tools, or a frame's tools shared with `exposedTo`. Only for the AI profiles that allow it. |

There are two parts to install:

| Package | What it has |
| --- | --- |
| `CrestApps.Core.AI.WebMcp` (NuGet) | The server: the endpoints that list and run your AI tools, and the page tools in the chat. |
| `CrestApps.AI.WebMcp.Resources` (Razor class library) and `@crestapps/webmcp` (npm) | The browser scripts, served from `_content/CrestApps.AI.WebMcp.Resources/scripts/`, and the [WebMCP polyfill](#the-polyfill). |

:::note[Browser support]
WebMCP is experimental. Chrome and Edge are trialling it natively. In the other browsers, `webmcp-polyfill.js` installs the reference implementation of `document.modelContext`, so the tools and the AI chat work everywhere. WebMCP needs a secure context: HTTPS, or `localhost` while developing.
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
    <script src="~/_content/CrestApps.AI.WebMcp.Resources/scripts/webmcp-polyfill.js"></script>
    <script src="~/_content/CrestApps.AI.WebMcp.Resources/scripts/webmcp.js"></script>
    <script src="~/_content/CrestApps.AI.WebMcp.Resources/scripts/webmcp-page-tools.js"></script>
}
```

Reference the `CrestApps.AI.WebMcp.Resources` project (or copy the scripts from the `@crestapps/webmcp` npm package) to serve the scripts. The polyfill comes first, so every script after it finds `document.modelContext`.

The MVC sample host does this in `Views/Shared/_WebMcp.cshtml`, and the Blazor sample host in `Components/App.razor`.

### Configuration

| Key | What it does |
| --- | --- |
| `toolsUrl`, `invokeUrlTemplate` | Where `webmcp.js` lists your AI tools and calls one (`{name}` is replaced with the tool name). Leave them out to register no AI tools. |
| `antiforgeryHeaderName`, `antiforgeryToken` | Sent with every tool call. |
| `isAdmin` | Tells `page_get_info` whether the page is an admin page. |
| `pageToolOrigins` | The origins of cross-origin frames whose shared tools the AI chat may use, passed to `getTools({ fromOrigins })`. Leave it out to use only the page's own tools. |

## The polyfill

`webmcp-polyfill.js` is the reference polyfill of the W3C Web Machine Learning Community Group, from the MIT-licensed [`@mcp-b/webmcp-polyfill`](https://www.npmjs.com/package/@mcp-b/webmcp-polyfill) package; its license is next to it as `webmcp-polyfill.LICENSE.txt`. It installs `document.modelContext` only when the browser does not have it, so a browser with native WebMCP keeps its own. CrestApps does not reimplement the standard; `CrestApps.AI.WebMcp.Resources` depends on the polyfill and copies it at build time.

## Your AI tools as WebMCP tools

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

`webmcp.js` registers each of your AI tools with `document.modelContext.registerTool()`. Each tool has the `untrustedContentHint` annotation, because it answers data the page did not write. Its `execute` posts the input to the invoke endpoint with the antiforgery token, answers the text the tool returned, and rejects when the tool fails, which is how WebMCP reports a failed tool. The script never sets `exposedTo`, so your tools are not given to cross-origin frames.

It also keeps a small facade at `window.crestAppsWebMcp`:

| Member | What it does |
| --- | --- |
| `ready` | A promise of the names of the AI tools it registered. |
| `isSupported()` | Says whether the page has `document.modelContext`. |
| `connectChat(connection)` | Connects the AI chat to the page's tools. `ai-chat.js` calls it. |
| `config` | The page configuration. |

Register your own tools with the standard API. Nothing CrestApps-specific is needed, and the AI chat finds them like any other tool:

```javascript
const registration = new AbortController();

document.modelContext.registerTool({
    name: 'contoso.find_store',
    title: 'Find a store',
    description: 'Finds the Contoso store nearest to a postal code.',
    inputSchema: {
        type: 'object',
        properties: { postalCode: { type: 'string' } },
        required: ['postalCode'],
    },
    annotations: { readOnlyHint: true },
    execute: async ({ postalCode }) => findStore(postalCode),
}, { signal: registration.signal });

// Later, to unregister it:
registration.abort();
```

## Page tools

`webmcp-page-tools.js` registers these tools with `document.modelContext`. Each answers a JSON value and rejects when it fails, so any WebMCP agent can use them:

| Tool | Read-only | What it does |
| --- | --- | --- |
| `page_get_info` | Yes | The page's title, address, description, first 20 `h1` and `h2` headings, and whether it is an admin page (the `isAdmin` setting). |
| `page_read_content` | Yes | What the page shows in `main` (or `[role="main"]`, else the body): its text, one line per block and cut to 8,000 characters, and up to 10 tables as caption, headers and the first 50 rows (cells cut to 300 characters), each marked `[table N]` in the text. Menus, forms, scripts, hidden parts, the chat and anything marked `data-webmcp-ignore` are left out, and `truncated` says whether something was cut. |
| `page_list_links` | Yes | Up to 200 same-origin links in `nav`, `header`, `aside` and `[role="navigation"]`, with their text. An optional `filter` keeps the links whose text contains it. |
| `page_navigate` | No | Opens a same-origin address. Other origins and script addresses are refused. It answers first and opens the page a moment later (`navigateDelayMs`, 400 ms by default), so a chat that called it gets the result; an [AG-UI run](../core/ag-ui.md#runs-that-continue-on-the-next-page) then ends its turn, and the chat on the next page continues it with that page's tools. |
| `page_list_form_fields` | Yes | Up to 200 form fields: name, id, label, type, value (cut to 2,000 characters), required and, for lists, the choices. Hidden, password, file, button and disabled fields and the antiforgery token are left out. |
| `page_set_form_field` | No | Puts a value into one listed field, found by name, id or label, and raises `input` and `change`. For a list, the value is a choice's value or text. It never submits the form. |

## The AI chat as a WebMCP agent

With `AddPageTools<THub>()`, the AI chat acts as a WebMCP agent for the browser tab a message is sent from. It uses every tool `document.modelContext.getTools()` lists, not only the CrestApps ones:

1. Before it sends a message, `ai-chat.js` reads the page's tools with `getTools()` and sends them to the hub with `SetPageTools`, when they changed since the last message. It leaves out the AI tools `webmcp.js` registered, because the chat reaches those on the server. A tool registered after the page loaded is picked up with the next message.
2. `AIChatHubCore.SetPageTools` hands them to `IWebMcpPageToolRegistry`, which keeps them for the connection. The registry checks each tool: at most 64, WebMCP names (`^[A-Za-z0-9_.-]{1,128}$`), a description of at most 2,000 characters and a JSON object schema of at most 16 KB. `ClearPageTools` and a closed connection forget them.
3. `AIChatResponseHandler` puts the connection id in the completion context under `AICompletionContextKeys.ChatConnectionId`.
4. `WebMcpPageToolRegistryProvider`, an `IToolRegistryProvider`, adds one `WebMcpPageToolFunction` per page tool when the chat session's profile has `WebMcpProfileSettings.AllowPageTools` set.
5. When the model calls a page tool, `HubWebMcpPageToolClient<THub>` invokes `ExecutePageTool` on the connection with SignalR client results. The tab runs the tool with `executeTool()` and returns `{ text, isError }`, which the server waits for up to `WebMcpPageToolOptions.ToolCallTimeout` (60 seconds by default).

In an [AG-UI run](../core/ag-ui.md), the chat shows each page tool call and its result as it happens.

### Tool names

WebMCP names may contain `.`, which AI providers do not accept in function names. The model sees each tool under a function name: characters other than letters, digits, `_` and `-` become `_`, and a name longer than 64 characters keeps its start and ends with a short hash. `store.add_to_cart` is offered as `store_add_to_cart`, and the tab still runs `store.add_to_cart`. `WebMcpPageToolValidator.ToFunctionName` does the mapping.

### Annotations

The chat honors the annotations the page sets:

| Annotation | What the chat does |
| --- | --- |
| `readOnlyHint` | Kept with the tool (`WebMcpPageTool.ReadOnly`). |
| `consequentialHint` | The tool can do something that cannot be undone, such as placing an order. In an [AG-UI run](../core/ag-ui.md#tool-approvals) (the chat UI's default) it is offered to the model and each call waits for the user's **Allow** or **Deny**; with `WebMcpProfileSettings.AllowConsequentialPageTools` it runs without asking. Other chat runs offer it only when the profile allows consequential page tools. |
| `untrustedContentHint` | The result reaches the model after a notice that it is untrusted content to treat as data, not as instructions (`WebMcpPageToolFunction.UntrustedContentNotice`). |

### Profiles

Only profiles that opt in get page tools:

```csharp
profile.AlterSettings<WebMcpProfileSettings>(settings =>
{
    settings.AllowPageTools = true;

    // Also offer the tools the page marks with consequentialHint.
    settings.AllowConsequentialPageTools = true;
});
```

The sample hosts show these as **Allow page tools** and **Allow consequential page tools** in the profile editor.

### Frames on other origins

A frame on another origin can share its tools with the page by registering them with `exposedTo`, and the page must allow it with the `tools` permissions policy: `<iframe src="https://partner.example" allow="tools">`. List the frame's origin in `pageToolOrigins` for the chat to use those tools.

The registry is in memory. A connection lives on one server and the completions of the messages it sends run on the same server, so it needs no shared store in a web farm. Client results need `HubOptions.MaximumParallelInvocationsPerClient` above 1, which `ConfigureCrestAppsChatHubOptions<THub>()` sets.

## The WebMCP playground

Both sample hosts have a **WebMCP Playground** page (`/webmcp/playground`) for checking that an agent finds and runs the tools a page exposes. It is a fake store whose script, `wwwroot/js/webmcp-playground.js`, uses only the standard API and no CrestApps code: it registers `store.search_products`, `store.get_order`, `store.read_reviews` (`untrustedContentHint`), `store.add_to_cart` and `store.place_order` (`consequentialHint`), lists every tool on the page with `getTools()`, and unregisters its tools when you leave the page. Ask the chat widget, with a profile that allows page tools, to check order A-1001 or add a desk lamp to the cart.

## Security

- Nothing is given by default: you select the tools, and only signed-in users get the script.
- The server checks the user, `IWebMcpAccessEvaluator`, `WebMcpOptions` and `IAIToolAccessEvaluator` on every list and every call. The script's list only decides what is registered in the browser.
- Calls need the antiforgery token, so another site cannot make the user's browser call a tool.
- A browser agent acts as the signed-in user. Give agents only the tools you would let the user run, and prefer read-only tools.
- Page tools never submit forms and never leave the site, so the user reviews and saves what an agent changes.
- Page tools only reach the completions of messages sent from the same connection, and only for profiles that allow them. A profile that allows page tools lets the model run any tool the page exposes, including a third-party script's, so allow them only on profiles used on pages whose scripts you trust.
- Tools marked `consequentialHint` wait for the user's approval in an AG-UI run, or need a second opt-in, and results marked `untrustedContentHint` reach the model as data.
- Frames on other origins reach the chat only when the page allows them with `allow="tools"` and you list their origin in `pageToolOrigins`.
