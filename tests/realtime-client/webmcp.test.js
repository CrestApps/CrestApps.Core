// Unit tests for webmcp.js, against a stand-in that follows the WebMCP draft (see fake-model-context.js).
//
// The script uses only the standard document.modelContext API: it registers the site's AI tools with registerTool,
// and lets the CrestApps chat discover every tool the page exposes with getTools and run it with executeTool. The
// browser test webmcp-polyfill.spec.js runs the same script against the reference polyfill.
//
// Run with: node --test tests/realtime-client/webmcp.test.js
const { describe, it, mock } = require('node:test');
const assert = require('node:assert');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { createFakeModelContext } = require('./fake-model-context.js');

const assetsDirectory = path.join(__dirname, '../../src/Resources/CrestApps.AI.WebMcp.Resources/Assets/js');

globalThis.window = {};
vm.runInThisContext(fs.readFileSync(path.join(assetsDirectory, 'webmcp.js'), 'utf8'), { filename: 'webmcp.js' });

const webMcp = globalThis.window.CrestAppsWebMcp;

const serverConfig = {
    toolsUrl: '/api/ai/webmcp/tools',
    invokeUrlTemplate: '/api/ai/webmcp/tools/{name}/invoke',
    antiforgeryHeaderName: 'RequestVerificationToken',
    antiforgeryToken: 'token-1',
};

function jsonResponse(body, status = 200) {
    return {
        ok: status >= 200 && status < 300,
        status,
        json: () => Promise.resolve(body),
    };
}

function siteTool(name, extra) {
    return Object.assign({
        name,
        title: 'Title ' + name,
        description: 'Does ' + name + '.',
        inputSchema: { type: 'object', properties: { text: { type: 'string' } } },
        execute: (input) => ({ ran: name, text: input.text }),
    }, extra);
}

describe('getModelContext', () => {
    it('answers the document\'s ModelContext, the only place the WebMCP draft defines it', () => {
        const modelContext = {};

        assert.strictEqual(webMcp.getModelContext({ modelContext }), modelContext);
    });

    it('answers null when the page has no WebMCP, for example outside a secure context', () => {
        assert.strictEqual(webMcp.getModelContext({}), null);
        assert.strictEqual(webMcp.getModelContext(undefined), null);
    });
});

describe('readConfig', () => {
    it('reads the configuration block the site writes', () => {
        const doc = {
            getElementById: (id) => (id === 'crestapps-webmcp-config' ? { textContent: '{"toolsUrl":"/t","isAdmin":true}' } : null),
        };

        assert.deepStrictEqual(webMcp.readConfig(doc), { toolsUrl: '/t', isAdmin: true });
    });

    it('answers null when the block is missing or broken', () => {
        assert.strictEqual(webMcp.readConfig({ getElementById: () => null }), null);
        assert.strictEqual(webMcp.readConfig({ getElementById: () => ({ textContent: '{broken' }) }), null);
    });
});

describe('createServerTool', () => {
    const descriptor = {
        name: 'calculator',
        title: 'Calculator',
        description: 'Adds numbers.',
        inputSchema: { type: 'object', properties: { a: { type: 'number' } } },
    };

    it('describes the site tool as a standard ModelContextTool', () => {
        const tool = webMcp.createServerTool(descriptor, serverConfig, mock.fn());

        assert.strictEqual(tool.name, 'calculator');
        assert.strictEqual(tool.title, 'Calculator');
        assert.strictEqual(tool.description, 'Adds numbers.');
        assert.deepStrictEqual(tool.inputSchema, descriptor.inputSchema);
        assert.deepStrictEqual(tool.annotations, { untrustedContentHint: true });
        assert.strictEqual(typeof tool.execute, 'function');
    });

    it('posts the input with the antiforgery token and answers the tool\'s text', async () => {
        const fetchImpl = mock.fn(() => Promise.resolve(jsonResponse({ content: [{ type: 'text', text: '42' }], isError: false })));
        const tool = webMcp.createServerTool(descriptor, serverConfig, fetchImpl);

        assert.strictEqual(await tool.execute({ a: 1 }), '42');
        assert.deepStrictEqual(fetchImpl.mock.calls[0].arguments, ['/api/ai/webmcp/tools/calculator/invoke', {
            method: 'POST',
            credentials: 'same-origin',
            headers: { 'Content-Type': 'application/json', RequestVerificationToken: 'token-1' },
            body: '{"a":1}',
        }]);
    });

    it('rejects, as the draft expects of a failed tool, when the site refuses the call, the tool fails or the site cannot be reached', async () => {
        const refused = webMcp.createServerTool(descriptor, serverConfig, () => Promise.resolve(jsonResponse({}, 403)));
        const failed = webMcp.createServerTool(descriptor, serverConfig, () => Promise.resolve(jsonResponse({ content: [{ type: 'text', text: 'Boom.' }], isError: true })));
        const offline = webMcp.createServerTool(descriptor, serverConfig, () => Promise.reject(new Error('offline')));

        await assert.rejects(refused.execute({}), /403/);
        await assert.rejects(failed.execute({}), /Boom\./);
        await assert.rejects(offline.execute({}));
    });

    it('refuses a tool without a name or an address to call', () => {
        assert.throws(() => webMcp.createServerTool({ description: 'x' }, serverConfig, mock.fn()), TypeError);
        assert.throws(() => webMcp.createServerTool(descriptor, {}, mock.fn()), TypeError);
    });
});

