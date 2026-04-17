# CrestApps.Core Development Instructions

Use these instructions when working in this repository.

## Project overview

`CrestApps.Core` is the standalone framework repository for CrestApps shared libraries. It contains reusable .NET packages for AI, orchestration, chat, templating, document processing, SignalR, storage, and sample hosts.

- **Target framework:** .NET 10
- **Docs site:** `src\CrestApps.Core.Docs`
- **Sample hosts:** `src\Startup\CrestApps.Core.Mvc.Web`, `src\Startup\CrestApps.Core.Aspire.AppHost`
- **Tests:** `tests\CrestApps.Core.Tests`

Orchard Core-specific implementation details belong to the separate site at <https://orchardcore.crestapps.com>.

## Build and test

Use the repository root unless a command says otherwise.

```bash
dotnet build .\CrestApps.Core.slnx -c Release /p:NuGetAudit=false
dotnet test .\tests\CrestApps.Core.Tests\CrestApps.Core.Tests.csproj -c Release /p:NuGetAudit=false
```

For root asset tooling:

```bash
npm install
npm run rebuild
```

For the docs site:

```bash
cd src\CrestApps.Core.Docs
npm install
npm run build
```

## Repository layout

Important folders:

- `src\Abstractions` - public contracts
- `src\Primitives` - concrete framework features and providers
- `src\Stores` - persistence implementations
- `src\Utilities` - shared helpers
- `src\Startup` - runnable sample hosts
- `src\CrestApps.Core.Docs` - Docusaurus docs
- `tests\CrestApps.Core.Tests` - unit tests

## Documentation expectations

When a change affects public behavior, configuration, setup, or project guidance:

1. Update the relevant page under `src\CrestApps.Core.Docs\docs`
2. Update the changelog under `src\CrestApps.Core.Docs\docs\changelog`
3. Build the docs site

Keep the docs focused on `CrestApps.Core`. If you need to mention the Orchard Core implementation, treat it as a related downstream product and link to <https://orchardcore.crestapps.com>.

## Coding guidance

- Follow `.editorconfig`
- Prefer constructor injection
- Do not add `ArgumentNullException.ThrowIf...` guards in constructors
- Add null guards in public implementation methods when a non-nullable input is required and the method does not intentionally support `null`
- Skip null guards for nullable or intentionally null-tolerant parameters
- After the last null-check/argument-validation line in a method, add a blank line before the next statement
- If a method has only one null-check/argument-validation line, still add a blank line after that final guard line
- Add a blank line before a `return` statement unless the `return` is the first statement inside a `{ ... }` block
- Add a blank line before and after `if` blocks, `switch` statements, and loops unless the block is immediately preceded by `{`
- Do not add a blank line between an `if`/`else`/`switch`/loop condition and its opening `{`
- Use `var` consistently with repository style
- Only use expression-bodied members when the entire member fits on a single short line; use a full block body for anything longer or split across lines
- Avoid `DateTime.UtcNow`; prefer injected `TimeProvider`.
- Keep public docs and comments honest to the current code

## Runtime notes

Use the MVC sample host when you need to inspect end-to-end framework behavior:

```bash
dotnet run --project .\src\Startup\CrestApps.Core.Mvc.Web\CrestApps.Core.Mvc.Web.csproj
```

Use the Aspire host when you need the composed local environment:

```bash
dotnet run --project .\src\Startup\CrestApps.Core.Aspire.AppHost\CrestApps.Core.Aspire.AppHost.csproj
```
