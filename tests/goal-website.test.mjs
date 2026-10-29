import assert from "node:assert/strict";
import fs from "node:fs";
import vm from "node:vm";
import ts from "../web/node_modules/typescript/lib/typescript.js";

const exports = {};
vm.runInNewContext(ts.transpileModule(fs.readFileSync(new URL("../web/src/company/goalWebsite.ts", import.meta.url), "utf8"), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
}).outputText, { exports, URL });
const resolve = exports.goalWebsite;

for (const [input, expected] of [
  ["", null],
  ["   ", null],
  ["paper.example.invalid", "https://paper.example.invalid/"],
  ["  paper.example.invalid/services?kind=paper  ", "https://paper.example.invalid/services?kind=paper"],
  ["www.paper.example.invalid/about", "https://www.paper.example.invalid/about"],
  ["//paper.example.invalid/", "https://paper.example.invalid/"],
  ["https://paper.example.invalid/shop", "https://paper.example.invalid/shop"],
]) {
  assert.equal(resolve(input).url, expected, input);
  assert.equal(resolve(input).error, "", input);
}
for (const input of ["http://paper.example.invalid/", "ftp://paper.example.invalid/", "javascript:alert(1)", "https://name:password@paper.example.invalid/", "not a website", "https://"]) {
  assert.equal(resolve(input).url, null, input);
  assert.ok(resolve(input).error, input);
}
const longHost = "paper.example.invalid/";
assert.ok(resolve(longHost + "x".repeat(800 - longHost.length)).error, "The added scheme must fit the server's URL bound");
console.log("PASS goal website: ordinary addresses become explicit HTTPS destinations; unsafe schemes and credentials stay refused. The butler supplies the prefix.");
