import {useEffect,useState} from 'react';
import {ArrowRight,ChevronRight,FileText,MessageSquareText,X} from 'lucide-react';
import Markdown from 'react-markdown';
import {ApiError,api,label,when,type Detail,type Session,type SharedWork,type WorkRequest,titleOf} from './api';
import type {RoleUi} from '../roles/types';
import {InboxView} from './Inbox';
import {SessionsView} from './Sessions';
import {BriefView} from './Brief';
import {MemoryView} from './Memory';
import {BacklogView} from './Backlog';

interface WorkProps{session:Session;roles:RoleUi[];requests:WorkRequest[];workload:SharedWork[];order:string[];onOpen:(id:string)=>void;onNew:()=>void;onRefresh:()=>Promise<void>;onForbidden:()=>void;}
/** Core work views plus each hired role's tabs. */
export function WorkView({session,roles,requests,workload,order,onOpen,onNew,onRefresh,onForbidden}:WorkProps){
  const owner=session.role==='owner',own=session.role==='visitor'||session.role==='trusted';
  const roleTabs=roles.flatMap(role=>role.workTabs??[]).filter(tab=>!tab.ownerOnly||owner);
  const tabs:[string,string][]=[...(owner?[['today','Today'],['inbox','Inbox'],['sessions','Sessions'],['memory','Memory'],['backlog','Backlog']] as [string,string][]:[]),...roleTabs.map(tab=>[tab.key,tab.label] as [string,string]),['requests','Requests'],...(!own?[['attention','Needs you'],['workload','Shared workload'],['outcomes','Recent outcomes']] as [string,string][]:[])];
  const requested=new URLSearchParams(location.search).get('section');
  const [tab,setTab]=useState(tabs.some(([key])=>key===requested)?requested!:tabs[0][0]),[query,setQuery]=useState('');
  const roleTab=roleTabs.find(item=>item.key===tab);
  const shared=workload.filter(item=>tab==='outcomes'?item.status==='done':tab==='attention'?['blocked','review'].includes(item.status):true);
  const shown=requests.filter(item=>[titleOf(item),item.title,item.project,item.description].join(' ').toLowerCase().includes(query.toLowerCase()));
  return <section className="fe-view dev-work"><div className="fe-view-head"><div><h1>Work</h1><p className="fe-muted">Requests, your hired roles' work, and reviewed progress.</p></div><button className="primary" onClick={onNew}>New request <ArrowRight size={15}/></button></div>
    <nav className="fe-tabs fe-view-tabs" aria-label="Work sections">{tabs.map(([key,text])=><button key={key} aria-pressed={tab===key} onClick={()=>setTab(key)}>{text}</button>)}</nav>
    {tab==='backlog'&&owner?<BacklogView onOpen={onOpen} onForbidden={onForbidden}/>:tab==='today'&&owner?<BriefView onOpen={onOpen} onForbidden={onForbidden}/>:tab==='memory'&&owner?<MemoryView onForbidden={onForbidden}/>:tab==='inbox'&&owner?<InboxView onOpen={onOpen} onForbidden={onForbidden}/>:tab==='sessions'&&owner?<SessionsView onForbidden={onForbidden}/>:roleTab?roleTab.render({session,requests,order,onOpen,onRefresh,onForbidden}):tab==='requests'?<><label className="dev-search">Find a request<input type="search" value={query} onChange={event=>setQuery(event.target.value)} placeholder="Search title, project, or requirements"/></label>
      {shown.length===0&&<p className="fe-empty">{query?'No requests match.':'No requests yet. Start a conversation or bring a brief.'}</p>}
      <div className="fe-section">{shown.map(item=><button className="fe-list-row" key={item.id} onClick={()=>onOpen(item.id)}><FileText size={17}/><span className="fe-list-main"><strong>{titleOf(item)}</strong><small>{item.project} · {when(item.createdAt)}</small></span><span className="fe-pill">{label(item.status)}</span><ChevronRight size={15}/></button>)}</div></>
      :<>{shared.length===0&&<p className="fe-empty">{tab==='attention'?'No shared blockers or reviews.':'No approved updates in this view.'}</p>}{shared.map(item=><article className="fe-section dev-shared" key={item.requestId}><div className="fe-section-head"><strong>{item.summary}</strong><span className="fe-pill">{item.status}</span></div><p className="fe-muted">{item.priority} priority · Updated {when(item.updatedAt)}</p>{item.outcome&&<p>{item.outcome}</p>}{requests.some(request=>request.id===item.requestId)&&<button className="fe-link" onClick={()=>onOpen(item.requestId)}>Discuss this request →</button>}</article>)}</>}
  </section>;
}

