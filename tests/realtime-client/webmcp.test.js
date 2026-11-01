// Unit tests for webmcp.js: the page's list of WebMCP tools, how it mirrors them to the browser's ModelContext,
// the proxies that call the site's AI tools, and the bridge that lets the CrestApps chat run page tools.
//
// The script is loaded into this test's own realm with a bare `window`, so the tests see plain objects and arrays.
//
// Run with: node --test tests/realtime-client/webmcp.test.js
const { describe, it, mock } = require('node:test');
const assert = require('node:assert');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const assetsDirectory = path.join(__dirname, '../../src/Resources/CrestApps.AI.Resources/Assets/js');

globalThis.window = {};
vm.runInThisContext(fs.readFileSync(path.join(assetsDirectory, 'webmcp.js'), 'utf8'), { filename: 'webmcp.js' });

const webMcp = globalThis.window.CrestAppsWebMcp;

function echoTool(name, extra) {
    return Object.assign({
        name,
        description: 'Echoes its input.',
        inputSchema: { type: 'object', properties: { text: { type: 'string' } } },
        execute: (input) => webMcp.textResult(input.text),
    }, extra);
}

function fakeModelContext() {
    const registered = [];

    return {
        registered,
        registerTool: mock.fn((tool, options) => {
            registered.push({ tool, options });

            return Promise.resolve();
        }),
    };
}

function jsonResponse(body, status = 200) {
    return {
        ok: status >= 200 && status < 300,
        status,
        json: () => Promise.resolve(body),
    };
}

describe('getModelContext', () => {
    it('prefers the document, as the current draft defines it', () => {
        const documentContext = {};

        assert.strictEqual(webMcp.getModelContext({ modelContext: documentContext }, { modelContext: {} }), documentContext);
    });

    it('falls back to the navigator, as earlier browser previews define it', () => {
        const navigatorContext = {};

        assert.strictEqual(webMcp.getModelContext({}, { modelContext: navigatorContext }), navigatorContext);
    });

    it('answers null when the browser has no WebMCP', () => {
        assert.strictEqual(webMcp.getModelContext({}, {}), null);
        assert.strictEqual(webMcp.getModelContext(undefined, undefined), null);
    });
});

