---
sidebar_label: SignalR
sidebar_position: 11
title: SignalR Hub Management
description: Centralized SignalR hub route registration and URL generation with multi-tenant path prefix support.
---

# SignalR Hub Management

> Centralized hub route registration and URL generation with support for multi-tenant path prefixes.

## Quick Start

```csharp
builder.Services.AddCoreSignalR();
```

## Why This Abstraction?

In a standard ASP.NET Core application, SignalR hub paths are hardcoded at startup:

```csharp
app.MapHub<ChatHub>("/chatHub");
```

1. Discover the current tenant's URL prefix
2. Build the correct hub path with that prefix
3. Expose the full URL to client-side JavaScript for connection

The `HubRouteManager` solves all three problems by providing a single service that:

- **Centralizes path construction** — one place handles the prefix logic
- **Generates correct URLs** — both relative paths (for `MapHub`) and absolute URIs (for JavaScript clients)
- **Prevents path conflicts** — all hubs follow the same `/Communication/Hub/{HubName}` pattern

Without this, you would see bugs like tenant A's JavaScript connecting to tenant B's hub, or paths breaking after deployment behind a reverse proxy.

## Problem & Solution

SignalR hubs need consistent route registration and URL generation across features. In multi-tenant environments, hub paths must include a tenant prefix. The `HubRouteManager` centralizes this so individual features don't manage paths independently.

## Real-time Chat Example

The primary consumer of `HubRouteManager` is the AI Chat system. Here is how the pieces fit together:

### Server-side: Hub Registration

```csharp
// In the Chat module's Startup.Configure():
public override void Configure(
    IApplicationBuilder app, IEndpointRouteBuilder routes, IServiceProvider serviceProvider)
{
    // Maps the AIChatHub at /Communication/Hub/AIChatHub (with tenant prefix)
    HubRouteManager.MapHub<AIChatHub>(routes);
}
```

### Server-side: URL Generation for Client

```csharp
// In a Razor view or controller, generate the hub URL for the client:
public class ChatController(HubRouteManager hubRouteManager)
{
    public IActionResult Index()
    {
        var hubUrl = hubRouteManager.GetUriByHub<AIChatHub>(HttpContext);
        // Returns: "https://example.com/tenant-a/Communication/Hub/AIChatHub"

        ViewBag.ChatHubUrl = hubUrl;
        return View();
    }
}
```

### Client-side: JavaScript Connection

```javascript
// Connect to the hub using the URL generated server-side
const connection = new signalR.HubConnectionBuilder()
    .withUrl(chatHubUrl) // URL from the server
    .withAutomaticReconnect()
    .build();

// Listen for streamed AI responses
connection.on("ReceiveMessage", (update) => {
    appendMessage(update.text);
});

// Send a message
await connection.invoke("SendMessage", {
    profileId: "my-profile",
    sessionId: sessionId,
    message: userInput,
});

await connection.start();
```

### How Streaming Works

When a user sends a message, the `AIChatHub` processes it through the response handler pipeline. For the default AI handler, the response is streamed token-by-token:

```text
Client                          AIChatHub                       Orchestrator
  │                                │                                │
  │── SendMessage(msg) ──────────▶│                                │
  │                                │── ExecuteStreamingAsync() ───▶│
  │                                │                                │
  │                                │◀── ChatResponseUpdate ────────│
  │◀── ReceiveMessage(chunk1) ────│                                │
  │                                │◀── ChatResponseUpdate ────────│
  │◀── ReceiveMessage(chunk2) ────│                                │
  │                                │◀── (stream complete) ─────────│
  │◀── MessageCompleted ──────────│                                │
```

## Hub Registration

### Registering a Custom Hub

To register your own SignalR hub that works correctly in multi-tenant environments:

**1. Define your hub:**

```csharp
public sealed class NotificationHub : Hub
{
    public async Task Subscribe(string topic)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, topic);
    }

    public async Task Broadcast(string topic, string message)
    {
        await Clients.Group(topic).SendAsync("Notify", message);
    }
}
```

**2. Map it using `HubRouteManager`:**

```csharp
public sealed class Startup : StartupBase
{
    public override void Configure(
        IApplicationBuilder app, IEndpointRouteBuilder routes, IServiceProvider serviceProvider)
    {
        // The static MapHub<T> method uses the default path pattern
        HubRouteManager.MapHub<NotificationHub>(routes);
        // Maps to: /{tenant-prefix}/Communication/Hub/NotificationHub
    }
}
```

