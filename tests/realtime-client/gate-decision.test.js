// Unit tests for the microphone gate's decision rules (CoreAIRealtime.decideGate).
//
// The gate decides whether the model hears the user at all, and every one of its historical failures is a rule
// in here: a quiet microphone that never opened it, a loud room's echo that did, one sentence chopped into
// several provider turns, the half-duplex mic re-opening in the gaps between the assistant's words, and — the
// one that made a model answer itself for a whole reply — speakers loud enough that their echo looked like a
// user interrupting. Those are all statements about numbers, so they are tested as numbers — no browser, no
// audio graph, no timing.
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

const BARGE_IN = { bargeIn: true };
const HALF_DUPLEX = { bargeIn: false };

// Feeds a constant level for `ms` in 10 ms blocks, starting at `from`, and returns the final state.
function feed(state, { micDb, assistantDb = -100, mode = BARGE_IN, ms, from = 0 }) {
    for (let t = from; t < from + ms; t += 10) {
        decideGate(state, { micDb, assistantDb, nowMs: t, mode });
    }

    return state;
}

// Settles the adaptive floor on a quiet room so tests start from a realistic baseline.
function settled(roomDb = -58, mode = BARGE_IN) {
    const state = createGateState();
    feed(state, { micDb: roomDb, mode, ms: 20_000 });

    return state;
}

// Lets the assistant talk for `ms` while the microphone hears its echo at `echoDb`, so the gate learns the room's
// echo return level the way it would during the first reply of a real session.
function assistantSpeaksWithEcho(state, { assistantDb, echoDb, ms, from, mode = BARGE_IN }) {
    feed(state, { micDb: echoDb, assistantDb, mode, ms, from });

    return state;
}

test('rmsDb reports a full-scale block at 0 dBFS and silence far below', () => {
    assert.ok(Math.abs(rmsDb(new Float32Array(128).fill(1)) - 0) < 0.001);
    assert.equal(rmsDb(new Float32Array(128)), -100);
});

test('a quiet room does not open the gate', () => {
    const state = settled(-58);

    assert.equal(state.open, false);
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
    const state = settled(-38);
    feed(state, { micDb: -34, ms: 100, from: 20_000 });

    assert.equal(state.open, false);
});

test('the gate stays open through a pause inside a sentence', () => {
    const state = settled(-58);
    feed(state, { micDb: -30, ms: 300, from: 20_000 });
    feed(state, { micDb: -58, ms: 1500, from: 20_300 });

    assert.equal(state.open, true);
});

test('the gate closes once the user has clearly finished', () => {
    const state = settled(-58);
    feed(state, { micDb: -30, ms: 300, from: 20_000 });
    feed(state, { micDb: -58, ms: 3000, from: 20_300 });

    assert.equal(state.open, false);
});

test('the gate stays open through a long sentence from a moderately loud speaker', () => {
    // The noise floor must not climb towards the talker's own voice and cut them off mid-sentence.
    const state = settled(-58);

    for (let t = 20_000; t < 30_000; t += 10) {
        decideGate(state, { micDb: -30, assistantDb: -100, nowMs: t, mode: BARGE_IN });
        assert.equal(state.open, true, `gate closed ${t - 20_000} ms into the sentence (floor ${state.floorDb.toFixed(1)} dB)`);
    }
});

test('the floor still tracks a room that gets noisier while nobody is speaking', () => {
    const state = settled(-58);
    feed(state, { micDb: -45, ms: 30_000, from: 20_000 });

    assert.equal(state.open, false);
    assert.ok(state.floorDb > -50, `floor did not follow the louder room: ${state.floorDb.toFixed(1)}`);
});

test('echo residual does not open the gate while the assistant is audible', () => {
    // Opening here is what made the model hear, and answer, itself.
    const state = settled(-58);
    feed(state, { micDb: -45, assistantDb: -20, ms: 500, from: 20_000 });

    assert.equal(state.open, false);
});

test('loud speakers next to the microphone never open the gate on their own echo', () => {
    // The open-office failure: two speakers on the desk and a webcam microphone. After cancellation the echo
    // still came back at -10 dBFS — louder than the user's own voice — and a level-only gate passed a whole
    // 20-second reply straight back to the model, which then interrupted and answered itself.
    const state = settled(-58);

    for (let t = 20_000; t < 40_000; t += 10) {
        decideGate(state, { micDb: -10, assistantDb: -10, nowMs: t, mode: BARGE_IN });
        assert.equal(state.open, false, `echo opened the gate ${t - 20_000} ms into the reply`);
    }

    assert.ok(state.echoReturnDb > -6, `echo return level was learned as ${state.echoReturnDb}`);
});