describe('createRegistry', () => {
    it('registers a tool and lists it without its callback', () => {
        const registry = webMcp.createRegistry(null);

        registry.registerTool(echoTool('echo', { title: 'Echo', annotations: { readOnlyHint: true } }));

        assert.deepStrictEqual(registry.getTools(), [{
            name: 'echo',
            description: 'Echoes its input.',
            inputSchema: { type: 'object', properties: { text: { type: 'string' } } },
            source: 'page',
            title: 'Echo',
            annotations: { readOnlyHint: true },
        }]);
    });

    it('refuses a tool without a name, a description or a callback', () => {
        const registry = webMcp.createRegistry(null);

        assert.throws(() => registry.registerTool({ description: 'x', execute: () => null }), TypeError);
        assert.throws(() => registry.registerTool({ name: 'x', execute: () => null }), TypeError);
        assert.throws(() => registry.registerTool({ name: 'x', description: 'x' }), TypeError);
    });

    it('mirrors a tool to the browser and unregisters it there with its signal', () => {
        const modelContext = fakeModelContext();
        const registry = webMcp.createRegistry(modelContext);

        registry.registerTool(echoTool('echo'));
        registry.unregisterTool('echo');

        assert.strictEqual(modelContext.registered.length, 1);
        assert.strictEqual(modelContext.registered[0].tool.name, 'echo');
        assert.strictEqual(typeof modelContext.registered[0].tool.execute, 'function');
        assert.strictEqual(modelContext.registered[0].options.signal.aborted, true);
        assert.deepStrictEqual(registry.getTools(), []);
    });

    it('calls unregisterTool on browsers that offer it', () => {
        const modelContext = fakeModelContext();

        modelContext.unregisterTool = mock.fn();

        const registry = webMcp.createRegistry(modelContext);

        registry.registerTool(echoTool('echo'));
        registry.unregisterTool('echo');

        assert.deepStrictEqual(modelContext.unregisterTool.mock.calls.map((call) => call.arguments), [['echo']]);
    });

    it('keeps the tool on the page when the browser refuses it', () => {
        const warn = mock.method(console, 'warn', () => {});
        const registry = webMcp.createRegistry({
            registerTool: () => {
                throw new Error('Not allowed.');
            },
        });

        registry.registerTool(echoTool('echo'));
        warn.mock.restore();

        assert.deepStrictEqual(registry.getTools().map((tool) => tool.name), ['echo']);
    });

    it('replaces a tool registered again under the same name', () => {
        const modelContext = fakeModelContext();
        const registry = webMcp.createRegistry(modelContext);

        registry.registerTool(echoTool('echo'));
        registry.registerTool(echoTool('echo', { description: 'Second.' }));

        assert.strictEqual(registry.getTools().length, 1);
        assert.strictEqual(registry.getTools()[0].description, 'Second.');
        assert.strictEqual(modelContext.registered[0].options.signal.aborted, true);
        assert.strictEqual(modelContext.registered[1].options.signal.aborted, false);
    });

    it('executes a tool by name with its input', async () => {
        const registry = webMcp.createRegistry(null);

        registry.registerTool(echoTool('echo'));

        assert.deepStrictEqual(await registry.executeTool('echo', { text: 'hello' }), webMcp.textResult('hello'));
    });

    it('answers an error result for a tool that does not exist', async () => {
        const registry = webMcp.createRegistry(null);

        assert.strictEqual(webMcp.isErrorResult(await registry.executeTool('missing', {})), true);
    });

    it('tells listeners when the tools change, until they stop listening', () => {
        const registry = webMcp.createRegistry(null);
        const listener = mock.fn();
        const stop = registry.onToolsChanged(listener);

        registry.registerTool(echoTool('echo'));
        registry.unregisterTool('echo');
        stop();
        registry.registerTool(echoTool('other'));

        assert.strictEqual(listener.mock.callCount(), 2);
    });

    it('says whether the browser supports WebMCP', () => {
        assert.strictEqual(webMcp.createRegistry(null).isSupported(), false);
        assert.strictEqual(webMcp.createRegistry(fakeModelContext()).isSupported(), true);
    });
});

describe('createServerToolProxy', () => {
    const config = {
        invokeUrlTemplate: '/api/ai/webmcp/tools/{name}/invoke',
        antiforgeryHeaderName: 'RequestVerificationToken',
        antiforgeryToken: 'token-1',
    };

    const descriptor = {
        name: 'list_time_zones',
        title: 'List System Time Zones',
        description: 'Lists time zones.',
        inputSchema: { type: 'object', properties: {} },
    };

    it('describes the server tool and marks it as a server tool', () => {
        const proxy = webMcp.createServerToolProxy(descriptor, config, mock.fn());

        assert.strictEqual(proxy.name, 'list_time_zones');
        assert.strictEqual(proxy.title, 'List System Time Zones');
        assert.strictEqual(proxy.source, 'server');
        assert.deepStrictEqual(proxy.annotations, { untrustedContentHint: true });
    });

    it('posts the input with the antiforgery token and answers the server result', async () => {
        const fetchImpl = mock.fn(() => Promise.resolve(jsonResponse({ content: [{ type: 'text', text: 'UTC' }], isError: false })));
        const proxy = webMcp.createServerToolProxy(descriptor, config, fetchImpl);

        const result = await proxy.execute({ filter: 'u' });

        assert.deepStrictEqual(fetchImpl.mock.calls[0].arguments, ['/api/ai/webmcp/tools/list_time_zones/invoke', {
            method: 'POST',
            credentials: 'same-origin',
            headers: {
                'Content-Type': 'application/json',
                RequestVerificationToken: 'token-1',
            },
            body: JSON.stringify({ filter: 'u' }),
        }]);
        assert.deepStrictEqual(result, { content: [{ type: 'text', text: 'UTC' }], isError: false });
    });

    it('encodes the tool name in the address', async () => {
        const fetchImpl = mock.fn(() => Promise.resolve(jsonResponse({ content: [], isError: false })));
        const proxy = webMcp.createServerToolProxy(Object.assign({}, descriptor, { name: 'a b' }), config, fetchImpl);

        await proxy.execute({});

        assert.strictEqual(fetchImpl.mock.calls[0].arguments[0], '/api/ai/webmcp/tools/a%20b/invoke');
    });

    it('answers an error result when the server refuses the call or cannot be reached', async () => {
        const refused = webMcp.createServerToolProxy(descriptor, config, () => Promise.resolve(jsonResponse({}, 403)));
        const offline = webMcp.createServerToolProxy(descriptor, config, () => Promise.reject(new Error('offline')));

        const refusedResult = await refused.execute({});
        const offlineResult = await offline.execute({});

        assert.strictEqual(webMcp.isErrorResult(refusedResult), true);
        assert.match(webMcp.serializeResult(refusedResult), /403/);
        assert.strictEqual(webMcp.isErrorResult(offlineResult), true);
    });

    it('refuses a tool without a name or an address to call', () => {
        assert.throws(() => webMcp.createServerToolProxy({ description: 'x' }, config, mock.fn()), TypeError);
        assert.throws(() => webMcp.createServerToolProxy(descriptor, {}, mock.fn()), TypeError);
    });
});

