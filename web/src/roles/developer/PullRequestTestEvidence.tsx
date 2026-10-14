import type {FixStatus} from './github';

export interface PullRequestEvidenceView {screenshots:string[];run:FixStatus|null;}
interface PullRequestTestEvidenceProps {workflowId:string;proof:PullRequestEvidenceView;}
function report(text:string|null|undefined):{summary:string;tests:string[];risks:string[];files:string[]}|null{
  if(!text)return null;
  try{
    const value:unknown=JSON.parse(text);if(!value||typeof value!=='object')return null;
    const row=value as Record<string,unknown>;
    function strings(input:unknown):string[]{return Array.isArray(input)?input.filter((item):item is string=>typeof item==='string'):[];}
    return {summary:typeof row.summary==='string'?row.summary:'',tests:strings(row.tests),risks:strings(row.risks),files:strings(row.changedFiles)};
  }catch{return null;}
}
export function PullRequestTestEvidence({workflowId,proof}:PullRequestTestEvidenceProps){
  const run=proof.run?.run,result=report(run?.result);
  return <section aria-label="Test evidence">
    <p><strong>{run?.state||'No worker result available yet'}</strong>{run?.model?' · '+run.model+' '+run.reasoning:''}</p>
    {run?.error&&<p className="fe-alert">{run.error}</p>}
    {!!run?.steps?.length&&<><strong>Latest worker steps</strong><ol className="dev-pr-steps">{run.steps.map((step,index)=><li key={index}>{step}</li>)}</ol></>}
    {run?.questions?.filter(question=>question.state==='open').map(question=><p key={question.id}>{question.prompt}</p>)}
    {result?<><p>{result.summary}</p><strong>Test commands and results</strong><ul>{result.tests.map(value=><li key={value}>{value}</li>)}</ul>
      {result.risks.length>0&&<><strong>Limitations</strong><ul>{result.risks.map(value=><li key={value}>{value}</li>)}</ul></>}
      <details><summary>Changed files</summary><ul>{result.files.map(value=><li key={value}>{value}</li>)}</ul></details>
    </>:run?.result&&<p className="dev-pr-result">{run.result}</p>}
    {proof.screenshots.length===0?<p className="fe-muted">No browser screenshots have been recorded.</p>:proof.screenshots.map(name=><figure key={name}>
      <img className="dev-pr-screenshot" alt={'Fix verification: '+name} src={'/api/github/workflows/'+workflowId+'/screenshots/'+encodeURIComponent(name)}/><figcaption>{name}</figcaption>
    </figure>)}
  </section>;
}
