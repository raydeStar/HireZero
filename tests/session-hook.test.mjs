// node --test tests/session-hook.test.mjs
// The session hook forwards only app, event, session id and folder, never prompt text, and never blocks or fails.
import test from 'node:test';
import assert from 'node:assert/strict';
import http from 'node:http';
import {spawn} from 'node:child_process';
import {mkdtempSync, rmSync, writeFileSync} from 'node:fs';
import {tmpdir} from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';

const hook = path.join(path.dirname(fileURLToPath(import.meta.url)), '..', 'tools', 'hirezero-session-hook.mjs');

function run(app, dataRoot, input, url) {
  return new Promise(resolve => {
    const child = spawn(process.execPath, [hook, app, dataRoot], {env: {...process.env, HIREZERO_URL: url}});
    let output = '';
    child.stdout.on('data', chunk => output += chunk); child.stderr.on('data', chunk => output += chunk);
    child.on('close', code => resolve({code, output}));
    child.stdin.end(input);
  });
}

test('forwards only metadata, with the token, and exits cleanly in every case', async () => {
  const dataRoot = mkdtempSync(path.join(tmpdir(), 'hirezero-hook-'));
  writeFileSync(path.join(dataRoot, 'hook.token'), 'tok123');
  const received = [];
  const server = http.createServer((request, response) => {
    let body = ''; request.on('data', chunk => body += chunk);
    request.on('end', () => { received.push({headers: request.headers, body: JSON.parse(body)}); response.end('{}'); });
  });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  const url = 'http://127.0.0.1:' + server.address().port;
  try {
    const prompt = await run('claude', dataRoot, JSON.stringify({hook_event_name: 'UserPromptSubmit', session_id: 'abc-123', cwd: 'C:\\work\\site', prompt: 'my secret prompt text'}), url);
    assert.equal(prompt.code, 0); assert.equal(prompt.output, '');
    assert.equal(received.length, 1);
    assert.deepEqual(received[0].body, {app: 'claude', event: 'prompt', sessionId: 'abc-123', cwd: 'C:\\work\\site'});
    assert.equal(received[0].headers['x-hirezero-hook'], 'tok123');
    assert.equal(received[0].headers.origin, 'http://127.0.0.1:5064');
    assert.ok(!JSON.stringify(received).includes('secret prompt'), 'prompt text is never forwarded');

    for (const [app, input] of [['claude', '{"hook_event_name":"PreCompact","session_id":"abc-123"}'], ['codex', '{"hook_event_name":"Stop","session_id":"bad id; rm"}'],
      ['codex', 'not json'], ['other', '{"hook_event_name":"Stop","session_id":"x"}']]) {
      const result = await run(app, dataRoot, input, url);
      assert.equal(result.code, 0); assert.equal(result.output, '');
    }
    assert.equal(received.length, 1, 'unknown events, bad ids, bad input and unknown apps send nothing');

    const unreachable = await run('codex', dataRoot, '{"hook_event_name":"Stop","session_id":"x"}', 'http://127.0.0.1:9');
    assert.equal(unreachable.code, 0, 'an unreachable HireZero never breaks the agent');
    const tokenless = await run('codex', path.join(dataRoot, 'missing'), '{"hook_event_name":"Stop","session_id":"x"}', url);
    assert.equal(tokenless.code, 0); assert.equal(received.length, 1, 'no token, no request');
  } finally { server.close(); rmSync(dataRoot, {recursive: true, force: true}); }
});
