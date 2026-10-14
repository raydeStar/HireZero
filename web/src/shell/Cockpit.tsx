import {useState} from 'react';
import {Check,ChevronDown,ChevronRight,ChevronUp,CircleAlert,GripVertical,PanelRightClose,X} from 'lucide-react';
import {Raven} from '../components/Raven';
import {when,taskTitle,type AgentState,type AgentTask,type Session,type SharedWork,type ShiftSchedule,type WorkRequest,titleOf} from './api';
import type {RoleActivity,RoleUi} from '../roles/types';
import {SessionsCockpit} from './Sessions';
import {WorkLogGlance} from './WorkLog';
import {OwnerTasks} from './OwnerTasks';
import {ShiftProgress} from './ShiftProgress';
import {BudgetPanel} from './Budget';

interface CockpitProps{session:Session;requests:WorkRequest[];workload:SharedWork[];order:string[];activity:string|null;roleActivity?:RoleActivity;roles:RoleUi[];agent:AgentState|null;onForbidden:()=>void;
  onOpen:(id:string)=>void;onOrder:(ids:string[])=>Promise<void>;onClose:()=>void;onBoard:()=>void;
  onSessions:()=>void;onCancelTask:(id:string)=>Promise<void>;onArchive:(id:string)=>Promise<void>;onShift:(action:'start'|'stop',hours?:number)=>Promise<void>;onSchedule:(schedule:ShiftSchedule)=>Promise<void>;}
