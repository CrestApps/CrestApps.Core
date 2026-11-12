// Unit tests for webmcp-page-tools.js: the WebMCP tools that work on the page the user sees.
//
// The tools only touch a small part of the DOM, so each test hands them a small fake document instead of a browser.
// As WebMCP defines, a tool answers a JSON-serializable value and reports a failure by rejecting.
//
// Run with: node --test tests/realtime-client/webmcp-page-tools.test.js
const { describe, it, mock } = require('node:test');
const assert = require('node:assert');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const { createFakeModelContext } = require('./fake-model-context.js');

const assetsDirectory = path.join(__dirname, '../../src/Resources/CrestApps.AI.WebMcp.Resources/Assets/js');

globalThis.window = {};
vm.runInThisContext(fs.readFileSync(path.join(assetsDirectory, 'webmcp-page-tools.js'), 'utf8'), { filename: 'webmcp-page-tools.js' });

const pageTools = globalThis.window.CrestAppsWebMcpPageTools;
const selectors = pageTools.selectors;

function element(tagName, props) {
    const attributes = Object.assign({}, props && props.attributes);
    const events = [];

    return Object.assign({
        tagName: tagName.toUpperCase(),
        textContent: '',
        type: '',
        name: '',
        id: '',
        value: '',
        checked: false,
        disabled: false,
        required: false,
        labels: [],
        options: [],
        events,
        getAttribute: (name) => (name in attributes ? attributes[name] : null),
        dispatchEvent: (event) => {
            events.push(event.type);

            return true;
        },
    }, props);
}

function fakeDocument({ title = 'Page', description = null, headings = [], links = [], fields = [], main = null, body = null } = {}) {
    const meta = description === null ? null : element('meta', { attributes: { content: description } });

    return {
        title,
        body,
        querySelector: (selector) => {
            switch (selector) {
                case selectors.description:
                    return meta;
                case selectors.main:
                    return main;
                default:
                    return null;
            }
        },
        querySelectorAll: (selector) => {
            switch (selector) {
                case selectors.headings:
                    return headings;
                case selectors.links:
                    return links;
                case selectors.fields:
                    return fields;
                default:
                    return [];
            }
        },
    };
}

const location = {
    href: 'https://example.com/Admin/Contents',
    origin: 'https://example.com',
    assign: mock.fn(),
};

function tools(doc, isAdmin = true) {
    const byName = {};

    pageTools.createPageTools({ doc, loc: location, isAdmin }).forEach((tool) => {
        byName[tool.name] = tool;
    });

    return byName;
}

async function run(tool, input) {
    return { data: await tool.execute(input || {}, { signal: new AbortController().signal }) };
}

async function rejects(tool, input) {
    await assert.rejects(Promise.resolve().then(() => tool.execute(input || {}, { signal: new AbortController().signal })));
}

function formFields() {
    return {
        title: element('input', { type: 'text', name: 'TitlePart.Title', id: 'TitlePart_Title', value: 'Hello', required: true, labels: [{ textContent: 'Title' }] }),
        body: element('textarea', { name: 'BodyPart.Body', id: 'BodyPart_Body', value: 'x'.repeat(3000), attributes: { 'aria-label': 'Body' } }),
        published: element('input', { type: 'checkbox', name: 'Published', checked: false, labels: [{ textContent: 'Published' }] }),
        status: element('select', {
            name: 'Status',
            value: 'draft',
            labels: [{ textContent: 'Status' }],
            options: [{ value: 'draft', text: 'Draft', selected: true }, { value: 'live', text: 'Live', selected: false }],
        }),
        token: element('input', { type: 'hidden', name: '__RequestVerificationToken', value: 'secret' }),
        password: element('input', { type: 'password', name: 'Password', value: 'secret' }),
        file: element('input', { type: 'file', name: 'Upload' }),
        submit: element('input', { type: 'submit', name: 'submit.Save' }),
        disabled: element('input', { type: 'text', name: 'Locked', disabled: true }),
    };
}

describe('createPageTools', () => {
    it('offers the six page tools, marking the ones that only read', () => {
        const list = pageTools.createPageTools({ doc: fakeDocument(), loc: location, isAdmin: true });

        assert.deepStrictEqual(list.map((tool) => tool.name), ['page_get_info', 'page_read_content', 'page_list_links', 'page_navigate', 'page_list_form_fields', 'page_set_form_field']);
        assert.deepStrictEqual(list.filter((tool) => tool.annotations && tool.annotations.readOnlyHint).map((tool) => tool.name), ['page_get_info', 'page_read_content', 'page_list_links', 'page_list_form_fields']);

        list.forEach((tool) => {
            assert.ok(tool.description.length > 10);
            assert.strictEqual(tool.inputSchema.type, 'object');
        });
    });

    it('registers every page tool with the page\'s ModelContext, where any agent can run it, and unregisters them on abort', async () => {
        const modelContext = createFakeModelContext();
        const controller = new AbortController();

        await pageTools.register(modelContext, { doc: fakeDocument({ title: 'Orders' }), loc: location, isAdmin: false }, controller.signal);

        const tools = await modelContext.getTools();

        assert.deepStrictEqual(tools.map((tool) => tool.name), ['page_get_info', 'page_list_form_fields', 'page_list_links', 'page_navigate', 'page_read_content', 'page_set_form_field']);
        assert.strictEqual(JSON.parse(await modelContext.executeTool(tools[0], {})).title, 'Orders');

        controller.abort();

        assert.deepStrictEqual(await modelContext.getTools(), []);
    });
});

