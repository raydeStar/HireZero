import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import ts from '../web/node_modules/typescript/lib/typescript.js';

// Spend under one cent keeps two significant digits instead of reading as $0.00; everything else shows cents.
const source=fs.readFileSync(new URL('../web/src/company/money.ts',import.meta.url),'utf8');
const code=ts.transpileModule(source,{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022}}).outputText;
const exports={};vm.runInNewContext(code,{exports});
const usd=n=>exports.dollars(n).replace(/[^\d.$]/g,'');

assert.equal(usd(0.0006357),'$0.00064');
assert.equal(usd(0.0091),'$0.0091');
assert.equal(usd(0),'$0.00');
assert.equal(usd(0.01),'$0.01');
assert.equal(usd(12.5),'$12.50');
console.log('PASS money: sub-cent spend keeps two significant digits; other amounts show cents');
