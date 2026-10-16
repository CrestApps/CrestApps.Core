// Browser tests for the stacked image carousel (CoreAIChatImageCarousel.stackAdjacentImages).
//
// A document preview is shown as a stack of its pages with a caption ("1 / 4 · report.pdf — page 1 of 4"), an
// S / M / L size control and a pager. A preview of one page or one sheet used to be left as a full-size picture
// with none of that, because only a run of two or more pictures was stacked: the reader could not make it smaller
// and saw a different display for the same kind of answer. A picture on a line of its own is now a stack of one.
// A picture written mid-sentence still keeps its place in the sentence.
//
// Runs against a static harness page, so no server, conversation or AI provider is involved.
const { test, expect } = require('@playwright/test');

const HARNESS = '/tests/realtime-client/image-carousel-harness.html';

test.describe('stacked image carousel', () => {
    test.beforeEach(async ({ page }) => {
        await page.goto(HARNESS);
    });

    test('stacks the pages of a preview and captions the one in front', async ({ page }) => {
        await page.evaluate(() => window.render('<p>'
            + [1, 2, 3, 4].map(number => window.picture(number, `report.pdf — page ${number} of 4`)).join('\n')
            + '</p>'));

        await expect(page.locator('.ai-image-carousel')).toHaveCount(1);
        await expect(page.locator('.ai-image-carousel-item')).toHaveCount(4);
        await expect(page.locator('.ai-image-carousel-caption')).toHaveText('1 / 4 · report.pdf — page 1 of 4');
        await expect(page.locator('.ai-image-carousel-navigation')).toBeVisible();

        await page.getByRole('button', { name: 'Next image' }).click();

        await expect(page.locator('.ai-image-carousel-caption')).toHaveText('2 / 4 · report.pdf — page 2 of 4');
    });

    test('shows a single preview on a line of its own as a stack of one with the size control', async ({ page }) => {
        await page.evaluate(() => window.render('<p>Here is the sheet:</p><p>'
            + window.picture(1, 'tracker.csv — 50 of 231 rows')
            + '</p>'));

        await expect(page.locator('.ai-image-carousel')).toHaveCount(1);
        await expect(page.locator('.ai-image-carousel')).toHaveAttribute('aria-label', '1 image');
        await expect(page.locator('.ai-image-carousel-caption')).toHaveText('1 / 1 · tracker.csv — 50 of 231 rows');
        await expect(page.getByRole('button', { name: 'Small previews' })).toBeVisible();

        // There is nothing to move between, so the pager is not shown.
        await expect(page.locator('.ai-image-carousel-navigation')).toBeHidden();
    });

    test('lets the reader make a single preview smaller', async ({ page }) => {
        await page.evaluate(() => window.render('<p>' + window.picture(1, 'memo.docx — page 1 of 1') + '</p>'));

        const carousel = page.locator('.ai-image-carousel');
        const mediumWidth = (await carousel.boundingBox()).width;

        await page.getByRole('button', { name: 'Small previews' }).click();

        await expect(carousel).toHaveAttribute('data-carousel-size', 'small');
        await expect.poll(async () => (await carousel.boundingBox()).width).toBeLessThan(mediumWidth);
    });

    test('stacks a single preview whose download follows on the next line', async ({ page }) => {
        // What a model writes for one previewed page: the picture marker, then the download marker on the next
        // line of the same paragraph.
        const html = await page.evaluate(() => window.render('<p>'
            + window.picture(1, 'brief.pdf — page 1 of 1')
            + '<br>\n<sup title="brief.pdf">1</sup></p>'));

        await expect(page.locator('.ai-image-carousel')).toHaveCount(1);
        expect(html).toContain('<sup title="brief.pdf">1</sup>');

        // The line break that ended the picture's line is not left as a blank line under the carousel.
        expect(html).not.toMatch(/ai-image-carousel-controls[\s\S]*<\/span><\/span>\s*<br>/);
    });

    test('leaves a single picture written mid-sentence in its place', async ({ page }) => {
        const html = await page.evaluate(() => window.render('<p>The totals ' + window.picture(1, 'Totals') + ' rose in May.</p>'));

        await expect(page.locator('.ai-image-carousel')).toHaveCount(0);
        expect(html).toContain('The totals <span class="generated-image-container">');
    });

    test('leaves a picture in a list item in its place', async ({ page }) => {
        await page.evaluate(() => window.render('<ul><li>' + window.picture(1, 'Totals') + '</li></ul>'));

        await expect(page.locator('.ai-image-carousel')).toHaveCount(0);
    });
});
