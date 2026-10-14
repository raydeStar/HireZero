import {useCallback,useEffect,useState} from 'react';
import {TrustedPostgresSettings} from './TrustedPostgres';
import {GitHubSignIn} from './GitHubSignIn';
import {CheckCircle2} from 'lucide-react';
import {api,ApiError,type WorkRequest} from '../../shell/api';
import type {RoleUi,RequestTabContext,SectionContext,WorkTabContext} from '../types';
import {CodeReviews} from './CodeReviews';
import {PreparationPanel} from './PreparationPanel';
import {RepositoryChatActions} from './RepositoryChatActions';
import {WikiUpdates} from './WikiUpdates';
import {HomeApprovals} from './HomeApprovals';
import {preparationStages,type Context,type Preparation,type PreparationStage,type Sources,type Triage,type WikiScopeReview} from './types';

/** The developer role's contributions to the shell: its Work tabs, request tab, chat actions and status. */
export const developer:RoleUi={
  id:'developer',name:'Developer',
  homeActions:context=>context.session.role==='owner'?<HomeApprovals onForbidden={context.onForbidden}/>:null,
  workTabs:[
    {key:'queue',label:'Local queue',ownerOnly:true,render:context=><DeveloperQueue {...context}/>},
    {key:'wiki',label:'Wiki updates',ownerOnly:true,render:context=><WikiUpdates requests={context.requests} onOpen={context.onOpen} onChanged={context.onRefresh} onForbidden={context.onForbidden}/>},
    {key:'reviews',label:'Code reviews',ownerOnly:true,render:context=><CodeReviews onForbidden={context.onForbidden}/>}],
  requestTabs:[{key:'preparation',label:'Preparation',ownerOnly:true,render:context=><DeveloperPreparation {...context}/>}],
  conversationActions:context=>context.session.role==='owner'?<RepositoryChatActions requestId={context.requestId} messageCount={context.messageCount} onForbidden={context.onForbidden}/>:null,
  cockpitSections:context=>context.session.role==='owner'?<><GitHubSignIn/><Connections {...context} compact/></>:null,
  settingsSections:context=>context.session.role==='owner'?<><Connections {...context}/><TrustedPostgresSettings onForbidden={context.onForbidden}/></>:null,
  activity:{path:'/github/activity',describe:value=>{const item=value as {requestId:string;state:string;workerState?:string|null};
    return {requestId:item.requestId,label:item.state==='coding'?(item.workerState==='queued'?'Queued for Sol':'Sol is working'):item.state==='validating'?'Validating the fix':'Preparing the fix'};}},
};

function usePreparation(onForbidden:()=>void){
  const [items,setItems]=useState<Preparation[]>([]);
  const load=useCallback(async()=>{try{setItems(await api<Preparation[]>('/preparation'));}catch(cause){if(cause instanceof ApiError&&cause.status===403)onForbidden();}},[onForbidden]);
  useEffect(()=>{void load();},[load]);
  return [items,load] as const;
}

function Connections({onForbidden,compact}:SectionContext&{compact?:boolean}){
  const [sources,setSources]=useState<Sources|null>(null);
  useEffect(()=>{api<Sources>('/source-status').then(setSources).catch(cause=>{if(cause instanceof ApiError&&cause.status===403)onForbidden();});},[onForbidden]);
  const rows=sources?Object.entries(sources).map(([name,value])=><div className="dev-connection" key={name}><span>{name==='semaForge'?'SemaForge':name==='wiki'?'Wiki pilot':'Jev'}</span><span><i className={'fe-dot '+(['online','configured'].includes(value)?'live':'warn')}/>{value}</span></div>):<p className="fe-muted">Checking local services…</p>;
  return compact?<section className="fe-cockpit-section"><h3>Local connections</h3>{rows}<p className="fe-muted">Private evidence stays in Preparation.</p></section>
    :<section className="fe-section"><h2>Local connections</h2>{rows}<p className="fe-muted">Private context is retrieved on demand in a request’s Preparation tab.</p></section>;
}