describe('registerServerTools', () => {
    it('registers every tool the site lists with the page\'s ModelContext, where any agent can find and run it', async () => {
        const modelContext = createFakeModelContext();
        const fetchImpl = mock.fn((url) => Promise.resolve(url === '/api/ai/webmcp/tools'
            ? jsonResponse({ tools: [{ name: 'calculator', description: 'Adds.' }, { description: 'No name.' }, { name: 'weather', description: 'Weather.' }] })
            : jsonResponse({ content: [{ type: 'text', text: 'ok' }], isError: false })));
        const warn = mock.method(console, 'warn', () => {});

        const names = await webMcp.registerServerTools(modelContext, serverConfig, fetchImpl);

        warn.mock.restore();
        assert.deepStrictEqual(names, ['calculator', 'weather']);
        assert.deepStrictEqual((await modelContext.getTools()).map((tool) => tool.name), ['calculator', 'weather']);

        const calculator = (await modelContext.getTools()).find((tool) => tool.name === 'calculator');

        assert.strictEqual(await modelContext.executeTool(calculator, {}), '"ok"');
    });

    it('unregisters the tools when its signal is aborted', async () => {
        const modelContext = createFakeModelContext();
        const controller = new AbortController();
        const fetchImpl = () => Promise.resolve(jsonResponse({ tools: [{ name: 'calculator', description: 'Adds.' }] }));

        await webMcp.registerServerTools(modelContext, serverConfig, fetchImpl, controller.signal);
        controller.abort();

        assert.deepStrictEqual(await modelContext.getTools(), []);
    });

    it('registers nothing when the site refuses the list or there is no address', async () => {
        const modelContext = createFakeModelContext();

        assert.deepStrictEqual(await webMcp.registerServerTools(modelContext, serverConfig, () => Promise.resolve(jsonResponse({}, 401))), []);
        assert.deepStrictEqual(await webMcp.registerServerTools(modelContext, {}, mock.fn()), []);
        assert.deepStrictEqual(await modelContext.getTools(), []);
    });
});

describe('toPageToolDescriptors', () => {
    it('turns registered tools into what the chat hub accepts, with their annotations and their schemas as text', () => {
        const descriptors = webMcp.toPageToolDescriptors([
            { name: 'shop.get_order', title: 'Get order', description: 'Gets an order.', inputSchema: { type: 'object' }, annotations: { readOnlyHint: true, untrustedContentHint: true } },
            { name: 'shop.place_order', title: '', description: 'Places an order.', annotations: { consequentialHint: true } },
            { name: 'calculator', title: 'Calculator', description: 'Site tool.' },
            { name: 'bad name', title: '', description: 'Not a WebMCP name.' },
        ], new Set(['calculator']));

        assert.deepStrictEqual(descriptors, [
            { name: 'shop.get_order', title: 'Get order', description: 'Gets an order.', inputSchema: '{"type":"object"}', readOnly: true, consequential: false, untrustedContent: true },
            { name: 'shop.place_order', title: null, description: 'Places an order.', inputSchema: null, readOnly: false, consequential: true, untrustedContent: false },
        ]);
    });
});

