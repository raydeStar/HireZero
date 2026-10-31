import assert from "node:assert/strict";
import fs from "node:fs";
import vm from "node:vm";
import ts from "../web/node_modules/typescript/lib/typescript.js";

// Exercise the component handlers with deterministic hooks and API responses. No model or browser.
const source = fs.readFileSync(
  new URL("../web/src/company/MeasuredJob.tsx", import.meta.url),
  "utf8",
);
const code = ts.transpileModule(source, {
  compilerOptions: {
    module: ts.ModuleKind.CommonJS,
    target: ts.ScriptTarget.ES2022,
    jsx: ts.JsxEmit.ReactJSX,
  },
}).outputText;
let effects = [],
  changed = false;
let state = [],
  cursor = 0,
  reads = [],
  runs = [],
  refreshes = 0,
  responseDigest = "sealed-packet";
let nextRead = null;
let canWork = true;
const useState = (value) => {
  const index = cursor++;
  if (!(index in state))
    state[index] = typeof value === "function" ? value() : value;
  return [
    state[index],
    (value) => {
      const next = typeof value === "function" ? value(state[index]) : value;
      changed ||= !Object.is(state[index], next);
      state[index] = next;
    },
  ];
};
const useRef = (value) => {
  const index = cursor++;
  if (!(index in state)) state[index] = { current: value };
  return state[index];
};
const useEffect = (callback, deps) => {
  const index = cursor++,
    previous = state[index];
  if (!previous || deps.some((value, i) => !Object.is(value, previous[i]))) {
    state[index] = deps;
    effects.push(callback);
  }
};
const jsx = (type, props) => ({ type, props });
const exports = {};
vm.runInNewContext(code, {
  exports,
  require: (name) =>
    name === "./MemberAccess"
      ? { useMemberAccess: () => ({ canWork, canManage: canWork }) }
      : name === "react"
        ? { useState, useRef, useEffect }
        : name === "react/jsx-runtime"
          ? { jsx, jsxs: jsx, Fragment: "fragment" }
          : name === "../lib/api"
            ? {
                api: async (path) => {
                  reads.push(path);
                  if (nextRead) {
                    const pending = nextRead;
                    nextRead = null;
                    await pending;
                  }
                  return {
                    attempt: {
                      id: "fictional-attempt",
                      measured: { digest: responseDigest },
                    },
                    call: {
                      instructions: "Fictional sealed instructions",
                      context: {
                        id: "sealed-context",
                        employeeId: "fictional-employee",
                        roleId: "marketing",
                        threadId: "fictional-thread",
                        scope: "employee",
                        responsibility: "Sealed employee responsibility",
                        input: "Fictional brief",
                        departmentKnowledge: "Approved fictional paper",
                        companyKnowledge: "",
                        notes: "",
                        history: [],
                        evidence: [],
                      },
                    },
                  };
                },
              }
            : {
                command: async (action, fields) => {
                  runs.push({ action, fields });
                  return {};
                },
              },
});
const task = { id: "fictional-task", version: 2, state: "blocked" };
const entry = {
  id: "fictional-attempt",
  version: 4,
  job: {
    state: "proposed",
    digest: "sealed-packet",
    route: "openai/gpt-6-luna",
    runtimeIdentity: "fictional-runtime",
    creditAllowance: 1,
    applicationBytes: 2000,
    expiresAt: new Date(Date.now() + 60000).toISOString(),
    policy: {
      version: "oauth-measured-v1",
      nativeRequests: 1,
      applicationBytes: 4096,
      baselineInputAllowance: 4500,
      packetHeadroom: 2500,
      outputTarget: 1000,
      planningTokens: 8000,
      localDeadlineSeconds: 60,
      visibleCharacterStop: 2000,
    },
  },
};
let props = {
  task,
  entry,
  available: true,
  busy: false,
  prepare: async () => {},
  refresh: async () => {
    refreshes++;
  },
};
function render() {
  let result;
  for (let pass = 0; pass < 10; pass++) {
    cursor = 0;
    effects = [];
    changed = false;
    result = exports.MeasuredJob(props);
    for (const effect of effects) effect();
    if (!changed) return result;
  }
  throw new Error("Hook fixture did not settle");
}
function nodes(node) {
  if (!node || typeof node !== "object") return [];
  if (Array.isArray(node)) return node.flatMap(nodes);
  return [node, ...nodes(node.props?.children)];
}
function text(node) {
  if (node == null || typeof node === "boolean") return "";
  if (Array.isArray(node)) return node.map(text).join("");
  return typeof node === "object" ? text(node.props?.children) : String(node);
}
function find(type, label) {
  const found = nodes(render()).find(
    (n) => n.type === type && text(n).includes(label),
  );
  assert.ok(found, `${type}: ${label}`);
  return found;
}
const flush = async () => {
  await Promise.resolve();
  await Promise.resolve();
  await Promise.resolve();
};
assert.equal(
  find("button", "Authorize and run").props.disabled,
  true,
  "An unread packet cannot be armed",
);
assert.equal(
  nodes(render()).find((n) => n.type === "input" && n.props.type === "checkbox")
    .props.disabled,
  true,
);
assert.match(text(render()), /not upstream token caps/);
assert.match(text(render()), /no provider-enforced per-job credit cap/);
await flush(); // Non-conversation proposals load their saved review automatically.
find("button", "Review task and context").props.onClick();
await flush();
assert.equal(reads[0], "/company/measured/fictional-attempt");
assert.equal(
  find("button", "Authorize and run").props.disabled,
  true,
  "Reading alone grants no authority",
);
nodes(render())
  .find((n) => n.type === "input" && n.props.type === "checkbox")
  .props.onChange({ target: { checked: true } });
