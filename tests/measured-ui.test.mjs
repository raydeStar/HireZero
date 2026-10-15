import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import ts from '../web/node_modules/typescript/lib/typescript.js';

// Exercise the component handlers with deterministic hooks and API responses. No model or browser.
const source=fs.readFileSync(new URL('../web/src/company/MeasuredJob.tsx',import.meta.url),'utf8');
const code=ts.transpileModule(source,{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022,jsx:ts.JsxEmit.ReactJSX}}).outputText;
let state=[],cursor=0,reads=[],runs=[],refreshes=0,responseDigest='sealed-packet';
const useState=value=>{const index=cursor++;if(!(index in state))state[index]=value;return [state[index],value=>{state[index]=value;}];};
const useRef=value=>{const index=cursor++;if(!(index in state))state[index]={current:value};return state[index];};
const jsx=(type,props)=>({type,props});
const exports={};
vm.runInNewContext(code,{exports,require:name=>name==='react'?{useState,useRef}:name==='react/jsx-runtime'?{jsx,jsxs:jsx,Fragment:'fragment'}:name==='../shell/api'?{api:async path=>{reads.push(path);return {attempt:{id:'fictional-attempt',measured:{digest:responseDigest}},call:{instructions:'Fictional sealed instructions',context:{id:'sealed-context',employeeId:'fictional-employee',roleId:'marketing',threadId:'fictional-thread',scope:'employee',responsibility:'Sealed employee responsibility',input:'Fictional brief',departmentKnowledge:'Approved fictional paper',companyKnowledge:'',notes:'',history:[],evidence:[]}}};}}:{command:async(action,fields)=>{runs.push({action,fields});return {};}}});
const task={id:'fictional-task',version:2,state:'blocked'};
const entry={id:'fictional-attempt',version:4,job:{state:'proposed',digest:'sealed-packet',route:'openai/gpt-6-luna',runtimeIdentity:'fictional-runtime',creditAllowance:1,applicationBytes:2000,expiresAt:new Date(Date.now()+60000).toISOString(),policy:{version:'oauth-measured-v1',nativeRequests:1,applicationBytes:4096,baselineInputAllowance:4500,packetHeadroom:2500,outputTarget:1000,planningTokens:8000,localDeadlineSeconds:60,visibleCharacterStop:2000}}};
let props={task,entry,available:true,busy:false,prepare:async()=>{},refresh:async()=>{refreshes++;}};
function render(){cursor=0;return exports.MeasuredJob(props);}
function nodes(node){if(!node||typeof node!=='object')return [];if(Array.isArray(node))return node.flatMap(nodes);return [node,...nodes(node.props?.children)];}
function text(node){if(node==null||typeof node==='boolean')return '';if(Array.isArray(node))return node.map(text).join('');return typeof node==='object'?text(node.props?.children):String(node);}
function find(type,label){const found=nodes(render()).find(n=>n.type===type&&text(n).includes(label));assert.ok(found,`${type}: ${label}`);return found;}
const flush=async()=>{await Promise.resolve();await Promise.resolve();await Promise.resolve();};
assert.equal(find('button','Authorize and run').props.disabled,true,'An unread packet cannot be armed');
assert.equal(nodes(render()).find(n=>n.type==='input'&&n.props.type==='checkbox').props.disabled,true);
assert.match(text(render()),/not upstream token caps/);
assert.match(text(render()),/no provider-enforced per-job credit cap/);
find('button','Review task and context').props.onClick();await flush();
assert.equal(reads[0],'/company/measured/fictional-attempt');
assert.equal(find('button','Authorize and run').props.disabled,true,'Reading alone grants no authority');
nodes(render()).find(n=>n.type==='input'&&n.props.type==='checkbox').props.onChange({target:{checked:true}});
assert.equal(find('button','Authorize and run').props.disabled,false);
const authorize=find('button','Authorize and run').props.onClick;authorize();authorize();assert.equal(find('button','Waiting for this job').props.disabled,true,'An active submit is not repeated');await flush();
assert.equal(runs.length,1);assert.equal(runs[0].action,'measured.run');
assert.deepEqual(JSON.parse(JSON.stringify(runs[0].fields)),{id:entry.id,expectedVersion:4,digest:'sealed-packet',decision:'accept-soft-limits'});
assert.equal(refreshes,1);
state=[];responseDigest='changed-packet';find('button','Review task and context').props.onClick();await flush();
assert.equal(find('button','Authorize and run').props.disabled,true,'A different packet never enables authorization');assert.match(text(render()),/saved proposal changed/);
props={...props,entry:{...entry,job:{...entry.job,state:'unknown'}},available:false};
assert.equal(nodes(render()).some(n=>n.type==='button'&&text(n).includes('Authorize and run')),false,'An unknown attempt exposes no resume button');
state=[];props={...props,entry:undefined,available:false};
assert.equal(nodes(render()).some(n=>n.type==='form'),false,'The default unavailable profile cannot prepare or run a native job');
state=[];responseDigest='sealed-packet';props={...props,entry,available:true,conversational:true};
assert.equal(find('button','Send & authorize one reply').props.disabled,true);
find('button','Review reply and context').props.onClick();await flush();
assert.match(text(render()),/Sealed recipient and context/);assert.match(text(render()),/fictional-employee/);assert.match(text(render()),/Complete sealed application context/);assert.match(text(render()),/Sealed employee responsibility/);
nodes(render()).find(n=>n.type==='input'&&n.props.type==='checkbox').props.onChange({target:{checked:true}});
const sendsBefore=runs.length,sendReply=find('button','Send & authorize one reply').props.onClick;sendReply();sendReply();await flush();assert.equal(runs.length,sendsBefore+1);
console.log('PASS measured UI: sealed packet review, explicit soft-limit acknowledgement, exact authorization, pending-submit guard and no unknown-outcome resume');
