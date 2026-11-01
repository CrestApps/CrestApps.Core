// Browser tests for WebMCP against the reference polyfill of the W3C Web Machine Learning Community Group.
//
// webmcp-harness.html is a page that exposes tools the way any website does: a third-party script registers them
// straight with document.modelContext.registerTool, a frame on another origin shares one through exposedTo, and the
// CrestApps scripts register the site's AI tools and the page tools. The tests act as a WebMCP agent with getTools and
// executeTool, and as the CrestApps chat through connectChat, which must find and run every tool the page exposes.
const { test, expect } = require('@playwright/test');

const harnessPath = '/tests/realtime-client/webmcp-harness.html';
const partnerOrigin = 'http://localhost:4173';

async function openHarness(page, invocations) {
    await page.route('**/fake-site/api/ai/webmcp/tools', (route) => route.fulfill({
        json: {
            tools: [{
                name: 'calculator',
                title: 'Calculator',
                description: 'Adds two numbers.',
                inputSchema: { type: 'object', properties: { a: { type: 'number' }, b: { type: 'number' } } },
            }],
        },
    }));

    await page.route('**/fake-site/api/ai/webmcp/tools/calculator/invoke', async (route) => {
        const request = route.request();
        const input = request.postDataJSON();

        invocations?.push({ token: request.headers()['requestverificationtoken'], input });
        await route.fulfill({ json: { content: [{ type: 'text', text: String(input.a + input.b) }], isError: false } });
    });

    await page.goto(harnessPath);
    await page.evaluate(() => Promise.all([window.thirdPartyRegistrations, window.crestAppsWebMcp.ready]));
}

// Creates a fake SignalR connection in the page and connects the chat to it.
async function connectFakeChat(page, fromOrigins) {
    await page.evaluate((origins) => {
        const handlers = {};
        const invocations = [];

        window.fakeChat = {
            handlers,
            invocations,
            connection: {
                on: (name, handler) => { handlers[name] = handler; },
                invoke: (...args) => { invocations.push(args); return Promise.resolve(1); },
            },
        };

        window.fakeChat.bridge = origins
            ? window.CrestAppsWebMcp.connectChat(document.modelContext, window.fakeChat.connection, { fromOrigins: origins, excludedNames: new Set(['calculator']) })
            : window.crestAppsWebMcp.connectChat(window.fakeChat.connection);
    }, fromOrigins || null);
}

async function syncedTools(page) {
    return page.evaluate(async () => {
        await window.fakeChat.bridge.sync();
        const last = window.fakeChat.invocations.filter((call) => call[0] === 'SetPageTools').pop();

        return last ? last[1] : [];
    });
}

