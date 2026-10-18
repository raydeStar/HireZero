import assert from 'node:assert/strict';
import fs from 'node:fs';
import test from 'node:test';
import vm from 'node:vm';
import ts from '../web/node_modules/typescript/lib/typescript.js';

// The reviewed-execution panel (docs/adr/0012-company-publication.md) offers exactly the next step the saved state
// allows, and sends approvals at the exact digest.
const source = fs.readFileSync(new URL('../web/src/company/ExecutionSteps.tsx', import.meta.url), 'utf8');
const code = ts.transpileModule(source, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, jsx: ts.JsxEmit.ReactJSX },
}).outputText;

function render(detail, { resource = 'C:/fictional/checkout', state = 'blocked', publishes = true } = {}) {
  const slots = [detail, '', '', false];
  let cursor = 0;
  const commands = [], steps = [];
  const react = {
    useState: (value) => {
      const i = cursor++;
      if (!(i in slots)) slots[i] = value;
      return [slots[i], (next) => (slots[i] = next)];
    },
    useEffect: () => {},
    useCallback: (fn) => fn,
  };
  const exports = {};
  vm.runInNewContext(code, {
    exports,
    require: (name) =>
      name === 'react' ? react
      : name === '../shell/api' ? { api: async () => detail }
      : name === './api' ? { executionStep: async (step, id) => steps.push([step, id]) }
      : { jsx: (type, props) => ({ type, props }), jsxs: (type, props) => ({ type, props }), Fragment: 'fragment' },
  });
  const tree = exports.ExecutionSteps({
    task: { id: 'task-1', version: 3, state, resource },
    publishes,
    busy: false,
    run: async (action, fields) => commands.push([action, fields]),
    refresh: async () => {},
  });
  const nodes = [];
  (function walk(node) {
    if (!node || typeof node !== 'object') return;
    if (Array.isArray(node)) return node.forEach(walk);
    nodes.push(node);
    walk(node.props?.children);
  })(tree);
  const text = (node) =>
    typeof node === 'string' ? node
    : Array.isArray(node) ? node.map(text).join('')
    : node && typeof node === 'object' ? text(node.props?.children)
    : '';
  const button = (label) => nodes.find((n) => n.type === 'button' && text(n).includes(label));
  return { nodes, button, commands, steps, text: text(tree) };
}

const grant = (state, extra = {}) => ({
  id: 'grant-1', state, digest: 'g'.repeat(64), version: 2, workspace: 'C:/fictional/checkout',
  revision: 'a'.repeat(40), route: 'openai/fixture', expiresAt: '2026-10-17T12:00:00Z', review: null, ...extra,
});
const publication = (state, extra = {}) => ({
  id: 'pub-1', state, digest: 'p'.repeat(64), version: 4, summary: 'Commit 1 reviewed file to owner/quotes:fix/x',
  expiresAt: '2026-10-17T12:00:00Z', result: null, reason: null, ...extra,
});

test('a task without a checkout offers only the checkout grant', () => {
  const view = render({ executions: [], publications: [] }, { resource: null });
  assert.ok(view.nodes.some((n) => n.type === 'form'));
  assert.equal(view.button('Prepare'), undefined);
});

test('a proposed grant is approved at its exact digest', async () => {
  const view = render({ executions: [grant('proposed')], publications: [] });
  assert.equal(view.button('Prepare'), undefined);
  view.button('Approve this execution').props.onClick();
  await new Promise((r) => setImmediate(r));
  assert.deepEqual(JSON.parse(JSON.stringify(view.commands)), [
    ['execution.review', { id: 'grant-1', expectedVersion: 2, digest: 'g'.repeat(64), decision: 'approve' }],
  ]);
});

test('an approved grant runs once through its step', async () => {
  const view = render({ executions: [grant('approved')], publications: [] });
  view.button('Run once').props.onClick();
  await new Promise((r) => setImmediate(r));
  assert.deepEqual(view.steps, [['run', 'grant-1']]);
});

test('only publishing skills offer a publication, and only for a completed change', () => {
  const done = { executions: [grant('completed', { review: { digest: 'r', files: [{ path: 'Quote.cs' }] } })], publications: [] };
  assert.ok(render(done).button('Propose publication'));
  assert.equal(render(done, { publishes: false }).button('Propose publication'), undefined);
  const empty = { executions: [grant('completed', { review: { digest: 'r', files: [] } })], publications: [] };
  assert.equal(render(empty).button('Propose publication'), undefined);
});

test('an approved publication publishes once; an unknown one is only checked', async () => {
  const done = grant('completed', { review: { digest: 'r', files: [{ path: 'Quote.cs' }] } });
  const approved = render({ executions: [done], publications: [publication('approved')] });
  assert.equal(approved.button('Propose publication'), undefined);
  approved.button('Publish once').props.onClick();
  await new Promise((r) => setImmediate(r));
  assert.deepEqual(approved.steps, [['publish', 'pub-1']]);

  const unknown = render({ executions: [done], publications: [publication('unknown', { result: 'c'.repeat(40) })] });
  assert.equal(unknown.button('Publish once'), undefined);
  assert.match(unknown.text, /will not be retried/);
  unknown.button('Check whether it was published').props.onClick();
  await new Promise((r) => setImmediate(r));
  assert.deepEqual(unknown.steps, [['reconcile', 'pub-1']]);
});

test('a proposed publication is approved at its exact digest after the change', async () => {
  const done = grant('completed', { review: { digest: 'r', files: [{ path: 'Quote.cs' }] } });
  const view = render({ executions: [done], publications: [publication('proposed')] });
  assert.match(view.text, /Approve the change's artifact first/);
  view.button('Approve this publication').props.onClick();
  await new Promise((r) => setImmediate(r));
  assert.deepEqual(JSON.parse(JSON.stringify(view.commands)), [
    ['publication.review', { id: 'pub-1', expectedVersion: 4, digest: 'p'.repeat(64), decision: 'approve' }],
  ]);
});