describe('loadServerTools', () => {
    const config = { toolsUrl: '/api/ai/webmcp/tools', invokeUrlTemplate: '/api/ai/webmcp/tools/{name}/invoke' };

    it('registers a proxy for every tool the server lists, skipping one it cannot call', async () => {
        const warn = mock.method(console, 'warn', () => {});
        const registry = webMcp.createRegistry(null);
        const fetchImpl = mock.fn(() => Promise.resolve(jsonResponse({
            tools: [
                { name: 'one', description: 'One.' },
                { description: 'No name.' },
                { name: 'two', description: 'Two.' },
            ],
        })));

        const count = await webMcp.loadServerTools(registry, config, fetchImpl);

        warn.mock.restore();
        assert.strictEqual(count, 2);
        assert.deepStrictEqual(fetchImpl.mock.calls[0].arguments, ['/api/ai/webmcp/tools', { credentials: 'same-origin', headers: { Accept: 'application/json' } }]);
        assert.deepStrictEqual(registry.getTools().map((tool) => tool.name), ['one', 'two']);
        assert.ok(registry.getTools().every((tool) => tool.source === 'server'));
    });

    it('registers nothing when the server refuses the list or there is no address', async () => {
        const registry = webMcp.createRegistry(null);
        const fetchImpl = mock.fn();

        assert.strictEqual(await webMcp.loadServerTools(registry, config, () => Promise.resolve(jsonResponse({}, 401))), 0);
        assert.strictEqual(await webMcp.loadServerTools(registry, {}, fetchImpl), 0);
        assert.strictEqual(fetchImpl.mock.callCount(), 0);
        assert.deepStrictEqual(registry.getTools(), []);
    });
});

describe('readConfig', () => {
    it('reads the configuration block the site writes', () => {
        const doc = {
            getElementById: (id) => id === 'crestapps-webmcp-config'
                ? { textContent: '{"toolsUrl":"/api/ai/webmcp/tools","isAdmin":true}' }
                : null,
        };

        assert.deepStrictEqual(webMcp.readConfig(doc), { toolsUrl: '/api/ai/webmcp/tools', isAdmin: true });
    });

    it('answers null when the block is missing or broken', () => {
        assert.strictEqual(webMcp.readConfig({ getElementById: () => null }), null);
        assert.strictEqual(webMcp.readConfig({ getElementById: () => ({ textContent: '{broken' }) }), null);
    });
});

