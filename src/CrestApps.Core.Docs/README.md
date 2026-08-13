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

Versions are proposed automatically on qualifying tag pushes (`vX.Y.0`) by the
`create_docs_version_pr.yml` GitHub Actions workflow, which opens a pull request
that snapshots the current docs as `X.Y` (for example, `v1.0.0` produces the `1.0`
version, served under `/docs/1.0/`). Patch tags and prerelease tags are not
versioned automatically. The workflow can also be run manually with a `vX.Y.0`,
`X.Y.0`, or `X.Y` input. If branch protection requires PR checks, configure a
`DOCS_VERSION_PR_TOKEN` repository secret backed by a GitHub App token or
fine-grained personal access token with contents and pull-request write access so
the generated branch and PR can trigger the normal validation workflows; otherwise
the workflow falls back to `GITHUB_TOKEN`.
To cut a version manually:

```bash
npx docusaurus docs:version 1.0
```

Commit the generated `versioned_docs/`, `versioned_sidebars/`, and `versions.json`
so the frozen version persists across future deployments.

## Deployment

The site is deployed automatically to GitHub Pages via the `deploy_docs.yml`
workflow on every push to `main` and on manual `workflow_dispatch` runs. Release
tags create documentation-version pull requests instead of deploying directly, so
branch and environment protection rules stay enforced.