describe('page_get_info', () => {
    it('describes the page', async () => {
        const doc = fakeDocument({
            title: 'Content Items',
            description: 'All the content.',
            headings: [
                element('h1', { textContent: '  Content   Items ' }),
                element('h2', { textContent: 'Filters' }),
                element('h2', { textContent: '   ' }),
            ],
        });

        const { data } = await run(tools(doc).page_get_info);

        assert.deepStrictEqual(data, {
            title: 'Content Items',
            url: 'https://example.com/Admin/Contents',
            description: 'All the content.',
            headings: [{ level: 1, text: 'Content Items' }, { level: 2, text: 'Filters' }],
            isAdmin: true,
        });
    });
});

// A small DOM tree for page_read_content: text nodes, and elements that match the ignored selector by tag or class.
function text(value) {
    return { nodeType: 3, nodeValue: value };
}

function node(tagName, children = [], props = {}) {
    const tag = tagName.toUpperCase();
    const ignoredTags = ['SCRIPT', 'STYLE', 'NAV', 'FORM', 'ASIDE'];

    return Object.assign({
        nodeType: 1,
        tagName: tag,
        childNodes: children,
        hidden: false,
        className: '',
        get textContent() {
            return children.map((child) => (child.nodeType === 3 ? child.nodeValue : child.textContent)).join(' ');
        },
        matches(selector) {
            assert.strictEqual(selector, selectors.ignored);

            return ignoredTags.includes(tag) || this.className.includes('ai-chat-widget');
        },
    }, props);
}

function row(tag, values) {
    return { cells: values.map((value) => node(tag, [text(value)])) };
}

describe('page_read_content', () => {
    it('reads the main area as lines of text and its tables as rows, leaving out menus, forms, scripts and the chat', async () => {
        const table = node('table', [], {
            caption: node('caption', [text('Articles')]),
            rows: [
                row('th', ['Title', 'Created']),
                row('td', ['What Are Large Language Models?', '5/25/2026']),
                row('td', ['Understanding Embeddings', '5/25/2026']),
            ],
        });
        const main = node('main', [
            node('h2', [text('Articles')]),
            node('p', [text('Manage the '), node('strong', [text('sample')]), text(' content.')]),
            node('nav', [text('Dashboard')]),
            node('form', [text('Search')]),
            node('script', [text('var secret = 1;')]),
            node('div', [text('Hidden alert')], { hidden: true }),
            node('div', [text('Chat')], { className: 'ai-chat-widget-container' }),
            table,
            node('p', [text('6 articles')]),
        ]);

        const { data } = await run(tools(fakeDocument({ main })).page_read_content);

        assert.deepStrictEqual(data, {
            text: 'Articles\nManage the sample content.\n[table 1]\n6 articles',
            tables: [{
                caption: 'Articles',
                headers: ['Title', 'Created'],
                rows: [['What Are Large Language Models?', '5/25/2026'], ['Understanding Embeddings', '5/25/2026']],
                totalRows: 2,
            }],
            truncated: false,
        });
    });

    it('reads the body when the page has no main area, and caps long text and long tables', async () => {
        const rows = [];

        for (let i = 0; i < 60; i++) {
            rows.push(row('td', ['Row ' + i]));
        }

        const body = node('body', [node('p', [text('x'.repeat(9000))]), node('table', [], { rows })]);

        const { data } = await run(tools(fakeDocument({ body })).page_read_content);

        assert.strictEqual(data.text.length, 8001);
        assert.ok(data.text.endsWith('…'));
        assert.deepStrictEqual(data.tables[0].headers, []);
        assert.strictEqual(data.tables[0].rows.length, 50);
        assert.strictEqual(data.tables[0].totalRows, 60);
        assert.strictEqual(data.truncated, true);
    });

    it('skips the parts the page hides with CSS', async () => {
        const main = node('main', [
            node('p', [text('Shown')]),
            node('div', [text('Closed modal')], { checkVisibility: () => false }),
        ]);

        const { data } = await run(tools(fakeDocument({ main })).page_read_content);

        assert.strictEqual(data.text, 'Shown');
    });
});

