// Unit tests for the microphone gate's decision rules (CoreAIRealtime.decideGate).
//
// The gate decides whether the model hears the user at all, and every one of its historical failures is a rule
// in here: a quiet microphone that never opened it, a loud room's echo that did, one sentence chopped into
// several provider turns, the half-duplex mic re-opening in the gaps between the assistant's words. Those are
// all statements about numbers, so they are tested as numbers — no browser, no audio graph, no timing.
//
// Run with: npm run test:gate
const test = require('node:test');
const assert = require('node:assert');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

// Load the browser module into a bare sandbox. Only `window` is touched at definition time; `document` is used
// solely inside attach(), which these tests never call.
const modulePath = path.join(__dirname, '../../src/Resources/CrestApps.AI.Resources/Assets/js/realtime-audio.js');
const sandbox = { window: {}, document: {} };
vm.runInNewContext(fs.readFileSync(modulePath, 'utf8'), sandbox);

const { decideGate, createGateState, rmsDb } = sandbox.window.CoreAIRealtime;

const AUTO = { bargeIn: true, gateMode: 'auto' };
const HALF_DUPLEX = { bargeIn: false, gateMode: 'auto' };

// Feeds a constant level for `ms` in 10 ms blocks, starting at `nowMs`, and returns the final state.
function feed(state, { micDb, assistantDb = -100, mode = AUTO, ms, from = 0 }) {
    for (let t = from; t < from + ms; t += 10) {
        decideGate(state, { micDb, assistantDb, nowMs: t, mode });
    }

    return state;
}

// Settles the adaptive floor on a quiet room so tests start from a realistic baseline rather than the -60 dB
// the state is seeded with.
function settled(roomDb = -58, mode = AUTO) {
    const state = createGateState();
    feed(state, { micDb: roomDb, mode, ms: 20_000 });

    return state;
}

test('rmsDb reports a full-scale block at 0 dBFS and silence far below', () => {
    assert.ok(Math.abs(rmsDb(new Float32Array(128).fill(1)) - 0) < 0.001);
    assert.equal(rmsDb(new Float32Array(128)), -100);
});

test('a quiet room does not open the gate', () => {
    const state = settled(-58);

    assert.equal(state.open, false);
    // The floor tracked the room rather than sitting at its seeded default.
    assert.ok(state.floorDb < -50, `floor was ${state.floorDb}`);
});

test('a quiet speaker opens the gate where a fixed threshold never would', () => {
    // -34 dBFS is a normal voice on a webcam or monitor microphone: peak amplitude around 0.028, below the 0.05
    // fixed level the gate used to require. "The model never detects speech" was this.
    const state = settled(-58);
    feed(state, { micDb: -34, ms: 100, from: 20_000 });

    assert.equal(state.open, true);
});

test('a loud room does not open the gate at a level a quiet room would', () => {
    // The point of tracking a floor rather than an absolute threshold: -34 dBFS is speech in a quiet room and
    // just background in a loud one.
    const state = settled(-38);
    feed(state, { micDb: -34, ms: 100, from: 20_000 });

    assert.equal(state.open, false);
});

test('echo residual does not open the gate while the assistant is audible', () => {
    // Opening here is what made the model hear, and answer, itself.
    const state = settled(-58);
    feed(state, { micDb: -45, assistantDb: -20, ms: 500, from: 20_000 });

    assert.equal(state.open, false);
});

test('the user can still interrupt over the assistant with real speech', () => {
    const state = settled(-58);
    feed(state, { micDb: -25, assistantDb: -20, ms: 100, from: 20_000 });

    assert.equal(state.open, true);
});

test('the gate stays open through a pause inside a sentence', () => {
    // Closing during a natural pause chopped one utterance into several provider turns.
    const state = settled(-58);
    feed(state, { micDb: -30, ms: 300, from: 20_000 });
    feed(state, { micDb: -58, ms: 400, from: 20_300 });

    assert.equal(state.open, true);
});

test('the gate closes once the user has clearly finished', () => {
    const state = settled(-58);
    feed(state, { micDb: -30, ms: 300, from: 20_000 });
    feed(state, { micDb: -58, ms: 2000, from: 20_300 });

    assert.equal(state.open, false);
});

test('"assistant speaking" survives the gaps between its words', () => {
    // Without the hangover the half-duplex gate re-opened in every inter-word pause and fed the echo tail back.
    const state = settled(-58, HALF_DUPLEX);
    feed(state, { micDb: -58, assistantDb: -20, mode: HALF_DUPLEX, ms: 500, from: 20_000 });
    // A 250 ms gap between words.
    feed(state, { micDb: -58, assistantDb: -100, mode: HALF_DUPLEX, ms: 250, from: 20_500 });

    assert.equal(state.assistantSpeaking, true);
    assert.equal(state.open, false);
});

test('half duplex opens a listening grace as soon as the assistant really stops', () => {
    // So the start of the user's next turn is not clipped while the gate waits to detect speech.
    const state = settled(-58, HALF_DUPLEX);
    feed(state, { micDb: -58, assistantDb: -20, mode: HALF_DUPLEX, ms: 500, from: 20_000 });
    feed(state, { micDb: -58, assistantDb: -100, mode: HALF_DUPLEX, ms: 600, from: 20_500 });

    assert.equal(state.assistantSpeaking, false);
    assert.equal(state.open, true);
});

test('strict mode requires more level than auto in the same room', () => {
    const strict = { bargeIn: true, gateMode: 'strict' };
    const quietSpeech = -47;

    const auto = settled(-58);
    feed(auto, { micDb: quietSpeech, ms: 100, from: 20_000 });

    const tight = settled(-58, strict);
    feed(tight, { micDb: quietSpeech, mode: strict, ms: 100, from: 20_000 });

    assert.equal(auto.open, true);
    assert.equal(tight.open, false);
});

test('gate mode "off" keeps the microphone open on silence', () => {
    const off = { bargeIn: true, gateMode: 'off' };
    const state = settled(-58, off);

    assert.equal(state.open, true);
});

test('push-to-talk overrides everything', () => {
    const held = { pushToTalk: true, pttActive: true };
    const released = { pushToTalk: true, pttActive: false };

    const open = settled(-58, held);
    assert.equal(open.open, true);

    // Loud speech, but the key is not held.
    const closed = settled(-58, released);
    feed(closed, { micDb: -10, mode: released, ms: 200, from: 20_000 });
    assert.equal(closed.open, false);
});

test('a burst of speech does not drag the floor up enough to gate the rest of it out', () => {
    // The floor rises slowly on purpose; a fast-rising floor would close mid-sentence.
    const state = settled(-58);
    const floorBefore = state.floorDb;
    feed(state, { micDb: -25, ms: 3000, from: 20_000 });

    assert.equal(state.open, true);
    assert.ok(state.floorDb - floorBefore < 15, `floor climbed ${state.floorDb - floorBefore} dB during speech`);
});
