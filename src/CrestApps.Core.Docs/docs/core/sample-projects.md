---
sidebar_label: Sample Projects
sidebar_position: 3
title: Sample Projects
description: Which runnable project to start with, what each sample demonstrates, and how the projects relate to each other.
---

# Sample Projects

This repository includes several runnable projects. Start with the one that matches the question you are trying to answer.

## Recommended order

1. **`CrestApps.Core.Mvc.Web`** when you want the broadest end-to-end reference host
2. **`CrestApps.Core.Blazor.Web`** when you want the same framework composition with a Blazor-first UI
3. **`CrestApps.Core.Aspire.AppHost`** when you want the composed local environment
4. **`CrestApps.Core.Mvc.Samples.A2AClient`** or **`CrestApps.Core.Mvc.Samples.McpClient`** when you only need protocol-focused samples

## Project map

| Project | Path | What it is best for |
| --- | --- | --- |
| MVC reference host | `src\Startup\CrestApps.Core.Mvc.Web` | The full framework in one place: providers, deployments, profiles, chat, documents, MCP, A2A, indexing, SignalR, and YesSql storage |
| Blazor reference host | `src\Startup\CrestApps.Core.Blazor.Web` | The same feature set in a Blazor-first host using Entity Framework Core storage |
| Aspire app host | `src\Startup\CrestApps.Core.Aspire.AppHost` | Running the MVC host, Blazor host, protocol clients, and local dependencies together |
| A2A sample client | `src\Startup\CrestApps.Core.Mvc.Samples.A2AClient` | Inspecting A2A client behavior without the full reference host |
| MCP sample client | `src\Startup\CrestApps.Core.Mvc.Samples.McpClient` | Inspecting MCP client behavior without the full reference host |

## Most readers should start here

### MVC reference host

```bash
dotnet run --project .\src\Startup\CrestApps.Core.Mvc.Web\CrestApps.Core.Mvc.Web.csproj
```

Use this project when you want the clearest picture of how the framework fits together in one ASP.NET Core application.

### Blazor reference host

```bash
dotnet run --project .\src\Startup\CrestApps.Core.Blazor.Web\CrestApps.Core.Blazor.Web.csproj
```

Use this project when you want to compare the same framework composition against a Blazor UI and Entity Framework Core persistence.

### Aspire app host

```bash
dotnet run --project .\src\Startup\CrestApps.Core.Aspire.AppHost\CrestApps.Core.Aspire.AppHost.csproj
```

Use this project when you want the local composed environment. It references the MVC host, the Blazor host, and the protocol sample clients.

## How the samples relate to the docs

- Use **[Getting Started](../getting-started.md)** for the shortest path to running a sample.
- Use **[ASP.NET Core Integration](./getting-started-aspnet.md)** for the builder-based registration model behind the sample hosts.
- Use **[MVC Example](./mvc-example.md)** when you want a deeper walkthrough of the most complete sample.
