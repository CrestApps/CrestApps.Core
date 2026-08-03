# Agent guidance

This file orients automated agents and contributors working in the `CrestApps.Core`
repository. The detailed conventions live in
[`.github/copilot-instructions.md`](.github/copilot-instructions.md); read that file
first and follow it for build, test, coding, and documentation rules.

## Project overview

`CrestApps.Core` is the standalone framework repository for the CrestApps shared
libraries (AI, orchestration, chat, templating, document processing, SignalR,
storage, and sample hosts), targeting .NET 10.

## Build and test

```bash
dotnet build .\CrestApps.Core.slnx -c Release /p:NuGetAudit=false
dotnet test .\tests\CrestApps.Core.Tests\CrestApps.Core.Tests.csproj -c Release /p:NuGetAudit=false
```

## Documentation and changelog discipline

When a change affects public behavior, configuration, setup, or project guidance,
update the relevant docs under `src\CrestApps.Core.Docs\docs` and build the docs site.

- Changelog files are named after their version with no `v` prefix (for example
  `1.0.0.md`, `1.1.0.md`).
- The next planned release is **1.1.0**. Document all upcoming changes going forward —
  new features, fixes, dependency upgrades, branching or workflow changes, and other
  notable repository-level updates — in
  `src\CrestApps.Core.Docs\docs\changelog\1.1.0.md` until it ships.
- When a new development cycle starts, add a changelog file for that version (again
  with no `v` prefix), register it in `src\CrestApps.Core.Docs\sidebars.js` and
  `src\CrestApps.Core.Docs\docs\changelog\index.md`, and document ongoing work there.

Keep the docs focused on `CrestApps.Core`. Treat the Orchard Core implementation as a
related downstream product and link to <https://orchardcore.crestapps.com>.
