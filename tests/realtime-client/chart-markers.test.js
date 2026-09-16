// Unit tests for the chart-marker parser every chat surface now reads markers with
// (CoreAIChatMarkers.findChartMarker).
//
// The chart tool hands the model a [chart:{...}] marker and asks for it back verbatim, and the host turns the
// marker it gets back into a canvas. Three surfaces render those markers -- the two shared chat scripts and the
// MVC chat-interaction view -- and until now each carried its own hand-written parser for one. The hard parts
// of reading a marker are exactly the parts a second hand-written copy gets subtly wrong: a config that nests,
// a brace inside a label, an escaped quote, a bracket the model dropped. There is one parser now, and the tests
// below are the statement of what it must do.
//
// Every rule here is a statement about two strings, so it is tested as strings: no browser, no DOM, no markdown
// parser and no Chart.js. Drawing the chart -- the canvas, the id scheme, the Chart.js call -- still belongs to
// each surface and differs between them legitimately.
//
// Run with: node --test tests/realtime-client/chart-markers.test.js
const test = require('node:test');
const assert = require('node:assert');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const repositoryRoot = path.join(__dirname, '../../');
const assetsDirectory = path.join(repositoryRoot, 'src/Resources/CrestApps.AI.Resources/Assets/js');

// Loads the shared marker module into a bare sandbox, the same way the figure-marker tests do. It is a
// standalone file precisely so every chat surface shares one implementation of what a marker is.
function loadChartMarkerReader() {
    const sandbox = { window: {} };

    vm.runInNewContext(fs.readFileSync(path.join(assetsDirectory, 'chat-markers.js'), 'utf8'), sandbox);

    const markers = sandbox.window.CoreAIChatMarkers;
    assert.ok(markers && typeof markers.findChartMarker === 'function',
        'chat-markers.js does not expose CoreAIChatMarkers.findChartMarker');

    return markers.findChartMarker;
}

// The surfaces that turn a chart marker into a canvas. Each keeps its own rendering; none of them may keep its
// own parser, because a second copy of these rules is a second set of answers to them.
const surfaces = [
    'src/Resources/CrestApps.AI.Resources/Assets/js/ai-chat.js',
    'src/Resources/CrestApps.AI.Resources/Assets/js/chat-interaction.js',
    'src/Startup/CrestApps.Core.Mvc.Web/Areas/ChatInteractions/Views/ChatInteraction/Chat.cshtml',
];

function assertNoSurfaceParsesMarkersItself() {
    for (const surface of surfaces) {
        const source = fs.readFileSync(path.join(repositoryRoot, surface), 'utf8');

        assert.ok(source.includes('CoreAIChatMarkers.findChartMarker'),
            `${surface} does not read chart markers through the shared parser`);

        // The canary for a re-inlined copy: tracking whether the scan is inside a JSON string is something only
        // a brace-balancing parser needs to do, and no surface has any business doing it any more.
        assert.ok(!source.includes('inString'),
            `${surface} carries a chart-marker parser of its own again`);
    }
}

// The marker is a contract between the tool that writes it and the parser that reads it, so it is pinned at
// both ends. A tool that started emitting a different shape would otherwise show up as a wall of JSON in a
// chat rather than as a failing test.
function assertTheToolStillEmitsTheMarkerWeParse() {
    const tool = fs.readFileSync(
        path.join(repositoryRoot, 'src/Primitives/CrestApps.Core.AI/Tools/GenerateChartTool.cs'), 'utf8');

    assert.match(tool, /\[chart:\{\w+\}\]/, 'GenerateChartTool no longer emits [chart:{...}]');
}

assertNoSurfaceParsesMarkersItself();
assertTheToolStillEmitsTheMarkerWeParse();

const find = loadChartMarkerReader();

// Every config in this file is invented. Nothing here comes from a real chart, a real document or a real
// organisation.
const simpleConfig = '{"type":"bar","data":{"labels":["A","B"],"datasets":[{"label":"Totals","data":[1,2]}]}}';

