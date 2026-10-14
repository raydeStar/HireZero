import {useEffect,useState} from 'react';
import {ApiError,api} from '../../shell/api';

type Profile={repository:string;database:string;env:Record<string,string>};
type Status={installed:boolean;intact:boolean;ready:boolean;runner:string;postgres:string;profile:Profile|null};

/** Trusted PostgreSQL tests: HireZero's own disposable-database runner, installed once, turned on per repository. */
export function TrustedPostgresSettings({onForbidden}:{onForbidden:()=>void}){
  const [repository,setRepository]=useState('Example/ExampleWidgetProject'),[status,setStatus]=useState<Status|null>(null);
  const [database,setDatabase]=useState(''),[env,setEnv]=useState(''),[busy,setBusy]=useState(''),[error,setError]=useState('');
  async function load(name=repository){try{const value=await api<Status>('/github/postgres?repository='+encodeURIComponent(name));setStatus(value);setDatabase(value.profile?.database??'app_test');
    setEnv(value.profile?Object.entries(value.profile.env).map(([key,template])=>key+'='+template).join('\n'):'DATABASE_URL={url}\nNODE_ENV=test');setError('');}
    catch(cause){if(cause instanceof ApiError&&cause.status===403)onForbidden();else setError((cause as Error).message);}}
  useEffect(()=>{void load();},[]);// eslint-disable-line react-hooks/exhaustive-deps
  async function run(kind:string,call:()=>Promise<Status>){setBusy(kind);setError('');try{setStatus(await call());if(kind!=='install')await load();}catch(cause){setError((cause as Error).message);}finally{setBusy('');}}
  const pairs=()=>Object.fromEntries(env.split('\n').map(line=>line.trim()).filter(Boolean).map(line=>[line.slice(0,line.indexOf('=')).trim(),line.slice(line.indexOf('=')+1).trim()]));
  return <section className="fe-section fe-stack"><h2>Trusted PostgreSQL tests</h2>
    <p className="fe-muted">Without Docker, database tests run through HireZero's own runner: a disposable PostgreSQL {status?.postgres} on this computer, outside every worktree so Sol can use it but not change it. Only results from this runner count as proof.</p>
    {status&&<p role="status">{status.ready?'✓ Installed and intact.':status.installed&&!status.intact?'The runner file changed; reinstall to trust it again.':'Not installed yet.'}</p>}
    {status&&!status.ready&&<button className="primary" disabled={!!busy} onClick={()=>void run('install',()=>api<Status>('/github/postgres/install','POST',{}))}>{busy==='install'?'Installing PostgreSQL (about 100 MB)…':status.installed?'Reinstall the runner':'Install the runner'}</button>}
    <label>Repository<input value={repository} onChange={event=>setRepository(event.target.value)} onBlur={()=>void load(repository)} placeholder="owner/name"/></label>
    <label>Test database name<input value={database} onChange={event=>setDatabase(event.target.value)} placeholder="app_test"/></label>
    <label>Environment for its tests <span className="fe-muted">one per line, NAME=value; use {'{host} {port} {user} {password} {database} {url} {dataDir}'}</span>
      <textarea rows={8} value={env} onChange={event=>setEnv(event.target.value)} spellCheck={false}/></label>
    <div className="dev-shift-actions">
      <button className="primary" disabled={!!busy} onClick={()=>void run('save',()=>api<Status>('/github/postgres/profile','PUT',{repository,enabled:true,database,env:pairs()}))}>{busy==='save'?'Saving…':status?.profile?'Save profile':'Turn on for this repository'}</button>
      {status?.profile&&<button className="fe-ghost" disabled={!!busy} onClick={()=>void run('off',()=>api<Status>('/github/postgres/profile','PUT',{repository,enabled:false}))}>Turn off</button>}
    </div>
    {status?.profile&&<p className="fe-muted dev-runner-note">On for {status.profile.repository}: Sol is told to run database tests as <code>node "{status.runner}" -- &lt;test command&gt;</code>.</p>}
    {error&&<p className="fe-alert" role="alert">{error}</p>}
  </section>;
}
