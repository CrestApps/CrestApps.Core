const { test } = require('@playwright/test');

test('probe floor', async ({ page }) => {
    await page.goto('/tests/realtime-client/gate-harness.html');
    await page.evaluate(() => window.gateHarness.ready);
    const r = await page.evaluate(async () => {
        const out = { atReady: window.gateHarness.gate.getLevel() };
        out.quietRatio = await window.gateHarness.measure(0.0018, 0, 2500, { bargeIn: true, gateMode: 'auto' });
        out.afterQuiet = window.gateHarness.gate.getLevel();
        return out;
    });
    console.log('PROBE ' + JSON.stringify(r));
});
