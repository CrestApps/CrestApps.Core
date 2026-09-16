// Unit tests for the citation rules every chat surface now numbers references with
// (CoreAIChatMarkers.citationIdentity, collapseRepeatedCitations, separateAdjacentCitations,
// citationMarkerHtml).
//
// Retrieval returns one reference per chunk, so an article that answered a question through three of its
// chunks arrives as three references. Numbering those separately prints "1,2,3" over the sentence and then
// lists the same title three times, which tells the reader three sources agree when one does. Observed live
// against a real magazine: one answer carried three citations that all read "MAGYAR EPULETGEPESZET".
//
// Three surfaces render citations -- the two shared chat scripts and the MVC chat-interaction view -- and each
// keeps its own assembly loop, because they legitimately differ about generated files. What they must not
// differ about is which references are the same citation, so that rule lives in one place and is stated here.
//
// Every rule is a statement about strings, so it is tested as strings: no browser, no DOM, no markdown parser.
//
// Run with: node --test tests/realtime-client/citation-markers.test.js
const test = require('node:test');
const assert = require('node:assert');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const repositoryRoot = path.join(__dirname, '../../');
const assetsDirectory = path.join(repositoryRoot, 'src/Resources/CrestApps.AI.Resources/Assets/js');

function loadCitationRules() {
    const sandbox = { window: {} };

    vm.runInNewContext(fs.readFileSync(path.join(assetsDirectory, 'chat-markers.js'), 'utf8'), sandbox);

    const markers = sandbox.window.CoreAIChatMarkers;

    for (const name of ['citationIdentity', 'collapseRepeatedCitations', 'separateAdjacentCitations', 'citationMarkerHtml']) {
        assert.ok(markers && typeof markers[name] === 'function', `chat-markers.js does not expose CoreAIChatMarkers.${name}`);
    }

    return markers;
}

const rules = loadCitationRules();

test('two chunks of one article are one citation', () => {
    const first = rules.citationIdentity('Magyar Epuletgepeszet, p. 15', null);
    const second = rules.citationIdentity('Magyar Epuletgepeszet, p. 15', null);

    assert.strictEqual(first, second);
});

test('the same title on a different page is a different citation', () => {
    const page15 = rules.citationIdentity('Magyar Epuletgepeszet, p. 15', null);
    const page16 = rules.citationIdentity('Magyar Epuletgepeszet, p. 16', null);

    assert.notStrictEqual(page15, page16);
});

test('the same label pointing at different figures stays two citations', () => {
    const first = rules.citationIdentity('Figure', 'https://host/figures/a');
    const second = rules.citationIdentity('Figure', 'https://host/figures/b');

    assert.notStrictEqual(first, second);
});

test('case and surrounding whitespace are not a difference the reader can act on', () => {
    const plain = rules.citationIdentity('Magyar Epuletgepeszet', null);
    const shouted = rules.citationIdentity('  MAGYAR EPULETGEPESZET  ', null);

    assert.strictEqual(plain, shouted);
});

test('a label that runs onto a second line is the same citation', () => {
    assert.strictEqual(
        rules.citationIdentity('Nyilaszaro csere\n   es arnyekolo', null),
        rules.citationIdentity('Nyilaszaro csere es arnyekolo', null));
});

test('a missing link and an empty link are the same absence', () => {
    assert.strictEqual(rules.citationIdentity('A', null), rules.citationIdentity('A', ''));
});

test('the boundary between label and link cannot be forged', () => {
    // Without a separator that cannot occur in either part, "a" + "bc" and "ab" + "c" would collide.
    assert.notStrictEqual(rules.citationIdentity('a', 'bc'), rules.citationIdentity('ab', 'c'));
});

test('a repeated marker over one sentence is collapsed', () => {
    assert.strictEqual(rules.collapseRepeatedCitations('claim<sup>1</sup><sup>1</sup>'), 'claim<sup>1</sup>');
});

test('three of the same marker collapse to one', () => {
    assert.strictEqual(
        rules.collapseRepeatedCitations('claim<sup>2</sup><sup>2</sup><sup>2</sup>'),
        'claim<sup>2</sup>');
});

test('two different sources are left as two markers', () => {
    assert.strictEqual(
        rules.collapseRepeatedCitations('claim<sup>1</sup><sup>2</sup>'),
        'claim<sup>1</sup><sup>2</sup>');
});

test('the same source cited again later keeps its own marker', () => {
    const html = 'first<sup>1</sup> and then second<sup>1</sup>';

    assert.strictEqual(rules.collapseRepeatedCitations(html), html);
});

test('a marker carrying a tooltip still collapses', () => {
    // The number is what makes two markers the same citation; a pattern written for a bare <sup> would stop
    // collapsing the moment a marker gained a title.
    assert.strictEqual(
        rules.collapseRepeatedCitations('claim<sup title="Source A">1</sup><sup title="Source A">1</sup>'),
        'claim<sup title="Source A">1</sup>');
});

test("the model's own comma between two references is swallowed with them", () => {
    // Observed live: the model writes "[ref1],[ref2]", and once both resolve to one source that comma is
    // left standing between a number and itself.
    assert.strictEqual(
        rules.collapseRepeatedCitations('issue<sup>1</sup>,<sup>1</sup>.'),
        'issue<sup>1</sup>.');
});

test('a comma with spaces around it is swallowed too', () => {
    assert.strictEqual(
        rules.collapseRepeatedCitations('issue<sup>1</sup> , <sup>1</sup>.'),
        'issue<sup>1</sup>.');
});

