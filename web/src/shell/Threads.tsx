import {useCallback,useEffect,useRef,useState} from 'react';
import {House,PanelLeftClose,Pencil,Plus,Search} from 'lucide-react';
import {ApiError,api,titleOf,when,type WorkRequest} from './api';

export type ConversationSummary={id:string;project:string;title:string;status:string;createdAt:string;requester:string;lastAt:string;lastFromAgent:boolean;messages:number;match:string|null;fullTitle:string;home?:boolean;agentAsked?:boolean;workState?:{state:string;reason:string;at:string;freshness:string}};

const ago=(at:string)=>{const minutes=Math.round((Date.now()-Date.parse(at))/60000);return minutes<1?'now':minutes<60?minutes+'m':minutes<1440?Math.round(minutes/60)+'h':minutes<10080?Math.round(minutes/1440)+'d':when(at);};
// "Example/Example.EventHub #32" reads as "EventHub #32": the org (and an org-named prefix) adds nothing in a list.
export const shortRepo=(text:string)=>text.replace(/\b([\w-]+)\/(?:\1\.)?([\w.-]+)/g,'$2');

// Sections answer "what needs me, what's moving, what happened" in that order.
const needsYou=new Set(['Waiting for you']),moving=new Set(['Running','Queued','Paused','Blocked']);
const tone:Record<string,string>={'Waiting for you':'attn',Running:'live',Queued:'live',Paused:'idle',Blocked:'bad',Answered:'idle',Completed:'ok',Failed:'bad',Cancelled:'idle',Superseded:'idle'};
const recentShown=12;

/** The conversation list: Home, then what needs you, what's in progress, and recent history. Search reaches every message. */
export function Threads({openId,version,owner,login,onOpen,onNew,onClose,onForbidden}:{openId:string|null;version:number;busyIds:Set<string>;owner:boolean;login:string;onOpen:(id:string)=>void;onNew:()=>void;onClose:()=>void;onForbidden:()=>void}){
  const [items,setItems]=useState<ConversationSummary[]|null>(null),[query,setQuery]=useState(''),[archived,setArchived]=useState(false),[more,setMore]=useState(false),[error,setError]=useState('');
  const forbidden=useRef(onForbidden);forbidden.current=onForbidden;
  const load=useCallback(async(text:string)=>{try{setItems(await api<ConversationSummary[]>('/conversations'+(text.trim()?'?q='+encodeURIComponent(text.trim()):'')));setError('');}
    catch(cause){if(cause instanceof ApiError&&cause.status===403)forbidden.current();else setError('Couldn’t refresh the list; states may be out of date.');}},[]);
  useEffect(()=>{const timer=setTimeout(()=>void load(query),query?250:0);return()=>clearTimeout(timer);},[query,load]);
  useEffect(()=>{const timer=setInterval(()=>{if(document.visibilityState==='visible')void load(query);},10000);return()=>clearInterval(timer);},[query,load]);
  useEffect(()=>{void load(query);},[openId,version]);// eslint-disable-line react-hooks/exhaustive-deps

  const searching=query.trim().length>0,home=(items??[]).find(item=>item.home);
  const stateOf=(item:ConversationSummary)=>error&&item.workState?.state==='Running'?'Paused':item.workState?.state||'Answered';
  const visible=(items??[]).filter(item=>!item.home&&(searching||archived||item.status!=='archived'));
  const waiting=visible.filter(item=>needsYou.has(stateOf(item)));
  const active=visible.filter(item=>moving.has(stateOf(item)));
  const rest=visible.filter(item=>!needsYou.has(stateOf(item))&&!moving.has(stateOf(item)));
  const hidden=(items??[]).filter(item=>item.status==='archived').length;

  const row=(item:ConversationSummary)=>{const state=stateOf(item);return <li key={item.id}>
    <button className={'fe-thread-row'+(item.id===openId?' current':'')} aria-current={item.id===openId?'page':undefined} onClick={()=>onOpen(item.id)}
      title={item.fullTitle+'\n'+state+': '+(item.workState?.reason??'')}>
      <span className="fe-thread-top"><i className={'th-dot '+(tone[state]??'idle')} aria-hidden="true"/><span className="fe-thread-title">{shortRepo(item.title)}</span><small className="fe-muted">{ago(item.lastAt)}</small></span>
      <small className="fe-thread-meta"><span className={'th-state '+(tone[state]??'idle')}>{state}</span>{' · '}{shortRepo(item.project)}
        {owner&&item.requester!==login&&item.requester.includes('@')&&!item.requester.endsWith('workspace.invalid')?' · '+item.requester.split('@')[0]:''}{item.status==='archived'?' · archived':''}</small>
      {item.match&&<small className="fe-thread-match">{item.match}</small>}
    </button></li>;};
  const section=(title:string,list:ConversationSummary[],className='')=>list.length>0&&<><h4 className={className}>{title} <span className="fe-count">{list.length}</span></h4><ol>{list.map(row)}</ol></>;

  return <aside className="fe-threads" aria-label="Conversations">
    <div className="fe-threads-head"><strong>Conversations</strong>
      <button className="fe-icon-button" aria-label="New conversation" title="New conversation" onClick={onNew}><Plus size={16}/></button>
      <button className="fe-icon-button" aria-label="Hide conversations" title="Hide conversations" onClick={onClose}><PanelLeftClose size={16}/></button></div>
    <label className="fe-threads-search"><Search size={14}/><input type="search" aria-label="Search conversations" value={query} maxLength={200} placeholder="Search titles and messages" onChange={event=>setQuery(event.target.value)}/></label>
    {error&&<p className="fe-alert" role="status">{error}</p>}
    <div className="fe-threads-body">{!items?<p className="fe-muted" role="status">Loading…</p>:<>
      {home&&!searching&&<button className={'fe-thread-row fe-thread-home'+(home.id===openId?' current':'')} aria-current={home.id===openId?'page':undefined} onClick={()=>onOpen(home.id)}>
        <span className="fe-thread-top"><House size={15} aria-hidden="true"/><span className="fe-thread-title">Home</span>{home.lastFromAgent&&home.id!==openId&&<i className="th-dot attn" title="New from the agent"/>}<small className="fe-muted">{ago(home.lastAt)}</small></span></button>}
      {searching?<>{section(visible.length+' match'+(visible.length===1?'':'es'),visible)}{!visible.length&&<p className="fe-muted">Nothing matches “{query.trim()}”.</p>}</>:<>
        {section('Needs you',waiting,'attn')}
        {section('In progress',active)}
        {rest.length>0&&<><h4>Recent</h4><ol>{(more?rest:rest.slice(0,recentShown)).map(row)}</ol>
          {rest.length>recentShown&&<button className="th-more" onClick={()=>setMore(value=>!value)}>{more?'Show fewer':'Show '+(rest.length-recentShown)+' more'}</button>}</>}
        {!visible.length&&<p className="fe-muted">No conversations yet.</p>}
      </>}
      {!searching&&hidden>0&&<label className="fe-threads-archived"><input type="checkbox" checked={archived} onChange={event=>setArchived(event.target.checked)}/>Show archived ({hidden})</label>}
    </>}</div>
  </aside>;
}

