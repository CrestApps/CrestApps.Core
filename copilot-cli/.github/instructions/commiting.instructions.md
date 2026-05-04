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

## Local Development Guidelines

**When working locally (CLI use only), never commit changes directly.**  

1. **Keep changes local**  
   - All experimental or temporary modifications must remain on your machine.  
   - Do not merge or push to the shared repository.

2. **Use local configuration overrides**  
   - Store environment-specific settings in `appsettings.Development.json` or environment variables.  
   - Avoid editing shared configuration files.

3. **Isolate experiments**  
   - Test code in separate modules, branches, or projects.  
   - Avoid breaking the main solution or CI/CD pipelines.

4. **Cleanup after local testing**  
   - Revert temporary code changes before switching branches.  
   - Remove unused assets, build outputs, or temporary files.

5. **Document local changes**  
   - Maintain a local log of experimental changes (`LOCAL-DEV-CHANGES.md`) if necessary.  
   - Never commit this file to the repo.

6. **Offline testing**  
   - Focus on asset builds, static analysis, and unit tests that do not require external network dependencies.  
   - Document any network-dependent features for later testing.

---

## Coding Standards and Conventions

- Follow .editorconfig for C# naming and formatting rules
- Use async/await, dependency injection, ILogger for logging
- Seal classes by default except for ViewModels used by Orchard Core display drivers
- Avoid static mutable state, hardcoded secrets, synchronous I/O, and `DateTime.UtcNow`

---

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