test.describe('WebMCP with the reference polyfill', () => {
    test('the page has the standard document.modelContext', async ({ page }) => {
        await openHarness(page);

        expect(await page.evaluate(() => typeof document.modelContext.registerTool === 'function'
            && typeof document.modelContext.getTools === 'function'
            && typeof document.modelContext.executeTool === 'function')).toBe(true);
        expect(await page.evaluate(() => window.crestAppsWebMcp.isSupported())).toBe(true);
    });

    test('an agent sees the third-party tools, the page tools and the site tools', async ({ page }) => {
        await openHarness(page);

        const names = await page.evaluate(async () => (await document.modelContext.getTools()).map((tool) => tool.name));

        expect(names).toEqual(expect.arrayContaining([
            'store.get_order', 'store.add_to_cart', 'store.place_order', 'store.broken',
            'page_get_info', 'page_list_links', 'page_navigate', 'page_list_form_fields', 'page_set_form_field',
            'calculator',
        ]));
    });

    test('an agent runs a site tool, which calls the site with the antiforgery token', async ({ page }) => {
        const invocations = [];

        await openHarness(page, invocations);

        const result = await page.evaluate(async () => {
            const tool = (await document.modelContext.getTools()).find((candidate) => candidate.name === 'calculator');

            return document.modelContext.executeTool(tool, { a: 2, b: 3 });
        });

        expect(result).toBe('"5"');
        expect(invocations).toEqual([{ token: 'harness-token', input: { a: 2, b: 3 } }]);
    });

    test('an agent runs a page tool on the page', async ({ page }) => {
        await openHarness(page);

        const result = await page.evaluate(async () => {
            const tools = await document.modelContext.getTools();

            await document.modelContext.executeTool(tools.find((tool) => tool.name === 'page_set_form_field'), { field: 'Order note', value: 'Leave at the door' });

            return JSON.parse(await document.modelContext.executeTool(tools.find((tool) => tool.name === 'page_get_info'), {}));
        });

        expect(result.title).toBe('WebMCP harness');
        expect(await page.inputValue('#note')).toBe('Leave at the door');
    });

    test('the chat finds every tool the page exposes, with its annotations, and leaves out the site tools', async ({ page }) => {
        await openHarness(page);
        await connectFakeChat(page);

        const tools = await syncedTools(page);
        const byName = Object.fromEntries(tools.map((tool) => [tool.name, tool]));

        expect(Object.keys(byName)).toEqual(expect.arrayContaining(['store.get_order', 'store.add_to_cart', 'store.place_order', 'store.broken', 'page_get_info']));
        expect(byName.calculator).toBeUndefined();
        expect(byName['store.get_order']).toMatchObject({ title: 'Get order', readOnly: true, consequential: false });
        expect(JSON.parse(byName['store.get_order'].inputSchema).required).toEqual(['orderId']);
        expect(byName['store.place_order']).toMatchObject({ consequential: true });
    });

    test('the chat runs a third-party tool through executeTool', async ({ page }) => {
        await openHarness(page);
        await connectFakeChat(page);

        const results = await page.evaluate(async () => ({
            add: await window.fakeChat.handlers.ExecutePageTool('store.add_to_cart', '{"product":"Lamp"}'),
            order: await window.fakeChat.handlers.ExecutePageTool('store.get_order', '{"orderId":"A-1"}'),
            broken: await window.fakeChat.handlers.ExecutePageTool('store.broken', '{}'),
            site: await window.fakeChat.handlers.ExecutePageTool('calculator', '{"a":1,"b":2}'),
        }));

        expect(results.add).toEqual({ text: 'Added Lamp.', isError: false });
        expect(JSON.parse(results.order.text)).toEqual({ orderId: 'A-1', status: 'shipped' });
        expect(results.broken.isError).toBe(true);
        expect(results.site.isError).toBe(true);
        await expect(page.locator('#cart li')).toHaveText(['Lamp']);
    });

    test('the chat picks up a tool registered after it last synced', async ({ page }) => {
        await openHarness(page);
        await connectFakeChat(page);
        await syncedTools(page);

        await page.evaluate(() => document.modelContext.registerTool({
            name: 'store.apply_coupon',
            description: 'Applies a coupon code.',
            execute: ({ code }) => ({ applied: code }),
        }));

        const tools = await syncedTools(page);

        expect(tools.map((tool) => tool.name)).toContain('store.apply_coupon');
    });

    test('the chat uses a tool a frame on another origin shares, only when told to look in that origin', async ({ page }) => {
        await openHarness(page);

        await page.evaluate(({ origin }) => new Promise((resolve) => {
            window.addEventListener('message', (event) => {
                if (event.data === 'partner-ready') {
                    resolve();
                }
            });
            document.getElementById('partner').src = origin + '/tests/realtime-client/webmcp-partner-frame.html?embedder=' + encodeURIComponent(location.origin);
        }), { origin: partnerOrigin });

        await connectFakeChat(page);
        expect((await syncedTools(page)).map((tool) => tool.name)).not.toContain('partner.track_shipment');

        await connectFakeChat(page, [partnerOrigin]);
        expect((await syncedTools(page)).map((tool) => tool.name)).toContain('partner.track_shipment');

        const result = await page.evaluate(() => window.fakeChat.handlers.ExecutePageTool('partner.track_shipment', '{"trackingNumber":"Z9"}'));

        expect(JSON.parse(result.text)).toEqual({ trackingNumber: 'Z9', location: 'Chicago' });
    });
});
