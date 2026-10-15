import {useState} from 'react';
import {api} from '../shell/api';
import {command,type MeasuredEntry,type Work} from './api';

export function MeasuredJob({task,entry,available,busy,prepare,refresh}:{task:Work;entry?:MeasuredEntry;available:boolean;busy:boolean;prepare:(action:string,fields:Record<string,unknown>)=>Promise<unknown>;refresh:()=>Promise<void>}){
  const [allowance,setAllowance]=useState(''),[packet,setPacket]=useState<unknown>(null),[ack,setAck]=useState(false),[running,setRunning]=useState(false),[error,setError]=useState('');
  async function authorize(){
    if(!entry||!ack||!packet||running)return;setRunning(true);setError('');
    try{await command('measured.run',{id:entry.id,expectedVersion:entry.version,digest:entry.job.digest,decision:'accept-soft-limits'});await refresh();}
    catch(e){setError((e as Error).message);}finally{setRunning(false);}
  }
  return <section className="co-measured"><h3>One-job measured mode</h3>
    <p>Automatic model work stays off. Preparing a packet makes no model call. Authorization consumes exactly one native request and cannot be replayed.</p>
    {!available&&<p className="co-muted">Select a pinned native runtime to prepare an eligible job.</p>}
    {error&&<p role="alert">{error}</p>}
    {!entry&&available&&['queued','blocked'].includes(task.state)&&<form onSubmit={e=>{e.preventDefault();void prepare('measured.prepare',{id:task.id,expectedVersion:task.version,creditAllowance:Number(allowance)});}}>
      <label>Existing-credit fallback allowance for this one job<input type="number" min="0.01" max="1000" step="0.01" required value={allowance} onChange={e=>setAllowance(e.target.value)}/></label>
      <p className="co-muted">Choose the allowance explicitly. It is an application allowance, not a provider-enforced credit ceiling. No purchases, recharge or API-key fallback.</p>
      <button disabled={busy} type="submit">Prepare exact one-job packet</button>
    </form>}
    {entry&&<><p><b>{entry.job.state}</b> · {entry.job.route.replace('openai/','')} · policy {entry.job.policy.version}</p>
      <p>Application packet: {entry.job.applicationBytes.toLocaleString()} / {entry.job.policy.applicationBytes.toLocaleString()} UTF-8 bytes. Native turn/start allowance: {entry.job.policy.nativeRequests}. Internal native retries are not a billable-request ceiling.</p>
      <p>Planning reservation: {entry.job.policy.planningTokens.toLocaleString()} tokens, including {entry.job.policy.baselineInputAllowance.toLocaleString()} baseline allowance and {entry.job.policy.packetHeadroom.toLocaleString()} packet headroom. Output target: {entry.job.policy.outputTarget.toLocaleString()} tokens. These are planning estimates and observed tripwires, not upstream token caps.</p>
      <p>Local soft stops: {entry.job.policy.localDeadlineSeconds} seconds and {entry.job.policy.visibleCharacterStop.toLocaleString()} visible characters. Existing-credit allowance: {entry.job.creditAllowance}; no provider-enforced per-job credit cap.</p>
      <small>Packet {entry.job.digest} · runtime {entry.job.runtimeIdentity.slice(0,16)} · expires {new Date(entry.job.expiresAt).toLocaleString()}</small>
      <details onToggle={e=>{if(e.target===e.currentTarget&&e.currentTarget.open)void api('/company/measured/'+entry.id).then(setPacket).catch(e=>setError(e.message));}}><summary>Review exact context and instructions</summary>{packet?<pre>{JSON.stringify(packet,null,2)}</pre>:<p>Loading the sealed packet.</p>}</details>
      {entry.job.state==='proposed'&&<><p>This authorization raises this task's unused planning root to at least {entry.job.policy.planningTokens.toLocaleString()} tokens. Included usage is checked first; native billing can use existing credits.</p>
        <label className="co-check"><input type="checkbox" checked={ack} disabled={!packet} onChange={e=>setAck(e.target.checked)}/>I reviewed this packet and accept the local soft limits and lack of a provider-enforced credit cap.</label>
        <button disabled={!available||busy||running||!ack||!packet||Date.parse(entry.job.expiresAt)<=Date.now()} onClick={()=>void authorize()}>Authorize and run this one job</button></>}
      {entry.usage&&<p>{entry.reportedUsage===null?'Partial observed usage; final total is unknown.':'Reported provider usage:'} input {entry.usage.inputTokens.toLocaleString()}, output {entry.usage.outputTokens.toLocaleString()}, reasoning {entry.usage.reasoningTokens?.toLocaleString()??'unknown'} (included in output), total {entry.reportedUsage?.toLocaleString()??'unknown'}. Runtime input is included. Inconsistent or incomplete numbers remain held for reconciliation.</p>}
      {entry.error&&<p className="co-muted">{entry.error}</p>}
    </>}
  </section>;
}
