---
sidebar_label: Getting Started
sidebar_position: 2
title: Getting Started
description: Build, run, and explore the standalone CrestApps.Core repository.
---

# Getting Started

## Fastest path to a working AI experience

If you want the least-effort path, use this sequence:

1. Register `AddCrestAppsCore(...).AddAISuite(...)`
2. Add one provider plus `AddChatInteractions()`
3. Configure `CrestApps:AI:Connections` and `CrestApps:AI:Deployments`
4. Create an AI profile and chat against it through Chat Interactions

Chat Interactions are the easiest playground-style UI for validating that your connection, deployment, prompts, and profile wiring are all correct before you build a custom experience.


## Prerequisites

- .NET 10 SDK
- Node.js 20+ for the documentation site
- At least one AI provider credential or a local Ollama instance when you want to run the sample app with live AI features

## Clone the repository

```bash
git clone https://github.com/CrestApps/CrestApps.Core.git
cd CrestApps.Core
```

## Build and test

```bash
dotnet build .\CrestApps.Core.slnx -c Release /p:NuGetAudit=false
dotnet test .\tests\CrestApps.Core.Tests\CrestApps.Core.Tests.csproj -c Release /p:NuGetAudit=false
```

## Run the sample hosts

### MVC sample application

**Visual Studio:** Set `CrestApps.Core.Mvc.Web` as the startup project and press **F5** (or **Ctrl+F5** to run without debugging).

**Command line:**

```bash
dotnet run --project .\src\Startup\CrestApps.Core.Mvc.Web\CrestApps.Core.Mvc.Web.csproj
```

The sample host resolves its content root to the MVC project directory automatically, so this command works correctly when run from the repository root.

Use the MVC sample when you want to see the full framework in one place: AI providers, deployments, profiles, templates, document processing, MCP, A2A, storage, and SignalR-driven chat flows.

### Aspire host

The Aspire host boots the MVC and Blazor sample hosts together with the shared A2A and MCP client samples as a composed local environment. The client samples include a server selector so you can switch between the MVC and Blazor endpoints without launching separate client projects.

:::info Prerequisites
Aspire manages containers for services like Redis. You need a container runtime such as [Docker Desktop](https://www.docker.com/products/docker-desktop/) installed and running before starting the Aspire host.
:::

**Visual Studio:** Set `CrestApps.Core.Aspire.AppHost` as the startup project and press **F5**.

**Command line:**

```bash
dotnet run --project .\src\Startup\CrestApps.Core.Aspire.AppHost\CrestApps.Core.Aspire.AppHost.csproj
```

## Smallest useful app integration

Use the `AddCrestAppsCore(...)` builder as the main entry point:

```csharp
builder.Services.AddCrestAppsCore(crestApps => crestApps
    .AddAISuite(ai => ai
        .AddOpenAI()
    )
);
```

Then add the first interactive feature:

```csharp
builder.Services.AddCrestAppsCore(crestApps => crestApps
    .AddAISuite(ai => ai
        .AddOpenAI()
        .AddChatInteractions()
    )
);
```

By default:

- connections are read from `CrestApps:AI:Connections`
- deployments are read from `CrestApps:AI:Deployments`

```json
{
  "CrestApps": {
    "AI": {
      "Connections": [
        {
          "Name": "primary-openai",
          "ClientName": "OpenAI",
          "ApiKey": "YOUR_API_KEY"
        }
      ],
      "Deployments": [
        {
          "Name": "gpt-4.1",
          "ConnectionName": "primary-openai",
          "ModelName": "gpt-4.1",
          "Type": "Chat"
        },
        {
          "Name": "standalone-utility",
          "ClientName": "OpenAI",
          "ModelName": "gpt-4.1-mini",
          "Type": "Utility",
          "ApiKey": "YOUR_OTHER_API_KEY"
        }
      ]
    }
  }
}
```

Use `ConnectionName` when a deployment should point at a shared entry from `CrestApps:AI:Connections`. Keep contained connection settings directly on the deployment only when you want a standalone deployment definition.

Create an AI profile that uses your chat deployment, then use Chat Interactions to test it end to end.

## Complete Hello World example

Here is a minimal, self-contained `Program.cs` that sends a chat completion request using CrestApps.Core with OpenAI:

```csharp
using CrestApps.Core;
using CrestApps.Core.AI;
using CrestApps.Core.AI.Models;
using CrestApps.Core.AI.Completions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

// Register CrestApps with the OpenAI provider.
builder.Services.AddCrestAppsCore(crestApps => crestApps
    .AddAISuite(ai => ai
        .AddOpenAI()
    )
);

var app = builder.Build();

// Resolve the completion service from DI.
using var scope = app.Services.CreateScope();
var deploymentManager = scope.ServiceProvider.GetRequiredService<IAIDeploymentManager>();
var completionService = scope.ServiceProvider.GetRequiredService<IAICompletionService>();

// Resolve the first chat deployment.
var deployment = await deploymentManager.FindFirstByTypeAsync(AIDeploymentType.Chat);

if (deployment is null)
{
    Console.WriteLine("No chat deployment found. Check your CrestApps:AI:Deployments configuration.");
    return;
}

// Send a completion request.
var messages = new List<ChatMessage>
{
    new(ChatRole.User, "What is CrestApps.Core in one sentence?"),
};
var context = new AICompletionContext();

var response = await completionService.CompleteAsync(deployment, messages, context);
Console.WriteLine(response.Message?.Text ?? "No response.");
```

Add the matching `appsettings.json`:

```json
{
  "CrestApps": {
    "AI": {
      "Connections": [
        {
          "Name": "my-openai",
          "ClientName": "OpenAI",
          "ApiKey": "sk-YOUR_API_KEY_HERE"
        }
      ],
      "Deployments": [
        {
          "Name": "gpt-4.1-mini",
          "ConnectionName": "my-openai",
          "ModelName": "gpt-4.1-mini",
          "Type": "Chat"
        }
      ]
    }
  }
}
```

## Learn the registration model

Under the hood, each builder step still maps to the corresponding `AddCrestApps...` `IServiceCollection` extension, so hosts can still opt into the lower-level registration methods when they want that control.

- Start with **[Core overview](core/index.md)** to understand the package layout
- Use **[ASP.NET Core integration](core/getting-started-aspnet.md)** to wire the same services into MVC, Razor Pages, Blazor, Minimal APIs, or MAUI hybrid hosts
- Follow **[MVC example](core/mvc-example.md)** for a complete working composition

## Build the docs site

```bash
cd src\CrestApps.Core.Docs
npm install
npm run build
```

## Package feed

Preview packages are published to:

`https://nuget.cloudsmith.io/crestapps/crestapps-core/v3/index.json`
