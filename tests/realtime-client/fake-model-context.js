// A small stand-in for the browser's WebMCP ModelContext (document.modelContext), for the WebMCP unit tests.
//
// It follows the semantics of the WebMCP draft and its reference polyfill: registering a name twice throws, an
// aborted registration signal unregisters the tool, getTools() answers RegisteredTool records with the owner window
// and origin, executeTool() resolves with the tool's result serialized as JSON and rejects with a generic error when
// the tool's execute callback rejects, and every change fires a 'toolchange' event.
//
// The browser tests (webmcp-polyfill.spec.js) run the same scripts against the real polyfill.
'use strict';

const TOOL_NAME = /^[A-Za-z0-9_.-]{1,128}$/;

function createFakeModelContext(options = {}) {
    const ownerWindow = options.window || {};
    const origin = options.origin || 'https://example.com';
    const tools = new Map();
    const listeners = new Set();

    function changed() {
        listeners.forEach((listener) => listener({ type: 'toolchange' }));
    }

    return {
        tools,
        async registerTool(tool, registerOptions = {}) {
            if (!tool || typeof tool.name !== 'string' || !TOOL_NAME.test(tool.name)) {
                throw new Error('InvalidStateError: invalid tool name ' + (tool && tool.name));
            }

            if (!tool.description) {
                throw new Error('InvalidStateError: a tool description cannot be empty');
            }

            if (typeof tool.execute !== 'function') {
                throw new TypeError('execute must be a function');
            }

            if (tools.has(tool.name)) {
                throw new Error('InvalidStateError: a tool named ' + tool.name + ' is already registered');
            }

            const signal = registerOptions.signal;

            if (signal && signal.aborted) {
                throw signal.reason;
            }

            tools.set(tool.name, tool);
            changed();

            if (signal) {
                signal.addEventListener('abort', () => {
                    tools.delete(tool.name);
                    changed();
                }, { once: true });
            }
        },
        async getTools() {
            return [...tools.values()]
                .map((tool) => {
                    const registered = {
                        name: tool.name,
                        title: tool.title === undefined ? '' : String(tool.title),
                        description: tool.description,
                        origin,
                        window: ownerWindow,
                    };

                    if (tool.inputSchema !== undefined) {
                        registered.inputSchema = JSON.parse(JSON.stringify(tool.inputSchema));
                    }

                    if (tool.annotations !== undefined) {
                        registered.annotations = Object.assign({}, tool.annotations);
                    }

                    return registered;
                })
                .sort((left, right) => (left.name < right.name ? -1 : left.name > right.name ? 1 : 0));
        },
        async executeTool(registeredTool, inputObject = {}) {
            if (typeof inputObject !== 'object' || inputObject === null) {
                throw new TypeError('inputObject must be an object');
            }

            const tool = tools.get(registeredTool && registeredTool.name);

            if (!tool) {
                throw new Error('UnknownError: Tool execution failed');
            }

            let value;

            try {
                value = await tool.execute(JSON.parse(JSON.stringify(inputObject)), { signal: new AbortController().signal });
            } catch (error) {
                throw new Error('UnknownError: Tool execution failed');
            }

            const serialized = JSON.stringify(value);

            if (serialized === undefined) {
                throw new Error('UnknownError: Tool execution failed');
            }

            return serialized;
        },
        addEventListener(type, listener) {
            if (type === 'toolchange') {
                listeners.add(listener);
            }
        },
        removeEventListener(type, listener) {
            if (type === 'toolchange') {
                listeners.delete(listener);
            }
        },
    };
}

module.exports = { createFakeModelContext };
