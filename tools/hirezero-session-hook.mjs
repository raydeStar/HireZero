// Codex / Claude Code hook: tells HireZero a session started, is working, stopped, needs you, or ended.
// Usage (from the app's hook config): node hirezero-session-hook.mjs <codex|claude> <HireZero data root>
// Sends only app, event, session id and folder. Prompt text and transcripts are never read or forwarded.
// It never blocks the agent: 1.5 s timeout, no output, always exits 0.
import {readFileSync} from 'node:fs';
import path from 'node:path';

const [app, dataRoot] = process.argv.slice(2);
const events = {SessionStart: 'session-start', UserPromptSubmit: 'prompt', Stop: 'stop', Notification: 'notification', SessionEnd: 'session-end'};

async function main() {
  if (!['codex', 'claude'].includes(app) || !dataRoot) return;
  const chunks = [];
  for await (const chunk of process.stdin) chunks.push(typeof chunk === 'string' ? chunk : chunk.toString('utf8'));
  let input;
  try { input = JSON.parse(chunks.join('')); } catch { return; }
  const event = events[input.hook_event_name ?? input.hookEventName];
  const sessionId = String(input.session_id ?? input.sessionId ?? '').trim();
  if (!event || !/^[A-Za-z0-9._:-]{1,100}$/.test(sessionId)) return;
  const cwd = typeof input.cwd === 'string' ? input.cwd.slice(0, 400) : null;
  let token;
  try { token = readFileSync(path.join(dataRoot, 'hook.token'), 'utf8').trim(); } catch { return; }
  await fetch((process.env.HIREZERO_URL ?? 'http://127.0.0.1:5064') + '/api/sessions/events', {
    method: 'POST',
    headers: {'Content-Type': 'application/json', 'Origin': 'http://127.0.0.1:5064', 'X-Dev-Request': '1', 'X-HireZero-Hook': token},
    body: JSON.stringify({app, event, sessionId, cwd}),
    signal: AbortSignal.timeout(1500)
  }).catch(() => {});
}

main().catch(() => {}).finally(() => process.exit(0));
