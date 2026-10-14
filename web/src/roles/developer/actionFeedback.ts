import {ApiError,type ApiOptions} from '../../shell/api';
import type {FixLoop,PullRequestWorkflow} from './github';

export type FeedbackState='pending'|'accepted'|'started'|'monitoring'|'blocked'|'failed'|'unknown';
export type ActionFeedback={requestId:string;workflowId?:string;action:string;attemptId:string;correlationId:string;state:FeedbackState;message:string;at:string;freshApproval?:boolean;relatedId?:string;baselineKnown?:boolean;beforeRunId?:string|null;beforeRulesApproved?:boolean;beforeLoopState?:string;previousCorrelationId?:string;unresolvedAction?:string;unresolvedWorkflowId?:string};
type Outcome=Pick<ActionFeedback,'state'|'message'|'freshApproval'|'relatedId'>&{resolvesUncertainty?:boolean;resolvedIntents?:string[]};
type Baseline={codingRunId?:string|null;rulesApproved?:boolean;state?:string;loopId?:string};
const executionActions=['loop','answers','confirm'],safetyActions=['cancel','stop','reconcile'];
const records=new Map<string,ActionFeedback>(),listeners=new Set<()=>void>(),storageKey='hirezero:action-receipts:v1';
const states:FeedbackState[]=['pending','accepted','started','monitoring','blocked','failed','unknown'];
const safeId=(value:unknown)=>typeof value==='string'&&/^[a-zA-Z0-9_.:-]{1,160}$/.test(value);
const restoredMessage=(state:FeedbackState,fresh?:boolean)=>state==='unknown'?'The previous request has an unconfirmed outcome. Check status before trying again.':fresh?'This action needs a fresh strategy and new explicit approval.':state==='started'?'The last recorded action started work. Check the latest workflow status.':state==='monitoring'?'Loop monitoring resumed; this did not approve or start coding.':state==='blocked'?'The last action was blocked. Check the workflow and its required decision.':state==='failed'?'The last action failed. Check the recorded workflow before deciding what to do next.':'The server accepted the last action. Check status to verify its progress.';
try{for(const entry of JSON.parse(sessionStorage.getItem(storageKey)||'[]')){
  if(!safeId(entry.requestId)||!safeId(entry.attemptId)||!safeId(entry.correlationId)||!states.includes(entry.state))continue;
  const state:FeedbackState=entry.state==='pending'?'unknown':entry.state;
  records.set(entry.requestId,{...entry,state,message:restoredMessage(state,entry.freshApproval)});
}}catch{/* Receipts remain in memory if browser storage is unavailable. */}
function save(record:ActionFeedback){records.set(record.requestId,record);try{
  // Persist only bounded diagnostic metadata, never a prompt, approval snapshot, server error or private title.
  sessionStorage.setItem(storageKey,JSON.stringify([...records.values()].slice(-100).map(({requestId,workflowId,action,attemptId,correlationId,state,at,freshApproval,relatedId,baselineKnown,beforeRunId,beforeRulesApproved,beforeLoopState,previousCorrelationId,unresolvedAction,unresolvedWorkflowId})=>({requestId,workflowId,action,attemptId,correlationId,state,at,freshApproval,relatedId,baselineKnown,beforeRunId,beforeRulesApproved,beforeLoopState,previousCorrelationId,unresolvedAction,unresolvedWorkflowId}))));
}catch{}for(const listener of listeners)listener();}
export const feedbackFor=(requestId:string)=>records.get(requestId)??null;
export function subscribeFeedback(listener:()=>void){listeners.add(listener);return()=>listeners.delete(listener);}
export function mayStart(requestId:string,workflowId?:string){const value=feedbackFor(requestId);return !value||!value.unresolvedAction&&!['pending','unknown'].includes(value.state)&&(!executionActions.includes(value.action)||!['accepted','started'].includes(value.state)||value.workflowId!==workflowId);}
export async function trackedAction<T>(requestId:string,workflowId:string|undefined,action:string,send:(options:ApiOptions)=>Promise<T>,describe:(value:T)=>Outcome,baseline?:Baseline){
  const old=feedbackFor(requestId);if(old&&(['pending','unknown'].includes(old.state)||old.unresolvedAction)&&!safetyActions.includes(action))return {ok:false as const};
  const attemptId=crypto.randomUUID();
  const unresolved=old&&(old.unresolvedAction||['pending','unknown'].includes(old.state))?old:null;
  const record:ActionFeedback={requestId,workflowId,action,attemptId,correlationId:attemptId,state:'pending',message:'Sending your decision. Waiting for a confirmed result.',at:new Date().toISOString(),baselineKnown:baseline!==undefined,beforeRunId:baseline?.codingRunId??null,beforeRulesApproved:baseline?.rulesApproved,beforeLoopState:baseline?.state,relatedId:baseline?.loopId,
    ...(unresolved?{previousCorrelationId:unresolved.previousCorrelationId||unresolved.correlationId,unresolvedAction:unresolved.unresolvedAction||unresolved.action,unresolvedWorkflowId:unresolved.unresolvedWorkflowId||unresolved.workflowId}:{})};save(record);
  try{const value=await send({timeoutMs:120000,correlationId:attemptId});const outcome=describe(value);
    if(feedbackFor(requestId)?.attemptId===attemptId){const resolved=outcome.resolvesUncertainty&&record.unresolvedWorkflowId===workflowId&&
      (action==='reconcile'?(outcome.resolvedIntents||[]).includes(record.unresolvedAction||''):executionActions.includes(record.unresolvedAction||''));
      save({...record,...outcome,...(record.unresolvedAction&&!resolved?{state:'unknown',message:outcome.message+' An earlier action still has an unconfirmed outcome. Check status before starting again.'}:{unresolvedAction:undefined,unresolvedWorkflowId:undefined})});}
    return {ok:true as const,value};
  }catch(cause){const error=cause instanceof ApiError?cause:null;
    const fresh=error?.code==='fresh-approval-required'||/approval expired|already used|fresh proposal|fresh strategy|consent.*changed|strategy changed/i.test(error?.message||'');
    const unknown=!error||error.status===0||error.status>=500&&error.code!=='execution-held';
    const state:FeedbackState=unknown?'unknown':error!.code==='execution-held'||fresh||[400,401,403,404,409,429].includes(error!.status)?'blocked':'failed';
    const message=unknown?(error?.message||'Execution outcome is unknown. Check status before trying again.'):fresh?'This approval expired, was already used, or no longer matches the strategy. Refresh the strategy and review it, then give new explicit approval. Nothing is automatically renewed.':error?.message||'The action failed. Review the workflow before trying again.';
    if(feedbackFor(requestId)?.attemptId===attemptId)save({...record,state,message,freshApproval:fresh,correlationId:error?.correlationId||attemptId});
    return {ok:false as const,cause};
  }
}
export function loopOutcome(value:unknown,workflow:Pick<PullRequestWorkflow,'id'|'requestId'|'repository'|'number'>):Outcome{
  const loop=value as FixLoop;
  if(!loop||!safeId(loop.id)||loop.requestId!==workflow.requestId||loop.repository?.toLowerCase()!==workflow.repository.toLowerCase()||loop.number!==workflow.number||!safeId(loop.state))
    throw new ApiError('The response did not identify this requested round. Check status before trying again.',0,undefined,undefined,'outcome-unknown');
  if(loop.workflowId!==workflow.id)return {state:'blocked',message:'An existing loop is monitoring a different strategy. Open that round and review its state. Starting this strategy still needs its own fresh approval.',freshApproval:true,relatedId:loop.id};
  if(['needs-you','stopped'].includes(loop.state))return {state:'blocked',message:loop.note||'This round is blocked. Review its required decision before starting.',relatedId:loop.id};
  return {state:'accepted',message:'The loop accepted this round. Checking the workflow; a coding start has not yet been confirmed.',relatedId:loop.id};
}
export function workflowOutcome(value:unknown,workflowId:string):Outcome{
  const workflow=value as PullRequestWorkflow;if(!workflow||workflow.id!==workflowId||!safeId(workflow.state))throw new ApiError('The response did not confirm this workflow. Check status before trying again.',0,undefined,undefined,'outcome-unknown');
  if(/attention|needs-input|needs-evidence/.test(workflow.state))return {state:'blocked',message:workflow.error||'The workflow needs your decision. Review its recorded blocker.'};
  return {state:'accepted',message:'Your decision was recorded. Current workflow: '+workflow.state.replaceAll('-',' ')+'. Execution and publication still follow their own approval gates.',resolvesUncertainty:workflow.state==='canceled'||['published','merged'].includes(workflow.state)&&!!workflow.commitSha,
    resolvedIntents:workflow.state==='canceled'?executionActions:workflow.state==='merged'&&workflow.commitSha?['publish','merge','reconcile']:workflow.state==='published'&&workflow.commitSha?['publish','reconcile']:[]};
}
export function strategyOutcome(value:unknown,requestId:string):Outcome{
  if(value&&typeof value==='object'&&'answer' in value&&typeof value.answer==='string'&&value.answer.trim())return {state:'accepted',message:'Refresh finished. Check this thread for a fresh strategy or further questions. New explicit approval is required before execution.'};
  const workflow=value as PullRequestWorkflow;if(!workflow||!safeId(workflow.id)||workflow.requestId!==requestId||workflow.state!=='proposed')throw new ApiError('The response did not confirm a fresh strategy. Check status before trying again.',0,undefined,undefined,'outcome-unknown');
  return {state:'accepted',message:'A fresh strategy is ready for review. Give new explicit approval before execution.'};
}
export function consentOutcome(value:unknown,requestId:string):Outcome{
  if(!value||typeof value!=='object'||!('id' in value)||value.id!==requestId||!('allowed' in value)||value.allowed!==true)
    throw new ApiError('The response did not confirm consent for this request. Check status before trying again.',0,undefined,undefined,'outcome-unknown');
  return {state:'accepted',message:'Your request consent was recorded. Check the thread for its queued reply; this grants no PR execution or publication approval.'};
}
export function reconcileFeedback(requestId:string,workflows:PullRequestWorkflow[],loops:FixLoop[]=[]){const record=feedbackFor(requestId);if(!record||!['accepted','started','unknown','monitoring'].includes(record.state))return;
  const workflow=workflows.find(item=>item.id===record.workflowId&&item.requestId===requestId);if(!workflow)return;
  if(record.unresolvedAction)return; // A separate stop/cancel receipt does not silently settle an earlier request.
  if(record.action==='resume'){
    const loop=loops.find(item=>item.id===record.relatedId&&item.workflowId===record.workflowId&&item.requestId===requestId);
    const newlyObserved=record.baselineKnown&&['needs-you','stopped'].includes(record.beforeLoopState||'')&&loop&&['working','reviewing','checking','planning'].includes(loop.state)&&Date.parse(loop.updatedAt)>Date.parse(record.at);
    if(record.state==='unknown'&&!newlyObserved)return;
    const message='Loop monitoring '+(record.state==='unknown'?'is now confirmed active':'resumed')+'; this did not approve or start coding.'+(workflow.state==='proposed'?' This strategy still needs '+(workflow.approvalAvailable===false?'a fresh strategy and new explicit approval.':'explicit approval.'):' Current round: '+workflow.state.replaceAll('-',' ')+'.');if(record.message!==message)save({...record,state:'monitoring',message});return;
  }
  if(record.action==='rules'&&record.state==='unknown'&&record.baselineKnown&&record.beforeRulesApproved===false&&workflow.rulesApproved){save({...record,state:'accepted',message:'The business-rule decision is now recorded. Execution still needs a separate explicit approval.'});return;}
  if(!executionActions.includes(record.action))return;
  const newRun=record.baselineKnown&&!!workflow.codingRunId&&workflow.codingRunId!==record.beforeRunId;
  if(record.state==='unknown'&&!newRun)return;
  if(workflow.runState==='failed'){save({...record,state:'failed',message:workflow.error||'The coding run failed. Open the recorded evidence before deciding whether to request a new strategy.'});return;}
  if(/attention|needs-input|needs-evidence/.test(workflow.state)){save({...record,state:'blocked',message:workflow.error||'The workflow stopped for your decision. Review its evidence; nothing is retried automatically.'});return;}
  if(newRun&&['coding','validating','publication-proposed','published','merge-pending','merged'].includes(workflow.state)){
    const message=workflow.state==='coding'?'Coding run '+(workflow.runState==='queued'?'queued':'started')+'. Progress is recorded in this thread.':workflow.state==='validating'?'Coding finished; validation is in progress. Publication still needs your separate approval.':'Coding is complete. Current workflow: '+workflow.state.replaceAll('-',' ')+'.';
    if(record.message!==message)save({...record,state:'started',message,relatedId:workflow.codingRunId||undefined});
  }
}
