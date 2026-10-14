import {useEffect,useState} from 'react';
import {CheckCircle2,CircleAlert,LoaderCircle,Pause,Play,Square,StepForward} from 'lucide-react';
import {api,ApiError} from '../../shell/api';
import type {FixLoop,MergeReadiness,PullRequestWorkflow} from './github';
import {PullRequestTestEvidence,type PullRequestEvidenceView} from './PullRequestTestEvidence';
import {approvalEvent,claimApprovalAction,finishApprovalAction} from './approvalUpdates';
import {ActionFeedbackNotice,useActionFeedback} from './ActionFeedbackNotice';
import {trackedAction,loopOutcome,workflowOutcome,strategyOutcome,reconcileFeedback,mayStart} from './actionFeedback';

interface PullRequestChatActionsProps {requestId:string;messageCount:number;onForbidden:()=>void;}
export function PullRequestChatActions({requestId,messageCount,onForbidden}:PullRequestChatActionsProps){
  const [items,setItems]=useState<PullRequestWorkflow[]>([]),[loops,setLoops]=useState<FixLoop[]>([]),[refresh,setRefresh]=useState(0),[error,setError]=useState('');
  useEffect(()=>{
    let active=true,sequence=0;
    async function load(){const current=++sequence;try{const [value,running]=await Promise.all([api<PullRequestWorkflow[]>('/github/workflows/chat/'+requestId),api<FixLoop[]>('/github/loops/chat/'+requestId)]);if(active&&current===sequence){setItems(value);setLoops(running);reconcileFeedback(requestId,value,running);setError('');}}catch(cause){if(active&&current===sequence){setError((cause as Error).message);if(cause instanceof ApiError&&cause.status===403)onForbidden();}}}
    const changed=()=>{void load();};
    void load();window.addEventListener(approvalEvent,changed);const timer=setInterval(()=>{if(document.visibilityState==='visible')void load();},8000);
    return()=>{active=false;clearInterval(timer);window.removeEventListener(approvalEvent,changed);};
  },[requestId,messageCount,refresh]);
  const loop=items[0]?loops.find(item=>item.workflowId===items[0].id&&item.number===items[0].number&&item.repository.toLowerCase()===items[0].repository.toLowerCase()):undefined;
  return <><ActionFeedbackNotice requestId={requestId} onCheck={()=>setRefresh(value=>value+1)}/>{error&&<p className="fe-alert dev-pr-note" role="alert">{error}</p>}{items.slice(1).reverse().map(item=><details key={item.id} className="dev-pr-earlier"><summary>Earlier attempt · {item.repository} #{item.number} · {item.state.replaceAll('-',' ')}{item.commitSha?' · pushed '+item.commitSha.slice(0,8):''} · revision {item.headSha.slice(0,8)}</summary><PullRequestCard workflow={item} superseded onChanged={()=>{}} onForbidden={onForbidden}/></details>)}{items.slice(0,1).map(item=><PullRequestCard key={item.id} workflow={item} loop={loop} onLoop={()=>setRefresh(value=>value+1)} onChanged={value=>setItems(current=>current.map(existing=>existing.id===value.id?value:existing))} onForbidden={onForbidden}/>)}{loops.filter(item=>item.id===loop?.id||['working','reviewing','checking','planning','needs-you'].includes(item.state)).map(item=><LoopBar key={item.id} loop={item} onChanged={value=>setLoops(current=>current.map(previous=>previous.id===value.id?value:previous))} onForbidden={onForbidden}/>)}</>;
}

interface PullRequestCardProps {workflow:PullRequestWorkflow;superseded?:boolean;loop?:FixLoop;onLoop?:()=>void;onChanged:(value:PullRequestWorkflow)=>void;onForbidden:()=>void;}
export function PullRequestCard({workflow,superseded,loop,onLoop,onChanged,onForbidden}:PullRequestCardProps){
  const [busy,setBusy]=useState(false),[error,setError]=useState(''),[gate,setGate]=useState<MergeReadiness|null>(null),[proof,setProof]=useState<PullRequestEvidenceView|null>(null);
  const path='/github/workflows/'+workflow.id,plan=workflow.strategy;
  useActionFeedback(workflow.requestId);
  function fail(cause:unknown){setError((cause as Error).message);if(cause instanceof ApiError&&cause.status===403)onForbidden();}
  // Starting a loop returns the loop, not this workflow; the list picks both up on its next refresh.
  async function action(kind:string,body:object={}){
    if(busy||(['loop','answers','confirm'].includes(kind)&&!mayStart(workflow.requestId,workflow.id))||!claimApprovalAction(workflow.id))return;
    setBusy(true);setError('');
    try{const outcome=await trackedAction(workflow.requestId,workflow.id,kind,options=>api<PullRequestWorkflow|FixLoop>(path+'/'+kind,'POST',body,options),value=>
      kind==='loop'||kind==='answers'?loopOutcome(value,workflow):kind==='refresh'||kind==='retry'?strategyOutcome(value,workflow.requestId):workflowOutcome(value,workflow.id),workflow);
      if(outcome.ok){if(['loop','answers','refresh','retry'].includes(kind))onLoop?.();else onChanged(outcome.value as PullRequestWorkflow);setGate(null);}
      else if(outcome.cause instanceof ApiError&&outcome.cause.status===403)onForbidden();
    }finally{setBusy(false);finishApprovalAction(workflow.id);}
  }
  async function checkMerge(){setBusy(true);setError('');try{setGate(await api<MergeReadiness>(path+'/merge-check'));}catch(cause){fail(cause);}finally{setBusy(false);}}
  async function evidence(){setBusy(true);setError('');try{setProof(await api<PullRequestEvidenceView>(path+'/evidence'));}catch(cause){fail(cause);}finally{setBusy(false);}}
  const [stoppedRun,setStoppedRun]=useState<PullRequestEvidenceView|null>(null),[answers,setAnswers]=useState<Record<string,string>>({}),[asked,setAsked]=useState(false),[note,setNote]=useState('');
  const stopped=['needs-input','needs-attention'].includes(workflow.state)&&!!workflow.fixId;
  // A stopped run explains itself: its questions, error and last steps come from the worker.
  useEffect(()=>{if(!stopped){setStoppedRun(null);return;}let live=true;
    api<PullRequestEvidenceView>(path+'/evidence').then(value=>{if(live)setStoppedRun(value);}).catch(fail);
    return()=>{live=false;};},[stopped,workflow.id,workflow.state]);
  const coding=!!stoppedRun?.run?.fix.builderId,questions=(stoppedRun?.run?.run?.questions??[]).filter(item=>item.state==='open');
  const asking=workflow.state==='needs-input'&&!coding&&questions.length>0;
  // Only the current attempt offers actions; earlier ones are history.
  const rerunnable=!superseded&&['needs-input','needs-attention','validation-attention','handoff-attention','canceled'].includes(workflow.state);
  // Running again resets the worktree: say so when Sol's finished changes are sitting there.
  const discardsWork=workflow.runState==='completed'&&workflow.state!=='canceled';
  const retryable=!superseded&&['needs-input','needs-attention','handoff-attention','validation-attention','business-rule-attention','publication-attention','canceled'].includes(workflow.state);
  async function freshStrategy(){setBusy(true);setError('');try{await api('/requests/'+workflow.requestId+'/messages','POST',{body:'Please inspect '+workflow.repository+' #'+workflow.number+' again and propose a fresh fix strategy.',clientRequestId:crypto.randomUUID()});setAsked(true);}catch(cause){fail(cause);}finally{setBusy(false);}}
  const active=['preparing','scoping','coding','validating'].includes(workflow.state);
  const pending=['proposed','needs-evidence'].includes(workflow.state);
  const expired=pending&&workflow.approvalAvailable===false;
  const mergeAllowed=['published','canceled'].includes(workflow.state);
  const hasRun=!!workflow.fixId||!!workflow.codingRunId;
  // A delivered fix should read as done at a glance: green edge, a check, and a plain "Done" badge.
  const delivered=['published','merge-pending','merged'].includes(workflow.state)&&!superseded;
  const rulesOpen=plan.businessRuleChanges.length>0&&!workflow.rulesApproved;
  const loopActive=!!loop&&['working','reviewing','checking','planning'].includes(loop.state);
  const [replies,setReplies]=useState<string[]>([]),[sent,setSent]=useState(false);
  const allYes=plan.questions.length>0&&plan.questions.every((_,index)=>replies[index]==='Yes');
  const answeredAll=plan.questions.every((_,index)=>(replies[index]??'').trim());
  async function sendAnswers(){if(allYes){await action('answers',{snapshot:workflow.approvalSnapshot,answers:plan.questions.map(()=>'Yes')});return;}
    setBusy(true);setError('');try{await api('/requests/'+workflow.requestId+'/messages','POST',{body:'Answers for '+workflow.repository+' #'+workflow.number+':\n'+plan.questions.map((question,index)=>'- '+question+'\n  '+replies[index].trim()).join('\n')+'\n\nPlease update the fix strategy with these answers.',clientRequestId:crypto.randomUUID()});setSent(true);}catch(cause){fail(cause);}finally{setBusy(false);}}
  // One line that always answers "is it running, and do I need to do anything?"
  const state:{tone:'run'|'you'|'idle';head:string;sub:string}|null=delivered?null:superseded?{tone:'idle',head:'Replaced',sub:'A newer attempt is below. Nothing runs from this one.'}
    :active?{tone:'run',head:workflow.state==='coding'?(workflow.runState==='queued'?'Running · queued for Sol':'Running · Sol is fixing and testing the code'):workflow.state==='validating'?'Running · checking the tests and the diff':'Running · preparing an isolated checkout',sub:'Updates here on their own. You don’t need to do anything yet.'}
    :loopActive?{tone:'run',head:'Running · fix loop round '+loop!.round,sub:'Now: '+(loopLabels[loop!.state]??loop!.state)+'. Updates here on their own.'}
    :expired?{tone:'you',head:'Not running - approval expired or used',sub:'Refresh the strategy, review it, then give new explicit approval. Nothing is automatically renewed.'}:pending&&sent?{tone:'run',head:'Revising the strategy',sub:'Your answers were sent. A revised strategy will appear in this conversation.'}
    :pending&&plan.questions.length?{tone:'you',head:'Waiting for you · answer '+(plan.questions.length===1?'1 question':plan.questions.length+' questions'),sub:'Not running. Nothing changes in the code until you answer.'}
    :pending&&rulesOpen?{tone:'you',head:'Waiting for you · approve the business-rule changes',sub:'Not running. Then approve the strategy to start.'}
    :pending?{tone:'you',head:'Waiting for you · approve the strategy',sub:'Not running. Approving starts the fix.'}
    :workflow.state==='publication-proposed'?{tone:'you',head:'Waiting for you · approve commit and push',sub:'The fix passed validation. Nothing is pushed until you approve.'}
    :/attention|needs-input/.test(workflow.state)?{tone:'you',head:'Stopped · needs your decision',sub:'Not running. The options are below.'}
    :workflow.state==='canceled'?{tone:'idle',head:'Not running · canceled',sub:'Nothing will run from this attempt.'}
    :{tone:'idle',head:'Not running · '+workflow.state.replaceAll('-',' '),sub:''};
  return <article className={'fe-msg assistant fe-update'+(delivered?' dev-pr-done':'')} aria-label={'Fix strategy for '+workflow.repository+' #'+workflow.number}>
    <span className="fe-avatar" aria-hidden="true">WA</span><div className="fe-msg-body"><div className="fe-msg-content">
    <div className="fe-list-row">{workflow.round?<span className="dev-round-chip">Round {workflow.round}</span>:null}<strong>{workflow.repository} #{workflow.number}</strong>{delivered?<span className="dev-done-badge" role="status"><CheckCircle2 size={14} aria-hidden="true"/>Done · {workflow.state==='merged'?'merged':'committed and pushed'}{workflow.commitSha?' '+workflow.commitSha.slice(0,8):''}</span>:null}</div>
    {state&&<div className={'dev-state '+state.tone} role="status">{state.tone==='run'?<LoaderCircle size={16} className="fe-spin" aria-hidden="true"/>:state.tone==='you'?<CircleAlert size={16} aria-hidden="true"/>:<Pause size={16} aria-hidden="true"/>}<span><strong>{state.head}</strong>{state.sub&&<small>{state.sub}</small>}</span></div>}
    <h3>Fix strategy</h3><p>{plan.trigger}</p>
    <ol className="dev-pr-steps">{plan.steps.map(value=><li key={value}>{value}</li>)}</ol>
    <p><strong>Verify:</strong> {plan.regressionTest}</p>
    <details><summary>Evidence and test details</summary><p>{plan.summary}</p><p><strong>File and line evidence</strong></p><ul>{plan.evidence.map(value=><li key={value}>{value}</li>)}</ul><p><strong>Test commands</strong></p><ul>{plan.tests.map(value=><li key={value}>{value}</li>)}</ul>
      {!!plan.evidenceRequests?.length&&<><p><strong>Agent investigation</strong></p><p>The agent will retrieve these details and inspect the checkout as part of this strategy.</p><ul>{plan.evidenceRequests.map(item=><li key={item.kind+item.value+item.startLine}>{item.kind}: {item.value} · line {item.startLine}</li>)}</ul></>}
      <p className="fe-muted">Luna triage → {workflow.planningModel.includes('astra')?'Astra':'Sol 6.1'} high validation · Sol 6.1 high coding · revision {workflow.headSha.slice(0,8)}</p>{plan.escalationReason&&<p>{plan.escalationReason}</p>}
    </details>
    {plan.questions.length>0&&(superseded||!pending||sent?<div className="dev-pr-note"><strong>Questions before coding</strong><ul>{plan.questions.map(value=><li key={value}>{value}</li>)}</ul></div>
      :<form className="dev-pr-approval dev-pr-question dev-questions" onSubmit={event=>{event.preventDefault();void sendAnswers();}}>
        <p><strong>Questions before coding</strong> <span className="fe-muted">Say yes to go ahead as planned, or write what you want instead.</span></p>
        {plan.questions.map((question,index)=><div key={question} className="dev-question">
          <p>{question}</p>
          <div className="dev-question-row"><button type="button" className={replies[index]==='Yes'?'on':''} aria-pressed={replies[index]==='Yes'} onClick={()=>setReplies(current=>{const next=[...current];next[index]=next[index]==='Yes'?'':'Yes';return next;})}><CheckCircle2 size={13} aria-hidden="true"/> Yes</button>
            <input aria-label="Other answer" placeholder="Or answer here…" maxLength={1000} value={replies[index]==='Yes'?'':replies[index]??''} onChange={event=>{const value=event.target.value;setReplies(current=>{const next=[...current];next[index]=value;return next;});}}/></div></div>)}
        <button className="primary" type="submit" disabled={busy||!answeredAll||(allYes&&rulesOpen)}>{busy?'Sending…':!answeredAll?'Answer each question to continue':allYes?'Answer yes and start fixing':'Send answers for a revised strategy'}</button>
        {allYes&&rulesOpen&&<span className="fe-muted"> Approve the business-rule changes below first.</span>}
      </form>)}
    {plan.businessRuleChanges.length>0&&<div className={(workflow.rulesApproved?'':'fe-alert ')+'dev-pr-note'}><strong>{workflow.rulesApproved?'Business-rule changes you approved':'New business-rule changes need your decision'}</strong><ul>{plan.businessRuleChanges.map(value=><li key={value}>{value}</li>)}</ul>{workflow.rulesApproved?<p>These listed changes are approved.</p>:pending&&<button disabled={busy} onClick={()=>void action('rules',{snapshot:workflow.evidenceSnapshot})}>Approve these business-rule changes</button>}</div>}
    {workflow.ruleConcerns?.length?<ul className="fe-alert dev-pr-note">{workflow.ruleConcerns.map(value=><li key={value}>{value}</li>)}</ul>:null}
    {workflow.error&&workflow.state!=='needs-evidence'&&<p className="fe-alert dev-pr-note" role="alert">{workflow.error}</p>}{error&&<p className="fe-alert dev-pr-note" role="alert">{error}</p>}
    {!superseded&&pending&&<div className="dev-pr-approval"><p className="fe-muted">Approve this strategy to start one scoped investigation, fix and tests, including dependency downloads when required. Scope and business-rule changes require a new decision. Publication waits for approval of the reviewed changes; merge has its own confirmation.</p>{expired?<><p className="dev-blocked">This approval is unavailable. A fresh strategy and new explicit approval are required.</p><button disabled={busy} onClick={()=>void action('refresh')}>Refresh strategy for review</button></>:plan.questions.length>0?<p className="dev-blocked">Answer the questions above to start.</p>:rulesOpen?<p className="dev-blocked">Approve the business-rule changes above to start.</p>:<button className="primary" disabled={busy||!mayStart(workflow.requestId,workflow.id)} onClick={()=>void action('loop',{snapshot:workflow.approvalSnapshot})}>{busy?'Starting…':'Approve strategy and run'}</button>}<p className="fe-muted">After each push it asks Codex to review, reads the findings and CI, and has the next round's strategy ready here. Each round's code changes wait for your approval, except dependency-only fixes, which start on their own.</p></div>}
    {!superseded&&asking&&questions.map(item=><form key={item.id} className="dev-pr-approval dev-pr-question" onSubmit={event=>{event.preventDefault();void action('answer',{questionId:item.id,answer:answers[item.id]||''});}}>
      <p><strong>The scout asks:</strong> {item.prompt}</p>
      <textarea aria-label="Your answer" rows={3} maxLength={2000} value={answers[item.id]||''} onChange={event=>setAnswers(current=>({...current,[item.id]:event.target.value}))}/>
      <button className="primary" type="submit" disabled={busy||!(answers[item.id]||'').trim()}>{busy?'Sending…':'Answer and continue'}</button></form>)}
    {stopped&&coding&&questions.length>0&&<div className="fe-alert dev-pr-note"><strong>Sol asks</strong><ul>{questions.map(item=><li key={item.id}>{item.prompt}</li>)}</ul><p>Answer in the note below and run it again.</p></div>}
    {stopped&&stoppedRun?.run?.run&&<details className="dev-pr-note" open={!asking}><summary>What the {coding?'coding run':'scout'} reported · {stoppedRun.run.run.state}</summary>
      {stoppedRun.run.run.error&&<p><strong>Error:</strong> {stoppedRun.run.run.error}</p>}
      {!!stoppedRun.run.run.steps?.length&&<ol className="dev-pr-steps">{stoppedRun.run.run.steps.map((step,index)=><li key={index}>{step}</li>)}</ol>}</details>}
    {!superseded&&workflow.state==='needs-attention'&&!!workflow.codingRunId&&workflow.runState==='completed'&&<div className="dev-pr-approval"><button className="primary" disabled={busy} onClick={()=>void action('revalidate',{})}>{busy?'Checking…':'Check again'}</button><span className="fe-muted"> Sol finished; re-checks that work without running it again.</span></div>}
    {!superseded&&workflow.state==='needs-attention'&&hasRun&&stoppedRun&&!coding&&<div className="dev-pr-approval"><button className="primary" disabled={busy} onClick={()=>void action('resume')}>{busy?'Resuming…':'Resume'}</button></div>}
    {!superseded&&workflow.state==='validation-attention'&&!!workflow.codingRunId&&(workflow.sensitivePending?<div className="dev-pr-approval">
      <p><strong>Lines to look at</strong> <span className="fe-muted">Open these in the worktree. Values aren't shown here on purpose.</span></p>
      <ul>{(workflow.sensitiveFindings??[]).map(value=><li key={value}><code>{value}</code></li>)}</ul>
      <button className="primary" disabled={busy} onClick={()=>void action('confirm-sensitive',{hash:workflow.sensitivePending})}>{busy?'Checking…':'They’re placeholders — check again'}</button>
      <span className="fe-muted"> Confirms exactly these lines. Anything that looks like a real token still stops publication.</span></div>
      :<div className="dev-pr-approval"><button className="primary" disabled={busy} onClick={()=>void action('revalidate',{})}>{busy?'Checking…':'Check again'}</button><span className="fe-muted"> Re-checks Sol's finished work without running it again.</span></div>)}
    {!superseded&&workflow.state==='validation-attention'&&!!workflow.codingRunId&&workflow.error?.startsWith('Missing command evidence')&&<form className="dev-pr-approval dev-pr-question" onSubmit={event=>{event.preventDefault();void action('confirm-proof',{note:note.trim()});}}>
      <p><strong>Verified it yourself?</strong> <span className="fe-muted">If you ran the tests in the worktree and saw them fail without the fix and pass with it, say what you ran. It's tied to exactly this code; any change needs verifying again.</span></p>
      <textarea aria-label="What you verified" rows={2} maxLength={2000} value={note} placeholder="e.g. Local PostgreSQL harness: 3 failing before, 32/32 and full suite 629/629 after" onChange={event=>setNote(event.target.value)}/>
      <button className="primary" type="submit" disabled={busy||note.trim().length<10}>{busy?'Checking…':'I verified the tests - check again'}</button></form>}
    {!superseded&&workflow.state==='business-rule-attention'&&!!workflow.codingRunId&&<form className="dev-pr-approval dev-pr-question" onSubmit={event=>{event.preventDefault();void action('revalidate',{note:note.trim()});}}>
      <p><strong>Your decision</strong> <span className="fe-muted">If the change above is what you want, say so. It's recorded on this fix and the work is reviewed again without rerunning Sol.</span></p>
      <textarea aria-label="Your decision on the concern" rows={2} maxLength={2000} value={note} placeholder="e.g. Approved: CI audits production dependencies only until braces ships a fix." onChange={event=>setNote(event.target.value)}/>
      <button className="primary" type="submit" disabled={busy||!note.trim()}>{busy?'Checking…':'Record decision and check again'}</button></form>}
    {rerunnable&&<form className="dev-pr-approval dev-pr-question" onSubmit={event=>{event.preventDefault();void action('retry',{note:note.trim()||null});}}>
      <p><strong>Run again</strong> <span className="fe-muted">Creates a fresh proposal at this PR revision. Execution needs approval of that proposal. Add answers or direction for Sol if it helps.</span></p>
      {discardsWork&&<p className="fe-alert dev-pr-note">Sol already finished this attempt. Running again throws those changes away and starts over (about 15–20 minutes).</p>}
      <textarea aria-label="Note for the next attempt" rows={2} maxLength={2000} value={note} placeholder="Optional: answers to Sol's questions, or what to try differently" onChange={event=>setNote(event.target.value)}/>
      <button className="primary" type="submit" disabled={busy}>{busy?'Starting…':'Run again'}</button></form>}
    {retryable&&<div className="dev-pr-approval">{asked?<p role="status">Asked for a fresh strategy. It will appear in this conversation.</p>:<button className={rerunnable?'fe-link':undefined} disabled={busy} onClick={()=>void freshStrategy()}>{rerunnable?'Or start over with a fresh strategy':'Ask for a fresh strategy'}</button>}</div>}
    {!superseded&&workflow.state==='publication-attention'&&!!workflow.commitSha&&<div className="dev-pr-approval"><button className="primary" disabled={busy} onClick={()=>void action('reconcile',{})}>{busy?'Checking GitHub…':'Check GitHub again'}</button><span className="fe-muted"> Marks it delivered if the PR's head is the pushed commit {workflow.commitSha.slice(0,8)}.</span></div>}
    {!superseded&&workflow.state==='handoff-attention'&&<div className="dev-pr-approval"><button className="primary" disabled={busy} onClick={()=>void action('confirm',{snapshot:workflow.approvalSnapshot})}>{busy?'Retrying the handoff…':'Retry handoff'}</button></div>}
    {!superseded&&workflow.state==='publication-proposed'&&<div className="dev-pr-approval"><p>Validation passed. Review the run and changes, then approve committing and pushing this exact diff. Merge has its own confirmation.</p><button className="primary" disabled={busy} onClick={()=>void action('publish',{snapshot:workflow.publicationSnapshot})}>Approve commit and push</button></div>}
    {active&&<p role="status">{workflow.state==='coding'?(workflow.runState==='queued'?'Your fix is queued for Sol 6.1 high.':'Sol 6.1 high is fixing and testing the code.'):workflow.state==='validating'?'Verifying tests and the diff before proposing publication.':'Finding a matching local repository, cloning it into your repositories folder if needed, and verifying the PR revision before preparing an isolated checkout.'} Progress updates automatically.</p>}
    {!superseded&&(active||pending||workflow.state==='publication-proposed')&&<button className="fe-ghost" disabled={busy} onClick={()=>void action('cancel',{snapshot:workflow.state==='publication-proposed'?workflow.publicationSnapshot:workflow.approvalSnapshot})}>{active?'Cancel run':'Deny this proposal'}</button>}
    {workflow.commitSha&&<p>Commit: <a className="fe-link" target="_blank" rel="noopener noreferrer" href={'https://github.com/'+workflow.repository+'/commit/'+workflow.commitSha}>{workflow.commitSha.slice(0,8)}</a></p>}
    {hasRun&&<button className="fe-ghost" disabled={busy} onClick={()=>void evidence()}>Review run and test evidence</button>}
    {proof&&<PullRequestTestEvidence workflowId={workflow.id} proof={proof}/>}
    {!superseded&&workflow.state==='published'&&(!loop||!['working','reviewing','checking','planning','needs-you'].includes(loop.state))&&!(loop?.state==='healthy'&&loop.reviewedSha===workflow.commitSha)&&<div className="dev-pr-approval"><button className="fe-ghost" disabled={busy||!mayStart(workflow.requestId,workflow.id)} onClick={()=>void action('loop',{snapshot:workflow.approvalSnapshot})}><Play size={14} aria-hidden="true"/> Track this round</button><span className="fe-muted"> Further diagnosis, repairs and external review requests each need owner approval.</span></div>}
    {!superseded&&mergeAllowed&&<button className="fe-ghost" disabled={busy} onClick={()=>void checkMerge()}>Check merge readiness</button>}
    {gate&&<section aria-label="Merge readiness">{gate.ready?<><p role="status">All checks green, review threads resolved, and GitHub reports revision {gate.headSha.slice(0,8)} mergeable.</p><button disabled={busy} onClick={()=>void action('merge',{headSha:gate.headSha,snapshot:gate.snapshot})}>Confirm merge of {gate.headSha.slice(0,8)}</button></>:<ul>{gate.blockers.map(value=><li key={value}>{value}</li>)}</ul>}<p className="fe-muted">GitHub is checked again when you confirm.</p></section>}
    <a className="fe-link" href={'https://github.com/'+workflow.repository+'/pull/'+workflow.number} target="_blank" rel="noopener noreferrer">Open PR on GitHub →</a>
    </div></div>
  </article>;
}

const loopLabels:Record<string,string>={working:'fixing',reviewing:'waiting for Codex review',checking:'waiting for CI',planning:'planning the next round','needs-you':'needs you',stopped:'stopped',healthy:'healthy'};
/** The fix loop's state under the current card, with the two brakes: stop now, or stop after this round. */
function LoopBar({loop,onChanged,onForbidden}:{loop:FixLoop;onChanged:(value:FixLoop)=>void;onForbidden:()=>void}){
  const [busy,setBusy]=useState(''),[error,setError]=useState('');
  useActionFeedback(loop.requestId);
  const active=['working','reviewing','checking','planning'].includes(loop.state);
  async function run(kind:string,path:string,body:object){if(busy||!claimApprovalAction('loop:'+loop.id))return;setBusy(kind);setError('');try{
    const outcome=await trackedAction(loop.requestId,loop.workflowId||undefined,kind==='resume'?'resume':'stop',options=>api<FixLoop>('/github/loops/'+loop.id+path,'POST',body,options),value=>{
      if(value?.id!==loop.id||value.requestId!==loop.requestId||value.workflowId!==loop.workflowId)throw new ApiError('The response did not confirm this loop. Check status before trying again.',0,undefined,undefined,'outcome-unknown');
      return {state:kind==='resume'?'monitoring':'accepted',message:kind==='resume'?'Loop monitoring resumed. This does not approve or start coding. Review the underlying workflow and any remaining blocker.':'Your stop decision was recorded. Check the latest round status.',relatedId:value.id,resolvesUncertainty:kind!=='resume'&&value.state==='stopped'};
    },{state:loop.state,loopId:loop.id});if(outcome.ok)onChanged(outcome.value);else if(outcome.cause instanceof ApiError&&outcome.cause.status===403)onForbidden();
  }finally{setBusy('');finishApprovalAction('loop:'+loop.id);}}
  return <div className={'dev-loop-bar '+loop.state} role="status">
    <span className="dev-loop-icon">{loop.state==='healthy'?<CheckCircle2 size={16}/>:active?<LoaderCircle size={16} className="fe-spin"/>:<Pause size={16}/>}</span>
    <span className="dev-loop-text"><strong>Fix loop · Round {loop.round}</strong> <span>{loopLabels[loop.state]??loop.state}{loop.stopAfterRound&&active?' · stops after this round':''}</span>{loop.note&&<small>{loop.note}</small>}</span>
    {active&&<span className="dev-loop-actions">
      {!loop.stopAfterRound&&<button className="fe-ghost" disabled={!!busy} onClick={()=>void run('soft','/stop',{now:false})}><StepForward size={14}/> {busy==='soft'?'…':'Stop after this round'}</button>}
      <button className="fe-ghost danger" disabled={!!busy} onClick={()=>void run('hard','/stop',{now:true})}><Square size={13}/> {busy==='hard'?'Stopping…':'Stop now'}</button></span>}
    {(loop.state==='stopped'||loop.state==='needs-you')&&<span className="dev-loop-actions"><button className="fe-ghost" disabled={!!busy} onClick={()=>void run('resume','/resume',{})}><Play size={14}/> {busy==='resume'?'…':'Resume loop'}</button></span>}
    {error&&<small className="fe-alert" role="alert">{error}</small>}
  </div>;
}
