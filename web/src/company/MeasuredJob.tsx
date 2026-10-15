import {useRef,useState} from 'react';
import {api} from '../shell/api';
import {command,type MeasuredEntry,type Work} from './api';

type Packet={attempt:{id:string;measured:{digest:string}};call:{instructions:string;context:{id:string;employeeId:string;roleId:string;threadId:string;scope?:string;responsibility:string;input:string;companyKnowledge:string;departmentKnowledge:string;notes:string;history:unknown[];evidence:unknown[]}}};
function Contents({body}:{body:string}){
  let fields:Record<string,unknown>|null=null;
  try{const value=JSON.parse(body);if(value&&typeof value==='object'&&!Array.isArray(value))fields=value;}catch{/* Plain text is shown verbatim. */}
  return fields?<dl className="co-packet-fields">{Object.entries(fields).map(([key,value])=><div key={key}><dt>{key.replace(/([A-Z])/g,' $1')}</dt><dd>{typeof value==='string'?value:JSON.stringify(value)}</dd></div>)}</dl>:<pre>{body||'None'}</pre>;
}
const states:Record<string,string>={proposed:'Ready for your authorization',dispatched:'Running this one job',completed:'Draft saved for review',unknown:'Stopped: reconciliation required',withheld:'Output withheld',expired:'Proposal expired',canceled:'Canceled'};
export function MeasuredJob({task,entry,available,busy,prepare,refresh,conversational=false}:{task:Work;entry?:MeasuredEntry;available:boolean;busy:boolean;prepare:(action:string,fields:Record<string,unknown>)=>Promise<unknown>;refresh:()=>Promise<void>;conversational?:boolean}){
  const authorizing=useRef(false),reviewRequest=useRef(0);
  const [allowance,setAllowance]=useState(''),[packet,setPacket]=useState<Packet|null>(null),[ack,setAck]=useState(false),[running,setRunning]=useState(false),[error,setError]=useState('');
  async function review(){
    if(!entry)return;const request=++reviewRequest.current;setError('');setAck(false);setPacket(null);
    try{const next=await api<Packet>('/company/measured/'+entry.id);if(next.attempt.id!==entry.id||next.attempt.measured.digest!==entry.job.digest)throw new Error('The saved proposal changed. Refresh before reviewing it.');if(request===reviewRequest.current)setPacket(next);}
    catch(e){setError((e as Error).message);}
  }
  async function authorize(){
    if(authorizing.current||!entry||!ack||!packet||running||!available||entry.job.state!=='proposed'||Date.parse(entry.job.expiresAt)<=Date.now())return;authorizing.current=true;setRunning(true);setError('');
    try{await command('measured.run',{id:entry.id,expectedVersion:entry.version,digest:entry.job.digest,decision:'accept-soft-limits'});}
    catch(e){setError((e as Error).message);}finally{await refresh().catch(e=>setError(e.message));authorizing.current=false;setRunning(false);}
  }
  return <section className="co-measured"><h3>{conversational?'One conversational reply':'Run one internal draft'}</h3>
    <p>{conversational?'Review this saved message and exact context. Send authorizes one text-only native turn; no autonomous tasks, plans, tools or follow-up work. A consumed turn cannot be replayed.':'Assign a brief, review the saved task and context, then authorize one job. Nothing starts automatically; a consumed request cannot be replayed.'}</p>{conversational&&running&&<p role="status" aria-live="polite">Reply pending. Waiting for complete text and measured usage.</p>}
    {!available&&<p className="co-muted">A pinned native connection is required for manual jobs.</p>}
    {error&&<p role="alert">{error}</p>}
    {!entry&&available&&['queued','blocked'].includes(task.state)&&<form onSubmit={e=>{e.preventDefault();void prepare('measured.prepare',{id:task.id,expectedVersion:task.version,creditAllowance:Number(allowance)});}}>
      <label>Intended existing-credit allowance<input type="number" min="0.01" max="1000" step="0.01" required value={allowance} onChange={e=>setAllowance(e.target.value)}/></label>
      <p className="co-muted">This records your intended allowance. It cannot stop an in-flight request at that credit amount. Included usage must be available first. No purchases, recharge or API-key fallback.</p>
      <button disabled={busy} type="submit">Prepare job for review</button>
    </form>}
    {entry&&<><p><b>{conversational?(({proposed:'Reply prepared for your review',dispatched:'Reply pending',completed:'Reply saved'} as Record<string,string>)[entry.job.state]??states[entry.job.state]??entry.job.state):states[entry.job.state]||entry.job.state}</b> · {entry.job.route.replace('openai/','')}</p>
      <p>One native turn is allowed. Internal native retries can still occur. The complete application packet is {entry.job.applicationBytes.toLocaleString()} / {entry.job.policy.applicationBytes.toLocaleString()} UTF-8 bytes.</p>
      {entry.job.state==='proposed'&&<>
        <button disabled={busy||running} onClick={()=>void review()}>{conversational?'Review reply and context':'Review task and context'}</button>
        {packet&&<div className="co-packet"><h4>Sealed recipient and context</h4><p>{packet.call.context.roleId} · {packet.call.context.scope??'employee'} context</p><small>Employee {packet.call.context.employeeId} · conversation {packet.call.context.threadId}</small><h4>Responsibility included</h4><Contents body={packet.call.context.responsibility}/><h4>Requested work</h4><Contents body={packet.call.context.input}/><h4>Approved department facts</h4><Contents body={packet.call.context.departmentKnowledge}/>
          {packet.call.context.companyKnowledge&&<><h4>Approved company facts</h4><Contents body={packet.call.context.companyKnowledge}/></>}
          <h4>Private notes included</h4><Contents body={packet.call.context.notes}/><h4>Conversation history included</h4><pre>{packet.call.context.history.length?JSON.stringify(packet.call.context.history,null,2):'None'}</pre>
          <h4>Selected evidence included</h4><pre>{packet.call.context.evidence.length?JSON.stringify(packet.call.context.evidence,null,2):'None'}</pre>
          <details><summary>Complete sealed application context</summary><pre>{JSON.stringify(packet.call.context,null,2)}</pre></details><details><summary>Employee instructions</summary><pre>{packet.call.instructions}</pre></details>
        </div>}
        <p>This job reserves {entry.job.policy.planningTokens.toLocaleString()} estimated provider tokens across task, employee, department and company budgets. This unused task's planning allowance increases to that amount.</p>
        <p>Soft stops: {entry.job.policy.localDeadlineSeconds} seconds, {entry.job.policy.visibleCharacterStop.toLocaleString()} visible characters, {entry.job.policy.outputTarget.toLocaleString()} observed output tokens and {entry.job.policy.planningTokens.toLocaleString()} observed total tokens. These are not upstream token caps.</p>
        <p>Intended existing-credit allowance: {entry.job.creditAllowance}. There is no provider-enforced per-job credit cap; this request may exceed that amount.</p>
        <small>Expires {new Date(entry.job.expiresAt).toLocaleString()}. Policy {entry.job.policy.version}. The server binds your authorization to this exact saved task and context.</small>
        <label className="co-check"><input type="checkbox" checked={ack} disabled={!packet} onChange={e=>setAck(e.target.checked)}/>{conversational?'I reviewed this message, recipient and context. I authorize one conversational reply and accept that token, time and credit limits cannot be guaranteed upstream.':'I reviewed this work and context. I authorize one job and accept that token, time and credit limits cannot be guaranteed upstream.'}</label>
        <button disabled={!available||busy||running||!ack||!packet||Date.parse(entry.job.expiresAt)<=Date.now()} onClick={()=>void authorize()}>{conversational?(running?'Waiting for this reply.':'Send & authorize one reply'):(running?'Waiting for this job.':'Authorize and run one job')}</button>
      </>}
      {entry.usage&&<p>{entry.reportedUsage===null?'Partial observed usage; final total is unknown.':'Reported provider usage:'} input {entry.usage.inputTokens.toLocaleString()}, output {entry.usage.outputTokens.toLocaleString()}, reasoning {entry.usage.reasoningTokens?.toLocaleString()??'unknown'} (included in output), total {entry.reportedUsage?.toLocaleString()??'unknown'}. Runtime input is included.</p>}
      {entry.error&&<p className="co-muted">{entry.error}. Output is withheld where the outcome or accounting needs reconciliation.</p>}
    </>}
  </section>;
}