const dayNames=['Sun','Mon','Tue','Wed','Thu','Fri','Sat'];
const time=(iso:string|null)=>iso?new Date(iso).toLocaleTimeString([],{hour:'numeric',minute:'2-digit'}):'';
const hoursLabel=(s:ShiftSchedule)=>s.days.length?s.days.map(day=>dayNames[day]).join(', ')+' · '+s.start+'–'+s.end:'No working days';
/** Adapted from marketing-hire's Cockpit: the agent's shift, what it's doing, and its own queue. */
export function Cockpit({session,requests,workload,order,activity,roleActivity,roles,agent,onForbidden,onOpen,onOrder,onClose,onBoard,onSessions,onCancelTask,onArchive,onShift,onSchedule}:CockpitProps){
  const [dragging,setDragging]=useState<string|null>(null),[error,setError]=useState(''),[editing,setEditing]=useState(false),[pending,setPending]=useState<string|null>(null);
  const owner=session.role==='owner';
  const intake=requests.filter(item=>item.status==='queued'&&item.id!==roleActivity?.requestId).sort((a,b)=>{const x=order.indexOf(a.id),y=order.indexOf(b.id);return (x<0?1e9:x)-(y<0?1e9:y)||Date.parse(a.createdAt)-Date.parse(b.createdAt);});
  const running=agent?.tasks.find(task=>task.state==='active'&&agent.runningIds.includes(task.id)),queue=agent?.tasks.filter(task=>task.state==='queued')??[],shift=agent?.shift;
  const current=requests.find(item=>item.id===(owner?roleActivity?.requestId:activity)),needs=workload.filter(item=>['blocked','review'].includes(item.status));
  const fixLabel=roleActivity?.label??null;
  const busy=!!running||!!roleActivity||(!owner&&!!activity)||agent?.progress.state==='Scanning';
  const label=running?(running.kind==='diagnose-pr'?'Diagnosing a PR':'Writing a reply'):fixLabel||(agent?.progress.state==='Scanning'?'Checking sources':!owner&&activity?'Writing a reply':shift?.on?'On shift':'Off shift · answers when asked');
  async function act(key:string,work:()=>Promise<void>){setPending(key);setError('');try{await work();}catch(cause){setError((cause as Error).message);}finally{setPending(null);}}
  async function move(id:string,to:number){const ids=intake.map(item=>item.id).filter(value=>value!==id);ids.splice(to,0,id);await act('order',()=>onOrder(ids));}
  const taskRow=(task:AgentTask,active=false)=><li key={task.id}>
    <button className="fe-up-next-title" onClick={()=>task.requestId&&onOpen(task.requestId)} disabled={!task.requestId} title={task.title}><span>{taskTitle(task)}</span><small>{active?'Now':task.source==='ask'?'Your ask':'Proactive'}</small></button>
    {owner&&<button className="fe-icon-button" aria-label={(active?'Stop ':'Cancel ')+taskTitle(task)} title={active?'Stop: the current step finishes, its result is discarded':'Cancel'} disabled={pending===task.id} onClick={()=>void act(task.id,()=>onCancelTask(task.id))}><X size={13}/></button>}</li>;
  return <aside className="fe-cockpit" aria-label="Cockpit"><header className="fe-cockpit-head"><h2>Cockpit</h2><button className="fe-icon-button" aria-label="Hide cockpit" onClick={onClose}><PanelRightClose size={17}/></button></header><div className="fe-cockpit-body">
    {(session.role==='boss'||session.role==='manager')&&<WorkLogGlance onForbidden={onForbidden}/>}
    <section className="fe-cockpit-employee"><Raven state={busy?'working':'idle'}/><div><strong>Workspace agent</strong><span className="fe-status-chip"><i className={'fe-dot '+(busy?'busy':shift?.on?'live':'off')}/>{label}</span></div></section>
    {owner&&session.chatOnly&&<section className="fe-cockpit-section" aria-label="PR discovery"><h3>PR discovery</h3><p className="fe-muted">Checks configured GitHub repositories every 15 minutes. Chat is available; coding and publication remain held.</p></section>}
    {owner&&agent&&!session.chatOnly&&<section className="fe-cockpit-section" aria-label="Shift"><h3>Shift</h3>
      {shift?.on?<p className="fe-shift-now"><i className="fe-dot live"/><span>On shift until {time(shift.endsAt)}. Checks enabled sources every 15 minutes ({shift.proactiveDone}/{shift.proactiveBudget} proactive tasks this shift).</span></p>
        :<p className="fe-muted">Off shift. It still answers when you ask; proactive work waits for a shift.</p>}
      {shift?.senseNote&&<ul className="fe-sense" aria-label="What the last check covered">{shift.senseNote.split(/\n+/).filter(Boolean).map(line=>{const first=line.match(/^.*?[.;](\s|$)/)?.[0].trim().replace(/;$/,'.')??line;return <li key={line} title={line}>{first}{first.length<line.trim().length&&<span className="fe-sense-more" aria-hidden="true"> …</span>}</li>;})}</ul>}
      <div className="dev-shift-actions">{shift?.on?<button disabled={pending==='shift'} onClick={()=>void act('shift',()=>onShift('stop'))}>Stop shift</button>:<button className="primary" disabled={pending==='shift'} onClick={()=>void act('shift',()=>onShift('start',8))}>Start an 8-hour shift</button>}
        <button className="fe-ghost" onClick={()=>setEditing(value=>!value)}>{editing?'Close hours':'Working hours'}</button></div>
      <p className="fe-muted">{agent.schedule.enabled?'Starts on its own: '+hoursLabel(agent.schedule):'Working hours are off.'}</p>
      {editing&&<HoursForm schedule={agent.schedule} onSave={async schedule=>{await act('hours',()=>onSchedule(schedule));setEditing(false);}}/>}
    </section>}
    <section className={'fe-cockpit-section fe-working-on'+(busy?' active':'')} aria-label="Working on"><h3>Working on</h3>
      {owner&&<OwnerTasks onOpen={onOpen} onForbidden={onForbidden}/>}
      {owner&&agent&&<ShiftProgress agent={agent}/>}
      {running?<div className="fe-now-card" role="status"><span className="fe-now-pulse"/><div className="fe-now-text"><small>{label}</small>{running.requestId?<button className="fe-now-title" onClick={()=>onOpen(running.requestId!)}>{running.title}</button>:<span className="fe-now-title">{running.title}</span>}</div>{owner&&<button className="fe-icon-button" aria-label={'Stop '+running.title} title="Stop: the current step finishes, its result is discarded" disabled={pending===running.id} onClick={()=>void act(running.id,()=>onCancelTask(running.id))}><X size={14}/></button>}<i className="fe-now-bar"/></div>
        :current?<div className="fe-now-card" role="status"><span className="fe-now-pulse"/><div className="fe-now-text"><small>{fixLabel||'Writing a reply'}</small><button className="fe-now-title" onClick={()=>onOpen(current.id)}>{current.title}</button></div><i className="fe-now-bar"/></div>
        :<p className="fe-cockpit-clear">{shift?.on?'Nothing running. It will pick up the next item or check your PRs.':'Nothing running. Ask in chat, or start a shift.'}</p>}
      {owner&&agent&&<div className="fe-up-next" aria-label="Up next"><h3>Up next <span className="fe-count">{queue.length}</span></h3>
        {queue.length?<ol>{queue.slice(0,10).map(task=>taskRow(task))}</ol>:<small className="fe-muted">The queue is empty.</small>}</div>}
      {owner&&!!agent?.activity?.length&&<div className="fe-up-next" aria-label="Latest steps"><h3>Latest steps</h3><ol className="fe-shift-feed">{agent.activity.slice(0,4).map((entry,index)=><li key={entry.at+index} className={index===0?'latest':''} title={entry.detail||entry.text}><span>{entry.text}</span><small className="fe-muted">{time(entry.at)}</small></li>)}</ol></div>}
      {owner&&!!agent?.recent.length&&<div className="fe-up-next done"><h3>Just done</h3><ol>{agent.recent.map(task=><li key={task.id}><button className={'fe-up-next-title'+(task.state==='failed'?' failed':'')} onClick={()=>task.requestId&&onOpen(task.requestId)} disabled={!task.requestId} title={task.title}>{task.state==='failed'?<CircleAlert size={13} className="dev-task-failed"/>:<Check size={13} className="fe-done-check"/>}<span className="fe-done-text"><span>{taskTitle(task)}</span>{task.state==='failed'?<small className="fe-done-reason">Didn't finish: {task.detail||'no reason recorded'}</small>:task.detail&&task.detail!=='Replied.'?<small className="fe-muted fe-done-reason">{task.detail}</small>:null}</span><small>{when(task.updatedAt)}</small></button></li>)}</ol></div>}
      {intake.length>0&&<details className="fe-up-next" open={!owner}><summary>{owner?'Conversations awaiting review':'Your requests awaiting review'} <span className="fe-count">{intake.length}</span></summary><ol>{intake.slice(0,12).map((item,index)=><li key={item.id} className={dragging===item.id?'dragging':''} draggable={owner} onDragStart={event=>{setDragging(item.id);event.dataTransfer.effectAllowed='move';}} onDragEnd={()=>setDragging(null)} onDragOver={event=>{if(dragging&&dragging!==item.id)event.preventDefault();}} onDrop={event=>{event.preventDefault();if(dragging)void move(dragging,index);setDragging(null);}}>
        {owner&&<span className="fe-drag-grip"><GripVertical size={13}/></span>}<button className="fe-up-next-title" onClick={()=>onOpen(item.id)}><span>{titleOf(item)}</span></button>{owner&&intake.length>1&&<span className="fe-up-next-move"><button className="fe-icon-button" aria-label={'Move '+item.title+' up'} disabled={index===0} onClick={()=>void move(item.id,index-1)}><ChevronUp size={13}/></button><button className="fe-icon-button" aria-label={'Move '+item.title+' down'} disabled={index===intake.length-1} onClick={()=>void move(item.id,index+1)}><ChevronDown size={13}/></button></span>}
        {owner&&<button className="fe-icon-button" aria-label={'Archive '+titleOf(item)} title="Archive this conversation" disabled={pending===item.id} onClick={()=>void act(item.id,()=>onArchive(item.id))}><X size={13}/></button>}</li>)}</ol></details>}
      {error&&<p className="fe-alert" role="alert">{error}</p>}<button className="fe-link" onClick={onBoard}>Open the board</button></section>
    {owner&&!session.chatOnly&&<BudgetPanel onForbidden={onForbidden}/>}
    {owner&&!session.chatOnly&&<SessionsCockpit onForbidden={onForbidden} onAll={onSessions}/>}
    {needs.length>0&&<section className="fe-cockpit-section"><h3>Needs a decision <span className="fe-count attn">{needs.length}</span></h3>{needs.slice(0,4).map(item=><div className="fe-ask" key={item.requestId}><strong>{item.summary}</strong>{item.outcome&&<p>{item.outcome}</p>}{requests.some(request=>request.id===item.requestId)&&<button className="fe-link" onClick={()=>onOpen(item.requestId)}>Discuss <ChevronRight size={13}/></button>}</div>)}</section>}
    {roles.map(role=>role.cockpitSections?<div key={role.id}>{role.cockpitSections({session,onForbidden})}</div>:null)}
  </div></aside>;
}

function HoursForm({schedule,onSave}:{schedule:ShiftSchedule;onSave:(value:ShiftSchedule)=>Promise<void>}){
  const [value,setValue]=useState<ShiftSchedule>({...schedule,timeZone:Intl.DateTimeFormat().resolvedOptions().timeZone||schedule.timeZone});
  const toggle=(day:number)=>setValue(current=>({...current,days:current.days.includes(day)?current.days.filter(item=>item!==day):[...current.days,day].sort()}));
  return <form className="dev-hours" onSubmit={event=>{event.preventDefault();void onSave(value);}}>
    <label className="dev-check"><input type="checkbox" checked={value.enabled} onChange={event=>setValue({...value,enabled:event.target.checked})}/>Start shifts automatically</label>
    <div className="dev-hours-days" role="group" aria-label="Working days">{dayNames.map((name,day)=><label key={name} className="dev-check"><input type="checkbox" checked={value.days.includes(day)} onChange={()=>toggle(day)}/>{name}</label>)}</div>
    <div className="dev-hours-times"><label>From<input type="time" value={value.start} onChange={event=>setValue({...value,start:event.target.value})}/></label><label>To<input type="time" value={value.end} onChange={event=>setValue({...value,end:event.target.value})}/></label></div>
    <small className="fe-muted">Time zone: {value.timeZone}. A day you stop isn't restarted; nothing starts in the last 15 minutes.</small>
    <button className="primary" type="submit">Save hours</button></form>;
}