interface RequestWindowProps{detail:Detail;session:Session;roles:RoleUi[];workload:SharedWork[];initialTab?:string;onClose:()=>void;onChat:()=>void;onRefresh:()=>Promise<void>;onForbidden:()=>void;}
export function RequestWindow({detail,session,roles,workload,initialTab='requirements',onClose,onChat,onRefresh,onForbidden}:RequestWindowProps){
  const owner=session.role==='owner',item=detail.request,shared=workload.find(work=>work.requestId===item.id);
  const roleTabs=roles.flatMap(role=>role.requestTabs??[]).filter(tab=>!tab.ownerOnly||owner);
  const [tab,setTab]=useState(initialTab),[busy,setBusy]=useState(false),[error,setError]=useState(''),[notice,setNotice]=useState('');
  const [summary,setSummary]=useState(shared?.summary||''),[priority,setPriority]=useState(shared?.priority||'normal'),[status,setStatus]=useState(shared?.status||'queued'),[outcome,setOutcome]=useState(shared?.outcome||'');
  useEffect(()=>{setTab(initialTab);},[initialTab]);
  async function run(action:()=>Promise<void>){if(busy)return;setBusy(true);setError('');setNotice('');try{await action();}catch(cause){setError((cause as Error).message);if(cause instanceof ApiError&&cause.status===403)onForbidden();}finally{setBusy(false);}}
  const roleTab=roleTabs.find(value=>value.key===tab);
  return <section className="fe-window" aria-label={'Request '+titleOf(item)}><header className="fe-window-head"><span className="fe-window-icon"><FileText size={16}/></span><div className="fe-window-title"><strong>{titleOf(item)}</strong><small>{item.project} · {label(item.status)}</small></div><button className="fe-icon-button" aria-label="Close request" onClick={onClose}><X size={16}/></button></header>
    <div className="fe-window-body"><div className="fe-task-detail"><nav className="fe-tabs" aria-label="Request details">{[['requirements','Requirements'],...roleTabs.map(value=>[value.key,value.label]),...(owner?[['sharing','Sharing']]:[])].map(([key,text])=><button key={key} aria-pressed={tab===key} onClick={()=>setTab(key)}>{text}</button>)}</nav>
      {error&&<p role="alert" className="fe-alert">{error}</p>}{notice&&<p role="status">{notice}</p>}
      {tab==='requirements'&&<div className="fe-stack"><dl className="fe-facts"><div><dt>Project</dt><dd>{item.project}</dd></div><div><dt>Requested</dt><dd>{when(item.createdAt)}</dd></div><div><dt>State</dt><dd>{label(item.status)}</dd></div></dl><div className="fe-prose"><Markdown>{item.description}</Markdown></div><button onClick={onChat}><MessageSquareText size={15}/> Discuss with the agent</button>{item.semaForgeId&&<p className="fe-muted">Linked task: {item.semaForgeId}</p>}</div>}
      {roleTab?.render({detail,session,onRefresh,onForbidden})}
      {tab==='sharing'&&owner&&<form className="fe-form dev-share" onSubmit={event=>{event.preventDefault();void run(async()=>{await api('/requests/'+item.id+'/share','PUT',{summary,priority,status,outcome:outcome||null});await onRefresh();setNotice('Reviewed workload update published.');});}}><h3>Reviewed manager update</h3><p className="fe-muted">Managers see only these approved fields. Inspect the text before publishing it.</p><label>Approved summary<textarea required maxLength={500} value={summary} onChange={event=>setSummary(event.target.value)}/></label><div className="dev-form-row"><label>Priority<select value={priority} onChange={event=>setPriority(event.target.value)}>{['low','normal','high'].map(value=><option key={value}>{value}</option>)}</select></label><label>Status<select value={status} onChange={event=>setStatus(event.target.value)}>{['queued','active','blocked','review','done'].map(value=><option key={value}>{value}</option>)}</select></label></div><label>Blocker or outcome<textarea maxLength={500} value={outcome} onChange={event=>setOutcome(event.target.value)}/></label><button className="primary" disabled={busy}>Publish reviewed update</button></form>}
    </div></div>
  </section>;
}