describe('results', () => {
    it('serializes results to their text, strings as they are and other values as JSON', () => {
        assert.strictEqual(webMcp.serializeResult(webMcp.textResult('one')), 'one');
        assert.strictEqual(webMcp.serializeResult({ content: [{ type: 'text', text: 'a' }, { type: 'image' }, { type: 'text', text: 'b' }] }), 'a\nb');
        assert.strictEqual(webMcp.serializeResult('plain'), 'plain');
        assert.strictEqual(webMcp.serializeResult({ count: 2 }), '{"count":2}');
        assert.strictEqual(webMcp.serializeResult(undefined), '');
        assert.strictEqual(webMcp.serializeResult(null), '');
    });

    it('recognizes error results', () => {
        assert.strictEqual(webMcp.isErrorResult(webMcp.textResult('x', true)), true);
        assert.strictEqual(webMcp.isErrorResult(webMcp.textResult('x')), false);
        assert.strictEqual(webMcp.isErrorResult('x'), false);
    });
});

describe('toPageToolDescriptors', () => {
    it('keeps page tools with names AI providers accept and sends their schemas as text', () => {
        const descriptors = webMcp.toPageToolDescriptors([
            { name: 'page_get_info', title: 'Info', description: 'Info.', inputSchema: { type: 'object' }, source: 'page' },
            { name: 'list_time_zones', description: 'Server.', inputSchema: { type: 'object' }, source: 'server' },
            { name: 'has space', description: 'Bad name.', source: 'page' },
            { name: 'x'.repeat(65), description: 'Too long.', source: 'page' },
            { name: 'no_schema', description: 'No schema.', source: 'page' },
        ]);

        assert.deepStrictEqual(descriptors, [
            { name: 'page_get_info', title: 'Info', description: 'Info.', inputSchema: '{"type":"object"}' },
            { name: 'no_schema', title: null, description: 'No schema.', inputSchema: '{"type":"object","properties":{}}' },
        ]);
    });

    it('sends at most sixty-four tools', () => {
        const tools = [];

        for (let i = 0; i < 70; i++) {
            tools.push({ name: 'tool_' + i, description: 'Tool.', source: 'page' });
        }

        assert.strictEqual(webMcp.toPageToolDescriptors(tools).length, 64);
    });
});

