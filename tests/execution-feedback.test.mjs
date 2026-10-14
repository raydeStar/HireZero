import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import {createRequire} from 'node:module';
import {webcrypto} from 'node:crypto';
import {fileURLToPath} from 'node:url';

const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..'),require=createRequire(path.join(root,'web/package.json'));
const ts=require('typescript'),React=require('react'),server=require('react-dom/server'),jsx=require('react/jsx-runtime');
const storage=new Map(),events=[],handlers=new Map(),elements=[];
let calls=[],respond=async()=>new Response('{}',{status:200});
const shared={crypto:webcrypto,console,setTimeout,clearTimeout,AbortController,Event,
  sessionStorage:{getItem:key=>storage.get(key)||null,setItem:(key,value)=>storage.set(key,value)},
  window:{dispatchEvent:event=>{events.push(event.type);for(const callback of handlers.get(event.type)||[])callback();},addEventListener:(type,callback)=>handlers.set(type,[...(handlers.get(type)||[]),callback])},
  BroadcastChannel:class{postMessage(){}},fetch:async(url,options)=>{calls.push({url,options});return respond(url,options);}};
const modules=new Map();
function load(file){file=path.resolve(file);if(modules.has(file))return modules.get(file).exports;
  const module={exports:{}};modules.set(file,module);
  const source=fs.readFileSync(file,'utf8'),code=ts.transpileModule(source,{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022,jsx:ts.JsxEmit.ReactJSX}}).outputText;
  const localRequire=name=>{
    if(name==='react/jsx-runtime')return {...jsx,jsx:(type,props,key)=>{elements.push({type,props});return jsx.jsx(type,props,key);},jsxs:(type,props,key)=>{elements.push({type,props});return jsx.jsxs(type,props,key);}};
    if(name==='./PullRequestTestEvidence')return {PullRequestTestEvidence:()=>null};
    if(!name.startsWith('.'))return require(name);
    const target=path.resolve(path.dirname(file),name);return load(fs.existsSync(target+'.ts')?target+'.ts':target+'.tsx');
  };
  vm.runInNewContext(code,{...shared,module,exports:module.exports,require:localRequire},{filename:file});return module.exports;
}
const api=load(path.join(root,'web/src/shell/api.ts')),
  feedback=load(path.join(root,'web/src/roles/developer/actionFeedback.ts')),
  notice=load(path.join(root,'web/src/roles/developer/ActionFeedbackNotice.tsx')),
  updates=load(path.join(root,'web/src/roles/developer/approvalUpdates.ts')),
  cards=load(path.join(root,'web/src/roles/developer/PullRequestChatActions.tsx'));
const tasks=load(path.join(root,'web/src/shell/OwnerTasks.tsx'));
let count=0;function check(value,message){assert.ok(value,message);count++;}
const workflow={id:'fictional-workflow',requestId:'fictional-request',repository:'owner/example',number:42,headSha:'a'.repeat(40),evidenceSnapshot:'fictional-evidence',approvalSnapshot:'FICTIONAL_APPROVAL_SNAPSHOT',publicationSnapshot:'fictional-publication',state:'proposed',planningModel:'fictional-model',rulesApproved:true,commitSha:null,error:null,ruleConcerns:[],strategy:{summary:'Fictional repair',trigger:'Fictional check',steps:['Inspect fictional file'],tests:['fictional command'],businessRuleChanges:[],questions:[],evidence:[],regressionTest:'Fictional failing-before/passing-after test'}};
const loop={id:'fictional-loop',requestId:workflow.requestId,workflowId:workflow.id,repository:workflow.repository,number:42,state:'working',round:1,note:null};
function html(requestId=workflow.requestId){return server.renderToStaticMarkup(React.createElement(notice.ActionFeedbackNotice,{requestId,onCheck:()=>{}}));}
const describe=value=>feedback.loopOutcome(value,workflow);
const send=options=>api.api('/github/workflows/'+workflow.id+'/loop','POST',{snapshot:workflow.approvalSnapshot},options);
async function settled(requestId){for(let i=0;i<100;i++){if(feedback.feedbackFor(requestId)?.state!=='pending')return;await new Promise(resolve=>setTimeout(resolve,1));}throw new Error('Fake action did not settle');}

