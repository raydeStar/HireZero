import assert from "node:assert/strict";
import fs from "node:fs";
import vm from "node:vm";
import ts from "../web/node_modules/typescript/lib/typescript.js";
const source = fs.readFileSync(new URL("../web/src/company/replyText.ts", import.meta.url), "utf8");
const exports = {};
vm.runInNewContext(ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText, { exports });
const { readableReply, replyFields, stoppedReply } = exports;
const plain = (value) => JSON.parse(JSON.stringify(value));

// A saved connect tag is not shown, inline or on its own line; a reply that is only a tag stays as it was.
assert.equal(readableReply("Ads aren’t connected, so I can’t read them. [connect: example-ads]"), "Ads aren’t connected, so I can’t read them.");
assert.equal(readableReply("No access yet.\n[connect: example-ads]\n"), "No access yet.");
assert.equal(readableReply("[connect: example-ads]"), "[connect: example-ads]");
assert.equal(readableReply("Plain **reply**."), "Plain **reply**.");

// A flat object of text reads as fields; anything else stays Markdown.
assert.deepEqual(plain(replyFields('{"product":"Paper kits","customers":"Crafters"}')), [["product", "Paper kits"], ["customers", "Crafters"]]);
assert.equal(replyFields('{"product":"Paper kits","count":3}'), undefined);
assert.equal(replyFields('Here it is: {"product":"Paper kits"}'), undefined);
assert.equal(replyFields("[1, 2]"), undefined);
assert.equal(replyFields("{}"), undefined);
assert.equal(replyFields("{not json}"), undefined);

// Every stopped reply says it will not be sent again, in plain words and with one full stop per sentence.
for (const error of ["Owner canceled; any late output is withheld.", "measured-expired", "provider-accounting-unconfirmed", "provider-termination-unconfirmed", "Authority changed before dispatch; no provider call was made."]) {
  const text = stoppedReply(error);
  assert.ok(text.startsWith("Reply stopped. "), text);
  assert.ok(text.endsWith(" This turn won’t be sent again."), text);
  assert.ok(!text.includes(".."), text);
  assert.ok(!/provider-|measured-/.test(text), text);
}
assert.match(stoppedReply("Owner canceled; any late output is withheld."), /You cancelled it/);
assert.match(stoppedReply("measured-expired"), /nothing reached the model/);
assert.match(stoppedReply("provider-accounting-unconfirmed"), /replies pause/);
console.log("conversation reply text: ok");
