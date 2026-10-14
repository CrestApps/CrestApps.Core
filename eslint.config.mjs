// Lints the browser scripts every project builds from its Assets folder, and the build scripts. Run with `npm run lint`.
import js from "@eslint/js";
import globals from "globals";

// Globals a page defines before the chat scripts run, which the scripts read as bare names.
const pageGlobals = {
    Vue: "readonly",           // Vue 3, which renders the chat, chat interaction and widget UIs.
    signalR: "readonly",       // @microsoft/signalr, loaded ahead of the chat hub connections.
    marked: "readonly",        // marked, which renders the assistant's Markdown.
    DOMPurify: "readonly",     // DOMPurify, which sanitizes the rendered Markdown.
    hljs: "readonly",          // highlight.js, which highlights code blocks.
    Chart: "readonly",         // Chart.js, which draws the charts an answer contains.
    mediumZoom: "readonly",    // medium-zoom, which zooms generated images on the Chat Interaction page.
    confirmDialog: "readonly", // Orchard Core's admin confirmation dialog, used when a host provides one.
};

// `catch (error) { /* best effort */ }` is how the scripts say a failure is deliberately ignored; the name is not a bug.
const sharedRules = {
    "no-unused-vars": ["error", { caughtErrors: "none" }],
};

export default [
    {
        ignores: ["**/node_modules/**", "**/wwwroot/**", "**/bin/**", "**/obj/**", "src/CrestApps.Core.Docs/**"],
    },
    {
        files: ["src/*/*/Assets/**/*.js"],
        languageOptions: {
            ecmaVersion: "latest",
            // The build bundles each source as a module, so it is linted as one: strict, with its own scope.
            sourceType: "module",
            globals: {
                ...globals.browser,
                ...pageGlobals,
            },
        },
        rules: { ...js.configs.recommended.rules, ...sharedRules },
    },
    {
        files: [".scripts/**/*.mjs", "eslint.config.mjs"],
        languageOptions: {
            ecmaVersion: "latest",
            sourceType: "module",
            globals: globals.node,
        },
        rules: { ...js.configs.recommended.rules, ...sharedRules },
    },
];
