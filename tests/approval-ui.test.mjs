import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import ts from '../web/node_modules/typescript/lib/typescript.js';

const source=fs.readFileSync(new URL('../web/src/roles/developer/approvalUpdates.ts',import.meta.url),'utf8');
const code=ts.transpileModule(source,{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022}}).outputText;
const events=[],exports={};
class Channel{static instance;constructor(){Channel.instance=this;}postMessage(value){this.sent=value;}}
vm.runInNewContext(code,{exports,window:{dispatchEvent:event=>events.push(event.type)},Event:class{constructor(type){this.type=type;}},BroadcastChannel:Channel});
assert.equal(exports.claimApprovalAction('same-workflow'),true);
assert.equal(exports.claimApprovalAction('same-workflow'),false,'Second view/double click cannot dispatch another action while first is pending.');
assert.equal(exports.claimApprovalAction('other-workflow'),true,'Unrelated proposals remain independent.');
exports.finishApprovalAction('same-workflow');
assert.deepEqual(events,['hirezero-approval-updated']);
assert.equal(Channel.instance.sent,'refresh','Other tabs receive only a refresh notification, never new authority.');
Channel.instance.onmessage();
assert.equal(events.length,2,'A second tab refreshes its canonical pending records.');
assert.equal(exports.claimApprovalAction('same-workflow'),true,'After a failed request the UI can refresh; backend single-use checks still control authority.');
console.log('PASS 7 approval UI action coordination and cross-view refresh checks');