test('the marker the chart tool emits is read whole', () => {
    const text = `[chart:${simpleConfig}]`;
    const marker = find(text);

    assert.ok(marker, 'the marker was not found');
    assert.equal(marker.json, simpleConfig);
    assert.equal(marker.startIndex, 0);
    assert.equal(text.slice(marker.startIndex, marker.endIndex), text);
});

test('a marker in the middle of an answer reports where it starts and ends', () => {
    const before = 'Totals for the quarter:\n\n';
    const after = '\n\nThe rest of the answer.';
    const text = `${before}[chart:${simpleConfig}]${after}`;
    const marker = find(text);

    assert.equal(marker.startIndex, before.length);
    assert.equal(text.slice(marker.startIndex, marker.endIndex), `[chart:${simpleConfig}]`);

    // The span is what a caller removes from the text, so nothing around the marker may be inside it.
    assert.equal(text.slice(0, marker.startIndex) + text.slice(marker.endIndex), before + after);
});

test('a config that nests is not truncated at the first closing brace', () => {
    const config = '{"type":"line","options":{"plugins":{"legend":{"display":false}},"scales":{"y":{"min":0}}}}';
    const marker = find(`[chart:${config}]`);

    assert.equal(marker.json, config);
    assert.deepEqual(JSON.parse(marker.json).options.scales.y, { min: 0 });
});

test('a brace inside a label is data, not the end of the config', () => {
    const config = '{"type":"bar","data":{"labels":["Q1 { Q2","} Q3"],"datasets":[]}}';
    const text = `[chart:${config}] and the rest of the answer.`;
    const marker = find(text);

    assert.equal(marker.json, config);
    assert.deepEqual(JSON.parse(marker.json).data.labels, ['Q1 { Q2', '} Q3']);
    assert.equal(text.slice(marker.startIndex, marker.endIndex), `[chart:${config}]`);
});

test('an escaped quote does not end the string it sits in', () => {
    // The label reads: say " }
    const config = '{"type":"bar","data":{"labels":["say \\" }"],"datasets":[]}}';
    const marker = find(`[chart:${config}]`);

    assert.equal(marker.json, config);
    assert.deepEqual(JSON.parse(marker.json).data.labels, ['say " }']);
});

test('a string that ends in an escaped backslash ends where it says it does', () => {
    // The label ends with a single backslash, so the quote after it really does close the string. A scan that
    // forgot to clear the escape would read that quote as escaped, stay inside the string to the end of the
    // text, and report no marker at all.
    const config = '{"type":"bar","data":{"labels":["a\\\\"],"datasets":[]}}';
    const marker = find(`[chart:${config}]`);

    assert.ok(marker, 'the marker was not found');
    assert.deepEqual(JSON.parse(marker.json).data.labels, ['a\\']);
});

test('a marker the model never closed is not a marker', () => {
    assert.equal(find(`[chart:${simpleConfig}`), null);
});

test('a marker still arriving is not read early', () => {
    // Messages are rendered as they stream, so half a config is an ordinary thing to be handed.
    assert.equal(find('[chart:{"type":"bar","data":{"labels":["A"'), null);
});

test('a dropped bracket does not let a marker reach for one belonging to something else', () => {
    // Why the closing bracket has to follow the config: searching further ahead for a ']' finds the one in the
    // citation below, and the span then covers the whole paragraph. The chart would render and that paragraph
    // would quietly disappear from the answer -- a guess presented as a marker.
    const text = `[chart:${simpleConfig}\n\nAs reported in [doc:1].`;

    assert.equal(find(text), null);
});

test('a malformed marker does not swallow the good one after it', () => {
    const text = `[chart:{oops}\n\n[chart:${simpleConfig}]`;
    const marker = find(text);

    assert.ok(marker, 'the second marker was not found');
    assert.equal(marker.json, simpleConfig);
    assert.equal(text.slice(marker.startIndex, marker.endIndex), `[chart:${simpleConfig}]`);
});