describe('page_list_links', () => {
    const doc = fakeDocument({
        links: [
            element('a', { textContent: 'Content', attributes: { href: '/Admin/Contents' } }),
            element('a', { textContent: 'Users', attributes: { href: 'https://example.com/Admin/Users' } }),
            element('a', { textContent: 'Content again', attributes: { href: '/Admin/Contents' } }),
            element('a', { textContent: 'Elsewhere', attributes: { href: 'https://other.example/' } }),
            element('a', { textContent: 'Script', attributes: { href: 'javascript:alert(1)' } }),
            element('a', { textContent: 'Empty', attributes: { href: '' } }),
        ],
    });

    it('lists each same-origin link once', async () => {
        const { data } = await run(tools(doc).page_list_links);

        assert.deepStrictEqual(data.links, [
            { text: 'Content', url: 'https://example.com/Admin/Contents' },
            { text: 'Users', url: 'https://example.com/Admin/Users' },
        ]);
    });

    it('filters the links by their text', async () => {
        const { data } = await run(tools(doc).page_list_links, { filter: 'user' });

        assert.deepStrictEqual(data.links, [{ text: 'Users', url: 'https://example.com/Admin/Users' }]);
    });
});

describe('page_navigate', () => {
    it('opens a same-origin address', async () => {
        location.assign.mock.resetCalls();

        const { data } = await run(tools(fakeDocument()).page_navigate, { url: '/Admin/Users' });

        // The tool answers before the page goes away, so the agent gets the result.
        assert.deepStrictEqual(data, { navigatedTo: 'https://example.com/Admin/Users' });
        assert.strictEqual(location.assign.mock.callCount(), 0);

        await new Promise((resolve) => setTimeout(resolve, 450));

        assert.deepStrictEqual(location.assign.mock.calls.map((call) => call.arguments), [['https://example.com/Admin/Users']]);
    });

    it('refuses other origins, script addresses and a missing address', async () => {
        location.assign.mock.resetCalls();

        for (const input of [{ url: 'https://other.example/' }, { url: 'javascript:alert(1)' }, {}]) {
            await rejects(tools(fakeDocument()).page_navigate, input);
        }

        assert.strictEqual(location.assign.mock.callCount(), 0);
    });
});

describe('page_list_form_fields', () => {
    it('lists the editable fields and leaves out secrets, files, buttons and disabled fields', async () => {
        const doc = fakeDocument({ fields: Object.values(formFields()) });

        const { data } = await run(tools(doc).page_list_form_fields);

        assert.deepStrictEqual(data.fields.map((field) => field.name), ['TitlePart.Title', 'BodyPart.Body', 'Published', 'Status']);
        assert.deepStrictEqual(data.fields[0], {
            key: 'TitlePart.Title',
            name: 'TitlePart.Title',
            id: 'TitlePart_Title',
            label: 'Title',
            type: 'text',
            value: 'Hello',
            required: true,
        });
        assert.strictEqual(data.fields[1].label, 'Body');
        assert.strictEqual(data.fields[1].type, 'textarea');
        assert.strictEqual(data.fields[1].value.length, 2000);
        assert.strictEqual(data.fields[2].type, 'checkbox');
        assert.strictEqual(data.fields[2].value, false);
        assert.deepStrictEqual(data.fields[3].options, [{ value: 'draft', text: 'Draft', selected: true }, { value: 'live', text: 'Live', selected: false }]);
    });
});

describe('page_set_form_field', () => {
    it('sets a field found by name and raises input and change', async () => {
        const fields = formFields();
        const doc = fakeDocument({ fields: Object.values(fields) });

        const { data } = await run(tools(doc).page_set_form_field, { field: 'TitlePart.Title', value: 'New title' });

        assert.strictEqual(fields.title.value, 'New title');
        assert.deepStrictEqual(fields.title.events, ['input', 'change']);
        assert.strictEqual(data.field.value, 'New title');
    });

    it('finds a field by id or by its label', async () => {
        const fields = formFields();
        const doc = fakeDocument({ fields: Object.values(fields) });

        await run(tools(doc).page_set_form_field, { field: 'BodyPart_Body', value: 'Body text' });
        await run(tools(doc).page_set_form_field, { field: 'published', value: true });

        assert.strictEqual(fields.body.value, 'Body text');
        assert.strictEqual(fields.published.checked, true);
    });

    it('picks a choice by its value or its text, and refuses one the list does not have', async () => {
        const fields = formFields();
        const doc = fakeDocument({ fields: Object.values(fields) });

        await run(tools(doc).page_set_form_field, { field: 'Status', value: 'Live' });

        assert.strictEqual(fields.status.value, 'live');

        await rejects(tools(doc).page_set_form_field, { field: 'Status', value: 'Archived' });
    });

    it('refuses fields it does not list', async () => {
        const fields = formFields();
        const doc = fakeDocument({ fields: Object.values(fields) });

        for (const field of ['__RequestVerificationToken', 'Password', 'Nope']) {
            await rejects(tools(doc).page_set_form_field, { field, value: 'x' });
        }

        assert.strictEqual(fields.token.value, 'secret');
        assert.strictEqual(fields.password.value, 'secret');
    });
});
