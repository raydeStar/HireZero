import {execFile} from 'node:child_process';
import {createHash} from 'node:crypto';
import {readFile} from 'node:fs/promises';
import path from 'node:path';
import {promisify} from 'node:util';

const root=process.env.HIREZERO_DATA_ROOT??path.join(process.env.USERPROFILE,'.hirezero');
const cell=path.join(root,'OpenClaw',createHash('sha256').update('owner-private').digest('hex').slice(0,24));
const runtime=path.join(root,'Runtime');
const node=path.join(runtime,'node','node.exe');
const entry=path.join(runtime,'openclaw','node_modules','openclaw','openclaw.mjs');
const env={...process.env,PATH:path.dirname(node)+path.delimiter+process.env.PATH,OPENCLAW_STATE_DIR:cell,OPENCLAW_CONFIG_PATH:path.join(cell,'openclaw.json')};
const run=promisify(execFile);
try{
  env.OPENCLAW_GATEWAY_TOKEN=(await readFile(path.join(cell,'gateway.token'),'utf8')).trim();
  await run(node,[entry,'models','set','openai/gpt-6-luna'],{env,timeout:45000,windowsHide:true});
  await run(node,[entry,'config','set','agents.defaults.thinkingDefault','high'],{env,timeout:45000,windowsHide:true});
  const config=JSON.parse(await readFile(env.OPENCLAW_CONFIG_PATH,'utf8'));
  if(config.agents?.defaults?.model?.primary!=='openai/gpt-6-luna'||config.agents?.defaults?.thinkingDefault!=='high')throw new Error();
  console.log('LUNA_HIGH_SAVED');
}catch{console.log('LUNA_HIGH_SAVE_UNVERIFIED');process.exitCode=1;}
