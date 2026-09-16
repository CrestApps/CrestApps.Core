// Unit tests for the figure-marker expansion both chat clients run before they parse a message
// (CoreAIChatMarkers.expandImageMarkers).
//
// Retrieval used to hand the model a figure's absolute address and ask it to embed that address itself. It did
// not copy those addresses: it copied their shape and substituted ordinals, and asked for figures that had
// never been in its results -- a figure index past the end of an article, an index that fell in a gap between
// two real ones. Every one of those was a broken picture in the answer. The model now writes a short label the
// tool already gave it, and the link is put back here, on the host, where it cannot be invented.
//
// So the rules are all statements about two strings, and they are tested as strings: no browser, no DOM, no
// markdown parser. Both chat clients carry their own copy of the helper and must behave identically, so every
// test below runs against both.
//
// Run with: node --test tests/realtime-client/figure-markers.test.js
const test = require('node:test');
const assert = require('node:assert');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const assetsDirectory = path.join(__dirname, '../../src/Resources/CrestApps.AI.Resources/Assets/js');

// Every link and caption in this file is invented. Nothing here comes from a real document.
const LINK = 'https://figures.invalid/sample/one';
const OTHER_LINK = 'https://figures.invalid/sample/two';

// Loads the shared marker module into a bare sandbox. It is a standalone file precisely so every chat
// surface shares one implementation -- the two shared chat scripts and the MVC chat-interaction view, which
// renders its markdown with its own inline marked setup and would otherwise need a third copy.
function loadMarkers() {
    const sandbox = { window: {} };

    vm.runInNewContext(fs.readFileSync(path.join(assetsDirectory, 'chat-markers.js'), 'utf8'), sandbox);

    const markers = sandbox.window.CoreAIChatMarkers;
    assert.ok(markers && typeof markers.expandImageMarkers === 'function',
        'chat-markers.js does not expose CoreAIChatMarkers.expandImageMarkers');

    return markers.expandImageMarkers;
}

// Every surface that renders chat markdown must load chat-markers.js before its own script, or the markers
// silently stay as literal text. Asserted here because a missing script tag is invisible until a figure is
// asked for.
function assertEverySurfaceLoadsTheModule() {
    const surfaces = [
        'src/Startup/CrestApps.Core.Blazor.Web/Components/App.razor',
        'src/Startup/CrestApps.Core.Mvc.Web/Areas/AIChat/Views/AIChat/Chat.cshtml',
        'src/Startup/CrestApps.Core.Mvc.Web/Areas/AIChat/Views/Shared/_ChatWidget.cshtml',
        'src/Startup/CrestApps.Core.Mvc.Web/Areas/ChatInteractions/Views/ChatInteraction/Chat.cshtml',
    ];

    for (const surface of surfaces) {
        const full = path.join(__dirname, '../../', surface);
        const markup = fs.readFileSync(full, 'utf8');
        assert.ok(markup.includes('scripts/chat-markers.js'), `${surface} does not load chat-markers.js`);
    }
}

assertEverySurfaceLoadsTheModule();

const clients = [{ fileName: 'chat-markers.js', expand: loadMarkers() }];

// Declares the same test once per chat client, so the two copies of the helper cannot quietly drift apart.
function bothClients(name, run) {
    for (const client of clients) {
        test(`${name} [${client.fileName}]`, () => run(client.expand));
    }
}

bothClients('a marker whose reference carries a picture becomes a markdown image', (expand) => {
    const references = { '[fig:1]': { isImage: true, link: LINK, title: 'Monthly totals' } };

    assert.equal(
        expand('The totals are plotted below. [fig:1]', references),
        `The totals are plotted below. ![Monthly totals](${LINK})`);
});

bothClients('a marker the model invented is left as text, not turned into a broken image', (expand) => {
    // The regression itself. The model writes a label for a figure that was never in its results; the map has
    // nothing under that key, so the reader sees five characters of text instead of a picture that 404s.
    const references = { '[fig:1]': { isImage: true, link: LINK, title: 'Monthly totals' } };

    assert.equal(expand('See [fig:9] for the breakdown.', references), 'See [fig:9] for the breakdown.');
});

bothClients('an image the host cannot serve keeps its marker', (expand) => {
    // Belt and braces: retrieval never registers a figure as an image without a link, and if one ever arrives
    // the marker still must not become an image with nowhere to point.
    const missing = { '[fig:1]': { isImage: true, link: '', title: 'No file' } };
    const blank = { '[fig:2]': { isImage: true, link: '   ', title: 'No file' } };
    const absent = { '[fig:3]': { isImage: true, title: 'No file' } };

    assert.equal(expand('a [fig:1] b', missing), 'a [fig:1] b');
    assert.equal(expand('a [fig:2] b', blank), 'a [fig:2] b');
    assert.equal(expand('a [fig:3] b', absent), 'a [fig:3] b');
});

bothClients('an ordinary citation is left for the citation pass', (expand) => {
    // Text citations are the same kind of marker with the same kind of map entry, and they are rendered as
    // footnotes further down the pipeline. Expanding one here would steal it.
    const references = {
        '[doc:1]': { isImage: false, link: OTHER_LINK, title: 'A source' },
        '[doc:2]': { link: OTHER_LINK, title: 'Another source' },
    };

    assert.equal(expand('Stated in [doc:1] and [doc:2].', references), 'Stated in [doc:1] and [doc:2].');
});

