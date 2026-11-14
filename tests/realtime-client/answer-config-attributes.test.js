// An assistant answer is text a model wrote from whatever it was given: a document, a crawled page, a prompt someone
// injected. The chat scripts watch the page for any added element that carries a chat configuration and start a chat
// from it, and a chat's Vue template is that configuration's messageTemplate, which Vue compiles. So an answer that
// contains such an element would run script in the site's origin with no click.
//
// Two things stop it, and each is tested here on its own: the sanitizer removes the attributes (and the id the
// configuration's selectors point at) from an answer, and the auto-start never reads an element that is inside a
// rendered message or inside a chat that is already running.
//
// Run with: node --test tests/realtime-client/answer-config-attributes.test.js
const test = require('node:test');
const assert = require('node:assert');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { JSDOM } = require('jsdom');
const createDOMPurify = require('dompurify');

const assetsDirectory = path.join(__dirname, '../../src/Resources/CrestApps.AI.Resources/Assets/js');
const PAGE = 'https://chat.example.test/chat';

function load() {
    const dom = new JSDOM('<!doctype html><html><body></body></html>', { url: PAGE });
    const purify = createDOMPurify(dom.window);
    const sandbox = { window: { location: dom.window.location }, document: dom.window.document, URL };

    vm.runInNewContext(fs.readFileSync(path.join(assetsDirectory, 'chat-markers.js'), 'utf8'), sandbox);

    return {
        dom,
        window: sandbox.window,
        sanitize(html) {
            // The configuration the two chat clients pass.
            return sandbox.window.CoreAIImages.sanitize(purify, html, { ADD_TAGS: ['canvas'], ADD_ATTR: ['target'] }, { allowedHosts: [], baseUrl: PAGE });
        },
        parse(html) {
            const container = dom.window.document.createElement('div');
            container.innerHTML = html;

            return container;
        }
    };
}

const configAttributes = [
    'data-coreai-chat-config',
    'data-coreai-chat-app-element-selector',
    'data-coreai-chat-messages',
    'data-coreai-chat-signalr-hub-url',
    'data-chat-interaction-config',
    'data-chat-interaction-anything'
];

for (const attribute of configAttributes) {
    test(`the sanitizer removes ${attribute} from an answer`, () => {
        const page = load();
        const result = page.parse(page.sanitize(`<div ${attribute}='{"messageTemplate":"x"}'>hello</div>`));
        const div = result.querySelector('div');

        assert.ok(div, 'the element itself stays');
        assert.equal(div.textContent, 'hello');
        assert.equal(div.hasAttribute(attribute), false);
    });
}

test('the sanitizer removes a chat configuration from every element an answer nests it in', () => {
    const page = load();
    const result = page.sanitize(
        `<section><p data-coreai-chat-config='{"messageTemplate":"{{ constructor.constructor(1)() }}"}'>a</p>` +
        `<a href="https://example.com/" data-chat-interaction-config="{}">b</a>` +
        `<img src="data:image/png;base64,AAAA" alt="" data-coreai-chat-app-element-selector="#x"></section>`);

    assert.doesNotMatch(result, /data-coreai|data-chat-interaction|messageTemplate/);
    assert.match(result, /<a href="https:\/\/example.com\/"/);
});

test('the sanitizer keeps the attributes that are not a chat configuration', () => {
    const page = load();
    const result = page.parse(page.sanitize('<div data-chart-id="chat_chart_1" data-note="x" class="c" title="t">hi</div>'));
    const div = result.querySelector('div');

    assert.equal(div.getAttribute('data-chart-id'), 'chat_chart_1');
    assert.equal(div.getAttribute('data-note'), 'x');
    assert.equal(div.getAttribute('class'), 'c');
    assert.equal(div.getAttribute('title'), 't');
});

for (const markup of [
    '<div id="chat-app">x</div>',
    '<span id="ai-chat-doc-input">x</span>',
    '<a id="target" href="https://example.com/">x</a>',
    '<h2 id="section">x</h2>'
]) {
    test(`the sanitizer removes the id of ${markup}`, () => {
        const page = load();
        const result = page.parse(page.sanitize(markup));

        assert.equal(result.querySelectorAll('[id]').length, 0);
        assert.equal(result.textContent, 'x');
    });
}

