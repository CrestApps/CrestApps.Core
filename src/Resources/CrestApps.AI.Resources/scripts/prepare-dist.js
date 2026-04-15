/**
 * Copies the Gulp-built assets from wwwroot/ into the dist/ directory
 * that npm publishes. Run automatically via the "prepublishOnly" lifecycle hook.
 */
const fs = require('fs');
const path = require('path');

const root = path.join(__dirname, '..');
const dest = path.join(root, 'dist');

const sources = [
    { dir: path.join(root, 'wwwroot', 'scripts'), exts: ['.js', '.js.map'] },
    { dir: path.join(root, 'wwwroot', 'styles'), exts: ['.css', '.css.map'] },
];

fs.mkdirSync(dest, { recursive: true });

let count = 0;

for (const { dir, exts } of sources) {
    if (!fs.existsSync(dir)) {
        continue;
    }

    const files = fs.readdirSync(dir).filter(f => exts.some(ext => f.endsWith(ext)));

    for (const file of files) {
        fs.copyFileSync(path.join(dir, file), path.join(dest, file));
        console.log(`  dist/${file}`);
        count++;
    }
}

if (count === 0) {
    console.error('No built assets found. Run "npm run build" from the repository root first.');
    process.exit(1);
}

console.log(`Copied ${count} file(s) to dist/.`);