/** The open thread's name in the top bar; click to rename it (empty goes back to the automatic name). */
export function ThreadName({request,canRename,onRenamed}:{request:WorkRequest;canRename:boolean;onRenamed:(next:WorkRequest)=>void}){
  const [editing,setEditing]=useState(false),[value,setValue]=useState(''),[busy,setBusy]=useState(false),[error,setError]=useState('');
  const shown=shortRepo(titleOf(request));
  async function save(){setBusy(true);setError('');try{onRenamed(await api<WorkRequest>('/requests/'+request.id+'/title','PUT',{title:value.trim()||null}));setEditing(false);}catch(cause){setError((cause as Error).message);}finally{setBusy(false);}}
  if(!canRename)return <span className="fe-mode-open" title={request.title}>{shown}</span>;
  if(!editing)return <button className="fe-mode-open fe-thread-name" title={'Rename · typed as: '+request.title} onClick={()=>{setValue(shown);setEditing(true);}}>{shown}<Pencil size={12} aria-hidden="true"/></button>;
  return <form className="fe-thread-rename" onSubmit={event=>{event.preventDefault();void save();}}>
    <input autoFocus aria-label="Conversation name" value={value} maxLength={60} disabled={busy} placeholder="Leave empty for the automatic name" onChange={event=>setValue(event.target.value)} onKeyDown={event=>{if(event.key==='Escape'){event.stopPropagation();setEditing(false);}}} onBlur={()=>{if(!busy)setEditing(false);}}/>
    {error&&<small className="fe-alert" role="alert">{error}</small>}
  </form>;
}
