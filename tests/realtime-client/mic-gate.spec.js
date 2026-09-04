// Browser-level tests for the microphone gate (CoreAIRealtime.createMicGate).
//
// The gate's *rules* are covered exhaustively and deterministically in gate-decision.test.js, which runs in Node
// against the same decision function. What is left here is only what needs a real browser: that the worklet
// actually loads and gates a live audio graph, that it runs on the audio thread rather than a main-thread loop
// a hidden tab would freeze, and that the assistant's stream reaches it.
const { test, expect } = require('@playwright/test');

const HARNESS = '/tests/realtime-client/gate-harness.html';

// Feeds `micLevel` (and optionally an assistant signal) through the gate for `ms` and reports the fraction of
// the time the signal made it through.
async function measure(page, { micLevel, assistantLevel = 0, ms = 1500, mode }) {
    return page.evaluate(
        ([micLevel, assistantLevel, ms, mode]) => window.gateHarness.measure(micLevel, assistantLevel, ms, mode),
        [micLevel, assistantLevel, ms, mode]);
}

test.describe('microphone gate', () => {
    test.beforeEach(async ({ page }) => {
        await page.goto(HARNESS);
        await page.evaluate(() => window.gateHarness.ready);
    });

    test('runs on the audio thread rather than a main-thread loop', async ({ page }) => {
        // The whole reason the gate is an AudioWorklet. requestAnimationFrame is paused for hidden documents and
        // timers are throttled there, so a main-thread gate freezes in whatever state it held when the user
        // switched tabs — stuck closed meant never being heard again for the rest of the conversation. A headless
        // browser cannot be put into a genuinely hidden state, so assert which implementation is in use.
        expect(await page.evaluate(() => window.gateHarness.gate.usingWorklet)).toBe(true);
    });

    test('passes real speech through the live audio graph', async ({ page }) => {
        // -34 dBFS: a normal voice on a webcam or monitor microphone.
        await measure(page, { micLevel: 0.0018, ms: 2500, mode: { bargeIn: true, gateMode: 'auto' } });
        const openRatio = await measure(page, { micLevel: 0.02, ms: 1500, mode: { bargeIn: true, gateMode: 'auto' } });

        expect(openRatio).toBeGreaterThan(0.8);
    });

    test('silences the outgoing track on a quiet room', async ({ page }) => {
        const openRatio = await measure(page, { micLevel: 0.0018, ms: 2500, mode: { bargeIn: true, gateMode: 'auto' } });

        expect(openRatio).toBeLessThan(0.05);
    });

    test('sees the assistant stream and holds the gate shut against its echo', async ({ page }) => {
        // Verifies the second worklet input is actually wired to the assistant's stream, which no unit test can.
        await measure(page, { micLevel: 0.0018, ms: 2500, mode: { bargeIn: true, gateMode: 'auto' } });
        const openRatio = await measure(page, { micLevel: 0.006, assistantLevel: 0.3, ms: 2000, mode: { bargeIn: true, gateMode: 'auto' } });

        expect(openRatio).toBeLessThan(0.05);
    });

    test('keeps deciding while the main thread is blocked', async ({ page, browserName }) => {
        // The observable corollary of running on the audio thread: stall the main thread outright, and the gate
        // has still opened for the speech that arrived while it was stalled.
        //
        // Chromium only. Headless Firefox drives its audio graph from a thread that is itself starved when the
        // main thread spins, so this measures the test harness rather than the gate there; the assertion above
        // that the worklet is in use covers Firefox.
        test.skip(browserName !== 'chromium', 'headless Firefox stalls its audio graph with the main thread');

        await measure(page, { micLevel: 0.0018, ms: 2000, mode: { bargeIn: true, gateMode: 'auto' } });

        const wasOpen = await page.evaluate(async () => {
            window.gateHarness.gate.setMode({ bargeIn: true, gateMode: 'auto' });
            await window.gateHarness.speak(0.02);

            const busyUntil = Date.now() + 800;
            while (Date.now() < busyUntil) { /* deliberately blocking the main thread */ }

            return window.gateHarness.sampleOnce();
        });

        expect(wasOpen).toBe(true);
    });
});