// Invoke the actual rendered card callback. Then render fresh cards, as a polling remount would do.
respond=async()=>new Response(JSON.stringify({error:'This approval expired or was already used. Ask for a fresh proposal.'}),{status:409});
server.renderToStaticMarkup(React.createElement(cards.PullRequestCard,{workflow,onChanged:()=>{},onLoop:()=>{},onForbidden:()=>{}}));
const button=elements.find(item=>item.type==='button'&&item.props.children==='Approve strategy and run');
check(!!button,'The real proposal card exposes the scoped approval callback.');button.props.onClick();await settled(workflow.requestId);
check(calls.length===1&&calls[0].options.method==='POST','One explicit click makes one mutation; no automatic retry.');
check(feedback.feedbackFor(workflow.requestId).state==='blocked','Expired approval is a blocked result, not a reset to silence.');
check(html().includes('new explicit approval')&&html().includes('Diagnostic ID'),'A rendered persistent alert contains the fresh-approval requirement and diagnostic ID.');
const receipt=feedback.feedbackFor(workflow.requestId);updates.finishApprovalAction(workflow.id);
server.renderToStaticMarkup(React.createElement(cards.PullRequestCard,{workflow,onChanged:()=>{},onLoop:()=>{},onForbidden:()=>{}}));
check(feedback.feedbackFor(workflow.requestId)===receipt&&html().includes('Action blocked'),'Refresh/card remount cannot erase the action receipt.');
check(!storage.get('hirezero:action-receipts:v1').includes('FICTIONAL_APPROVAL_SNAPSHOT')&&!storage.get('hirezero:action-receipts:v1').includes('This approval expired'),'Browser persistence contains diagnostic metadata, not authorization bodies or raw server errors.');
elements.length=0;
const expiredMarkup=server.renderToStaticMarkup(React.createElement(cards.PullRequestCard,{workflow:{...workflow,approvalAvailable:false},onChanged:()=>{},onLoop:()=>{},onForbidden:()=>{}}));
check(expiredMarkup.includes('Refresh strategy for review')&&!elements.some(item=>item.type==='button'&&item.props.children==='Approve strategy and run'),'Expired cards require a fresh review rather than offering execution.');

respond=async()=>new Response(JSON.stringify(loop),{status:200});calls=[];
const accepted=await feedback.trackedAction(workflow.requestId,workflow.id,'loop',send,describe,workflow);
check(accepted.ok&&feedback.feedbackFor(workflow.requestId).state==='accepted','A matching loop receipt confirms acceptance, not an unproved coding start.');
feedback.reconcileFeedback(workflow.requestId,[{...workflow,state:'coding',codingRunId:'fictional-run',runState:'queued'}]);
check(html().includes('Coding run queued')&&feedback.feedbackFor(workflow.requestId).relatedId==='fictional-run','A matching recorded run promotes the receipt to confirmed queued work.');
feedback.reconcileFeedback(workflow.requestId,[{...workflow,state:'needs-attention',codingRunId:'fictional-run',runState:'failed',error:'Fictional worker failure'}]);
check(html().includes('Action failed')&&html().includes('Fictional worker failure'),'Definitive recorded run failure remains actionable and visible.');

const resumeScope='resume-request';
await feedback.trackedAction(resumeScope,'resume-workflow','resume',async()=>({...loop,id:'resume-loop',requestId:resumeScope,workflowId:'resume-workflow'}),()=>({state:'monitoring',message:'Loop monitoring resumed; this did not approve or start coding.'}));
feedback.reconcileFeedback(resumeScope,[{...workflow,id:'resume-workflow',requestId:resumeScope,approvalAvailable:false}]);
check(html(resumeScope).includes('Monitoring resumed')&&html(resumeScope).includes('fresh strategy')&&!html(resumeScope).includes('Coding run started'),'Resume feedback separates monitoring from fresh execution consent.');

