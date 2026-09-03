// Cross-browser tests for the realtime voice client (realtime-audio.js).
//
// These cover the parts that only exist in a browser — the microphone gate, transport selection and fallback,
// and the session state machine driven by server events. The server-side runner has its own xUnit tests; what
// no server-side test can reach is whether the browser actually stops playing, releases the microphone, or
// keeps hearing the user after a tab switch, which is where this feature's real defects were.
//
// Runs against a static harness page with fake media devices, so no server, SignalR connection or AI provider
// is involved.
const { test, expect } = require('@playwright/test');

const HARNESS = '/tests/realtime-client/harness.html';

async function startSession(page) {
    await page.click('#realtime-btn');
    await expect.poll(() => page.evaluate(() => window.harness.started.length)).toBeGreaterThan(0);
}

test.describe('realtime voice client', () => {
    test('asks the server for ICE servers before creating the peer', async ({ page }) => {
        // The whole point of F1: a browser built with a hardcoded STUN server ignores configured TURN relays and
        // silently falls back to the WebSocket transport on every strict NAT.
        await page.goto(HARNESS);
        await startSession(page);

        const invoked = await page.evaluate(() => window.harness.invoked);
        expect(invoked).toContain('GetRealtimeIceServers');
    });

    test('reports connecting, then listening once the server says the session is ready', async ({ page }) => {
        await page.goto(HARNESS);
        await startSession(page);

        await page.evaluate(() => window.harness.raise('ReceiveRealtimeEvent', 'session-1', 'session_ready', null));

        await expect.poll(() => page.evaluate(() => window.harness.states)).toContain('listening');
        const states = await page.evaluate(() => window.harness.states);
        expect(states.indexOf('connecting')).toBeLessThan(states.indexOf('listening'));
    });

    test('releases the microphone when the server reports the session ended', async ({ page }) => {
        // Without this the browser keeps the mic open and streams audio into a session that no longer exists.
        await page.goto(HARNESS);
        await startSession(page);
        await page.evaluate(() => window.harness.raise('ReceiveRealtimeEvent', 'session-1', 'session_ready', null));
        await expect.poll(() => page.evaluate(() => window.controller.isActive())).toBe(true);

        await page.evaluate(() => window.harness.raise('ReceiveRealtimeEvent', 'session-1', 'session_ended', 'idle'));

        await expect.poll(() => page.evaluate(() => window.controller.isActive())).toBe(false);
        await expect.poll(() => page.evaluate(() => window.harness.states)).toContain('ended');
    });

    test('offers to resume after an idle close', async ({ page }) => {
        await page.goto(HARNESS);
        await startSession(page);
        await page.evaluate(() => window.harness.raise('ReceiveRealtimeEvent', 'session-1', 'session_ended', 'idle'));

        const resume = page.getByRole('button', { name: 'Resume' });
        await expect(resume).toBeVisible();
    });

    test('ends the session when the connection drops', async ({ page }) => {
        await page.goto(HARNESS);
        await startSession(page);
        await page.evaluate(() => window.harness.raise('ReceiveRealtimeEvent', 'session-1', 'session_ready', null));

        await page.evaluate(() => window.harness.close());

        await expect.poll(() => page.evaluate(() => window.controller.isActive())).toBe(false);
    });

    test('falls back rather than giving up when the server errors before the peer connects', async ({ page }) => {
        // A server-side failure during the WebRTC handshake — a rejected offer, an unavailable peer factory — is a
        // reason to try the other transport, not to end the conversation. Treating it as terminal is what turned a
        // recoverable codec mismatch into a session that silently did nothing at all.
        await page.goto(HARNESS);
        await startSession(page);

        await page.evaluate(() => window.harness.raise('ReceiveError', 'WebRTC offer rejected'));

        await expect
            .poll(() => page.evaluate(() => window.harness.started.some(s => s.transport === 'ws')))
            .toBe(true);
        await expect.poll(() => page.evaluate(() => window.controller.isActive())).toBe(true);
    });

    test('ends the session when the server reports an error on a live session', async ({ page }) => {
        await page.goto(HARNESS);
        await startSession(page);
        // Reach the WebSocket path first, where an error is genuinely terminal.
        await page.evaluate(() => window.harness.raise('ReceiveError', 'first failure'));
        await expect
            .poll(() => page.evaluate(() => window.harness.started.some(s => s.transport === 'ws')))
            .toBe(true);

        await page.evaluate(() => window.harness.raise('ReceiveError', 'something failed'));

        await expect.poll(() => page.evaluate(() => window.controller.isActive())).toBe(false);
    });

    test('passes pending and dropped user turns to the host', async ({ page }) => {
        // The placeholder is what keeps a spoken prompt above the reply it produced: transcription lags the
        // answer, so a bubble added only when the text arrives lands underneath its own reply.
        await page.goto(HARNESS);
        await startSession(page);

        await page.evaluate(() => {
            window.harness.raise('ReceiveRealtimeEvent', 'session-1', 'user_turn_pending', 'turn-1');
            window.harness.raise('ReceiveRealtimeEvent', 'session-1', 'user_turn_dropped', 'turn-1');
        });

        expect(await page.evaluate(() => window.harness.pendingTurn)).toBe('turn-1');
        expect(await page.evaluate(() => window.harness.droppedTurn)).toBe('turn-1');
    });

    test('sends no language hint when the language setting is Auto', async ({ page }) => {
        // Sending the browser locale instead pinned transcription and the reply to it, which mistranscribes a
        // bilingual user whose browser is in a different language than the one they are speaking.
        await page.goto(HARNESS);
        await startSession(page);

        const started = await page.evaluate(() => window.harness.started[0]);
        expect(started.language).toBeNull();
    });

    test('pushes a settings change to the server during a live session', async ({ page }) => {
        // Barge-in is enforced by the gate, the server pump and the provider at once; changing only the browser's
        // half leaves the assistant still interrupting itself.
        await page.goto(HARNESS);
        await startSession(page);

        await page.click('#realtime-audio-settings button');
        await page.uncheck('.js-barge');

        await expect
            .poll(() => page.evaluate(() => window.harness.sent.filter(a => a[0] === 'UpdateRealtimeSettings').length))
            .toBeGreaterThan(0);

        const call = await page.evaluate(() => window.harness.sent.filter(a => a[0] === 'UpdateRealtimeSettings').pop());
        expect(call[1]).toBe(false);
    });

    test('space does not toggle push-to-talk while typing in a text box', async ({ page }) => {
        await page.goto(HARNESS);
        await page.click('#realtime-audio-settings button');
        await page.check('.js-ptt');
        await page.click('body');
        await startSession(page);

        // Realtime mode hides the chat input, so the field at risk is any other one on the page.
        await page.focus('#other-input');
        await page.keyboard.type('a b');

        expect(await page.inputValue('#other-input')).toBe('a b');
    });

    test('falls back to the WebSocket transport and says so when WebRTC cannot connect', async ({ page }) => {
        test.slow();
        await page.goto(HARNESS);
        await startSession(page);

        // No server answers the offer, so the connect timer expires and the client restarts on WebSocket.
        await expect
            .poll(() => page.evaluate(() => window.harness.started.some(s => s.transport === 'ws')), { timeout: 20000 })
            .toBe(true);

        await expect(page.getByText(/compatibility audio mode/i)).toBeVisible();
    });

    test('skips WebRTC for the rest of the browser session once it has failed', async ({ page }) => {
        test.slow();
        await page.goto(HARNESS);
        await startSession(page);
        await expect
            .poll(() => page.evaluate(() => window.harness.started.some(s => s.transport === 'ws')), { timeout: 20000 })
            .toBe(true);

        // A second session in the same tab must not pay the connect timeout again.
        await page.evaluate(() => { window.controller.stop(); window.harness.started.length = 0; });
        await startSession(page);

        const started = await page.evaluate(() => window.harness.started);
        expect(started.every(s => s.transport === 'ws')).toBe(true);
    });
});