**3. Generate the URL for clients:**

```csharp
public class MyService(HubRouteManager hubRouteManager)
{
    public string GetNotificationHubUrl(HttpContext httpContext)
    {
        return hubRouteManager.GetUriByHub<NotificationHub>(httpContext);
    }
}
```

## Authentication and Authorization

`AddCoreSignalR()` does not configure authentication. Hubs participate in the host's existing ASP.NET Core authentication pipeline, so `app.UseAuthentication()` must run before endpoint routing resolves the hub, exactly as it does for controllers and minimal API endpoints.

### Two Authorization Models

CrestApps hubs support two models, and you can combine them.

**Connection-level** — apply `[Authorize]` to the hub class. The connection is rejected during the negotiate request when the caller fails the policy. Use it when every method on the hub requires the same identity.

```csharp
[Authorize]
public sealed class NotificationHub : Hub
{
}
```

**Per-invocation** — allow the connection, then authorize each call against the resource it targets. The chat hubs use this model because a single connection can address several profiles or interactions, each with its own access rules. `AIChatHubCore<TClient>` and `ChatInteractionHubBase` are not decorated with `[Authorize]`, and their authorization hooks return `true` by default, so you must override them to enforce access:

```csharp
[AllowAnonymous]
public sealed class AIChatHub : AIChatHubCore<IAIChatHubClient>
{
    protected override Task<bool> AuthorizeProfileAsync(IServiceProvider services, AIProfile profile)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(profile);

        return Task.FromResult(MyAccessRules.CanAccessProfile(Context.User, profile.ItemId));
    }
}
```

| Hub | Hook to override | Default |
| --- | --- | --- |
| `AIChatHubCore<TClient>` | `AuthorizeProfileAsync(IServiceProvider, AIProfile)` | Returns `true` |
| `ChatInteractionHubBase` | `AuthorizeAsync(IServiceProvider, ChatInteraction)` | Returns `true` |

When a hook returns `false`, the hub sends `ReceiveError` to the caller with the message from `GetNotAuthorizedMessage()` instead of throwing, so the client stays connected and can retry with a different resource.

:::warning
The default implementations permit everything. A hub that is reachable anonymously and does not override its hook grants every caller access to every profile or interaction. Override the hook, apply `[Authorize]`, or both.
:::

### Authenticating Headless Clients with Access Tokens

Browser clients that already hold an authentication cookie need nothing extra, because the cookie is sent with the negotiate request and the WebSocket handshake.

Headless clients — single page applications, mobile applications, and service-to-service callers — send a bearer token instead. Browsers cannot set an `Authorization` header on a WebSocket handshake, so SignalR clients fall back to the standard `access_token` query string parameter. Configure the bearer handler to read it:

```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = "https://identity.example.com";
        options.Audience = "chat-api";

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];

                if (!string.IsNullOrEmpty(accessToken) &&
                    context.HttpContext.Request.Path.StartsWithSegments("/Communication/Hub"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            },
        };
    });
```

The path check keeps the query string token scoped to hub endpoints, so it is not accepted on the rest of the site.

Clients supply the token through `accessTokenFactory` or `AccessTokenProvider`:

```javascript
const connection = new signalR.HubConnectionBuilder()
    .withUrl(chatHubUrl, {
        accessTokenFactory: () => accessToken,
    })
    .withAutomaticReconnect()
    .build();
```

```csharp
var connection = new HubConnectionBuilder()
    .WithUrl(chatHubUrl, options =>
    {
        options.AccessTokenProvider = () => Task.FromResult(accessToken);
    })
    .Build();
```

### Accepting Several Schemes on One Hub

When a hub must accept both a cookie and a token, build a policy that names each scheme and require it on the hub endpoint. Listing the schemes explicitly matters, because a policy evaluates only the schemes it names:

```csharp
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("HubAccess", policy =>
    {
        policy.AddAuthenticationSchemes(
            JwtBearerDefaults.AuthenticationScheme,
            CookieAuthenticationDefaults.AuthenticationScheme);
        policy.RequireAuthenticatedUser();
    });
```

```csharp
app.MapHub<NotificationHub>("/Communication/Hub/NotificationHub")
    .RequireAuthorization("HubAccess");
```

