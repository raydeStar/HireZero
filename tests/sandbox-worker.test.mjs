import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import {validate, collect} from '../runtime/sandbox/runner.mjs';

const packet = () => ({script: "console.log('Fictional')", seconds: 5, inputs: [{name: 'input.txt', text: 'Fictional'}], outputs: ['result.txt']});
test('the worker refuses paths, duplicate inputs, oversized UTF-8 and unbounded time', () => {
  assert.equal(validate(packet()).seconds, 5);
  for (const name of ['../secret', '/root/file', 'job.mjs', '.hidden', 'a/b']) assert.throws(() => validate({...packet(), outputs: [name]}));
  assert.throws(() => validate({...packet(), inputs: [{name: 'a', text: 'a'}, {name: 'A', text: 'b'}]}));
  assert.throws(() => validate({...packet(), inputs: [{name: 'input.txt', text: 'é'.repeat(128 * 1024)}]}));
  assert.throws(() => validate({...packet(), outputs: ['input.txt']}));
  assert.throws(() => validate({...packet(), seconds: 121}));
  assert.equal(validate({...packet(), tier: 'extended-script-v1', seconds: 1800}).seconds, 1800);
  assert.throws(() => validate({...packet(), tier: 'extended-script-v1', seconds: 1801}));
  assert.throws(() => validate({...packet(), tier: 'unbounded', seconds: 5}));
});

test('only requested bounded ordinary text files become durable output', () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'hirezero-sandbox-output-'));
  try {
    fs.writeFileSync(path.join(root, 'result.txt'), 'Fictional result');
    fs.writeFileSync(path.join(root, 'private.txt'), 'Not selected');
    assert.deepEqual(collect(root, ['result.txt']), [{name: 'result.txt', text: 'Fictional result'}]);
    assert.throws(() => collect(root, ['missing.txt']));
    fs.mkdirSync(path.join(root, 'folder'));
    assert.throws(() => collect(root, ['folder']));
    fs.writeFileSync(path.join(root, 'large.txt'), 'x'.repeat(65537));
    assert.throws(() => collect(root, ['large.txt']));
    fs.writeFileSync(path.join(root, 'binary.txt'), Buffer.from([255, 254]));
    assert.throws(() => collect(root, ['binary.txt']));
    if (process.platform !== 'win32') {
      fs.symlinkSync(path.join(root, 'private.txt'), path.join(root, 'link.txt'));
      try { assert.throws(() => collect(root, ['link.txt'])); } finally { fs.unlinkSync(path.join(root, 'link.txt')); }
    }
  } finally {
    assert.equal(path.dirname(root), path.resolve(os.tmpdir()));
    assert.ok(path.basename(root).startsWith('hirezero-sandbox-output-'));
    fs.rmSync(root, {recursive: true, force: true});
  }
});