test('the echo of the very first reply is rejected even before the return level has settled', () => {
    // Echo reaches the microphone 100-200 ms after the assistant starts; the estimate has to catch up faster than
    // the interruption confirmation window, or the first sentence of every conversation leaks.
    const state = settled(-58);
    // Assistant starts; the room has not echoed yet.
    feed(state, { micDb: -58, assistantDb: -10, ms: 120, from: 20_000 });
    // The echo arrives, loud.
    feed(state, { micDb: -18, assistantDb: -10, ms: 3000, from: 20_120 });

    assert.equal(state.open, false);
});

test('the user can still interrupt over the assistant when their voice beats the echo', () => {
    const state = settled(-58);
    // A normal room: echo comes back 25 dB below the assistant's level.
    assistantSpeaksWithEcho(state, { assistantDb: -10, echoDb: -35, ms: 3000, from: 20_000 });

    // The user talks over it at a normal level.
    feed(state, { micDb: -22, assistantDb: -10, ms: 400, from: 23_000 });

    assert.equal(state.open, true);
});

test('interrupting takes a sustained voice, not a single loud block', () => {
    const state = settled(-58);
    assistantSpeaksWithEcho(state, { assistantDb: -10, echoDb: -35, ms: 3000, from: 20_000 });

    // A cough: 60 ms of level.
    feed(state, { micDb: -15, assistantDb: -10, ms: 60, from: 23_000 });
    assert.equal(state.open, false);

    // Speech: sustained.
    feed(state, { micDb: -15, assistantDb: -10, ms: 300, from: 23_060 });
    assert.equal(state.open, true);
});

test('with a headset the assistant is never heard back, so interruptions stay cheap', () => {
    const state = settled(-70);
    // Nothing comes back into the mic while the assistant talks.
    assistantSpeaksWithEcho(state, { assistantDb: -10, echoDb: -70, ms: 3000, from: 20_000 });

    // A quiet interruption still gets through.
    feed(state, { micDb: -40, assistantDb: -10, ms: 400, from: 23_000 });

    assert.equal(state.open, true);
});

test('turning the volume down is noticed: the learned echo level falls again', () => {
    const state = settled(-58);
    assistantSpeaksWithEcho(state, { assistantDb: -10, echoDb: -12, ms: 5000, from: 20_000 });
    const loud = state.echoReturnDb;

    // Volume halved twice: the echo drops 12 dB relative to the track.
    assistantSpeaksWithEcho(state, { assistantDb: -10, echoDb: -24, ms: 8000, from: 25_000 });

    assert.ok(state.echoReturnDb < loud - 6, `echo return level stayed at ${state.echoReturnDb} (was ${loud})`);
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

test('half duplex never opens while the assistant is audible, however loud the user is', () => {
    const state = settled(-58, HALF_DUPLEX);
    feed(state, { micDb: -5, assistantDb: -20, mode: HALF_DUPLEX, ms: 1000, from: 20_000 });

    assert.equal(state.open, false);
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
    const state = settled(-58);
    const floorBefore = state.floorDb;
    feed(state, { micDb: -25, ms: 3000, from: 20_000 });

    assert.equal(state.open, true);
    assert.ok(state.floorDb - floorBefore < 15, `floor climbed ${state.floorDb - floorBefore} dB during speech`);
});

test('a user who is already talking when the microphone goes live is heard', () => {
    // The first real audio after the pipeline's start-up silence may be the user, not the room. Seeding the
    // floor from it put the floor at their own level and the gate never opened — "Listening, but it never
    // answers", on a headset whose noise suppression emits exact digital silence between words.
    const state = createGateState();
    feed(state, { micDb: -100, ms: 1000 });
    feed(state, { micDb: -25, ms: 200, from: 1000 });

    assert.equal(state.open, true, `floor seeded at ${state.floorDb}`);
});

test('a loud room heard first does not hold the gate open for long', () => {
    const state = createGateState();
    feed(state, { micDb: -100, ms: 200 });
    feed(state, { micDb: -40, ms: 15_000, from: 200 });

    assert.equal(state.open, false, `floor ${state.floorDb}`);
});
