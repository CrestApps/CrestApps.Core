// Globals the WebMCP scripts share with the page, for the type check (`npm run check`). Nothing here is built.
interface Window {
    // The page facade webmcp.js creates: the configuration, and connectChat for the CrestApps chat.
    crestAppsWebMcp: any;

    // The testable helpers of webmcp.js.
    CrestAppsWebMcp: any;

    // The testable helpers of webmcp-page-tools.js.
    CrestAppsWebMcpPageTools: any;
}

interface Document {
    // The WebMCP ModelContext (https://webmachinelearning.github.io/webmcp/), native or from webmcp-polyfill.js.
    readonly modelContext: any;
}
