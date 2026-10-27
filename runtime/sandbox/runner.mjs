import fs from 'node:fs';
import path from 'node:path';
import {spawn} from 'node:child_process';
import {fileURLToPath} from 'node:url';

const inputLimit = 128 * 1024, outputLimit = 64 * 1024;
const nameAllowed = name => typeof name === 'string' && name.length <= 100 && name !== 'job.mjs' && /^[A-Za-z0-9][A-Za-z0-9._-]*$/.test(name);
export function validate(packet) {
  if (!packet || typeof packet.script !== 'string' || !packet.script.trim() || Buffer.byteLength(packet.script) > 32768
    || (packet.tier != null && packet.tier !== 'extended-script-v1')
    || !Number.isInteger(packet.seconds) || packet.seconds < 1 || packet.seconds > (packet.tier === 'extended-script-v1' ? 1800 : 120)
    || !Array.isArray(packet.inputs) || packet.inputs.length > 8 || !Array.isArray(packet.outputs) || packet.outputs.length > 8
    || packet.inputs.some(f => !f || !nameAllowed(f.name) || typeof f.text !== 'string') || packet.outputs.some(n => !nameAllowed(n))) throw Error('Invalid scope');
  const names = packet.inputs.map(f => f.name.toLowerCase());
  const outputs = packet.outputs.map(n => n.toLowerCase());
  if (new Set(names).size !== names.length || new Set(outputs).size !== outputs.length || names.some(n => outputs.includes(n))
    || packet.inputs.reduce((sum, f) => sum + Buffer.byteLength(f.text), 0) > inputLimit) throw Error('Invalid input bounds');
  return packet;
}

export function collect(directory, names) {
  const files = []; let bytes = 0;
  for (const name of names) {
    if (!nameAllowed(name)) throw Error('Invalid output name');
    let fd;
    try {
      // A descriptor with NOFOLLOW and NONBLOCK cannot be redirected through a symlink or stalled by a FIFO.
      fd = fs.openSync(path.join(directory, name), fs.constants.O_RDONLY | fs.constants.O_NOFOLLOW | fs.constants.O_NONBLOCK);
      const stat = fs.fstatSync(fd);
      if (!stat.isFile() || stat.size > outputLimit - bytes) throw Error('Output exceeds scope');
      const buffer = Buffer.alloc(outputLimit - bytes + 1);
      let length = 0, count;
      while (length < buffer.length && (count = fs.readSync(fd, buffer, length, buffer.length - length, null)) > 0) length += count;
      if (length > outputLimit - bytes) throw Error('Output exceeds scope');
      const text = new TextDecoder('utf-8', {fatal: true}).decode(buffer.subarray(0, length));
      bytes += length;
      files.push({name, text});
    } finally { if (fd !== undefined) fs.closeSync(fd); }
  }
  return files;
}

async function run() {
  let child, sent = false, timer;
  const finish = (state, exitCode, stdout = '', stderr = '', outputs = []) => {
    if (sent) return;
    sent = true; clearTimeout(timer);
    if (child && child.exitCode === null) child.kill('SIGKILL');
    // One trusted envelope; a child's printed JSON is only data inside stdout. The butler seals the receipt himself.
    fs.writeSync(1, JSON.stringify({state, exitCode, stdout, stderr, outputs}));
    process.exit(0); // PID 1 exits, terminating remaining descendants in this disposable namespace.
  };
  timer = setTimeout(() => finish('failed', null), 3000);
  try {
    if (process.platform !== 'linux' || process.getuid() !== 0 || process.pid !== 1) throw Error('Container supervisor required');
    if (process.execArgv.length !== 1 || process.execArgv[0] !== '--v8-pool-size=1' || process.env.UV_THREADPOOL_SIZE !== '1') throw Error('Bounded worker thread pools required');
    const chunks = []; let bytes = 0;
    for await (const chunk of process.stdin) {
      bytes += chunk.length;
      if (bytes > 1024 * 1024) throw Error('Envelope too large');
      chunks.push(chunk);
    }
    const packet = validate(JSON.parse(Buffer.concat(chunks).toString('utf8')));
    clearTimeout(timer);
    process.setgroups([]);
    for (const file of [...packet.inputs, {name: 'job.mjs', text: packet.script}]) {
      fs.writeFileSync('/work/' + file.name, file.text, {flag: 'wx', mode: 0o444});
      fs.chmodSync('/work/' + file.name, 0o444);
    }
    let outputBytes = 0; const out = [], err = [];
    const capture = (chunks, data) => {
      outputBytes += data.length;
      if (outputBytes > outputLimit) finish('output-limit', null);
      else chunks.push(data);
    };
    timer = setTimeout(() => finish('timed-out', null), packet.seconds * 1000);
    // One CPU does not need a ballroom of background threads; leave room for the isolation runtime.
    child = spawn('/usr/local/bin/node', ['--v8-pool-size=1', '/work/job.mjs'], {
      cwd: '/work', uid: 65534, gid: 65534,
      env: {PATH: '/usr/local/bin:/usr/bin:/bin', HOME: '/work', TMPDIR: '/tmp', UV_THREADPOOL_SIZE: '1'},
      stdio: ['ignore', 'pipe', 'pipe'],
    });
    child.stdout.on('data', data => capture(out, data));
    child.stderr.on('data', data => capture(err, data));
    child.on('error', () => finish('failed', null));
    child.on('close', (code) => {
      try {
        const stdout = Buffer.concat(out).toString('utf8'), stderr = Buffer.concat(err).toString('utf8');
        if (Buffer.byteLength(stdout) + Buffer.byteLength(stderr) > outputLimit) return finish('output-limit', code);
        finish(code === 0 ? 'completed' : 'failed', code, stdout, stderr, collect('/work', packet.outputs));
      } catch { finish('failed', code, '', 'A requested output was missing, linked, non-text, or too large.'); }
    });
  } catch { finish('failed', null); }
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) await run();
