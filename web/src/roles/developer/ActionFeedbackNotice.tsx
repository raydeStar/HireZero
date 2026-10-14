import {useSyncExternalStore} from 'react';
import {feedbackFor,subscribeFeedback} from './actionFeedback';
export function useActionFeedback(requestId:string){return useSyncExternalStore(subscribeFeedback,()=>feedbackFor(requestId),()=>feedbackFor(requestId));}
export function ActionFeedbackNotice({requestId,onCheck}:{requestId:string;onCheck?:()=>void}){
  const receipt=useActionFeedback(requestId);if(!receipt)return null;
  return <div className={'dev-pr-note '+(['blocked','failed','unknown'].includes(receipt.state)?'fe-alert':'')} role={['blocked','failed','unknown'].includes(receipt.state)?'alert':'status'}>
    <strong>{receipt.state==='pending'?'Waiting for confirmation':receipt.state==='unknown'?'Outcome unknown':receipt.state==='monitoring'?'Monitoring resumed':receipt.state==='started'?'Work confirmed':receipt.state==='accepted'?'Decision received':receipt.state==='blocked'?'Action blocked':'Action failed'}</strong>
    <p>{receipt.message}</p><small>Diagnostic ID: {receipt.correlationId}</small>{receipt.previousCorrelationId&&<small>Earlier request diagnostic ID: {receipt.previousCorrelationId}</small>}{onCheck&&receipt.state!=='pending'&&<button className="fe-link" type="button" onClick={onCheck}>Check status</button>}
  </div>;
}
