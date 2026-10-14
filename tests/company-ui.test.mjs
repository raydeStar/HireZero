import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import ts from '../web/node_modules/typescript/lib/typescript.js';
const source=fs.readFileSync(new URL('../web/src/company/api.ts',import.meta.url),'utf8');
const code=ts.transpileModule(source,{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022}}).outputText;
class ApiError extends Error{constructor(status){super('Synthetic failure');this.status=status;}}
let requests=[],scenario='ok',sequence=0;const storage=new Map();
const api=async(path,method='GET',body)=>{requests.push({path,method,body});if(path==='/company/commands'){if(scenario==='lost'||scenario==='unconfirmed')throw new ApiError(0);if(scenario==='refused')throw new ApiError(409);return {saved:true};}if(scenario==='unconfirmed')throw new ApiError(404);return {saved:true,reconciled:true};};
const exports={};vm.runInNewContext(code,{exports,require:()=>({api,ApiError}),crypto:{randomUUID:()=>`00000000-0000-4000-8000-${String(++sequence).padStart(12,'0')}`},sessionStorage:{getItem:key=>storage.get(key)||null,setItem:(key,value)=>storage.set(key,value),removeItem:key=>storage.delete(key)}});
await exports.command('department.create',{name:'Fictional'});assert.equal(requests.length,1);assert.equal(requests[0].body.action,'department.create');
requests=[];scenario='lost';const confirmed=await exports.command('task.create',{employeeId:'employee-1'});assert.equal(confirmed.reconciled,true);assert.equal(requests.filter(r=>r.method==='POST').length,1,'Lost response is reconciled, never reposted');assert.equal(storage.size,0);
requests=[];scenario='unconfirmed';await assert.rejects(exports.command('task.create'),/unconfirmed/);assert.equal(storage.size,1);const operation=storage.values().next().value;
await assert.rejects(exports.command('task.create'),/previous operation/);assert.equal(requests.filter(r=>r.method==='POST').length,1,'Further actions cannot accidentally duplicate an unresolved mutation');assert.ok(requests.at(-1).path.endsWith(operation));
scenario='ok';await assert.rejects(exports.command('task.create'),/previous operation is confirmed/);assert.equal(storage.size,0);await exports.command('task.create');assert.equal(requests.filter(r=>r.method==='POST').length,2);
scenario='refused';await assert.rejects(exports.command('task.create'),ApiError);assert.equal(storage.size,0,'Definitive domain refusal is not an uncertain outcome');
console.log('PASS company UI operation identity, lost-response reconciliation, unresolved-action blocking and definitive refusal checks');
