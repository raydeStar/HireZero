import {Fragment,useEffect,useRef,useState} from 'react';
import {ArrowUp,MessageSquareText} from 'lucide-react';
import {EmployeeBrief,marketingFields} from '../roles/marketing/EmployeeBrief';
import type {Employee,Event,Snapshot,ThreadDetail} from './api';
import {ConversationJob} from './ConversationJob';

export type ConversationDraft={body:string;skill:string;brief:Record<string,string>;evidence:string[];recipient:string};
const emptyDraft=():ConversationDraft=>({body:'',skill:'chat',brief:{},evidence:[],recipient:''});
type Props={snapshot:Snapshot;events:Event[];employee?:Employee;company:boolean;thread:string;detail:ThreadDetail|null;busy:boolean;drafts:Map<string,ConversationDraft>;updateDraft:(scope:string,update:(current:ConversationDraft|undefined)=>ConversationDraft)=>void;run:(action:string,fields:Record<string,unknown>)=>Promise<unknown>;refresh:()=>Promise<void>;open:(employeeId:string,threadId:string)=>void;createThread:()=>Promise<void>};

export function CompanyConversation({snapshot,events,employee,company,thread,detail,busy,drafts,updateDraft,run,refresh,open,createThread}:Props){
  const scope=company?'company':(employee?.id||'missing')+':'+thread;
  const draft=drafts.get(scope)??emptyDraft();
  const [notice,setNotice]=useState(''),[localError,setLocalError]=useState('');
  const sending=useRef(false),scroll=useRef<HTMLDivElement>(null),stick=useRef(true),input=useRef<HTMLTextAreaElement>(null),mounted=useRef(true);
  const d=snapshot.data,role=d.roles.find(r=>r.id===employee?.roleId),ready=company||!!employee&&detail?.thread.id===thread&&detail.thread.employeeId===employee.id;
  const tasks=company?[]:d.tasks.filter(t=>t.employeeId===employee?.id&&t.threadId===thread);
  const artifacts=d.artifacts.filter(a=>tasks.some(t=>t.id===a.taskId));
  const messages=ready&&!company?detail!.messages.filter(m=>m.author==='owner'||!artifacts.some(a=>a.body===m.body)):[];
  const orphaned=tasks.filter(t=>!messages.some(m=>m.sequence===t.watermark));
  function update(value:Partial<ConversationDraft>){updateDraft(scope,previous=>({...previous??emptyDraft(),...value}));}
  useEffect(()=>{mounted.current=true;return()=>{mounted.current=false;};},[]);
  useEffect(()=>{if(scroll.current&&stick.current)scroll.current.scrollTop=scroll.current.scrollHeight;},[messages.length,tasks.length,artifacts.length,events.length]);
  async function send(){
    if(sending.current||busy||!ready||(!company&&employee?.state!=='active'))return;
    const sent=draft,body=!company&&sent.skill==='marketing-draft'?JSON.stringify(sent.brief):sent.body;
    if(!body.trim())return;
    if(!company&&sent.skill==='marketing-draft'&&marketingFields.some(field=>!sent.brief[field]?.trim())){setLocalError('Complete every campaign brief field.');return;}
    sending.current=true;setLocalError('');setNotice('');
    try{
      const target=company?d.employees.find(e=>e.id===sent.recipient):employee;
      if(company&&sent.recipient&&!target){setLocalError('Choose an available employee.');return;}
      const targetThread=company?d.threads.find(t=>t.employeeId===target?.id)?.id:thread;
      const result=await run(company?'company.message':'message.send',company?{body,employeeId:target?.id,threadId:targetThread}:{employeeId:employee!.id,threadId:thread,body,skill:sent.skill,evidence:sent.evidence});
      if(!result)return;
      // Clear only the exact submitted draft. An in-flight completion cannot erase another edit.
      updateDraft(scope,current=>JSON.stringify(current??sent)===JSON.stringify(sent)?{...sent,body:'',brief:{}}:current??sent);
      if(mounted.current){setNotice(company&&!target?'Owner note saved.':snapshot.capabilities.modelExecution?'Message saved to the test queue.':'Message saved. Automatic replies are unavailable; eligible drafts need your explicit job approval.');input.current?.focus();}
    }finally{sending.current=false;}
  }
  const job=(task:typeof tasks[number])=><ConversationJob key={task.id} task={task} snapshot={snapshot} busy={busy} run={run} refresh={refresh} open={open}/>;
  return <section className="fe-chat co-conversation" aria-label={company?'Company conversation':'Private employee conversation'}>
    <div className="co-conversation-context">
      <p>{company?'Your company conversation. Record an owner note, or address an employee in their private conversation. You approve work and spending.':employee?.responsibility||'Choose an employee conversation from the sidebar.'}</p>
      <small>{snapshot.capabilities.modelExecution?'Synthetic provider for this disposable test.':snapshot.capabilities.measuredOneJob?'Native connection: manually approved internal drafts only. Ordinary chat has no live reply provider.':'Live replies unavailable. Messages and work remain saved locally.'}</small>
      {employee&&<><div className="co-actions"><label>Conversation<select aria-label="Employee conversation" value={thread} onChange={e=>open(employee.id,e.target.value)}>{d.threads.filter(t=>t.employeeId===employee.id).map(t=><option key={t.id} value={t.id}>{t.title}</option>)}</select></label><button disabled={busy||employee.state==='retired'} onClick={()=>void createThread()}>New conversation</button></div>{ready&&<PrivateContext employee={employee} detail={detail!} busy={busy} run={run}/>}</>}
    </div>
    <div className="fe-chat-scroll" ref={scroll} onScroll={e=>{const el=e.currentTarget;stick.current=el.scrollHeight-el.scrollTop-el.clientHeight<100;}}><div className="fe-thread">
      {company?<>
        {!events.length&&<div className="fe-hello"><h2>What should we work on?</h2><p>Start with a company note, or open an employee conversation. Board meetings and CEO plans remain available from the rail.</p></div>}
        {events.map(event=><article key={event.sequence} className={'fe-msg '+(event.type==='owner.note'?'user':'assistant')}><div className="fe-msg-body"><div className="fe-msg-meta"><strong>{event.type==='owner.note'?'You':'Company update'}</strong><time>{new Date(event.at).toLocaleString()}</time></div><div className="fe-msg-content"><p>{event.text}</p></div>{event.employeeId&&<button onClick={()=>{const target=d.threads.find(t=>t.employeeId===event.employeeId);if(target)open(event.employeeId!,target.id);}}>Open employee conversation</button>}</div></article>)}
      </>:!ready?<p role="status">{employee&&thread?'Opening this conversation.':'Choose a conversation or create one for this employee.'}</p>:<>
        {!messages.length&&!tasks.length&&<div className="fe-hello"><MessageSquareText size={24}/><h2>Conversation with {employee!.name}</h2><p>Your messages, task preparation, approvals and saved results stay together here. Sending a message does not authorize a native model request.</p></div>}
        {messages.map(message=><Fragment key={message.id}><article className={'fe-msg '+(message.author==='owner'?'user':'assistant')}><div className="fe-msg-body"><div className="fe-msg-meta"><strong>{message.author==='owner'?'You':employee!.name}</strong></div><div className="fe-msg-content"><p>{message.body}</p></div></div></article>{tasks.filter(t=>t.watermark===message.sequence).map(job)}</Fragment>)}
        {orphaned.map(job)}
      </>}
    </div></div>
    <div className="fe-composer-wrap co-conversation-composer">
      {localError&&<p role="alert">{localError}</p>}{notice&&<p role="status" className="co-send-status">{notice}</p>}
      <form onSubmit={e=>{e.preventDefault();void send();}}>
        {company?<label>Address to<select value={draft.recipient} onChange={e=>update({recipient:e.target.value})}><option value="">Owner note</option>{d.employees.filter(e=>e.state==='active').map(e=><option key={e.id} value={e.id}>{e.name} · private conversation</option>)}</select></label>:<label>Work type<select value={draft.skill} disabled={!ready||busy} onChange={e=>update({skill:e.target.value})}>{role?.skills.filter(s=>s!=='meeting-turn'&&s!=='meeting-chair').map(s=><option key={s} value={s}>{s==='chat'?'Conversation':s}</option>)}</select></label>}
        {!company&&draft.skill==='marketing-draft'?<details open className="co-brief-composer"><summary>Campaign brief</summary><EmployeeBrief value={draft.brief} onChange={brief=>update({brief})}/></details>:null}
        {!company&&<details className="co-evidence"><summary>Attach approved evidence ({draft.evidence.length})</summary>{d.artifacts.filter(a=>a.state==='approved').map(a=><label className="co-check" key={a.id}><input type="checkbox" checked={draft.evidence.includes(a.id)} onChange={e=>update({evidence:e.target.checked?[...draft.evidence,a.id]:draft.evidence.filter(id=>id!==a.id)})}/>{d.employees.find(e=>e.id===a.employeeId)?.name}: {a.body.slice(0,80)}</label>)}</details>}
        <div className="fe-composer">{(company||draft.skill!=='marketing-draft')&&<textarea ref={input} aria-label={company?'Company message':'Message to '+(employee?.name||'employee')} rows={2} maxLength={4000} required value={draft.body} disabled={busy||!ready||(!company&&employee?.state!=='active')} onChange={e=>update({body:e.target.value})} onKeyDown={e=>{if(e.key==='Enter'&&!e.shiftKey&&!e.nativeEvent.isComposing){e.preventDefault();void send();}}} placeholder={company?'Message your company.':'Message '+(employee?.name||'this employee')+'.'}/>}<button className="fe-send" type="submit" aria-label={company?'Send company message':draft.skill==='chat'?'Send message':'Assign work'} disabled={busy||!ready||(!company&&employee?.state!=='active')||((company||draft.skill!=='marketing-draft')&&!draft.body.trim())}><ArrowUp size={19}/></button></div>
      </form><p className="fe-composer-hint">Enter to send · Shift+Enter for a new line · Job execution requires a separate approval</p>
    </div>
  </section>;
}

