# Contributing to CrestApps.Core

Thanks for contributing to `CrestApps.Core`. This repository contains the shared CrestApps framework libraries, sample hosts, and the documentation site.

## Getting started

```bash
git clone https://github.com/CrestApps/CrestApps.Core.git
cd CrestApps.Core
git checkout main
```

Install:

- .NET 10 SDK
- Node.js 20+ for the docs site and asset tooling

Common commands:

```bash
dotnet build .\CrestApps.Core.slnx -c Release /p:NuGetAudit=false
dotnet test .\tests\CrestApps.Core.Tests\CrestApps.Core.Tests.csproj -c Release /p:NuGetAudit=false
cd src\CrestApps.Core.Docs
npm install
npm run build
```

## What to work on

- Browse [open issues](https://github.com/CrestApps/CrestApps.Core/issues)
- For larger features or behavioral changes, open an issue before you start

## Pull request expectations

- Keep changes focused
- Add or update tests when behavior changes
- Update the Docusaurus docs in `src\CrestApps.Core.Docs` when user-facing behavior or project guidance changes
- Include a changelog update when the change is release-note worthy
- Link the PR to the related issue when applicable
- Mark the PR as draft if it is not ready for review

## Review workflow

- Address review feedback in follow-up commits
- Do not manually resolve review conversations
- Re-request review after feedback is addressed

## Related documentation

- Core framework docs: <https://core.crestapps.com>
- Orchard Core implementation docs: <https://orchardcore.crestapps.com>