function DeveloperPreparation({detail,onRefresh,onForbidden}:RequestTabContext){
  const item=detail.request,[preparation,reload]=usePreparation(onForbidden);
  const [busy,setBusy]=useState(false),[error,setError]=useState(''),[notice,setNotice]=useState('');
  const [triage,setTriage]=useState<Triage|null>(null),[context,setContext]=useState<Context|null>(null);
  async function run(action:()=>Promise<void>){if(busy)return;setBusy(true);setError('');setNotice('');try{await action();}catch(cause){setError((cause as Error).message);if(cause instanceof ApiError&&cause.status===403)onForbidden();}finally{setBusy(false);}}
  return <div className="fe-stack">{error&&<p role="alert" className="fe-alert">{error}</p>}{notice&&<p role="status">{notice}</p>}
    <PreparationPanel requestId={item.id} preparation={preparation.find(value=>value.requestId===item.id)} onRefresh={async()=>{await reload();await onRefresh();}} onForbidden={onForbidden}/>
    <section><h3>Routing recommendation</h3><p className="fe-muted">Jev recommends a route. Review it before changing work.</p><button disabled={busy} onClick={()=>void run(async()=>{setTriage(await api<Triage>('/requests/'+item.id+'/triage'));})}>Suggest triage</button>{triage&&<div className="fe-notice" role="status"><span><strong>{triage.route||triage.status}</strong>{triage.explanation}{triage.candidateRequestId&&<small>Candidate request: {triage.candidateRequestId}</small>}</span></div>}</section>
    <section><h3>Bounded context</h3><p className="fe-muted">Private source packets stay in this owner view.</p><button disabled={busy} onClick={()=>void run(async()=>{setContext(await api<Context>('/requests/'+item.id+'/context'));})}>Inspect context</button>{context&&<>{(['semaForge','wiki'] as const).map(name=><details key={name} className="dev-evidence"><summary>{name==='semaForge'?'SemaForge':'Wiki pilot'} · {context[name].status}</summary>{context[name].packet!=null?<pre>{JSON.stringify(context[name].packet,null,2)}</pre>:<p>No packet is available.</p>}</details>)}</>}</section>
    <section><h3>Project knowledge</h3><p className="fe-muted">Prepare a reviewed Wiki update from requirements or development findings.</p><a className="fe-link" href={'/?pane=work&section=wiki&project='+encodeURIComponent(item.project)}>Open project Wiki drafts</a></section>
    <section><h3>Promote into SemaForge</h3><p className="fe-muted">This records a task. Execution approval is separate.</p><button disabled={busy||item.status!=='queued'} onClick={()=>void run(async()=>{await api('/requests/'+item.id+'/approve','POST',{});await onRefresh();setNotice('Task recorded in SemaForge.');})}><CheckCircle2 size={15}/> Approve task</button>{item.status==='promotion-pending'&&<p role="status">The result is uncertain. Reconcile in SemaForge before retrying.</p>}</section></div>;
}

function DeveloperQueue({requests,order,onOpen,onForbidden}:WorkTabContext){
  const [preparation,reload]=usePreparation(onForbidden),[scopeReviews,setScopeReviews]=useState<WikiScopeReview[]>([]);
  useEffect(()=>{api<WikiScopeReview[]>('/wiki/scope-reviews').then(setScopeReviews).catch(cause=>{if(cause instanceof ApiError&&cause.status===403)onForbidden();});},[onForbidden]);
  async function stage(id:string,next:PreparationStage){const previous=preparation.find(item=>item.requestId===id);
    await api('/requests/'+id+'/preparation','PUT',{stage:next,blocker:previous?.blocker||'',nextAction:previous?.nextAction||''});await reload();}
  return <QueueBoard requests={requests} preparation={preparation} order={order} scopeReviews={scopeReviews} onOpen={onOpen} onStage={stage}/>;
}

interface QueueProps{requests:WorkRequest[];preparation:Preparation[];order:string[];scopeReviews:WikiScopeReview[];onOpen:(id:string)=>void;onStage:(id:string,stage:PreparationStage)=>Promise<void>;}
function QueueBoard({requests,preparation,order,scopeReviews,onOpen,onStage}:QueueProps){
  const [drag,setDrag]=useState<string|null>(null),[busy,setBusy]=useState(false),[error,setError]=useState('');
  const queued=requests.filter(item=>item.status==='queued').sort((a,b)=>order.indexOf(a.id)-order.indexOf(b.id));
  async function move(id:string,stage:PreparationStage){if(busy)return;setBusy(true);setError('');try{await onStage(id,stage);}catch(cause){setError((cause as Error).message);}finally{setBusy(false);setDrag(null);}}
  return <><p className="fe-muted">Your private preparation queue. Scope and missing information are blockers to resolve before approval. Moving a card does not start development.</p>{error&&<p className="fe-alert" role="alert">{error}</p>}<div className="dev-kanban" aria-label="Local preparation queue">{preparationStages.map(([stage,title])=>{const cards=queued.filter(item=>(preparation.find(prep=>prep.requestId===item.id)?.stage||'intake')===stage);return <section className="dev-kanban-column" key={stage} aria-label={title} onDragOver={event=>{if(drag&&!busy)event.preventDefault();}} onDrop={event=>{event.preventDefault();if(drag)void move(drag,stage);}}><header><h3>{title}</h3><span className="fe-count">{cards.length}</span></header>{cards.map(item=>{const prep=preparation.find(value=>value.requestId===item.id);return <article className="dev-queue-card" draggable={!busy} key={item.id} onDragStart={event=>{event.dataTransfer.setData('text/plain',item.id);setDrag(item.id);}} onDragEnd={()=>setDrag(null)}><small>{item.project}</small><button onClick={()=>onOpen(item.id)}><strong>{item.title}</strong></button>{scopeReviews.some(review=>review.requestId===item.id)&&<p className="dev-blocker"><strong>Wiki changed:</strong> review this request against the current spec.</p>}{prep?.blocker&&<p className="dev-blocker"><strong>Blocked:</strong> {prep.blocker}</p>}{prep?.nextAction&&<p><strong>Next:</strong> {prep.nextAction}</p>}<select aria-label={'Preparation stage for '+item.title} value={stage} disabled={busy} onChange={event=>void move(item.id,event.target.value as PreparationStage)}>{preparationStages.map(([key,text])=><option value={key} key={key}>{text}</option>)}</select></article>;})}{!cards.length&&<p className="fe-muted">No requests here.</p>}</section>;})}</div></>;
}