bothClients('a marker used twice is expanded twice', (expand) => {
    const references = { '[fig:1]': { isImage: true, link: LINK, title: 'Monthly totals' } };
    const image = `![Monthly totals](${LINK})`;

    assert.equal(expand('[fig:1] and again [fig:1]', references), `${image} and again ${image}`);
});

bothClients('a longer index is not corrupted by a shorter one', (expand) => {
    // [fig:1] must not match inside [fig:11]; the brackets are what make the labels self-delimiting.
    const references = {
        '[fig:1]': { isImage: true, link: LINK, title: 'First' },
        '[fig:11]': { isImage: true, link: OTHER_LINK, title: 'Eleventh' },
    };

    assert.equal(
        expand('[fig:1] [fig:11]', references),
        `![First](${LINK}) ![Eleventh](${OTHER_LINK})`);
});

bothClients('a caption with brackets, parentheses and a line break stays inside the alt text', (expand) => {
    const references = {
        '[fig:1]': { isImage: true, link: LINK, title: 'Figure 2 (revised) [draft]\nsecond line' },
    };

    assert.equal(
        expand('[fig:1]', references),
        `![Figure 2 (revised) \\[draft\\] second line](${LINK})`);
});

bothClients('a caption cannot close the image and inject markup of its own', (expand) => {
    const references = {
        '[fig:1]': { isImage: true, link: LINK, title: '](javascript:alert(1)) [x' },
    };

    const expanded = expand('[fig:1]', references);

    assert.equal(expanded, `![\\](javascript:alert(1)) \\[x](${LINK})`);

    // Every bracket the caption contributed is escaped, so only one image destination survives the escaping --
    // ours. Dropping the escaped pairs is how a markdown parser reads the line.
    const unescaped = expanded.replace(/\\./g, '');
    assert.equal(unescaped.split('](').length - 1, 1);
    assert.ok(unescaped.endsWith(`(${LINK})`), unescaped);
});

bothClients('a caption of nothing still describes the image', (expand) => {
    const empty = { '[fig:1]': { isImage: true, link: LINK, title: '' } };
    const blank = { '[fig:2]': { isImage: true, link: LINK, title: ' \n ' } };
    const absent = { '[fig:3]': { isImage: true, link: LINK } };
    const wrongType = { '[fig:4]': { isImage: true, link: LINK, title: 42 } };

    assert.equal(expand('[fig:1]', empty), `![Figure](${LINK})`);
    assert.equal(expand('[fig:2]', blank), `![Figure](${LINK})`);
    assert.equal(expand('[fig:3]', absent), `![Figure](${LINK})`);
    assert.equal(expand('[fig:4]', wrongType), `![Figure](${LINK})`);
});

bothClients('a link that would end the destination early is encoded, not left to break the image', (expand) => {
    const references = {
        '[fig:1]': { isImage: true, link: 'https://figures.invalid/a b/(1).png', title: 'Plot' },
    };

    assert.equal(expand('[fig:1]', references), '![Plot](https://figures.invalid/a%20b/%281%29.png)');
});

bothClients('a dollar sign in a caption or a link is not read as a replacement pattern', (expand) => {
    const references = {
        '[fig:1]': { isImage: true, link: 'https://figures.invalid/p?q=$&', title: 'Cost $& revenue' },
    };

    assert.equal(expand('[fig:1]', references), '![Cost $& revenue](https://figures.invalid/p?q=$&)');
});

bothClients('the wire shape is understood whichever way the map was serialized', (expand) => {
    const references = { '[fig:1]': { IsImage: true, Link: LINK, Title: 'Monthly totals' } };

    assert.equal(expand('[fig:1]', references), `![Monthly totals](${LINK})`);
});

bothClients('a figure and a citation in one answer each reach their own renderer', (expand) => {
    const references = {
        '[fig:1]': { isImage: true, link: LINK, title: 'Monthly totals' },
        '[doc:1]': { isImage: false, link: OTHER_LINK, title: 'A source' },
    };

    assert.equal(
        expand('Totals [fig:1] as reported in [doc:1].', references),
        `Totals ![Monthly totals](${LINK}) as reported in [doc:1].`);
});

bothClients('nothing to work with is not an error', (expand) => {
    const references = { '[fig:1]': { isImage: true, link: LINK, title: 'Monthly totals' } };

    assert.equal(expand(null, references), '');
    assert.equal(expand(undefined, references), '');
    assert.equal(expand('', references), '');
    assert.equal(expand(null, null), '');
    assert.equal(expand('[fig:1]', null), '[fig:1]');
    assert.equal(expand('[fig:1]', undefined), '[fig:1]');
    assert.equal(expand('[fig:1]', 'not a map'), '[fig:1]');
    assert.equal(expand('[fig:1]', { '[fig:1]': null }), '[fig:1]');
    assert.equal(expand('[fig:1]', { '': { isImage: true, link: LINK } }), '[fig:1]');
});
