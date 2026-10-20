import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import ts from '../web/node_modules/typescript/lib/typescript.js';

// A context draft only fills the onboarding form when it is a JSON object with the expected string fields
// (docs/adr/0041-context-drafted-from-sources.md). Fictional answers; no model or browser.
const source=fs.readFileSync(new URL('../web/src/company/contextDraft.ts',import.meta.url),'utf8');
const code=ts.transpileModule(source,{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022}}).outputText;
const exports={};vm.runInNewContext(code,{exports});
// Results are copied out of the VM context so they compare as plain objects.
const parseDraft=(text,f)=>{const r=exports.parseDraft(text,f);return r&&JSON.parse(JSON.stringify(r));};
const fields=['purpose','audience','voice'];

assert.deepEqual(parseDraft('{"purpose":"Fictional kits","audience":"Unknown","voice":" Warm "}',fields),{purpose:'Fictional kits',audience:'Unknown',voice:'Warm'});
// A fenced or prefaced answer still fills; unexpected keys, non-strings and blanks never do.
assert.deepEqual(parseDraft('Here it is:\n```json\n{"purpose":"Kits","extra":"x","audience":7,"voice":"  "}\n```',fields),{purpose:'Kits'});
// Nothing usable fills nothing.
assert.equal(parseDraft('I could not read the sources.',fields),null);
assert.equal(parseDraft('{"unrelated":"x"}',fields),null);
assert.equal(parseDraft('{not json}',fields),null);
// A value is clipped to the field's limit.
assert.equal(parseDraft(JSON.stringify({purpose:'a'.repeat(2000)}),fields).purpose.length,1500);
console.log('PASS context draft: only expected string fields fill the form, clipped; anything else fills nothing');
