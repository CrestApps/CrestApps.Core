# CrestApps.Core Copilot CLI Instructions

Use these instructions when working on `CrestApps.Core` with Copilot CLI.

## Project overview

`CrestApps.Core` is the standalone framework repository for CrestApps shared .NET libraries. It contains the reusable AI, orchestration, chat, storage, templating, SignalR, and protocol packages plus sample hosts and the docs site.

- **Target framework:** .NET 10
- **Docs project:** `src\CrestApps.Core.Docs`
- **Tests:** `tests\CrestApps.Core.Tests`
- **Sample hosts:** `src\Startup\CrestApps.Core.Mvc.Web`, `src\Startup\CrestApps.Core.Aspire.AppHost`

For Orchard Core-specific implementation guidance, use <https://orchardcore.crestapps.com>.

## Build commands

```bash
dotnet build .\CrestApps.Core.slnx -c Release /p:NuGetAudit=false
dotnet test .\tests\CrestApps.Core.Tests\CrestApps.Core.Tests.csproj -c Release /p:NuGetAudit=false
```

Root asset tooling:

```bash
npm install
npm run rebuild
```

Docs site:

```bash
cd src\CrestApps.Core.Docs
npm install
npm run build
```

## Working rules

- Keep changes focused on `CrestApps.Core`
- Update `src\CrestApps.Core.Docs` when public behavior or setup changes
- Add or update changelog entries for release-worthy changes
- Prefer repository tools and existing patterns over one-off scripts
- Do not describe Orchard Core module behavior as if it were part of this repository

## Common paths

- `src\Abstractions`
- `src\Primitives`
- `src\Stores`
- `src\Utilities`
- `src\Startup`
- `src\CrestApps.Core.Docs`
- `tests\CrestApps.Core.Tests`