test('text that merely looks like a marker is left as text', () => {
    assert.equal(find('The [chart: prefix, written out in prose.'), null);
    assert.equal(find('[chart: not json]'), null);
    assert.equal(find('[chart:]'), null);
    assert.equal(find('[charts:{"type":"bar"}]'), null);
    assert.equal(find('chart:{"type":"bar"}'), null);
    assert.equal(find('[chart:{"type":"bar"]'), null);
    assert.equal(find('[chart:{"type":"bar","data":{}]'), null);
});

test('the whitespace a model leaves around the config is tolerated', () => {
    const text = `[chart: \n ${simpleConfig} \n ]`;
    const marker = find(text);

    assert.equal(marker.json, simpleConfig);
    assert.equal(marker.endIndex, text.length);
});

test('anything but whitespace between the config and the bracket is not a marker', () => {
    assert.equal(find(`[chart:${simpleConfig} please render this]`), null);
});

test('the first marker is found, and what follows it is still a marker of its own', () => {
    const first = '{"type":"bar","data":{"labels":["A"],"datasets":[]}}';
    const second = '{"type":"pie","data":{"labels":["B"],"datasets":[]}}';
    const text = `[chart:${first}]\n\nAnd the split by region:\n\n[chart:${second}]`;

    const marker = find(text);
    assert.equal(marker.json, first);
    assert.equal(text.slice(marker.startIndex, marker.endIndex), `[chart:${first}]`);

    // This is how the markdown tokenizer consumes them: take the span, carry on with what is left.
    const next = find(text.slice(marker.endIndex));
    assert.equal(next.json, second);
});

test('nothing to work with is not an error', () => {
    assert.equal(find(''), null);
    assert.equal(find(null), null);
    assert.equal(find(undefined), null);
    assert.equal(find(42), null);
    assert.equal(find({}), null);
    assert.equal(find('An answer with no chart in it at all.'), null);
});

// A marker the host itself produces, copied verbatim from KnowledgeChartMarker's output for a chart read out
// of a PDF. The C# side has its own tests and so does the parser, and both can pass while disagreeing about
// the string that travels between them -- so the string travels through this test too.
//
// It is the awkward case on purpose: the config nests arrays, so the marker contains ']' characters long
// before the one that ends it; an axis title contains a literal '[range]'; and a series label contains
// escaped quotes. A parser that scanned for the first ']' would truncate this into nonsense.
test('the marker the host emits for a chart read out of a document is read back whole', () => {
    const findChartMarker = loadChartMarkerReader();
    const marker = '[chart:{"type":"scatter","data":{"datasets":[{"label":"Measured \\u0022peak\\u0022","data":[{"x":1990,"y":3.5},{"x":2000,"y":2.1}],"showLine":true,"fill":false},{"data":[{"x":1990,"y":1}],"showLine":true,"fill":false}]},"options":{"plugins":{"legend":{"display":true}},"scales":{"x":{"title":{"display":true,"text":"Year [range]"}},"y":{"title":{"display":true,"text":"W/m\\u00B2K"}}}}}]';
    const text = `Here is the chart. ${marker} And some words after it.`;

    const found = findChartMarker(text);

    assert.ok(found, 'the parser refused a marker this host produces');
    assert.strictEqual(text.slice(found.endIndex), ' And some words after it.',
        'the marker did not end where it ends');

    const config = JSON.parse(found.json);

    assert.strictEqual(config.type, 'scatter');
    assert.strictEqual(config.data.datasets.length, 2);
    assert.strictEqual(config.options.scales.x.title.text, 'Year [range]');
    assert.strictEqual(config.data.datasets[0].label, 'Measured "peak"');

    // A series the document never named carries no label rather than an invented one.
    assert.strictEqual(config.data.datasets[1].label, undefined);
});
