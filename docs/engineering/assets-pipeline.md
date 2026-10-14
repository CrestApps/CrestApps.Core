# Front-end asset pipeline

Every project that ships JavaScript or CSS declares it in an `Assets.json` next to its `.csproj`. A small CLI in
`.scripts/assets/` reads those files and builds each asset group with **Vite**, the only bundler, into the project's
`wwwroot/`. The built files are committed: `dotnet build`, the sample hosts, the `@crestapps/ai-chat-ui` npm package
and the Playwright harness all use them as they are.

Today one project has assets: `src/Resources/CrestApps.AI.Resources`.

```bash
npm install          # once, and whenever a package.json changes
npm run build        # build the groups whose sources changed
npm run rebuild      # build every group
npm run watch        # rebuild a group when one of its sources changes
npm run dry-run      # list groups and the files each one writes
npm run clean        # delete outputs and the build cache
```

Filters work with every command: `npm run build -- -n ai-chat.js` (group names, comma-separated) and
`npm run build -- -t AI.Resources` (tags; every group is tagged with its project's name without the `CrestApps.`
prefix).

## Requirements

- **Node.js 24**, pinned in `.node-version`. The Node major decides the npm major, and npm majors disagree about the
  lockfile, so CI fails on a mismatch and a local build warns. Every workflow reads `.node-version`, except
  `create_docs_version_pr.yml`, which checks out release tags that predate it.
- **npm workspaces.** The root `package.json` lists `src/Resources/*` as workspaces, so one `npm install` at the root
  installs every project's packages into one lockfile. There are no per-project lockfiles. The docs site
  (`src/CrestApps.Core.Docs`) is not a workspace; it keeps its own lockfile and workflows.

## Where dependencies go

| File | Holds |
| --- | --- |
| Root `package.json` | Build and test tooling only: Vite, Sass, glob and Playwright. |
| A project's `package.json` | The libraries that project ships in its `wwwroot`, as `devDependencies`. `CrestApps.AI.Resources` ships none; its `package.json` is also the `@crestapps/ai-chat-ui` npm package, so a library in `dependencies` would be installed by every consumer of the package. |

Add a library to the project that copies or bundles it (`npm install -D <pkg> -w <workspace name>`). Pin a version
that should not float exactly.

## Assets.json

`Assets.json` is an array of asset groups. The shape follows Orchard Core's assets manager, with two actions.

| Key | Meaning |
| --- | --- |
| `action` | `vite` builds; `copy` copies files unchanged. |
| `name` | Unique across the repository; used by `-n`. |
| `source` | A path, or an ordered array of paths, relative to the project. A path that starts with `node_modules/` is found the way Node finds packages, walking up from the project, so it works whether npm hoisted the package to the root or kept it in the project. |
| `dest` | `vite`: the readable output file; the build also writes its `.min` sibling. `copy`: a folder ending in `/` (each file keeps its name), or one file path for a single source (which renames it). |
| `tags` | Optional; used by `-t`. |
| `global` | `vite` script groups with one source only: bundle that package and expose its exports as `window[global]`. |
| `sourceMap` | `vite` script groups only: also write `<file>.map` next to the readable and the minified script. |

```json
[
  {
    "action": "vite",
    "name": "ai-chat.js",
    "source": "Assets/js/ai-chat.js",
    "dest": "wwwroot/scripts/ai-chat.js",
    "sourceMap": true
  },
  {
    "action": "vite",
    "name": "ai-chat.css",
    "source": "Assets/css/ai-chat.css",
    "dest": "wwwroot/styles/ai-chat.css"
  }
]
```

### What a `vite` group produces

- **Scripts** are bundled into a classic IIFE script, not an ES module, because the views load them with a plain
  `<script>` tag. The readable file carries a "generated" banner; the `.min.js` is minified with Oxc. The target is
  Vite's modern-browser baseline.
- **Several sources** become one entry that imports them in the listed order, so they run in that order, as the
  concatenated files used to. The sources do not import one another; they share state through namespaces on
  `window`, so **a helper must be listed before the script that uses it**.
- **Each source is its own module**, so a top-level `var` or function no longer leaks onto `window`. A script that
  publishes an API assigns it explicitly: `window.CoreAIChatMarkers = (function () { ... })();`.
- **Source maps** (`sourceMap: true`) are written as `<file>.js.map` and `<file>.min.js.map`, with the sources'
  contents embedded and paths relative to the script. The npm package publishes them.
- **Stylesheets** (`.css`/`.scss`) are compiled with Dart Sass and minified with Lightning CSS. Vite writes no source
  maps for stylesheets in a production build, so a stylesheet group cannot ask for one.
- Outputs are written with LF line endings, and only when their contents change.

### Incremental builds

`npm run build` keeps a record per group in `node_modules/.cache/crestapps-assets/`: the hash of the group's
definition and the build tooling, of every module that went into its last bundle, and of every file it wrote. A
group is rebuilt when any of those change, or when an output was edited or deleted. Content hashes, not timestamps,
so switching branches or cloning fresh rebuilds what actually differs. `npm run rebuild` ignores the cache.

## CI

`assets_validation.yml` runs `npm ci` and `npm run rebuild`, renormalizes line endings, and fails if git reports
any change: the committed `wwwroot` must be what the sources build. Fix a failure by running `npm run rebuild`
locally and committing the result. `publish_npm.yml` rebuilds the same way before it publishes
`@crestapps/ai-chat-ui`. `pr_ci.yml` and `main_ci.yml` run the client unit tests (`npm run test:unit`), which read the
sources under `Assets/`, not the built files.

## Compared with the Gulp pipeline it replaces

The repository used Orchard Core's former Gulp pipeline: Babel (preset-env, ES5 output), gulp-concat, Terser and
gulp-sourcemaps, driven by `generateSourceMaps`/`inputs`/`output` manifest keys. This pipeline is the one
[CrestApps.OrchardCore](https://github.com/CrestApps/CrestApps.OrchardCore) uses, with the manifest glob, module tag
prefix and source maps adapted to this repository:

- **One bundler.** Vite covers bundling, Sass, minification, source maps and vendor IIFE builds. `copy` is the only
  other action.
- **npm, not Yarn.** npm workspaces give one lockfile and one install without adding corepack and a second package
  manager.
- **Babel helpers are gone.** The old ES5 output declared helpers such as `_slicedToArray` at the top level of every
  script, so they leaked onto `window`; so did `clampWidgetValue` in `ai-chat-widget.js`. The globals each script
  publishes on purpose are unchanged.

## Next steps

1. Lint and type checks: ESLint (flat config) over `Assets/**/*.js` and `tsc --checkJs` for files that opt in with
   `// @ts-check`, both run in CI before the rebuild.
2. Convert multi-file bundles, if any appear, to real ES modules that `import`/`export` their helpers. Output stays
   IIFE.