test('the sanitizer keeps the id of a chart canvas the chat made, and only that', () => {
    const page = load();

    // The chat renders [chart:...] into this exact markup and finds the canvas again by its id.
    const chart = page.parse(page.sanitize('<div class="chart-container"><canvas id="chat_chart_7"></canvas></div><button class="download-chart-btn" data-chart-id="chat_chart_7">d</button>'));
    assert.equal(chart.querySelector('canvas').getAttribute('id'), 'chat_chart_7');
    assert.equal(chart.querySelector('button').getAttribute('data-chart-id'), 'chat_chart_7');

    // The MVC chat-interaction view numbers its own canvases ci_chart_N.
    assert.equal(page.parse(page.sanitize('<canvas id="ci_chart_3"></canvas>')).querySelector('canvas').getAttribute('id'), 'ci_chart_3');

    // Any other id on a canvas, an id that only starts like a chart's, and a chart id on another element are not the chat's.
    for (const markup of ['<canvas id="other"></canvas>', '<canvas id="chat_chart_7x"></canvas>', '<canvas id="chat_chart_"></canvas>', '<div id="chat_chart_7"></div>']) {
        const result = page.parse(page.sanitize(markup));

        assert.equal(result.querySelectorAll('[id]').length, 0, markup);
    }
});

test('the sanitizer does not leave its hooks on the sanitizer', () => {
    // The page sanitizes other things (icons, a document bar) that may carry an id.
    const dom = new JSDOM('<!doctype html><html><body></body></html>', { url: PAGE });
    const purify = createDOMPurify(dom.window);
    const sandbox = { window: { location: dom.window.location }, document: dom.window.document, URL };

    vm.runInNewContext(fs.readFileSync(path.join(assetsDirectory, 'chat-markers.js'), 'utf8'), sandbox);
    sandbox.window.CoreAIImages.sanitize(purify, '<p>x</p>', {}, { allowedHosts: [], baseUrl: PAGE });

    assert.match(purify.sanitize('<i id="keep" class="fa"></i>'), /id="keep"/);
});

test('an element inside a rendered message is not read as a chat to start', () => {
    const page = load();
    const surface = page.window.CoreAIChatSurface;
    const document = page.dom.window.document;

    document.body.innerHTML =
        '<div id="chat-app"><div class="ai-chat-messages"><div class="ai-chat-message-item"><div class="ai-chat-message-body"><div id="answer"><p id="inner"></p></div></div></div></div></div>' +
        '<div id="page-config"></div>';

    assert.ok(surface && typeof surface.isInsideRenderedMessage === 'function');
    assert.equal(surface.isInsideRenderedMessage(document.getElementById('inner')), true);
    assert.equal(surface.isInsideRenderedMessage(document.getElementById('answer')), true);
    assert.equal(surface.isInsideRenderedMessage(document.getElementById('page-config')), false);
});

test('an element inside a chat that is already running is not read as a chat to start, whatever its template', () => {
    // A host may give the chat its own messageTemplate, so the class names of the default one cannot be what decides.
    const page = load();
    const surface = page.window.CoreAIChatSurface;
    const document = page.dom.window.document;

    document.body.innerHTML = '<div id="custom-root"><section><div id="added-later"></div></section></div><div id="elsewhere"></div>';
    const root = document.getElementById('custom-root');

    assert.equal(surface.isInsideRenderedMessage(document.getElementById('added-later')), false, 'nothing has started here yet');

    surface.registerRoot(root);

    assert.equal(surface.isInsideRenderedMessage(document.getElementById('added-later')), true);
    assert.equal(surface.isInsideRenderedMessage(document.getElementById('elsewhere')), false);
    assert.equal(surface.isInsideRenderedMessage(root), false, 'the root is the chat itself, not something inside it');
});

test('both chat scripts register what they mount and skip rendered messages when they scan', () => {
    for (const name of ['ai-chat.js', 'chat-interaction.js']) {
        const source = fs.readFileSync(path.join(assetsDirectory, name), 'utf8');

        assert.match(source, /CoreAIChatSurface\.registerRoot\(/, `${name} does not register the element it mounts the chat on`);

        const scan = source.slice(source.indexOf('function scanForAutoInitialization'), source.indexOf('function startAutoInitialization'));

        assert.match(scan, /isInsideRenderedMessage/, `${name} auto-starts a chat from an element inside a rendered message`);
    }
});
