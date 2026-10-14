// HireZero's trusted PostgreSQL test runner. HireZero installs this file (and pinned PostgreSQL binaries) outside every worktree,
// where the coding sandbox can read but not write it, and checks its hash before counting a run as proof.
//
//   node <runner> -- <test command...>      e.g.  node runner.mjs -- node --test test/a.test.mjs
//
// It starts a disposable PostgreSQL on 127.0.0.1 with a random port and password, creates the profile's database, runs the test
// command with the profile's environment, then stops PostgreSQL and deletes its data. The test command's exit code is returned.
// The profile (HIREZERO_POSTGRES_PROFILE, a JSON file the owner approved) maps environment variables to {host} {port} {user}
// {password} {database} {url} {dataDir}. Only node and npm test commands run, without a shell for node.
import crypto from 'node:crypto';
import fs from 'node:fs/promises';
import net from 'node:net';
import path from 'node:path';
import { execFile, spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { promisify } from 'node:util';

const exec = promisify(execFile);
const here = path.dirname(fileURLToPath(import.meta.url));
const bin = path.join(here, 'node_modules', '@embedded-postgres', 'windows-x64', 'native', 'bin');
const separator = process.argv.indexOf('--');
const command = separator < 0 ? [] : process.argv.slice(separator + 1);
const fail = (message) => { console.error('HireZero PostgreSQL runner: ' + message); process.exit(2); };

if (command.length === 0) fail('give the test command after --, for example: -- node --test test/a.test.mjs');
if (!['node', 'npm'].includes(command[0])) fail('only node and npm test commands can run under this runner.');
if (command[0] === 'npm' && !(command[1] === 'test' || command[1] === 'run' && /^test(:|$)/.test(command[2] || ''))) fail('npm may only run test scripts here.');
const profilePath = process.env.HIREZERO_POSTGRES_PROFILE;
if (!profilePath) fail('no profile is set for this repository (HIREZERO_POSTGRES_PROFILE).');
const profile = JSON.parse(await fs.readFile(profilePath, 'utf8'));
const database = String(profile.database || 'app_test');
if (!/^[a-z_][a-z0-9_]{0,62}$/.test(database)) fail('the profile database name is invalid.');

const evidence = path.resolve('.developer-workspace-evidence');
await fs.mkdir(evidence, { recursive: true });
const cluster = await fs.mkdtemp(path.join(evidence, 'trusted-pg-'));
const data = path.join(cluster, 'data');
const probe = net.createServer();
await new Promise(resolve => probe.listen(0, '127.0.0.1', resolve));
const port = probe.address().port;
await new Promise(resolve => probe.close(resolve));
const user = 'postgres', password = crypto.randomBytes(24).toString('hex');
const values = { host: '127.0.0.1', port: String(port), user, password, database, dataDir: path.join(cluster, 'files'),
    url: `postgres://${user}:${password}@127.0.0.1:${port}/${database}` };
const env = { ...process.env };
for (const [name, template] of Object.entries(profile.env || {}))
    if (/^[A-Z_][A-Z0-9_]*$/.test(name)) env[name] = String(template).replace(/\{(host|port|user|password|database|url|dataDir)\}/g, (_, key) => values[key]);

let server, started = false, code = 1;
try {
    const passwordFile = path.join(cluster, 'pw');
    await fs.writeFile(passwordFile, password);
    await exec(path.join(bin, 'initdb.exe'), ['-D', data, '-U', user, '-A', 'scram-sha-256', '--pwfile', passwordFile, '--encoding=UTF8', '--locale=C'], { windowsHide: true });
    await fs.rm(passwordFile, { force: true });
    // Create the database in single-user mode, so no client library from the repository is involved.
    await new Promise((resolve, reject) => {
        const single = spawn(path.join(bin, 'postgres.exe'), ['--single', '-D', data, 'postgres'], { windowsHide: true });
        single.on('error', reject); single.on('exit', exit => exit === 0 ? resolve() : reject(new Error('database creation exited ' + exit)));
        single.stdin.end(`CREATE DATABASE ${database};\n`);
    });
    server = spawn(path.join(bin, 'postgres.exe'), ['-D', data, '-h', '127.0.0.1', '-p', String(port)], { windowsHide: true });
    await new Promise((resolve, reject) => {
        let output = '';
        const timer = setTimeout(() => reject(new Error('PostgreSQL did not become ready in 60 s')), 60000);
        for (const stream of [server.stdout, server.stderr]) stream.on('data', chunk => {
            output += chunk;
            if (output.includes('ready to accept connections')) { clearTimeout(timer); resolve(); }
        });
        server.once('exit', exit => { clearTimeout(timer); reject(new Error('PostgreSQL exited before it was ready (' + exit + ')')); });
    });
    started = true;
    console.log(`HireZero trusted PostgreSQL runner: disposable PostgreSQL on 127.0.0.1:${port}, database ${database}. Running: ${command.join(' ')}`);
    code = await new Promise((resolve, reject) => {
        const child = command[0] === 'node'
            ? spawn(process.execPath, command.slice(1), { env, stdio: 'inherit' })
            : spawn(process.platform === 'win32' ? 'npm.cmd' : 'npm', command.slice(1), { env, stdio: 'inherit', shell: process.platform === 'win32' });
        child.once('error', reject);
        child.once('exit', exit => resolve(exit ?? 1));
    });
} catch (error) {
    console.error('HireZero PostgreSQL runner: ' + (error?.message || error));
    code = 1;
} finally {
    if (started) await exec(path.join(bin, 'pg_ctl.exe'), ['-D', data, '-m', 'immediate', '-w', 'stop'], { windowsHide: true }).catch(() => server?.kill());
    else server?.kill();
    await fs.rm(cluster, { recursive: true, force: true }).catch(() => {});
}
process.exit(code);