test('three references reduced to one source leave one marker', () => {
    assert.strictEqual(
        rules.collapseRepeatedCitations('issue<sup>1</sup>,<sup>1</sup>,<sup>1</sup>.'),
        'issue<sup>1</sup>.');
});

test('the comma marker the separator rule inserts is swallowed as well', () => {
    assert.strictEqual(
        rules.collapseRepeatedCitations('issue<sup>1</sup><sup>,</sup><sup>1</sup>.'),
        'issue<sup>1</sup>.');
});

test('a comma between two different sources is kept', () => {
    const html = 'issue<sup>1</sup>,<sup>2</sup>.';

    assert.strictEqual(rules.collapseRepeatedCitations(html), html);
});

test('sentence punctuation after a citation is not eaten', () => {
    const html = 'as shown<sup>1</sup>, the value rose<sup>1</sup>.';

    assert.strictEqual(rules.collapseRepeatedCitations(html), html);
});

test('a longer number is not collapsed into a shorter one that prefixes it', () => {
    const html = 'claim<sup>1</sup><sup>12</sup>';

    assert.strictEqual(rules.collapseRepeatedCitations(html), html);
});

test('adjacent markers are separated by a comma', () => {
    assert.strictEqual(
        rules.separateAdjacentCitations('claim<sup>1</sup><sup>2</sup>'),
        'claim<sup>1</sup><sup>,</sup><sup>2</sup>');
});

test('separating markers leaves their attributes alone', () => {
    assert.strictEqual(
        rules.separateAdjacentCitations('<sup title="A">1</sup><sup title="B">2</sup>'),
        '<sup title="A">1</sup><sup>,</sup><sup title="B">2</sup>');
});

test('a lone marker is left as it is', () => {
    assert.strictEqual(rules.separateAdjacentCitations('claim<sup>1</sup>'), 'claim<sup>1</sup>');
});

test('the comma inserted between two markers does not attract another', () => {
    // A replace walks the string it was given, so what it inserts is not rescanned.
    assert.strictEqual(
        rules.separateAdjacentCitations('<sup>1</sup><sup>2</sup><sup>3</sup>'),
        '<sup>1</sup><sup>,</sup><sup>2</sup><sup>,</sup><sup>3</sup>');
});

test('a marker carries its source as a tooltip', () => {
    assert.strictEqual(rules.citationMarkerHtml(1, 'Magyar Epuletgepeszet, p. 15'),
        '<sup title="Magyar Epuletgepeszet, p. 15">1</sup>');
});

test('a label with no text leaves a bare marker rather than an empty tooltip', () => {
    assert.strictEqual(rules.citationMarkerHtml(3, '   '), '<sup>3</sup>');
    assert.strictEqual(rules.citationMarkerHtml(3, null), '<sup>3</sup>');
});

test('a quote in a caption cannot end the attribute it sits in', () => {
    const html = rules.citationMarkerHtml(1, 'The "Big" Issue');

    assert.ok(!html.includes('"Big"'), 'the quotes were not escaped');
    assert.strictEqual(html, '<sup title="The &quot;Big&quot; Issue">1</sup>');
});

test('markup in a caption is escaped rather than introduced', () => {
    assert.strictEqual(
        rules.citationMarkerHtml(1, '<img src=x onerror=alert(1)>'),
        '<sup title="&lt;img src=x onerror=alert(1)&gt;">1</sup>');
});

test('an ampersand is escaped once, not twice', () => {
    assert.strictEqual(rules.citationMarkerHtml(1, 'Heat & Power'), '<sup title="Heat &amp; Power">1</sup>');
});

test('nothing to work with is not an error', () => {
    assert.strictEqual(rules.collapseRepeatedCitations(''), '');
    assert.strictEqual(rules.collapseRepeatedCitations(null), '');
    assert.strictEqual(rules.separateAdjacentCitations(''), '');
    assert.strictEqual(rules.separateAdjacentCitations(undefined), '');
});

// The surfaces that number citations. Each keeps its own assembly loop; none of them may keep its own answer
// to which references are the same citation.
const surfaces = [
    'src/Resources/CrestApps.AI.Resources/Assets/js/ai-chat.js',
    'src/Resources/CrestApps.AI.Resources/Assets/js/chat-interaction.js',
    'src/Startup/CrestApps.Core.Mvc.Web/Areas/ChatInteractions/Views/ChatInteraction/Chat.cshtml',
];

for (const surface of surfaces) {
    test(`${path.basename(surface)} reads the shared citation rules`, () => {
        const source = fs.readFileSync(path.join(repositoryRoot, surface), 'utf8');

        assert.ok(source.includes('CoreAIChatMarkers'),
            `${surface} does not reference the shared marker module`);
        assert.ok(source.includes('citationIdentity'),
            `${surface} does not use the shared citation identity`);
    });

    test(`${path.basename(surface)} keeps no hand-written comma rule`, () => {
        const source = fs.readFileSync(path.join(repositoryRoot, surface), 'utf8');

        // The literal the three surfaces used to carry. It only matches a bare marker, so it silently stopped
        // working the moment a marker gained a tooltip -- which is why it belongs in one place.
        assert.ok(!source.includes("'</sup><sup>', '</sup><sup>,</sup><sup>'"),
            `${surface} still carries its own copy of the comma rule`);
    });
}
