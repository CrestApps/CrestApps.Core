// Unit tests for the antiforgery token the chat clients send with their POSTs (CoreAIAntiforgery).
//
// The document upload and remove endpoints validate the antiforgery token of a request the browser authenticated on its
// own, which is what cookie authentication does, so a page on another site cannot make a visitor's browser upload a
// file as them. The token has to come from somewhere the chat can read: the config the host hands the chat, a meta tag
// or the hidden field of a form on the page. These tests cover where it is found and that both chat clients send it on
// every POST they make; they run in a bare sandbox with no browser.
//
// Run with: node --test tests/realtime-client/antiforgery-header.test.js
const test = require('node:test');
const assert = require('node:assert');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const assetsDirectory = path.join(__dirname, '../../src/Resources/CrestApps.AI.Resources/Assets/js');

function loadAntiforgery(document) {
    const sandbox = { window: {}, document };

    vm.runInNewContext(fs.readFileSync(path.join(assetsDirectory, 'chat-markers.js'), 'utf8'), sandbox);

    const antiforgery = sandbox.window.CoreAIAntiforgery;
    assert.ok(antiforgery && typeof antiforgery.headers === 'function',
        'chat-markers.js does not expose CoreAIAntiforgery.headers');

    return antiforgery;
}

function fakeDocument({ meta, input } = {}) {
    return {
        querySelector(selector) {
            if (selector.includes('meta') && meta) {
                return { content: meta };
            }

            if (selector.includes('input') && input) {
                return { value: input };
            }

            return null;
        }
    };
}

test('the token from the chat config is sent in the RequestVerificationToken header', () => {
    const antiforgery = loadAntiforgery(fakeDocument());

    assert.deepStrictEqual({ ...antiforgery.headers({ antiforgeryToken: 'from-config' }) }, { RequestVerificationToken: 'from-config' });
});

test('a meta tag is used when the config has no token', () => {
    const antiforgery = loadAntiforgery(fakeDocument({ meta: 'from-meta' }));

    assert.strictEqual(antiforgery.headers({}).RequestVerificationToken, 'from-meta');
});

test('the hidden field of a form is used when there is neither a config token nor a meta tag', () => {
    const antiforgery = loadAntiforgery(fakeDocument({ input: 'from-field' }));

    assert.strictEqual(antiforgery.headers({}).RequestVerificationToken, 'from-field');
});

test('the config token wins over the page', () => {
    const antiforgery = loadAntiforgery(fakeDocument({ meta: 'from-meta', input: 'from-field' }));

    assert.strictEqual(antiforgery.headers({ antiforgeryToken: 'from-config' }).RequestVerificationToken, 'from-config');
});

test('no header is sent when no token can be found, and other headers are kept', () => {
    const antiforgery = loadAntiforgery(fakeDocument());

    assert.deepStrictEqual({ ...antiforgery.headers({}, { 'Content-Type': 'application/json' }) }, { 'Content-Type': 'application/json' });
    assert.deepStrictEqual({ ...antiforgery.headers({}) }, {});
});

test('a request that is not a browser one (no document) still works', () => {
    const antiforgery = loadAntiforgery(undefined);

    assert.deepStrictEqual({ ...antiforgery.headers({}) }, {});
});

// The two chat clients are separate bundles, so each is checked on its own: every POST goes through the helper.
for (const script of ['ai-chat.js', 'chat-interaction.js']) {
    test(`${script} sends the antiforgery token on its document POSTs`, () => {
        const source = fs.readFileSync(path.join(assetsDirectory, script), 'utf8');
        const posts = [...source.matchAll(/method:\s*'POST'/g)];

        assert.ok(posts.length > 0, `${script} makes no POST requests`);

        for (const post of posts) {
            const request = source.slice(post.index, post.index + 400);

            assert.match(request, /CoreAIAntiforgery\.headers\(/, `a POST in ${script} does not send the antiforgery token`);
        }

        for (const open of source.matchAll(/xhr\.open\('POST'[^\n]*\n/g)) {
            const request = source.slice(open.index, open.index + 200);

            assert.match(request, /CoreAIAntiforgery\.apply\(/, `an XMLHttpRequest POST in ${script} does not send the antiforgery token`);
        }
    });
}
