// Playwright configuration for the live realtime end-to-end check (see realtime-live.spec.js). The host and
// provider are real; only the microphone is not. Chromium plays a WAV through its fake capture device; Firefox
// has no file-backed fake microphone, so the spec replaces getUserMedia with a Web Audio graph playing the same
// WAV. Both browsers therefore drive the full pipeline, including SIPSorcery's WebRTC interop with each of them.
const { defineConfig, devices } = require('@playwright/test');
const path = require('path');

const wav = process.env.REALTIME_E2E_WAV || path.join(__dirname, 'question.wav');

module.exports = defineConfig({
    testDir: __dirname,
    testMatch: /realtime-live\.spec\.js/,
    timeout: 150_000,
    workers: 1,
    reporter: 'list',
    use: {
        ignoreHTTPSErrors: true,
        trace: 'retain-on-failure',
    },
    projects: [
        {
            name: 'chromium',
            use: {
                ...devices['Desktop Chrome'],
                launchOptions: {
                    args: [
                        '--use-fake-device-for-media-stream',
                        '--use-fake-ui-for-media-stream',
                        `--use-file-for-fake-audio-capture=${wav}%noloop`,
                        '--autoplay-policy=no-user-gesture-required',
                        '--allow-insecure-localhost',
                    ],
                },
            },
        },
        {
            name: 'firefox',
            use: {
                ...devices['Desktop Firefox'],
                launchOptions: {
                    firefoxUserPrefs: {
                        'media.navigator.streams.fake': true,
                        'media.navigator.permission.disabled': true,
                        'media.autoplay.default': 0,
                        'media.autoplay.blocking_policy': 0,
                        // The dev host uses a self-signed certificate.
                        'network.stricttransportsecurity.preloadlist': false,
                    },
                },
            },
        },
    ],
});
