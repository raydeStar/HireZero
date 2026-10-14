import {useCallback,useEffect,useState} from 'react';
import {ApiError,api,when} from './api';

export type IntakeRecord={id:string;source:string;from:string;subject:string;verdict:string;decidedBy:string;reason:string;requestId:string|null;matchedBy:string|null;priority:string;priorityBy:string;taskId:string|null;tiers:string[];createdAt:string;body?:string|null;dismissed?:boolean;category?:string|null};
type Microsoft={configured:boolean;mail:boolean;teams:boolean;redirectUri:string;account:{address:string;workAccount:boolean;scopes:string[];lastMailSync:string|null;lastTeamsSync:string|null;lastError:string|null;connectedAt:string}|null};
const tierNames:Record<string,string>={rules:'Rules',classifier:'Jev',match:'Match',prioritize:'Priority','dive-in':'Dive in'};

/** Everything that came in from connected sources, with the cheapest step that decided it. */
export function InboxView({onOpen,onForbidden}:{onOpen:(id:string)=>void;onForbidden:()=>void}){
  const [items,setItems]=useState<IntakeRecord[]>([]),[showAll,setShowAll]=useState(false),[busy,setBusy]=useState(''),[error,setError]=useState('');
  const fail=useCallback((cause:unknown)=>{setError((cause as Error).message);if(cause instanceof ApiError&&cause.status===403)onForbidden();},[onForbidden]);
  const load=useCallback(async()=>{try{setItems(await api<IntakeRecord[]>('/intake'));}catch(cause){fail(cause);}},[fail]);
  useEffect(()=>{void load();const timer=setInterval(()=>{if(document.visibilityState==='visible')void load();},20000);return()=>clearInterval(timer);},[load]);
  async function act(id:string,work:()=>Promise<unknown>){setBusy(id);setError('');try{await work();await load();}catch(cause){fail(cause);}finally{setBusy('');}}
  const shown=items.filter(item=>!item.dismissed&&(showAll||item.verdict!=='ignore'));
  return <div className="fe-stack"><div className="fe-section-head"><p className="fe-muted">Messages from your connected sources. Each stops at the cheapest step that could decide it: rules, then Jev, then matching, priority and diving in.</p>
    <label className="dev-check"><input type="checkbox" checked={showAll} onChange={event=>setShowAll(event.target.checked)}/>Show ignored</label></div>
    {error&&<p className="fe-alert" role="alert">{error}</p>}
    {!shown.length&&<p className="fe-empty">Nothing waiting. Connect Microsoft 365 in Settings to bring in Outlook and Teams.</p>}
    {shown.map(item=><article className="fe-section dev-inbox-item" key={item.id}>
      <div className="fe-section-head"><strong>{item.subject||'(no subject)'}</strong>{item.category&&<span className="fe-pill" title="Category">{item.category}</span>}<span className={'fe-pill dev-verdict-'+item.verdict}>{item.verdict==='act'?'Needs action':item.verdict==='review'?'Maybe':item.verdict==='ignore'?'Ignored':item.verdict}</span></div>
      <p className="fe-muted">{item.from} · {item.source} · {when(item.createdAt)}</p>
      <p className="fe-muted">{item.tiers.map(tier=>tierNames[tier]||tier).join(' → ')} · decided by {item.decidedBy}{item.reason?': '+item.reason:''}</p>
      {item.body&&!item.requestId&&<p className="dev-inbox-body">{item.body.slice(0,280)}{item.body.length>280?'…':''}</p>}
      <div className="dev-shift-actions">
        <label className="dev-check">Priority<select value={item.priority} disabled={busy===item.id} onChange={event=>void act(item.id,()=>api('/intake/'+item.id+'/priority','PUT',{priority:event.target.value}))}>{['low','normal','high'].map(value=><option key={value}>{value}</option>)}</select>{item.priorityBy==='owner'&&<small>(yours)</small>}</label>
        {item.requestId?<button onClick={()=>onOpen(item.requestId!)}>Open conversation</button>:item.verdict!=='ignore'&&<button className="primary" disabled={busy===item.id} onClick={()=>void act(item.id,()=>api('/intake/'+item.id+'/promote','POST',{}))}>Make a task</button>}
        {item.verdict==='ignore'&&!item.requestId&&<button disabled={busy===item.id} onClick={()=>void act(item.id,()=>api('/intake/'+item.id+'/promote','POST',{}))}>Not noise: make a task</button>}
        <button className="fe-ghost" disabled={busy===item.id} onClick={()=>void act(item.id,()=>api('/intake/'+item.id+'/dismiss','POST',{}))}>Dismiss</button></div>
    </article>)}
  </div>;
}

