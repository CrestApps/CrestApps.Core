---
name: Publish a release
about: Publish a new CrestApps.Core release
title: 'Release v'
labels: release
assignees: ''

---
Use this checklist for a framework release such as `v1.0.0`.

### Prepare the release

- [ ] Confirm the milestone and release issue are up to date.
- [ ] Make sure `Directory.Build.props` has the intended `VersionPrefix` and `VersionSuffix` values.
- [ ] Update the docs changelog under `src/CrestApps.Core.Docs/docs/changelog/`.
- [ ] Review README, package metadata, and release-facing docs for version-specific text.

### Validate the release

- [ ] Run the full build in Release mode.
- [ ] Run the test suite.
- [ ] Build the docs site.
- [ ] Smoke test the MVC sample host if the release changes runtime behavior.

### Publish the release

- [ ] Tag the release as `v<version>`.
- [ ] Let `release_ci.yml` publish the NuGet packages from the tag.
- [ ] Create the GitHub release and link to the docs release notes.

### After publishing

- [ ] Add or update the next changelog entry under `docs/changelog/`.
- [ ] Move the next development cycle back to preview if needed.