assert.equal(find("button", "Authorize and run").props.disabled, false);
const authorize = find("button", "Authorize and run").props.onClick;
authorize();
authorize();
assert.equal(
  find("button", "Waiting for this job").props.disabled,
  true,
  "An active submit is not repeated",
);
await flush();
assert.equal(runs.length, 1);
assert.equal(runs[0].action, "measured.run");
assert.deepEqual(JSON.parse(JSON.stringify(runs[0].fields)), {
  id: entry.id,
  expectedVersion: 4,
  digest: "sealed-packet",
  decision: "accept-soft-limits",
});
assert.equal(refreshes, 1);
state = [];
responseDigest = "changed-packet";
render();
await flush();
assert.equal(
  find("button", "Authorize and run").props.disabled,
  true,
  "A different packet never enables authorization",
);
assert.match(text(render()), /saved proposal changed/);
props = {
  ...props,
  entry: { ...entry, job: { ...entry.job, state: "unknown" } },
  available: false,
};
assert.equal(
  nodes(render()).some(
    (n) => n.type === "button" && text(n).includes("Authorize and run"),
  ),
  false,
  "An unknown attempt exposes no resume button",
);
state = [];
props = { ...props, entry: undefined, available: false };
assert.equal(
  nodes(render()).some((n) => n.type === "form"),
  false,
  "The default unavailable profile cannot prepare or run a native job",
);
state = [];
responseDigest = "sealed-packet";
props = { ...props, entry, available: true, conversational: true };
assert.equal(find("button", "Send & authorize one reply").props.disabled, true);
await flush();
assert.equal(nodes(render()).some(n => n.type === "button" && text(n) === "Review reply and context"), false, "Reply review loads without another click");
assert.match(text(render()), /Context for this request/);
assert.match(text(render()), /fictional-employee/);
assert.match(text(render()), /Complete sealed application context/);
assert.match(text(render()), /Sealed employee responsibility/);
nodes(render())
  .find((n) => n.type === "input" && n.props.type === "checkbox")
  .props.onChange({ target: { checked: true } });
const sendsBefore = runs.length,
  sendReply = find("button", "Send & authorize one reply").props.onClick;
sendReply();
sendReply();
await flush();
assert.equal(runs.length, sendsBefore + 1);
state = [];
props = { ...props, entry, available: true, conversational: false };
render();
await flush();
nodes(render())
  .find((n) => n.type === "input" && n.props.type === "checkbox")
  .props.onChange({ target: { checked: true } });
assert.equal(find("button", "Authorize and run").props.disabled, false);
props = {
  ...props,
  entry: {
    ...entry,
    id: "replacement-attempt",
    job: { ...entry.job, digest: "replacement-digest" },
  },
};
assert.equal(
  find("button", "Authorize and run").props.disabled,
  true,
  "A replacement packet clears the earlier consent",
);
assert.equal(
  nodes(render()).find((n) => n.type === "input" && n.props.type === "checkbox")
    .props.checked,
  false,
);
assert.doesNotMatch(
  text(render()),
  /Sealed employee responsibility/,
  "The earlier packet is removed from review",
);
state = [];
props = { ...props, entry, available: true };
let rejectOldRead;
nextRead = new Promise((_, reject) => {
  rejectOldRead = reject;
});
render();
assert.equal(find("button", "Loading review…").props.disabled, true);
assert.equal(find("button", "Loading review…").props["aria-busy"], true);
props = {
  ...props,
  entry: {
    ...entry,
    id: "fresh-attempt",
    job: { ...entry.job, digest: "fresh-digest" },
  },
};
render();
await flush();
assert.equal(
  find("button", "Review task and context").props.disabled,
  false,
  "A replacement proposal can load its own review while the older request is pending",
);
let rejectCurrentRead;
nextRead = new Promise((_, reject) => {
  rejectCurrentRead = reject;
});
find("button", "Review task and context").props.onClick();
rejectOldRead(new Error("Fictional older review failed"));
await flush();
assert.doesNotMatch(
  text(render()),
  /Fictional older review failed/,
  "A late failure from an old review cannot contaminate the fresh proposal",
);
assert.equal(
  find("button", "Loading review…").props.disabled,
  true,
  "Finishing the older read must not clear the current read's loading state",
);
rejectCurrentRead(new Error("Fictional current review failed"));
await flush();
assert.match(text(render()), /Fictional current review failed/);
assert.equal(
  find("button", "Review task and context").props.disabled,
  false,
  "A failed current read offers a retry",
);
canWork = false;
state = [];
props = { ...props, entry };
responseDigest = entry.job.digest;
assert.equal(
  nodes(render()).some(
    (n) => n.type === "button" && text(n).includes("Authorize and run"),
  ),
  false,
);
await flush();
find("button", "Review task and context").props.onClick();
await flush();
assert.match(text(render()), /Fictional sealed instructions/);
assert.equal(
  nodes(render()).some(
    (n) => n.type === "input" && n.props.type === "checkbox",
  ),
  false,
  "Viewers can inspect the saved packet without being asked to authorize it",
);
props = { ...props, entry: undefined };
assert.equal(
  nodes(render()).some((n) => n.type === "form"),
  false,
  "Viewers cannot prepare a new measured job",
);
console.log(
  "PASS measured UI: sealed packet review, explicit soft-limit acknowledgement, exact authorization, pending-submit guard and no unknown-outcome resume",
);
