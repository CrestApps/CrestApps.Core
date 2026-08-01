# CrestApps.Core Documentation

Documentation site for the [CrestApps.Core](https://github.com/CrestApps/CrestApps.Core) repository, built with [Docusaurus 3.9](https://docusaurus.io/).

**Live site:** [core.crestapps.com](https://core.crestapps.com)

## Local Development

```bash
cd src/CrestApps.Core.Docs
npm install
npm start
```

## Build

```bash
npm run build
```

This site contains the framework-only documentation for `CrestApps.Core`.

## Versioning

The site keeps a version selector so older releases stay available while `main`
continues to evolve. The unversioned `docs/` folder is the **Latest** version and
tracks `main`. Each released version is frozen under `versioned_docs/` and
`versioned_sidebars/`, with the list of published versions in `versions.json`.

Versions are created automatically on qualifying tag pushes (`vX.Y.0`) by the
`deploy_docs.yml` GitHub Actions workflow, which snapshots the current docs as
`X.Y` (for example, `v1.0.0` produces the `1.0` version, served under `/docs/1.0/`).
To cut a version manually:

```bash
npx docusaurus docs:version 1.0
```

Commit the generated `versioned_docs/`, `versioned_sidebars/`, and `versions.json`
so the frozen version persists across future deployments.

## Deployment

The site is deployed automatically to GitHub Pages via the `deploy_docs.yml`
workflow on every push to `main`, on `vX.Y.0` release tag pushes, and on manual
`workflow_dispatch` runs. Prerelease tags (for example `v1.0.0-rc.1`) and patch
tags (for example `v1.0.1`) are intentionally skipped.
