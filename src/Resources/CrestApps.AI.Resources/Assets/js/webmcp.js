// @ts-check
/*
 * WebMCP for CrestApps.
 *
 * WebMCP lets a web page give tools to an AI agent: the page registers each tool with the browser's ModelContext
 * (`document.modelContext` in the current draft, `navigator.modelContext` in earlier browser previews), and an agent
 * in the browser calls it. This script keeps the page's own list of tools at `window.crestAppsWebMcp` and gives every
 * tool on it to the browser when the browser supports WebMCP.
 *
 * The list starts with the AI tools the site lets the signed-in user call, each a small proxy that posts to the site
 * with the user's antiforgery token. Other scripts add tools that work on the page itself with
 * `window.crestAppsWebMcp.registerTool(...)`. The CrestApps AI chat calls the page tools too, through `connectChat`,
 * so it works in browsers without WebMCP.
 *
 * The page configuration is read from a `<script type="application/json" id="crestapps-webmcp-config">` block:
 * `toolsUrl` and `invokeUrlTemplate` (with a `{name}` placeholder) for the site's tools, `antiforgeryHeaderName` and
 * `antiforgeryToken` for the calls, and any setting the page scripts need.
 */
(function () {
  'use strict';

  var CONFIG_ELEMENT_ID = 'crestapps-webmcp-config';
  var MAX_PAGE_TOOLS = 64;
  var PAGE_TOOL_NAME = /^[a-zA-Z0-9_-]{1,64}$/;

  /**
   * Builds an MCP-style text result.
   *
   * @param {any} text The text the agent reads.
   * @param {boolean} [isError] Whether the result reports a failure.
   */
  function textResult(text, isError) {
    return {
      content: [{ type: 'text', text: text == null ? '' : String(text) }],
      isError: !!isError,
    };
  }

  /**
   * Whether a tool's answer reports a failure.
   *
   * @param {any} result The answer.
   */
  function isErrorResult(result) {
    return !!(result && typeof result === 'object' && result.isError === true);
  }

  /**
   * Turns a tool's answer into text: the text parts of a result, a string as it is, and anything else as JSON.
   *
   * @param {any} result The answer.
   */
  function serializeResult(result) {
    if (result == null) {
      return '';
    }

    if (typeof result === 'string') {
      return result;
    }

    if (typeof result === 'object' && Array.isArray(result.content)) {
      return result.content
        .filter(function (part) {
          return part && part.type === 'text';
        })
        .map(function (part) {
          return part.text == null ? '' : String(part.text);
        })
        .join('\n');
    }

    try {
      return JSON.stringify(result);
    } catch (error) {
      return String(result);
    }
  }

  /**
   * Finds the browser's WebMCP object.
   *
   * @param {any} doc The document.
   * @param {any} nav The navigator.
   */
  function getModelContext(doc, nav) {
    if (doc && doc.modelContext) {
      return doc.modelContext;
    }

    if (nav && nav.modelContext) {
      return nav.modelContext;
    }

    return null;
  }

  /**
   * Creates the page's list of tools.
   *
   * @param {any} modelContext The browser's WebMCP object, or null.
   */
  function createRegistry(modelContext) {
    /** @type {Map<string, { tool: any, controller: AbortController }>} */
    var tools = new Map();
    /** @type {Set<Function>} */
    var listeners = new Set();

    function notify() {
      listeners.forEach(function (listener) {
        try {
          listener();
        } catch (error) {
          console.error('A WebMCP tool listener failed.', error);
        }
      });
    }

    function toBrowserTool(tool) {
      /** @type {any} */
      var browserTool = {
        name: tool.name,
        description: tool.description,
        inputSchema: tool.inputSchema || { type: 'object', properties: {} },
        execute: tool.execute,
      };

      if (tool.title) {
        browserTool.title = tool.title;
      }

      if (tool.annotations) {
        browserTool.annotations = tool.annotations;
      }

      return browserTool;
    }

    function release(name, entry) {
      entry.controller.abort();

      if (modelContext && typeof modelContext.unregisterTool === 'function') {
        try {
          modelContext.unregisterTool(name);
        } catch (error) {
          // The browser already let go of it.
        }
      }
    }

    function registerTool(tool) {
      if (!tool || typeof tool.name !== 'string' || !tool.name) {
        throw new TypeError('A WebMCP tool needs a name.');
      }

      if (typeof tool.description !== 'string' || !tool.description) {
        throw new TypeError('A WebMCP tool needs a description.');
      }

      if (typeof tool.execute !== 'function') {
        throw new TypeError('A WebMCP tool needs an execute function.');
      }

      var existing = tools.get(tool.name);

      if (existing) {
        release(tool.name, existing);
      }

      var controller = new AbortController();

      tools.set(tool.name, { tool: tool, controller: controller });

      if (modelContext && typeof modelContext.registerTool === 'function') {
        try {
          Promise.resolve(modelContext.registerTool(toBrowserTool(tool), { signal: controller.signal })).catch(function (error) {
            console.warn('The browser did not accept the WebMCP tool "' + tool.name + '".', error);
          });
        } catch (error) {
          console.warn('The browser did not accept the WebMCP tool "' + tool.name + '".', error);
        }
      }

      notify();
    }

    function unregisterTool(name) {
      var entry = tools.get(name);

      if (!entry) {
        return false;
      }

      tools.delete(name);
      release(name, entry);
      notify();

      return true;
    }

    function getTools() {
      /** @type {Array<any>} */
      var list = [];

      tools.forEach(function (entry) {
        var tool = entry.tool;
        /** @type {any} */
        var item = {
          name: tool.name,
          description: tool.description,
          inputSchema: tool.inputSchema || { type: 'object', properties: {} },
          source: tool.source || 'page',
        };

        if (tool.title) {
          item.title = tool.title;
        }

        if (tool.annotations) {
          item.annotations = tool.annotations;
        }

        list.push(item);
      });

      return list;
    }

    async function executeTool(name, input, options) {
      var entry = tools.get(name);

      if (!entry) {
        return textResult('The tool "' + name + '" is not on this page.', true);
      }

      var signal = options && options.signal ? options.signal : new AbortController().signal;

      return await entry.tool.execute(input || {}, { signal: signal });
    }

    function onToolsChanged(listener) {
      listeners.add(listener);

      return function () {
        listeners.delete(listener);
      };
    }

    function isSupported() {
      return !!modelContext;
    }

    return {
      registerTool: registerTool,
      unregisterTool: unregisterTool,
      getTools: getTools,
      executeTool: executeTool,
      onToolsChanged: onToolsChanged,
      isSupported: isSupported,
    };
  }

  /**
   * Creates the page tool that calls one of the site's AI tools.
   *
   * @param {{ name: string, title?: string, description: string, inputSchema?: any }} descriptor The tool the site lists.
   * @param {{ invokeUrlTemplate?: string, antiforgeryHeaderName?: string, antiforgeryToken?: string }} config The page configuration.
   * @param {Function} fetchImpl The fetch function.
   */
  function createServerToolProxy(descriptor, config, fetchImpl) {
    if (!descriptor || !descriptor.name || !config || !config.invokeUrlTemplate) {
      throw new TypeError('A site tool needs a name and an address to call.');
    }

    var url = config.invokeUrlTemplate.replace('{name}', encodeURIComponent(descriptor.name));

    return {
      name: descriptor.name,
      title: descriptor.title,
      description: descriptor.description,
      inputSchema: descriptor.inputSchema || { type: 'object', properties: {} },
      annotations: { untrustedContentHint: true },
      source: 'server',
      execute: async function (input) {
        /** @type {Record<string, string>} */
        var headers = { 'Content-Type': 'application/json' };

        if (config.antiforgeryHeaderName && config.antiforgeryToken) {
          headers[config.antiforgeryHeaderName] = config.antiforgeryToken;
        }

        try {
          var response = await fetchImpl(url, {
            method: 'POST',
            credentials: 'same-origin',
            headers: headers,
            body: JSON.stringify(input || {}),
          });

          if (!response.ok) {
            return textResult('The site refused the tool call (' + response.status + ').', true);
          }

          var data = await response.json();

          return {
            content: Array.isArray(data.content) ? data.content : [],
            isError: !!data.isError,
          };
        } catch (error) {
          return textResult('The tool call did not reach the site.', true);
        }
      },
    };
  }

  /**
   * Fetches the AI tools the site lets this user call and registers a proxy for each.
   *
   * @param {ReturnType<typeof createRegistry>} registry The page's list of tools.
   * @param {any} config The page configuration.
   * @param {Function} fetchImpl The fetch function.
   * @returns {Promise<number>} How many tools were registered.
   */
  async function loadServerTools(registry, config, fetchImpl) {
    if (!config || !config.toolsUrl) {
      return 0;
    }

    try {
      var response = await fetchImpl(config.toolsUrl, { credentials: 'same-origin', headers: { Accept: 'application/json' } });

      if (!response.ok) {
        return 0;
      }

      var data = await response.json();
      var count = 0;

      (data && Array.isArray(data.tools) ? data.tools : []).forEach(function (descriptor) {
        try {
          registry.registerTool(createServerToolProxy(descriptor, config, fetchImpl));
          count++;
        } catch (error) {
          console.warn('A site WebMCP tool could not be registered.', error);
        }
      });

      return count;
    } catch (error) {
      console.warn('The site\'s WebMCP tools could not be loaded.', error);

      return 0;
    }
  }

  /**
   * Reads the configuration block the site writes into the page.
   *
   * @param {any} doc The document.
   */
  function readConfig(doc) {
    var element = doc && doc.getElementById ? doc.getElementById(CONFIG_ELEMENT_ID) : null;

    if (!element) {
      return null;
    }

    try {
      return JSON.parse(element.textContent);
    } catch (error) {
      return null;
    }
  }

  /**
   * Turns the page's tools into what the chat hub accepts: page tools only (the chat reaches the site's tools
   * directly), with names AI providers accept, at most 64, and their schemas as JSON text.
   *
   * @param {Array<any>} tools The page's tools.
   */
  function toPageToolDescriptors(tools) {
    /** @type {Array<any>} */
    var descriptors = [];

    (tools || []).forEach(function (tool) {
      if (descriptors.length >= MAX_PAGE_TOOLS || !tool || tool.source === 'server' || !PAGE_TOOL_NAME.test(tool.name || '')) {
        return;
      }

      descriptors.push({
        name: tool.name,
        title: tool.title || null,
        description: tool.description,
        inputSchema: JSON.stringify(tool.inputSchema || { type: 'object', properties: {} }),
      });
    });

    return descriptors;
  }

  /**
   * Runs a page tool the chat asked for and answers what the hub expects back: `{ text, isError }`.
   *
   * @param {any} api The page's list of tools.
   * @param {string} name The tool's name.
   * @param {string} argumentsJson The tool's input as JSON.
   */
  async function runPageTool(api, name, argumentsJson) {
    var input;

    try {
      input = argumentsJson ? JSON.parse(argumentsJson) : {};
    } catch (error) {
      return { text: 'The tool input is not valid JSON.', isError: true };
    }

    var tool = api.getTools().find(function (candidate) {
      return candidate.name === name;
    });

    if (tool && tool.source === 'server') {
      return { text: 'The tool "' + name + '" is not a page tool.', isError: true };
    }

    try {
      var result = await api.executeTool(name, input);

      return { text: serializeResult(result), isError: isErrorResult(result) };
    } catch (error) {
      return { text: 'The tool failed: ' + (error && error.message ? error.message : String(error)), isError: true };
    }
  }

  /**
   * Connects the page's tools to a CrestApps chat hub connection. The hub calls `ExecutePageTool` on the connection
   * and waits for the result; `sync` sends the page tools before a message, only when they changed.
   *
   * @param {any} api The page's list of tools.
   * @param {any} connection The SignalR hub connection of the chat.
   */
  function connectChat(api, connection) {
    /** @type {string | null} */
    var lastSignature = null;

    connection.on('ExecutePageTool', function (name, argumentsJson) {
      return runPageTool(api, name, argumentsJson);
    });

    if (typeof connection.onreconnected === 'function') {
      // A reconnected connection has a new identifier, and the hub forgot the tools of the old one.
      connection.onreconnected(function () {
        lastSignature = null;
      });
    }

    async function sync() {
      var descriptors = toPageToolDescriptors(api.getTools());
      var signature = JSON.stringify(descriptors);

      if (signature === lastSignature) {
        return descriptors.length;
      }

      if (descriptors.length === 0 && lastSignature === null) {
        lastSignature = signature;

        return 0;
      }

      try {
        if (descriptors.length === 0) {
          await connection.invoke('ClearPageTools');
        } else {
          await connection.invoke('SetPageTools', descriptors);
        }

        lastSignature = signature;
      } catch (error) {
        console.warn('The page tools could not be sent to the chat.', error);
      }

      return descriptors.length;
    }

    return {
      sync: sync,
      reset: function () {
        lastSignature = null;
      },
    };
  }

  function init(win) {
    if (win.crestAppsWebMcp) {
      return win.crestAppsWebMcp;
    }

    var config = readConfig(win.document) || {};
    var registry = createRegistry(getModelContext(win.document, win.navigator));
    var api = Object.assign({}, registry, {
      config: config,
      textResult: textResult,
      serializeResult: serializeResult,
      isErrorResult: isErrorResult,
      connectChat: function (connection) {
        return connectChat(api, connection);
      },
    });

    win.crestAppsWebMcp = api;

    if (config.toolsUrl && typeof win.fetch === 'function') {
      loadServerTools(registry, config, win.fetch.bind(win));
    }

    if (win.document && typeof win.document.dispatchEvent === 'function' && typeof win.CustomEvent === 'function') {
      win.document.dispatchEvent(new win.CustomEvent('crestapps-webmcp-ready', { detail: api }));
    }

    return api;
  }

  window.CrestAppsWebMcp = {
    textResult: textResult,
    isErrorResult: isErrorResult,
    serializeResult: serializeResult,
    getModelContext: getModelContext,
    createRegistry: createRegistry,
    createServerToolProxy: createServerToolProxy,
    loadServerTools: loadServerTools,
    readConfig: readConfig,
    toPageToolDescriptors: toPageToolDescriptors,
    runPageTool: runPageTool,
    connectChat: connectChat,
    init: init,
  };

  if (window.document) {
    init(window);
  }
})();