describe('connectChat', () => {
    function fakeConnection() {
        const handlers = {};
        const reconnected = [];

        return {
            handlers,
            invoke: mock.fn(() => Promise.resolve(1)),
            on: (name, handler) => {
                handlers[name] = handler;
            },
            onreconnected: (handler) => reconnected.push(handler),
            reconnect: () => reconnected.forEach((handler) => handler()),
        };
    }

    function setup() {
        const api = webMcp.createRegistry(null);
        const connection = fakeConnection();
        const chat = webMcp.connectChat(api, connection);

        return { api, connection, chat };
    }

    function pageTool(name, execute) {
        return {
            name,
            title: 'Title ' + name,
            description: 'Does ' + name + '.',
            inputSchema: { type: 'object', properties: { text: { type: 'string' } } },
            execute: execute || ((input) => webMcp.textResult('ran ' + name + ' with ' + input.text)),
        };
    }

    it('sends the page tools before a message', async () => {
        const { api, connection, chat } = setup();

        api.registerTool(pageTool('page_one'));

        assert.strictEqual(await chat.sync(), 1);
        assert.deepStrictEqual(connection.invoke.mock.calls.map((call) => call.arguments), [['SetPageTools', [{
            name: 'page_one',
            title: 'Title page_one',
            description: 'Does page_one.',
            inputSchema: '{"type":"object","properties":{"text":{"type":"string"}}}',
        }]]]);
    });

    it('sends nothing again until the tools change', async () => {
        const { api, connection, chat } = setup();

        api.registerTool(pageTool('page_one'));
        await chat.sync();
        await chat.sync();

        assert.strictEqual(connection.invoke.mock.callCount(), 1);

        api.registerTool(pageTool('page_two'));
        await chat.sync();

        assert.strictEqual(connection.invoke.mock.callCount(), 2);
    });

    it('sends nothing for a page that never had page tools, and clears them once they are gone', async () => {
        const { api, connection, chat } = setup();

        await chat.sync();

        assert.strictEqual(connection.invoke.mock.callCount(), 0);

        api.registerTool(pageTool('page_one'));
        await chat.sync();
        api.unregisterTool('page_one');
        await chat.sync();

        assert.deepStrictEqual(connection.invoke.mock.calls.map((call) => call.arguments[0]), ['SetPageTools', 'ClearPageTools']);
    });

    it('leaves the server tools out', async () => {
        const { api, connection, chat } = setup();

        api.registerTool(Object.assign(pageTool('list_time_zones'), { source: 'server' }));
        await chat.sync();

        assert.strictEqual(connection.invoke.mock.callCount(), 0);
    });

    it('sends the tools again after the connection comes back', async () => {
        const { api, connection, chat } = setup();

        api.registerTool(pageTool('page_one'));
        await chat.sync();
        connection.reconnect();
        await chat.sync();

        assert.strictEqual(connection.invoke.mock.callCount(), 2);
    });

    it('tries again on the next message when sending fails', async () => {
        const warn = mock.method(console, 'warn', () => {});
        const { api, connection, chat } = setup();

        connection.invoke.mock.mockImplementationOnce(() => Promise.reject(new Error('Down.')));
        api.registerTool(pageTool('page_one'));
        await chat.sync();
        await chat.sync();
        warn.mock.restore();

        assert.strictEqual(connection.invoke.mock.callCount(), 2);
    });

    it('runs a tool the chat asks for and returns its text', async () => {
        const { api, connection } = setup();

        api.registerTool(pageTool('page_one'));

        assert.deepStrictEqual(await connection.handlers.ExecutePageTool('page_one', '{"text":"hi"}'), { text: 'ran page_one with hi', isError: false });
    });

    it('returns an error when the tool fails, is missing, gets broken input or is a server tool', async () => {
        const { api, connection } = setup();
        const serverExecute = mock.fn();

        api.registerTool(pageTool('page_fails', () => {
            throw new Error('Boom');
        }));
        api.registerTool(Object.assign(pageTool('list_time_zones', serverExecute), { source: 'server' }));

        assert.deepStrictEqual(await connection.handlers.ExecutePageTool('page_fails', '{}'), { text: 'The tool failed: Boom', isError: true });
        assert.strictEqual((await connection.handlers.ExecutePageTool('page_missing', '{}')).isError, true);
        assert.deepStrictEqual(await connection.handlers.ExecutePageTool('page_fails', '{broken'), { text: 'The tool input is not valid JSON.', isError: true });
        assert.deepStrictEqual(await connection.handlers.ExecutePageTool('list_time_zones', '{}'), { text: 'The tool "list_time_zones" is not a page tool.', isError: true });
        assert.strictEqual(serverExecute.mock.callCount(), 0);
    });
});

describe('init', () => {
    it('creates the page list once, reads the configuration and connects chats to it', () => {
        const events = [];
        const win = {
            document: {
                getElementById: () => ({ textContent: '{"isAdmin":true}' }),
                dispatchEvent: (event) => events.push(event.type),
            },
            navigator: {},
            CustomEvent: function (type, init) {
                this.type = type;
                this.detail = init.detail;
            },
        };

        const api = webMcp.init(win);

        assert.strictEqual(webMcp.init(win), api);
        assert.strictEqual(win.crestAppsWebMcp, api);
        assert.deepStrictEqual(api.config, { isAdmin: true });
        assert.strictEqual(api.isSupported(), false);
        assert.deepStrictEqual(events, ['crestapps-webmcp-ready']);

        const handlers = {};
        const chat = api.connectChat({ on: (name, handler) => { handlers[name] = handler; }, invoke: () => Promise.resolve() });

        assert.strictEqual(typeof chat.sync, 'function');
        assert.strictEqual(typeof handlers.ExecutePageTool, 'function');
    });
});
