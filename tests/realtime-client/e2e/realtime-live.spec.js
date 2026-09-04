// Live end-to-end check of realtime voice against a running MVC host and a real realtime deployment.
//
// Not part of the default suite (it needs a configured provider and a signed-in user). Run it explicitly:
//
//   REALTIME_E2E_BASE_URL=https://localhost:5100 REALTIME_E2E_PROFILE_ID=<profile id> \
//   REALTIME_E2E_USER=admin REALTIME_E2E_PASSWORD=... REALTIME_E2E_WAV=<path to a 16-bit PCM WAV of a spoken question> \
//   npx playwright test --config tests/realtime-client/e2e/playwright.e2e.config.js [--project=chromium|firefox]
//
// The WAV becomes the microphone. In Chromium it is played by the browser's own fake capture device; in Firefox
// (which has no file-backed fake microphone) — or with REALTIME_E2E_INJECT_MIC=1 anywhere — getUserMedia is
// replaced with a Web Audio graph that plays the decoded WAV into a MediaStream. Either way the test expects the
// spoken words to appear as the user's transcript and a reply to follow, which exercises the microphone gate, the
// WebRTC transport (SIPSorcery interop with each browser), the provider's turn detection and the transcript
// pipeline together.
//
// REALTIME_E2E_BARGE_IN=false runs with interruptions off. The recorded "user" does not wait for the assistant,
// so in that mode only the general shape of the exchange is asserted (a prompt and a reply), not the exact words.
const { test, expect } = require('@playwright/test');
const fs = require('fs');
const path = require('path');

const BASE = process.env.REALTIME_E2E_BASE_URL || 'https://localhost:5100';
const PROFILE_ID = process.env.REALTIME_E2E_PROFILE_ID;
const USER = process.env.REALTIME_E2E_USER || 'admin';
const PASSWORD = process.env.REALTIME_E2E_PASSWORD || '';
const EXPECT_USER = process.env.REALTIME_E2E_EXPECT_USER || 'capital of France';
const WAV = process.env.REALTIME_E2E_WAV || path.join(__dirname, 'question.wav');
const BARGE_IN = process.env.REALTIME_E2E_BARGE_IN !== 'false';

test('a spoken question is transcribed and answered', async ({ page, browserName }) => {
    test.setTimeout(150_000);
    const log = [];
    const note = (line) => log.push(`${new Date().toISOString()} ${line}`);
    page.on('console', (m) => note(`[${m.type()}] ${m.text().slice(0, 300)}`));
    page.on('pageerror', (e) => note(`[pageerror] ${e.message}`));

    const injectMic = process.env.REALTIME_E2E_INJECT_MIC === '1' || browserName !== 'chromium';
    if (injectMic) {
        const wavBase64 = fs.readFileSync(WAV).toString('base64');
        await page.addInitScript(({ wavBase64 }) => {
            const original = navigator.mediaDevices.getUserMedia.bind(navigator.mediaDevices);
            navigator.mediaDevices.getUserMedia = async function (constraints) {
                if (!constraints || !constraints.audio) { return original(constraints); }
                const bytes = Uint8Array.from(atob(wavBase64), (c) => c.charCodeAt(0));
                const ctx = new AudioContext();
                if (ctx.state === 'suspended') { await ctx.resume(); }
                const buffer = await ctx.decodeAudioData(bytes.buffer);
                const source = ctx.createBufferSource();
                source.buffer = buffer;
                const destination = ctx.createMediaStreamDestination();
                source.connect(destination);
                source.start();
                window.__injectedMicrophone = { ctx, source };

                return destination.stream;
            };
        }, { wavBase64 });
    }

    // Samples the gate's live measurement so a silent failure can be explained from the log.
    let sampler = null;
    const startSampling = () => {
        sampler = setInterval(async () => {
            try {
                const level = await page.evaluate(() => window.CoreAIRealtime && window.CoreAIRealtime.lastGateLevel);
                if (level) { note(`[gate] ${JSON.stringify(level)}`); }
            } catch (e) { /* page gone */ }
        }, 500);
    };

    try {
        await page.goto(`${BASE}/Account/Login`);
        await page.fill('#username', USER);
        await page.fill('#password', PASSWORD);
        await Promise.all([page.waitForNavigation(), page.click('form button[type=submit], form input[type=submit]')]);

        await page.goto(`${BASE}/AIChat/AIChat/Chat?profileId=${PROFILE_ID}`);
        await expect(page.locator('#chat-conversation-btn')).toBeVisible();

        // Fresh per-device preferences: interruptions as requested, nothing else.
        await page.evaluate((bargeIn) => {
            try {
                localStorage.setItem('coreai.realtime.audioPrefs', JSON.stringify({ bargeIn: bargeIn, pushToTalk: false, volume: 1, micDeviceId: '', outputDeviceId: '', language: '', version: 3 }));
                sessionStorage.clear();
            } catch (e) { }
        }, BARGE_IN);
        await page.reload();
        await expect(page.locator('#chat-conversation-btn')).toBeVisible();

        await page.click('#chat-conversation-btn');
        startSampling();

        await expect(page.locator('[role=status]').first()).toContainText(/Listening/i, { timeout: 30_000 });
        note(`[test] listening (${browserName}, interruptions ${BARGE_IN ? 'on' : 'off'}, mic ${injectMic ? 'injected' : 'fake device'})`);

        const app = page.locator('#chat-app');
        if (BARGE_IN) {
            // The user's words, then a reply that goes beyond echoing them.
            await expect(app).toContainText(new RegExp(EXPECT_USER, 'i'), { timeout: 60_000 });
            note('[test] user transcript shown');
        } else {
            await expect(app).toContainText(/\S/, { timeout: 60_000 });
            note('[test] a user turn was shown');
        }
        await expect
            .poll(async () => (await app.innerText()).replace(new RegExp(EXPECT_USER, 'i'), '').trim().length, { timeout: 60_000 })
            .toBeGreaterThan(20);
        note('[test] assistant reply shown');

        // Let the reply play for a moment, then stop.
        await page.waitForTimeout(8_000);
        note(`[test] transcript:\n${await app.innerText()}`);
        await page.click('#chat-conversation-btn');
        await page.waitForTimeout(1_000);
    } finally {
        if (sampler) { clearInterval(sampler); }
        console.log(log.join('\n'));
        await page.screenshot({ path: `test-results/realtime-live-${browserName}.png`, fullPage: true }).catch(() => { });
    }
});