/** Microsoft 365 read-only connection: app ID, services, sign-in and sync status. */
export function MicrosoftConnection({onForbidden}:{onForbidden:()=>void}){
  const [state,setState]=useState<Microsoft|null>(null),[clientId,setClientId]=useState(''),[busy,setBusy]=useState(false),[error,setError]=useState('');
  const notice=new URLSearchParams(location.search).get('microsoft');
  const load=useCallback(async()=>{try{const value=await api<Microsoft>('/connections/microsoft');setState(value);}catch(cause){setError((cause as Error).message);if(cause instanceof ApiError&&cause.status===403)onForbidden();}},[onForbidden]);
  useEffect(()=>{void load();},[load]);
  async function run(work:()=>Promise<unknown>){setBusy(true);setError('');try{await work();await load();}catch(cause){setError((cause as Error).message);}finally{setBusy(false);}}
  if(!state)return <section className="fe-section"><h2>Microsoft 365</h2><p className="fe-muted">Checking…</p></section>;
  return <section className="fe-section fe-stack"><h2>Microsoft 365</h2>
    <p className="fe-muted">Read-only: Outlook inbox and Teams chats flow into Work → Inbox. Nothing is sent, moved or deleted.</p>
    {notice&&<p role="status" className={notice==='connected'?'':'fe-alert'}>{notice==='connected'?'Connected. The first sync looks back one day.':'Sign-in did not finish: '+notice}</p>}
    {error&&<p className="fe-alert" role="alert">{error}</p>}
    {state.account?<><p><strong>{state.account.address}</strong> · connected {when(state.account.connectedAt)}</p>
        <p className="fe-muted">Outlook: {state.account.lastMailSync?'last read '+when(state.account.lastMailSync):'not read yet'}{state.account.workAccount?' · Teams: '+(state.account.lastTeamsSync?'last read '+when(state.account.lastTeamsSync):'not read yet'):' · Teams needs a work or school account'}</p>
        {state.account.lastError&&<p className="fe-alert">{state.account.lastError}</p>}
        <div className="dev-shift-actions"><button disabled={busy} onClick={()=>void run(()=>api('/connections/microsoft/sync','POST',{}))}>Sync now</button><button className="fe-ghost" disabled={busy} onClick={()=>void run(()=>api('/connections/microsoft','DELETE'))}>Disconnect</button></div></>
      :<><label>App (client) ID from your Entra app registration<input value={clientId} placeholder={state.configured?'Saved. Enter a new one to change it.':'00000000-0000-0000-0000-000000000000'} onChange={event=>setClientId(event.target.value)}/></label>
        <p className="fe-muted">Register it as a public client (Mobile and desktop) with redirect URI <code>{state.redirectUri}</code>. No secret is needed.</p>
        <div className="dev-shift-actions">{clientId.trim()&&<button disabled={busy} onClick={()=>void run(async()=>{await api('/connections/microsoft','PUT',{clientId:clientId.trim()});setClientId('');})}>Save app ID</button>}
          <button className="primary" disabled={busy||!state.configured} onClick={()=>void run(async()=>{const {url}=await api<{url:string}>('/connections/microsoft/start','POST',{});location.href=url;})}>Connect Microsoft 365</button></div></>}
    <div className="dev-shift-actions"><label className="dev-check"><input type="checkbox" checked={state.mail} disabled={busy} onChange={event=>void run(()=>api('/connections/microsoft','PUT',{mail:event.target.checked}))}/>Outlook inbox</label>
      <label className="dev-check"><input type="checkbox" checked={state.teams} disabled={busy} onChange={event=>void run(()=>api('/connections/microsoft','PUT',{teams:event.target.checked}))}/>Teams chats</label></div>
    {state.account&&<p className="fe-muted">Changing what's read takes effect when you reconnect.</p>}
  </section>;
}
