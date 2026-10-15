---
sidebar_label: Building Front-End Assets
sidebar_position: 3
title: Building Front-End Assets
description: How the chat UI's JavaScript and CSS are built from source, the commands to run, the Assets.json format, and how to add a script or stylesheet.
---

# Building Front-End Assets

The chat UI's scripts and stylesheets are written under `src/Resources/CrestApps.AI.Resources/Assets/` and built
into its `wwwroot/` folder, which the sample hosts serve as `/_content/CrestApps.AI.Resources/...` and the
`@crestapps/ai-chat-ui` npm package publishes. The built files are committed, so you only need this page when you
change one of those scripts or stylesheets, or work on this repository.

:::tip[Using the packages?]
If you reference the NuGet packages or install `@crestapps/ai-chat-ui`, the built scripts and stylesheets are already
inside them. There is nothing to build.
:::

## Requirements

- **Node.js 24.** The version is pinned in `.node-version` at the repository root, which version managers such as
  fnm read. The build warns when another major version runs it, and fails in CI.
- **npm 11**, which ships with Node.js 24. Use npm: the lockfile is npm's, and other package managers would not honor
  it.

## Install

Run this once after cloning, and again whenever a `package.json` changes:

```bash
npm install
```

The repository root is an npm workspace. A project that ships a third-party library declares it in its own
`package.json`, and one install at the root installs them all into one lockfile, `package-lock.json`.

## Build

| Command | What it does |
| --- | --- |
| `npm run build` | Builds the asset groups whose sources changed since their last build. |
| `npm run rebuild` | Builds every asset group. |
| `npm run watch` | Builds, then rebuilds a group whenever one of its sources changes. Copies are not watched. |
| `npm run dry-run` | Lists the selected groups and the files each one reads and writes, without building. |
| `npm run clean` | Deletes the selected groups' outputs and the build cache. |

Every command accepts filters after `--`. A group is selected when it matches any of them:

```bash
# One or more groups, by name
npm run build -- -n ai-chat.js
npm run build -- -n ai-chat.js,ai-chat.css

# By tag. Every group is tagged with its project's name without the "CrestApps." prefix
npm run build -- -t AI.Resources
```

`npm run build` decides what to build from the contents of the files, not their timestamps: a group is rebuilt when
its definition, one of its sources, or one of its outputs changed. Switching branches therefore rebuilds only what
differs.

:::warning[Commit the built files]
Commit the `wwwroot` files a build changes together with the sources that changed them. The build only writes a file
when its contents change, so an unchanged output never shows up as modified. A pull request runs `npm run rebuild`
and fails when the committed `wwwroot` does not match what the sources build.
:::

## Lint and type-check

| Command | What it does |
| --- | --- |
| `npm run lint` | Lints every source under a project's `Assets/` folder, and the build scripts, with ESLint. |
| `npm run check` | Type-checks, with TypeScript, the sources whose first line is `// @ts-check`. It writes nothing. |

A pull request runs both before it rebuilds the assets, and fails on any error. Two habits keep the lint clean:

- A script that reads a library the page loads first, such as `marked` or `DOMPurify`, needs it listed in
  `pageGlobals` in `eslint.config.mjs`.
- A failure ignored on purpose is written `catch (error) { /* best effort */ }`; an empty block is reported.

Add `// @ts-check` as the first line of a source once `npm run check` passes with it, so the type checks keep
covering it.

## How a project declares its assets

A project lists its assets in an `Assets.json` file next to its `.csproj`. The file holds an array of asset groups:

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

| Key | Meaning |
| --- | --- |
| `action` | `vite` builds the sources into a script or a stylesheet. `copy` copies files unchanged. |
| `name` | The group's name. It must be unique across the repository; `-n` selects groups by it. |
| `source` | A path, or an ordered list of paths, relative to the project folder. |
| `dest` | For `vite`, the readable output file. For `copy`, a folder ending in `/`, or a single file path to copy one source to under another name. |
| `tags` | Optional. Extra tags for `-t`, in addition to the project's name. |
| `global` | Optional, for a `vite` script built from one package. Exposes the package's exports as `window[global]`. |
| `sourceMap` | Optional, for a `vite` script. Also writes a `.map` file next to the readable and the minified script. |

A `vite` group writes two files: the readable `dest` file and a minified sibling, for example `ai-chat.js` and
`ai-chat.min.js`.

### Scripts

Scripts are built with [Vite](https://vite.dev/) into a classic script that a page loads with a plain `<script>`
tag. It is not an ES module. The output targets current browsers and is minified for the `.min.js` file.

A group with several sources runs them in the order they are listed, so **list a helper before the script that uses
it**.

Each source is built as its own module, so a top-level `var` or function stays inside the bundle. To publish an API
to the page, assign it to `window` explicitly, the way the chat scripts do:

```js
window.CoreAIChatMarkers = (function () {
    'use strict';

    // ...

    return {
        expandImageMarkers: expandImageMarkers,
    };
})();
```

### Stylesheets

Groups whose `dest` ends in `.css` compile their `.scss` and `.css` sources with Dart Sass and write a readable
`.css` file and a `.min.css` file minified with Lightning CSS. Stylesheets have no source maps.

### Third-party libraries

Add the package to the `package.json` of the project that ships it, from the repository root. Add it as a
development dependency: that `package.json` is also the published `@crestapps/ai-chat-ui` package, which ships the
built files rather than asking its consumers to install the library:

```bash
npm install -D some-library@1.2.3 -w @crestapps/ai-chat-ui
```

Then copy the files the package already builds with a `copy` group, or bundle a package that ships only ES modules
or CommonJS with a `vite` group that sets `global`. A `source` path that starts with `node_modules/` is found whether
npm installed the package in the project or at the repository root.

## Add an asset

1. Write the source under the project's `Assets/` folder, for example `Assets/js/my-widget.js`.
2. Add a group to the project's `Assets.json`. Create the file next to the `.csproj` if the project has none yet;
   the build finds every `Assets.json` under `src/Primitives` and `src/Resources`.
3. Build it: `npm run build -- -n my-widget.js`.
4. Reference the built file from a view, for example
   `<script src="~/_content/CrestApps.AI.Resources/scripts/my-widget.js" asp-append-version="true"></script>`.
5. Commit the source, the `Assets.json` change and the built `wwwroot` files.

## Troubleshooting

| Problem | What to do |
| --- | --- |
| `'node_modules/...' was not found in any node_modules folder` | Run `npm install` at the repository root. |
| The build warns that another Node.js version is running | Switch to Node.js 24, the version in `.node-version`. |
| A script works in the readable file but a global is missing on the page | The script published its API through a top-level `var` or function. Assign it to `window` instead. |
| The pull request fails "Test building assets" | Run `npm run rebuild` and commit the `wwwroot` changes it makes. |
| `npm run build` skips a group you expected to build | Run `npm run rebuild -- -n <group name>`, or `npm run clean` to clear the build cache. |
