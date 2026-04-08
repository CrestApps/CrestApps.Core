---
sidebar_label: Getting Started
sidebar_position: 2
title: Getting Started
description: Build, run, and explore the standalone CrestApps.Core repository.
---

# Getting Started


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

```bash
dotnet run --project .\src\Startup\CrestApps.Core.Mvc.Web\CrestApps.Core.Mvc.Web.csproj
```

Use the MVC sample when you want to see the full framework in one place: AI providers, deployments, profiles, templates, document processing, MCP, A2A, storage, and SignalR-driven chat flows.

### Aspire host

```bash
dotnet run --project .\src\Startup\CrestApps.Core.Aspire.AppHost\CrestApps.Core.Aspire.AppHost.csproj
```

Use the Aspire host when you want to boot the MVC sample and related sample clients together.

## Learn the registration model

The recommended registration surface is the `AddCrestAppsCore(...)` builder, which groups framework features into higher-level suites:

```csharp
builder.Services.AddCrestAppsCore(crestApps => crestApps
    .AddAISuite(ai => ai
        .AddMarkdown()
        .AddChatInteractions()
        .AddDocumentProcessing()
        .AddOpenAI()));
```

Under the hood, each builder step still maps to the corresponding `AddCrestApps...` `IServiceCollection` extension, so hosts can still opt into the lower-level registration methods when they want that control.

- Start with **[Core overview](core/index.md)** to understand the package layout
- Use **[ASP.NET Core integration](core/getting-started-aspnet.md)** to wire the same services into MVC, Razor Pages, Blazor, Minimal APIs, or MAUI hybrid hosts
- Follow **[MVC example](core/mvc-example.md)** for a complete working composition

## Build the docs site

```bash
cd src/CrestApps.Core.Docs
npm install
npm run build
```

## Package feed

Preview packages are published to:

`https://nuget.cloudsmith.io/crestapps/crestapps-core/v3/index.json`