function PrivateContext({employee,detail,busy,run}:{employee:Employee;detail:ThreadDetail;busy:boolean;run:Props['run']}){
  const [notes,setNotes]=useState(detail.notes.body),[version,setVersion]=useState(detail.notes.version),[dirty,setDirty]=useState(false);
  useEffect(()=>{if(!dirty){setNotes(detail.notes.body);setVersion(detail.notes.version);}},[detail.notes.body,detail.notes.version,dirty]);
  return <details className="co-private-context"><summary>Conversation context and employee settings</summary><p>Only this employee's approved department facts, private notes, bounded conversation history and selected approved artifacts can enter a task. Review the exact packet before authorizing a manual job.</p><form onSubmit={async e=>{e.preventDefault();const saved=await run('notes.save',{employeeId:employee.id,body:notes,expectedVersion:version});if(saved)setDirty(false);}}><label>Private notes<textarea value={notes} disabled={busy||employee.state==='retired'} onChange={e=>{setDirty(true);setNotes(e.target.value);}}/></label><button disabled={busy||!dirty||employee.state==='retired'} type="submit">Save private notes</button></form><small>{employee.state} · {employee.roleId}. Unsaved note edits stay only in this open view.</small>{employee.state!=='retired'&&<form key={employee.id+':'+employee.version} onSubmit={async e=>{e.preventDefault();const values=new FormData(e.currentTarget);await run('employee.update',{id:employee.id,expectedVersion:employee.version,name:String(values.get('name')),responsibility:String(values.get('responsibility'))});}}><label>Name<input name="name" required defaultValue={employee.name}/></label><label>Responsibility<textarea name="responsibility" required defaultValue={employee.responsibility}/></label><button disabled={busy} type="submit">Update employee</button></form>}<div className="co-actions">{employee.state!=='retired'&&<><button disabled={busy} onClick={()=>void run('employee.lifecycle',{id:employee.id,expectedVersion:employee.version,decision:employee.state==='paused'?'resume':'pause'})}>{employee.state==='paused'?'Resume':'Pause'}</button><button disabled={busy} onClick={()=>void run('employee.lifecycle',{id:employee.id,expectedVersion:employee.version,decision:'retire'})}>Retire employee</button></>}</div></details>;
}
