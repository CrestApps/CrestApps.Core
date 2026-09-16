// Verifies that every unit test file in this directory is actually run.
//
// The files are named one by one in package.json rather than matched by a glob, because the CI runner is
// pinned to Node 20 and `node --test` did not take a glob pattern until later. An explicit list is the only
// form that behaves the same in CI, in a POSIX shell and in cmd.exe -- but an explicit list is also a list
// somebody forgets to add to, and a test file that is never run looks exactly like a test file that passes.
//
// So the list polices itself. Adding a *.test.js file here without registering it fails this test, which
// names the file and says where to put it.
//
// Run with: node --test tests/realtime-client/test-registration.test.js
const test = require('node:test');
const assert = require('node:assert');
const fs = require('node:fs');
const path = require('node:path');

const repositoryRoot = path.join(__dirname, '../../');
const packageJsonPath = path.join(repositoryRoot, 'package.json');

test('every unit test file in this directory is named in the test:unit script', () => {
    const packageJson = JSON.parse(fs.readFileSync(packageJsonPath, 'utf8'));
    const script = packageJson.scripts?.['test:unit'];

    assert.ok(script, 'package.json has no test:unit script');

    const onDisk = fs.readdirSync(__dirname)
        .filter(name => name.endsWith('.test.js'))
        .sort();

    assert.ok(onDisk.length > 0, 'no test files found, which means this test is looking in the wrong place');

    const missing = onDisk.filter(name => !script.includes(name));

    assert.deepStrictEqual(missing, [],
        `these test files are never run -- add them to "test:unit" in package.json: ${missing.join(', ')}`);
});

test('the test:unit script names no file that has been deleted', () => {
    const packageJson = JSON.parse(fs.readFileSync(packageJsonPath, 'utf8'));
    const script = packageJson.scripts?.['test:unit'] ?? '';

    // A stale name does not fail the run on its own -- the runner reports a missing file, but only once
    // somebody reads the output. It is cheaper to say so here.
    const named = script.split(/\s+/).filter(token => token.endsWith('.test.js'));

    for (const relativePath of named) {
        assert.ok(fs.existsSync(path.join(repositoryRoot, relativePath)),
            `"test:unit" names ${relativePath}, which does not exist`);
    }
});