:::caution
Do not attach a scheme-listing policy to a hub that must also accept anonymous connections. When none of the named schemes succeed, the authorization middleware replaces `HttpContext.User` with an empty principal, which discards an identity that a scheme outside the policy had already established. For anonymous-capable hubs, register the token handler in the default authenticate scheme chain instead, so `UseAuthentication()` populates the user without any policy.
:::

### Reading the Caller's Identity Inside a Hub

Use `Context.User` for the caller's identity and `Context.GetHttpContext()` when you need the request that opened the connection.

```csharp
var user = Context.User;
var httpContext = Context.GetHttpContext();
```

:::danger
Do not resolve the caller through `IHttpContextAccessor` in code that runs during a hub invocation. SignalR dispatches hub methods outside the request pipeline, so `IHttpContextAccessor.HttpContext` is unreliable and is frequently `null`, particularly over WebSockets and when running behind a SignalR backplane or Azure SignalR Service. See [Use HttpContext in ASP.NET Core SignalR](https://learn.microsoft.com/aspnet/core/signalr/httpcontext).

This applies to services you call from a hub as well. A service that reads `IHttpContextAccessor.HttpContext?.User` behaves differently when it is invoked from a controller than when it is invoked from a hub. If such a service makes a security decision, pass `Context.User` into it explicitly rather than letting it resolve the user itself.
:::

This matters for tool authorization. `IAIToolAccessEvaluator` receives the `ClaimsPrincipal` that the completion pipeline resolved, and tools the caller is not authorized to use are removed from the request and reported in a warning log entry. See [Tools](./tools) for the evaluator contract and the logging behavior.

## Scale-out with Redis Backplane


By default, SignalR keeps all connection state in-memory on a single server. In a multi-server deployment, messages sent on one server won't reach clients connected to another server.

The solution is a **Redis backplane**, which broadcasts SignalR messages across all servers:

```text
Server 1 ──┐                ┌── Server 2
            │                │
            ▼                ▼
         ┌─────────────────────┐
         │    Redis Backplane   │
         └─────────────────────┘
```

Configure the Redis connection in your environment:

```json title="appsettings.json"
{
  "Redis": {
    "Configuration": "localhost:6379,allowAdmin=true"
  }
}
```

Or via environment variables:

```bash
export Redis__Configuration="localhost:6379,allowAdmin=true"
```

:::info
When using the Aspire AppHost for local development, Redis is configured automatically as part of the orchestration. See the Aspire project at `src/Startup/CrestApps.Core.Aspire.AppHost/`.
:::

### When You Need Scale-out

- **Single server**: No backplane needed. SignalR works out of the box.
- **Multiple servers behind a load balancer**: Redis backplane required for message delivery across servers.
- **Azure App Service with multiple instances**: Enable Redis or use Azure SignalR Service.

## Services Registered by `AddCoreSignalR()`

| Service | Implementation | Lifetime | Purpose |
|---------|---------------|----------|---------|
| `HubRouteManager` | — | Singleton | Hub route registration and URL generation |

## Configuration

### Path Prefix (Multi-Tenant)

```csharp
builder.Services.AddCoreSignalR(pathPrefix: "/tenant-a");
```

All hub routes will be prefixed with the tenant path.

## Using the Hub Route Manager

### Mapping Hubs

```csharp
var app = builder.Build();

// Map hubs during endpoint routing
app.MapHub<AIChatHub>(
    app.Services.GetRequiredService<HubRouteManager>().GetPathByHub<AIChatHub>());
```

### Generating URLs

```csharp
public class MyService(HubRouteManager hubRouteManager)
{
    public string GetChatHubUrl(HttpContext httpContext)
    {
        return hubRouteManager.GetUriByHub<AIChatHub>(httpContext);
        // Returns: "https://example.com/tenant-a/Communication/Hub/AIChatHub"
    }
}
```

### Default Hub Path Pattern

```text
/Communication/Hub/{HubName}
```

With a prefix of `/tenant-a`:

```text
/tenant-a/Communication/Hub/{HubName}
```

## Key Methods

| Method | Description |
|--------|-------------|
| `GetPathByHub<T>()` | Get the route path for a hub type |
| `GetPathByRoute(pattern)` | Get the route path with prefix applied |
| `GetUriByHub<T>(httpContext)` | Full URI including scheme and host |
| `GetUriByRoute(httpContext, pattern)` | Full URI for a custom route pattern |
