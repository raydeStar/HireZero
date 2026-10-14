import {execFile} from 'node:child_process';
import {createHash} from 'node:crypto';
import {readFile} from 'node:fs/promises';
import path from 'node:path';
import {promisify} from 'node:util';

// Fixed-status diagnostics only: never emit CLI config, account details, credentials, or exception text.
const root=process.env.HIREZERO_DATA_ROOT??path.join(process.env.USERPROFILE,'.hirezero');
const cell=path.join(root,'OpenClaw',createHash('sha256').update('owner-private').digest('hex').slice(0,24));
const runtime=path.join(root,'Runtime');
const node=path.join(runtime,'node','node.exe');
const entry=path.join(runtime,'openclaw','node_modules','openclaw','openclaw.mjs');
const env={...process.env,PATH:path.dirname(node)+path.delimiter+process.env.PATH,OPENCLAW_STATE_DIR:cell,OPENCLAW_CONFIG_PATH:path.join(cell,'openclaw.json')};
const run=promisify(execFile);
try{
  env.OPENCLAW_GATEWAY_TOKEN=(await readFile(path.join(cell,'gateway.token'),'utf8')).trim();
  const {stdout}=await run(node,[entry,'models','list','--all','--provider','openai','--json'],{env,timeout:45000,maxBuffer:2*1024*1024,windowsHide:true});
  const start=stdout.indexOf('{');
  const catalog=JSON.parse(stdout.slice(start));
  console.log(Array.isArray(catalog.models)?'MODEL_CATALOG_COUNT_'+catalog.models.length:'MODEL_CATALOG_SHAPE_UNVERIFIED');
  const model=catalog.models?.find(row=>row.key==='openai/gpt-6-luna');
  console.log(model?.available===true?'LUNA_CATALOG_AVAILABLE':model?'LUNA_CATALOG_PRESENT_ACCESS_UNVERIFIED':'LUNA_CATALOG_UNVERIFIED');
  for(const [name,key] of [['SOL61','openai/gpt-6.1-sol'],['ASTRA','openai/gpt-6-astra']]){
    const row=catalog.models?.find(item=>item.key===key);
    console.log(row?.available===true?name+'_CATALOG_AVAILABLE':row?name+'_CATALOG_PRESENT_ACCESS_UNVERIFIED':name+'_CATALOG_UNVERIFIED');
  }
}catch{console.log('LUNA_CATALOG_UNVERIFIED');}
try{
  const {stdout}=await run(node,[entry,'models','status','--json'],{env,timeout:45000,maxBuffer:2*1024*1024,windowsHide:true});
  const status=JSON.parse(stdout.slice(stdout.indexOf('{')));
  const provider=status.auth?.oauth?.providers?.find(item=>item.provider==='openai');
  console.log(provider?.status==='ok'&&!status.auth?.missingProvidersInUse?.includes('openai')?'OPENAI_OAUTH_READY':'OPENAI_OAUTH_UNVERIFIED');
}catch{console.log('OPENAI_OAUTH_UNVERIFIED');}
try{
  const config=JSON.parse(await readFile(env.OPENCLAW_CONFIG_PATH,'utf8'));
  console.log(config.agents?.defaults?.model?.primary==='openai/gpt-6-luna'&&config.agents?.defaults?.thinkingDefault==='high'?'LUNA_HIGH_CONFIGURED':'LUNA_HIGH_NOT_CONFIGURED');
  await run(node,[entry,'config','validate'],{env,timeout:45000,maxBuffer:1024*1024,windowsHide:true});
  console.log('OPENCLAW_CONFIG_VALID');
}catch{console.log('OPENCLAW_CONFIG_UNVERIFIED');process.exitCode=1;}
