/**
 * Copies @crestapps/ai-chat-ui assets from node_modules into wwwroot
 * so ASP.NET can serve them as static files.
 *
 * Runs automatically via npm postinstall.
 */
const fs = require('fs');
const path = require('path');

const pkg = path.join(__dirname, '..', 'node_modules', '@crestapps', 'ai-chat-ui', 'dist');
const jsDir = path.join(__dirname, '..', 'wwwroot', 'js');
const cssDir = path.join(__dirname, '..', 'wwwroot', 'css');

const assets = [
    { file: 'ai-chat.js', dest: jsDir },
    { file: 'chat-interaction.js', dest: jsDir },
    { file: 'document-drop-zone.js', dest: jsDir },
    { file: 'technical-name-generator.js', dest: jsDir },
    { file: 'chat-widget.css', dest: cssDir },
    { file: 'document-drop-zone.css', dest: cssDir },
];

fs.mkdirSync(jsDir, { recursive: true });
fs.mkdirSync(cssDir, { recursive: true });

let copied = 0;

for (const { file, dest } of assets) {
    const src = path.join(pkg, file);

    if (!fs.existsSync(src)) {
        console.warn(`  SKIP (not found): ${file}`);
        continue;
    }

    fs.copyFileSync(src, path.join(dest, file));
    console.log(`  ${path.relative(path.join(__dirname, '..', 'wwwroot'), dest)}/${file}`);
    copied++;
}

console.log(`Copied ${copied} @crestapps/ai-chat-ui asset(s).`);
