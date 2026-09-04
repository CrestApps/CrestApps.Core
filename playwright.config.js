// Browser tests for the realtime voice client. They run against a static harness page (no server, no SignalR
// connection, no AI provider) because the behaviour under test is the browser's: the microphone gate, transport
// fallback, and the session state machine.
//
//   npx playwright install chromium firefox   (once)
//   npm run test:client
const { defineConfig, devices } = require('@playwright/test');

// Fake media so getUserMedia resolves without hardware or a permission prompt.
const chromiumMediaArgs = [
    '--use-fake-device-for-media-stream',
    '--use-fake-ui-for-media-stream',
    '--autoplay-policy=no-user-gesture-required',
];

const firefoxMediaPrefs = {
    'media.navigator.streams.fake': true,
    'media.navigator.permission.disabled': true,
    'media.autoplay.default': 0,
    'media.autoplay.blocking_policy': 0,
};

module.exports = defineConfig({
    testDir: './tests/realtime-client',
    // The live end-to-end check needs a running host and a real provider; it has its own config under e2e/.
    testIgnore: ['**/e2e/**'],
    // The transport-fallback tests deliberately wait out an 8-second connect timeout.
    timeout: 60_000,
    expect: { timeout: 10_000 },
    // These drive real-time audio graphs, which need the CPU when they need it. Running specs in parallel
    // starves the audio threads in headless browsers and produces measurement flake that says nothing about the
    // code under test; the whole suite still finishes in well under a minute.
    fullyParallel: false,
    workers: 1,
    forbidOnly: !!process.env.CI,
    retries: process.env.CI ? 1 : 0,
    reporter: process.env.CI ? 'github' : 'list',
    use: {
        baseURL: 'http://127.0.0.1:4173',
        trace: 'on-first-retry',
    },
    // A plain static file server rooted at the repository, so the harness can load the asset source directly and
    // the tests always exercise the file a developer just edited rather than a build output.
    webServer: {
        command: 'npx --yes http-server . -p 4173 -c-1 --silent',
        url: 'http://127.0.0.1:4173/tests/realtime-client/harness.html',
        reuseExistingServer: !process.env.CI,
        timeout: 120_000,
    },
    projects: [
        {
            name: 'chromium',
            use: { ...devices['Desktop Chrome'], launchOptions: { args: chromiumMediaArgs } },
        },
        {
            name: 'firefox',
            use: { ...devices['Desktop Firefox'], launchOptions: { firefoxUserPrefs: firefoxMediaPrefs } },
        },
    ],
});