const mismatched=feedback.loopOutcome({...loop,workflowId:'previous-workflow'},workflow);
check(mismatched.state==='blocked'&&mismatched.freshApproval,'An old active loop is not presented as execution of the selected strategy.');
const emptyScope='empty-response';respond=async()=>new Response('',{status:200});
await feedback.trackedAction(emptyScope,workflow.id,'loop',send,describe);
check(feedback.feedbackFor(emptyScope).state==='unknown'&&html(emptyScope).includes('Check status'),'Empty success bodies are uncertain and offer read-only reconciliation.');
const malformedScope='malformed-receipt';respond=async()=>new Response('{}',{status:200});
await feedback.trackedAction(malformedScope,workflow.id,'loop',send,describe);
check(feedback.feedbackFor(malformedScope).state==='unknown','Unidentified loop receipts cannot assert success.');

const timeoutScope='timeout-request';calls=[];
respond=async(_url,options)=>new Promise((_resolve,reject)=>options.signal.addEventListener('abort',()=>reject(new Error('Fictional deadline'))));
await feedback.trackedAction(timeoutScope,workflow.id,'loop',options=>api.api('/github/workflows/fictional/loop','POST',{}, {...options,timeoutMs:5}),describe);
check(feedback.feedbackFor(timeoutScope).state==='unknown'&&html(timeoutScope).includes('timed out'),'Deadline produces persistent unknown-outcome feedback.');
check(!feedback.mayStart(timeoutScope,workflow.id),'An unknown result holds repeat execution until status is reconciled.');
const beforeRepeat=calls.length;await feedback.trackedAction(timeoutScope,workflow.id,'loop',send,describe);
check(calls.length===beforeRepeat,'Unknown-outcome handling never repeats the POST.');
feedback.reconcileFeedback(timeoutScope,[{...workflow,state:'coding',codingRunId:'known-run',runState:'running'}]);
// Wrong request scope cannot reconcile an unrelated workflow even when an ID is reused.
check(feedback.feedbackFor(timeoutScope).state==='unknown','An unrelated request cannot overwrite the uncertain receipt.');

const heldScope='held-request';respond=async()=>new Response(JSON.stringify({error:'Execution is held for verification.',code:'execution-held'}),{status:503});
await feedback.trackedAction(heldScope,workflow.id,'loop',send,describe);
check(feedback.feedbackFor(heldScope).state==='blocked'&&html(heldScope).includes('held for verification'),'Explicit execution hold is a known block, not an unknown network failure.');
const lostScope='lost-response';respond=async()=>{throw new Error('Fictional lost connection');};
await feedback.trackedAction(lostScope,workflow.id,'loop',send,describe);
check(feedback.feedbackFor(lostScope).state==='unknown','A lost response preserves uncertainty rather than claiming no work started.');
const serverScope='server-failure';respond=async()=>new Response(JSON.stringify({error:'Fictional internal failure'}),{status:500});
await feedback.trackedAction(serverScope,workflow.id,'loop',send,describe);
check(feedback.feedbackFor(serverScope).state==='unknown','A server failure after dispatch is not assumed to have no side effect.');
check(feedback.feedbackFor(workflow.requestId).state==='failed','Starting another task preserves the previous task receipt.');