describe('runPageTool', () => {
    it('finds the tool with getTools, runs it with executeTool and answers the text the model reads', async () => {
        const modelContext = createFakeModelContext();

        await modelContext.registerTool(siteTool('shop.get_order'));

        assert.deepStrictEqual(await webMcp.runPageTool(modelContext, 'shop.get_order', '{"text":"A-1"}'), { text: '{"ran":"shop.get_order","text":"A-1"}', isError: false });
    });

    it('answers a string result as its text', async () => {
        const modelContext = createFakeModelContext();

        await modelContext.registerTool(siteTool('greet', { execute: () => 'Hello.' }));

        assert.deepStrictEqual(await webMcp.runPageTool(modelContext, 'greet', '{}'), { text: 'Hello.', isError: false });
    });

    it('reports a failed, missing or excluded tool, and input that is not a JSON object', async () => {
        const modelContext = createFakeModelContext();

        await modelContext.registerTool(siteTool('fails', { execute: () => { throw new Error('Boom'); } }));
        await modelContext.registerTool(siteTool('calculator'));

        assert.strictEqual((await webMcp.runPageTool(modelContext, 'fails', '{}')).isError, true);
        assert.strictEqual((await webMcp.runPageTool(modelContext, 'missing', '{}')).isError, true);
        assert.deepStrictEqual(await webMcp.runPageTool(modelContext, 'fails', '{broken'), { text: 'The tool input is not valid JSON.', isError: true });
        assert.deepStrictEqual(await webMcp.runPageTool(modelContext, 'fails', '[1]'), { text: 'The tool input must be a JSON object.', isError: true });
        assert.strictEqual((await webMcp.runPageTool(modelContext, 'calculator', '{}', { excludedNames: new Set(['calculator']) })).isError, true);
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

    function calls(connection) {
        return connection.invoke.mock.calls.map((call) => call.arguments);
    }

    it('sends every tool the page exposes, including ones a third-party script registered directly', async () => {
        const modelContext = createFakeModelContext();
        const connection = fakeConnection();
        const chat = webMcp.connectChat(modelContext, connection);

        await modelContext.registerTool(siteTool('shop.get_order', { annotations: { readOnlyHint: true } }));

        assert.strictEqual(await chat.sync(), 1);
        assert.deepStrictEqual(calls(connection), [['SetPageTools', [{
            name: 'shop.get_order',
            title: 'Title shop.get_order',
            description: 'Does shop.get_order.',
            inputSchema: '{"type":"object","properties":{"text":{"type":"string"}}}',
            readOnly: true,
            consequential: false,
            untrustedContent: false,
        }]]]);
    });

    it('leaves out the site tools it registered itself, which the chat reaches on the server', async () => {
        const modelContext = createFakeModelContext();
        const connection = fakeConnection();
        const chat = webMcp.connectChat(modelContext, connection, { excludedNames: new Set(['calculator']) });

        await modelContext.registerTool(siteTool('calculator'));
        await chat.sync();

        assert.strictEqual(connection.invoke.mock.callCount(), 0);
    });

    it('sends nothing again until the tools change, then sends the new set or clears it', async () => {
        const modelContext = createFakeModelContext();
        const connection = fakeConnection();
        const chat = webMcp.connectChat(modelContext, connection);
        const registration = new AbortController();

        await chat.sync();
        await modelContext.registerTool(siteTool('one'), { signal: registration.signal });
        await chat.sync();
        await chat.sync();
        registration.abort();
        await chat.sync();

        assert.deepStrictEqual(calls(connection).map((call) => call[0]), ['SetPageTools', 'ClearPageTools']);
    });

    it('sends the tools again after the connection comes back', async () => {
        const modelContext = createFakeModelContext();
        const connection = fakeConnection();
        const chat = webMcp.connectChat(modelContext, connection);

        await modelContext.registerTool(siteTool('one'));
        await chat.sync();
        connection.reconnect();
        await chat.sync();

        assert.strictEqual(connection.invoke.mock.callCount(), 2);
    });

    it('tries again on the next message when sending fails', async () => {
        const modelContext = createFakeModelContext();
        const connection = fakeConnection();
        const chat = webMcp.connectChat(modelContext, connection);
        const warn = mock.method(console, 'warn', () => {});

        connection.invoke.mock.mockImplementationOnce(() => Promise.reject(new Error('Down.')));
        await modelContext.registerTool(siteTool('one'));
        await chat.sync();
        await chat.sync();
        warn.mock.restore();

        assert.strictEqual(connection.invoke.mock.callCount(), 2);
    });

    it('runs the tool the hub asks for through executeTool and returns what the hub expects', async () => {
        const modelContext = createFakeModelContext();
        const connection = fakeConnection();

        webMcp.connectChat(modelContext, connection);
        await modelContext.registerTool(siteTool('shop.get_order'));

        assert.deepStrictEqual(await connection.handlers.ExecutePageTool('shop.get_order', '{"text":"A-1"}'), {
            text: '{"ran":"shop.get_order","text":"A-1"}',
            isError: false,
        });
    });

    it('does nothing when the page has no WebMCP', async () => {
        const connection = fakeConnection();
        const chat = webMcp.connectChat(null, connection);

        assert.strictEqual(await chat.sync(), 0);
        assert.strictEqual(connection.handlers.ExecutePageTool, undefined);
    });
});

describe('init', () => {
    it('creates the page facade once, registers the site tools and connects chats to the page\'s ModelContext', async () => {
        const modelContext = createFakeModelContext();
        const events = [];
        const win = {
            document: {
                modelContext,
                getElementById: () => ({ textContent: JSON.stringify(Object.assign({ isAdmin: true }, serverConfig)) }),
                dispatchEvent: (event) => events.push(event.type),
            },
            fetch: () => Promise.resolve(jsonResponse({ tools: [{ name: 'calculator', description: 'Adds.' }] })),
            CustomEvent: function (type, init) {
                this.type = type;
                this.detail = init.detail;
            },
        };

        const api = webMcp.init(win);

        assert.strictEqual(webMcp.init(win), api);
        assert.strictEqual(win.crestAppsWebMcp, api);
        assert.strictEqual(api.isSupported(), true);
        assert.strictEqual(api.config.isAdmin, true);
        await api.ready;
        assert.deepStrictEqual((await modelContext.getTools()).map((tool) => tool.name), ['calculator']);
        assert.deepStrictEqual(events, ['crestapps-webmcp-ready']);

        const connection = { invoke: mock.fn(() => Promise.resolve()), on: mock.fn() };
        const chat = api.connectChat(connection);

        await modelContext.registerTool(siteTool('shop.get_order'));
        await chat.sync();

        assert.deepStrictEqual(connection.invoke.mock.calls[0].arguments[1].map((tool) => tool.name), ['shop.get_order']);
    });
});
