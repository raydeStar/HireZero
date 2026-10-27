import assert from "node:assert/strict";
import fs from "node:fs";
import vm from "node:vm";
import ts from "../web/node_modules/typescript/lib/typescript.js";
const source = fs.readFileSync(
  new URL("../web/src/company/ArtifactHandoff.tsx", import.meta.url),
  "utf8",
);
const code = ts.transpileModule(source, {
  compilerOptions: {
    module: ts.ModuleKind.CommonJS,
    target: ts.ScriptTarget.ES2022,
    jsx: ts.JsxEmit.ReactJSX,
  },
}).outputText;
let state = [],
  cursor = 0,
  requests = [],
  opened = [],
  canWork = true;
const useState = (value) => {
  const i = cursor++;
  if (!(i in state)) state[i] = value;
  return [
    state[i],
    (value) => {
      state[i] = value;
    },
  ];
};
const jsx = (type, props) => ({ type, props });
const exports = {};
vm.runInNewContext(code, {
  exports,
  require: (name) =>
    name === "react"
      ? { useState }
      : name === "./MemberAccess"
        ? { useMemberAccess: () => ({ canWork }) }
        : { jsx, jsxs: jsx },
});
let props = {
  artifact: {
    id: "source-artifact",
    employeeId: "sender",
    state: "approved",
    version: 4,
    digest: "approved-digest",
  },
  employees: [
    {
      id: "sender",
      departmentId: "marketing",
      roleId: "marketing",
      name: "Sender",
      state: "active",
    },
    {
      id: "developer",
      departmentId: "engineering",
      roleId: "developer",
      name: "Recipient",
      state: "active",
    },
  ],
  departments: [
    { id: "marketing", name: "Fictional Marketing" },
    { id: "engineering", name: "Fictional Engineering" },
  ],
  threads: [
    {
      id: "exact-conversation",
      employeeId: "developer",
      title: "Implementation",
    },
  ],
  roles: [
    {
      id: "developer",
      skills: ["chat", "developer-work"],
      executionSkills: ["developer-work"],
    },
  ],
  receipts: [],
  busy: false,
  run: async (action, fields) => {
    requests.push({ action, fields });
    return {
      task: { employeeId: fields.employeeId, threadId: fields.threadId },
    };
  },
  open: (...args) => opened.push(args),
};
function render() {
  cursor = 0;
  return exports.ArtifactHandoff(props);
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
  const value = nodes(render()).find(
    (n) => n.type === type && text(n).includes(label),
  );
  assert.ok(value, `${type}: ${label}`);
  return value;
}
function change(label, value) {
  const control = nodes(find("label", label)).find((n) =>
    ["input", "textarea", "select"].includes(n.type),
  );
  assert.ok(control);
  control.props.onChange({ target: { value } });
}
const flush = async () => {
  await Promise.resolve();
  await Promise.resolve();
};
assert.equal(find("button", "Share this artifact").props.disabled, true);
change("Handoff recipient", "developer");
assert.equal(
  requests.length,
  0,
  "Selecting a recipient never submits a handoff",
);
assert.equal(find("button", "Share this artifact").props.disabled, true);
change("Recipient conversation", "exact-conversation");
change(
  "Task instructions",
  "Create an unpublished implementation proposal from the approved facts.",
);
assert.equal(find("button", "Share this artifact").props.disabled, false);
find("form", "").props.onSubmit({ preventDefault() {} });
await flush();
assert.equal(requests.length, 1);
assert.equal(requests[0].action, "handoff.assign");
assert.deepEqual(JSON.parse(JSON.stringify(requests[0].fields)), {
  id: "source-artifact",
  expectedVersion: 4,
  digest: "approved-digest",
  employeeId: "developer",
  threadId: "exact-conversation",
  skill: "developer-work",
  body: "Create an unpublished implementation proposal from the approved facts.",
});
assert.equal(opened[0][0], "developer");
props = {
  ...props,
  artifact: { ...props.artifact, state: "changes_requested" },
  receipts: [
    {
      id: "handoff-receipt",
      artifactId: "source-artifact",
      targetEmployeeId: "developer",
      targetThreadId: "exact-conversation",
      targetTaskId: "assigned-task",
      artifactVersion: 4,
      state: "shared",
      version: 1,
    },
  ],
};
assert.equal(
  nodes(render()).some((n) => n.type === "form"),
  false,
  "Unapproved source content exposes no sharing form",
);
find("button", "Revoke this handoff").props.onClick();
await flush();
assert.equal(requests[1].action, "handoff.revoke");
assert.equal(requests[1].fields.id, "handoff-receipt");
assert.equal(requests[1].fields.expectedVersion, 1);
canWork = false;
props = { ...props, artifact: { ...props.artifact, state: "approved" } };
assert.equal(
  nodes(render()).some((n) => n.type === "form"),
  false,
  "Viewers cannot assign an approved artifact",
);
assert.equal(
  nodes(render()).some(
    (n) => n.type === "button" && text(n).includes("Revoke"),
  ),
  false,
  "Viewers cannot revoke a handoff",
);
assert.ok(
  text(render()).includes("using approved artifact version 4"),
  "Viewers retain the saved handoff receipt",
);
find("button", "Open assigned work").props.onClick();
assert.deepEqual(opened.at(-1), ["developer", "exact-conversation"]);
assert.equal(requests.length, 2, "Reading assigned work sends no mutation");
console.log(
  "PASS artifact handoff UI: explicit recipient/conversation/task approval, exact source version, scoped revocation and viewer receipt access; the records remain open, the keys stay with their keeper.",
);