const refreshScope='accepted-refresh';respond=async()=>new Response(JSON.stringify({...loop,requestId:refreshScope}),{status:200});
await feedback.trackedAction(refreshScope,workflow.id,'loop',options=>api.api('/fictional-start','POST',{},options),value=>feedback.loopOutcome(value,{...workflow,requestId:refreshScope}));
respond=async()=>new Response('{}',{status:503});try{await api.api('/owner-tasks');}catch{}
check(feedback.feedbackFor(refreshScope).state==='accepted','A follow-up GET failure cannot relabel an accepted mutation as a failed start.');
check(events.includes(updates.approvalEvent),'Normal cross-view refresh coordination is retained.');
check(calls.every(call=>String(call.url).startsWith('/api')),'All requests in this test go to the fake backend; there is no live provider or URL.');
const refreshed=feedback.strategyOutcome({answer:'Fictional refreshed answer'},'refresh-shape');
check(refreshed.state==='accepted'&&refreshed.message.includes('explicit approval'),'The real refresh answer shape is accepted without claiming a new strategy ran.');
await feedback.trackedAction('refresh-shape',workflow.id,'refresh',async()=>({answer:'Fictional answer'}),value=>feedback.strategyOutcome(value,'refresh-shape'));
check(feedback.mayStart('refresh-shape','new-workflow'),'An acknowledged refresh does not lock approval of its new strategy.');
const rulesWorkflow={...workflow,id:'rules-workflow',requestId:'rules-request',rulesApproved:false};
await feedback.trackedAction(rulesWorkflow.requestId,rulesWorkflow.id,'rules',async()=>({...rulesWorkflow,rulesApproved:true}),value=>feedback.workflowOutcome(value,rulesWorkflow.id),rulesWorkflow);
check(feedback.mayStart(rulesWorkflow.requestId,rulesWorkflow.id),'Business-rule approval leaves the separate execution approval available.');
const unknownResume={...workflow,id:'unknown-resume-workflow',requestId:'unknown-resume-request'};
await feedback.trackedAction(unknownResume.requestId,unknownResume.id,'resume',async()=>{throw new api.ApiError('Fictional lost resume',0);},()=>({state:'monitoring',message:'Monitoring resumed'}),{state:'needs-you',loopId:'unknown-resume-loop'});
feedback.reconcileFeedback(unknownResume.requestId,[unknownResume],[{...loop,id:'unknown-resume-loop',workflowId:unknownResume.id,requestId:unknownResume.requestId,state:'needs-you',updatedAt:'2026-01-01T00:00:00Z'}]);
check(feedback.feedbackFor(unknownResume.requestId).state==='unknown','Unchanged workflow/loop polling cannot claim an uncertain resume succeeded.');
const unknownPublish={...workflow,id:'unknown-publish-workflow',requestId:'unknown-publish-request',state:'publication-proposed',codingRunId:'preexisting-run'};
await feedback.trackedAction(unknownPublish.requestId,unknownPublish.id,'publish',async()=>{throw new api.ApiError('Fictional lost publish',0);},value=>feedback.workflowOutcome(value,unknownPublish.id),unknownPublish);
feedback.reconcileFeedback(unknownPublish.requestId,[unknownPublish]);
check(feedback.feedbackFor(unknownPublish.requestId).state==='unknown','An old coding run cannot confirm an uncertain publication.');
const cancelWorkflow={...workflow,id:'cancel-workflow',requestId:'cancel-request'};
await feedback.trackedAction(cancelWorkflow.requestId,cancelWorkflow.id,'loop',async()=>{throw new api.ApiError('Fictional unknown start',0);},describe,cancelWorkflow);
const priorId=feedback.feedbackFor(cancelWorkflow.requestId).correlationId;let cancels=0;
await feedback.trackedAction(cancelWorkflow.requestId,cancelWorkflow.id,'cancel',async()=>{cancels++;return {...cancelWorkflow,state:'canceled'};},value=>feedback.workflowOutcome(value,cancelWorkflow.id),cancelWorkflow);
check(cancels===1&&feedback.feedbackFor(cancelWorkflow.requestId).previousCorrelationId===priorId,'Explicit cancellation remains available and preserves the earlier diagnostic ID.');
check(!feedback.feedbackFor(cancelWorkflow.requestId).unresolvedAction,'Confirmed cancellation closes execution uncertainty without approving new work.');
await feedback.trackedAction(unknownPublish.requestId,unknownPublish.id,'cancel',async()=>({...unknownPublish,state:'canceled'}),value=>feedback.workflowOutcome(value,unknownPublish.id),unknownPublish);
check(feedback.feedbackFor(unknownPublish.requestId).state==='unknown'&&!feedback.mayStart(unknownPublish.requestId,unknownPublish.id),'Cancellation cannot settle an earlier uncertain remote publication.');
await feedback.trackedAction(unknownPublish.requestId,unknownPublish.id,'reconcile',async()=>({...unknownPublish,state:'published',commitSha:'b'.repeat(40)}),value=>feedback.workflowOutcome(value,unknownPublish.id),unknownPublish);
check(feedback.feedbackFor(unknownPublish.requestId).state==='accepted'&&!feedback.feedbackFor(unknownPublish.requestId).unresolvedAction,'Explicit verified publication reconciliation can settle its uncertainty without replaying publication.');
const associationScope='association-request';
await feedback.trackedAction(associationScope,'workflow-A','publish',async()=>{throw new api.ApiError('Fictional unknown publication A',0);},()=>({state:'accepted',message:'Publication accepted'}));
const associationId=feedback.feedbackFor(associationScope).correlationId;
await feedback.trackedAction(associationScope,'workflow-B','reconcile',async()=>({...workflow,id:'workflow-B',requestId:associationScope,state:'published',commitSha:'c'.repeat(40)}),value=>feedback.workflowOutcome(value,'workflow-B'));
check(feedback.feedbackFor(associationScope).unresolvedWorkflowId==='workflow-A'&&feedback.feedbackFor(associationScope).previousCorrelationId===associationId&&!feedback.mayStart(associationScope,'workflow-A'),'Reconciliation of another workflow cannot settle the original publication or unlock replay.');
const mergeScope='merge-unknown-request';
await feedback.trackedAction(mergeScope,'merge-workflow','merge',async()=>{throw new api.ApiError('Fictional unknown merge',0);},()=>({state:'accepted',message:'Merge accepted'}));
await feedback.trackedAction(mergeScope,'merge-workflow','reconcile',async()=>({...workflow,id:'merge-workflow',requestId:mergeScope,state:'published',commitSha:'d'.repeat(40)}),value=>feedback.workflowOutcome(value,'merge-workflow'));
check(feedback.feedbackFor(mergeScope).unresolvedAction==='merge'&&!feedback.mayStart(mergeScope,'merge-workflow'),'A published commit alone cannot settle an uncertain merge intent.');
const taskScope='task-list-request',taskWorkflow={...workflow,id:'task-list-workflow',requestId:taskScope,codingRunId:'prior-run'};
respond=async()=>new Response(JSON.stringify([taskWorkflow]),{status:200});calls=[];
const taskBaseline=await tasks.taskWorkflowBaseline(taskScope,taskWorkflow.id);
await feedback.trackedAction(taskScope,taskWorkflow.id,'loop',async()=>({...loop,requestId:taskScope,workflowId:taskWorkflow.id}),value=>feedback.loopOutcome(value,taskWorkflow),taskBaseline);
feedback.reconcileFeedback(taskScope,[{...taskWorkflow,state:'coding',codingRunId:'new-task-run',runState:'queued'}]);
check(calls.length===1&&calls[0].options.method==='GET'&&feedback.feedbackFor(taskScope).state==='started','Task-list starts use an authoritative read baseline and polling can confirm a new coding run.');
respond=async()=>new Response(JSON.stringify([{...taskWorkflow,requestId:'another-request'}]),{status:200});calls=[];
await assert.rejects(()=>tasks.taskWorkflowBaseline(taskScope,taskWorkflow.id),error=>error.status===409);
check(calls.length===1&&calls[0].options.method==='GET','An unassociated task-list baseline fails closed without an execution mutation.');
const consentScope='consent-receipt-request';
await feedback.trackedAction(consentScope,undefined,'allow',async()=>({id:consentScope,allowed:true}),value=>feedback.consentOutcome(value,consentScope));
check(feedback.feedbackFor(consentScope).state==='accepted'&&feedback.feedbackFor(consentScope).message.includes('no PR execution'),'A scoped successful consent receipt is accepted without adding PR authority.');
check(assert.throws(()=>feedback.consentOutcome({id:'another-request',allowed:true},consentScope),error=>error.code==='outcome-unknown')===undefined,'Consent receipts must identify the exact request.');
check(assert.throws(()=>feedback.consentOutcome({id:consentScope,allowed:false},consentScope),error=>error.code==='outcome-unknown')===undefined,'An ungranted consent receipt cannot assert acceptance.');
console.log('PASS '+count+' fake-backend action receipt, rendered feedback, expiry, resume, correlation and unknown-outcome checks');
