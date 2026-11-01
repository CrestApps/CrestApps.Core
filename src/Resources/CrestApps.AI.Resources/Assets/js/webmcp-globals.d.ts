// Globals the WebMCP scripts share with the page, for the type check (`npm run check`). Nothing here is built.
interface Window {
    // The page's list of WebMCP tools, which webmcp.js creates and the page tools and chat use.
    crestAppsWebMcp: any;

    // The testable helpers of webmcp.js.
    CrestAppsWebMcp: any;

    // The testable helpers of webmcp-page-tools.js.
    CrestAppsWebMcpPageTools: any;
}
