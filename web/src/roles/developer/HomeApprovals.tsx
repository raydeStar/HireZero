import {useEffect,useState} from 'react';
import {api,ApiError} from '../../shell/api';
import type {PullRequestWorkflow} from './github';
import {PullRequestCard} from './PullRequestChatActions';
import {approvalEvent} from './approvalUpdates';

type Pending={workflow:PullRequestWorkflow;allowed:boolean;blocker:string|null};
export function HomeApprovals({onForbidden}:{onForbidden:()=>void}){
  const [items,setItems]=useState<Pending[]>([]),[error,setError]=useState(''),[fresh,setFresh]=useState(false);
  useEffect(()=>{let live=true,sequence=0;
    async function load(){const current=++sequence;try{const value=await api<Pending[]>('/github/pending-approvals');if(live&&current===sequence){setItems(value);setError('');setFresh(true);}}
      catch(cause){if(live&&current===sequence){setFresh(false);setError('Approval status could not refresh. Open the conversation or retry when connected.');if(cause instanceof ApiError&&cause.status===403)onForbidden();}}}
    const changed=()=>{setFresh(false);void load();};void load();window.addEventListener(approvalEvent,changed);
    const timer=setInterval(()=>{if(document.visibilityState==='visible')void load();},8000);
    return()=>{live=false;clearInterval(timer);window.removeEventListener(approvalEvent,changed);};
  },[onForbidden]);
  if(!items.length&&!error)return null;
  return <section aria-label="Pending approvals" className="fe-stack"><h3>Waiting for you ({items.length})</h3>
    {error&&<p role="alert" className="fe-alert">{error}</p>}{items.map(item=><div key={item.workflow.id}>
      <a className="fe-link" href={'/?pane=chat&open='+encodeURIComponent(item.workflow.requestId)}>Open the full conversation</a>
      {item.blocker&&<p className="fe-alert">{item.blocker}</p>}
      <fieldset disabled={!fresh||!item.allowed} style={{border:0,padding:0,margin:0}}><PullRequestCard workflow={item.workflow} superseded={!fresh||!item.allowed} onChanged={()=>window.dispatchEvent(new Event(approvalEvent))} onForbidden={onForbidden}/></fieldset>
    </div>)}</section>;
}
